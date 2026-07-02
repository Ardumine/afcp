using System.Buffers.Binary;

namespace AFCP;

/// <summary>
/// Length-prefix framing: turns a raw <see cref="Streamy"/> byte stream into an
/// <see cref="IMessageStream"/>. Wire format per message:
/// <c>[u32 little-endian length][payload]</c>. Reads block until the whole
/// payload arrives. A zero-length message is legal (the length is still framed).
///
/// This is the bridge from Layer 1 (bytes) to Layer 2 (messages); all higher
/// message layers (<see cref="Checksum"/>, <see cref="Crypto"/>,
/// <see cref="RequestChannel"/>) compose on top of an <c>IMessageStream</c>.
/// </summary>
public sealed class Framing : IMessageStream
{
    public const int DefaultMaxMessageLength = 16 * 1024 * 1024; // 16 MB

    private readonly Streamy _base;
    private readonly byte[] _lenBuf = new byte[4];
    private byte[]? _readBuf;

    public Framing(Streamy baseStream) => _base = baseStream;

    /// <summary>Maximum allowed message length in bytes. Frames exceeding this limit are rejected.</summary>
    public int MaxMessageLength { get; set; } = DefaultMaxMessageLength;

    public bool IsConnected => _base.IsConnected;
    public event Action? OnDisconnect { add => _base.OnDisconnect += value; remove => _base.OnDisconnect -= value; }

    public IMessageStream Initialize(bool isServer)
    {
        _base.Initialize(new StreamyParameters { IsServer = isServer });
        return this;
    }

    public void Write(ReadOnlySpan<byte> message)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_lenBuf, (uint)message.Length);
        _base.Write(_lenBuf);
        if (message.Length > 0)
            _base.Write(message);
    }

    public ReadOnlySpan<byte> Read()
    {
        if (!ReadExact(_lenBuf))
        {
            _readBuf = null;
            return ReadOnlySpan<byte>.Empty;
        }
        var len32 = BinaryPrimitives.ReadUInt32LittleEndian(_lenBuf);
        if (len32 > (uint)MaxMessageLength)
            throw new InvalidDataException($"Framing: message length {len32} exceeds maximum {MaxMessageLength} bytes.");
        var len = (int)len32;
        if (len == 0)
        {
            _readBuf = Array.Empty<byte>();
            return _readBuf;
        }
        _readBuf = new byte[len];
        if (!ReadExact(_readBuf))
        {
            _readBuf = null;
            return ReadOnlySpan<byte>.Empty;
        }
        return _readBuf;
    }

    private bool ReadExact(Span<byte> dst)
    {
        var total = 0;
        while (total < dst.Length)
        {
            var n = _base.Read(dst.Slice(total));
            if (n <= 0) return false;
            total += n;
        }
        return true;
    }

    public void Dispose() { /* ownership of _base is the caller's */ }
}
