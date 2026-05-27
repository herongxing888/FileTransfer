using System.Security.Cryptography;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transfer;

namespace FileTransfer.Core.Tests.Transfer;

public class FileReceiverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ft-recv-" + Guid.NewGuid());

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    [Fact]
    public void FullFlow_WritesFileToReceiveDirectory()
    {
        byte[] data = { 10, 20, 30, 40, 50 };
        var id = Guid.NewGuid();
        var receiver = new FileReceiver(_dir);

        receiver.Begin(new FileOffer { Id = id, Name = "hello.bin", Size = data.Length });
        receiver.WriteChunk(id, data);
        string finalPath = receiver.Complete(id, Sha(data));

        Assert.True(File.Exists(finalPath));
        Assert.Equal(data, File.ReadAllBytes(finalPath));
        Assert.Equal("hello.bin", Path.GetFileName(finalPath));
    }

    [Fact]
    public void DuplicateName_GetsNumericSuffix()
    {
        byte[] data = { 1 };
        var receiver = new FileReceiver(_dir);

        var id1 = Guid.NewGuid();
        receiver.Begin(new FileOffer { Id = id1, Name = "dup.bin", Size = 1 });
        receiver.WriteChunk(id1, data);
        string first = receiver.Complete(id1, Sha(data));

        var id2 = Guid.NewGuid();
        receiver.Begin(new FileOffer { Id = id2, Name = "dup.bin", Size = 1 });
        receiver.WriteChunk(id2, data);
        string second = receiver.Complete(id2, Sha(data));

        Assert.Equal("dup.bin", Path.GetFileName(first));
        Assert.Equal("dup (1).bin", Path.GetFileName(second));
    }

    [Fact]
    public void ChecksumMismatch_ThrowsAndDeletesPartial()
    {
        byte[] data = { 7, 7, 7 };
        var id = Guid.NewGuid();
        var receiver = new FileReceiver(_dir);

        receiver.Begin(new FileOffer { Id = id, Name = "bad.bin", Size = data.Length });
        receiver.WriteChunk(id, data);

        Assert.Throws<InvalidDataException>((Action)(() => receiver.Complete(id, "DEADBEEF")));
        Assert.False(File.Exists(Path.Combine(_dir, "bad.bin")));
    }

    [Fact]
    public void Cancel_DeletesPartialAndForgetsTransfer()
    {
        var id = Guid.NewGuid();
        var receiver = new FileReceiver(_dir);
        receiver.Begin(new FileOffer { Id = id, Name = "x.bin", Size = 100 });
        receiver.WriteChunk(id, new byte[] { 1, 2 });

        receiver.Cancel(id);

        Assert.Throws<InvalidOperationException>((Action)(() => receiver.WriteChunk(id, new byte[] { 3 })));
    }

    [Fact]
    public void IllegalFileNameCharacters_AreReplaced()
    {
        byte[] data = { 9 };
        var id = Guid.NewGuid();
        var receiver = new FileReceiver(_dir);

        receiver.Begin(new FileOffer { Id = id, Name = "a:b*c?.bin", Size = 1 });
        receiver.WriteChunk(id, data);
        string finalPath = receiver.Complete(id, Sha(data));

        Assert.DoesNotContain(':', Path.GetFileName(finalPath));
        Assert.DoesNotContain('*', Path.GetFileName(finalPath));
        Assert.DoesNotContain('?', Path.GetFileName(finalPath));
    }
}
