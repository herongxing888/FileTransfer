using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeAutoStartRegistry : IAutoStartRegistry
{
    public bool Enabled { get; set; }
    public string? EnabledPath { get; private set; }

    public bool IsEnabled() => Enabled;
    public void Enable(string executablePath) { Enabled = true; EnabledPath = executablePath; }
    public void Disable() { Enabled = false; EnabledPath = null; }
}
