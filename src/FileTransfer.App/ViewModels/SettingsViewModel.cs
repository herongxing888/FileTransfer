using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransfer.App.Services;

namespace FileTransfer.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;
    private readonly IAutoStartRegistry _autoStartRegistry;
    private readonly string _executablePath;

    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _receiveDirectory = "";
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _ownFingerprint = "";

    public event Action? UnpairRequested;

    public SettingsViewModel(IFolderPicker folderPicker, IAutoStartRegistry autoStart, string executablePath)
    {
        _folderPicker = folderPicker;
        _autoStartRegistry = autoStart;
        _executablePath = executablePath;
    }

    [RelayCommand]
    private async Task BrowseReceiveDirectory()
    {
        var chosen = await _folderPicker.PickAsync(initialDirectory: ReceiveDirectory);
        if (chosen is not null) ReceiveDirectory = chosen;
    }

    /// Called by the View when Save is confirmed; writes the auto-start registry flag
    /// based on the current property value.
    public void ApplyAutoStart()
    {
        if (AutoStart) _autoStartRegistry.Enable(_executablePath);
        else _autoStartRegistry.Disable();
    }

    [RelayCommand]
    private void Unpair() => UnpairRequested?.Invoke();
}
