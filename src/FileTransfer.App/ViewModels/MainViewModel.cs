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

    public ObservableCollection<DeviceCandidateViewModel> Devices { get; } = new();

    public MainViewModel(IDispatcher dispatcher, IPairingHost pairing, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _pairing = pairing;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;

        _pairing.PeerDiscovered += peer => _dispatcher.Invoke(() => OnPeerDiscovered(peer));
    }

    public Task StartAsync() => _pairing.StartAsync();

    private void OnPeerDiscovered(PeerCandidate peer)
    {
        foreach (var d in Devices)
            if (d.Fingerprint == peer.Fingerprint) return;
        Devices.Add(new DeviceCandidateViewModel(peer));
    }

    [RelayCommand]
    private Task RequestPairing(DeviceCandidateViewModel? candidate)
        => candidate is null ? Task.CompletedTask : _pairing.RequestPairingAsync(candidate.Peer);
}
