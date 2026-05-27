using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transfer;

namespace FileTransfer.Core;

/// Pure message-routing brain: turns inbound frames into high-level events and
/// drives the FileReceiver. Has no sockets, so it is unit-testable in isolation.
public sealed class MessageRouter
{
    private readonly FileReceiver _receiver;

    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress; // id, received, total
    public event Action<Guid, string>? FileCompleted;     // id, final path
    public event Action<Guid, string>? TransferFailed;    // id, reason

    private readonly Dictionary<Guid, (long Received, long Total)> _progress = new();

    public MessageRouter(FileReceiver receiver) => _receiver = receiver;

    public void Handle(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.Text:
                TextReceived?.Invoke(MessageSerializer.Deserialize<TextMessage>(payload).Text);
                break;

            case MessageType.FileOffer:
            {
                var offer = MessageSerializer.Deserialize<FileOffer>(payload);
                _receiver.Begin(offer);
                _progress[offer.Id] = (0, offer.Size);
                FileOfferReceived?.Invoke(offer);
                break;
            }

            case MessageType.FileChunk:
            {
                var (id, data) = FileChunkCodec.Decode(payload);
                _receiver.WriteChunk(id, data);
                if (_progress.TryGetValue(id, out var p))
                {
                    p.Received += data.Length;
                    _progress[id] = p;
                    FileProgress?.Invoke(id, p.Received, p.Total);
                }
                break;
            }

            case MessageType.FileDone:
            {
                var done = MessageSerializer.Deserialize<FileDone>(payload);
                try
                {
                    string path = _receiver.Complete(done.Id, done.Sha256);
                    _progress.Remove(done.Id);
                    FileCompleted?.Invoke(done.Id, path);
                }
                catch (Exception ex)
                {
                    _progress.Remove(done.Id);
                    TransferFailed?.Invoke(done.Id, ex.Message);
                }
                break;
            }

            case MessageType.FileCancel:
            {
                var cancel = MessageSerializer.Deserialize<FileCancel>(payload);
                _receiver.Cancel(cancel.Id);
                _progress.Remove(cancel.Id);
                TransferFailed?.Invoke(cancel.Id, string.IsNullOrEmpty(cancel.Reason) ? "cancelled" : cancel.Reason);
                break;
            }
        }
    }
}
