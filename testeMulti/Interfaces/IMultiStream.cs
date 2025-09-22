using testeMulti.Interfaces;

namespace testeMulti.Interfaces;

public interface IMultiStreamServer : IDisposable
{
    /// <summary>
    /// Handles the creation of new streams
    /// </summary>
    /// <returns></returns>
    public void Start();

    public IConnection HandleConnections(CancellationToken ct = default);
    public void Stop();
}

public interface IMultiStreamClient : IDisposable
{
    /// <summary>
    /// Opens a new stream on the connection.
    /// </summary>
    /// <returns></returns>
    public IConnection CreateStream();
}