using FileTransfer.App.ViewModels;
using FileTransfer.Core;
using FileTransfer.Core.Protocol;

namespace FileTransfer.App.Composition;

public sealed class NodeHost : INodeHost, IDisposable
{
    private readonly Node _node;
    private readonly string _ownFingerprint;

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;

    public ConnectionStatus Status => _node.Status;
    public string PeerName => _node.PeerName;
    public string OwnFingerprint => _ownFingerprint;

    public NodeHost(NodeOptions options, string ownFingerprint)
    {
        _node = new Node(options);
        _ownFingerprint = ownFingerprint;
        _node.StatusChanged += s => StatusChanged?.Invoke(s);
        _node.TextReceived += t => TextReceived?.Invoke(t);
        _node.FileOfferReceived += o => FileOfferReceived?.Invoke(o);
        _node.FileProgress += (id, r, t) => FileProgress?.Invoke(id, r, t);
        _node.FileCompleted += (id, p) => FileCompleted?.Invoke(id, p);
        _node.TransferFailed += (id, r) => TransferFailed?.Invoke(id, r);
    }

    public Task StartAsync() => _node.StartAsync();
    public Task SendTextAsync(string text) => _node.SendTextAsync(text);
    public Task<Guid> SendFileAsync(string path) => _node.SendFileAsync(path);
    public Task CancelTransferAsync(Guid id) => _node.CancelTransferAsync(id);
    public void Stop() => _node.Stop();
    public void Dispose() => _node.Dispose();
}
