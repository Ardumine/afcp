using System.Buffers.Binary;

namespace AFCP;

/// <summary>
/// Per-message integrity decorator over an <see cref="IMessageStream"/>. Appends
/// a 4-byte additive checksum to each outgoing message; verifies on read and
/// throws if it doesn't match. One framed message round-trips per call (the
/// checksum rides inside the framed payload, not as a separate frame).
///
/// The checksum is the vectorized additive sum from the original
/// <c>testeMulti/Streams/CheckSumBasedStream.cs</c> (bytes summed in
/// big-endian u32 lanes with a tail handler) — fast, not cryptographic. For
/// tamper resistance use <see cref="Crypto"/>; this catches line noise.
/// </summary>
public sealed class Checksum : IMessageStream
{
    private readonly IMessageStream _base;

    public Checksum(IMessageStream baseStream) => _base = baseStream;

    public bool IsConnected => _base.IsConnected;
    public event Action? OnDisconnect { add => _base.OnDisconnect += value; remove => _base.OnDisconnect -= value; }

    public IMessageStream Initialize(bool isServer) => _base.Initialize(isServer);

    public void Write(ReadOnlySpan<byte> message)
    {
        var sum = Compute(message);
        var combined = new byte[message.Length + 4];
        message.CopyTo(combined.AsSpan());
        BinaryPrimitives.WriteUInt32LittleEndian(combined.AsSpan(message.Length), sum);
        _base.Write(combined);
    }

    public ReadOnlySpan<byte> Read()
    {
        var raw = _base.Read();
        if (raw.Length == 0) return raw;
        if (raw.Length < 4)
            throw new InvalidDataException($"Checksum: frame too short ({raw.Length} bytes).");

        var body = raw[..^4];
        var received = BinaryPrimitives.ReadUInt32LittleEndian(raw[^4..]);
        var expected = Compute(body);
        if (received != expected)
            throw new InvalidDataException($"Checksum mismatch: received 0x{received:X8}, expected 0x{expected:X8}.");
        return body;
    }

    private static unsafe uint Compute(ReadOnlySpan<byte> arr)
    {
        if (arr.Length == 0) return 0;
        fixed (byte* ptr = arr)
        {
            uint sum = 0;
            int z = 0;
            var limit = arr.Length - 32;
            while (z <= limit)
            {
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z));
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z + 4));
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z + 8));
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z + 12));
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z + 16));
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z + 20));
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z + 24));
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z + 28));
                z += 32;
            }
            limit = arr.Length - 4;
            while (z <= limit)
            {
                sum += BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + z));
                z += 4;
            }
            switch ((arr.Length - z) & 3)
            {
                case 3: sum += (uint)ptr[z + 2] << 8; sum += (uint)ptr[z + 1] << 16; sum += (uint)ptr[z] << 24; break;
                case 2: sum += (uint)ptr[z + 1] << 16; sum += (uint)ptr[z] << 24; break;
                case 1: sum += (uint)ptr[z] << 24; break;
            }
            return sum;
        }
    }

    public void Dispose() => _base.Dispose();
}
