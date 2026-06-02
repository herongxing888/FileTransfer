using FileTransfer.App.ViewModels;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.Composition;

/// IPairingHost that owns a real PairingService.
public sealed class PairingServiceHost : IPairingHost, IDisposable
{
    private readonly PairingService _svc;

    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;

    public PairingServiceHost(PairingServiceOptions options)
    {
        _svc = new PairingService(options);
        _svc.PeerDiscovered += p => PeerDiscovered?.Invoke(p);
        _svc.PairingCandidateReady += (code, p) => PairingCandidateReady?.Invoke(code, p);
        _svc.PairingCompleted += r => PairingCompleted?.Invoke(r);
        _svc.PairingFailed += (reason, msg) => PairingFailed?.Invoke(reason, msg);
    }

    public string OwnFingerprint => _svc.OwnFingerprint;
    public Task StartAsync() => _svc.StartAsync();
    public Task RequestPairingAsync(PeerCandidate peer) => _svc.RequestPairingAsync(peer);
    public Task ConfirmAsync() => _svc.ConfirmAsync();
    public Task RejectAsync(string reason = "") => _svc.RejectAsync(reason);
    public void Dispose() => _svc.Dispose();
}
