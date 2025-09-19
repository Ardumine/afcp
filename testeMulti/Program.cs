using System.Net;
using System.Net.Sockets;
using AFCP.Core.Utils;

namespace testeMulti;

public interface IConnection : IDisposable
{
    /// <summary>
    /// Writes data to the connection.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public int Write(ReadOnlySpan<byte> data);

    /// <summary>
    /// Reads data from the connection.
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public int Read(Span<byte> buffer);

    public void Close();
}

public interface IMultiStreamServer : IDisposable
{
    /// <summary>
    /// Handles the creation of new streams
    /// </summary>
    /// <returns></returns>
    public void Start();

    public IConnection HandleConnections(CancellationToken ct = default);
    public void Stop();
}

public interface IMultiStreamClient : IDisposable
{
    /// <summary>
    /// Opens a new stream on the connection.
    /// </summary>
    /// <returns></returns>
    public IConnection CreateStream();
}

public abstract class RequestBasedStreamClient
{
    private protected IMultiStreamClient _multiStreamClient;

    public RequestBasedStreamClient(IMultiStreamClient client)
    {
        _multiStreamClient = client;
    }

    public abstract ReadOnlySpan<byte> SendRequest(ReadOnlySpan<byte> requestData, CancellationToken ct = default);
}

public abstract class RequestBasedStreamServer
{
    public delegate void RequestEvent(object sender, RequestEventArgs e);

    public event RequestEvent? OnRequest;
    protected IMultiStreamServer _multiStreamServer;

    public RequestBasedStreamServer(IMultiStreamServer server)
    {
        _multiStreamServer = server;
    }

    protected virtual void RaiseRequestEvent(RequestEventArgs e)
    {
        OnRequest?.Invoke(this, e);
    }
}

public class TcpStreamServer : IMultiStreamServer
{
    private TcpListener _listener;

    public TcpStreamServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        _listener.Start();
    }


    public IConnection HandleConnections(CancellationToken ct = default)
    {
        var client = _listener.AcceptTcpClient();
        return new TcpConnection(client);
        using (ct.Register(_listener.Stop))
        {
            try
            {
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted)
            {
                throw new OperationCanceledException(ct);
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }
    }

    public void Stop()
    {
        _listener.Stop();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}

public class TcpStreamClient : IMultiStreamClient
{
    private IPEndPoint _ipEndpoint;

    public TcpStreamClient(IPEndPoint ipEndpoint)
    {
        _ipEndpoint = ipEndpoint;
    }

    public IConnection CreateStream()
    {
        var client = new TcpClient();
        client.Connect(_ipEndpoint);

        return new TcpConnection(client);
    }

    public void Dispose()
    {
    }
}

public class TcpConnection : IConnection
{
    private NetworkStream _stream;
    private TcpClient _client;

    internal TcpConnection(TcpClient client)
    {
        _client = client;
        _stream = _client.GetStream();
    }

    public int Read(Span<byte> buffer)
    {
        return _stream.Read(buffer);
    }

    public int Write(ReadOnlySpan<byte> data)
    {
        _stream.Write(data);
        return data.Length;
    }

    public void Close()
    {
        _stream.Close();
        _client.Close();
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
        _stream = null;
        _client = null;
    }
}

public interface ICountableStream
{
    public void Write(ReadOnlySpan<byte> data);

    public ReadOnlySpan<byte> Read();
}

public class DataCompletionStream : ICountableStream
{
    private IConnection _connection;

    public DataCompletionStream(IConnection connection)
    {
        _connection = connection;
    }

    private uint ReadDataCount()
    {
        Span<byte> bufferLen = stackalloc byte[4];
        _connection.Read(bufferLen);
        return Tools.GetUInt(bufferLen);
    }

    public ReadOnlySpan<byte> Read()
    {
        var totalBytes = (int)ReadDataCount();
        Span<byte> actualData = new byte[totalBytes];

        int bytesAlreadyRead = 0;
        var bytesUnread = totalBytes;
        while (bytesUnread > 0)
        {
            bytesAlreadyRead += _connection.Read(actualData.Slice(bytesAlreadyRead, bytesUnread));
            bytesUnread = totalBytes - bytesAlreadyRead;
        }

        return actualData;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        _connection.Write(Tools.GetBytes((uint)data.Length));
        _connection.Write(data);
    }
}

public class RequestEventArgs : EventArgs
{
    private DataCompletionStream _stream;

    public RequestEventArgs(DataCompletionStream stream)
    {
        _stream = stream;
    }

    public ReadOnlySpan<byte> ReadRequest()
    {
        return _stream.Read();
    }

    public void Answer(ReadOnlySpan<byte> data)
    {
        _stream.Write(data);
    }
}

public class RequestStreamServer : RequestBasedStreamServer
{
    public RequestStreamServer(IMultiStreamServer server) : base(server)
    {
    }

    public void HandleRequests()
    {
        var client = _multiStreamServer.HandleConnections();
        var stream = new DataCompletionStream(client);
        RaiseRequestEvent(new RequestEventArgs(stream));
        client.Close();
        client.Dispose();
    }
}

public class RequestStreamClient : RequestBasedStreamClient
{
    public RequestStreamClient(IMultiStreamClient client) : base(client)
    {
    }

    public override ReadOnlySpan<byte> SendRequest(ReadOnlySpan<byte> requestData, CancellationToken ct = default)
    {
        var client = _multiStreamClient.CreateStream();

        var stream = new DataCompletionStream(client);
        stream.Write(requestData);
        var data = stream.Read();


        client.Close();
        client.Dispose();

        return data;
    }
}

internal class Program
{
    public static void Main2()
    {
        using var server = new TcpStreamServer(9999);
        server.Start();

        new Thread(() =>
        {
            using var connection = server.HandleConnections();

            var data = new byte[3];
            connection.Read(data);

            Console.WriteLine(BitConverter.ToString(data));
            connection.Close();
        }).Start();


        using var client = new TcpStreamClient(new IPEndPoint(IPAddress.Loopback, 9999));
        using var con = client.CreateStream();

        con.Write([1, 2, 3]);
        con.Close();


        server.Stop();
    }

    private static void Main()
    {
        byte[] resp = [4, 5, 6];
        byte[] req = [1, 2, 3];
        var ipEndpoint = new IPEndPoint(IPAddress.Loopback, 9999);

        RequestStreamServer requestServer = null!;

        Thread f = null;
        for (int i = 0; i < 100000; i++)
        {
            var server = new TcpStreamServer(9999);
            server.Start();

            requestServer = new RequestStreamServer(server);
         
            requestServer.OnRequest += (_, e) =>
            {
                //Console.WriteLine(BitConverter.ToString(requestData.ToArray()));
                e.Answer(e.ReadRequest());
            };

            f = new Thread(() =>
            {
                requestServer.HandleRequests();
            });
            f.Start();

            var client = new TcpStreamClient(ipEndpoint);
            var requestClient = new RequestStreamClient(client);
            requestClient.SendRequest(req);
            //Console.WriteLine(BitConverter.ToString(response.ToArray()));

            client.Dispose();

            server.Stop();
            server.Dispose();
        }

        Console.ReadKey();
    }
}