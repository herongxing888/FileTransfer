using System.Security.Cryptography.X509Certificates;

namespace FileTransfer.Core.Pairing;

public sealed class PairingServiceOptions
{
    public required string DeviceName { get; init; }
    public required X509Certificate2 OwnCertificate { get; init; }
    public int UdpPort { get; init; } = 47100;
    public int TcpPort { get; init; } = 47101;
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan DecisionTimeout { get; init; } = TimeSpan.FromMinutes(2);
}
