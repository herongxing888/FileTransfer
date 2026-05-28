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

    [Fact]
    public void Compute_KnownInput_IsStableSha256()
    {
        byte[] certBytes = { 1, 2, 3, 4 };
        Assert.Equal("9F64A747E1B97F131FABB6B447296C9B6F0201E79FB3C5356E6C77E89B6A806A", Fingerprint.Compute(certBytes));
    }

    [Fact]
    public void LocalInitiates_ExactlyOneSideInitiates()
    {
        Assert.True(
            Fingerprint.LocalInitiates("AAAA", "BBBB") != Fingerprint.LocalInitiates("BBBB", "AAAA"));
    }
}
