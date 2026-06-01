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
    private bool _ourConfirmSent;
    private bool _peerConfirmReceived;
    private CancellationTokenSource? _decisionTimeoutCts;

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

        // Transport hands us a not-yet-started Connection. Start the receive loop only
        // after handlers are wired, otherwise the peer's HELLO can arrive before we
        // subscribed to FrameReceived and the frame is silently dropped.
        conn.Start();

        var hello = new HelloMessage { DeviceName = _options.DeviceName, ProtocolVersion = ProtocolVersion };
        _ = conn.SendAsync(MessageType.Hello, MessageSerializer.Serialize(hello), CancellationToken.None);
    }

    private void OnActiveConnectionClosed()
    {
        // Treat a drop as ConnectionLost only if we're still mid-pairing. Fail is idempotent,
        // so a Closed firing after Completed or Failed is a harmless no-op.
        Fail(PairingFailureReason.ConnectionLost, "peer disconnected");
    }

    private void OnFrameReceived(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.Hello: HandleHello(payload); break;
            case MessageType.PairingConfirm: HandlePeerConfirm(); break;
            case MessageType.PairingReject: HandlePeerReject(); break;
        }
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
            ArmDecisionTimeout();
        }
        PairingCandidateReady?.Invoke(code, finalPeer);
    }

    // Must be called inside _stateLock.
    private void ArmDecisionTimeout()
    {
        _decisionTimeoutCts = new CancellationTokenSource();
        var ct = _decisionTimeoutCts.Token;
        _ = Task.Delay(_options.DecisionTimeout, ct).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Fail(PairingFailureReason.LocalTimeout, "decision timeout");
        }, TaskScheduler.Default);
    }

    public async Task ConfirmAsync()
    {
        Connection conn;
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision)
                throw new InvalidOperationException($"Cannot confirm in state {_state}.");
            if (_ourConfirmSent) return;
            _ourConfirmSent = true;
            conn = _activeConnection ?? throw new InvalidOperationException("No active connection.");
        }

        await conn.SendAsync(MessageType.PairingConfirm, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        TryComplete();
    }

    private void HandlePeerConfirm()
    {
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision || _peerConfirmReceived) return;
            _peerConfirmReceived = true;
        }
        TryComplete();
    }

    private void TryComplete()
    {
        PairingResult? result = null;
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision) return;
            if (!_ourConfirmSent || !_peerConfirmReceived) return;
            _state = PairingState.Completed;
            _decisionTimeoutCts?.Cancel();
            _decisionTimeoutCts = null;
            var peer = _activePeer!;
            result = new PairingResult(peer.Fingerprint, peer.DeviceName);
        }
        PairingCompleted?.Invoke(result);
    }

    public async Task RejectAsync(string reason = "")
    {
        Connection? conn;
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision)
                throw new InvalidOperationException($"Cannot reject in state {_state}.");
            conn = _activeConnection;
        }

        if (conn is not null)
        {
            try { await conn.SendAsync(MessageType.PairingReject, ReadOnlyMemory<byte>.Empty, CancellationToken.None); }
            catch { /* peer may have already disconnected; we still fail locally */ }
        }

        Fail(PairingFailureReason.LocallyRejected, reason);
    }

    private void HandlePeerReject()
    {
        Fail(PairingFailureReason.PeerRejected, "");
    }

    private void Fail(PairingFailureReason reason, string detail)
    {
        bool raise;
        lock (_stateLock)
        {
            // Idempotent: only the first failure wins.
            if (_state == PairingState.Failed || _state == PairingState.Completed) return;
            raise = _state == PairingState.Negotiating || _state == PairingState.AwaitingDecision;
            _state = PairingState.Failed;
            _decisionTimeoutCts?.Cancel();
            _decisionTimeoutCts = null;
            // We intentionally do NOT dispose _activeConnection here. If we just sent a final
            // frame (REJECT, CONFIRM) and disposed immediately, the local TCP close can race
            // ahead of the in-flight bytes and the peer would observe ConnectionLost instead
            // of the intended reason. The connection is torn down later by Stop()/Dispose();
            // any peer-initiated close arrives via OnActiveConnectionClosed which calls Fail
            // again, which is a no-op once state is already Failed.
        }
        if (raise) PairingFailed?.Invoke(reason, detail);
    }

    public void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
        _listener?.Dispose();
        _listener = null;
        lock (_stateLock)
        {
            _decisionTimeoutCts?.Cancel();
            _decisionTimeoutCts = null;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
    }

    public void Dispose() => Stop();
}
