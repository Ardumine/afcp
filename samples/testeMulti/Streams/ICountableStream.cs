namespace testeMulti.Streams;

public interface ICountableStream
{
    public void Write(ReadOnlySpan<byte> data);

    public ReadOnlySpan<byte> Read();

    public void WriteRaw(ReadOnlySpan<byte> data);
    public ReadOnlySpan<byte> ReadExactly(int count);
}