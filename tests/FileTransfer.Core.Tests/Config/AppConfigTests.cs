using FileTransfer.Core.Config;
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Tests.Fakes;

namespace FileTransfer.Core.Tests.Config;

public class AppConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ft-cfg-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RestoresAllFields()
    {
        var protector = new PassthroughProtector();
        using var cert = CertificateFactory.CreateSelfSigned("ConfigTest");
        string path = Path.Combine(_dir, "config.json");

        var config = new AppConfig
        {
            DeviceName = "MyLaptop",
            ReceiveDirectory = @"C:\Recv",
            AutoStart = true,
            PeerFingerprint = "DEADBEEF",
            PeerDeviceName = "MyDesktop",
        };
        config.SetCertificate(cert, protector);

        config.Save(path, protector);
        var loaded = AppConfig.Load(path, protector)!;

        Assert.Equal("MyLaptop", loaded.DeviceName);
        Assert.Equal(@"C:\Recv", loaded.ReceiveDirectory);
        Assert.True(loaded.AutoStart);
        Assert.Equal("DEADBEEF", loaded.PeerFingerprint);
        Assert.Equal("MyDesktop", loaded.PeerDeviceName);

        using var restoredCert = loaded.GetCertificate(protector);
        Assert.True(restoredCert.HasPrivateKey);
        Assert.Equal(Fingerprint.Compute(cert.RawData), Fingerprint.Compute(restoredCert.RawData));
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var loaded = AppConfig.Load(Path.Combine(_dir, "nope.json"), new PassthroughProtector());

        Assert.Null(loaded);
    }

    [Fact]
    public void IsPaired_FalseUntilPeerFingerprintSet()
    {
        var config = new AppConfig();
        Assert.False(config.IsPaired);

        config.PeerFingerprint = "ABC";
        Assert.True(config.IsPaired);
    }

    [Fact]
    public void GetCertificate_ThrowsWhenNoCertStored()
    {
        var config = new AppConfig();
        Assert.Throws<InvalidOperationException>(() => config.GetCertificate(new PassthroughProtector()));
    }
}
