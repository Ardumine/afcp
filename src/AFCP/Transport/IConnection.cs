namespace AFCP;

/// <summary>
/// A duplex byte stream — the transport layer (Layer 0). Implementations:
/// <see cref="TcpConnection"/> (TCP over any network interface: WiFi, Ethernet),
/// <see cref="SerialConnection"/> (serial port), <see cref="InMemoryConnection"/>
/// (in-process test pair), and <see cref="ReconnectingConnection"/> (auto-reconnect
/// wrapper).
///
/// A connection is a raw byte pipe. Message boundaries, integrity, and encryption
/// are layered above it (<see cref="IMessageStream"/>). The connection reports its
/// own health via <see cref="IsConnected"/>/<see cref="OnDisconnect"/> so upper
/// layers can tear down or retry rather than crash on a dropped link.
/// </summary>
public interface IConnection : IDisposable
{
    /// <summary>Read up to <paramref name="buffer"/>.Length bytes. Returns 0 on EOF/disconnect.</summary>
    int Read(Span<byte> buffer);

    /// <summary>Write all bytes. Throws if the connection is dropped mid-write.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>True while the underlying link is alive.</summary>
    bool IsConnected { get; }

    /// <summary>Fires (once) when the link drops — socket error, remote close, or disposal.</summary>
    event Action? OnDisconnect;

    /// <summary>Close the link. Idempotent.</summary>
    void Close();
}
