using FileTransfer.Core.Crypto;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core.Tests.Transport;

public class TlsHandshakeTests
{
    [Fact]
    public async Task ConnectorAndListener_CompleteHandshake_WhenFingerprintMatches()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        string serverFp = Fingerprint.Compute(serverCert.RawData);
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47950;
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: clientFp);

        var serverConnTask = new TaskCompletionSource<Connection>();
        listener.ConnectionAccepted += c => serverConnTask.TrySetResult(c);
        listener.Start();

        using var clientConn = await TransportConnector.ConnectAsync(
            "127.0.0.1", port, clientCert, expectedPeerFingerprint: serverFp, CancellationToken.None);

        var serverConn = await serverConnTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(clientConn);
        Assert.NotNull(serverConn);
    }

    [Fact]
    public async Task Connector_Rejects_WhenServerFingerprintDoesNotMatchPin()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47951;
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: clientFp);
        listener.Start();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            TransportConnector.ConnectAsync(
                "127.0.0.1", port, clientCert,
                expectedPeerFingerprint: "0000000000000000000000000000000000000000000000000000000000000000",
                CancellationToken.None));
    }
}
