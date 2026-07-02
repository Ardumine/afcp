namespace AFCP;

/// <summary>
/// Initialization parameters handed to <see cref="Streamy.Initialize"/> — the
/// handshake phase (camouflage, key exchange) is role-aware: one side speaks
/// first. <see cref="IsServer"/> picks the role.
/// </summary>
public sealed class StreamyParameters
{
    public bool IsServer { get; set; }
}

/// <summary>
/// A span-level duplex byte stream (Layer 1). The base of the decorator chain:
/// <see cref="StreamyFromConnection"/> wraps an <see cref="IConnection"/>, then
/// byte-level transforms (<see cref="Camouflage"/>, <see cref="Logger"/>) stack
/// on top. <see cref="Initialize"/> runs the role-aware handshake bottom-up
/// (each decorator initializes its base first).
///
/// <see cref="Streamy"/> carries no message-boundary semantics — that is
/// <see cref="IMessageStream"/>'s job, layered above via <see cref="Framing"/>.
/// </summary>
public abstract class Streamy
{
    public abstract int Read(Span<byte> buffer);
    public abstract void Write(ReadOnlySpan<byte> buffer);

    /// <summary>Role-aware handshake (camouflage / key exchange). Default: no-op.</summary>
    public virtual Streamy Initialize(StreamyParameters parameters) => this;

    public abstract bool IsConnected { get; }
    public abstract event Action? OnDisconnect;
}
