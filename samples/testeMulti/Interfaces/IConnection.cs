namespace testeMulti.Interfaces;

public interface IConnection : IDisposable
{
    /// <summary>
    /// Writes data to the connection.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public int Write(ReadOnlySpan<byte> data);

    /// <summary>
    /// Reads data from the connection.
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public int Read(Span<byte> buffer);

    public void Close();
}