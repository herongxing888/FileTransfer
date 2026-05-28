using FileTransfer.Core.Crypto;
using FileTransfer.Core.Discovery;

namespace FileTransfer.Core.Pairing;

/// First-time pairing orchestrator. Runs UDP discovery on the same magic as DiscoveryService,
/// surfaces every peer as a PeerCandidate (regardless of fingerprint), and — once
/// RequestPairingAsync or an incoming TLS is in play (added in later tasks) — drives the
/// HELLO + 4-digit-code + mutual-confirm handshake. Single active session at a time.
public sealed class PairingService : IDisposable
{
    private readonly PairingServiceOptions _options;
    private readonly string _ownFingerprint;
    private DiscoveryService? _discovery;

    public string OwnFingerprint => _ownFingerprint;
    public PairingState State { get; private set; } = PairingState.Idle;

    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string /*pairingCode*/, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;

    public PairingService(PairingServiceOptions options)
    {
        _options = options;
        _ownFingerprint = Fingerprint.Compute(options.OwnCertificate.RawData);
    }

    public Task StartAsync()
    {
        _discovery = new DiscoveryService(
            _options.UdpPort, _options.TcpPort, _ownFingerprint, _options.DeviceName, _options.AnnounceInterval);
        _discovery.PeerDiscovered += peer =>
            PeerDiscovered?.Invoke(new PeerCandidate(peer.Address, peer.TcpPort, peer.Fingerprint, peer.DeviceName));
        _discovery.Start();
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
    }

    public void Dispose() => Stop();
}
