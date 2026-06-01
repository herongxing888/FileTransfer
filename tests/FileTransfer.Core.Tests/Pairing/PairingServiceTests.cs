using FileTransfer.Core.Crypto;
using FileTransfer.Core.Pairing;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core.Tests.Pairing;

[Collection(LoopbackSocketCollection.Name)]
public class PairingServiceTests
{
    [Fact]
    public async Task TwoServices_DiscoverEachOther_AsPeerCandidates()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47980;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47981,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47982,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        PeerCandidate? heardByA = null;
        PeerCandidate? heardByB = null;
        string aFp = Fingerprint.Compute(certA.RawData);
        string bFp = Fingerprint.Compute(certB.RawData);
        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) heardByA = p; };
        b.PeerDiscovered += p => { if (p.Fingerprint == aFp) heardByB = p; };

        await a.StartAsync();
        await b.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((heardByA is null || heardByB is null) && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.NotNull(heardByA);
        Assert.NotNull(heardByB);
        Assert.Equal("B", heardByA!.DeviceName);
        Assert.Equal(47981, heardByB!.TcpPort);
    }

    [Fact]
    public async Task HappyPath_ReachesAwaitingDecision_OnBothSides_WithMatchingCode()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47983;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47984,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47985,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aCandidateTcs = new TaskCompletionSource<(string Code, PeerCandidate Peer)>();
        var bCandidateTcs = new TaskCompletionSource<(string Code, PeerCandidate Peer)>();
        string bFp = Fingerprint.Compute(certB.RawData);
        string aFp = Fingerprint.Compute(certA.RawData);

        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        a.PairingCandidateReady += (code, peer) => aCandidateTcs.TrySetResult((code, peer));
        b.PairingCandidateReady += (code, peer) => bCandidateTcs.TrySetResult((code, peer));

        await a.StartAsync();
        await b.StartAsync();

        var bPeer = await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await a.RequestPairingAsync(bPeer);

        var aResult = await aCandidateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bResult = await bCandidateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(aResult.Code, bResult.Code);
        Assert.Equal("B", aResult.Peer.DeviceName);
        Assert.Equal("A", bResult.Peer.DeviceName);
        Assert.Equal(bFp, aResult.Peer.Fingerprint);
        Assert.Equal(aFp, bResult.Peer.Fingerprint);
        Assert.Equal(PairingState.AwaitingDecision, a.State);
        Assert.Equal(PairingState.AwaitingDecision, b.State);
    }

    [Fact]
    public async Task HappyPath_BothConfirm_RaisesPairingCompleted_OnBothSides()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47986;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47987,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47988,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aCompleted = new TaskCompletionSource<PairingResult>();
        var bCompleted = new TaskCompletionSource<PairingResult>();
        string bFp = Fingerprint.Compute(certB.RawData);
        string aFp = Fingerprint.Compute(certA.RawData);

        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        a.PairingCandidateReady += async (_, _) => await a.ConfirmAsync();
        b.PairingCandidateReady += async (_, _) => await b.ConfirmAsync();
        a.PairingCompleted += r => aCompleted.TrySetResult(r);
        b.PairingCompleted += r => bCompleted.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        var bPeer = await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await a.RequestPairingAsync(bPeer);

        var aRes = await aCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bRes = await bCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(bFp, aRes.PeerFingerprint);
        Assert.Equal("B", aRes.PeerDeviceName);
        Assert.Equal(aFp, bRes.PeerFingerprint);
        Assert.Equal("A", bRes.PeerDeviceName);
        Assert.Equal(PairingState.Completed, a.State);
        Assert.Equal(PairingState.Completed, b.State);
    }

    [Fact]
    public async Task BRejects_BothSidesRaisePairingFailed_WithCorrectReasons()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47989;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47990,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47991,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        var bFailed = new TaskCompletionSource<PairingFailureReason>();
        string bFp = Fingerprint.Compute(certB.RawData);

        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        // A confirms (it will see PeerRejected from B). B rejects.
        a.PairingCandidateReady += async (_, _) => await a.ConfirmAsync();
        b.PairingCandidateReady += async (_, _) => await b.RejectAsync("test reject");
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);
        b.PairingFailed += (r, _) => bFailed.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        await a.RequestPairingAsync(await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        var aReason = await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bReason = await bFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PairingFailureReason.PeerRejected, aReason);
        Assert.Equal(PairingFailureReason.LocallyRejected, bReason);
    }

    [Fact]
    public async Task DecisionTimeout_BothSidesRaisePairingFailed_LocalTimeout()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47992;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47993,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromMilliseconds(200),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47994,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromMilliseconds(200),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        var bFailed = new TaskCompletionSource<PairingFailureReason>();
        string bFp = Fingerprint.Compute(certB.RawData);
        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        // Nobody calls Confirm or Reject — just wait for the timer.
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);
        b.PairingFailed += (r, _) => bFailed.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        await a.RequestPairingAsync(await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(PairingFailureReason.LocalTimeout, await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(PairingFailureReason.LocalTimeout, await bFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PeerDisconnectsDuringAwaitingDecision_FailsWithConnectionLost()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47995;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47996,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47997,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aReady = new TaskCompletionSource();
        var bReady = new TaskCompletionSource();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        string bFp = Fingerprint.Compute(certB.RawData);
        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        a.PairingCandidateReady += (_, _) => aReady.TrySetResult();
        b.PairingCandidateReady += (_, _) => bReady.TrySetResult();
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        await a.RequestPairingAsync(await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(aReady.Task, bReady.Task).WaitAsync(TimeSpan.FromSeconds(5));

        // B unilaterally goes away.
        b.Dispose();

        Assert.Equal(PairingFailureReason.ConnectionLost, await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PeerSendsWrongProtocolVersion_FailsWithProtocolMismatch()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47998;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47999,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });
        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        a.PeerDiscovered += p => { if (p.Fingerprint == Fingerprint.Compute(certB.RawData)) aSeesB.TrySetResult(p); };
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);
        await a.StartAsync();

        // Roll our own "B": broadcast a beacon and run an unpinned TLS listener that sends
        // a HELLO with a bumped version as soon as it receives any frame.
        using var bDiscovery = new FileTransfer.Core.Discovery.DiscoveryService(
            udp, 48000, Fingerprint.Compute(certB.RawData), "B", TimeSpan.FromMilliseconds(100));
        using var bListener = new TransportListener(48000, certB, expectedPeerFingerprint: null);
        bListener.ConnectionAccepted += conn =>
        {
            conn.FrameReceived += (type, payload) =>
            {
                if (type != MessageType.Hello) return;
                var bad = new HelloMessage { DeviceName = "B", ProtocolVersion = PairingService.ProtocolVersion + 1 };
                _ = conn.SendAsync(MessageType.Hello, MessageSerializer.Serialize(bad), CancellationToken.None);
            };
            conn.Start();
        };
        bListener.Start();
        bDiscovery.Start();

        var bPeer = await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await a.RequestPairingAsync(bPeer);

        Assert.Equal(PairingFailureReason.ProtocolMismatch, await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
