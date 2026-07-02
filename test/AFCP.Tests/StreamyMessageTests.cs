using System.Text;
using AFCP;
using Xunit;

namespace AFCP.Tests;

public sealed class StreamyMessageTests : IDisposable
{
    // ── Framing ─────────────────────────────────────────────────────

    [Fact]
    public void Framing_ZeroLengthMessage()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var thr = new Thread(() =>
        {
            var srv = new Framing(new StreamyFromConnection(b));
            srv.Initialize(isServer: true);
            var m = srv.Read();
            srv.Write(m);
            srv.Dispose();
        }) { IsBackground = true };
        thr.Start();

        var f = new Framing(new StreamyFromConnection(a));
        f.Initialize(isServer: false);
        f.Write(ReadOnlySpan<byte>.Empty);
        var result = f.Read();
        Assert.True(result.Length == 0);
        Assert.True(f.IsConnected);
        thr.Join(5000);
        f.Dispose();
        a.Close(); b.Close();
    }

    [Fact]
    public void Framing_BurstWritesThenReads()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload1 = "msg1"u8.ToArray();
        var payload2 = "msg2"u8.ToArray();

        var (t, _, done) = StartServerEcho(b, false);
        var cli = new Framing(new StreamyFromConnection(a));
        cli.Initialize(isServer: false);

        cli.Write(payload1);
        cli.Write(payload2);
        var r1 = cli.Read().ToArray();
        var r2 = cli.Read().ToArray();
        Assert.Equal(payload1, r1);
        Assert.Equal(payload2, r2);
        cli.Dispose();
        a.Close();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        t.Join(5000);
        b.Close();
    }

    [Fact]
    public void Framing_MaxMessageLength_RejectsOversized()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var thr = new Thread(() =>
        {
            var srv = new Framing(new StreamyFromConnection(b));
            srv.Initialize(isServer: true);
            srv.Write(new byte[5]); // ok
            srv.Write(new byte[15]); // too large for client's MaxMessageLength
            srv.Dispose();
        }) { IsBackground = true };
        thr.Start();

        var f = new Framing(new StreamyFromConnection(a)) { MaxMessageLength = 10 };
        f.Initialize(isServer: false);
        Assert.Equal(5, f.Read().Length);
        var ex = Assert.Throws<InvalidDataException>(() => f.Read());
        Assert.Contains("10", ex.Message);
        f.Dispose();
        a.Close();
        thr.Join(5000);
        b.Close();
    }

    // ── Checksum (alignment boundaries) ─────────────────────────────

    [Fact]
    public void Checksum_RoundTrips_VariousSizes()
    {
        var sizes = new[] { 0, 1, 3, 4, 5, 31, 32, 33, 63, 64, 65, 1000 };
        foreach (var size in sizes)
        {
            var (a, b) = InMemoryConnection.CreatePair();
            var payload = new byte[size];
            new Random((size * 31) + 7).NextBytes(payload);

            var ready = new ManualResetEventSlim();
            var done = new ManualResetEventSlim();
            var t = new Thread(() =>
            {
                try
                {
                    IMessageStream srv = new Checksum(new Framing(new StreamyFromConnection(b)));
                    srv.Initialize(isServer: true);
                    ready.Set();
                    var m = srv.Read().ToArray();
                    srv.Write(m);
                    srv.Dispose();
                }
                catch { }
                finally { done.Set(); }
            }) { IsBackground = true };
            t.Start();

            var cli = new Checksum(new Framing(new StreamyFromConnection(a)));
            cli.Initialize(isServer: false);
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)), $"server ready timeout, size={size}");

            cli.Write(payload);
            var back = cli.Read().ToArray();
            Assert.Equal(payload, back);
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            cli.Dispose();
            t.Join(5000);
            a.Close(); b.Close();
        }
    }

    // ── Crypto ──────────────────────────────────────────────────────

    [Fact]
    public void Crypto_UninitializedWriteThrows()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        var c = new Crypto(new Framing(new StreamyFromConnection(a)));
        Assert.Throws<InvalidOperationException>(() => c.Write(new byte[1]));
        c.Dispose();
    }

    [Fact]
    public void Crypto_UninitializedReadThrows()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        var c = new Crypto(new Framing(new StreamyFromConnection(a)));
        Assert.Throws<InvalidOperationException>(() => c.Read());
        c.Dispose();
    }

    [Fact]
    public void Crypto_VariousPayloadSizes()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var sizes = new[] { 0, 1, 16, 17, 31, 32, 33, 64, 1000 };
        foreach (var size in sizes)
        {
            var pair = InMemoryConnection.CreatePair();
            var payload = new byte[size];
            new Random(size).NextBytes(payload);

            var ready = new ManualResetEventSlim();
            var done = new ManualResetEventSlim();
            var t = new Thread(() =>
            {
                try
                {
                    var srv = new AfcpStackBuilder(pair.B).WithCrypto().Build(isServer: true);
                    ready.Set();
                    var m = srv.Read().ToArray();
                    srv.Write(m);
                    srv.Dispose();
                }
                catch { }
                finally { done.Set(); }
            }) { IsBackground = true };
            t.Start();

            var cli = new AfcpStackBuilder(pair.A).WithCrypto().Build(isServer: false);
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));

            cli.Write(payload);
            var back = cli.Read().ToArray();
            Assert.Equal(payload, back);
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            cli.Dispose();
            t.Join(5000);
            pair.A.Close();
            pair.B.Close();
        }
    }

    // ── Camouflage ──────────────────────────────────────────────────

    [Fact]
    public void Camouflage_DisconnectDuringHandshakeThrows()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var serverDone = new ManualResetEventSlim();
        var serverThread = new Thread(() =>
        {
            try
            {
                var srv = new Camouflage(new StreamyFromConnection(b));
                srv.Initialize(new StreamyParameters { IsServer = true });
            }
            catch (IOException)
            {
                // expected — peer disconnected during handshake
            }
            catch { }
            finally { serverDone.Set(); }
        }) { IsBackground = true };
        serverThread.Start();

        // Client: write part of the handshake, then close — server's ReadExact should throw
        var cli = new StreamyFromConnection(a);
        cli.Write("GET"u8); // incomplete request, then close
        a.Close();
        Assert.True(serverDone.Wait(TimeSpan.FromSeconds(5)));
        b.Close();
    }

    // ── Logger ──────────────────────────────────────────────────────

    [Fact]
    public void Logger_ComposesInStackDoesNotThrow()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var ready = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            try
            {
                var srv = new AfcpStackBuilder(b).WithLogger("srv").Build(isServer: true);
                ready.Set();
                var m = srv.Read().ToArray();
                srv.Write(m);
                srv.Dispose();
            }
            finally { done.Set(); }
        }) { IsBackground = true };
        t.Start();

        var cli = new AfcpStackBuilder(a).WithLogger("cli").Build(isServer: false);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));

        cli.Write("log test"u8);
        var back = cli.Read().ToArray();
        Assert.Equal("log test"u8.ToArray(), back);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        cli.Dispose();
        t.Join(5000);
        a.Close(); b.Close();
    }

    // ── StreamyFromStream / StreamFromStreamy ───────────────────────

    [Fact]
    public void StreamFromStreamy_CanSeekIsFalse()
    {
        var (a, _) = InMemoryConnection.CreatePair();
        var s = new StreamFromStreamy(new StreamyFromConnection(a));
        Assert.False(s.CanSeek);
        Assert.Throws<NotSupportedException>(() => s.Seek(0, System.IO.SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => s.SetLength(0));
    }

    [Fact]
    public void StreamyFromStream_OnDisconnectIsNoOp()
    {
        var ms = new System.IO.MemoryStream();
        var s = new StreamyFromStream(ms);
        var fired = false;
        s.OnDisconnect += () => fired = true;
        ms.Close();
        // OnDisconnect subscription is silently discarded; close does not fire event
        Assert.False(fired);
        // But subsequent reads will throw (delegated to the closed stream)
        Assert.Throws<ObjectDisposedException>(() => { var buf = new byte[1]; s.Read(buf); });
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static (Thread, ManualResetEventSlim, ManualResetEventSlim) StartServerEcho(
        InMemoryConnection conn, bool checksum)
    {
        var ready = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            try
            {
                IMessageStream srv = new Framing(new StreamyFromConnection(conn));
                if (checksum) srv = new Checksum(srv);
                srv.Initialize(isServer: true);
                ready.Set();
                while (true)
                {
                    var m = srv.Read();
                    if (m.Length == 0) break;
                    srv.Write(m);
                }
                srv.Dispose();
            }
            catch { }
            finally { done.Set(); }
        }) { IsBackground = true, Name = "AFCP.Test.EchoServer" };
        t.Start();
        return (t, ready, done);
    }

    public void Dispose() { }
}
