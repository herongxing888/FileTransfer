using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Transport;

public sealed class TransportListener : IDisposable
{
    private readonly int _port;
    private readonly X509Certificate2 _ownCert;
    private readonly string _expectedPeerFingerprint;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private X509Certificate2? _tlsCert;

    /// Raised once per accepted, fully-handshaken peer connection.
    public event Action<Connection>? ConnectionAccepted;

    public TransportListener(int port, X509Certificate2 ownCert, string expectedPeerFingerprint)
    {
        _port = port;
        _ownCert = ownCert;
        _expectedPeerFingerprint = expectedPeerFingerprint;
    }

    public void Start()
    {
        if (_tlsCert is not null)
            throw new InvalidOperationException("Listener is already started.");
        _tlsCert = CertificateFactory.MakeTlsReady(_ownCert);
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = HandshakeAsync(tcp, ct); // handle each peer without blocking the accept loop
        }
    }

    private async Task HandshakeAsync(TcpClient tcp, CancellationToken ct)
    {
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && Fingerprint.Compute(cert.GetRawCertData()) == _expectedPeerFingerprint);

        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = _tlsCert,
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        try
        {
            await ssl.AuthenticateAsServerAsync(options, ct);
        }
        catch
        {
            ssl.Dispose();
            tcp.Dispose();
            return; // rejected peer — drop silently
        }

        var conn = new Connection(ssl, TransportConnector.HeartbeatInterval, TransportConnector.HeartbeatTimeout);
        conn.Start();
        ConnectionAccepted?.Invoke(conn);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
        _tlsCert?.Dispose();
    }
}
