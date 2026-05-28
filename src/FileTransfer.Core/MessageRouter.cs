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
    // Only touched on the single Connection receive-loop thread, so a plain Dictionary is safe.
    private readonly Dictionary<Guid, (long Received, long Total)> _progress = new();

    public MessageRouter(FileReceiver receiver) => _receiver = receiver;

    public void Handle(MessageType type, byte[] payload)
    {
        // A failure handling one inbound frame must never tear down the whole
        // connection. Per-transfer failures are surfaced as TransferFailed inside
        // Dispatch; an undecodable/foreign frame is dropped to keep the link alive.
        try
        {
            Dispatch(type, payload);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MessageRouter dropped a bad {type} frame: {ex.Message}");
        }
    }

    private void Dispatch(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.Text:
                TextReceived?.Invoke(MessageSerializer.Deserialize<TextMessage>(payload).Text);
                break;

            case MessageType.FileOffer:
            {
                var offer = MessageSerializer.Deserialize<FileOffer>(payload);
                try
                {
                    _receiver.Begin(offer);
                }
                catch (Exception ex)
                {
                    TransferFailed?.Invoke(offer.Id, ex.Message);
                    break;
                }
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
                try
                {
                    _receiver.WriteChunk(id, data);
                }
                catch (Exception ex)
                {
                    // e.g. disk full — fail this transfer, clean up, keep the connection alive.
                    _receiver.Cancel(id);
                    _progress.Remove(id);
                    TransferFailed?.Invoke(id, ex.Message);
                    break;
                }
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
                    _progress.Remove(done.Id);
                    TransferFailed?.Invoke(done.Id, ex.Message);
                    break;
                }
                _progress.Remove(done.Id);
                FileCompleted?.Invoke(done.Id, path);
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
