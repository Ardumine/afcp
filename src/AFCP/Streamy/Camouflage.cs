using System.Text;

namespace AFCP;

/// <summary>
/// A byte-stream decorator that disguises the link as an HTTP connection to a
/// passive middlebox. During <see cref="Initialize"/>, the client sends a fake
/// <c>GET</c> request and the server replies with a fake <c>200</c> chunked
/// response; afterwards the stream is transparent — framed/encrypted bytes ride
/// inside what looks like an HTTP body.
///
/// Optional layer. Composes below <see cref="Framing"/> (camouflage is
/// byte-level; it must handshake before message framing starts).
/// </summary>
public sealed class Camouflage : Streamy
{
    private static readonly byte[] _serverResponse = """
        HTTP/1.1 200 OK
        Content-Type: application/octet-stream
        Transfer-Encoding: chunked
        Server: nginx/1.18.0
        Connection: keep-alive

        """u8.ToArray();

    private static readonly byte[] _clientRequest = """
        GET /api/stream HTTP/1.1
        Host: example.com
        Accept: application/octet-stream
        Connection: keep-alive

        """u8.ToArray();

    private readonly Streamy _base;

    public Camouflage(Streamy baseStream) => _base = baseStream;

    public override Streamy Initialize(StreamyParameters parameters)
    {
        var buf = new byte[Math.Max(_clientRequest.Length, _serverResponse.Length)];
        if (parameters.IsServer)
        {
            // Server: read the client's request, send the response.
            ReadExact(_base, buf, _clientRequest.Length);
            _base.Write(_serverResponse);
        }
        else
        {
            // Client: send request, read response.
            _base.Write(_clientRequest);
            ReadExact(_base, buf, _serverResponse.Length);
        }
        _base.Initialize(parameters);
        return this;
    }

    public override int Read(Span<byte> buffer) => _base.Read(buffer);
    public override void Write(ReadOnlySpan<byte> buffer) => _base.Write(buffer);
    public override bool IsConnected => _base.IsConnected;
    public override event Action? OnDisconnect { add => _base.OnDisconnect += value; remove => _base.OnDisconnect -= value; }

    private static void ReadExact(Streamy s, Span<byte> buf, int count)
    {
        var read = 0;
        while (read < count)
            read += s.Read(buf.Slice(read, count - read));
    }
}
