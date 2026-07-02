using System.Net;
using System.Net.Sockets;
using System.Text;
using AFCP;
using Xunit;

namespace AFCP.Tests;

public sealed class ExistingTests : IDisposable
{
    private static byte[] EchoPayload(byte[] x) => x;

    private static IMessageStream BuildClient(IConnection conn, bool checksum, bool crypto, bool camouflage)
    {
        var b = new AfcpStackBuilder(conn);
        if (camouflage) b.WithCamouflage();
        if (checksum) b.WithChecksum();
        if (crypto) b.WithCrypto();
        return b.Build(isServer: false);
    }

    // Build and return a server thread. The client must be built on the main
    // thread concurrently (so the ECDH handshake can complete), then wait for
    // the ready signal before exchanging messages, and finally wait for done.
    private static (Thread thread, ManualResetEventSlim ready, ManualResetEventSlim done)
        StartServer(IConnection conn, bool checksum, bool crypto, bool camouflage,
        Action<IMessageStream> handler)
    {
        var ready = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            try
            {
                var b = new AfcpStackBuilder(conn);
                if (camouflage) b.WithCamouflage();
                if (checksum) b.WithChecksum();
                if (crypto) b.WithCrypto();
                var srv = b.Build(isServer: true);
                ready.Set();
                handler(srv);
                srv.Dispose();
            }
            catch { }
            finally { done.Set(); }
        }) { IsBackground = true, Name = "AFCP.Test.Server" };
        t.Start();
        return (t, ready, done);
    }

    [Fact]
    public void FramingEchoInMemory()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "hello"u8.ToArray();

        var (t, ready, done) = StartServer(b, false, false, false, srv =>
        {
            var m = srv.Read().ToArray();
            srv.Write(m);
        });
        var cli = BuildClient(a, false, false, false);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(t.Join(5000));
        Assert.Equal(payload, back);
        cli.Dispose();
    }

    [Fact]
    public void FramingChecksumEchoInMemory()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "checksum me"u8.ToArray();

        var (t, ready, done) = StartServer(b, true, false, false, srv =>
        {
            var m = srv.Read().ToArray();
            srv.Write(m);
        });
        var cli = BuildClient(a, true, false, false);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(t.Join(5000));
        Assert.Equal(payload, back);
        cli.Dispose();
    }

    [Fact]
    public void FramingCryptoEchoInMemory()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "secret message"u8.ToArray();

        var (t, ready, done) = StartServer(b, false, true, false, srv =>
        {
            var m = srv.Read().ToArray();
            srv.Write(m);
        });
        var cli = BuildClient(a, false, true, false);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(t.Join(5000));
        Assert.Equal(payload, back);
        cli.Dispose();
    }

    [Fact]
    public void FramingChecksumCryptoEchoInMemory()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "the full stack"u8.ToArray();

        var (t, ready, done) = StartServer(b, true, true, false, srv =>
        {
            var m = srv.Read().ToArray();
            srv.Write(m);
        });
        var cli = BuildClient(a, true, true, false);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(t.Join(5000));
        Assert.Equal(payload, back);
        cli.Dispose();
    }

    [Fact]
    public void CamouflageFramingEchoInMemory()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "behind http"u8.ToArray();

        var (t, ready, done) = StartServer(b, false, false, true, srv =>
        {
            var m = srv.Read().ToArray();
            srv.Write(m);
        });
        var cli = BuildClient(a, false, false, true);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(t.Join(5000));
        Assert.Equal(payload, back);
        cli.Dispose();
    }

    [Fact]
    public void RequestChannelMultiplexInMemory()
    {
        var (a, b) = InMemoryConnection.CreatePair();

        var srvReady = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        RequestChannel? srvChan = null;
        var srvThread = new Thread(() =>
        {
            try
            {
                var (_, ch) = new AfcpStackBuilder(b).WithChecksum().WithCrypto().BuildWithRequestChannel(isServer: true);
                srvChan = ch;
                ch.OnRequest += ctx => ctx.Respond(EchoPayload(ctx.Payload.ToArray()));
                srvReady.Set();
            }
            catch { }
            finally { done.Set(); }
        }) { IsBackground = true };
        srvThread.Start();

        var (_, cliChan) = new AfcpStackBuilder(a).WithChecksum().WithCrypto().BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(10)));

        var p1 = "req-one"u8.ToArray();
        var p2 = "req-two"u8.ToArray();
        byte[]? r1 = null, r2 = null;
        var t1 = new Thread(() => r1 = cliChan.SendRequest(p1)) { IsBackground = true };
        var t2 = new Thread(() => r2 = cliChan.SendRequest(p2)) { IsBackground = true };
        t1.Start(); t2.Start();
        Assert.True(t1.Join(5000));
        Assert.True(t2.Join(5000));
        Assert.Equal(p1, r1);
        Assert.Equal(p2, r2);
        cliChan.Dispose();
        Assert.True(srvThread.Join(5000));
        srvChan?.Dispose();
    }

    [Fact]
    public void TcpLoopbackRequestChannel()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var srvReady = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        RequestChannel? srvChan = null;
        var serverThread = new Thread(() =>
        {
            try
            {
                var tcp = listener.AcceptTcpClient();
                var conn = new TcpConnection(tcp);
                var (_, ch) = new AfcpStackBuilder(conn).WithChecksum().WithCrypto().BuildWithRequestChannel(isServer: true);
                srvChan = ch;
                ch.OnRequest += ctx => ctx.Respond(EchoPayload(ctx.Payload.ToArray()));
                srvReady.Set();
            }
            catch { }
            finally { done.Set(); }
        }) { IsBackground = true };
        serverThread.Start();

        var cliConn = new TcpConnection(new IPEndPoint(IPAddress.Loopback, port));
        var (_, cliChan) = new AfcpStackBuilder(cliConn).WithChecksum().WithCrypto().BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(10)));

        var payload = "over tcp"u8.ToArray();
        var resp = cliChan.SendRequest(payload);
        Assert.Equal(payload, resp);
        cliChan.Dispose();
        Assert.True(serverThread.Join(5000));
        srvChan?.Dispose();
        listener.Stop();
    }

    [Fact]
    public void DisconnectHandlingInMemory()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var cli = BuildClient(a, false, false, false);
        Assert.True(cli.IsConnected);

        var disconnected = new ManualResetEventSlim();
        cli.OnDisconnect += () => disconnected.Set();

        b.Close();
        var empty = cli.Read(); // triggers disconnect detection on this side
        Assert.True(disconnected.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(cli.IsConnected);
        Assert.True(empty.Length == 0);

        cli.Dispose();
    }

    [Fact]
    public void ChecksumDetectsCorruption()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var srvFraming = new Framing(new StreamyFromConnection(b));
        srvFraming.Initialize(isServer: true);
        var cli = new AfcpStackBuilder(a).WithChecksum().Build(isServer: false);

        Exception? caught = null;
        var done = new ManualResetEventSlim();
        var st = new Thread(() =>
        {
            try { cli.Read(); }
            catch (InvalidDataException ex) { caught = ex; }
            catch (Exception ex) { caught = ex; }
            finally { done.Set(); }
        }) { IsBackground = true };
        st.Start();

        srvFraming.Write(new byte[] { 0xAA, 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(st.Join(5000));
        Assert.IsType<InvalidDataException>(caught);
        cli.Dispose();
        srvFraming.Dispose();
    }

    [Fact]
    public void CustomXorTransformerEcho()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "transform me"u8.ToArray();
        const byte key = 0x5A;

        var done = new ManualResetEventSlim();
        var st = new Thread(() =>
        {
            try
            {
                var srv = new Framing(new XorTransformer(new StreamyFromConnection(b), key));
                srv.Initialize(isServer: true);
                var m = srv.Read().ToArray();
                srv.Write(m);
                srv.Dispose();
            }
            catch { }
            finally { done.Set(); }
        }) { IsBackground = true };
        st.Start();

        var cli = new Framing(new XorTransformer(new StreamyFromConnection(a), key));
        cli.Initialize(isServer: false);
        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(st.Join(5000));
        Assert.Equal(payload, back);
        cli.Dispose();
    }

    [Fact]
    public void StreamAdapterRoundTrip()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "via stream adapter"u8.ToArray();

        var done = new ManualResetEventSlim();
        var st = new Thread(() =>
        {
            try
            {
                Streamy baseSrv = new StreamyFromConnection(b);
                baseSrv = new StreamyFromStream(new StreamFromStreamy(baseSrv));
                var srv = new Framing(baseSrv);
                srv.Initialize(isServer: true);
                var m = srv.Read().ToArray();
                srv.Write(m);
                srv.Dispose();
            }
            catch { }
            finally { done.Set(); }
        }) { IsBackground = true };
        st.Start();

        Streamy baseCli = new StreamyFromConnection(a);
        baseCli = new StreamyFromStream(new StreamFromStreamy(baseCli));
        var cli = new Framing(baseCli);
        cli.Initialize(isServer: false);
        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(st.Join(5000));
        Assert.Equal(payload, back);
        cli.Dispose();
    }

    [Fact]
    public void TcpServerAcceptEcho()
    {
        var server = new TcpServer(IPAddress.Loopback, 0);
        server.Start();
        var port = server.LocalEndpoint.Port;

        var srvReady = new ManualResetEventSlim();
        RequestChannel? srvChan = null;
        var serverThread = new Thread(() =>
        {
            try
            {
                var conn = server.Accept();
                var (_, ch) = new AfcpStackBuilder(conn).WithChecksum().WithCrypto().BuildWithRequestChannel(isServer: true);
                srvChan = ch;
                ch.OnRequest += ctx => ctx.Respond(ctx.Payload.ToArray());
                srvReady.Set();
            }
            catch { }
        }) { IsBackground = true };
        serverThread.Start();

        var cliConn = new TcpConnection(new IPEndPoint(IPAddress.Loopback, port));
        var (_, cliChan) = new AfcpStackBuilder(cliConn).WithChecksum().WithCrypto().BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(10)));

        var payload = "via tcp server"u8.ToArray();
        var resp = cliChan.SendRequest(payload);
        Assert.Equal(payload, resp);
        cliChan.Dispose();
        Assert.True(serverThread.Join(5000));
        srvChan?.Dispose();
        server.Dispose();
    }

    public void Dispose() { }

    private sealed class XorTransformer : StreamyTransformer
    {
        private readonly byte _key;
        public XorTransformer(Streamy baseStream, byte key) : base(baseStream) => _key = key;

        public override int Read(Span<byte> buffer)
        {
            var n = Base.Read(buffer);
            for (int i = 0; i < n; i++) buffer[i] ^= _key;
            return n;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var xored = new byte[buffer.Length];
            for (int i = 0; i < buffer.Length; i++) xored[i] = (byte)(buffer[i] ^ _key);
            Base.Write(xored);
        }
    }
}
