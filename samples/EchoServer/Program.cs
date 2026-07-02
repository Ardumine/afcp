using System.Net;
using AFCP;

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8000;
var endpoint = new IPEndPoint(IPAddress.Loopback, port);

// --- Server ---
var server = new TcpServer(endpoint);
server.Start();
Console.WriteLine($"Server listening on {server.LocalEndpoint}");

var serverDone = new TaskCompletionSource();
var serverThread = new Thread(() =>
{
    try
    {
        var conn = server.Accept();
        Console.WriteLine("Server: client connected.");

        var msgStream = new AfcpStackBuilder(conn)
            .WithChecksum()
            .WithCrypto()
            .Build(isServer: true);

        while (msgStream.IsConnected)
        {
            var data = msgStream.Read();
            if (data.Length == 0) break;
            var text = System.Text.Encoding.UTF8.GetString(data);
            Console.WriteLine($"Server received: {text}");
            msgStream.Write(data); // echo
        }

        msgStream.Dispose();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Server error: {ex.Message}");
    }
    finally
    {
        serverDone.SetResult();
    }
}) { IsBackground = true, Name = "AFCP.Sample.Server" };
serverThread.Start();

// --- Client ---
var conn = new TcpConnection(endpoint);
var cli = new AfcpStackBuilder(conn)
    .WithChecksum()
    .WithCrypto()
    .Build(isServer: false);

var message = "Hello, AFCP!"u8;
Console.WriteLine($"Client sending: {System.Text.Encoding.UTF8.GetString(message)}");
cli.Write(message);

var echo = cli.Read();
Console.WriteLine($"Client received: {System.Text.Encoding.UTF8.GetString(echo)}");

cli.Dispose();
conn.Close();

serverDone.Task.Wait(TimeSpan.FromSeconds(5));
server.Stop();
Console.WriteLine("Done.");
