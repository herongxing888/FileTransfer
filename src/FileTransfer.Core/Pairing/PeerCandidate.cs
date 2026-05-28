using System.Net;

namespace FileTransfer.Core.Pairing;

public sealed record PeerCandidate(IPAddress Address, int TcpPort, string Fingerprint, string DeviceName);
