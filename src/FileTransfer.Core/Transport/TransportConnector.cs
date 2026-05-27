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

    /// Dials a peer over TCP+TLS, pinning the peer's certificate by fingerprint.
    /// NOTE: With TLS 1.3 the client handshake can complete before the server finishes
    /// validating the client certificate. If the server REJECTS this client (fingerprint
    /// mismatch), ConnectAsync may still return a Connection that the server has already
    /// dropped. The server side remains protected (it never raises ConnectionAccepted for an
    /// untrusted client). Confirming the peer accepted us is the job of the application-level
    /// HELLO exchange (wired in Node); the heartbeat is the backstop that closes a dead link.
    public static async Task<Connection> ConnectAsync(
        string host, int port, X509Certificate2 ownCert, string expectedPeerFingerprint, CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);

        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && Fingerprint.Compute(cert.GetRawCertData()) == expectedPeerFingerprint);

        // SChannel needs a non-ephemeral key for client-cert auth; this cert's key
        // is deleted when the cert is disposed (we dispose it when the connection closes).
        var clientCert = CertificateFactory.MakeTlsReady(ownCert);

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
            clientCert.Dispose();
            throw;
        }

        var conn = new Connection(ssl, HeartbeatInterval, HeartbeatTimeout);
        conn.Closed += _ => clientCert.Dispose(); // delete the temp TLS key once the connection ends
        conn.Start();
        return conn;
    }
}
