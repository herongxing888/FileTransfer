using System.Collections.Concurrent;
using System.Security.Cryptography;
using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Transfer;

/// Receives one or more concurrent transfers, each streamed to a .part temp file,
/// verified by SHA256 on completion, then moved into the receive directory with a
/// sanitized, de-duplicated name.
public sealed class FileReceiver
{
    private readonly string _receiveDirectory;
    private readonly ConcurrentDictionary<Guid, Incoming> _active = new();

    public FileReceiver(string receiveDirectory)
    {
        _receiveDirectory = receiveDirectory;
        Directory.CreateDirectory(receiveDirectory);
    }

    public void Begin(FileOffer offer)
    {
        string partPath = Path.Combine(Path.GetTempPath(), $"ft-{offer.Id:N}.part");
        var stream = new FileStream(partPath, FileMode.Create, FileAccess.Write);
        _active[offer.Id] = new Incoming(offer, partPath, stream, IncrementalHash.CreateHash(HashAlgorithmName.SHA256));
    }

    public void WriteChunk(Guid id, ReadOnlySpan<byte> data)
    {
        if (!_active.TryGetValue(id, out var incoming))
            throw new InvalidOperationException($"No active transfer {id}.");
        incoming.Stream.Write(data);
        incoming.Hash.AppendData(data);
    }

    /// Verifies the checksum, moves the file into place, and returns the final path.
    /// On mismatch, deletes the partial and throws InvalidDataException.
    public string Complete(Guid id, string expectedSha256)
    {
        if (!_active.TryRemove(id, out var incoming))
            throw new InvalidOperationException($"No active transfer {id}.");

        incoming.Stream.Flush();
        incoming.Stream.Dispose();

        string actual = Convert.ToHexString(incoming.Hash.GetHashAndReset());
        incoming.Hash.Dispose();

        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(incoming.PartPath);
            throw new InvalidDataException($"Checksum mismatch for {incoming.Offer.Name}.");
        }

        string finalPath = UniquePath(Sanitize(incoming.Offer.Name));
        File.Move(incoming.PartPath, finalPath);
        return finalPath;
    }

    public void Cancel(Guid id)
    {
        if (_active.TryRemove(id, out var incoming))
        {
            incoming.Stream.Dispose();
            incoming.Hash.Dispose();
            TryDelete(incoming.PartPath);
        }
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "received.bin" : name;
    }

    private string UniquePath(string fileName)
    {
        string candidate = Path.Combine(_receiveDirectory, fileName);
        if (!File.Exists(candidate)) return candidate;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 1; ; i++)
        {
            candidate = Path.Combine(_receiveDirectory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private sealed record Incoming(FileOffer Offer, string PartPath, FileStream Stream, IncrementalHash Hash);
}
