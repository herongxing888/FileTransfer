using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class FileMessageViewModelTests
{
    [Fact]
    public void Constructor_Outgoing_StartsAtSending_With0Progress()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), name: "doc.pdf", size: 2_400_000,
            mime: "application/pdf", isOutgoing: true);
        Assert.Equal(FileMessageState.Sending, vm.State);
        Assert.Equal(0.0, vm.Progress);
        Assert.True(vm.IsOutgoing);
    }

    [Fact]
    public void Constructor_Incoming_StartsAtReceiving()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), name: "x.png", size: 100,
            mime: "image/png", isOutgoing: false);
        Assert.Equal(FileMessageState.Receiving, vm.State);
    }

    [Fact]
    public void UpdateProgress_SetsRatioCorrectly()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a", 1000, "application/octet-stream", isOutgoing: true);
        vm.UpdateProgress(received: 250, total: 1000);
        Assert.Equal(0.25, vm.Progress, 3);
    }

    [Fact]
    public void MarkSent_Outgoing_TransitionsToSent()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a", 1000, "application/octet-stream", isOutgoing: true);
        vm.MarkSent();
        Assert.Equal(FileMessageState.Sent, vm.State);
        Assert.Equal(1.0, vm.Progress);
    }

    [Fact]
    public void MarkReceived_SetsStateAndResolvedPath()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a.png", 1000, "image/png", isOutgoing: false);
        vm.MarkReceived(@"C:\Recv\a.png");
        Assert.Equal(FileMessageState.Received, vm.State);
        Assert.Equal(@"C:\Recv\a.png", vm.ResolvedPath);
        Assert.True(vm.IsImage);
    }

    [Fact]
    public void MarkFailed_SetsStateAndReason()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a", 1000, "application/octet-stream", isOutgoing: true);
        vm.MarkFailed("disk full");
        Assert.Equal(FileMessageState.Failed, vm.State);
        Assert.Equal("disk full", vm.FailureReason);
    }
}
