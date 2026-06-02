using FileTransfer.App.ViewModels;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakePairingHost : IPairingHost
{
    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;

    public bool Started { get; private set; }
    public PeerCandidate? LastRequestedPeer { get; private set; }
    public int ConfirmCount { get; private set; }
    public int RejectCount { get; private set; }
    public string LastRejectReason { get; private set; } = "";

    public Task StartAsync() { Started = true; return Task.CompletedTask; }

    public Task RequestPairingAsync(PeerCandidate peer)
    { LastRequestedPeer = peer; return Task.CompletedTask; }

    public Task ConfirmAsync() { ConfirmCount++; return Task.CompletedTask; }

    public Task RejectAsync(string reason = "")
    { RejectCount++; LastRejectReason = reason; return Task.CompletedTask; }

    public void RaisePeerDiscovered(PeerCandidate p) => PeerDiscovered?.Invoke(p);
    public void RaisePairingCandidateReady(string code, PeerCandidate p)
        => PairingCandidateReady?.Invoke(code, p);
    public void RaisePairingCompleted(PairingResult r) => PairingCompleted?.Invoke(r);
    public void RaisePairingFailed(PairingFailureReason r, string msg)
        => PairingFailed?.Invoke(r, msg);
}
