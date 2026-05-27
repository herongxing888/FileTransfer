using FileTransfer.Core.Protocol;
using FileTransfer.Core.Tests.Fakes;
using FileTransfer.Core.Transfer;

namespace FileTransfer.Core.Tests.Transfer;

public class FileSenderTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "ft-send-" + Guid.NewGuid() + ".bin");

    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    [Fact]
    public async Task SendsOffer_ThenChunks_ThenDone()
    {
        // 600 KB of data => with 256 KB chunks => 3 chunks
        byte[] data = new byte[600 * 1024];
        new Random(1).NextBytes(data);
        await File.WriteAllBytesAsync(_file, data);

        var sink = new FakeFrameSink();
        var sender = new FileSender(sink, chunkSize: 256 * 1024);

        var id = await sender.SendAsync(_file, progress: null, CancellationToken.None);

        var frames = sink.Frames.ToArray();
        Assert.Equal(MessageType.FileOffer, frames[0].Type);
        Assert.Equal(MessageType.FileChunk, frames[1].Type);
        Assert.Equal(MessageType.FileChunk, frames[2].Type);
        Assert.Equal(MessageType.FileChunk, frames[3].Type);
        Assert.Equal(MessageType.FileDone, frames[4].Type); // offer + 3 chunks + done = 5 frames

        // Every chunk payload carries the transfer id in its first 16 bytes.
        var (chunkId, _) = FileChunkCodec.Decode(frames[1].Payload);
        Assert.Equal(id, chunkId);

        // The offer announces the right size and name.
        var offer = MessageSerializer.Deserialize<FileOffer>(frames[0].Payload);
        Assert.Equal(data.Length, offer.Size);
        Assert.Equal(Path.GetFileName(_file), offer.Name);
        Assert.Equal(id, offer.Id);

        var done = MessageSerializer.Deserialize<FileDone>(frames[4].Payload);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)), done.Sha256);
    }

    [Fact]
    public async Task EmptyFile_SendsOfferAndDone_WithEmptySha()
    {
        await File.WriteAllBytesAsync(_file, Array.Empty<byte>());
        var sink = new FakeFrameSink();

        await new FileSender(sink).SendAsync(_file, progress: null, CancellationToken.None);

        var frames = sink.Frames.ToArray();
        Assert.Equal(2, frames.Length);
        Assert.Equal(MessageType.FileOffer, frames[0].Type);
        Assert.Equal(MessageType.FileDone, frames[1].Type);

        var done = MessageSerializer.Deserialize<FileDone>(frames[1].Payload);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Array.Empty<byte>())),
            done.Sha256);
    }

    [Fact]
    public async Task ReportsProgress_ReachingFullSize()
    {
        byte[] data = new byte[300 * 1024];
        await File.WriteAllBytesAsync(_file, data);
        long lastReported = 0;

        var sender = new FileSender(new FakeFrameSink(), chunkSize: 256 * 1024);
        await sender.SendAsync(_file, progress: sent => lastReported = sent, CancellationToken.None);

        Assert.Equal(data.Length, lastReported);
    }
}
