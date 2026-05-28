using FileTransfer.Core.Crypto;
using FileTransfer.Core.Pairing;

namespace FileTransfer.Core.Tests.Pairing;

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
}
