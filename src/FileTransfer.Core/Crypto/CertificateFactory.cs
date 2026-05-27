using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FileTransfer.Core.Crypto;

public static class CertificateFactory
{
    public static X509Certificate2 CreateSelfSigned(string subjectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
    }

    /// Export including the private key as a PFX byte blob (no password — the
    /// blob itself is DPAPI-protected by the caller before being persisted).
    public static byte[] ExportPfx(X509Certificate2 cert)
    {
        ArgumentNullException.ThrowIfNull(cert);
        return cert.Export(X509ContentType.Pfx);
    }

    public static X509Certificate2 ImportPfx(byte[] pfx)
    {
        ArgumentNullException.ThrowIfNull(pfx);
        return new X509Certificate2(pfx, (string?)null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }
}
