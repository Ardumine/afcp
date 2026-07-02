namespace AFCP;

/// <summary>
/// Abstract base for a <see cref="Streamy"/> decorator (a "transformer"). Holds
/// the wrapped <see cref="Base"/> stream and propagates
/// <see cref="Initialize(StreamyParameters)"/>, <see cref="IsConnected"/>, and
/// <see cref="OnDisconnect"/> to it. A subclass overrides <see cref="Read"/>/
/// <see cref="Write"/> to transform bytes (or leaves them as pass-through if it
/// only injects a handshake, like <see cref="Camouflage"/>).
///
/// This is the extensibility point: write <c>class MyTransformer : StreamyTransformer</c>
/// and slot it into the stack via <see cref="AfcpStackBuilder"/>.
/// </summary>
public abstract class StreamyTransformer : Streamy
{
    /// <summary>The wrapped lower layer.</summary>
    protected readonly Streamy Base;

    protected StreamyTransformer(Streamy baseStream) => Base = baseStream;

    public override Streamy Initialize(StreamyParameters parameters)
    {
        Base.Initialize(parameters);
        return this;
    }

    public override int Read(Span<byte> buffer) => Base.Read(buffer);
    public override void Write(ReadOnlySpan<byte> buffer) => Base.Write(buffer);
    public override bool IsConnected => Base.IsConnected;
    public override event Action? OnDisconnect { add => Base.OnDisconnect += value; remove => Base.OnDisconnect -= value; }
}
