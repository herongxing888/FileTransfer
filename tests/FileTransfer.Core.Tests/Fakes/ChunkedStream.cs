namespace FileTransfer.Core.Tests.Fakes;

/// A read-only stream that returns at most maxPerRead bytes per read, simulating
/// TCP delivering a frame in fragments.
public sealed class ChunkedStream : Stream
{
    private readonly byte[] _data;
    private readonly int _maxPerRead;
    private int _pos;

    public ChunkedStream(byte[] data, int maxPerRead)
    {
        _data = data;
        _maxPerRead = maxPerRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        await Task.Yield();
        if (_pos >= _data.Length) return 0;
        int n = Math.Min(Math.Min(_maxPerRead, buffer.Length), _data.Length - _pos);
        _data.AsSpan(_pos, n).CopyTo(buffer.Span);
        _pos += n;
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _data.Length;
    public override long Position { get => _pos; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
