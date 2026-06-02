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
}
