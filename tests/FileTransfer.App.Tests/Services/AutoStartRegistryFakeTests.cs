using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;

namespace FileTransfer.App.Tests.Services;

public class AutoStartRegistryFakeTests
{
    [Fact]
    public void Enable_SetsBothFlagAndPath()
    {
        var r = new FakeAutoStartRegistry();
        Assert.False(r.IsEnabled());
        r.Enable(@"C:\app.exe");
        Assert.True(r.IsEnabled());
        Assert.Equal(@"C:\app.exe", r.EnabledPath);
    }

    [Fact]
    public void Disable_ClearsBothFlagAndPath()
    {
        var r = new FakeAutoStartRegistry { Enabled = true };
        r.Enable(@"C:\app.exe");
        r.Disable();
        Assert.False(r.IsEnabled());
        Assert.Null(r.EnabledPath);
    }
}
