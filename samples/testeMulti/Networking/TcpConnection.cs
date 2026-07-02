using System.Net;
using System.Net.Sockets;
using testeMulti.Interfaces;

namespace testeMulti.Networking;

public class TcpConnection : IConnection
{
    private readonly NetworkStream _stream;
    private readonly TcpClient _client;

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