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

    /// `expectedPeerFingerprint` null means unpinned: any well-formed server cert is
    /// accepted and its fingerprint is exposed via Connection.PeerFingerprint for the
    /// caller (PairingService) to validate at the application layer.
    public static async Task<Connection> ConnectAsync(
        string host, int port, X509Certificate2 ownCert, string? expectedPeerFingerprint, CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);

        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
            {
                if (cert is null) return false;
                if (expectedPeerFingerprint is null) return true;
                return Fingerprint.Compute(cert.GetRawCertData()) == expectedPeerFingerprint;
            });

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

        string? peerFp = ssl.RemoteCertificate is { } rc
            ? Fingerprint.Compute(rc.GetRawCertData())
            : null;

        var conn = new Connection(
            ssl, HeartbeatInterval, HeartbeatTimeout, peerFingerprint: peerFp);
        conn.Closed += _ => clientCert.Dispose();
        conn.Start();
        return conn;
    }
}
