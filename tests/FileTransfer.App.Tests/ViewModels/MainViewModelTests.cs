using System.Net;
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;
using FileTransfer.App.ViewModels;
using FileTransfer.Core;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.Tests.ViewModels;

public class MainViewModelTests
{
    private static (MainViewModel vm, FakePairingHost host, FakeNodeHost node,
                    FakeClipboard clipboard, FakeFilePicker filePicker,
                    ImmediateDispatcher dispatcher) NewVm(bool paired)
    {
        var dispatcher = new ImmediateDispatcher();
        var pairing = new FakePairingHost();
        var node = new FakeNodeHost();
        var clipboard = new FakeClipboard();
        var filePicker = new FakeFilePicker();
        var vm = new MainViewModel(dispatcher, pairing, node, clipboard, filePicker, isPairedOnBoot: paired);
        return (vm, pairing, node, clipboard, filePicker, dispatcher);
    }

    private static (MainViewModel vm, FakePairingHost host, ImmediateDispatcher dispatcher) NewVmUnpaired()
    {
        var (vm, pairing, _, _, _, dispatcher) = NewVm(paired: false);
        return (vm, pairing, dispatcher);
    }

    [Fact]
    public void State_WhenUnpaired_StartsAsUnpaired()
    {
        var (vm, _, _) = NewVmUnpaired();
        Assert.Equal(AppState.Unpaired, vm.State);
    }

    [Fact]
    public async Task PeerDiscovered_AddsDeviceCandidateToList()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePeerDiscovered(peer);
        Assert.Single(vm.Devices);
        Assert.Equal("Lab-PC", vm.Devices[0].DeviceName);
        Assert.Equal("DEAD", vm.Devices[0].Fingerprint);
    }

    [Fact]
    public async Task PeerDiscovered_TwiceForSameFingerprint_DoesNotDuplicate()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePeerDiscovered(peer);
        host.RaisePeerDiscovered(peer);
        Assert.Single(vm.Devices);
    }

    [Fact]
    public async Task RequestPairingCommand_ForwardsToHost()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePeerDiscovered(peer);
        var candidate = vm.Devices[0];
        await vm.RequestPairingCommand.ExecuteAsync(candidate);
        Assert.Equal("DEAD", host.LastRequestedPeer?.Fingerprint);
    }

    [Fact]
    public async Task PairingCandidateReady_RaisesPairingCodeRequested()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        string? receivedCode = null;
        string? receivedPeer = null;
        vm.PairingCodeRequested += (code, peerName) =>
            { receivedCode = code; receivedPeer = peerName; };
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePairingCandidateReady("4837", peer);
        Assert.Equal("4837", receivedCode);
        Assert.Equal("Lab-PC", receivedPeer);
        Assert.Equal(AppState.Pairing, vm.State);
    }

    [Fact]
    public async Task ConfirmPairing_Confirmed_ForwardsToHost()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        host.RaisePairingCandidateReady("4837", new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC"));
        await vm.RespondToPairingAsync(PairingDecision.Confirmed);
        Assert.Equal(1, host.ConfirmCount);
    }

    [Fact]
    public async Task ConfirmPairing_Rejected_ForwardsToHost()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        host.RaisePairingCandidateReady("4837", new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC"));
        await vm.RespondToPairingAsync(PairingDecision.Rejected);
        Assert.Equal(1, host.RejectCount);
    }

    [Fact]
    public async Task PairingCompleted_PersistsAndSwitchesState()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        bool persistedFired = false;
        string? persistedFingerprint = null;
        vm.PairingPersisted += result =>
            { persistedFired = true; persistedFingerprint = result.PeerFingerprint; };
        host.RaisePairingCompleted(new PairingResult("BEEF", "Lab-PC"));
        Assert.True(persistedFired);
        Assert.Equal("BEEF", persistedFingerprint);
        // After persisting, the state moves to Offline (Node not yet connected).
        Assert.Equal(AppState.Offline, vm.State);
    }

    [Fact]
    public async Task PairingFailed_GoesBackToUnpaired_WithError()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        host.RaisePairingCandidateReady("4837", new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC"));
        Assert.Equal(AppState.Pairing, vm.State);
        host.RaisePairingFailed(PairingFailureReason.PeerRejected, "");
        Assert.Equal(AppState.Unpaired, vm.State);
        Assert.NotNull(vm.LastError);
        Assert.Contains("PeerRejected", vm.LastError);
    }

    [Fact]
    public async Task SendTextCommand_OnPaired_CallsNodeAndAppendsOutgoingBubble()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        vm.InputText = "hello";
        await vm.SendTextCommand.ExecuteAsync(null);
        Assert.Single(node.SentTexts);
        Assert.Equal("hello", node.SentTexts[0]);
        Assert.Single(vm.Messages);
        var msg = Assert.IsType<TextMessageViewModel>(vm.Messages[0]);
        Assert.True(msg.IsOutgoing);
        Assert.Equal("hello", msg.Text);
        Assert.Equal("", vm.InputText);   // cleared after send
    }

    [Fact]
    public async Task SendTextCommand_EmptyInput_DoesNotSend()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        vm.InputText = "   ";
        await vm.SendTextCommand.ExecuteAsync(null);
        Assert.Empty(node.SentTexts);
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public async Task TextReceived_AppendsIncomingBubble()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        node.RaiseTextReceived("hi back");
        Assert.Single(vm.Messages);
        var msg = Assert.IsType<TextMessageViewModel>(vm.Messages[0]);
        Assert.False(msg.IsOutgoing);
        Assert.Equal("hi back", msg.Text);
    }

    [Fact]
    public async Task StatusChanged_Online_SetsAppStateOnline()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        Assert.Equal(AppState.Offline, vm.State);
        node.SetStatus(ConnectionStatus.Online);
        Assert.Equal(AppState.Online, vm.State);
    }

    [Fact]
    public async Task StatusChanged_Offline_FromOnline_GoesBackToOffline()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        node.SetStatus(ConnectionStatus.Online);
        node.SetStatus(ConnectionStatus.Offline);
        Assert.Equal(AppState.Offline, vm.State);
    }

    [Fact]
    public async Task DropFilesCommand_QueuesAllPathsAndSendsSerially()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        node.NextSendFileId = Guid.NewGuid();
        await vm.DropFilesCommand.ExecuteAsync(new[] { @"C:\a.txt", @"C:\b.txt", @"C:\c.txt" });
        // Synchronous fake: all three sends already happened in order.
        Assert.Equal(3, node.SentFiles.Count);
        Assert.Equal(@"C:\a.txt", node.SentFiles[0]);
        Assert.Equal(@"C:\b.txt", node.SentFiles[1]);
        Assert.Equal(@"C:\c.txt", node.SentFiles[2]);
        Assert.Equal(3, vm.Messages.Count);
        Assert.All(vm.Messages, m => Assert.IsType<FileMessageViewModel>(m));
    }

    [Fact]
    public async Task FileProgress_UpdatesMatchingFileMessage()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.NextSendFileId = id;
        await vm.DropFilesCommand.ExecuteAsync(new[] { @"C:\big.bin" });
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        node.RaiseFileProgress(id, 500, 1000);
        Assert.Equal(0.5, fileVm.Progress, 3);
    }

    [Fact]
    public async Task CancelTransferCommand_CallsNode()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.NextSendFileId = id;
        await vm.DropFilesCommand.ExecuteAsync(new[] { @"C:\big.bin" });
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        fileVm.CancelCommand.Execute(null);
        Assert.Single(node.Cancelled);
        Assert.Equal(id, node.Cancelled[0]);
    }

    [Fact]
    public async Task FileOfferReceived_AppendsReceivingBubble()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "incoming.bin", Size = 5000, Mime = "application/octet-stream"
        });
        Assert.Single(vm.Messages);
        var fileVm = Assert.IsType<FileMessageViewModel>(vm.Messages[0]);
        Assert.False(fileVm.IsOutgoing);
        Assert.Equal(FileMessageState.Receiving, fileVm.State);
        Assert.Equal("incoming.bin", fileVm.Name);
    }

    [Fact]
    public async Task FileCompleted_OnReceive_SetsReceivedWithPath()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "incoming.bin", Size = 5000, Mime = "application/octet-stream"
        });
        node.RaiseFileCompleted(id, @"C:\Recv\incoming.bin");
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        Assert.Equal(FileMessageState.Received, fileVm.State);
        Assert.Equal(@"C:\Recv\incoming.bin", fileVm.ResolvedPath);
    }

    [Fact]
    public async Task TransferFailed_OnReceive_SetsFailedWithReason()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "incoming.bin", Size = 5000, Mime = "application/octet-stream"
        });
        node.RaiseTransferFailed(id, "disk full");
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        Assert.Equal(FileMessageState.Failed, fileVm.State);
        Assert.Equal("disk full", fileVm.FailureReason);
    }

    [Fact]
    public async Task PasteImageCommand_WithClipboardImage_EnqueuesAsFile()
    {
        var (vm, _, node, clipboard, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        clipboard.NextResult = @"C:\Temp\screenshot.png";
        await vm.PasteImageCommand.ExecuteAsync(null);
        Assert.Single(node.SentFiles);
        Assert.Equal(@"C:\Temp\screenshot.png", node.SentFiles[0]);
        Assert.Equal(1, clipboard.CallCount);
    }

    [Fact]
    public async Task PasteImageCommand_NoImage_NoOp()
    {
        var (vm, _, node, clipboard, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        clipboard.NextResult = null;
        await vm.PasteImageCommand.ExecuteAsync(null);
        Assert.Empty(node.SentFiles);
    }

    [Fact]
    public async Task ReceivedImageFile_HasIsImageTrue()
    {
        var (vm, _, node, _, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "shot.png", Size = 1000, Mime = "image/png"
        });
        node.RaiseFileCompleted(id, @"C:\Recv\shot.png");
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        Assert.True(fileVm.IsImage);
    }

    [Fact]
    public async Task PickFileCommand_EnqueuesPickedPaths()
    {
        var (vm, _, node, _, picker, _) = NewVm(paired: true);
        await vm.StartAsync();
        picker.NextResult = new[] { @"C:\a.txt", @"C:\b.txt" };
        await vm.PickFileCommand.ExecuteAsync(null);
        Assert.Equal(2, node.SentFiles.Count);
    }
}
