using FileTransfer.Core;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Tests;

public class EndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ft-e2e-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private Node MakeNode(string name, int udpPort, int tcpPort,
        System.Security.Cryptography.X509Certificates.X509Certificate2 own,
        string peerFp)
    {
        string recvDir = Path.Combine(_root, name);
        return new Node(new NodeOptions
        {
            DeviceName = name,
            OwnCertificate = own,
            PeerFingerprint = peerFp,
            UdpPort = udpPort,
            TcpPort = tcpPort,
            ReceiveDirectory = recvDir,
            AnnounceInterval = TimeSpan.FromMilliseconds(150),
        });
    }

    [Fact]
    public async Task TwoNodes_Discover_Connect_ExchangeTextAndFile()
    {
        using var certA = CertificateFactory.CreateSelfSigned("NodeA");
        using var certB = CertificateFactory.CreateSelfSigned("NodeB");
        string fpA = Fingerprint.Compute(certA.RawData);
        string fpB = Fingerprint.Compute(certB.RawData);

        using var a = MakeNode("A", 47800, 47801, certA, fpB);
        using var b = MakeNode("B", 47800, 47802, certB, fpA);

        string? textOnB = null;
        var fileOnB = new TaskCompletionSource<string>();
        b.TextReceived += t => textOnB = t;
        b.FileCompleted += (_, path) => fileOnB.TrySetResult(path);

        await a.StartAsync();
        await b.StartAsync();

        // Wait for both to report Online.
        await WaitFor(() => a.Status == ConnectionStatus.Online && b.Status == ConnectionStatus.Online, seconds: 8);

        await a.SendTextAsync("hello from A");
        await WaitFor(() => textOnB == "hello from A", seconds: 5);
        Assert.Equal("hello from A", textOnB);

        // Send a 500 KB file A -> B.
        string srcPath = Path.Combine(_root, "payload.bin");
        Directory.CreateDirectory(_root);
        byte[] data = new byte[500 * 1024];
        new Random(7).NextBytes(data);
        await File.WriteAllBytesAsync(srcPath, data);

        await a.SendFileAsync(srcPath);
        string receivedPath = await fileOnB.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(data, await File.ReadAllBytesAsync(receivedPath));
    }

    private static async Task WaitFor(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        if (!condition()) throw new TimeoutException("Condition not met in time.");
    }
}
