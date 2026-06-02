using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

public sealed class DeviceCandidateViewModel
{
    public PeerCandidate Peer { get; }
    public string DeviceName => Peer.DeviceName;
    public string Fingerprint => Peer.Fingerprint;
    public DeviceCandidateViewModel(PeerCandidate peer) => Peer = peer;
}
