using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Discovery;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transfer;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core;

public sealed class NodeOptions
{
    public required string DeviceName { get; init; }
    public required X509Certificate2 OwnCertificate { get; init; }
    public required string PeerFingerprint { get; init; }
    public int UdpPort { get; init; } = 47100;
    public int TcpPort { get; init; } = 47101;
    public required string ReceiveDirectory { get; init; }
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(5);
}

/// Top-level orchestrator the UI binds to. Owns discovery + transport + transfer
/// and exposes high-level events and send methods. One peer connection at a time.
public sealed class Node : IDisposable
{
    private readonly NodeOptions _options;
    private readonly string _ownFingerprint;
    private readonly FileReceiver _receiver;
    private readonly MessageRouter _router;

    private DiscoveryService? _discovery;
    private TransportListener? _listener;
    private Connection? _connection;
    private readonly object _connLock = new();

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public string PeerName { get; private set; } = "";

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;

    public Node(NodeOptions options)
    {
        _options = options;
        _ownFingerprint = Fingerprint.Compute(options.OwnCertificate.RawData);
        _receiver = new FileReceiver(options.ReceiveDirectory);
        _router = new MessageRouter(_receiver);

        _router.TextReceived += t => TextReceived?.Invoke(t);
        _router.FileOfferReceived += o => FileOfferReceived?.Invoke(o);
        _router.FileProgress += (id, r, t) => FileProgress?.Invoke(id, r, t);
        _router.FileCompleted += (id, p) => FileCompleted?.Invoke(id, p);
        _router.TransferFailed += (id, r) => TransferFailed?.Invoke(id, r);
    }

    public Task StartAsync()
    {
        _listener = new TransportListener(_options.TcpPort, _options.OwnCertificate, _options.PeerFingerprint);
        _listener.ConnectionAccepted += AdoptConnection;
        _listener.Start();

        _discovery = new DiscoveryService(
            _options.UdpPort, _options.TcpPort, _ownFingerprint, _options.DeviceName, _options.AnnounceInterval);
        _discovery.PeerDiscovered += OnPeerDiscovered;
        _discovery.Start();

        SetStatus(ConnectionStatus.Offline);
        return Task.CompletedTask;
    }

    private void OnPeerDiscovered(PeerInfo peer)
    {
        if (peer.Fingerprint != _options.PeerFingerprint) return; // only our paired peer
        lock (_connLock) { if (_connection is not null) return; }   // already connected
        if (!Fingerprint.LocalInitiates(_ownFingerprint, peer.Fingerprint)) return; // the other side dials

        _ = DialAsync(peer);
    }

    private async Task DialAsync(PeerInfo peer)
    {
        try
        {
            var conn = await TransportConnector.ConnectAsync(
                peer.Address.ToString(), peer.TcpPort, _options.OwnCertificate, peer.Fingerprint, CancellationToken.None);
            PeerName = peer.DeviceName;
            AdoptConnection(conn);
        }
        catch
        {
            // peer not ready yet — discovery will retry on the next beacon
        }
    }

    private void AdoptConnection(Connection conn)
    {
        lock (_connLock)
        {
            if (_connection is not null) { conn.Dispose(); return; } // keep the first one
            _connection = conn;
        }

        conn.FrameReceived += (type, payload) => _router.Handle(type, payload);
        conn.Closed += _ =>
        {
            lock (_connLock) { if (ReferenceEquals(_connection, conn)) _connection = null; }
            conn.Dispose();
            SetStatus(ConnectionStatus.Offline);
        };

        SetStatus(ConnectionStatus.Online);
    }

    public async Task SendTextAsync(string text)
    {
        var conn = RequireConnection();
        var msg = new TextMessage { Id = Guid.NewGuid(), Text = text };
        await conn.SendAsync(MessageType.Text, MessageSerializer.Serialize(msg), CancellationToken.None);
    }

    public async Task<Guid> SendFileAsync(string path)
    {
        var conn = RequireConnection();
        var sender = new FileSender(conn);
        return await sender.SendAsync(path, progress: null, CancellationToken.None);
    }

    public async Task CancelTransferAsync(Guid id)
    {
        var conn = RequireConnection();
        var cancel = new FileCancel { Id = id, Reason = "cancelled by sender" };
        await conn.SendAsync(MessageType.FileCancel, MessageSerializer.Serialize(cancel), CancellationToken.None);
    }

    private Connection RequireConnection()
    {
        lock (_connLock)
        {
            return _connection ?? throw new InvalidOperationException("Not connected to peer.");
        }
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void Stop()
    {
        _discovery?.Dispose();
        _listener?.Dispose();
        lock (_connLock) { _connection?.Dispose(); _connection = null; }
        SetStatus(ConnectionStatus.Disconnected);
    }

    public void Dispose() => Stop();
}
