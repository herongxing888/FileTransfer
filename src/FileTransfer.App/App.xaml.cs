using System.Windows;
using FileTransfer.App.Composition;
using FileTransfer.App.Services;
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Config;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App;

public partial class App : Application
{
    private BootArtifacts? _boot;
    private MainViewModel? _mainVm;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            ISecretProtector protector = new DpapiProtector();
            _boot = BootSequence.Build(protector);

            var dispatcher = new WpfDispatcher(Dispatcher);
            var pairing = _boot.PairingHost ?? new NullPairingHost();
            var node = _boot.NodeHost ?? new NullNodeHost();
            var clipboard = new WpfClipboard();
            var filePicker = new WpfFilePicker();

            _mainVm = new MainViewModel(dispatcher, pairing, node, clipboard, filePicker, _boot.IsPaired);
            _mainVm.PairingCodeRequested += (code, peerName) => ShowPairingDialog(code, peerName);
            _mainVm.PairingPersisted += result => OnPairingPersisted(result);
            _mainVm.SettingsRequested += () => ShowSettingsDialog();

            var window = new MainWindow { DataContext = _mainVm };
            MainWindow = window;
            window.Show();
            await _mainVm.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup failed:\n{ex.Message}", "FileTransfer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowPairingDialog(string code, string peerName)
    {
        var vm = new PairingCodeDialogViewModel(code, peerName);
        var dialog = new Views.PairingCodeDialog { DataContext = vm, Owner = MainWindow };
        dialog.ShowDialog();
        var decision = vm.Decision ?? PairingDecision.Rejected;
        _ = _mainVm!.RespondToPairingAsync(decision);
    }

    private void ShowSettingsDialog()
    {
        if (_boot is null || _mainVm is null) return;
        var folderPicker = new WpfFolderPicker();
        var autoStart = new WpfAutoStartRegistry();
        var exePath = Environment.ProcessPath ?? "FileTransfer.App.exe";
        var vm = new SettingsViewModel(folderPicker, autoStart, exePath)
        {
            DeviceName = _boot.Config.DeviceName,
            ReceiveDirectory = _boot.Config.ReceiveDirectory,
            AutoStart = _boot.Config.AutoStart,
            OwnFingerprint = (_boot.PairingHost as Composition.PairingServiceHost)?.OwnFingerprint
                          ?? (_boot.NodeHost as Composition.NodeHost)?.OwnFingerprint
                          ?? "",
        };
        var dialog = new Views.SettingsDialog { DataContext = vm, Owner = MainWindow };
        bool? result = dialog.ShowDialog();
        if (result == true)
        {
            _boot.Config.DeviceName = vm.DeviceName;
            _boot.Config.ReceiveDirectory = vm.ReceiveDirectory;
            _boot.Config.AutoStart = vm.AutoStart;
            _boot.Config.Save(_boot.ConfigPath, _boot.Protector);
        }
    }

    private void OnPairingPersisted(PairingResult result)
    {
        // Persist to config and restart hosts to switch from pairing → node.
        var protector = _boot!.Protector;
        _boot.Config.PeerFingerprint = result.PeerFingerprint;
        _boot.Config.PeerDeviceName = result.PeerDeviceName;
        _boot.Config.Save(_boot.ConfigPath, protector);
        // Tear down pairing host; build node host. For v1 we rebuild MainViewModel.
        MessageBox.Show($"Paired with {result.PeerDeviceName}. Please restart the app to start chatting.",
            "FileTransfer", MessageBoxButton.OK, MessageBoxImage.Information);
        // Future improvement: hot-swap hosts without a restart. v1 prompts user.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_boot?.PairingHost as IDisposable)?.Dispose();
        (_boot?.NodeHost as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}

/// No-op fallback host used when the alternate mode isn't active (e.g., NullNodeHost when
/// unpaired). Lets MainViewModel keep its single constructor.
file sealed class NullPairingHost : IPairingHost
{
#pragma warning disable CS0067
    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;
#pragma warning restore CS0067
    public Task StartAsync() => Task.CompletedTask;
    public Task RequestPairingAsync(PeerCandidate peer) => Task.CompletedTask;
    public Task ConfirmAsync() => Task.CompletedTask;
    public Task RejectAsync(string reason = "") => Task.CompletedTask;
}

file sealed class NullNodeHost : INodeHost
{
    public FileTransfer.Core.ConnectionStatus Status => FileTransfer.Core.ConnectionStatus.Offline;
    public string PeerName => "";
#pragma warning disable CS0067
    public event Action<FileTransfer.Core.ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileTransfer.Core.Protocol.FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;
#pragma warning restore CS0067
    public Task StartAsync() => Task.CompletedTask;
    public Task SendTextAsync(string text) => Task.CompletedTask;
    public Task<Guid> SendFileAsync(string path) => Task.FromResult(Guid.Empty);
    public Task CancelTransferAsync(Guid id) => Task.CompletedTask;
    public void Stop() { }
}
