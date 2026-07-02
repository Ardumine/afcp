using System.Text;
using AFCP;
using Xunit;

namespace AFCP.Tests;

public sealed class LayerCombinationTests : IDisposable
{
    private static (Thread, ManualResetEventSlim, ManualResetEventSlim) StartServer(
        IConnection conn, bool camouflage, bool checksum, bool crypto,
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
        }) { IsBackground = true, Name = "AFCP.Test.ComboServer" };
        t.Start();
        return (t, ready, done);
    }

    private static IMessageStream BuildClient(IConnection conn, bool camouflage, bool checksum, bool crypto)
    {
        var b = new AfcpStackBuilder(conn);
        if (camouflage) b.WithCamouflage();
        if (checksum) b.WithChecksum();
        if (crypto) b.WithCrypto();
        return b.Build(isServer: false);
    }

    private static void EchoTest(string name, bool camouflage, bool checksum, bool crypto)
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = Encoding.UTF8.GetBytes(name);

        var (t, ready, done) = StartServer(b, camouflage, checksum, crypto, srv =>
        {
            var m = srv.Read().ToArray();
            srv.Write(m);
        });
        var cli = BuildClient(a, camouflage, checksum, crypto);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), $"{name}: server build timeout");
        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.Equal(payload, back);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        t.Join(5000);
        cli.Dispose();
        a.Close(); b.Close();
    }

    private static (RequestChannel cliChan, RequestChannel srvChan, Thread srvThread, IConnection a, IConnection b)
        BuildRequestChannelPair(bool camouflage, bool checksum, bool crypto)
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var srvReady = new ManualResetEventSlim();
        RequestChannel? srvChan = null;
        var srvThread = new Thread(() =>
        {
            try
            {
                var bld = new AfcpStackBuilder(b);
                if (camouflage) bld.WithCamouflage();
                if (checksum) bld.WithChecksum();
                if (crypto) bld.WithCrypto();
                var (_, ch) = bld.BuildWithRequestChannel(isServer: true);
                srvChan = ch;
                ch.OnRequest += ctx => ctx.Respond(ctx.Payload.ToArray());
                srvReady.Set();
            }
            catch { }
        }) { IsBackground = true };
        srvThread.Start();

        var bld2 = new AfcpStackBuilder(a);
        if (camouflage) bld2.WithCamouflage();
        if (checksum) bld2.WithChecksum();
        if (crypto) bld2.WithCrypto();
        var (_, cliChan) = bld2.BuildWithRequestChannel(isServer: false);
        Assert.True(srvReady.Wait(TimeSpan.FromSeconds(10)));
        return (cliChan, srvChan!, srvThread, a, b);
    }

    // ── Layer combinations ──────────────────────────────────────────

    [Fact] public void CamouflageChecksumEcho() => EchoTest("C+CS", true, true, false);
    [Fact] public void CamouflageCryptoEcho() => EchoTest("C+K", true, false, true);
    [Fact] public void CamouflageChecksumCryptoEcho() => EchoTest("C+CS+K", true, true, true);
    [Fact] public void FramingOnlyRequestChannel() => EchoTest("F+RC", false, false, false);
    [Fact] public void ChecksumOnlyRequestChannel() => EchoTest("CS+RC", false, true, false);

    [Fact]
    public void CamouflageRequestChannel()
    {
        var (cliChan, srvChan, srvThread, a, b) = BuildRequestChannelPair(true, false, false);
        var payload = "camouflaged request"u8.ToArray();
        var resp = cliChan.SendRequest(payload);
        Assert.Equal(payload, resp);
        cliChan.Dispose();
        srvThread.Join(5000);
        srvChan.Dispose();
        a.Close(); b.Close();
    }

    [Fact]
    public void CamouflageChecksumCryptoRequestChannel()
    {
        var (cliChan, srvChan, srvThread, a, b) = BuildRequestChannelPair(true, true, true);
        var payload = "full stack request"u8.ToArray();
        var resp = cliChan.SendRequest(payload);
        Assert.Equal(payload, resp);
        cliChan.Dispose();
        srvThread.Join(5000);
        srvChan.Dispose();
        a.Close(); b.Close();
    }

    // ── Custom MessageTransformer (extensibility) ───────────────────

    [Fact]
    public void CustomMessageTransformer_ReverseBytes()
    {
        var (a, b) = InMemoryConnection.CreatePair();
        var payload = "reversed"u8.ToArray();

        var done = new ManualResetEventSlim();
        var st = new Thread(() =>
        {
            try
            {
                var srvFraming = new Framing(new StreamyFromConnection(b));
                srvFraming.Initialize(isServer: true);
                var srv = new ReverseTransformer(srvFraming);
                var m = srv.Read().ToArray();
                srv.Write(m);
                srv.Dispose();
            }
            catch { }
            finally { done.Set(); }
        }) { IsBackground = true };
        st.Start();

        var cliFraming = new Framing(new StreamyFromConnection(a));
        cliFraming.Initialize(isServer: false);
        var cli = new ReverseTransformer(cliFraming);

        cli.Write(payload);
        var back = cli.Read().ToArray();
        Assert.Equal(payload, back);
        cli.Dispose();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        st.Join(5000);
        a.Close(); b.Close();
    }

    private sealed class ReverseTransformer : MessageTransformer
    {
        public ReverseTransformer(IMessageStream baseStream) : base(baseStream) { }
        public override void Write(ReadOnlySpan<byte> message)
        {
            var rev = message.ToArray();
            Array.Reverse(rev);
            Base.Write(rev);
        }
        public override ReadOnlySpan<byte> Read()
        {
            var data = Base.Read();
            if (data.Length == 0) return data;
            return data.ToArray().Reverse().ToArray();
        }
    }

    public void Dispose() { }
}
