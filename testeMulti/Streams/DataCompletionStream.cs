using AFCP.Core.Utils;
using testeMulti.Interfaces;

namespace testeMulti.Streams;

public class DataCompletionStream(IConnection connection) : ICountableStream
{
    private uint ReadDataCount()
    {
        Span<byte> bufferLen = stackalloc byte[4];
        connection.Read(bufferLen);
        return Tools.GetUInt(bufferLen);
    }

    public ReadOnlySpan<byte> Read()
    {
        var totalBytes = (int)ReadDataCount();

        return ReadExactly(totalBytes);
    }

    public void WriteRaw(ReadOnlySpan<byte> data)
    {
        connection.Write(data);
    }

    public ReadOnlySpan<byte> ReadExactly(int count)
    {
        Span<byte> data = new byte[count];

        int bytesAlreadyRead = 0;
        var bytesUnread = count;
        while (bytesUnread > 0)
        {
            bytesAlreadyRead += connection.Read(data.Slice(bytesAlreadyRead, bytesUnread));
            bytesUnread = count - bytesAlreadyRead;
        }

        return data;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        connection.Write(Tools.GetBytes((uint)data.Length));
        connection.Write(data);
    }
}