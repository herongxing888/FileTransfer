namespace FileTransfer.App.Services;

/// Read/write the per-user "run at logon" registry entry.
/// Hides Microsoft.Win32.Registry behind a narrow interface so ViewModel tests
/// don't need to touch HKCU.
public interface IAutoStartRegistry
{
    bool IsEnabled();
    void Enable(string executablePath);
    void Disable();
}
