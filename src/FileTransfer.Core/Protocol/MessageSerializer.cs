using System.Text.Json;

namespace FileTransfer.Core.Protocol;

public static class MessageSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static byte[] Serialize<T>(T message)
        => JsonSerializer.SerializeToUtf8Bytes(message, Options);

    public static T Deserialize<T>(ReadOnlySpan<byte> payload)
        => JsonSerializer.Deserialize<T>(payload, Options)
           ?? throw new InvalidDataException($"Payload deserialized to null for {typeof(T).Name}.");
}
