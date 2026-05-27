using System.Buffers.Binary;

namespace FileTransfer.Core.Protocol;

public static class FrameCodec
{
    public const int MaxPayloadSize = 16 * 1024 * 1024; // 16 MB
    public const int HeaderSize = 5; // 4-byte length + 1-byte type

    public static byte[] Encode(MessageType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadSize)
            throw new ArgumentException($"Payload {payload.Length} exceeds max {MaxPayloadSize}.", nameof(payload));

        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)payload.Length);
        frame[4] = (byte)type;
        payload.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }
}
