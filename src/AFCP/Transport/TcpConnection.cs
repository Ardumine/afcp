using System.Net.Sockets;
using System.Net;

namespace AFCP;

/// <summary>
/// TCP transport — covers WiFi and Ethernet (TCP over any network interface).
/// Sets <see cref="TcpClient.NoDelay"/> to defeat Nagle/delayed-ACK latency on
/// small framed messages (the original V2-port fix, kept).
///
/// Construct either from an accepted <see cref="TcpClient"/> (server side) or by
/// connecting to an <see cref="IPEndPoint"/> (client side).
/// </summary>
public sealed class TcpConnection : IConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private int _disposed;
    private int _disconnected;

    public TcpConnection(TcpClient client)
    {
        client.NoDelay = true;
        _client = client;
        _stream = client.GetStream();
    }

    public TcpConnection(IPEndPoint endpoint) : this(Connect(endpoint)) { }

    private static TcpClient Connect(IPEndPoint endpoint)
    {
        var c = new TcpClient();
        c.Connect(endpoint);
        return c;
    }

    public bool IsConnected => _disposed == 0 && _client.Connected && _stream.Socket.Connected;

    public event Action? OnDisconnect;

    public int Read(Span<byte> buffer)
    {
        try
        {
            var n = _stream.Read(buffer);
            if (n == 0) RaiseDisconnect(); // EOF
            return n;
        }
        catch
        {
            RaiseDisconnect();
            return 0;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        try { _stream.Write(data); }
        catch { RaiseDisconnect(); throw; }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _stream.Dispose(); _client.Dispose(); } catch { }
        RaiseDisconnect();
    }

    private void RaiseDisconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) == 1) return;
        try { OnDisconnect?.Invoke(); } catch { }
    }

    public void Dispose() => Close();
}
