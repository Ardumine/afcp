using AFCP;
using Xunit;

namespace AFCP.Tests;

public sealed class RequestChannelTests : IDisposable
{
    private static byte[] Echo(byte[] x) => x;

    [Fact]
    public void Respond_Twice_Throws()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var srvReady = new ManualResetEventSlim();
        RequestChannel? srvChan = null;
        var srvThread = new Thread(() =>
        {
            try
            {
                var (_, ch) = new AfcpStackBuilder(b).WithChecksum().BuildWithRequestChannel(isServer: true);
                srvChan = ch;
                ch.OnRequest += ctx =>
                {
                    ctx.Respond("ok"u8.ToArray());
                    Assert.Throws<InvalidOperationException>(() =>
                        ctx.Respond("again"u8.ToArray()));
                };
                srvReady.Set();
            }
            catch { }
        }) { IsBackground = true };
        srvThread.Start();

        var (_, cliChan) = new AfcpStackBuilder(a).WithChecksum().BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(5)));

        var resp = cliChan.SendRequest("ping"u8.ToArray());
        Assert.Equal("ok"u8.ToArray(), resp);
        cliChan.Dispose();
        srvThread.Join(5000);
        srvChan?.Dispose();
        a.Close(); b.Close();
    }

    [Fact]
    public void Respond_NeverCalled_ClientTimesOut()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var srvReady = new ManualResetEventSlim();
        var srvThread = new Thread(() =>
        {
            try
            {
                var (_, ch) = new AfcpStackBuilder(b).BuildWithRequestChannel(isServer: true);
                ch.OnRequest += _ => { /* never responds */ };
                srvReady.Set();
            }
            catch { }
        }) { IsBackground = true };
        srvThread.Start();

        var (_, cliChan) = new AfcpStackBuilder(a).BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(5)));

        // Short timeout for the test
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        Assert.Throws<TaskCanceledException>(() =>
            cliChan.SendRequest("ping"u8.ToArray(), cts.Token));
        cliChan.Dispose();
        srvThread.Join(2000);
        a.Close(); b.Close();
    }

    [Fact]
    public void BufferRequests_BeforeHandlerSubscribed()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var requestReceived = new ManualResetEventSlim();
        var srvThread = new Thread(() =>
        {
            try
            {
                var (stream, ch) = new AfcpStackBuilder(b).BuildWithRequestChannel(isServer: true);
                stream.Dispose(); // ignore the outer stream, we just need ch
                ch.OnRequest += ctx =>
                {
                    ctx.Respond(Echo(ctx.Payload.ToArray()));
                    requestReceived.Set();
                };
            }
            catch { }
        }) { IsBackground = true };
        srvThread.Start();

        var (_, cliChan) = new AfcpStackBuilder(a).BuildWithRequestChannel(isServer: false);

        // Request arrives before server handler is subscribed (handled by buffering)
        var resp = cliChan.SendRequest("buffered"u8.ToArray());
        Assert.True(requestReceived.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("buffered"u8.ToArray(), resp);
        cliChan.Dispose();
        srvThread.Join(5000);
        a.Close(); b.Close();
    }

    [Fact]
    public void ZeroLengthPayloads()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var srvReady = new ManualResetEventSlim();
        var srvThread = new Thread(() =>
        {
            try
            {
                var (_, ch) = new AfcpStackBuilder(b).WithChecksum().BuildWithRequestChannel(isServer: true);
                ch.OnRequest += ctx =>
                {
                    ctx.Respond(ReadOnlySpan<byte>.Empty);
                };
                srvReady.Set();
            }
            catch { }
        }) { IsBackground = true };
        srvThread.Start();

        var (_, cliChan) = new AfcpStackBuilder(a).WithChecksum().BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(5)));

        var resp = cliChan.SendRequest(ReadOnlySpan<byte>.Empty);
        Assert.True(resp.Length == 0);
        cliChan.Dispose();
        srvThread.Join(5000);
        a.Close(); b.Close();
    }

    [Fact]
    public void Dispose_WhileRequestsInFlight()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var srvThread = new Thread(() =>
        {
            try
            {
                var (_, ch) = new AfcpStackBuilder(b).WithChecksum().BuildWithRequestChannel(isServer: true);
                ch.OnRequest += ctx => { Thread.Sleep(500); ctx.Respond("late"u8.ToArray()); };
            }
            catch { }
        }) { IsBackground = true };
        srvThread.Start();

        var (_, cliChan) = new AfcpStackBuilder(a).WithChecksum().BuildWithRequestChannel(isServer: false);

        // Send request, then immediately dispose
        var sendDone = new ManualResetEventSlim();
        byte[]? result = null;
        var t = new Thread(() =>
        {
            try { result = cliChan.SendRequest("req"u8.ToArray()); }
            catch { }
            finally { sendDone.Set(); }
        }) { IsBackground = true };
        t.Start();

        Thread.Sleep(50);
        cliChan.Dispose(); // should cancel all pending
        Assert.True(sendDone.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(result); // should have been cancelled
        a.Close(); b.Close();
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        var f = new Framing(new StreamyFromConnection(a));
        f.Initialize(isServer: false);
        var ch = new RequestChannel(f);
        ch.Start();
        Assert.Throws<InvalidOperationException>(() => ch.Start());
        ch.Dispose();
    }

    [Fact]
    public void ConcurrentRequests_ManyInFlight()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var srvReady = new ManualResetEventSlim();
        var srvThread = new Thread(() =>
        {
            try
            {
                var (_, ch) = new AfcpStackBuilder(b).WithChecksum().BuildWithRequestChannel(isServer: true);
                ch.OnRequest += ctx =>
                {
                    var id = int.Parse(System.Text.Encoding.UTF8.GetString(ctx.Payload));
                    ctx.Respond(System.Text.Encoding.UTF8.GetBytes($"resp-{id}"));
                };
                srvReady.Set();
            }
            catch { }
        }) { IsBackground = true };
        srvThread.Start();

        var (_, cliChan) = new AfcpStackBuilder(a).WithChecksum().BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(5)));

        const int count = 20;
        var threads = new Thread[count];
        var results = new byte[]?[count];
        for (int i = 0; i < count; i++)
        {
            var id = i;
            threads[i] = new Thread(() =>
            {
                results[id] = cliChan.SendRequest(System.Text.Encoding.UTF8.GetBytes(id.ToString()));
            }) { IsBackground = true };
            threads[i].Start();
        }

        for (int i = 0; i < count; i++)
            Assert.True(threads[i].Join(TimeSpan.FromSeconds(15)));

        for (int i = 0; i < count; i++)
        {
            var expected = System.Text.Encoding.UTF8.GetBytes($"resp-{i}");
            Assert.NotNull(results[i]);
            Assert.Equal(expected, results[i]!);
        }

        cliChan.Dispose();
        srvThread.Join(5000);
        a.Close(); b.Close();
    }

    public void Dispose() { }
}
