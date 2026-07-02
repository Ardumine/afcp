using AFCP;

// End-to-end tests for the unified AFCP byte-stream stack.
// Server stacks are always built inside the server thread so role-aware
// handshakes (camouflage, ECDH) run concurrently with the client's Build.

var failures = new List<string>();
var passed = 0;

void Pass(string name) { passed++; Console.WriteLine($"  [ok] {name}"); }
void Fail(string name, string why) { failures.Add($"[FAIL] {name}: {why}"); Console.WriteLine($"  [FAIL] {name}: {why}"); }

// Build a server stack on a background thread, run a handler, and signal when done.
// The handler receives the built server IMessageStream.
Thread RunServer(IConnection conn, bool checksum, bool crypto, bool camouflage,
    Action<IMessageStream> handler, string name)
{
    var t = new Thread(() =>
    {
        try
        {
            var b = new AfcpStackBuilder(conn);
            if (camouflage) b.WithCamouflage();
            if (checksum) b.WithChecksum();
            if (crypto) b.WithCrypto();
            var srv = b.Build(isServer: true);
            handler(srv);
            srv.Dispose();
        }
        catch (Exception ex) { Fail(name, $"server threw {ex.GetType().Name}: {ex.Message}"); }
    })
    { IsBackground = true, Name = $"srv:{name}" };
    t.Start();
    return t;
}

IMessageStream BuildClient(IConnection conn, bool checksum, bool crypto, bool camouflage)
{
    var b = new AfcpStackBuilder(conn);
    if (camouflage) b.WithCamouflage();
    if (checksum) b.WithChecksum();
    if (crypto) b.WithCrypto();
    return b.Build(isServer: false);
}

static byte[] Echo(byte[] x) => x;

// ---------------------------------------------------------------------------
Console.WriteLine("1. Framing only (in-memory)");
{
    var (a, b) = InMemoryConnection.CreatePair();
    var payload = "hello"u8.ToArray();
    var st = RunServer(b, false, false, false, srv => { var m = srv.Read().ToArray(); srv.Write(m); }, "1");
    var cli = BuildClient(a, false, false, false);
    cli.Write(payload);
    var back = cli.Read().ToArray();
    st.Join(2000);
    if (back.SequenceEqual(payload)) Pass("framing echo"); else Fail("framing echo", $"got {BitConverter.ToString(back)}");
    cli.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine("2. Framing + Checksum (in-memory)");
{
    var (a, b) = InMemoryConnection.CreatePair();
    var payload = "checksum me"u8.ToArray();
    var st = RunServer(b, true, false, false, srv => { var m = srv.Read().ToArray(); srv.Write(m); }, "2");
    var cli = BuildClient(a, true, false, false);
    cli.Write(payload);
    var back = cli.Read().ToArray();
    st.Join(2000);
    if (back.SequenceEqual(payload)) Pass("checksum echo"); else Fail("checksum echo", $"got {BitConverter.ToString(back)}");
    cli.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine("3. Framing + Crypto (in-memory, ECDH handshake)");
{
    var (a, b) = InMemoryConnection.CreatePair();
    var payload = "secret message"u8.ToArray();
    var st = RunServer(b, false, true, false, srv => { var m = srv.Read().ToArray(); srv.Write(m); }, "3");
    var cli = BuildClient(a, false, true, false);
    cli.Write(payload);
    var back = cli.Read().ToArray();
    st.Join(3000);
    if (back.SequenceEqual(payload)) Pass("crypto echo"); else Fail("crypto echo", $"got {BitConverter.ToString(back)}");
    cli.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine("4. Framing + Checksum + Crypto (in-memory, full stack)");
{
    var (a, b) = InMemoryConnection.CreatePair();
    var payload = "the full stack"u8.ToArray();
    var st = RunServer(b, true, true, false, srv => { var m = srv.Read().ToArray(); srv.Write(m); }, "4");
    var cli = BuildClient(a, true, true, false);
    cli.Write(payload);
    var back = cli.Read().ToArray();
    st.Join(3000);
    if (back.SequenceEqual(payload)) Pass("full-stack echo"); else Fail("full-stack echo", $"got {BitConverter.ToString(back)}");
    cli.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine("5. Camouflage + Framing (in-memory, HTTP disguise handshake)");
{
    var (a, b) = InMemoryConnection.CreatePair();
    var payload = "behind http"u8.ToArray();
    var st = RunServer(b, false, false, true, srv => { var m = srv.Read().ToArray(); srv.Write(m); }, "5");
    var cli = BuildClient(a, false, false, true);
    cli.Write(payload);
    var back = cli.Read().ToArray();
    st.Join(3000);
    if (back.SequenceEqual(payload)) Pass("camouflage echo"); else Fail("camouflage echo", $"got {BitConverter.ToString(back)}");
    cli.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine("6. RequestChannel (in-memory, full stack + req/resp multiplex)");
{
    var (a, b) = InMemoryConnection.CreatePair();

    RequestChannel? srvChan = null;
    var srvThread = new Thread(() =>
    {
        try
        {
            var (_, ch) = new AfcpStackBuilder(b).WithChecksum().WithCrypto().WithRequestChannel().BuildWithRequestChannel(isServer: true);
            srvChan = ch;
            ch.OnRequest += ctx => ctx.Respond(Echo(ctx.Payload.ToArray()));
        }
        catch (Exception ex) { Fail("6", $"server threw {ex.GetType().Name}: {ex.Message}"); }
    }) { IsBackground = true };
    srvThread.Start();

    var (_, cliChan) = new AfcpStackBuilder(a).WithChecksum().WithCrypto().WithRequestChannel().BuildWithRequestChannel(isServer: false);

    var p1 = "req-one"u8.ToArray();
    var p2 = "req-two"u8.ToArray();
    byte[]? r1 = null, r2 = null;
    var t1 = new Thread(() => r1 = cliChan.SendRequest(p1)) { IsBackground = true };
    var t2 = new Thread(() => r2 = cliChan.SendRequest(p2)) { IsBackground = true };
    t1.Start(); t2.Start();
    t1.Join(5000); t2.Join(5000);
    if (r1 != null && r1.SequenceEqual(p1) && r2 != null && r2.SequenceEqual(p2)) Pass("request channel multiplex");
    else Fail("request channel multiplex", $"r1={r1?.Length ?? -1}bytes r2={r2?.Length ?? -1}bytes");
    cliChan.Dispose();
    srvThread.Join(2000);
    srvChan?.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine("7. TCP loopback (framing + checksum + crypto + RequestChannel)");
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

    RequestChannel? srvChan = null;
    var serverThread = new Thread(() =>
    {
        try
        {
            var tcp = listener.AcceptTcpClient();
            var conn = new TcpConnection(tcp);
            var (_, ch) = new AfcpStackBuilder(conn).WithChecksum().WithCrypto().WithRequestChannel().BuildWithRequestChannel(isServer: true);
            srvChan = ch;
            ch.OnRequest += ctx => ctx.Respond(Echo(ctx.Payload.ToArray()));
        }
        catch (Exception ex) { Fail("7", $"server threw {ex.GetType().Name}: {ex.Message}"); }
    }) { IsBackground = true };
    serverThread.Start();

    var cliConn = new TcpConnection(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
    var (_, cliChan) = new AfcpStackBuilder(cliConn).WithChecksum().WithCrypto().WithRequestChannel().BuildWithRequestChannel(isServer: false);

    Thread.Sleep(500);
    var payload = "over tcp"u8.ToArray();
    var resp = cliChan.SendRequest(payload);
    if (resp.SequenceEqual(payload)) Pass("tcp loopback echo"); else Fail("tcp loopback echo", $"got {BitConverter.ToString(resp)}");
    cliChan.Dispose();
    serverThread.Join(2000);
    srvChan?.Dispose();
    listener.Stop();
}

// ---------------------------------------------------------------------------
Console.WriteLine("8. Disconnect handling (in-memory, IsConnected + OnDisconnect)");
{
    var (a, b) = InMemoryConnection.CreatePair();
    var cli = BuildClient(a, false, false, false);
    var srv = BuildClient(b, false, false, false); // no handshake — safe to build inline
    var cliDisconnected = false;
    cli.OnDisconnect += () => cliDisconnected = true;
    if (!cli.IsConnected) { Fail("disconnect", "expected connected at start"); }
    else
    {
        b.Close();
        Thread.Sleep(200);
        var empty = cli.Read();
        if (empty.Length == 0 && cliDisconnected) Pass("disconnect signal"); else Fail("disconnect signal", $"empty={empty.Length} notified={cliDisconnected}");
    }
    cli.Dispose(); srv.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine("9. Checksum detects corruption");
{
    var (a, b) = InMemoryConnection.CreatePair();
    // Server uses bare Framing (no checksum) to inject a frame with a bad checksum body.
    var srvFraming = new Framing(new StreamyFromConnection(b));
    srvFraming.Initialize(isServer: true);
    var cli = new AfcpStackBuilder(a).WithChecksum().Build(isServer: false);
    var threw = false;
    var st = new Thread(() =>
    {
        try { cli.Read(); }
        catch (InvalidDataException) { threw = true; }
        catch (Exception ex) { Fail("9", $"unexpected {ex.GetType().Name}: {ex.Message}"); }
    }) { IsBackground = true };
    st.Start();
    // body=0xAA, trailing 4 bytes = a wrong checksum (0xDEADBEEF)
    srvFraming.Write(new byte[] { 0xAA, 0xDE, 0xAD, 0xBE, 0xEF });
    st.Join(2000);
    if (threw) Pass("checksum corruption detected"); else Fail("checksum corruption detected", "no exception thrown");
    cli.Dispose(); srvFraming.Dispose();
}

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine($"passed: {passed}");
Console.WriteLine($"failed: {failures.Count}");
foreach (var f in failures) Console.WriteLine(f);
if (failures.Count > 0) Environment.Exit(1);
Console.WriteLine("ALL OK");
