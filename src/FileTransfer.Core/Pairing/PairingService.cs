using FileTransfer.Core.Crypto;
using FileTransfer.Core.Discovery;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core.Pairing;

public sealed class PairingService : IDisposable
{
    internal const int ProtocolVersion = 1;

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
    private CancellationTokenSource? _dialCts;
    // True when _activeConnection is our own outgoing dial (not an accepted incoming).
    // Used by OnActiveConnectionClosed to distinguish the tiebreaker race from a real drop.
    private bool _activeConnectionIsOutgoing;
    // Cancels the deferred "wait for incoming after outgoing died" timeout.
    private CancellationTokenSource? _recoveryTimeoutCts;

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
        CancellationToken dialCt;
        lock (_stateLock)
        {
            // If an incoming connection already kicked off the session (extreme race where
            // the incoming TLS arrived before the user's "Pair" click), let the incoming path
            // continue — nothing for us to dial.
            if (_state == PairingState.Negotiating && _activeConnection is not null) return;
            if (_state != PairingState.Idle)
                throw new InvalidOperationException($"Cannot request pairing in state {_state}.");
            _state = PairingState.Negotiating;
            _activePeer = peer;
            _dialCts = new CancellationTokenSource();
            dialCt = _dialCts.Token;
        }

        Connection conn;
        try
        {
            conn = await TransportConnector.ConnectAsync(
                peer.Address.ToString(), peer.TcpPort, _options.OwnCertificate,
                expectedPeerFingerprint: null, dialCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Race tiebreaker cancelled us in favour of the incoming connection. The incoming
            // path is driving the session forward; we exit silently.
            return;
        }
        catch (Exception ex)
        {
            Fail(PairingFailureReason.TlsHandshakeFailed, ex.Message);
            return;
        }

        bool adopt;
        lock (_stateLock)
        {
            // If an incoming connection beat us to adoption while we were still dialing,
            // our outgoing is the race-loser. Drop it. No handlers wired yet → nothing fires.
            // Also drop if the tiebreaker already cancelled us — the incoming path will adopt
            // its connection momentarily even if _activeConnection isn't set yet.
            adopt = _activeConnection is null
                    && _state == PairingState.Negotiating
                    && (_dialCts is null || !_dialCts.IsCancellationRequested);
        }
        if (!adopt) { conn.Dispose(); return; }
        await AdoptConnectionAsync(conn, isOutgoing: true).ConfigureAwait(false);
    }

    private void OnIncomingConnection(Connection conn)
    {
        bool adopt;
        lock (_stateLock)
        {
            // Past Negotiating? We're busy with an active session — drop a third connection.
            if (_state == PairingState.AwaitingDecision ||
                _state == PairingState.Completed ||
                _state == PairingState.Failed)
            {
                conn.Dispose(); return;
            }

            if (_state == PairingState.Idle)
            {
                // Pure incoming, no outgoing race. Adopt directly.
                _state = PairingState.Negotiating;
                _activePeer = new PeerCandidate(
                    System.Net.IPAddress.Loopback, 0, conn.PeerFingerprint ?? "", "");
                adopt = true;
            }
            else
            {
                // _state == Negotiating: an outgoing dial is in flight. Apply the deterministic
                // race rule — smaller fingerprint keeps its OUTGOING dial; larger keeps INCOMING.
                string peerFp = conn.PeerFingerprint ?? "";
                bool localIsSmaller = string.CompareOrdinal(_ownFingerprint, peerFp) < 0;
                if (localIsSmaller)
                {
                    // We keep our outgoing dial; drop this incoming.
                    conn.Dispose(); return;
                }
                // We are larger — cancel our outgoing dial and adopt this incoming.
                // If the dial has already completed, the cancel is a no-op and
                // RequestPairingAsync's continuation will drop the loser via the
                // `_activeConnection is null` check (it will be non-null by then).
                _dialCts?.Cancel();
                adopt = true;
            }
        }
        if (adopt)
        {
            // Listener callback path is necessarily fire-and-forget — but we don't want
            // unhandled exceptions from inside AdoptConnectionAsync to escape onto
            // TaskScheduler.UnobservedTaskException unnoticed. Route any escape through
            // Fail so the session terminates cleanly. AdoptConnectionAsync's own SendAsync
            // catch already handles the expected case; this is a backstop for the rest.
            _ = AdoptConnectionAsync(conn, isOutgoing: false).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception is { } ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"PairingService: AdoptConnectionAsync(incoming) faulted: {ex}");
                    Fail(PairingFailureReason.ConnectionLost, ex.GetBaseException().Message);
                }
            }, TaskScheduler.Default);
        }
    }

    private async Task AdoptConnectionAsync(Connection conn, bool isOutgoing = false)
    {
        lock (_stateLock)
        {
            _activeConnection = conn;
            _activeConnectionIsOutgoing = isOutgoing;
            // If an incoming connection is taking over, cancel any pending recovery timeout
            // that was started when the outgoing died in OnActiveConnectionClosed.
            if (!isOutgoing)
            {
                _recoveryTimeoutCts?.Cancel();
                _recoveryTimeoutCts = null;
            }
        }

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

        // Send HELLO BEFORE starting the receive loop. On loopback the peer's HELLO is
        // often already buffered when Start() launches ReceiveLoopAsync; that loop would
        // dispatch FrameReceived synchronously on the calling thread, the auto-confirm
        // handler would run ConfirmAsync, and PairingConfirm would hit the wire BEFORE our
        // HELLO. The peer would then drop PairingConfirm because its state is still
        // Negotiating, and pairing deadlocks. Awaiting SendAsync here guarantees HELLO is
        // in the kernel send buffer before any reactive frame can overtake it.
        var hello = new HelloMessage { DeviceName = _options.DeviceName, ProtocolVersion = ProtocolVersion };
        try
        {
            await conn.SendAsync(MessageType.Hello, MessageSerializer.Serialize(hello), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Surface a send failure through the same path Connection.Closed uses, so the
            // race-tiebreaker recovery (when this is an outgoing dial whose peer just dropped
            // us in favour of the incoming) is honoured — OnActiveConnectionClosed inspects
            // _activeConnectionIsOutgoing and either Fails immediately or defers by 3 s for
            // the incoming connection to take over. Without this redirection, the catch
            // would Fail synchronously and short-circuit the deferred-Fail mechanism.
            System.Diagnostics.Debug.WriteLine(
                $"PairingService: HELLO send failed on adoption ({ex.GetType().Name}: {ex.Message}); " +
                "routing through OnActiveConnectionClosed for recovery.");
            OnActiveConnectionClosed();
            return;
        }

        conn.Start();
    }

    private void OnActiveConnectionClosed()
    {
        // Special case for the simultaneous-dial tiebreaker race: the "larger FP" side may
        // have mistakenly adopted its own outgoing dial before OnIncomingConnection had a
        // chance to cancel it. The peer (smaller FP) immediately drops the incoming (our
        // outgoing), killing the connection. The correct incoming connection is arriving in
        // parallel via the listener. We defer the failure by a short window so that
        // OnIncomingConnection can overwrite _activeConnection before we give up.
        bool isRaceWindow;
        CancellationTokenSource? recoveryCts = null;
        lock (_stateLock)
        {
            isRaceWindow = _state == PairingState.Negotiating && _activeConnectionIsOutgoing;
            if (isRaceWindow)
            {
                _recoveryTimeoutCts?.Cancel();
                _recoveryTimeoutCts = recoveryCts = new CancellationTokenSource();
            }
        }

        if (!isRaceWindow)
        {
            // Treat a drop as ConnectionLost only if we're still mid-pairing. Fail is idempotent,
            // so a Closed firing after Completed or Failed is a harmless no-op.
            Fail(PairingFailureReason.ConnectionLost, "peer disconnected");
            return;
        }

        // Give the incoming connection a short window to arrive and take over.
        // If it does, OnIncomingConnection will cancel recoveryCts before we fail.
        _ = Task.Delay(TimeSpan.FromSeconds(3), recoveryCts!.Token).ContinueWith(t =>
        {
            recoveryCts.Dispose();
            if (t.IsCanceled) return; // incoming arrived, recovery succeeded
            Fail(PairingFailureReason.ConnectionLost, "peer disconnected (no incoming after outgoing died)");
        }, TaskScheduler.Default);
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
        catch
        {
            // A peer that sends a HELLO we cannot decode is not someone we can pair with.
            // Fail immediately rather than sitting in Negotiating until the decision timeout.
            Fail(PairingFailureReason.ConnectionLost, "malformed HELLO");
            return;
        }

        if (hello.ProtocolVersion != ProtocolVersion)
        {
            Fail(PairingFailureReason.ProtocolMismatch, $"peer version={hello.ProtocolVersion}");
            return;
        }

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

        await conn.SendAsync(MessageType.PairingConfirm, ReadOnlyMemory<byte>.Empty, CancellationToken.None)
            .ConfigureAwait(false);
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
            try { await conn.SendAsync(MessageType.PairingReject, ReadOnlyMemory<byte>.Empty, CancellationToken.None)
                              .ConfigureAwait(false); }
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
            _dialCts?.Cancel();
            _dialCts = null;
            _recoveryTimeoutCts?.Cancel();
            _recoveryTimeoutCts = null;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
    }

    public void Dispose() => Stop();
}
