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
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    /// Round-trips a cert through PFX export so Windows SChannel gets a persisted
    /// key handle. Certs created with EphemeralKeySet (e.g. from RSA.Create()) fail
    /// AuthenticateAsServerAsync on Windows without this step.
    private static X509Certificate2 EnsurePersistedKey(X509Certificate2 cert)
    {
        if (!cert.HasPrivateKey) return cert;
        try
        {
            // If SChannel can already access the key this export/import is a no-op in practice.
            byte[] pfx = cert.Export(X509ContentType.Pfx);
            // Use UserKeySet | PersistKeySet so Windows SChannel gets a persisted
            // CNG key handle — required for AuthenticateAsServerAsync on Windows.
            // EphemeralKeySet is intentionally NOT used here.
            return new X509Certificate2(pfx, (string?)null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
        }
        catch
        {
            return cert; // fall back to original; SChannel will fail with a clearer error
        }
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

        var serverCert = EnsurePersistedKey(_ownCert);
        bool serverCertIsOwned = !ReferenceEquals(serverCert, _ownCert);
        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCert,
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
            if (serverCertIsOwned) serverCert.Dispose();
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
    }
}
