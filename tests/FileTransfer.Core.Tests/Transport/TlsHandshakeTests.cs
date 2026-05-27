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

    [Fact]
    public async Task Listener_Rejects_WhenClientFingerprintDoesNotMatchPin()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        using var rogueCert = CertificateFactory.CreateSelfSigned("Rogue");
        string serverFp = Fingerprint.Compute(serverCert.RawData);
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47952;
        // Listener trusts only clientFp; the rogue connects with a different cert.
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: clientFp);
        bool accepted = false;
        listener.ConnectionAccepted += _ => accepted = true;
        listener.Start();

        // The rogue's pin of the server is correct, so the client side trusts the server;
        // but the listener rejects the rogue's client cert, tearing down the handshake.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            TransportConnector.ConnectAsync(
                "127.0.0.1", port, rogueCert,
                expectedPeerFingerprint: serverFp,
                CancellationToken.None));

        await Task.Delay(300); // give the listener a chance to (not) fire ConnectionAccepted
        Assert.False(accepted);
    }
}
