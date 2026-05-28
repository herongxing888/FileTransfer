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
    public async Task Listener_DoesNotAccept_ClientWithWrongFingerprint()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        using var rogueCert = CertificateFactory.CreateSelfSigned("Rogue");
        string serverFp = Fingerprint.Compute(serverCert.RawData);
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47952;
        // Listener trusts ONLY clientFp; the rogue presents a different cert.
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: clientFp);
        int acceptedCount = 0;
        listener.ConnectionAccepted += _ => Interlocked.Increment(ref acceptedCount);
        listener.Start();

        // The rogue's pin of the server (serverFp) is correct, so the rogue's client-side
        // validation of the server passes. But the listener must reject the rogue's CLIENT
        // certificate. With TLS 1.3 the rogue may still get a (dead) Connection back, so we
        // tolerate either outcome on the client side and assert the real guarantee below.
        Connection? rogueConn = null;
        try
        {
            rogueConn = await TransportConnector.ConnectAsync(
                "127.0.0.1", port, rogueCert, expectedPeerFingerprint: serverFp, CancellationToken.None);
        }
        catch
        {
            // Acceptable: the client may also observe the rejection and throw.
        }

        await Task.Delay(500); // allow any erroneous accept to surface
        // THE SECURITY GUARANTEE: the listener never accepts an untrusted client.
        Assert.Equal(0, acceptedCount);
        rogueConn?.Dispose();
    }

    [Fact]
    public async Task UnpinnedListener_AcceptsAnyClient_AndPopulatesPeerFingerprint()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        string serverFp = Fingerprint.Compute(serverCert.RawData);
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47960;
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: null);

        var serverConnTask = new TaskCompletionSource<Connection>();
        listener.ConnectionAccepted += c => serverConnTask.TrySetResult(c);
        listener.Start();

        using var clientConn = await TransportConnector.ConnectAsync(
            "127.0.0.1", port, clientCert, expectedPeerFingerprint: serverFp, CancellationToken.None);

        var serverConn = await serverConnTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(clientFp, serverConn.PeerFingerprint);
    }
}
