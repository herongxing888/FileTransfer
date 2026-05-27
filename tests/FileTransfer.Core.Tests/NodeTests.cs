using FileTransfer.Core;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transfer;

namespace FileTransfer.Core.Tests;

public class NodeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ft-node-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void HandleFrame_Text_RaisesTextReceived()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        string? got = null;
        router.TextReceived += t => got = t;

        var payload = MessageSerializer.Serialize(new TextMessage { Id = Guid.NewGuid(), Text = "hi there" });
        router.Handle(MessageType.Text, payload);

        Assert.Equal("hi there", got);
    }

    [Fact]
    public void HandleFrame_FileLifecycle_RaisesCompletedWithPath()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        Guid? completedId = null;
        string? completedPath = null;
        router.FileCompleted += (id, path) => { completedId = id; completedPath = path; };

        byte[] data = { 1, 2, 3, 4 };
        var offerId = Guid.NewGuid();
        router.Handle(MessageType.FileOffer, MessageSerializer.Serialize(
            new FileOffer { Id = offerId, Name = "n.bin", Size = data.Length }));
        router.Handle(MessageType.FileChunk, FileChunkCodec.Encode(offerId, data));
        router.Handle(MessageType.FileDone, MessageSerializer.Serialize(
            new FileDone { Id = offerId, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)) }));

        Assert.Equal(offerId, completedId);
        Assert.True(File.Exists(completedPath));
    }

    [Fact]
    public void HandleFrame_FileCancel_RaisesTransferFailed()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        Guid? failedId = null;
        router.TransferFailed += (id, _) => failedId = id;

        var offerId = Guid.NewGuid();
        router.Handle(MessageType.FileOffer, MessageSerializer.Serialize(
            new FileOffer { Id = offerId, Name = "n.bin", Size = 100 }));
        router.Handle(MessageType.FileCancel, MessageSerializer.Serialize(
            new FileCancel { Id = offerId, Reason = "peer cancelled" }));

        Assert.Equal(offerId, failedId);
    }

    [Fact]
    public void HandleFrame_FileChunk_RaisesProgressReachingTotal()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        (Guid Id, long Received, long Total)? last = null;
        router.FileProgress += (id, recv, total) => last = (id, recv, total);

        byte[] data = { 1, 2, 3, 4 };
        var offerId = Guid.NewGuid();
        router.Handle(MessageType.FileOffer, MessageSerializer.Serialize(
            new FileOffer { Id = offerId, Name = "p.bin", Size = data.Length }));
        router.Handle(MessageType.FileChunk, FileChunkCodec.Encode(offerId, data));

        Assert.NotNull(last);
        Assert.Equal(offerId, last!.Value.Id);
        Assert.Equal(4, last.Value.Received);
        Assert.Equal(4, last.Value.Total);
    }

    [Fact]
    public void HandleFrame_FileDoneWithWrongSha_RaisesTransferFailedNotCompleted()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        Guid? failedId = null;
        bool completed = false;
        router.TransferFailed += (id, _) => failedId = id;
        router.FileCompleted += (_, _) => completed = true;

        byte[] data = { 5, 6, 7 };
        var offerId = Guid.NewGuid();
        router.Handle(MessageType.FileOffer, MessageSerializer.Serialize(
            new FileOffer { Id = offerId, Name = "bad.bin", Size = data.Length }));
        router.Handle(MessageType.FileChunk, FileChunkCodec.Encode(offerId, data));
        router.Handle(MessageType.FileDone, MessageSerializer.Serialize(
            new FileDone { Id = offerId, Sha256 = "00000000" })); // deliberately wrong

        Assert.Equal(offerId, failedId);
        Assert.False(completed);
    }

    [Fact]
    public void HandleFrame_MalformedPayload_DoesNotThrow_AndConnectionSurvives()
    {
        var router = new MessageRouter(new FileReceiver(_dir));

        // Malformed Text (invalid UTF-8/JSON) and a FileChunk shorter than the 16-byte GUID
        // header must be swallowed, not thrown — otherwise the receive loop would drop the link.
        Assert.Null(Record.Exception(() => router.Handle(MessageType.Text, new byte[] { 0xFF, 0xFF })));
        Assert.Null(Record.Exception(() => router.Handle(MessageType.FileChunk, new byte[] { 1, 2, 3 })));

        // A well-formed frame after the bad ones is still processed normally.
        string? got = null;
        router.TextReceived += t => got = t;
        router.Handle(MessageType.Text, MessageSerializer.Serialize(new TextMessage { Id = Guid.NewGuid(), Text = "ok" }));
        Assert.Equal("ok", got);
    }
}
