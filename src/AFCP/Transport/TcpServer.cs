using System.Net;
using System.Net.Sockets;

namespace AFCP;

/// <summary>
/// A TCP server: binds an <see cref="IPEndPoint"/>, accepts inbound connections
/// as <see cref="TcpConnection"/>s. The serve-side counterpart to
/// <see cref="TcpConnection"/>/client. Call <see cref="Accept"/> per peer (it
/// blocks until a connection arrives); <see cref="Stop"/> releases the listener.
///
/// Each accepted <see cref="IConnection"/> is handed to an
/// <see cref="AfcpStackBuilder"/> for its own stack — a server typically runs one
/// stack (and one <see cref="RequestChannel"/> reader loop) per accepted peer on
/// its own thread.
/// </summary>
public sealed class TcpServer : IDisposable
{
    private readonly TcpListener _listener;
    private int _stopped;

    public TcpServer(IPEndPoint endpoint)
    {
        _listener = new TcpListener(endpoint);
    }

    public TcpServer(IPAddress address, int port) : this(new IPEndPoint(address, port)) { }

    public IPEndPoint LocalEndpoint => (IPEndPoint)_listener.LocalEndpoint;

    public void Start() => _listener.Start();

    /// <summary>Block until a peer connects, then return its connection. Throws on stop.</summary>
    public IConnection Accept()
    {
        var client = _listener.AcceptTcpClient();
        return new TcpConnection(client);
    }

    /// <summary>Asynchronously accept a peer.</summary>
    public async Task<IConnection> AcceptAsync(CancellationToken ct = default)
    {
        var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        return new TcpConnection(client);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
        try { _listener.Stop(); } catch { }
    }

    public void Dispose() => Stop();
}
