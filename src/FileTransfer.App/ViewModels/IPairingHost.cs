using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

/// Narrow surface MainViewModel needs from PairingService — exists so tests can inject
/// a fake without spinning a real LAN socket. WpfPairingHost (added in a later App-only
/// integration task) wraps a real PairingService instance.
public interface IPairingHost
{
    event Action<PeerCandidate>? PeerDiscovered;
    event Action<string /*code*/, PeerCandidate>? PairingCandidateReady;
    event Action<PairingResult>? PairingCompleted;
    event Action<PairingFailureReason, string>? PairingFailed;

    Task StartAsync();
    Task RequestPairingAsync(PeerCandidate peer);
    Task ConfirmAsync();
    Task RejectAsync(string reason = "");
}
