using FileTransfer.App.ViewModels;
using FileTransfer.Core;
using FileTransfer.Core.Protocol;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeNodeHost : INodeHost
{
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Offline;
    public string PeerName { get; set; } = "Peer";

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }
    public List<string> SentTexts { get; } = new();
    public List<string> SentFiles { get; } = new();
    public List<Guid> Cancelled { get; } = new();

    public Task StartAsync() { Started = true; return Task.CompletedTask; }
    public Task SendTextAsync(string text) { SentTexts.Add(text); return Task.CompletedTask; }

    /// Optional override: when non-null, this GUID is returned for the NEXT SendFileAsync only,
    /// then cleared. When null, a fresh GUID is generated for each call. This keeps existing
    /// tests that rely on a specific id working while ensuring multi-file tests get distinct ids.
    public Guid? NextSendFileId { get; set; }
    public List<Guid> SentFileIds { get; } = new();

    public Task<Guid> SendFileAsync(string path)
    {
        SentFiles.Add(path);
        var id = NextSendFileId ?? Guid.NewGuid();
        NextSendFileId = null;
        SentFileIds.Add(id);
        return Task.FromResult(id);
    }

    public Task CancelTransferAsync(Guid id) { Cancelled.Add(id); return Task.CompletedTask; }
    public void Stop() { Stopped = true; }

    public void SetStatus(ConnectionStatus s)
    { Status = s; StatusChanged?.Invoke(s); }
    public void RaiseTextReceived(string t) => TextReceived?.Invoke(t);
    public void RaiseFileOffer(FileOffer o) => FileOfferReceived?.Invoke(o);
    public void RaiseFileProgress(Guid id, long r, long t) => FileProgress?.Invoke(id, r, t);
    public void RaiseFileCompleted(Guid id, string p) => FileCompleted?.Invoke(id, p);
    public void RaiseTransferFailed(Guid id, string r) => TransferFailed?.Invoke(id, r);
}
