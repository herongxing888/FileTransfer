using FileTransfer.Core.Crypto;
using FileTransfer.Core.Discovery;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core.Pairing;

public sealed class PairingService : IDisposable
{
    private const int ProtocolVersion = 1;

    private readonly PairingServiceOptions _options;
    private readonly string _ownFingerprint;

    private DiscoveryService? _discovery;
    private TransportListener? _listener;

    // All session state below is touched only under _stateLock.
    private readonly object _stateLock = new();
    private PairingState _state = PairingState.Idle;
    private Connection? _activeConnection;
    private PeerCandidate? _activePeer;

    public string OwnFingerprint => _ownFingerprint;
    public PairingState State { get { lock (_stateLock) return _state; } }

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
        _listener = new TransportListener(_options.TcpPort, _options.OwnCertificate, expectedPeerFingerprint: null);
        _listener.ConnectionAccepted += OnIncomingConnection;
        _listener.Start();

        _discovery = new DiscoveryService(
            _options.UdpPort, _options.TcpPort, _ownFingerprint, _options.DeviceName, _options.AnnounceInterval);
        _discovery.PeerDiscovered += peer =>
            PeerDiscovered?.Invoke(new PeerCandidate(peer.Address, peer.TcpPort, peer.Fingerprint, peer.DeviceName));
        _discovery.Start();
        return Task.CompletedTask;
    }

    public async Task RequestPairingAsync(PeerCandidate peer)
    {
        lock (_stateLock)
        {
            if (_state != PairingState.Idle)
                throw new InvalidOperationException($"Cannot request pairing in state {_state}.");
            _state = PairingState.Negotiating;
            _activePeer = peer;
        }

        Connection conn;
        try
        {
            conn = await TransportConnector.ConnectAsync(
                peer.Address.ToString(), peer.TcpPort, _options.OwnCertificate,
                expectedPeerFingerprint: null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            lock (_stateLock) { _state = PairingState.Failed; _activePeer = null; }
            PairingFailed?.Invoke(PairingFailureReason.TlsHandshakeFailed, ex.Message);
            return;
        }

        AdoptConnection(conn);
    }

    private void OnIncomingConnection(Connection conn)
    {
        lock (_stateLock)
        {
            if (_state != PairingState.Idle) { conn.Dispose(); return; }
            _state = PairingState.Negotiating;
            // Peer device name not yet known — filled in once we receive HELLO.
            _activePeer = new PeerCandidate(System.Net.IPAddress.Loopback, 0, conn.PeerFingerprint ?? "", "");
        }
        AdoptConnection(conn);
    }

    private void AdoptConnection(Connection conn)
    {
        lock (_stateLock) { _activeConnection = conn; }

        // The lambdas capture `conn` so we can ignore late events from a connection that the
        // race tiebreaker (Task 14) may later replace. Wired this way from day one so later
        // tasks don't have to rewrite event handlers.
        conn.FrameReceived += (type, payload) =>
        {
            lock (_stateLock) { if (!ReferenceEquals(_activeConnection, conn)) return; }
            OnFrameReceived(type, payload);
        };
        conn.Closed += _ =>
        {
            lock (_stateLock) { if (!ReferenceEquals(_activeConnection, conn)) return; }
            OnActiveConnectionClosed();
        };

        var hello = new HelloMessage { DeviceName = _options.DeviceName, ProtocolVersion = ProtocolVersion };
        _ = conn.SendAsync(MessageType.Hello, MessageSerializer.Serialize(hello), CancellationToken.None);
    }

    // No-op until Task 11 wires ConnectionLost handling.
    private void OnActiveConnectionClosed() { }

    private void OnFrameReceived(MessageType type, byte[] payload)
    {
        if (type == MessageType.Hello) { HandleHello(payload); return; }
        // Other message types handled in later tasks.
    }

    private void HandleHello(byte[] payload)
    {
        HelloMessage hello;
        try { hello = MessageSerializer.Deserialize<HelloMessage>(payload); }
        catch { return; } // malformed HELLO is handled as ConnectionLost in a later task

        PeerCandidate finalPeer;
        string code;
        lock (_stateLock)
        {
            if (_state != PairingState.Negotiating || _activeConnection is null || _activePeer is null) return;
            string peerFp = _activeConnection.PeerFingerprint ?? "";
            finalPeer = _activePeer with { Fingerprint = peerFp, DeviceName = hello.DeviceName };
            _activePeer = finalPeer;
            _state = PairingState.AwaitingDecision;
            code = Fingerprint.PairingCode(_ownFingerprint, peerFp);
        }

        PairingCandidateReady?.Invoke(code, finalPeer);
    }

    public Task ConfirmAsync() => throw new NotImplementedException("Added in a later task.");
    public Task RejectAsync(string reason = "") => throw new NotImplementedException("Added in a later task.");

    public void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
        _listener?.Dispose();
        _listener = null;
        lock (_stateLock)
        {
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
    }

    public void Dispose() => Stop();
}
