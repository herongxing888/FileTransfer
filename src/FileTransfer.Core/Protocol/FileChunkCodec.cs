namespace FileTransfer.Core.Protocol;

/// FILE_CHUNK payload layout: 16-byte transfer GUID followed by the raw bytes.
public static class FileChunkCodec
{
    public const int IdLength = 16;

    public static byte[] Encode(Guid id, ReadOnlySpan<byte> data)
    {
        var buffer = new byte[IdLength + data.Length];
        id.TryWriteBytes(buffer.AsSpan(0, IdLength));
        data.CopyTo(buffer.AsSpan(IdLength));
        return buffer;
    }

    public static (Guid Id, byte[] Data) Decode(byte[] payload)
    {
        if (payload.Length < IdLength)
            throw new InvalidDataException("Chunk payload shorter than GUID header.");
        var id = new Guid(payload.AsSpan(0, IdLength));
        var data = payload[IdLength..];
        return (id, data);
    }
}
