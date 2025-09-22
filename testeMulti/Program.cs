using System.Net;
using testeMulti.Networking;
using testeMulti.Request;

namespace testeMulti;

internal static class Program
{
    public static void Main2()
    {
        var ipEndpoint = new IPEndPoint(IPAddress.Loopback, 9999);

        var server = new TcpStreamServer(ipEndpoint);
        server.Start();

        new Thread(() =>
        {
            using var connection = server.HandleConnections();

            var data = new byte[3];
            connection.Read(data);

            Console.WriteLine(BitConverter.ToString(data));
            connection.Close();
        }).Start();


        using var client = new TcpStreamClient(ipEndpoint);
        using var con = client.CreateStream();

        con.Write([1, 2, 3]);
        con.Close();


        server.Stop();
        server.Dispose();
    }

    private static void Main()
    {
        var ipEndpoint = new IPEndPoint(IPAddress.Loopback, 9999);

        var server = new TcpStreamServer(ipEndpoint);
        server.Start();

        var requestServer = new RequestStreamServer(server);

        requestServer.OnRequest += (_, e) =>
        {
            var requestData = e.ReadRequest();
            Console.WriteLine(BitConverter.ToString(requestData.ToArray()));
            e.Answer([4, 5, 6]);
        };

        new Thread(() => { requestServer.HandleRequests(); }).Start();

        var client = new TcpStreamClient(ipEndpoint);
        var requestClient = new RequestStreamClient(client);
        var response = requestClient.SendRequest([1, 2, 3]);
        Console.WriteLine(BitConverter.ToString(response.ToArray()));

        client.Dispose();

        server.Stop();
        server.Dispose();
    }
}