using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Transport;

public static class TransportConnector
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(30);

    public static async Task<Connection> ConnectAsync(
        string host, int port, X509Certificate2 ownCert, string expectedPeerFingerprint, CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);

        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && Fingerprint.Compute(cert.GetRawCertData()) == expectedPeerFingerprint);

        // Round-trip through PFX so Windows SChannel gets a persisted key handle for
        // client-certificate authentication (EphemeralKeySet keys are not supported by SChannel).
        var clientCert = EnsurePersistedKey(ownCert);
        bool clientCertIsOwned = !ReferenceEquals(clientCert, ownCert);

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "filetransfer",
            ClientCertificates = new X509CertificateCollection { clientCert },
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        try
        {
            await ssl.AuthenticateAsClientAsync(options, ct);
        }
        catch
        {
            ssl.Dispose();
            tcp.Dispose();
            if (clientCertIsOwned) clientCert.Dispose();
            throw;
        }

        var conn = new Connection(ssl, HeartbeatInterval, HeartbeatTimeout);
        conn.Start();
        return conn;
    }

    /// Round-trips a cert through PFX export so Windows SChannel gets a persisted
    /// key handle. Certs created with EphemeralKeySet (e.g. from RSA.Create()) fail
    /// client-certificate authentication on Windows without this step.
    private static X509Certificate2 EnsurePersistedKey(X509Certificate2 cert)
    {
        if (!cert.HasPrivateKey) return cert;
        try
        {
            byte[] pfx = cert.Export(X509ContentType.Pfx);
            // EphemeralKeySet intentionally omitted — Windows SChannel requires a persisted key handle.
            return new X509Certificate2(pfx, (string?)null,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
        }
        catch
        {
            return cert; // fall back; SChannel will surface a clearer error
        }
    }
}
