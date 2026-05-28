using System.Net;

namespace FileTransfer.Core.Discovery;

public sealed record PeerInfo(IPAddress Address, int TcpPort, string Fingerprint, string DeviceName);
