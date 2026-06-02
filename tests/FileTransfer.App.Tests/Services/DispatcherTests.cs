using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;

namespace FileTransfer.App.Tests.Services;

public class DispatcherTests
{
    [Fact]
    public void ImmediateDispatcher_Invoke_RunsActionSynchronously()
    {
        IDispatcher d = new ImmediateDispatcher();
        bool ran = false;
        d.Invoke(() => ran = true);
        Assert.True(ran);
    }

    [Fact]
    public async Task ImmediateDispatcher_InvokeAsync_AwaitsWorkInline()
    {
        IDispatcher d = new ImmediateDispatcher();
        int value = 0;
        await d.InvokeAsync(async () => { await Task.Yield(); value = 42; });
        Assert.Equal(42, value);
    }
}
