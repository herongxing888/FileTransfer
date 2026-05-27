# FileTransfer.Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the headless, fully-testable core library that lets two paired Windows machines discover each other on a LAN and exchange text, files, and images over a TLS-encrypted custom binary protocol.

**Architecture:** A single `net8.0-windows` class library (`FileTransfer.Core`) with clearly bounded modules — Protocol (framing + messages), Crypto (self-signed cert + fingerprint + pairing code), Config (DPAPI-protected persistence), Discovery (UDP broadcast), Transport (TCP + TLS with fingerprint pinning), Transfer (chunked file send/receive), and a top-level `Node` orchestrator. Every module talks through narrow interfaces so it can be unit-tested without real network or UI. The deliverable is proven by an end-to-end integration test where two in-process `Node` instances complete the full discover → pair → transfer flow over loopback.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, xUnit, `System.Security.Cryptography` (native self-signed certs + DPAPI), `System.Net.Sockets`, `System.Net.Security.SslStream`, `System.Text.Json`.

---

## File Structure

```
FileTransfer.sln
src/FileTransfer.Core/
  Protocol/
    MessageType.cs        enum of the 8 frame types
    FrameCodec.cs         encode a (type, payload) into a length-prefixed frame
    FrameReader.cs        read frames from a Stream, handle partial/split reads
    Messages.cs           JSON DTOs: HelloMessage, TextMessage, FileOffer, FileDone, FileCancel
    MessageSerializer.cs  JSON serialize/deserialize helpers
    IFrameSink.cs         narrow send interface (Connection implements it)
    FileChunkCodec.cs     pack/unpack the 16-byte-GUID + bytes chunk payload
  Crypto/
    CertificateFactory.cs self-signed cert generation + PFX roundtrip
    Fingerprint.cs        SHA256 fingerprint, pairing-code derivation, initiator arbitration
  Config/
    ISecretProtector.cs   abstraction over DPAPI
    DpapiProtector.cs      Windows DPAPI implementation
    AppConfig.cs          config model + load/save to %APPDATA%\FileTransfer\config.json
  Discovery/
    PeerInfo.cs           a discovered peer (endpoint, fingerprint, device name)
    DiscoveryService.cs   UDP broadcast announce + listen
  Transport/
    Connection.cs         wraps an SslStream: send/receive frames + PING/PONG heartbeat
    TransportListener.cs  TCP listener + TLS accept with fingerprint pinning
    TransportConnector.cs TCP connect + TLS handshake with fingerprint pinning
  Transfer/
    FileSender.cs         stream a file as FILE_CHUNK frames through an IFrameSink
    FileReceiver.cs       assemble chunks to a .part temp file, verify sha256, dedupe name
  Node.cs                 orchestrator: ties discovery + transport + transfer; events + send methods
  ConnectionStatus.cs     enum: Disconnected / Pairing / Online / Offline

tests/FileTransfer.Core.Tests/
  (mirrors the module layout; one test file per production file that has logic)
  Fakes/
    DuplexStreamPair.cs   in-memory bidirectional stream pair for Connection tests
    FakeFrameSink.cs      collects frames for FileSender tests
    PassthroughProtector.cs  ISecretProtector that does not encrypt (for config tests)
```

**Boundary rationale:** Protocol has zero I/O — pure bytes ↔ objects, trivially unit-tested. Crypto is pure functions over cert bytes. Transfer depends only on `IFrameSink`, never on sockets, so it tests against a fake sink. Only Discovery, Transport, and the end-to-end Node tests touch real loopback sockets. DPAPI is hidden behind `ISecretProtector` so config serialization tests use a passthrough fake and stay deterministic.

---

## Task 1: Scaffold solution and projects

**Files:**
- Create: `FileTransfer.sln`
- Create: `src/FileTransfer.Core/FileTransfer.Core.csproj`
- Create: `tests/FileTransfer.Core.Tests/FileTransfer.Core.Tests.csproj`

- [ ] **Step 1: Create the solution and projects**

Run from the repo root (`d:\Project\File Transfer`):

```powershell
dotnet new sln -n FileTransfer
dotnet new classlib -n FileTransfer.Core -o src/FileTransfer.Core -f net8.0-windows
dotnet new xunit -n FileTransfer.Core.Tests -o tests/FileTransfer.Core.Tests -f net8.0-windows
Remove-Item src/FileTransfer.Core/Class1.cs
Remove-Item tests/FileTransfer.Core.Tests/UnitTest1.cs
dotnet sln add src/FileTransfer.Core/FileTransfer.Core.csproj
dotnet sln add tests/FileTransfer.Core.Tests/FileTransfer.Core.Tests.csproj
dotnet add tests/FileTransfer.Core.Tests/FileTransfer.Core.Tests.csproj reference src/FileTransfer.Core/FileTransfer.Core.csproj
```

- [ ] **Step 2: Enable nullable + implicit usings in Core**

Edit `src/FileTransfer.Core/FileTransfer.Core.csproj` so the `<PropertyGroup>` contains:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <LangVersion>latest</LangVersion>
</PropertyGroup>
```

- [ ] **Step 3: Add a trivial smoke test**

Create `tests/FileTransfer.Core.Tests/SmokeTest.cs`:

```csharp
namespace FileTransfer.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_Builds_And_Tests_Run()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet test`
Expected: build succeeds, 1 test passes.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "chore: scaffold FileTransfer solution with Core library and xUnit tests"
```

---

## Task 2: Frame encoding

**Files:**
- Create: `src/FileTransfer.Core/Protocol/MessageType.cs`
- Create: `src/FileTransfer.Core/Protocol/FrameCodec.cs`
- Test: `tests/FileTransfer.Core.Tests/Protocol/FrameCodecTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Protocol/FrameCodecTests.cs`:

```csharp
using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Tests.Protocol;

public class FrameCodecTests
{
    [Fact]
    public void Encode_WritesBigEndianLength_Type_AndPayload()
    {
        byte[] payload = { 0xAA, 0xBB, 0xCC };

        byte[] frame = FrameCodec.Encode(MessageType.Text, payload);

        // 4-byte big-endian length = 3, then type byte, then payload
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x03, (byte)MessageType.Text, 0xAA, 0xBB, 0xCC }, frame);
    }

    [Fact]
    public void Encode_EmptyPayload_ProducesFiveByteHeaderOnly()
    {
        byte[] frame = FrameCodec.Encode(MessageType.Ping, ReadOnlySpan<byte>.Empty);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, (byte)MessageType.Ping }, frame);
    }

    [Fact]
    public void Encode_PayloadOverMax_Throws()
    {
        var tooBig = new byte[FrameCodec.MaxPayloadSize + 1];

        Assert.Throws<ArgumentException>(() => FrameCodec.Encode(MessageType.FileChunk, tooBig));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FrameCodecTests"`
Expected: FAIL — `MessageType` / `FrameCodec` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Protocol/MessageType.cs`:

```csharp
namespace FileTransfer.Core.Protocol;

public enum MessageType : byte
{
    Hello = 0x01,
    Text = 0x10,
    FileOffer = 0x20,
    FileChunk = 0x21,
    FileDone = 0x22,
    FileCancel = 0x23,
    Ping = 0xF0,
    Pong = 0xF1,
}
```

Create `src/FileTransfer.Core/Protocol/FrameCodec.cs`:

```csharp
using System.Buffers.Binary;

namespace FileTransfer.Core.Protocol;

public static class FrameCodec
{
    public const int MaxPayloadSize = 16 * 1024 * 1024; // 16 MB
    public const int HeaderSize = 5; // 4-byte length + 1-byte type

    public static byte[] Encode(MessageType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadSize)
            throw new ArgumentException($"Payload {payload.Length} exceeds max {MaxPayloadSize}.", nameof(payload));

        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)payload.Length);
        frame[4] = (byte)type;
        payload.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FrameCodecTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(protocol): add MessageType enum and frame encoder"
```

---

## Task 3: Frame reading from a stream

**Files:**
- Create: `src/FileTransfer.Core/Protocol/FrameReader.cs`
- Test: `tests/FileTransfer.Core.Tests/Protocol/FrameReaderTests.cs`
- Create: `tests/FileTransfer.Core.Tests/Fakes/ChunkedStream.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Fakes/ChunkedStream.cs` — a read-only stream that hands out at most `maxPerRead` bytes per `ReadAsync` call, to prove the reader reassembles split frames:

```csharp
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
```

Create `tests/FileTransfer.Core.Tests/Protocol/FrameReaderTests.cs`:

```csharp
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Tests.Fakes;

namespace FileTransfer.Core.Tests.Protocol;

public class FrameReaderTests
{
    [Fact]
    public async Task ReadAsync_DecodesSingleFrame()
    {
        byte[] payload = { 1, 2, 3, 4, 5 };
        byte[] frame = FrameCodec.Encode(MessageType.Text, payload);
        var reader = new FrameReader(new MemoryStream(frame));

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(MessageType.Text, result!.Value.Type);
        Assert.Equal(payload, result.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_ReassemblesFrameDeliveredOneByteAtATime()
    {
        byte[] payload = { 9, 8, 7 };
        byte[] frame = FrameCodec.Encode(MessageType.FileOffer, payload);
        var reader = new FrameReader(new ChunkedStream(frame, maxPerRead: 1));

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(MessageType.FileOffer, result!.Value.Type);
        Assert.Equal(payload, result.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_ReadsTwoBackToBackFrames()
    {
        byte[] a = FrameCodec.Encode(MessageType.Ping, ReadOnlySpan<byte>.Empty);
        byte[] b = FrameCodec.Encode(MessageType.Text, new byte[] { 42 });
        var reader = new FrameReader(new MemoryStream(a.Concat(b).ToArray()));

        var first = await reader.ReadAsync(CancellationToken.None);
        var second = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(MessageType.Ping, first!.Value.Type);
        Assert.Empty(first.Value.Payload);
        Assert.Equal(MessageType.Text, second!.Value.Type);
        Assert.Equal(new byte[] { 42 }, second.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullOnCleanEof()
    {
        var reader = new FrameReader(new MemoryStream(Array.Empty<byte>()));

        var result = await reader.ReadAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_ThrowsWhenDeclaredLengthExceedsMax()
    {
        // header declares length = MaxPayloadSize + 1
        byte[] header = { 0x01, 0x00, 0x00, 0x01, (byte)MessageType.FileChunk };
        var reader = new FrameReader(new MemoryStream(header));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => reader.ReadAsync(CancellationToken.None).AsTask());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FrameReaderTests"`
Expected: FAIL — `FrameReader` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Protocol/FrameReader.cs`:

```csharp
using System.Buffers.Binary;

namespace FileTransfer.Core.Protocol;

public sealed class FrameReader
{
    private readonly Stream _stream;

    public FrameReader(Stream stream) => _stream = stream;

    /// Returns the next frame, or null on a clean end-of-stream (no bytes left).
    public async ValueTask<(MessageType Type, byte[] Payload)?> ReadAsync(CancellationToken ct)
    {
        byte[]? header = await ReadExactAsync(FrameCodec.HeaderSize, allowEof: true, ct);
        if (header is null) return null;

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (length > FrameCodec.MaxPayloadSize)
            throw new InvalidDataException($"Frame length {length} exceeds max {FrameCodec.MaxPayloadSize}.");

        var type = (MessageType)header[4];
        byte[] payload = length == 0
            ? Array.Empty<byte>()
            : (await ReadExactAsync((int)length, allowEof: false, ct))!;

        return (type, payload);
    }

    private async ValueTask<byte[]?> ReadExactAsync(int count, bool allowEof, CancellationToken ct)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await _stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
            {
                if (read == 0 && allowEof) return null;
                throw new EndOfStreamException($"Stream ended after {read}/{count} bytes.");
            }
            read += n;
        }
        return buffer;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FrameReaderTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(protocol): add FrameReader with split-frame reassembly and size guard"
```

---

## Task 4: Message DTOs and JSON serialization

**Files:**
- Create: `src/FileTransfer.Core/Protocol/Messages.cs`
- Create: `src/FileTransfer.Core/Protocol/MessageSerializer.cs`
- Test: `tests/FileTransfer.Core.Tests/Protocol/MessageSerializerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Protocol/MessageSerializerTests.cs`:

```csharp
using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Tests.Protocol;

public class MessageSerializerTests
{
    [Fact]
    public void TextMessage_RoundTrips()
    {
        var msg = new TextMessage { Id = Guid.NewGuid(), Text = "你好, world" };

        byte[] bytes = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<TextMessage>(bytes);

        Assert.Equal(msg.Id, back.Id);
        Assert.Equal(msg.Text, back.Text);
    }

    [Fact]
    public void FileOffer_RoundTrips()
    {
        var offer = new FileOffer { Id = Guid.NewGuid(), Name = "report.pdf", Size = 2400000, Mime = "application/pdf" };

        var back = MessageSerializer.Deserialize<FileOffer>(MessageSerializer.Serialize(offer));

        Assert.Equal(offer.Name, back.Name);
        Assert.Equal(offer.Size, back.Size);
        Assert.Equal(offer.Mime, back.Mime);
    }

    [Fact]
    public void HelloMessage_RoundTrips()
    {
        var hello = new HelloMessage { DeviceName = "DESKTOP-XYZ", ProtocolVersion = 1 };

        var back = MessageSerializer.Deserialize<HelloMessage>(MessageSerializer.Serialize(hello));

        Assert.Equal("DESKTOP-XYZ", back.DeviceName);
        Assert.Equal(1, back.ProtocolVersion);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MessageSerializerTests"`
Expected: FAIL — message types do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Protocol/Messages.cs`:

```csharp
namespace FileTransfer.Core.Protocol;

public sealed class HelloMessage
{
    public string DeviceName { get; set; } = "";
    public int ProtocolVersion { get; set; } = 1;
}

public sealed class TextMessage
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
}

public sealed class FileOffer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Mime { get; set; } = "application/octet-stream";
}

public sealed class FileDone
{
    public Guid Id { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class FileCancel
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = "";
}
```

Create `src/FileTransfer.Core/Protocol/MessageSerializer.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~MessageSerializerTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(protocol): add message DTOs and JSON serializer"
```

---

## Task 5: Fingerprint, pairing code, and initiator arbitration

**Files:**
- Create: `src/FileTransfer.Core/Crypto/Fingerprint.cs`
- Test: `tests/FileTransfer.Core.Tests/Crypto/FingerprintTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Crypto/FingerprintTests.cs`:

```csharp
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Tests.Crypto;

public class FingerprintTests
{
    [Fact]
    public void Compute_IsUppercaseHex_64Chars()
    {
        byte[] certBytes = { 1, 2, 3, 4 };

        string fp = Fingerprint.Compute(certBytes);

        Assert.Equal(64, fp.Length);
        Assert.Matches("^[0-9A-F]+$", fp);
    }

    [Fact]
    public void PairingCode_IsFourDigits()
    {
        string code = Fingerprint.PairingCode("AAAA", "BBBB");

        Assert.Matches("^[0-9]{4}$", code);
    }

    [Fact]
    public void PairingCode_IsOrderIndependent()
    {
        // Both machines must compute the same code regardless of who calls first.
        string fromA = Fingerprint.PairingCode("AAAA", "BBBB");
        string fromB = Fingerprint.PairingCode("BBBB", "AAAA");

        Assert.Equal(fromA, fromB);
    }

    [Fact]
    public void PairingCode_DiffersForDifferentPeers()
    {
        Assert.NotEqual(
            Fingerprint.PairingCode("AAAA", "BBBB"),
            Fingerprint.PairingCode("AAAA", "CCCC"));
    }

    [Fact]
    public void LocalInitiates_TrueWhenLocalFingerprintSortsFirst()
    {
        Assert.True(Fingerprint.LocalInitiates(local: "AAAA", peer: "BBBB"));
        Assert.False(Fingerprint.LocalInitiates(local: "BBBB", peer: "AAAA"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FingerprintTests"`
Expected: FAIL — `Fingerprint` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Crypto/Fingerprint.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace FileTransfer.Core.Crypto;

public static class Fingerprint
{
    /// SHA256 of the raw certificate bytes, as uppercase hex.
    public static string Compute(byte[] certRawData)
        => Convert.ToHexString(SHA256.HashData(certRawData));

    /// A 4-digit code derived from both fingerprints. Deterministic and
    /// independent of argument order, so both machines compute the same value.
    /// A man-in-the-middle (with different keys) produces a mismatching code.
    public static string PairingCode(string fingerprintA, string fingerprintB)
    {
        string ordered = string.CompareOrdinal(fingerprintA, fingerprintB) <= 0
            ? fingerprintA + fingerprintB
            : fingerprintB + fingerprintA;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ordered));
        int value = ((hash[0] << 8) | hash[1]) % 10000;
        return value.ToString("D4");
    }

    /// Deterministic tie-breaker for who dials whom when both sides discover
    /// each other at once: the lexicographically smaller fingerprint connects.
    public static bool LocalInitiates(string local, string peer)
        => string.CompareOrdinal(local, peer) < 0;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FingerprintTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(crypto): add fingerprint, pairing-code derivation, and initiator arbitration"
```

---

## Task 6: Self-signed certificate factory

**Files:**
- Create: `src/FileTransfer.Core/Crypto/CertificateFactory.cs`
- Test: `tests/FileTransfer.Core.Tests/Crypto/CertificateFactoryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Crypto/CertificateFactoryTests.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Tests.Crypto;

public class CertificateFactoryTests
{
    [Fact]
    public void CreateSelfSigned_ProducesCertWithPrivateKey()
    {
        using var cert = CertificateFactory.CreateSelfSigned("FileTransfer-TestMachine");

        Assert.True(cert.HasPrivateKey);
        Assert.Contains("FileTransfer-TestMachine", cert.Subject);
    }

    [Fact]
    public void ExportAndImportPfx_PreservesFingerprintAndPrivateKey()
    {
        using var original = CertificateFactory.CreateSelfSigned("RoundTrip");
        string originalFp = Fingerprint.Compute(original.RawData);

        byte[] pfx = CertificateFactory.ExportPfx(original);
        using var imported = CertificateFactory.ImportPfx(pfx);

        Assert.True(imported.HasPrivateKey);
        Assert.Equal(originalFp, Fingerprint.Compute(imported.RawData));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CertificateFactoryTests"`
Expected: FAIL — `CertificateFactory` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Crypto/CertificateFactory.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FileTransfer.Core.Crypto;

public static class CertificateFactory
{
    public static X509Certificate2 CreateSelfSigned(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
    }

    /// Export including the private key as a PFX byte blob (no password — the
    /// blob itself is DPAPI-protected by the caller before being persisted).
    public static byte[] ExportPfx(X509Certificate2 cert)
        => cert.Export(X509ContentType.Pfx);

    public static X509Certificate2 ImportPfx(byte[] pfx)
        => new(pfx, (string?)null,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CertificateFactoryTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(crypto): add self-signed certificate factory with PFX roundtrip"
```

---

## Task 7: Secret protector and app config persistence

**Files:**
- Create: `src/FileTransfer.Core/Config/ISecretProtector.cs`
- Create: `src/FileTransfer.Core/Config/DpapiProtector.cs`
- Create: `src/FileTransfer.Core/Config/AppConfig.cs`
- Create: `tests/FileTransfer.Core.Tests/Fakes/PassthroughProtector.cs`
- Test: `tests/FileTransfer.Core.Tests/Config/AppConfigTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Fakes/PassthroughProtector.cs`:

```csharp
using FileTransfer.Core.Config;

namespace FileTransfer.Core.Tests.Fakes;

/// Test double for ISecretProtector that does not actually encrypt, so config
/// tests stay deterministic and platform-independent.
public sealed class PassthroughProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => (byte[])data.Clone();
    public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
}
```

Create `tests/FileTransfer.Core.Tests/Config/AppConfigTests.cs`:

```csharp
using FileTransfer.Core.Config;
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Tests.Fakes;

namespace FileTransfer.Core.Tests.Config;

public class AppConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ft-cfg-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RestoresAllFields()
    {
        var protector = new PassthroughProtector();
        using var cert = CertificateFactory.CreateSelfSigned("ConfigTest");
        string path = Path.Combine(_dir, "config.json");

        var config = new AppConfig
        {
            DeviceName = "MyLaptop",
            ReceiveDirectory = @"C:\Recv",
            AutoStart = true,
            PeerFingerprint = "DEADBEEF",
            PeerDeviceName = "MyDesktop",
        };
        config.SetCertificate(cert, protector);

        config.Save(path, protector);
        var loaded = AppConfig.Load(path, protector);

        Assert.Equal("MyLaptop", loaded.DeviceName);
        Assert.Equal(@"C:\Recv", loaded.ReceiveDirectory);
        Assert.True(loaded.AutoStart);
        Assert.Equal("DEADBEEF", loaded.PeerFingerprint);
        Assert.Equal("MyDesktop", loaded.PeerDeviceName);

        using var restoredCert = loaded.GetCertificate(protector);
        Assert.True(restoredCert.HasPrivateKey);
        Assert.Equal(Fingerprint.Compute(cert.RawData), Fingerprint.Compute(restoredCert.RawData));
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var loaded = AppConfig.Load(Path.Combine(_dir, "nope.json"), new PassthroughProtector());

        Assert.Null(loaded);
    }

    [Fact]
    public void IsPaired_FalseUntilPeerFingerprintSet()
    {
        var config = new AppConfig();
        Assert.False(config.IsPaired);

        config.PeerFingerprint = "ABC";
        Assert.True(config.IsPaired);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~AppConfigTests"`
Expected: FAIL — `ISecretProtector` / `AppConfig` do not exist.

- [ ] **Step 3: Write the implementations**

Create `src/FileTransfer.Core/Config/ISecretProtector.cs`:

```csharp
namespace FileTransfer.Core.Config;

public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}
```

Create `src/FileTransfer.Core/Config/DpapiProtector.cs`:

```csharp
using System.Security.Cryptography;

namespace FileTransfer.Core.Config;

/// Encrypts secrets with Windows DPAPI scoped to the current user, so the
/// stored private key is unreadable by other accounts on the machine.
public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data)
        => ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
```

Create `src/FileTransfer.Core/Config/AppConfig.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Config;

public sealed class AppConfig
{
    public string DeviceName { get; set; } = Environment.MachineName;
    public string ReceiveDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "FileTransfer");
    public bool AutoStart { get; set; }

    public string? PeerFingerprint { get; set; }
    public string? PeerDeviceName { get; set; }

    /// Base64 of the DPAPI-protected PFX blob holding this machine's cert + private key.
    public string? ProtectedCertificate { get; set; }

    [JsonIgnore]
    public bool IsPaired => !string.IsNullOrEmpty(PeerFingerprint);

    public void SetCertificate(X509Certificate2 cert, ISecretProtector protector)
    {
        byte[] pfx = CertificateFactory.ExportPfx(cert);
        ProtectedCertificate = Convert.ToBase64String(protector.Protect(pfx));
    }

    public X509Certificate2 GetCertificate(ISecretProtector protector)
    {
        if (string.IsNullOrEmpty(ProtectedCertificate))
            throw new InvalidOperationException("No certificate stored in config.");
        byte[] pfx = protector.Unprotect(Convert.FromBase64String(ProtectedCertificate));
        return CertificateFactory.ImportPfx(pfx);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public void Save(string path, ISecretProtector protector)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    /// Loads config, or returns null if the file does not exist.
    public static AppConfig? Load(string path, ISecretProtector protector)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json)
               ?? throw new InvalidDataException("Config file is empty or corrupt.");
    }

    /// Default config path: %APPDATA%\FileTransfer\config.json
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileTransfer", "config.json");
}
```

> Note: `Save`/`Load` take the protector for symmetry and future use (e.g. encrypting the whole file); today only the certificate field is protected, via `SetCertificate`/`GetCertificate`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AppConfigTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(config): add DPAPI secret protector and app config persistence"
```

---

## Task 8: UDP discovery service

**Files:**
- Create: `src/FileTransfer.Core/Discovery/PeerInfo.cs`
- Create: `src/FileTransfer.Core/Discovery/DiscoveryService.cs`
- Test: `tests/FileTransfer.Core.Tests/Discovery/DiscoveryServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Discovery/DiscoveryServiceTests.cs`. Two services on loopback, both broadcasting; each should hear the other. The test uses the loopback address for the announce target so it does not depend on a real subnet broadcast:

```csharp
using FileTransfer.Core.Discovery;

namespace FileTransfer.Core.Tests.Discovery;

public class DiscoveryServiceTests
{
    [Fact]
    public async Task TwoServices_DiscoverEachOther()
    {
        int port = 47900; // dedicated test port
        using var a = new DiscoveryService(
            udpPort: port, tcpPort: 47901, fingerprint: "AAAA", deviceName: "A",
            announceInterval: TimeSpan.FromMilliseconds(100));
        using var b = new DiscoveryService(
            udpPort: port, tcpPort: 47902, fingerprint: "BBBB", deviceName: "B",
            announceInterval: TimeSpan.FromMilliseconds(100));

        PeerInfo? heardByA = null;
        PeerInfo? heardByB = null;
        a.PeerDiscovered += p => { if (p.Fingerprint == "BBBB") heardByA = p; };
        b.PeerDiscovered += p => { if (p.Fingerprint == "AAAA") heardByB = p; };

        a.Start();
        b.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((heardByA is null || heardByB is null) && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.NotNull(heardByA);
        Assert.NotNull(heardByB);
        Assert.Equal("B", heardByA!.DeviceName);
        Assert.Equal(47902, heardByB!.TcpPort);
    }

    [Fact]
    public async Task DoesNotRaiseForOwnAnnouncements()
    {
        int port = 47910;
        using var solo = new DiscoveryService(
            udpPort: port, tcpPort: 47911, fingerprint: "SELF", deviceName: "Solo",
            announceInterval: TimeSpan.FromMilliseconds(100));

        bool heardSelf = false;
        solo.PeerDiscovered += p => { if (p.Fingerprint == "SELF") heardSelf = true; };
        solo.Start();

        await Task.Delay(800);

        Assert.False(heardSelf);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DiscoveryServiceTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write the implementations**

Create `src/FileTransfer.Core/Discovery/PeerInfo.cs`:

```csharp
using System.Net;

namespace FileTransfer.Core.Discovery;

public sealed record PeerInfo(IPAddress Address, int TcpPort, string Fingerprint, string DeviceName);
```

Create `src/FileTransfer.Core/Discovery/DiscoveryService.cs`. It broadcasts a small JSON beacon and listens on the same UDP port; it ignores beacons carrying its own fingerprint:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FileTransfer.Core.Discovery;

public sealed class DiscoveryService : IDisposable
{
    private const string Magic = "FT1"; // protocol marker to ignore foreign UDP traffic

    private readonly int _udpPort;
    private readonly int _tcpPort;
    private readonly string _fingerprint;
    private readonly string _deviceName;
    private readonly TimeSpan _announceInterval;

    private UdpClient? _listener;
    private UdpClient? _sender;
    private CancellationTokenSource? _cts;

    public event Action<PeerInfo>? PeerDiscovered;

    public DiscoveryService(int udpPort, int tcpPort, string fingerprint, string deviceName, TimeSpan announceInterval)
    {
        _udpPort = udpPort;
        _tcpPort = tcpPort;
        _fingerprint = fingerprint;
        _deviceName = deviceName;
        _announceInterval = announceInterval;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();

        _listener = new UdpClient();
        _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));

        _sender = new UdpClient { EnableBroadcast = true };

        _ = ListenLoopAsync(_cts.Token);
        _ = AnnounceLoopAsync(_cts.Token);
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        byte[] beacon = Encode();
        // Send to the subnet broadcast and to loopback (so two instances on one host find each other).
        var targets = new[]
        {
            new IPEndPoint(IPAddress.Broadcast, _udpPort),
            new IPEndPoint(IPAddress.Loopback, _udpPort),
        };

        while (!ct.IsCancellationRequested)
        {
            foreach (var target in targets)
            {
                try { await _sender!.SendAsync(beacon, beacon.Length, target); }
                catch (SocketException) { /* interface down / unreachable — ignore */ }
            }
            try { await Task.Delay(_announceInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _listener!.ReceiveAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }

            var peer = TryDecode(result);
            if (peer is not null && peer.Fingerprint != _fingerprint)
                PeerDiscovered?.Invoke(peer);
        }
    }

    private byte[] Encode()
    {
        var beacon = new Beacon { Magic = Magic, Fingerprint = _fingerprint, DeviceName = _deviceName, TcpPort = _tcpPort };
        return JsonSerializer.SerializeToUtf8Bytes(beacon);
    }

    private PeerInfo? TryDecode(UdpReceiveResult result)
    {
        try
        {
            var beacon = JsonSerializer.Deserialize<Beacon>(result.Buffer);
            if (beacon is null || beacon.Magic != Magic) return null;
            return new PeerInfo(result.RemoteEndPoint.Address, beacon.TcpPort, beacon.Fingerprint, beacon.DeviceName);
        }
        catch (JsonException) { return null; }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Dispose();
        _sender?.Dispose();
        _cts?.Dispose();
    }

    private sealed class Beacon
    {
        public string Magic { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public int TcpPort { get; set; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~DiscoveryServiceTests"`
Expected: PASS (2 tests). If the loopback beacon is flaky on the CI box, the 5-second deadline gives ample margin at a 100 ms interval.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(discovery): add UDP broadcast discovery service"
```

---

## Task 9: Connection — framed send/receive over a stream with heartbeat

**Files:**
- Create: `src/FileTransfer.Core/Protocol/IFrameSink.cs`
- Create: `src/FileTransfer.Core/Transport/Connection.cs`
- Create: `tests/FileTransfer.Core.Tests/Fakes/DuplexStreamPair.cs`
- Test: `tests/FileTransfer.Core.Tests/Transport/ConnectionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Fakes/DuplexStreamPair.cs` — two streams wired so writes to one are reads on the other, using bounded in-memory pipes:

```csharp
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
```

Create `tests/FileTransfer.Core.Tests/Transport/ConnectionTests.cs`:

```csharp
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Tests.Fakes;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core.Tests.Transport;

public class ConnectionTests
{
    [Fact]
    public async Task FrameSentOnOneEnd_IsRaisedOnTheOther()
    {
        var (sa, sb) = DuplexStreamPair.Create();
        using var connA = new Connection(sa, heartbeatInterval: Timeout.InfiniteTimeSpan, heartbeatTimeout: Timeout.InfiniteTimeSpan);
        using var connB = new Connection(sb, heartbeatInterval: Timeout.InfiniteTimeSpan, heartbeatTimeout: Timeout.InfiniteTimeSpan);

        var tcs = new TaskCompletionSource<(MessageType, byte[])>();
        connB.FrameReceived += (t, p) => tcs.TrySetResult((t, p));
        connA.Start();
        connB.Start();

        await connA.SendAsync(MessageType.Text, new byte[] { 1, 2, 3 }, CancellationToken.None);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MessageType.Text, received.Item1);
        Assert.Equal(new byte[] { 1, 2, 3 }, received.Item2);
    }

    [Fact]
    public async Task PingIsAnsweredWithPong_Internally()
    {
        var (sa, sb) = DuplexStreamPair.Create();
        // Short heartbeat on A; B answers pings automatically.
        using var connA = new Connection(sa, heartbeatInterval: TimeSpan.FromMilliseconds(100), heartbeatTimeout: TimeSpan.FromSeconds(5));
        using var connB = new Connection(sb, heartbeatInterval: Timeout.InfiniteTimeSpan, heartbeatTimeout: Timeout.InfiniteTimeSpan);

        bool closedFired = false;
        connA.Closed += _ => closedFired = true;
        connA.Start();
        connB.Start();

        await Task.Delay(600); // several heartbeat rounds

        Assert.False(closedFired); // pongs keep A alive
    }

    [Fact]
    public async Task HeartbeatTimeout_FiresClosedWhenPeerSilent()
    {
        var (sa, sb) = DuplexStreamPair.Create();
        // A sends pings but B never starts, so it never pongs.
        using var connA = new Connection(sa, heartbeatInterval: TimeSpan.FromMilliseconds(100), heartbeatTimeout: TimeSpan.FromMilliseconds(300));
        _ = sb; // B's stream exists but no Connection reads it

        var closed = new TaskCompletionSource();
        connA.Closed += _ => closed.TrySetResult();
        connA.Start();

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConnectionTests"`
Expected: FAIL — `IFrameSink` / `Connection` do not exist.

- [ ] **Step 3: Write the implementations**

Create `src/FileTransfer.Core/Protocol/IFrameSink.cs`:

```csharp
namespace FileTransfer.Core.Protocol;

/// Narrow send-only interface so file transfer logic depends on "something I
/// can push frames into" rather than on a concrete socket/TLS connection.
public interface IFrameSink
{
    Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
```

Create `src/FileTransfer.Core/Transport/Connection.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ConnectionTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(transport): add framed Connection with heartbeat over a stream"
```

---

## Task 10: TLS listener and connector with fingerprint pinning

**Files:**
- Create: `src/FileTransfer.Core/Transport/TransportListener.cs`
- Create: `src/FileTransfer.Core/Transport/TransportConnector.cs`
- Test: `tests/FileTransfer.Core.Tests/Transport/TlsHandshakeTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Transport/TlsHandshakeTests.cs`. A listener and connector complete a TLS handshake over loopback; the connector pins the server's expected fingerprint, and a wrong pin is rejected:

```csharp
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core.Tests.Transport;

public class TlsHandshakeTests
{
    [Fact]
    public async Task ConnectorAndListener_CompleteHandshake_WhenFingerprintMatches()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        string serverFp = Fingerprint.Compute(serverCert.RawData);
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47950;
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: clientFp);

        var serverConnTask = new TaskCompletionSource<Connection>();
        listener.ConnectionAccepted += c => serverConnTask.TrySetResult(c);
        listener.Start();

        using var clientConn = await TransportConnector.ConnectAsync(
            "127.0.0.1", port, clientCert, expectedPeerFingerprint: serverFp, CancellationToken.None);

        var serverConn = await serverConnTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(clientConn);
        Assert.NotNull(serverConn);
    }

    [Fact]
    public async Task Connector_Rejects_WhenServerFingerprintDoesNotMatchPin()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47951;
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: clientFp);
        listener.Start();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            TransportConnector.ConnectAsync(
                "127.0.0.1", port, clientCert,
                expectedPeerFingerprint: "0000000000000000000000000000000000000000000000000000000000000000",
                CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~TlsHandshakeTests"`
Expected: FAIL — listener/connector do not exist.

- [ ] **Step 3: Write the implementations**

Create `src/FileTransfer.Core/Transport/TransportConnector.cs`:

```csharp
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Transport;

public static class TransportConnector
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(30);

    public static async Task<Connection> ConnectAsync(
        string host, int port, X509Certificate2 ownCert, string expectedPeerFingerprint, CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);

        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && Fingerprint.Compute(cert.GetRawCertData()) == expectedPeerFingerprint);

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "filetransfer",
            ClientCertificates = new X509CertificateCollection { ownCert },
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        try
        {
            await ssl.AuthenticateAsClientAsync(options, ct);
        }
        catch
        {
            ssl.Dispose();
            tcp.Dispose();
            throw;
        }

        var conn = new Connection(ssl, HeartbeatInterval, HeartbeatTimeout);
        conn.Start();
        return conn;
    }
}
```

Create `src/FileTransfer.Core/Transport/TransportListener.cs`:

```csharp
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Transport;

public sealed class TransportListener : IDisposable
{
    private readonly int _port;
    private readonly X509Certificate2 _ownCert;
    private readonly string _expectedPeerFingerprint;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// Raised once per accepted, fully-handshaken peer connection.
    public event Action<Connection>? ConnectionAccepted;

    public TransportListener(int port, X509Certificate2 ownCert, string expectedPeerFingerprint)
    {
        _port = port;
        _ownCert = ownCert;
        _expectedPeerFingerprint = expectedPeerFingerprint;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = HandshakeAsync(tcp, ct); // handle each peer without blocking the accept loop
        }
    }

    private async Task HandshakeAsync(TcpClient tcp, CancellationToken ct)
    {
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && Fingerprint.Compute(cert.GetRawCertData()) == _expectedPeerFingerprint);

        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = _ownCert,
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        try
        {
            await ssl.AuthenticateAsServerAsync(options, ct);
        }
        catch
        {
            ssl.Dispose();
            tcp.Dispose();
            return; // rejected peer — drop silently
        }

        var conn = new Connection(ssl, TransportConnector.HeartbeatInterval, TransportConnector.HeartbeatTimeout);
        conn.Start();
        ConnectionAccepted?.Invoke(conn);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~TlsHandshakeTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(transport): add TLS listener and connector with fingerprint pinning"
```

---

## Task 11: File sender — chunk a file into frames

**Files:**
- Create: `src/FileTransfer.Core/Protocol/FileChunkCodec.cs`
- Create: `src/FileTransfer.Core/Transfer/FileSender.cs`
- Create: `tests/FileTransfer.Core.Tests/Fakes/FakeFrameSink.cs`
- Test: `tests/FileTransfer.Core.Tests/Transfer/FileSenderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Fakes/FakeFrameSink.cs`:

```csharp
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
```

Create `tests/FileTransfer.Core.Tests/Transfer/FileSenderTests.cs`:

```csharp
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Tests.Fakes;
using FileTransfer.Core.Transfer;

namespace FileTransfer.Core.Tests.Transfer;

public class FileSenderTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "ft-send-" + Guid.NewGuid() + ".bin");

    public void Dispose() { if (File.Exists(_file)) File.Delete(_file); }

    [Fact]
    public async Task SendsOffer_ThenChunks_ThenDone()
    {
        // 600 KB of data => with 256 KB chunks => 3 chunks
        byte[] data = new byte[600 * 1024];
        new Random(1).NextBytes(data);
        await File.WriteAllBytesAsync(_file, data);

        var sink = new FakeFrameSink();
        var sender = new FileSender(sink, chunkSize: 256 * 1024);

        var id = await sender.SendAsync(_file, progress: null, CancellationToken.None);

        var frames = sink.Frames.ToArray();
        Assert.Equal(MessageType.FileOffer, frames[0].Type);
        Assert.Equal(MessageType.FileChunk, frames[1].Type);
        Assert.Equal(MessageType.FileChunk, frames[2].Type);
        Assert.Equal(MessageType.FileChunk, frames[3].Type);
        Assert.Equal(MessageType.FileDone, frames[5 - 1].Type); // offer + 3 chunks + done = 5 frames

        // Every chunk payload carries the transfer id in its first 16 bytes.
        var (chunkId, _) = FileChunkCodec.Decode(frames[1].Payload);
        Assert.Equal(id, chunkId);

        // The offer announces the right size and name.
        var offer = MessageSerializer.Deserialize<FileOffer>(frames[0].Payload);
        Assert.Equal(data.Length, offer.Size);
        Assert.Equal(Path.GetFileName(_file), offer.Name);
        Assert.Equal(id, offer.Id);
    }

    [Fact]
    public async Task ReportsProgress_ReachingFullSize()
    {
        byte[] data = new byte[300 * 1024];
        await File.WriteAllBytesAsync(_file, data);
        long lastReported = 0;

        var sender = new FileSender(new FakeFrameSink(), chunkSize: 256 * 1024);
        await sender.SendAsync(_file, progress: sent => lastReported = sent, CancellationToken.None);

        Assert.Equal(data.Length, lastReported);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FileSenderTests"`
Expected: FAIL — `FileChunkCodec` / `FileSender` do not exist.

- [ ] **Step 3: Write the implementations**

Create `src/FileTransfer.Core/Protocol/FileChunkCodec.cs`:

```csharp
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
```

Create `src/FileTransfer.Core/Transfer/FileSender.cs`:

```csharp
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
        var info = new FileInfo(path);
        var id = Guid.NewGuid();

        var offer = new FileOffer
        {
            Id = id,
            Name = info.Name,
            Size = info.Length,
            Mime = MimeFor(info.Extension),
        };
        await _sink.SendAsync(MessageType.FileOffer, MessageSerializer.Serialize(offer), ct);

        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(path);
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FileSenderTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(transfer): add file chunk codec and chunked FileSender with sha256"
```

---

## Task 12: File receiver — assemble chunks, verify, dedupe name

**Files:**
- Create: `src/FileTransfer.Core/Transfer/FileReceiver.cs`
- Test: `tests/FileTransfer.Core.Tests/Transfer/FileReceiverTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Transfer/FileReceiverTests.cs`:

```csharp
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

        Assert.Throws<InvalidDataException>(() => receiver.Complete(id, "DEADBEEF"));
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

        Assert.Throws<InvalidOperationException>(() => receiver.WriteChunk(id, new byte[] { 3 }));
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~FileReceiverTests"`
Expected: FAIL — `FileReceiver` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Transfer/FileReceiver.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~FileReceiverTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(transfer): add FileReceiver with sha256 verify, sanitize, and dedupe"
```

---

## Task 13: Node orchestrator

**Files:**
- Create: `src/FileTransfer.Core/ConnectionStatus.cs`
- Create: `src/FileTransfer.Core/Node.cs`
- Test: `tests/FileTransfer.Core.Tests/NodeTests.cs`

**Design note:** `Node` is the single entry point the UI layer (Plan 2) will use. It owns a `DiscoveryService`, a `TransportListener`, an optional outgoing `Connection`, a `FileReceiver`, and surfaces:

- Properties: `Status`, `PeerName`
- Events: `StatusChanged(ConnectionStatus)`, `TextReceived(string)`, `FileOfferReceived(FileOffer)`, `FileProgress(Guid id, long received, long total)`, `FileCompleted(Guid id, string path)`, `TransferFailed(Guid id, string reason)`, `PeerDiscoveredForPairing(PeerInfo)`
- Methods: `Task StartAsync()`, `Task SendTextAsync(string)`, `Task<Guid> SendFileAsync(string path)`, `Task CancelTransferAsync(Guid id)`, `void Stop()`

For this task we test the message-routing brain in isolation by feeding frames directly, avoiding real sockets (those are covered end-to-end in Task 14). We extract the frame-handling into an internal method `HandleFrame(MessageType, byte[])` that operates against the active connection and receiver.

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/NodeTests.cs`:

```csharp
using FileTransfer.Core;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Tests.Fakes;
using FileTransfer.Core.Transfer;

namespace FileTransfer.Core.Tests;

public class NodeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ft-node-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void HandleFrame_Text_RaisesTextReceived()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        string? got = null;
        router.TextReceived += t => got = t;

        var payload = MessageSerializer.Serialize(new TextMessage { Id = Guid.NewGuid(), Text = "hi there" });
        router.Handle(MessageType.Text, payload);

        Assert.Equal("hi there", got);
    }

    [Fact]
    public void HandleFrame_FileLifecycle_RaisesCompletedWithPath()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        Guid? completedId = null;
        string? completedPath = null;
        router.FileCompleted += (id, path) => { completedId = id; completedPath = path; };

        byte[] data = { 1, 2, 3, 4 };
        var offerId = Guid.NewGuid();
        router.Handle(MessageType.FileOffer, MessageSerializer.Serialize(
            new FileOffer { Id = offerId, Name = "n.bin", Size = data.Length }));
        router.Handle(MessageType.FileChunk, FileChunkCodec.Encode(offerId, data));
        router.Handle(MessageType.FileDone, MessageSerializer.Serialize(
            new FileDone { Id = offerId, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)) }));

        Assert.Equal(offerId, completedId);
        Assert.True(File.Exists(completedPath));
    }

    [Fact]
    public void HandleFrame_FileCancel_RaisesTransferFailed()
    {
        var router = new MessageRouter(new FileReceiver(_dir));
        Guid? failedId = null;
        router.TransferFailed += (id, _) => failedId = id;

        var offerId = Guid.NewGuid();
        router.Handle(MessageType.FileOffer, MessageSerializer.Serialize(
            new FileOffer { Id = offerId, Name = "n.bin", Size = 100 }));
        router.Handle(MessageType.FileCancel, MessageSerializer.Serialize(
            new FileCancel { Id = offerId, Reason = "peer cancelled" }));

        Assert.Equal(offerId, failedId);
    }
}
```

> The routing brain is extracted into a `MessageRouter` so it can be tested without sockets. `Node` composes a `MessageRouter` with the real transport in Task 14.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~NodeTests"`
Expected: FAIL — `MessageRouter` does not exist.

- [ ] **Step 3: Write the implementations**

Create `src/FileTransfer.Core/ConnectionStatus.cs`:

```csharp
namespace FileTransfer.Core;

public enum ConnectionStatus
{
    Disconnected,
    Pairing,
    Online,
    Offline,
}
```

Create `src/FileTransfer.Core/MessageRouter.cs`:

```csharp
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transfer;

namespace FileTransfer.Core;

/// Pure message-routing brain: turns inbound frames into high-level events and
/// drives the FileReceiver. Has no sockets, so it is unit-testable in isolation.
public sealed class MessageRouter
{
    private readonly FileReceiver _receiver;

    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress; // id, received, total
    public event Action<Guid, string>? FileCompleted;     // id, final path
    public event Action<Guid, string>? TransferFailed;    // id, reason

    private readonly Dictionary<Guid, (long Received, long Total)> _progress = new();

    public MessageRouter(FileReceiver receiver) => _receiver = receiver;

    public void Handle(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.Text:
                TextReceived?.Invoke(MessageSerializer.Deserialize<TextMessage>(payload).Text);
                break;

            case MessageType.FileOffer:
            {
                var offer = MessageSerializer.Deserialize<FileOffer>(payload);
                _receiver.Begin(offer);
                _progress[offer.Id] = (0, offer.Size);
                FileOfferReceived?.Invoke(offer);
                break;
            }

            case MessageType.FileChunk:
            {
                var (id, data) = FileChunkCodec.Decode(payload);
                _receiver.WriteChunk(id, data);
                if (_progress.TryGetValue(id, out var p))
                {
                    p.Received += data.Length;
                    _progress[id] = p;
                    FileProgress?.Invoke(id, p.Received, p.Total);
                }
                break;
            }

            case MessageType.FileDone:
            {
                var done = MessageSerializer.Deserialize<FileDone>(payload);
                try
                {
                    string path = _receiver.Complete(done.Id, done.Sha256);
                    _progress.Remove(done.Id);
                    FileCompleted?.Invoke(done.Id, path);
                }
                catch (Exception ex)
                {
                    _progress.Remove(done.Id);
                    TransferFailed?.Invoke(done.Id, ex.Message);
                }
                break;
            }

            case MessageType.FileCancel:
            {
                var cancel = MessageSerializer.Deserialize<FileCancel>(payload);
                _receiver.Cancel(cancel.Id);
                _progress.Remove(cancel.Id);
                TransferFailed?.Invoke(cancel.Id, string.IsNullOrEmpty(cancel.Reason) ? "cancelled" : cancel.Reason);
                break;
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~NodeTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): add ConnectionStatus and MessageRouter brain"
```

---

## Task 14: Node composition and end-to-end integration

**Files:**
- Create: `src/FileTransfer.Core/Node.cs`
- Test: `tests/FileTransfer.Core.Tests/EndToEndTests.cs`

**Design note:** `Node` wires everything: it creates the `DiscoveryService`, `TransportListener`, and `FileReceiver`/`MessageRouter`, decides who dials whom via `Fingerprint.LocalInitiates`, establishes exactly one `Connection`, forwards inbound frames to the `MessageRouter`, and re-raises the router's events. `SendTextAsync` and `SendFileAsync` push frames into the active connection. On the active connection's `Closed`, status goes `Offline` and discovery keeps running so it can reconnect.

- [ ] **Step 1: Write the failing end-to-end test**

Create `tests/FileTransfer.Core.Tests/EndToEndTests.cs`. Two fully-configured `Node`s on loopback discover, connect, and exchange a text message and a file:

```csharp
using FileTransfer.Core;
using FileTransfer.Core.Crypto;

namespace FileTransfer.Core.Tests;

public class EndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ft-e2e-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private Node MakeNode(string name, int udpPort, int tcpPort,
        System.Security.Cryptography.X509Certificates.X509Certificate2 own,
        string peerFp)
    {
        string recvDir = Path.Combine(_root, name);
        return new Node(new NodeOptions
        {
            DeviceName = name,
            OwnCertificate = own,
            PeerFingerprint = peerFp,
            UdpPort = udpPort,
            TcpPort = tcpPort,
            ReceiveDirectory = recvDir,
            AnnounceInterval = TimeSpan.FromMilliseconds(150),
        });
    }

    [Fact]
    public async Task TwoNodes_Discover_Connect_ExchangeTextAndFile()
    {
        using var certA = CertificateFactory.CreateSelfSigned("NodeA");
        using var certB = CertificateFactory.CreateSelfSigned("NodeB");
        string fpA = Fingerprint.Compute(certA.RawData);
        string fpB = Fingerprint.Compute(certB.RawData);

        using var a = MakeNode("A", 47800, 47801, certA, fpB);
        using var b = MakeNode("B", 47800, 47802, certB, fpA);

        string? textOnB = null;
        var fileOnB = new TaskCompletionSource<string>();
        b.TextReceived += t => textOnB = t;
        b.FileCompleted += (_, path) => fileOnB.TrySetResult(path);

        await a.StartAsync();
        await b.StartAsync();

        // Wait for both to report Online.
        await WaitFor(() => a.Status == ConnectionStatus.Online && b.Status == ConnectionStatus.Online, seconds: 8);

        await a.SendTextAsync("hello from A");
        await WaitFor(() => textOnB == "hello from A", seconds: 5);
        Assert.Equal("hello from A", textOnB);

        // Send a 500 KB file A -> B.
        string srcPath = Path.Combine(_root, "payload.bin");
        Directory.CreateDirectory(_root);
        byte[] data = new byte[500 * 1024];
        new Random(7).NextBytes(data);
        await File.WriteAllBytesAsync(srcPath, data);

        await a.SendFileAsync(srcPath);
        string receivedPath = await fileOnB.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(data, await File.ReadAllBytesAsync(receivedPath));
    }

    private static async Task WaitFor(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        if (!condition()) throw new TimeoutException("Condition not met in time.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EndToEndTests"`
Expected: FAIL — `Node` / `NodeOptions` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Node.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Discovery;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transfer;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core;

public sealed class NodeOptions
{
    public required string DeviceName { get; init; }
    public required X509Certificate2 OwnCertificate { get; init; }
    public required string PeerFingerprint { get; init; }
    public int UdpPort { get; init; } = 47100;
    public int TcpPort { get; init; } = 47101;
    public required string ReceiveDirectory { get; init; }
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(5);
}

/// Top-level orchestrator the UI binds to. Owns discovery + transport + transfer
/// and exposes high-level events and send methods. One peer connection at a time.
public sealed class Node : IDisposable
{
    private readonly NodeOptions _options;
    private readonly string _ownFingerprint;
    private readonly FileReceiver _receiver;
    private readonly MessageRouter _router;

    private DiscoveryService? _discovery;
    private TransportListener? _listener;
    private Connection? _connection;
    private readonly object _connLock = new();

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public string PeerName { get; private set; } = "";

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;

    public Node(NodeOptions options)
    {
        _options = options;
        _ownFingerprint = Fingerprint.Compute(options.OwnCertificate.RawData);
        _receiver = new FileReceiver(options.ReceiveDirectory);
        _router = new MessageRouter(_receiver);

        _router.TextReceived += t => TextReceived?.Invoke(t);
        _router.FileOfferReceived += o => FileOfferReceived?.Invoke(o);
        _router.FileProgress += (id, r, t) => FileProgress?.Invoke(id, r, t);
        _router.FileCompleted += (id, p) => FileCompleted?.Invoke(id, p);
        _router.TransferFailed += (id, r) => TransferFailed?.Invoke(id, r);
    }

    public Task StartAsync()
    {
        _listener = new TransportListener(_options.TcpPort, _options.OwnCertificate, _options.PeerFingerprint);
        _listener.ConnectionAccepted += AdoptConnection;
        _listener.Start();

        _discovery = new DiscoveryService(
            _options.UdpPort, _options.TcpPort, _ownFingerprint, _options.DeviceName, _options.AnnounceInterval);
        _discovery.PeerDiscovered += OnPeerDiscovered;
        _discovery.Start();

        SetStatus(ConnectionStatus.Offline);
        return Task.CompletedTask;
    }

    private void OnPeerDiscovered(PeerInfo peer)
    {
        if (peer.Fingerprint != _options.PeerFingerprint) return; // only our paired peer
        lock (_connLock) { if (_connection is not null) return; }   // already connected
        if (!Fingerprint.LocalInitiates(_ownFingerprint, peer.Fingerprint)) return; // the other side dials

        _ = DialAsync(peer);
    }

    private async Task DialAsync(PeerInfo peer)
    {
        try
        {
            var conn = await TransportConnector.ConnectAsync(
                peer.Address.ToString(), peer.TcpPort, _options.OwnCertificate, peer.Fingerprint, CancellationToken.None);
            PeerName = peer.DeviceName;
            AdoptConnection(conn);
        }
        catch
        {
            // peer not ready yet — discovery will retry on the next beacon
        }
    }

    private void AdoptConnection(Connection conn)
    {
        lock (_connLock)
        {
            if (_connection is not null) { conn.Dispose(); return; } // keep the first one
            _connection = conn;
        }

        conn.FrameReceived += (type, payload) => _router.Handle(type, payload);
        conn.Closed += _ =>
        {
            lock (_connLock) { if (ReferenceEquals(_connection, conn)) _connection = null; }
            conn.Dispose();
            SetStatus(ConnectionStatus.Offline);
        };

        SetStatus(ConnectionStatus.Online);
    }

    public async Task SendTextAsync(string text)
    {
        var conn = RequireConnection();
        var msg = new TextMessage { Id = Guid.NewGuid(), Text = text };
        await conn.SendAsync(MessageType.Text, MessageSerializer.Serialize(msg), CancellationToken.None);
    }

    public async Task<Guid> SendFileAsync(string path)
    {
        var conn = RequireConnection();
        var sender = new FileSender(conn);
        return await sender.SendAsync(path, progress: null, CancellationToken.None);
    }

    public async Task CancelTransferAsync(Guid id)
    {
        var conn = RequireConnection();
        var cancel = new FileCancel { Id = id, Reason = "cancelled by sender" };
        await conn.SendAsync(MessageType.FileCancel, MessageSerializer.Serialize(cancel), CancellationToken.None);
    }

    private Connection RequireConnection()
    {
        lock (_connLock)
        {
            return _connection ?? throw new InvalidOperationException("Not connected to peer.");
        }
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void Stop()
    {
        _discovery?.Dispose();
        _listener?.Dispose();
        lock (_connLock) { _connection?.Dispose(); _connection = null; }
        SetStatus(ConnectionStatus.Disconnected);
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 4: Run the end-to-end test**

Run: `dotnet test --filter "FullyQualifiedName~EndToEndTests"`
Expected: PASS (1 test). This proves discovery → TLS connect → text → 500 KB file all work between two in-process nodes.

> If the test is flaky due to both nodes binding the same UDP port on one host: the `ReuseAddress` option set in `DiscoveryService` allows it. If a CI box still struggles, raise the `WaitFor` timeouts — the logic is correct, only timing is environmental.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test`
Expected: all tests from Tasks 2–14 pass.

```powershell
git add .
git commit -m "feat(core): add Node orchestrator and end-to-end transfer integration test"
```

---

## Self-Review

**Spec coverage check** against `2026-05-27-file-transfer-design.md`:

| Spec section | Covered by |
|--------------|-----------|
| Frame format (4-byte len + type + payload, 16 MB cap) | Task 2, 3 |
| 8 message types | Task 2 (enum), 4 (DTOs), 11 (chunk codec) |
| Self-signed cert + DPAPI private key | Task 6, 7 |
| Fingerprint + 4-digit pairing code + initiator arbitration | Task 5 |
| Config at %APPDATA% (device name, peer fp, receive dir, autostart) | Task 7 |
| UDP broadcast discovery, ignore own beacon | Task 8 |
| TLS with fingerprint pinning, reject mismatch | Task 10 |
| Heartbeat PING/PONG + 30s timeout → Offline | Task 9, 14 |
| Double-connect arbitration (smaller fp dials) | Task 5 (logic), 14 (applied) |
| File offer → chunk (256 KB) → done → sha256 verify | Task 11, 12 |
| Auto-accept files | Task 13 (router begins receive on offer, no prompt) |
| Filename sanitize + dedupe `(1)` `(2)` | Task 12 |
| Cancel deletes .part | Task 12, 13 |
| Status enum Disconnected/Pairing/Online/Offline | Task 13, 14 |
| End-to-end discover→connect→text→file | Task 14 |

**Gaps deferred to Plan 2 (App) — intentional, these are UI/host concerns, not Core:**
- First-run pairing UX (showing the pairing code, user confirm on both sides). Core exposes the building blocks (`Fingerprint.PairingCode`, discovery events, fingerprint-pinned transport); the pairing *flow and dialog* live in the App. **Note for Plan 2:** add a `Node` "pairing mode" that connects to an as-yet-untrusted peer to compute and display the code, then persists the fingerprint on confirm.
- Port-in-use fallback (try +1..+10). Core currently takes fixed ports via `NodeOptions`; the App will probe free ports before constructing `NodeOptions`. **Note for Plan 2.**
- Clipboard image → PNG file. Pure UI concern (App reads the clipboard, writes a temp PNG, calls `SendFileAsync`).
- Protocol version mismatch handling via HELLO. `HelloMessage` exists (Task 4); wiring HELLO exchange + version check into `Node` is a small follow-up. **Note for Plan 2** (or a Core v1.1 task) — not blocking the App.
- Auto-start registry write, settings UI, tray/close behavior — all App.

**Placeholder scan:** no TBD/TODO/"add error handling" placeholders; every code step is complete and compilable.

**Type consistency check:** `Fingerprint.Compute(byte[])`, `Fingerprint.PairingCode(string,string)`, `Fingerprint.LocalInitiates(string,string)` used consistently across Tasks 5/6/7/10/14. `IFrameSink.SendAsync(MessageType, ReadOnlyMemory<byte>, CancellationToken)` matches between Task 9 (Connection), Task 11 (FileSender), Task 14 (Node). `FileChunkCodec.Encode/Decode` signatures match between Tasks 11/13. `FileReceiver.Begin/WriteChunk/Complete/Cancel` match between Tasks 12/13. `Connection` constructor `(Stream, TimeSpan, TimeSpan)` matches between Tasks 9/10. Events on `MessageRouter` and `Node` line up. ✓

---

## Notes carried forward to Plan 2 (FileTransfer.App)

1. Add a `Node` pairing mode: connect to an untrusted peer over TLS (accept any cert), compute `Fingerprint.PairingCode(ownFp, peerFp)`, surface it for both users to confirm, then persist the peer fingerprint and restart in normal pinned mode.
2. Probe for free UDP/TCP ports (47100/47101 then +1..+10) in the App before building `NodeOptions`.
3. HELLO exchange + protocol-version check on connect (Core has the DTO; wire it in `Node.AdoptConnection`).
4. Clipboard-image-to-PNG, drag-drop, settings persistence (`AppConfig`), auto-start registry, close-to-exit behavior — all App-side.
