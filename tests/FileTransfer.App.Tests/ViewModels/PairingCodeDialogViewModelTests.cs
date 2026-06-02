using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class PairingCodeDialogViewModelTests
{
    [Fact]
    public void Constructor_ExposesCodeAndPeerName()
    {
        var vm = new PairingCodeDialogViewModel("4837", "Desktop-XYZ");
        Assert.Equal("4837", vm.Code);
        Assert.Equal("Desktop-XYZ", vm.PeerName);
        Assert.Null(vm.Decision);
    }

    [Fact]
    public void ConfirmCommand_SetsDecisionConfirmed()
    {
        var vm = new PairingCodeDialogViewModel("4837", "Desktop-XYZ");
        vm.ConfirmCommand.Execute(null);
        Assert.Equal(PairingDecision.Confirmed, vm.Decision);
    }

    [Fact]
    public void RejectCommand_SetsDecisionRejected()
    {
        var vm = new PairingCodeDialogViewModel("4837", "Desktop-XYZ");
        vm.RejectCommand.Execute(null);
        Assert.Equal(PairingDecision.Rejected, vm.Decision);
    }
}
