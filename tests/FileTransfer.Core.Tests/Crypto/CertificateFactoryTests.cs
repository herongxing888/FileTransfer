using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Tests.Crypto;

public class CertificateFactoryTests
{
    [Fact]
    public void CreateSelfSigned_ProducesCertWithPrivateKey()
    {
        using var cert = CertificateFactory.CreateSelfSigned("FileTransfer-TestMachine");

        Assert.True(cert.HasPrivateKey);
        Assert.Contains("FileTransfer-TestMachine", cert.Subject);
    }

    [Fact]
    public void ExportAndImportPfx_PreservesFingerprintAndPrivateKey()
    {
        using var original = CertificateFactory.CreateSelfSigned("RoundTrip");
        string originalFp = Fingerprint.Compute(original.RawData);

        byte[] pfx = CertificateFactory.ExportPfx(original);
        using var imported = CertificateFactory.ImportPfx(pfx);

        Assert.True(imported.HasPrivateKey);
        Assert.Equal(originalFp, Fingerprint.Compute(imported.RawData));
    }
}
