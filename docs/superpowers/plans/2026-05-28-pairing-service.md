# PairingService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the headless `PairingService` module that lets two unpaired Windows machines on a LAN complete the first-time pairing flow (discover → unpinned TLS → 4-digit code → mutual confirm → persist peer fingerprint), proven end-to-end by loopback xUnit tests.

**Architecture:** A new `Pairing/` namespace inside `FileTransfer.Core` containing `PairingService` (orchestrator analogous to `Node`) plus supporting data types. Reuses the existing `DiscoveryService` for UDP beacons, adds an **unpinned mode** to `TransportListener` / `TransportConnector` so TLS can complete without a known peer fingerprint, and introduces two new `MessageType` values (`PairingConfirm`, `PairingReject`) for the two-sided confirmation handshake. Single-active-session, with a deterministic fingerprint-based tiebreaker for the rare both-sides-dial-at-once race. State machine: `Idle → Negotiating → AwaitingDecision → Completed/Failed`.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, xUnit, `System.Net.Security.SslStream`, `System.Security.Cryptography.X509Certificates` — all already in use.

---

## File Structure

```
src/FileTransfer.Core/
  Pairing/                              ← new module
    PairingState.cs                     enum: Idle / Negotiating / AwaitingDecision / Completed / Failed
    PairingFailureReason.cs             enum: LocallyRejected / PeerRejected / LocalTimeout /
                                              TlsHandshakeFailed / ConnectionLost / ProtocolMismatch
    PeerCandidate.cs                    record: Address, TcpPort, Fingerprint, DeviceName
    PairingResult.cs                    record: PeerFingerprint, PeerDeviceName
    PairingServiceOptions.cs            DeviceName, OwnCertificate, ports, intervals
    PairingService.cs                   the orchestrator
  Protocol/
    MessageType.cs                      add PairingConfirm = 0x02, PairingReject = 0x03
  Transport/
    Connection.cs                       add `PeerFingerprint` property (constructor param)
    TransportListener.cs                allow expectedPeerFingerprint to be null
    TransportConnector.cs               allow expectedPeerFingerprint to be null

tests/FileTransfer.Core.Tests/
  Pairing/
    PairingServiceTests.cs              loopback end-to-end
  Transport/
    TlsHandshakeTests.cs                add unpinned-mode test
```

**Boundary rationale:** PairingService is parallel to `Node`, not nested in it. Both can be constructed with the same `X509Certificate2`; the UI swaps one for the other after the user confirms pairing. Reusing `DiscoveryService` and the existing TLS machinery means the new code is the state machine + the two new message types, nothing more. Tests use real loopback sockets exactly like `EndToEndTests`.

---

## Task 1: Add `PairingConfirm` and `PairingReject` message types

**Files:**
- Modify: `src/FileTransfer.Core/Protocol/MessageType.cs`
- Test: `tests/FileTransfer.Core.Tests/Protocol/MessageTypeTests.cs` (new file — none exists yet)

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Protocol/MessageTypeTests.cs`:

```csharp
using FileTransfer.Core.Protocol;

namespace FileTransfer.Core.Tests.Protocol;

public class MessageTypeTests
{
    [Fact]
    public void PairingConfirm_HasReservedByteValue()
    {
        Assert.Equal((byte)0x02, (byte)MessageType.PairingConfirm);
    }

    [Fact]
    public void PairingReject_HasReservedByteValue()
    {
        Assert.Equal((byte)0x03, (byte)MessageType.PairingReject);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MessageTypeTests"`
Expected: FAIL — `MessageType.PairingConfirm` does not exist.

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Protocol/MessageType.cs` to add the two values in the reserved 0x02-0x0F range:

```csharp
namespace FileTransfer.Core.Protocol;

public enum MessageType : byte
{
    Hello = 0x01,
    PairingConfirm = 0x02,
    PairingReject = 0x03,
    Text = 0x10,
    FileOffer = 0x20,
    FileChunk = 0x21,
    FileDone = 0x22,
    FileCancel = 0x23,
    Ping = 0xF0,
    Pong = 0xF1,
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~MessageTypeTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(protocol): add PairingConfirm and PairingReject message types"
```

---

## Task 2: Expose `Connection.PeerFingerprint` property

**Files:**
- Modify: `src/FileTransfer.Core/Transport/Connection.cs`
- Test: `tests/FileTransfer.Core.Tests/Transport/ConnectionTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `tests/FileTransfer.Core.Tests/Transport/ConnectionTests.cs` (inside the existing `ConnectionTests` class):

```csharp
    [Fact]
    public void PeerFingerprint_IsNull_WhenNotProvided()
    {
        var (sa, _) = DuplexStreamPair.Create();
        using var conn = new Connection(sa, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Assert.Null(conn.PeerFingerprint);
    }

    [Fact]
    public void PeerFingerprint_ReturnsConstructorValue_WhenProvided()
    {
        var (sa, _) = DuplexStreamPair.Create();
        using var conn = new Connection(
            sa, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan,
            peerFingerprint: "DEADBEEF");
        Assert.Equal("DEADBEEF", conn.PeerFingerprint);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConnectionTests.PeerFingerprint"`
Expected: FAIL — `PeerFingerprint` property and overload do not exist.

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Transport/Connection.cs`. Add an optional constructor parameter and a get-only property; existing call sites continue to compile because the parameter is optional:

```csharp
public sealed class Connection : IFrameSink, IDisposable
{
    // ... existing fields ...

    /// SHA256 fingerprint (uppercase hex) of the peer's TLS certificate, populated by
    /// the listener/connector after a successful handshake. Null if the connection
    /// was constructed without it (e.g. in unit tests that bypass TLS).
    public string? PeerFingerprint { get; }

    // ... existing event declarations ...

    public Connection(
        Stream stream,
        TimeSpan heartbeatInterval,
        TimeSpan heartbeatTimeout,
        string? peerFingerprint = null)
    {
        _stream = stream;
        _reader = new FrameReader(stream);
        _heartbeatInterval = heartbeatInterval;
        _heartbeatTimeout = heartbeatTimeout;
        PeerFingerprint = peerFingerprint;
    }

    // ... rest unchanged ...
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ConnectionTests"`
Expected: PASS (all existing + 2 new).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(transport): expose Connection.PeerFingerprint property"
```

---

## Task 3: `TransportListener` unpinned mode

**Files:**
- Modify: `src/FileTransfer.Core/Transport/TransportListener.cs`
- Test: `tests/FileTransfer.Core.Tests/Transport/TlsHandshakeTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `tests/FileTransfer.Core.Tests/Transport/TlsHandshakeTests.cs`:

```csharp
    [Fact]
    public async Task UnpinnedListener_AcceptsAnyClient_AndPopulatesPeerFingerprint()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        string serverFp = Fingerprint.Compute(serverCert.RawData);
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47960;
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: null);

        var serverConnTask = new TaskCompletionSource<Connection>();
        listener.ConnectionAccepted += c => serverConnTask.TrySetResult(c);
        listener.Start();

        using var clientConn = await TransportConnector.ConnectAsync(
            "127.0.0.1", port, clientCert, expectedPeerFingerprint: serverFp, CancellationToken.None);

        var serverConn = await serverConnTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(clientFp, serverConn.PeerFingerprint);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UnpinnedListener_AcceptsAnyClient"`
Expected: FAIL — passing `null` to the listener's `expectedPeerFingerprint` is currently a type error.

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Transport/TransportListener.cs` to make `expectedPeerFingerprint` nullable, short-circuit the validation callback when it's null, and always populate `Connection.PeerFingerprint`:

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
    private readonly string? _expectedPeerFingerprint;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private X509Certificate2? _tlsCert;

    public event Action<Connection>? ConnectionAccepted;

    /// `expectedPeerFingerprint` null means unpinned: any well-formed client cert is
    /// accepted and its fingerprint is exposed via Connection.PeerFingerprint for the
    /// caller (PairingService) to validate at the application layer.
    public TransportListener(int port, X509Certificate2 ownCert, string? expectedPeerFingerprint)
    {
        _port = port;
        _ownCert = ownCert;
        _expectedPeerFingerprint = expectedPeerFingerprint;
    }

    public void Start()
    {
        if (_tlsCert is not null)
            throw new InvalidOperationException("Listener is already started.");
        _tlsCert = CertificateFactory.MakeTlsReady(_ownCert);
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

            _ = HandshakeAsync(tcp, ct);
        }
    }

    private async Task HandshakeAsync(TcpClient tcp, CancellationToken ct)
    {
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
            {
                if (cert is null) return false;
                // Unpinned: accept any cert; we still record its fingerprint below.
                if (_expectedPeerFingerprint is null) return true;
                return Fingerprint.Compute(cert.GetRawCertData()) == _expectedPeerFingerprint;
            });

        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = _tlsCert,
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
            return;
        }

        // Always populate PeerFingerprint so callers (Node, PairingService) can read it
        // uniformly without caring whether the listener was pinned or not.
        string? peerFp = ssl.RemoteCertificate is { } rc
            ? Fingerprint.Compute(rc.GetRawCertData())
            : null;

        var conn = new Connection(
            ssl, TransportConnector.HeartbeatInterval, TransportConnector.HeartbeatTimeout,
            peerFingerprint: peerFp);
        conn.Start();
        ConnectionAccepted?.Invoke(conn);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
        _tlsCert?.Dispose();
    }
}
```

- [ ] **Step 4: Run all transport tests to verify nothing regressed**

Run: `dotnet test --filter "FullyQualifiedName~Transport"`
Expected: PASS (all existing + 1 new).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(transport): support unpinned TLS listener for pairing"
```

---

## Task 4: `TransportConnector` unpinned mode

**Files:**
- Modify: `src/FileTransfer.Core/Transport/TransportConnector.cs`
- Test: `tests/FileTransfer.Core.Tests/Transport/TlsHandshakeTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `tests/FileTransfer.Core.Tests/Transport/TlsHandshakeTests.cs`:

```csharp
    [Fact]
    public async Task UnpinnedConnector_PopulatesPeerFingerprint_WithRealServerCert()
    {
        using var serverCert = CertificateFactory.CreateSelfSigned("Server");
        using var clientCert = CertificateFactory.CreateSelfSigned("Client");
        string serverFp = Fingerprint.Compute(serverCert.RawData);
        string clientFp = Fingerprint.Compute(clientCert.RawData);

        int port = 47961;
        // Listener also unpinned so the test isolates the connector's behaviour.
        using var listener = new TransportListener(port, serverCert, expectedPeerFingerprint: null);
        listener.Start();

        using var clientConn = await TransportConnector.ConnectAsync(
            "127.0.0.1", port, clientCert, expectedPeerFingerprint: null, CancellationToken.None);

        Assert.Equal(serverFp, clientConn.PeerFingerprint);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~UnpinnedConnector_PopulatesPeerFingerprint"`
Expected: FAIL — `ConnectAsync` rejects `null` (current signature is `string`).

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Transport/TransportConnector.cs`:

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

    /// `expectedPeerFingerprint` null means unpinned: any well-formed server cert is
    /// accepted and its fingerprint is exposed via Connection.PeerFingerprint for the
    /// caller (PairingService) to validate at the application layer.
    public static async Task<Connection> ConnectAsync(
        string host, int port, X509Certificate2 ownCert, string? expectedPeerFingerprint, CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);

        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
            {
                if (cert is null) return false;
                if (expectedPeerFingerprint is null) return true;
                return Fingerprint.Compute(cert.GetRawCertData()) == expectedPeerFingerprint;
            });

        var clientCert = CertificateFactory.MakeTlsReady(ownCert);

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = "filetransfer",
            ClientCertificates = new X509CertificateCollection { clientCert },
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
            clientCert.Dispose();
            throw;
        }

        string? peerFp = ssl.RemoteCertificate is { } rc
            ? Fingerprint.Compute(rc.GetRawCertData())
            : null;

        var conn = new Connection(
            ssl, HeartbeatInterval, HeartbeatTimeout, peerFingerprint: peerFp);
        conn.Closed += _ => clientCert.Dispose();
        conn.Start();
        return conn;
    }
}
```

- [ ] **Step 4: Run all transport tests**

Run: `dotnet test --filter "FullyQualifiedName~Transport"`
Expected: PASS (all existing + 1 new).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(transport): support unpinned TLS connector for pairing"
```

---

## Task 5: Pairing module data types

**Files:**
- Create: `src/FileTransfer.Core/Pairing/PairingState.cs`
- Create: `src/FileTransfer.Core/Pairing/PairingFailureReason.cs`
- Create: `src/FileTransfer.Core/Pairing/PeerCandidate.cs`
- Create: `src/FileTransfer.Core/Pairing/PairingResult.cs`
- Create: `src/FileTransfer.Core/Pairing/PairingServiceOptions.cs`

These are pure data types — no behaviour to test directly. The next task's PairingService test exercises them. So this task has no per-step TDD; it lays the foundation in a single commit.

- [ ] **Step 1: Create the enums**

Create `src/FileTransfer.Core/Pairing/PairingState.cs`:

```csharp
namespace FileTransfer.Core.Pairing;

public enum PairingState
{
    Idle,
    Negotiating,
    AwaitingDecision,
    Completed,
    Failed,
}
```

Create `src/FileTransfer.Core/Pairing/PairingFailureReason.cs`:

```csharp
namespace FileTransfer.Core.Pairing;

public enum PairingFailureReason
{
    LocallyRejected,
    PeerRejected,
    LocalTimeout,
    TlsHandshakeFailed,
    ConnectionLost,
    ProtocolMismatch,
}
```

- [ ] **Step 2: Create the records**

Create `src/FileTransfer.Core/Pairing/PeerCandidate.cs`:

```csharp
using System.Net;

namespace FileTransfer.Core.Pairing;

public sealed record PeerCandidate(IPAddress Address, int TcpPort, string Fingerprint, string DeviceName);
```

Create `src/FileTransfer.Core/Pairing/PairingResult.cs`:

```csharp
namespace FileTransfer.Core.Pairing;

public sealed record PairingResult(string PeerFingerprint, string PeerDeviceName);
```

- [ ] **Step 3: Create the options**

Create `src/FileTransfer.Core/Pairing/PairingServiceOptions.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;

namespace FileTransfer.Core.Pairing;

public sealed class PairingServiceOptions
{
    public required string DeviceName { get; init; }
    public required X509Certificate2 OwnCertificate { get; init; }
    public int UdpPort { get; init; } = 47100;
    public int TcpPort { get; init; } = 47101;
    public TimeSpan AnnounceInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan DecisionTimeout { get; init; } = TimeSpan.FromMinutes(2);
}
```

- [ ] **Step 4: Build to verify everything compiles**

Run: `dotnet build`
Expected: success, no warnings or errors.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(pairing): add data types and options for PairingService"
```

---

## Task 6: `PairingService` skeleton with peer discovery

**Files:**
- Create: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`:

```csharp
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Pairing;

namespace FileTransfer.Core.Tests.Pairing;

public class PairingServiceTests
{
    [Fact]
    public async Task TwoServices_DiscoverEachOther_AsPeerCandidates()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47980;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47981,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47982,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        PeerCandidate? heardByA = null;
        PeerCandidate? heardByB = null;
        string aFp = Fingerprint.Compute(certA.RawData);
        string bFp = Fingerprint.Compute(certB.RawData);
        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) heardByA = p; };
        b.PeerDiscovered += p => { if (p.Fingerprint == aFp) heardByB = p; };

        await a.StartAsync();
        await b.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((heardByA is null || heardByB is null) && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.NotNull(heardByA);
        Assert.NotNull(heardByB);
        Assert.Equal("B", heardByA!.DeviceName);
        Assert.Equal(47981, heardByB!.TcpPort);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: FAIL — `PairingService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/FileTransfer.Core/Pairing/PairingService.cs`:

```csharp
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Discovery;

namespace FileTransfer.Core.Pairing;

/// First-time pairing orchestrator. Runs UDP discovery on the same magic as DiscoveryService,
/// surfaces every peer as a PeerCandidate (regardless of fingerprint), and — once
/// RequestPairingAsync or an incoming TLS is in play (added in later tasks) — drives the
/// HELLO + 4-digit-code + mutual-confirm handshake. Single active session at a time.
public sealed class PairingService : IDisposable
{
    private readonly PairingServiceOptions _options;
    private readonly string _ownFingerprint;
    private DiscoveryService? _discovery;

    public string OwnFingerprint => _ownFingerprint;
    public PairingState State { get; private set; } = PairingState.Idle;

    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string /*pairingCode*/, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;

    public PairingService(PairingServiceOptions options)
    {
        _options = options;
        _ownFingerprint = Fingerprint.Compute(options.OwnCertificate.RawData);
    }

    public Task StartAsync()
    {
        _discovery = new DiscoveryService(
            _options.UdpPort, _options.TcpPort, _ownFingerprint, _options.DeviceName, _options.AnnounceInterval);
        _discovery.PeerDiscovered += peer =>
            PeerDiscovered?.Invoke(new PeerCandidate(peer.Address, peer.TcpPort, peer.Fingerprint, peer.DeviceName));
        _discovery.Start();
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(pairing): add PairingService skeleton with peer discovery"
```

---

## Task 7: Exchange HELLO and reach `AwaitingDecision`

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

This task adds the TLS listener, `RequestPairingAsync`, the incoming-connection path, and the HELLO exchange — everything from `Idle` through `AwaitingDecision`. Confirm/reject/timeout/etc. come in later tasks.

- [ ] **Step 1: Add the failing test**

Append inside the existing `PairingServiceTests` class:

```csharp
    [Fact]
    public async Task HappyPath_ReachesAwaitingDecision_OnBothSides_WithMatchingCode()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47983;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47984,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47985,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aCandidateTcs = new TaskCompletionSource<(string Code, PeerCandidate Peer)>();
        var bCandidateTcs = new TaskCompletionSource<(string Code, PeerCandidate Peer)>();
        string bFp = Fingerprint.Compute(certB.RawData);
        string aFp = Fingerprint.Compute(certA.RawData);

        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        a.PairingCandidateReady += (code, peer) => aCandidateTcs.TrySetResult((code, peer));
        b.PairingCandidateReady += (code, peer) => bCandidateTcs.TrySetResult((code, peer));

        await a.StartAsync();
        await b.StartAsync();

        var bPeer = await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await a.RequestPairingAsync(bPeer);

        var aResult = await aCandidateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bResult = await bCandidateTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(aResult.Code, bResult.Code);
        Assert.Equal("B", aResult.Peer.DeviceName);
        Assert.Equal("A", bResult.Peer.DeviceName);
        Assert.Equal(bFp, aResult.Peer.Fingerprint);
        Assert.Equal(aFp, bResult.Peer.Fingerprint);
        Assert.Equal(PairingState.AwaitingDecision, a.State);
        Assert.Equal(PairingState.AwaitingDecision, b.State);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~HappyPath_ReachesAwaitingDecision"`
Expected: FAIL — `RequestPairingAsync` and `PairingCandidateReady` do not yet exist.

- [ ] **Step 3: Write the implementation**

Replace `src/FileTransfer.Core/Pairing/PairingService.cs` with the expanded version:

```csharp
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Discovery;
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transport;

namespace FileTransfer.Core.Pairing;

public sealed class PairingService : IDisposable
{
    private const int ProtocolVersion = 1;

    private readonly PairingServiceOptions _options;
    private readonly string _ownFingerprint;

    private DiscoveryService? _discovery;
    private TransportListener? _listener;

    // All session state below is touched only under _stateLock.
    private readonly object _stateLock = new();
    private PairingState _state = PairingState.Idle;
    private Connection? _activeConnection;
    private PeerCandidate? _activePeer;

    public string OwnFingerprint => _ownFingerprint;
    public PairingState State { get { lock (_stateLock) return _state; } }

    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string /*pairingCode*/, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;

    public PairingService(PairingServiceOptions options)
    {
        _options = options;
        _ownFingerprint = Fingerprint.Compute(options.OwnCertificate.RawData);
    }

    public Task StartAsync()
    {
        _listener = new TransportListener(_options.TcpPort, _options.OwnCertificate, expectedPeerFingerprint: null);
        _listener.ConnectionAccepted += OnIncomingConnection;
        _listener.Start();

        _discovery = new DiscoveryService(
            _options.UdpPort, _options.TcpPort, _ownFingerprint, _options.DeviceName, _options.AnnounceInterval);
        _discovery.PeerDiscovered += peer =>
            PeerDiscovered?.Invoke(new PeerCandidate(peer.Address, peer.TcpPort, peer.Fingerprint, peer.DeviceName));
        _discovery.Start();
        return Task.CompletedTask;
    }

    public async Task RequestPairingAsync(PeerCandidate peer)
    {
        lock (_stateLock)
        {
            if (_state != PairingState.Idle)
                throw new InvalidOperationException($"Cannot request pairing in state {_state}.");
            _state = PairingState.Negotiating;
            _activePeer = peer;
        }

        Connection conn;
        try
        {
            conn = await TransportConnector.ConnectAsync(
                peer.Address.ToString(), peer.TcpPort, _options.OwnCertificate,
                expectedPeerFingerprint: null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            lock (_stateLock) { _state = PairingState.Failed; _activePeer = null; }
            PairingFailed?.Invoke(PairingFailureReason.TlsHandshakeFailed, ex.Message);
            return;
        }

        AdoptConnection(conn);
    }

    private void OnIncomingConnection(Connection conn)
    {
        lock (_stateLock)
        {
            if (_state != PairingState.Idle) { conn.Dispose(); return; }
            _state = PairingState.Negotiating;
            // Peer device name not yet known — filled in once we receive HELLO.
            _activePeer = new PeerCandidate(System.Net.IPAddress.Loopback, 0, conn.PeerFingerprint ?? "", "");
        }
        AdoptConnection(conn);
    }

    private void AdoptConnection(Connection conn)
    {
        lock (_stateLock) { _activeConnection = conn; }

        // The lambdas capture `conn` so we can ignore late events from a connection that the
        // race tiebreaker (Task 14) may later replace. Wired this way from day one so later
        // tasks don't have to rewrite event handlers.
        conn.FrameReceived += (type, payload) =>
        {
            lock (_stateLock) { if (!ReferenceEquals(_activeConnection, conn)) return; }
            OnFrameReceived(type, payload);
        };
        conn.Closed += _ =>
        {
            lock (_stateLock) { if (!ReferenceEquals(_activeConnection, conn)) return; }
            OnActiveConnectionClosed();
        };

        var hello = new HelloMessage { DeviceName = _options.DeviceName, ProtocolVersion = ProtocolVersion };
        _ = conn.SendAsync(MessageType.Hello, MessageSerializer.Serialize(hello), CancellationToken.None);
    }

    // No-op until Task 11 wires ConnectionLost handling.
    private void OnActiveConnectionClosed() { }

    private void OnFrameReceived(MessageType type, byte[] payload)
    {
        if (type == MessageType.Hello) { HandleHello(payload); return; }
        // Other message types handled in later tasks.
    }

    private void HandleHello(byte[] payload)
    {
        HelloMessage hello;
        try { hello = MessageSerializer.Deserialize<HelloMessage>(payload); }
        catch { return; } // malformed HELLO is handled as ConnectionLost in a later task

        PeerCandidate finalPeer;
        string code;
        lock (_stateLock)
        {
            if (_state != PairingState.Negotiating || _activeConnection is null || _activePeer is null) return;
            string peerFp = _activeConnection.PeerFingerprint ?? "";
            finalPeer = _activePeer with { Fingerprint = peerFp, DeviceName = hello.DeviceName };
            _activePeer = finalPeer;
            _state = PairingState.AwaitingDecision;
            code = Fingerprint.PairingCode(_ownFingerprint, peerFp);
        }

        PairingCandidateReady?.Invoke(code, finalPeer);
    }

    public Task ConfirmAsync() => throw new NotImplementedException("Added in a later task.");
    public Task RejectAsync(string reason = "") => throw new NotImplementedException("Added in a later task.");

    public void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
        _listener?.Dispose();
        _listener = null;
        lock (_stateLock)
        {
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS (3 tests: the previous one + the new happy path + HappyPath skeleton sub-cases).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(pairing): exchange HELLO and reach AwaitingDecision"
```

---

## Task 8: Complete pairing on mutual confirm

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `PairingServiceTests`:

```csharp
    [Fact]
    public async Task HappyPath_BothConfirm_RaisesPairingCompleted_OnBothSides()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47986;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47987,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47988,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aCompleted = new TaskCompletionSource<PairingResult>();
        var bCompleted = new TaskCompletionSource<PairingResult>();
        string bFp = Fingerprint.Compute(certB.RawData);
        string aFp = Fingerprint.Compute(certA.RawData);

        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        a.PairingCandidateReady += async (_, _) => await a.ConfirmAsync();
        b.PairingCandidateReady += async (_, _) => await b.ConfirmAsync();
        a.PairingCompleted += r => aCompleted.TrySetResult(r);
        b.PairingCompleted += r => bCompleted.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        var bPeer = await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await a.RequestPairingAsync(bPeer);

        var aRes = await aCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bRes = await bCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(bFp, aRes.PeerFingerprint);
        Assert.Equal("B", aRes.PeerDeviceName);
        Assert.Equal(aFp, bRes.PeerFingerprint);
        Assert.Equal("A", bRes.PeerDeviceName);
        Assert.Equal(PairingState.Completed, a.State);
        Assert.Equal(PairingState.Completed, b.State);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~HappyPath_BothConfirm"`
Expected: FAIL — `ConfirmAsync` throws `NotImplementedException`.

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Pairing/PairingService.cs`. Add two boolean fields and replace `ConfirmAsync` plus extend `OnFrameReceived`:

```csharp
// Add fields near the existing state fields:
    private bool _ourConfirmSent;
    private bool _peerConfirmReceived;
```

Replace the `ConfirmAsync` placeholder with the real implementation:

```csharp
    public async Task ConfirmAsync()
    {
        Connection conn;
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision)
                throw new InvalidOperationException($"Cannot confirm in state {_state}.");
            if (_ourConfirmSent) return;
            _ourConfirmSent = true;
            conn = _activeConnection ?? throw new InvalidOperationException("No active connection.");
        }

        await conn.SendAsync(MessageType.PairingConfirm, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
        TryComplete();
    }

    private void HandlePeerConfirm()
    {
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision || _peerConfirmReceived) return;
            _peerConfirmReceived = true;
        }
        TryComplete();
    }

    private void TryComplete()
    {
        PairingResult? result = null;
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision) return;
            if (!_ourConfirmSent || !_peerConfirmReceived) return;
            _state = PairingState.Completed;
            var peer = _activePeer!;
            result = new PairingResult(peer.Fingerprint, peer.DeviceName);
        }
        PairingCompleted?.Invoke(result);
    }
```

Update `OnFrameReceived` to route the new message type:

```csharp
    private void OnFrameReceived(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.Hello: HandleHello(payload); break;
            case MessageType.PairingConfirm: HandlePeerConfirm(); break;
            // PairingReject handled in a later task.
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS (all existing + 1 new).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(pairing): complete pairing when both sides confirm"
```

---

## Task 9: Reject paths — `LocallyRejected` and `PeerRejected`

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `PairingServiceTests`:

```csharp
    [Fact]
    public async Task BRejects_BothSidesRaisePairingFailed_WithCorrectReasons()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47989;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47990,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47991,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        var bFailed = new TaskCompletionSource<PairingFailureReason>();
        string bFp = Fingerprint.Compute(certB.RawData);

        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        // A confirms (it will see PeerRejected from B). B rejects.
        a.PairingCandidateReady += async (_, _) => await a.ConfirmAsync();
        b.PairingCandidateReady += async (_, _) => await b.RejectAsync("test reject");
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);
        b.PairingFailed += (r, _) => bFailed.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        await a.RequestPairingAsync(await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        var aReason = await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bReason = await bFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PairingFailureReason.PeerRejected, aReason);
        Assert.Equal(PairingFailureReason.LocallyRejected, bReason);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BRejects_BothSidesRaisePairingFailed"`
Expected: FAIL — `RejectAsync` throws `NotImplementedException`.

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Pairing/PairingService.cs`.

Replace the `RejectAsync` placeholder with:

```csharp
    public async Task RejectAsync(string reason = "")
    {
        Connection? conn;
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision)
                throw new InvalidOperationException($"Cannot reject in state {_state}.");
            conn = _activeConnection;
        }

        if (conn is not null)
        {
            try { await conn.SendAsync(MessageType.PairingReject, ReadOnlyMemory<byte>.Empty, CancellationToken.None); }
            catch { /* peer may have already disconnected; we still fail locally */ }
        }

        Fail(PairingFailureReason.LocallyRejected, reason);
    }

    private void HandlePeerReject()
    {
        Fail(PairingFailureReason.PeerRejected, "");
    }

    private void Fail(PairingFailureReason reason, string detail)
    {
        bool raise;
        lock (_stateLock)
        {
            // Idempotent: only the first failure wins.
            if (_state == PairingState.Failed || _state == PairingState.Completed) return;
            raise = _state == PairingState.Negotiating || _state == PairingState.AwaitingDecision;
            _state = PairingState.Failed;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
        if (raise) PairingFailed?.Invoke(reason, detail);
    }
```

Update `OnFrameReceived` to route PairingReject:

```csharp
    private void OnFrameReceived(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.Hello: HandleHello(payload); break;
            case MessageType.PairingConfirm: HandlePeerConfirm(); break;
            case MessageType.PairingReject: HandlePeerReject(); break;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS (all + new test).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(pairing): fail pairing on reject (local or peer)"
```

---

## Task 10: Decision timeout

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `PairingServiceTests`:

```csharp
    [Fact]
    public async Task DecisionTimeout_BothSidesRaisePairingFailed_LocalTimeout()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47992;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47993,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromMilliseconds(200),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47994,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromMilliseconds(200),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        var bFailed = new TaskCompletionSource<PairingFailureReason>();
        string bFp = Fingerprint.Compute(certB.RawData);
        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        // Nobody calls Confirm or Reject — just wait for the timer.
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);
        b.PairingFailed += (r, _) => bFailed.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        await a.RequestPairingAsync(await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(PairingFailureReason.LocalTimeout, await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(PairingFailureReason.LocalTimeout, await bFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DecisionTimeout"`
Expected: FAIL — nothing arms the timer; A and B sit in `AwaitingDecision` forever.

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Pairing/PairingService.cs`. Add a CTS field and arm/disarm helpers:

```csharp
// Near the other state fields:
    private CancellationTokenSource? _decisionTimeoutCts;
```

In `HandleHello`, after raising `PairingCandidateReady`, start the timer (we need to start it BEFORE invoking the event because the event handler might call ConfirmAsync synchronously). Replace the tail of `HandleHello`:

```csharp
    private void HandleHello(byte[] payload)
    {
        HelloMessage hello;
        try { hello = MessageSerializer.Deserialize<HelloMessage>(payload); }
        catch { return; }

        PeerCandidate finalPeer;
        string code;
        lock (_stateLock)
        {
            if (_state != PairingState.Negotiating || _activeConnection is null || _activePeer is null) return;
            string peerFp = _activeConnection.PeerFingerprint ?? "";
            finalPeer = _activePeer with { Fingerprint = peerFp, DeviceName = hello.DeviceName };
            _activePeer = finalPeer;
            _state = PairingState.AwaitingDecision;
            code = Fingerprint.PairingCode(_ownFingerprint, peerFp);
            ArmDecisionTimeout();
        }
        PairingCandidateReady?.Invoke(code, finalPeer);
    }

    // Must be called inside _stateLock.
    private void ArmDecisionTimeout()
    {
        _decisionTimeoutCts = new CancellationTokenSource();
        var ct = _decisionTimeoutCts.Token;
        _ = Task.Delay(_options.DecisionTimeout, ct).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Fail(PairingFailureReason.LocalTimeout, "decision timeout");
        }, TaskScheduler.Default);
    }
```

Add a disarm call at the top of `TryComplete` (before completing) and inside `Fail` so the timer doesn't fire after a terminal state:

```csharp
    private void TryComplete()
    {
        PairingResult? result = null;
        lock (_stateLock)
        {
            if (_state != PairingState.AwaitingDecision) return;
            if (!_ourConfirmSent || !_peerConfirmReceived) return;
            _state = PairingState.Completed;
            _decisionTimeoutCts?.Cancel();
            _decisionTimeoutCts = null;
            var peer = _activePeer!;
            result = new PairingResult(peer.Fingerprint, peer.DeviceName);
        }
        PairingCompleted?.Invoke(result);
    }

    private void Fail(PairingFailureReason reason, string detail)
    {
        bool raise;
        lock (_stateLock)
        {
            if (_state == PairingState.Failed || _state == PairingState.Completed) return;
            raise = _state == PairingState.Negotiating || _state == PairingState.AwaitingDecision;
            _state = PairingState.Failed;
            _decisionTimeoutCts?.Cancel();
            _decisionTimeoutCts = null;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
        if (raise) PairingFailed?.Invoke(reason, detail);
    }
```

Also update `Stop` to cancel the decision-timeout CTS on disposal so a Stop mid-AwaitingDecision doesn't leave a Task.Delay holding the CTS until the timeout elapses:

```csharp
    public void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
        _listener?.Dispose();
        _listener = null;
        lock (_stateLock)
        {
            _decisionTimeoutCts?.Cancel();
            _decisionTimeoutCts = null;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS (all + new test).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(pairing): fail pairing on decision timeout"
```

---

## Task 11: Fail on connection loss during pairing

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `PairingServiceTests`:

```csharp
    [Fact]
    public async Task PeerDisconnectsDuringAwaitingDecision_FailsWithConnectionLost()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47995;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47996,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 47997,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aReady = new TaskCompletionSource();
        var bReady = new TaskCompletionSource();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        string bFp = Fingerprint.Compute(certB.RawData);
        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        a.PairingCandidateReady += (_, _) => aReady.TrySetResult();
        b.PairingCandidateReady += (_, _) => bReady.TrySetResult();
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();
        await a.RequestPairingAsync(await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(aReady.Task, bReady.Task).WaitAsync(TimeSpan.FromSeconds(5));

        // B unilaterally goes away.
        b.Dispose();

        Assert.Equal(PairingFailureReason.ConnectionLost, await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PeerDisconnectsDuringAwaitingDecision"`
Expected: FAIL — A never observes B's drop because we don't wire `Connection.Closed`.

- [ ] **Step 3: Write the implementation**

Edit `src/FileTransfer.Core/Pairing/PairingService.cs`. `AdoptConnection` already routes `Closed` to `OnActiveConnectionClosed` (Task 7 wired the empty hook). Fill in the body:

```csharp
    private void OnActiveConnectionClosed()
    {
        // Treat a drop as ConnectionLost only if we're still mid-pairing. Fail is idempotent,
        // so a Closed firing after Completed or Failed is a harmless no-op.
        Fail(PairingFailureReason.ConnectionLost, "peer disconnected");
    }
```

The per-connection identity check in Task 7's lambda ensures `OnActiveConnectionClosed` only fires for the currently-active connection, so a race-tiebreaker loser disposed in Task 14 will not raise a false `ConnectionLost`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS (all + new test).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(pairing): fail pairing on connection loss"
```

---

## Task 12: Fail on HELLO protocol version mismatch

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

For the test we need to force one side to send a non-matching `ProtocolVersion`. The cleanest way without exposing internals is to expose `ProtocolVersion` as an `internal` constant and use `InternalsVisibleTo` for the test project.

- [ ] **Step 1: Expose the protocol version to the test project**

Edit `src/FileTransfer.Core/FileTransfer.Core.csproj` to add (inside an `<ItemGroup>`):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="FileTransfer.Core.Tests" />
</ItemGroup>
```

Change `PairingService.ProtocolVersion` from `private const` to `internal const` so the test can read it. Inside `PairingService.cs`:

```csharp
    internal const int ProtocolVersion = 1;
```

- [ ] **Step 2: Add the failing test**

Append to `PairingServiceTests`. We send a hand-crafted HELLO frame with a bumped version by reaching into `TransportConnector` and writing it raw. To stay focused, the test confirms only A's side fails; B is the one sending the bad HELLO:

```csharp
    [Fact]
    public async Task PeerSendsWrongProtocolVersion_FailsWithProtocolMismatch()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 47998;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 47999,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });
        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aFailed = new TaskCompletionSource<PairingFailureReason>();
        a.PeerDiscovered += p => { if (p.Fingerprint == Fingerprint.Compute(certB.RawData)) aSeesB.TrySetResult(p); };
        a.PairingFailed += (r, _) => aFailed.TrySetResult(r);
        await a.StartAsync();

        // Roll our own "B": broadcast a beacon and run a hand-crafted TLS listener that
        // sends HELLO with a bad version. We piggyback on DiscoveryService for the beacon.
        using var bDiscovery = new FileTransfer.Core.Discovery.DiscoveryService(
            udp, 48000, Fingerprint.Compute(certB.RawData), "B", TimeSpan.FromMilliseconds(100));
        using var bListener = new TransportListener(48000, certB, expectedPeerFingerprint: null);
        bListener.ConnectionAccepted += async conn =>
        {
            // Subscribe to A's HELLO so we know the channel is up, then send a poisoned one.
            conn.FrameReceived += async (type, _) =>
            {
                if (type != MessageType.Hello) return;
                var bad = new HelloMessage { DeviceName = "B", ProtocolVersion = PairingService.ProtocolVersion + 1 };
                await conn.SendAsync(MessageType.Hello, MessageSerializer.Serialize(bad), CancellationToken.None);
            };
        };
        bListener.Start();
        bDiscovery.Start();

        var bPeer = await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await a.RequestPairingAsync(bPeer);

        Assert.Equal(PairingFailureReason.ProtocolMismatch, await aFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
```

The test imports needed at the top of the test file:

```csharp
using FileTransfer.Core.Protocol;
using FileTransfer.Core.Transport;
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PeerSendsWrongProtocolVersion"`
Expected: FAIL — A currently ignores `ProtocolVersion` and stays in `Negotiating`.

- [ ] **Step 4: Write the implementation**

Edit `src/FileTransfer.Core/Pairing/PairingService.cs`. Add the version check at the top of `HandleHello`:

```csharp
    private void HandleHello(byte[] payload)
    {
        HelloMessage hello;
        try { hello = MessageSerializer.Deserialize<HelloMessage>(payload); }
        catch { return; }

        if (hello.ProtocolVersion != ProtocolVersion)
        {
            Fail(PairingFailureReason.ProtocolMismatch, $"peer version={hello.ProtocolVersion}");
            return;
        }

        PeerCandidate finalPeer;
        string code;
        lock (_stateLock)
        {
            if (_state != PairingState.Negotiating || _activeConnection is null || _activePeer is null) return;
            string peerFp = _activeConnection.PeerFingerprint ?? "";
            finalPeer = _activePeer with { Fingerprint = peerFp, DeviceName = hello.DeviceName };
            _activePeer = finalPeer;
            _state = PairingState.AwaitingDecision;
            code = Fingerprint.PairingCode(_ownFingerprint, peerFp);
            ArmDecisionTimeout();
        }
        PairingCandidateReady?.Invoke(code, finalPeer);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat(pairing): fail pairing on protocol version mismatch"
```

---

## Task 13: Drop third TLS connection while a session is busy

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs` (no source change needed — already handled in Task 7's `OnIncomingConnection`)
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

The `OnIncomingConnection` from Task 7 already disposes any incoming connection when `_state != PairingState.Idle`. This task verifies that behaviour with a dedicated test (no production change unless the test exposes a bug).

- [ ] **Step 1: Add the test**

Append to `PairingServiceTests`:

```csharp
    [Fact]
    public async Task ThirdConnectionWhileBusy_IsDropped_ActiveSessionUnaffected()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");
        using var certC = CertificateFactory.CreateSelfSigned("C");

        int udp = 48010;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 48011,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 48012,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var aReady = new TaskCompletionSource();
        var bReady = new TaskCompletionSource();
        var aCompleted = new TaskCompletionSource<PairingResult>();
        string bFp = Fingerprint.Compute(certB.RawData);
        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        a.PairingCandidateReady += (_, _) => aReady.TrySetResult();
        b.PairingCandidateReady += (_, _) => bReady.TrySetResult();
        a.PairingCompleted += r => aCompleted.TrySetResult(r);
        b.PairingCandidateReady += async (_, _) => await b.ConfirmAsync();

        await a.StartAsync();
        await b.StartAsync();
        await a.RequestPairingAsync(await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(aReady.Task, bReady.Task).WaitAsync(TimeSpan.FromSeconds(5));

        // Now A and B are in AwaitingDecision. A rogue third party C dials B directly.
        try
        {
            using var rogueConn = await TransportConnector.ConnectAsync(
                "127.0.0.1", 48012, certC, expectedPeerFingerprint: null, CancellationToken.None);
            await Task.Delay(300); // give B time to drop the rogue
        }
        catch { /* B may RST the TCP, which is fine */ }

        // A → B confirm should still complete normally.
        await a.ConfirmAsync();
        var result = await aCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(bFp, result.PeerFingerprint);
    }
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ThirdConnectionWhileBusy"`
Expected: PASS — Task 7 already drops the third connection. If it fails, the bug is in `OnIncomingConnection`'s busy check.

- [ ] **Step 3: Commit**

```powershell
git add .
git commit -m "test(pairing): assert third TLS while busy is dropped"
```

---

## Task 14: Arbitrate simultaneous-dial race with fingerprint comparison

**Files:**
- Modify: `src/FileTransfer.Core/Pairing/PairingService.cs`
- Test: `tests/FileTransfer.Core.Tests/Pairing/PairingServiceTests.cs`

When both sides call `RequestPairingAsync` at nearly the same moment, the current code (Task 7) leaves both ends with **two** TLS connections: their own outgoing plus the incoming from the peer. Without arbitration the second incoming is dropped (Task 13), but each side may have kept a different stream → HELLO and confirm messages cross on dead channels. The deterministic fingerprint rule fixes this.

**Rule:** smaller fingerprint side keeps its **outgoing** dial; larger fingerprint side keeps the **incoming** connection and disposes its own outgoing. Two sides applying this rule converge on the same stream.

- [ ] **Step 1: Add the failing test**

Append to `PairingServiceTests`:

```csharp
    [Fact]
    public async Task BothDialSimultaneously_ConvergesToOneSession_BothReachCompleted()
    {
        using var certA = CertificateFactory.CreateSelfSigned("A");
        using var certB = CertificateFactory.CreateSelfSigned("B");

        int udp = 48020;
        using var a = new PairingService(new PairingServiceOptions
        {
            DeviceName = "A", OwnCertificate = certA,
            UdpPort = udp, TcpPort = 48021,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });
        using var b = new PairingService(new PairingServiceOptions
        {
            DeviceName = "B", OwnCertificate = certB,
            UdpPort = udp, TcpPort = 48022,
            AnnounceInterval = TimeSpan.FromMilliseconds(100),
            DecisionTimeout = TimeSpan.FromSeconds(10),
        });

        var aSeesB = new TaskCompletionSource<PeerCandidate>();
        var bSeesA = new TaskCompletionSource<PeerCandidate>();
        var aCompleted = new TaskCompletionSource<PairingResult>();
        var bCompleted = new TaskCompletionSource<PairingResult>();
        string aFp = Fingerprint.Compute(certA.RawData);
        string bFp = Fingerprint.Compute(certB.RawData);

        a.PeerDiscovered += p => { if (p.Fingerprint == bFp) aSeesB.TrySetResult(p); };
        b.PeerDiscovered += p => { if (p.Fingerprint == aFp) bSeesA.TrySetResult(p); };
        a.PairingCandidateReady += async (_, _) => await a.ConfirmAsync();
        b.PairingCandidateReady += async (_, _) => await b.ConfirmAsync();
        a.PairingCompleted += r => aCompleted.TrySetResult(r);
        b.PairingCompleted += r => bCompleted.TrySetResult(r);

        await a.StartAsync();
        await b.StartAsync();

        var bPeer = await aSeesB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var aPeer = await bSeesA.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Both dial concurrently.
        await Task.WhenAll(a.RequestPairingAsync(bPeer), b.RequestPairingAsync(aPeer));

        var aRes = await aCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var bRes = await bCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(bFp, aRes.PeerFingerprint);
        Assert.Equal(aFp, bRes.PeerFingerprint);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BothDialSimultaneously"`
Expected: FAIL — both sides may end up with mismatched connections; one side's `ConfirmAsync` never reaches the other.

- [ ] **Step 3: Write the implementation**

The cleanest implementation cancels the **outgoing dial** when the race resolves in favour of the incoming connection, instead of swapping adopted connections post-hoc. We need a `CancellationTokenSource` per dial.

Edit `src/FileTransfer.Core/Pairing/PairingService.cs`.

Add a field next to the other state fields:

```csharp
    private CancellationTokenSource? _dialCts;
```

Replace `RequestPairingAsync` with a cancellation-aware version:

```csharp
    public async Task RequestPairingAsync(PeerCandidate peer)
    {
        CancellationToken dialCt;
        lock (_stateLock)
        {
            // If an incoming connection already kicked off the session (extreme race where
            // the incoming TLS arrived before the user's "Pair" click), let the incoming path
            // continue — nothing for us to dial.
            if (_state == PairingState.Negotiating && _activeConnection is not null) return;
            if (_state != PairingState.Idle)
                throw new InvalidOperationException($"Cannot request pairing in state {_state}.");
            _state = PairingState.Negotiating;
            _activePeer = peer;
            _dialCts = new CancellationTokenSource();
            dialCt = _dialCts.Token;
        }

        Connection conn;
        try
        {
            conn = await TransportConnector.ConnectAsync(
                peer.Address.ToString(), peer.TcpPort, _options.OwnCertificate,
                expectedPeerFingerprint: null, dialCt);
        }
        catch (OperationCanceledException)
        {
            // Race tiebreaker cancelled us in favour of the incoming connection. The incoming
            // path is driving the session forward; we exit silently.
            return;
        }
        catch (Exception ex)
        {
            Fail(PairingFailureReason.TlsHandshakeFailed, ex.Message);
            return;
        }

        bool adopt;
        lock (_stateLock)
        {
            // If an incoming connection beat us to adoption while we were still dialing,
            // our outgoing is the race-loser. Drop it. No handlers wired yet → nothing fires.
            adopt = _activeConnection is null && _state == PairingState.Negotiating;
        }
        if (!adopt) { conn.Dispose(); return; }
        AdoptConnection(conn);
    }
```

Replace `OnIncomingConnection` with a tiebreaker-aware version:

```csharp
    private void OnIncomingConnection(Connection conn)
    {
        bool adopt;
        lock (_stateLock)
        {
            // Past Negotiating? We're busy with an active session — drop a third connection.
            if (_state == PairingState.AwaitingDecision ||
                _state == PairingState.Completed ||
                _state == PairingState.Failed)
            {
                conn.Dispose(); return;
            }

            if (_state == PairingState.Idle)
            {
                // Pure incoming, no outgoing race. Adopt directly.
                _state = PairingState.Negotiating;
                _activePeer = new PeerCandidate(
                    System.Net.IPAddress.Loopback, 0, conn.PeerFingerprint ?? "", "");
                adopt = true;
            }
            else
            {
                // _state == Negotiating: an outgoing dial is in flight. Apply the deterministic
                // race rule — smaller fingerprint keeps its OUTGOING dial; larger keeps INCOMING.
                string peerFp = conn.PeerFingerprint ?? "";
                bool localIsSmaller = string.CompareOrdinal(_ownFingerprint, peerFp) < 0;
                if (localIsSmaller)
                {
                    // We keep our outgoing dial; drop this incoming.
                    conn.Dispose(); return;
                }
                // We are larger — cancel our outgoing dial and adopt this incoming.
                // If the dial has already completed, the cancel is a no-op and
                // RequestPairingAsync's continuation will drop the loser via the
                // `_activeConnection is null` check (it will be non-null by then).
                _dialCts?.Cancel();
                adopt = true;
            }
        }
        if (adopt) AdoptConnection(conn);
    }
```

Update `Stop` to release the new CTS:

```csharp
    public void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
        _listener?.Dispose();
        _listener = null;
        lock (_stateLock)
        {
            _decisionTimeoutCts?.Cancel();
            _decisionTimeoutCts = null;
            _dialCts?.Cancel();
            _dialCts = null;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PairingServiceTests"`
Expected: PASS (all tests, including the race tiebreaker).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS, all tests across the solution.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat(pairing): arbitrate simultaneous-dial race with fingerprint comparison"
```

---

## After all tasks

When every Task is committed and `dotnet test` is green, this branch (`feature/pairing-service`) is ready for the merge-or-PR workflow via the `superpowers:finishing-a-development-branch` skill, identical to how the Core branch was wrapped up.

The WPF UI phase (next brainstorm) will consume this module's public API (`PairingService`, `PairingResult`, `PairingFailureReason`) and add no further dependencies inside `Core`.
