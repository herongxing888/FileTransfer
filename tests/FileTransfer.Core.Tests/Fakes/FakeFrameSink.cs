using System.Collections.Concurrent;
using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Tests.Fakes;

public sealed class FakeFrameSink : IFrameSink
{
    public ConcurrentQueue<(MessageType Type, byte[] Payload)> Frames { get; } = new();

    public Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        Frames.Enqueue((type, payload.ToArray()));
        return Task.CompletedTask;
    }
}
