using System.Buffers.Binary;

namespace FileTransfer.Core.Protocol;

public sealed class FrameReader
{
    private readonly Stream _stream;

    public FrameReader(Stream stream) => _stream = stream;

    /// Returns the next frame, or null on a clean end-of-stream (no bytes left).
    public async ValueTask<(MessageType Type, byte[] Payload)?> ReadAsync(CancellationToken ct)
    {
        byte[]? header = await ReadExactAsync(FrameCodec.HeaderSize, allowEof: true, ct);
        if (header is null) return null;

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (length > FrameCodec.MaxPayloadSize)
            throw new InvalidDataException($"Frame length {length} exceeds max {FrameCodec.MaxPayloadSize}.");

        var type = (MessageType)header[4];
        byte[] payload = length == 0
            ? Array.Empty<byte>()
            : (await ReadExactAsync((int)length, allowEof: false, ct))!;

        return (type, payload);
    }

    private async ValueTask<byte[]?> ReadExactAsync(int count, bool allowEof, CancellationToken ct)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await _stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
            {
                if (read == 0 && allowEof) return null;
                throw new EndOfStreamException($"Stream ended after {read}/{count} bytes.");
            }
            read += n;
        }
        return buffer;
    }
}
