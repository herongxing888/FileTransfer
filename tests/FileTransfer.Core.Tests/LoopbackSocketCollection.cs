namespace FileTransfer.Core.Tests;

/// Tests that bind real loopback UDP/TCP sockets must share this collection so that xUnit
/// runs them sequentially. Without this, two test classes can race on the same Windows
/// loopback / TCP state and produce flaky failures (e.g. PairingService and Discovery
/// tests running in parallel both broadcasting on different UDP ports, but the host's
/// TCP/UDP stack briefly delays handler scheduling under load and tests time out).
[CollectionDefinition(Name)]
public sealed class LoopbackSocketCollection
{
    public const string Name = "LoopbackSockets";
}
