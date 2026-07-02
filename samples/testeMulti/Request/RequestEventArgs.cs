using testeMulti.Streams;

namespace testeMulti.Request;

public class RequestEventArgs(ICountableStream stream) : EventArgs
{
    public ReadOnlySpan<byte> ReadRequest()
    {
        return stream.Read();
    }

    public void Answer(ReadOnlySpan<byte> data)
    {
        stream.Write(data);
    }
}