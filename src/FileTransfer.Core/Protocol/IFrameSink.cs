namespace FileTransfer.Core.Protocol;

/// Narrow send-only interface so file transfer logic depends on "something I
/// can push frames into" rather than on a concrete socket/TLS connection.
public interface IFrameSink
{
    Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
