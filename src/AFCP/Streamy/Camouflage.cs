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
public sealed class Camouflage : StreamyTransformer
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

    public Camouflage(Streamy baseStream) : base(baseStream) { }

    public override Streamy Initialize(StreamyParameters parameters)
    {
        var buf = new byte[Math.Max(_clientRequest.Length, _serverResponse.Length)];
        if (parameters.IsServer)
        {
            // Server: read the client's request, send the response.
            ReadExact(Base, buf, _clientRequest.Length);
            Base.Write(_serverResponse);
        }
        else
        {
            // Client: send request, read response.
            Base.Write(_clientRequest);
            ReadExact(Base, buf, _serverResponse.Length);
        }
        Base.Initialize(parameters);
        return this;
    }

    // Read/Write pass through unchanged — camouflage only injects the HTTP handshake.

    private static void ReadExact(Streamy s, Span<byte> buf, int count)
    {
        var read = 0;
        while (read < count)
            read += s.Read(buf.Slice(read, count - read));
    }
}
