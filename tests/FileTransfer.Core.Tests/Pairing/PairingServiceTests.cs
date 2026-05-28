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
}
