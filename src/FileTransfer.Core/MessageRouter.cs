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

    // Per-transfer progress. Added on FileOffer, removed on FileDone/FileCancel. An entry
    // for a transfer whose peer vanished is cleaned up when the Node disposes this router.
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
                // A chunk for a transfer we don't have active (late/duplicate after the
                // transfer completed or was cancelled) is ignored, not treated as fatal.
                if (!_progress.TryGetValue(id, out var p))
                    break;
                _receiver.WriteChunk(id, data);
                p.Received += data.Length;
                _progress[id] = p;
                FileProgress?.Invoke(id, p.Received, p.Total);
                break;
            }

            case MessageType.FileDone:
            {
                var done = MessageSerializer.Deserialize<FileDone>(payload);
                string path;
                try
                {
                    path = _receiver.Complete(done.Id, done.Sha256);
                }
                catch (Exception ex)
                {
                    // Any failure finishing the file (checksum mismatch, IO/permission
                    // error, unknown id) surfaces as a per-transfer failure, not a crash.
                    _progress.Remove(done.Id);
                    TransferFailed?.Invoke(done.Id, ex.Message);
                    break;
                }
                _progress.Remove(done.Id);
                FileCompleted?.Invoke(done.Id, path); // outside try: subscriber bugs propagate, not mislabeled
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
