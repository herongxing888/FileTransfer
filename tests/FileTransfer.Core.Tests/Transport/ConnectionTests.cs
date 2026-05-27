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
        // 300ms timeout with 100ms interval: connA only survives the 600ms wait if connB's
        // PONGs keep refreshing connA's last-inbound timestamp. A broken PONG path would time out.
        using var connA = new Connection(sa, heartbeatInterval: TimeSpan.FromMilliseconds(100), heartbeatTimeout: TimeSpan.FromMilliseconds(300));
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
        using var connA = new Connection(sa, heartbeatInterval: TimeSpan.FromMilliseconds(100), heartbeatTimeout: TimeSpan.FromMilliseconds(300));
        _ = sb; // B's stream exists but no Connection reads it

        var closed = new TaskCompletionSource();
        connA.Closed += _ => closed.TrySetResult();
        connA.Start();

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
