using System.Net;
using System.Text;
using AFCP;

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8001;
var endpoint = new IPEndPoint(IPAddress.Loopback, port);

// --- Request server ---
var server = new TcpServer(endpoint);
server.Start();
Console.WriteLine($"Request server listening on {server.LocalEndpoint}");

var serverReady = new ManualResetEventSlim();
RequestChannel? srvChan = null;
var serverThread = new Thread(() =>
{
    try
    {
        var conn = server.Accept();
        Console.WriteLine("Server: client connected.");
        var (_, ch) = new AfcpStackBuilder(conn)
            .WithChecksum()
            .WithCrypto()
            .BuildWithRequestChannel(isServer: true);
        srvChan = ch;
        ch.OnRequest += ctx =>
        {
            var text = Encoding.UTF8.GetString(ctx.Payload);
            Console.WriteLine($"Server received request: {text}");
            var reply = Encoding.UTF8.GetBytes($"Echo: {text}");
            ctx.Respond(reply);
        };
        serverReady.Set();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Server error: {ex.Message}");
        serverReady.Set();
    }
}) { IsBackground = true, Name = "AFCP.Sample.RequestServer" };
serverThread.Start();

serverReady.Wait(TimeSpan.FromSeconds(5));

// --- Request client ---
var conn = new TcpConnection(endpoint);
var (_, cliChan) = new AfcpStackBuilder(conn)
    .WithChecksum()
    .WithCrypto()
    .BuildWithRequestChannel(isServer: false);

var request = "ping"u8;
Console.WriteLine($"Client sending request: {Encoding.UTF8.GetString(request)}");
var response = cliChan.SendRequest(request);
Console.WriteLine($"Client received response: {Encoding.UTF8.GetString(response)}");

cliChan.Dispose();
conn.Close();

serverThread.Join(2000);
srvChan?.Dispose();
server.Stop();
Console.WriteLine("Done.");
