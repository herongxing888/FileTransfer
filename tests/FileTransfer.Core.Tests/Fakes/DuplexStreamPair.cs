using System.IO.Pipelines;

namespace FileTransfer.Core.Tests.Fakes;

/// Creates two Streams A and B where bytes written to A are readable from B and vice versa.
public static class DuplexStreamPair
{
    public static (Stream A, Stream B) Create()
    {
        var aToB = new Pipe();
        var bToA = new Pipe();
        var a = new DuplexStream(read: bToA.Reader.AsStream(), write: aToB.Writer.AsStream());
        var b = new DuplexStream(read: aToB.Reader.AsStream(), write: bToA.Writer.AsStream());
        return (a, b);
    }

    private sealed class DuplexStream : Stream
    {
        private readonly Stream _read;
        private readonly Stream _write;
        public DuplexStream(Stream read, Stream write) { _read = read; _write = write; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => await _read.ReadAsync(buffer, ct);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await _write.WriteAsync(buffer, ct);
            await _write.FlushAsync(ct);
        }
        public override int Read(byte[] b, int o, int c) => _read.Read(b, o, c);
        public override void Write(byte[] b, int o, int c) { _write.Write(b, o, c); _write.Flush(); }
        public override void Flush() => _write.Flush();
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }
}
