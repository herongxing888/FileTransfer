using System.Security.Cryptography;

namespace FileTransfer.Core.Config;

/// Encrypts secrets with Windows DPAPI scoped to the current user, so the
/// stored private key is unreadable by other accounts on the machine.
public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data)
        => ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
