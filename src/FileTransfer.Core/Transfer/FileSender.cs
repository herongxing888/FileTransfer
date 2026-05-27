using System.Security.Cryptography;
using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Transfer;

public sealed class FileSender
{
    private readonly IFrameSink _sink;
    private readonly int _chunkSize;

    public FileSender(IFrameSink sink, int chunkSize = 256 * 1024)
    {
        _sink = sink;
        _chunkSize = chunkSize;
    }

    /// Sends FILE_OFFER, then FILE_CHUNK frames, then FILE_DONE (with the file's
    /// SHA256). Returns the transfer id. `progress` reports cumulative bytes sent.
    public async Task<Guid> SendAsync(string path, Action<long>? progress, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var info = new FileInfo(path);

        // Open the file BEFORE announcing the offer: if it is missing or locked we
        // fail here, without leaving the receiver waiting on a transfer that never starts.
        await using var stream = File.OpenRead(path);

        var offer = new FileOffer
        {
            Id = id,
            Name = info.Name,
            Size = stream.Length,
            Mime = MimeFor(info.Extension),
        };
        await _sink.SendAsync(MessageType.FileOffer, MessageSerializer.Serialize(offer), ct);

        using var sha = SHA256.Create();
        var buffer = new byte[_chunkSize];
        long sent = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, _chunkSize), ct)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            byte[] chunk = FileChunkCodec.Encode(id, buffer.AsSpan(0, read));
            await _sink.SendAsync(MessageType.FileChunk, chunk, ct);
            sent += read;
            progress?.Invoke(sent);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var done = new FileDone { Id = id, Sha256 = Convert.ToHexString(sha.Hash!) };
        await _sink.SendAsync(MessageType.FileDone, MessageSerializer.Serialize(done), ct);
        return id;
    }

    private static string MimeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}
