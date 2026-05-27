using FileTransfer.Core.Discovery;

namespace FileTransfer.Core.Tests.Discovery;

public class DiscoveryServiceTests
{
    [Fact]
    public async Task TwoServices_DiscoverEachOther()
    {
        int port = 47900; // dedicated test port
        using var a = new DiscoveryService(
            udpPort: port, tcpPort: 47901, fingerprint: "AAAA", deviceName: "A",
            announceInterval: TimeSpan.FromMilliseconds(100));
        using var b = new DiscoveryService(
            udpPort: port, tcpPort: 47902, fingerprint: "BBBB", deviceName: "B",
            announceInterval: TimeSpan.FromMilliseconds(100));

        PeerInfo? heardByA = null;
        PeerInfo? heardByB = null;
        a.PeerDiscovered += p => { if (p.Fingerprint == "BBBB") heardByA = p; };
        b.PeerDiscovered += p => { if (p.Fingerprint == "AAAA") heardByB = p; };

        a.Start();
        b.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((heardByA is null || heardByB is null) && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.NotNull(heardByA);
        Assert.NotNull(heardByB);
        Assert.Equal("B", heardByA!.DeviceName);
        Assert.Equal(47901, heardByB!.TcpPort);
    }

    [Fact]
    public async Task DoesNotRaiseForOwnAnnouncements()
    {
        int port = 47910;
        using var solo = new DiscoveryService(
            udpPort: port, tcpPort: 47911, fingerprint: "SELF", deviceName: "Solo",
            announceInterval: TimeSpan.FromMilliseconds(100));

        bool heardSelf = false;
        solo.PeerDiscovered += p => { if (p.Fingerprint == "SELF") heardSelf = true; };
        solo.Start();

        await Task.Delay(800);

        Assert.False(heardSelf);
    }
}
