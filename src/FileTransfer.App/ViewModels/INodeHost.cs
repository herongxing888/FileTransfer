using FileTransfer.Core;
using FileTransfer.Core.Protocol;

namespace FileTransfer.App.ViewModels;

/// Narrow surface MainViewModel uses from the Node — testable via FakeNodeHost.
public interface INodeHost
{
    ConnectionStatus Status { get; }
    string PeerName { get; }

    event Action<ConnectionStatus>? StatusChanged;
    event Action<string>? TextReceived;
    event Action<FileOffer>? FileOfferReceived;
    event Action<Guid /*id*/, long /*received*/, long /*total*/>? FileProgress;
    event Action<Guid /*id*/, string /*finalPath*/>? FileCompleted;
    event Action<Guid /*id*/, string /*reason*/>? TransferFailed;

    Task StartAsync();
    Task SendTextAsync(string text);
    Task<Guid> SendFileAsync(string path);
    Task CancelTransferAsync(Guid id);
    void Stop();
}
