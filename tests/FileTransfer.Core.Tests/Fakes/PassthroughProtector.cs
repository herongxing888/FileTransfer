using FileTransfer.Core.Config;

namespace FileTransfer.Core.Tests.Fakes;

/// Test double for ISecretProtector that does not actually encrypt, so config
/// tests stay deterministic and platform-independent.
public sealed class PassthroughProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => (byte[])data.Clone();
    public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
}
