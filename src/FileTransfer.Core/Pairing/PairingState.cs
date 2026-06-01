namespace FileTransfer.Core.Pairing;

public enum PairingState
{
    Idle,
    Negotiating,
    AwaitingDecision,
    Completed,
    Failed,
}
