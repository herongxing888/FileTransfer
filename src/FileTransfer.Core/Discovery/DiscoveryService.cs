using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace FileTransfer.Core.Discovery;

public sealed class DiscoveryService : IDisposable
{
    private const string Magic = "FT1"; // protocol marker to ignore foreign UDP traffic

    private readonly int _udpPort;
    private readonly int _tcpPort;
    private readonly string _fingerprint;
    private readonly string _deviceName;
    private readonly TimeSpan _announceInterval;

    private UdpClient? _listener;
    private UdpClient? _sender;
    private CancellationTokenSource? _cts;

    public event Action<PeerInfo>? PeerDiscovered;

    public DiscoveryService(int udpPort, int tcpPort, string fingerprint, string deviceName, TimeSpan announceInterval)
    {
        _udpPort = udpPort;
        _tcpPort = tcpPort;
        _fingerprint = fingerprint;
        _deviceName = deviceName;
        _announceInterval = announceInterval;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();

        _listener = new UdpClient();
        _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));

        _sender = new UdpClient { EnableBroadcast = true };

        _ = ListenLoopAsync(_cts.Token);
        _ = AnnounceLoopAsync(_cts.Token);
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        byte[] beacon = Encode();
        var targets = new[]
        {
            new IPEndPoint(IPAddress.Broadcast, _udpPort),
            new IPEndPoint(IPAddress.Loopback, _udpPort),
        };

        while (!ct.IsCancellationRequested)
        {
            foreach (var target in targets)
            {
                try { await _sender!.SendAsync(beacon, beacon.Length, target); }
                catch (SocketException) { /* interface down / unreachable — ignore */ }
            }
            try { await Task.Delay(_announceInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _listener!.ReceiveAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }

            var peer = TryDecode(result);
            if (peer is not null && peer.Fingerprint != _fingerprint)
                PeerDiscovered?.Invoke(peer);
        }
    }

    private byte[] Encode()
    {
        var beacon = new Beacon { Magic = Magic, Fingerprint = _fingerprint, DeviceName = _deviceName, TcpPort = _tcpPort };
        return JsonSerializer.SerializeToUtf8Bytes(beacon);
    }

    private PeerInfo? TryDecode(UdpReceiveResult result)
    {
        try
        {
            var beacon = JsonSerializer.Deserialize<Beacon>(result.Buffer);
            if (beacon is null || beacon.Magic != Magic) return null;
            return new PeerInfo(result.RemoteEndPoint.Address, beacon.TcpPort, beacon.Fingerprint, beacon.DeviceName);
        }
        catch (JsonException) { return null; }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Dispose();
        _sender?.Dispose();
        _cts?.Dispose();
    }

    private sealed class Beacon
    {
        public string Magic { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public int TcpPort { get; set; }
    }
}
