using System.Net;
using System.Net.Sockets;
using testeMulti.Interfaces;

namespace testeMulti.Networking;

public class TcpStreamServer(IPEndPoint endpoint) : IMultiStreamServer
{
    private readonly TcpListener _listener = new(endpoint);

    public void Start()
    {
        _listener.Start();
    }

    public IConnection HandleConnections(CancellationToken ct = default)
    {
        var client = _listener.AcceptTcpClient();
        return new TcpConnection(client);
        /*
         using (ct.Register(_listener.Stop))
        {
            try
            {
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
        */
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

public class TcpStreamClient(IPEndPoint ipEndpoint) : IMultiStreamClient
{
    public IConnection CreateStream()
    {
        var client = new TcpClient();
        client.Connect(ipEndpoint);

        return new TcpConnection(client);
    }

    public void Dispose()
    {
    }
}