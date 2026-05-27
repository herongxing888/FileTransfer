using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Tests.Crypto;

public class FingerprintTests
{
    [Fact]
    public void Compute_IsUppercaseHex_64Chars()
    {
        byte[] certBytes = { 1, 2, 3, 4 };

        string fp = Fingerprint.Compute(certBytes);

        Assert.Equal(64, fp.Length);
        Assert.Matches("^[0-9A-F]+$", fp);
    }

    [Fact]
    public void PairingCode_IsFourDigits()
    {
        string code = Fingerprint.PairingCode("AAAA", "BBBB");

        Assert.Matches("^[0-9]{4}$", code);
    }

    [Fact]
    public void PairingCode_IsOrderIndependent()
    {
        string fromA = Fingerprint.PairingCode("AAAA", "BBBB");
        string fromB = Fingerprint.PairingCode("BBBB", "AAAA");

        Assert.Equal(fromA, fromB);
    }

    [Fact]
    public void PairingCode_DiffersForDifferentPeers()
    {
        Assert.NotEqual(
            Fingerprint.PairingCode("AAAA", "BBBB"),
            Fingerprint.PairingCode("AAAA", "CCCC"));
    }

    [Fact]
    public void LocalInitiates_TrueWhenLocalFingerprintSortsFirst()
    {
        Assert.True(Fingerprint.LocalInitiates(local: "AAAA", peer: "BBBB"));
        Assert.False(Fingerprint.LocalInitiates(local: "BBBB", peer: "AAAA"));
    }
}
