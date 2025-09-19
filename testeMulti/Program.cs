


using System.Net;
using System.Net.Sockets;

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
    public RequestBasedStreamClient(IMultiStreamClient client)
    {

    }

    public abstract TOut SendRequest<TIn, TOut>(TIn data, CancellationToken ct = default);
}

public abstract class RequestBasedStreamServer
{
    public RequestBasedStreamServer(IMultiStreamServer server)
    {

    }

    //Conection handler
}

public class TcpStreamServer : IMultiStreamServer
{
    private TcpListener _listener;
    public TcpStreamServer(int port)
    {
        _listener = new TcpListener(System.Net.IPAddress.Any, port);
    }
    public void Start()
    {
        _listener.Start();
    }


    public IConnection HandleConnections(CancellationToken ct = default)
    {
        TcpClient client;
        using (ct.Register(_listener.Stop))
        {
            try
            {
                client = _listener.AcceptTcpClient();
                return new TcpConnection(client);
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

        throw new NotImplementedException();
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







internal class Program
{
    private static void Main(string[] args)
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
}