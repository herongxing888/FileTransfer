using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Transport;

/// Owns a bidirectional stream (an SslStream in production). Runs a receive loop
/// that raises FrameReceived, answers PINGs with PONGs, and emits Closed when the
/// peer disconnects or the heartbeat times out. Sends are serialized with a lock.
public sealed class Connection : IFrameSink, IDisposable
{
    private readonly Stream _stream;
    private readonly FrameReader _reader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly CancellationTokenSource _cts = new();

    private DateTime _lastInbound = DateTime.UtcNow;
    private int _closedRaised;

    /// Raised for every non-PING/PONG frame. Handlers must not block.
    public event Action<MessageType, byte[]>? FrameReceived;
    /// Raised once when the connection ends (EOF, error, or heartbeat timeout).
    public event Action<Exception?>? Closed;

    public Connection(Stream stream, TimeSpan heartbeatInterval, TimeSpan heartbeatTimeout)
    {
        _stream = stream;
        _reader = new FrameReader(stream);
        _heartbeatInterval = heartbeatInterval;
        _heartbeatTimeout = heartbeatTimeout;
    }

    public void Start()
    {
        _ = ReceiveLoopAsync(_cts.Token);
        if (_heartbeatInterval != Timeout.InfiniteTimeSpan)
            _ = HeartbeatLoopAsync(_cts.Token);
    }

    public async Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        byte[] frame = FrameCodec.Encode(type, payload.Span);
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await _reader.ReadAsync(ct);
                if (frame is null) { RaiseClosed(null); return; } // clean EOF
                _lastInbound = DateTime.UtcNow;

                switch (frame.Value.Type)
                {
                    case MessageType.Ping:
                        await SendAsync(MessageType.Pong, ReadOnlyMemory<byte>.Empty, ct);
                        break;
                    case MessageType.Pong:
                        break; // liveness already recorded via _lastInbound
                    default:
                        FrameReceived?.Invoke(frame.Value.Type, frame.Value.Payload);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* disposed */ }
        catch (Exception ex) { RaiseClosed(ex); }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_heartbeatInterval, ct);
                if (_heartbeatTimeout != Timeout.InfiniteTimeSpan &&
                    DateTime.UtcNow - _lastInbound > _heartbeatTimeout)
                {
                    RaiseClosed(new TimeoutException("Heartbeat timed out."));
                    return;
                }
                try { await SendAsync(MessageType.Ping, ReadOnlyMemory<byte>.Empty, ct); }
                catch { RaiseClosed(new IOException("Failed to send heartbeat.")); return; }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RaiseClosed(Exception? ex)
    {
        if (Interlocked.Exchange(ref _closedRaised, 1) == 0)
        {
            _cts.Cancel();
            Closed?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        RaiseClosed(null);
        _cts.Dispose();
        _stream.Dispose();
        _writeLock.Dispose();
    }
}
