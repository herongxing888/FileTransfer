using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransfer.App.Services;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;
    private readonly IPairingHost _pairing;

    [ObservableProperty]
    private AppState _state;

    [ObservableProperty]
    private string? _lastError;

    public ObservableCollection<DeviceCandidateViewModel> Devices { get; } = new();

    /// Raised when the peer's HELLO has been exchanged and the user should be shown
    /// the 4-digit code. The View handler pops PairingCodeDialog and calls
    /// RespondToPairingAsync with the user's decision.
    public event Action<string /*code*/, string /*peerName*/>? PairingCodeRequested;

    /// Raised after PairingCompleted has been observed and AppConfig persistence
    /// should happen (Composition Root performs the actual write + Node startup).
    public event Action<PairingResult>? PairingPersisted;

    public MainViewModel(IDispatcher dispatcher, IPairingHost pairing, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _pairing = pairing;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;

        _pairing.PeerDiscovered += peer =>
            _dispatcher.Invoke(() => OnPeerDiscovered(peer));
        _pairing.PairingCandidateReady += (code, peer) =>
            _dispatcher.Invoke(() => OnPairingCandidate(code, peer));
        _pairing.PairingCompleted += result =>
            _dispatcher.Invoke(() => OnPairingCompleted(result));
        _pairing.PairingFailed += (reason, detail) =>
            _dispatcher.Invoke(() => OnPairingFailed(reason, detail));
    }

    public Task StartAsync() => _pairing.StartAsync();

    public Task RespondToPairingAsync(PairingDecision decision) =>
        decision == PairingDecision.Confirmed ? _pairing.ConfirmAsync() : _pairing.RejectAsync();

    private void OnPeerDiscovered(PeerCandidate peer)
    {
        foreach (var d in Devices)
            if (d.Fingerprint == peer.Fingerprint) return;
        Devices.Add(new DeviceCandidateViewModel(peer));
    }

    private void OnPairingCandidate(string code, PeerCandidate peer)
    {
        State = AppState.Pairing;
        PairingCodeRequested?.Invoke(code, peer.DeviceName);
    }

    private void OnPairingCompleted(PairingResult result)
    {
        State = AppState.Offline; // Node connection comes up via StatusChanged later
        Devices.Clear();
        LastError = null;
        PairingPersisted?.Invoke(result);
    }

    private void OnPairingFailed(PairingFailureReason reason, string detail)
    {
        State = AppState.Unpaired;
        LastError = string.IsNullOrEmpty(detail)
            ? reason.ToString()
            : $"{reason}: {detail}";
    }

    [RelayCommand]
    private Task RequestPairing(DeviceCandidateViewModel? candidate)
        => candidate is null ? Task.CompletedTask : _pairing.RequestPairingAsync(candidate.Peer);
}
