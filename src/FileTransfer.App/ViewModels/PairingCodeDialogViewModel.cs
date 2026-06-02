using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FileTransfer.App.ViewModels;

public enum PairingDecision { Confirmed, Rejected }

public sealed partial class PairingCodeDialogViewModel : ObservableObject
{
    public string Code { get; }
    public string PeerName { get; }

    [ObservableProperty]
    private PairingDecision? _decision;

    public PairingCodeDialogViewModel(string code, string peerName)
    {
        Code = code;
        PeerName = peerName;
    }

    [RelayCommand]
    private void Confirm() => Decision = PairingDecision.Confirmed;

    [RelayCommand]
    private void Reject() => Decision = PairingDecision.Rejected;
}
