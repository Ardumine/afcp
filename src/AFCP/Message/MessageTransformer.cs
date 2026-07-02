namespace AFCP;

/// <summary>
/// Abstract base for an <see cref="IMessageStream"/> decorator. Holds the wrapped
/// <see cref="Base"/> stream and propagates <see cref="Initialize"/>,
/// <see cref="IsConnected"/>, <see cref="OnDisconnect"/>, and <see cref="Dispose"/>.
/// A subclass overrides <see cref="Read"/>/<see cref="Write"/> to transform
/// messages (e.g. <see cref="Checksum"/> appends/verifies integrity,
/// <see cref="Crypto"/> encrypts/decrypts).
///
/// Symmetric to <see cref="StreamyTransformer"/> at the byte-stream layer: this is
/// the message-layer extensibility point.
/// </summary>
public abstract class MessageTransformer : IMessageStream
{
    protected readonly IMessageStream Base;

    protected MessageTransformer(IMessageStream baseStream) => Base = baseStream;

    public virtual IMessageStream Initialize(bool isServer) => Base.Initialize(isServer);
    public virtual void Write(ReadOnlySpan<byte> message) => Base.Write(message);
    public virtual ReadOnlySpan<byte> Read() => Base.Read();

    public virtual bool IsConnected => Base.IsConnected;
    public virtual event Action? OnDisconnect { add => Base.OnDisconnect += value; remove => Base.OnDisconnect -= value; }

    public virtual void Dispose() => Base.Dispose();
}
