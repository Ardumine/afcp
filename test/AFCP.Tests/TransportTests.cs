using System.Net;
using AFCP;
using Xunit;

namespace AFCP.Tests;

public sealed class TransportTests : IDisposable
{
    // ── InMemoryConnection ──────────────────────────────────────────

    [Fact]
    public void InMemoryConnection_BoundedCapacity_BlocksOnFullQueue()
    {
        var (a, b) = InMemoryConnection.CreatePair(capacity: 1);
        var payload1 = new byte[1024];
        var payload2 = new byte[1024];

        a.Write(payload1);

        var writeDone = new ManualResetEventSlim();
        var writeThread = new Thread(() =>
        {
            b.Write(payload2);
            writeDone.Set();
        }) { IsBackground = true };
        writeThread.Start();

        var buf = new byte[1024];
        Assert.Equal(1024, a.Read(buf));
        Assert.True(writeDone.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1024, b.Read(buf));
        a.Close(); b.Close();
    }

    [Fact]
    public void InMemoryConnection_DoubleCloseDoesNotThrow()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        a.Close();
        var ex = Record.Exception(() => a.Close());
        Assert.Null(ex);
    }

    [Fact]
    public void InMemoryConnection_WriteAfterCloseThrows()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        a.Close();
        Assert.Throws<ObjectDisposedException>(() => a.Write(new byte[1]));
    }

    [Fact]
    public void InMemoryConnection_ReadAfterCloseReturnsZero()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        a.Close();
        var buf = new byte[10];
        Assert.Equal(0, a.Read(buf));
    }

    // ── ReconnectingConnection ──────────────────────────────────────

    [Fact]
    public void ReconnectingConnection_IsConnectedReflectsUnderlyingConnection()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        var recon = new ReconnectingConnection(() => a, initialBackoff: TimeSpan.FromMilliseconds(10));
        Assert.True(recon.IsConnected);
        a.Close();
        recon.Close();
    }

    [Fact]
    public void ReconnectingConnection_FiresOnDisconnectAfterRead()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var recon = new ReconnectingConnection(() => a, initialBackoff: TimeSpan.FromMilliseconds(10));
        var disconnected = new ManualResetEventSlim();
        recon.OnDisconnect += () => disconnected.Set();

        b.Close();
        var buf = new byte[10];
        recon.Read(buf);
        Assert.True(disconnected.Wait(TimeSpan.FromSeconds(5)));
        recon.Close();
    }

    [Fact]
    public void ReconnectingConnection_DisposeClosesUnderlyingConnection()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        var recon = new ReconnectingConnection(() => a, initialBackoff: TimeSpan.FromMilliseconds(10));
        recon.Close();
        Assert.False(a.IsConnected);
    }

    // ── TransportRegistry ───────────────────────────────────────────

    [Fact]
    public void TransportRegistry_OpenInMemory()
    {
        var (a, _) = TransportRegistry.RegisterInMemory("test-key");
        var opened = TransportRegistry.Open("inmem://test-key");
        Assert.NotNull(opened);
        Assert.True(opened.IsConnected);
        a.Close();
        opened.Close();
    }

    [Fact]
    public void TransportRegistry_OpenInMemoryMultiKey()
    {
        var (a1, _) = TransportRegistry.RegisterInMemory("key-a");
        var (a2, _) = TransportRegistry.RegisterInMemory("key-b");
        var opened1 = TransportRegistry.Open("inmem://key-a");
        var opened2 = TransportRegistry.Open("inmem://key-b");
        Assert.True(opened1.IsConnected);
        Assert.True(opened2.IsConnected);
        a1.Close(); a2.Close();
        opened1.Close(); opened2.Close();
    }

    [Fact]
    public void TransportRegistry_UnknownSchemeThrows()
    {
        Assert.Throws<ArgumentException>(() => TransportRegistry.Open("fake://host"));
    }

    [Fact]
    public void TransportRegistry_CustomScheme()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        TransportRegistry.Register("custom", _ => a);

        var opened = TransportRegistry.Open("custom://whatever");
        Assert.Same(a, opened);
        a.Close();
    }

    // ── TcpServer ───────────────────────────────────────────────────

    [Fact]
    public void TcpServer_AcceptMultipleClients()
    {
        var server = new TcpServer(IPAddress.Loopback, 0);
        server.Start();
        var port = server.LocalEndpoint.Port;

        var acceptedCount = 0;
        var srvDone = new ManualResetEventSlim();
        var srvThread = new Thread(() =>
        {
            try
            {
                var c1 = server.Accept();
                c1.Close();
                Interlocked.Increment(ref acceptedCount);
                var c2 = server.Accept();
                c2.Close();
                Interlocked.Increment(ref acceptedCount);
            }
            catch { }
            finally { srvDone.Set(); }
        }) { IsBackground = true };
        srvThread.Start();

        var c1 = new TcpConnection(new IPEndPoint(IPAddress.Loopback, port));
        c1.Close();
        var c2 = new TcpConnection(new IPEndPoint(IPAddress.Loopback, port));
        c2.Close();

        Assert.True(srvDone.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, Volatile.Read(ref acceptedCount));
        server.Stop();
    }

    [Fact]
    public void TcpServer_StopUnblocksAccept()
    {
        var server = new TcpServer(IPAddress.Loopback, 0);
        server.Start();

        var acceptDone = new ManualResetEventSlim();
        var srvThread = new Thread(() =>
        {
            try { server.Accept(); }
            catch { }
            finally { acceptDone.Set(); }
        }) { IsBackground = true };
        srvThread.Start();

        Thread.Sleep(50);
        server.Stop();
        Assert.True(acceptDone.Wait(TimeSpan.FromSeconds(5)));
    }

    public void Dispose() { }
}
