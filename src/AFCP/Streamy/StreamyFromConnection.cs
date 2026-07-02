namespace AFCP;

/// <summary>
/// Adapts an <see cref="IConnection"/> into a <see cref="Streamy"/> — the base of
/// the byte-stream decorator chain. Read/Write/IsConnected/OnDisconnect pass
/// straight through.
/// </summary>
public sealed class StreamyFromConnection : Streamy
{
    private readonly IConnection _conn;

    public StreamyFromConnection(IConnection connection) => _conn = connection;

    public override int Read(Span<byte> buffer) => _conn.Read(buffer);
    public override void Write(ReadOnlySpan<byte> buffer) => _conn.Write(buffer);

    public override bool IsConnected => _conn.IsConnected;
    public override event Action? OnDisconnect
    {
        add => _conn.OnDisconnect += value;
        remove => _conn.OnDisconnect -= value;
    }
}
