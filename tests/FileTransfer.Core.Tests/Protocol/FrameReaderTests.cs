using FileTransfer.Core.Protocol;
using FileTransfer.Core.Tests.Fakes;

namespace FileTransfer.Core.Tests.Protocol;

public class FrameReaderTests
{
    [Fact]
    public async Task ReadAsync_DecodesSingleFrame()
    {
        byte[] payload = { 1, 2, 3, 4, 5 };
        byte[] frame = FrameCodec.Encode(MessageType.Text, payload);
        var reader = new FrameReader(new MemoryStream(frame));

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(MessageType.Text, result!.Value.Type);
        Assert.Equal(payload, result.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_ReassemblesFrameDeliveredOneByteAtATime()
    {
        byte[] payload = { 9, 8, 7 };
        byte[] frame = FrameCodec.Encode(MessageType.FileOffer, payload);
        var reader = new FrameReader(new ChunkedStream(frame, maxPerRead: 1));

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(MessageType.FileOffer, result!.Value.Type);
        Assert.Equal(payload, result.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_ReadsTwoBackToBackFrames()
    {
        byte[] a = FrameCodec.Encode(MessageType.Ping, ReadOnlySpan<byte>.Empty);
        byte[] b = FrameCodec.Encode(MessageType.Text, new byte[] { 42 });
        var reader = new FrameReader(new MemoryStream(a.Concat(b).ToArray()));

        var first = await reader.ReadAsync(CancellationToken.None);
        var second = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(MessageType.Ping, first!.Value.Type);
        Assert.Empty(first.Value.Payload);
        Assert.Equal(MessageType.Text, second!.Value.Type);
        Assert.Equal(new byte[] { 42 }, second.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullOnCleanEof()
    {
        var reader = new FrameReader(new MemoryStream(Array.Empty<byte>()));

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_ThrowsWhenDeclaredLengthExceedsMax()
    {
        // header declares length = MaxPayloadSize + 1
        byte[] header = { 0x01, 0x00, 0x00, 0x01, (byte)MessageType.FileChunk };
        var reader = new FrameReader(new MemoryStream(header));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => reader.ReadAsync(CancellationToken.None).AsTask());
    }
}
