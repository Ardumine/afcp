namespace AFCP;

/// <summary>
/// Adapts any <see cref="System.IO.Stream"/> into a <see cref="Streamy"/> — the
/// base of the byte-stream decorator chain when you already have a Stream
/// (<c>SslStream</c>, <c>NamedPipeServerStream</c>, <c>MemoryStream</c>,
/// <c>NetworkStream</c>, …) rather than an <see cref="IConnection"/>.
///
/// Use <see cref="StreamyFromConnection"/> when you have an <c>IConnection</c>
/// (it surfaces <c>IsConnected</c>/<c>OnDisconnect</c>); use this when all you
/// have is a raw <c>Stream</c>. <see cref="IsConnected"/> tracks
/// <see cref="System.IO.Stream.CanRead"/>; there is no mid-stream disconnect
/// event unless the stream itself throws on read/write.
/// </summary>
public sealed class StreamyFromStream : Streamy
{
    private readonly System.IO.Stream _stream;

    public StreamyFromStream(System.IO.Stream stream) => _stream = stream;

    public override int Read(Span<byte> buffer) => _stream.Read(buffer);
    public override void Write(ReadOnlySpan<byte> buffer) => _stream.Write(buffer);

    public override bool IsConnected => _stream.CanRead;
    public override event Action? OnDisconnect { add { } remove { } }
}
