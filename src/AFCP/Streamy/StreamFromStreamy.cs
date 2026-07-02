namespace AFCP;

/// <summary>
/// The reverse adapter: wraps a <see cref="Streamy"/> so it can be used where a
/// <see cref="System.IO.Stream"/> is expected (e.g. passing the encrypted,
/// checksummed stack to a library that reads/writes a <c>Stream</c>). Read/Write
/// delegate to the underlying <see cref="Streamy"/>; seek/length are unsupported
/// (the stack is a live pipe, not seekable storage).
/// </summary>
public sealed class StreamFromStreamy : System.IO.Stream
{
    private readonly Streamy _streamy;

    public StreamFromStreamy(Streamy streamy) => _streamy = streamy;

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
        => _streamy.Read(new Span<byte>(buffer, offset, count));

    public override void Write(byte[] buffer, int offset, int count)
        => _streamy.Write(new ReadOnlySpan<byte>(buffer, offset, count));

    public override void Flush() { }
    public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
