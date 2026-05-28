using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FileTransfer.Core.Crypto;

public static class Fingerprint
{
    /// SHA256 of the raw certificate bytes, as uppercase hex.
    public static string Compute(byte[] certRawData)
    {
        ArgumentNullException.ThrowIfNull(certRawData);
        return Convert.ToHexString(SHA256.HashData(certRawData));
    }

    /// A 4-digit code derived from both fingerprints. Deterministic and
    /// independent of argument order, so both machines compute the same value.
    /// A man-in-the-middle (with different keys) produces a mismatching code.
    public static string PairingCode(string fingerprintA, string fingerprintB)
    {
        string ordered = string.CompareOrdinal(fingerprintA, fingerprintB) <= 0
            ? fingerprintA + fingerprintB
            : fingerprintB + fingerprintA;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ordered));
        uint value = BinaryPrimitives.ReadUInt32BigEndian(hash);
        return (value % 10000).ToString("D4");
    }

    /// Deterministic tie-breaker for who dials whom when both sides discover
    /// each other at once: the lexicographically smaller fingerprint connects.
    public static bool LocalInitiates(string local, string peer)
        => string.CompareOrdinal(local, peer) < 0;
}
