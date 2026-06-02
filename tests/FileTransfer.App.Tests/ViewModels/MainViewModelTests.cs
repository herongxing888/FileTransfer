using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class MainViewModelTests
{
    private static MainViewModel NewVm(bool paired)
    {
        var dispatcher = new ImmediateDispatcher();
        return new MainViewModel(dispatcher, isPairedOnBoot: paired);
    }

    [Fact]
    public void State_WhenUnpaired_StartsAsUnpaired()
    {
        var vm = NewVm(paired: false);
        Assert.Equal(AppState.Unpaired, vm.State);
    }

    [Fact]
    public void State_WhenPaired_StartsAsOffline()
    {
        // When AppConfig already has a fingerprint, we boot into the paired-but-not-yet-
        // connected state. The Node will fire StatusChanged(Online) once it accepts/dials.
        var vm = NewVm(paired: true);
        Assert.Equal(AppState.Offline, vm.State);
    }
}
