namespace FileTransfer.Core.Pairing;

public enum PairingFailureReason
{
    LocallyRejected,
    PeerRejected,
    LocalTimeout,
    TlsHandshakeFailed,
    ConnectionLost,
    ProtocolMismatch,
}
