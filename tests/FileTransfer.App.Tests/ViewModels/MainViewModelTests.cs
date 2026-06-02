using System.Net;
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.Tests.ViewModels;

public class MainViewModelTests
{
    private static (MainViewModel vm, FakePairingHost host, ImmediateDispatcher dispatcher) NewVmUnpaired()
    {
        var dispatcher = new ImmediateDispatcher();
        var host = new FakePairingHost();
        var vm = new MainViewModel(dispatcher, host, isPairedOnBoot: false);
        return (vm, host, dispatcher);
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
}
