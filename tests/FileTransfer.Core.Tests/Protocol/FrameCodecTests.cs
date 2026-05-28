using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Tests.Protocol;

public class FrameCodecTests
{
    [Fact]
    public void Encode_WritesBigEndianLength_Type_AndPayload()
    {
        byte[] payload = { 0xAA, 0xBB, 0xCC };

        byte[] frame = FrameCodec.Encode(MessageType.Text, payload);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x03, (byte)MessageType.Text, 0xAA, 0xBB, 0xCC }, frame);
    }

    [Fact]
    public void Encode_EmptyPayload_ProducesFiveByteHeaderOnly()
    {
        byte[] frame = FrameCodec.Encode(MessageType.Ping, ReadOnlySpan<byte>.Empty);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, (byte)MessageType.Ping }, frame);
    }

    [Fact]
    public void Encode_PayloadOverMax_Throws()
    {
        var tooBig = new byte[FrameCodec.MaxPayloadSize + 1];

        Assert.Throws<ArgumentException>(() => FrameCodec.Encode(MessageType.FileChunk, tooBig));
    }
}
