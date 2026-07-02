using System.Buffers.Binary;
using AFCP.Core.Utils;

namespace testeMulti.Streams;

public class CheckSumBasedStream(ICountableStream baseStream) : ICountableStream
{
    public void Write(ReadOnlySpan<byte> data)
    {
        uint checksum = Checksum(data);

        baseStream.Write(data);
        baseStream.WriteRaw(Tools.GetBytes(checksum));
    }

    public ReadOnlySpan<byte> Read()
    {
        var data = baseStream.Read();
        var receivedChecksum = Tools.GetUInt(baseStream.ReadExactly(4));
        var dataChecksum = Checksum(data);

        if (receivedChecksum != dataChecksum)
        {
            throw new Exception($"Checksums did not match! Received: {receivedChecksum}, Expected: {dataChecksum}");
        }

        return data;
    }

    public void WriteRaw(ReadOnlySpan<byte> data)
    {
        throw new NotSupportedException();
    }

    public ReadOnlySpan<byte> ReadExactly(int count)
    {
        throw new NotSupportedException();
    }

    //https://github.com/israellot/checksum-challenge/blob/main/src/ChecksumChallenge/Checksum/Expert.cs
    private static unsafe uint Checksum(ReadOnlySpan<byte> arr)
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

            int rem = (arr.Length - z);

            switch (rem & 3)
            {
                case 3:
                    sum += (uint)(*(ptr + z + 2)) << 8;
                    sum += (uint)(*(ptr + z + 1)) << 16;
                    sum += (uint)(*(ptr + z)) << 24;
                    break;
                case 2:
                    sum += (uint)(*(ptr + z + 1)) << 16;
                    sum += (uint)(*(ptr + z)) << 24;
                    break;
                case 1:
                    sum += (uint)(*(ptr + z)) << 24;
                    break;
            }

            return sum;
        }
    }
}