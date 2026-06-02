using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransfer.App.Services;
using FileTransfer.Core;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;
    private readonly IPairingHost _pairing;
    private readonly INodeHost _node;

    [ObservableProperty]
    private AppState _state;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string _inputText = "";

    public ObservableCollection<DeviceCandidateViewModel> Devices { get; } = new();
    public ObservableCollection<object> Messages { get; } = new();

    public event Action<string /*code*/, string /*peerName*/>? PairingCodeRequested;
    public event Action<PairingResult>? PairingPersisted;

    public MainViewModel(IDispatcher dispatcher, IPairingHost pairing, INodeHost node, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _pairing = pairing;
        _node = node;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;

        _pairing.PeerDiscovered += peer =>
            _dispatcher.Invoke(() => OnPeerDiscovered(peer));
        _pairing.PairingCandidateReady += (code, peer) =>
            _dispatcher.Invoke(() => OnPairingCandidate(code, peer));
        _pairing.PairingCompleted += result =>
            _dispatcher.Invoke(() => OnPairingCompleted(result));
        _pairing.PairingFailed += (reason, detail) =>
            _dispatcher.Invoke(() => OnPairingFailed(reason, detail));

        _node.StatusChanged += s => _dispatcher.Invoke(() => OnStatusChanged(s));
        _node.TextReceived += t => _dispatcher.Invoke(() => OnTextReceived(t));
    }

    public Task StartAsync()
    {
        // The composition root decides which host to actually start based on isPairedOnBoot;
        // here we always invoke both, the fakes/no-ops it injects when unused.
        return Task.WhenAll(_pairing.StartAsync(), _node.StartAsync());
    }

    public Task RespondToPairingAsync(PairingDecision decision) =>
        decision == PairingDecision.Confirmed ? _pairing.ConfirmAsync() : _pairing.RejectAsync();

    [RelayCommand]
    private Task RequestPairing(DeviceCandidateViewModel? candidate)
        => candidate is null ? Task.CompletedTask : _pairing.RequestPairingAsync(candidate.Peer);

    [RelayCommand]
    private async Task SendText()
    {
        var text = InputText;
        if (string.IsNullOrWhiteSpace(text)) return;
        InputText = "";
        Messages.Add(new TextMessageViewModel(text, isOutgoing: true));
        await _node.SendTextAsync(text);
    }

    private void OnPeerDiscovered(PeerCandidate peer)
    {
        foreach (var d in Devices)
            if (d.Fingerprint == peer.Fingerprint) return;
        Devices.Add(new DeviceCandidateViewModel(peer));
    }

    private void OnPairingCandidate(string code, PeerCandidate peer)
    {
        State = AppState.Pairing;
        PairingCodeRequested?.Invoke(code, peer.DeviceName);
    }

    private void OnPairingCompleted(PairingResult result)
    {
        State = AppState.Offline;
        Devices.Clear();
        LastError = null;
        PairingPersisted?.Invoke(result);
    }

    private void OnPairingFailed(PairingFailureReason reason, string detail)
    {
        State = AppState.Unpaired;
        LastError = string.IsNullOrEmpty(detail) ? reason.ToString() : $"{reason}: {detail}";
    }

    private void OnStatusChanged(ConnectionStatus status)
    {
        State = status switch
        {
            ConnectionStatus.Online => AppState.Online,
            ConnectionStatus.Offline => AppState.Offline,
            _ => State,
        };
    }

    private void OnTextReceived(string text)
        => Messages.Add(new TextMessageViewModel(text, isOutgoing: false));
}
