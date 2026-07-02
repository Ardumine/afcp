namespace AFCP;

/// <summary>
/// A message-oriented stream (Layer 2): each <see cref="Write"/> is one discrete
/// message, each <see cref="Read"/> returns exactly one message. Layered above a
/// <see cref="Streamy"/> byte stream via <see cref="Framing"/> (length-prefix
/// boundaries), then optionally <see cref="Checksum"/> (integrity) and
/// <see cref="Crypto"/> (confidentiality) stack on top.
///
/// <see cref="Initialize(bool)"/> runs the role-aware handshake (e.g. the ECDH
/// key exchange in <see cref="Crypto"/>) bottom-up before any payload messages
/// are exchanged.
/// </summary>
public interface IMessageStream : IDisposable
{
    /// <summary>Send one message.</summary>
    void Write(ReadOnlySpan<byte> message);

    /// <summary>Receive one message. Returns an empty span on disconnect.</summary>
    ReadOnlySpan<byte> Read();

    bool IsConnected { get; }
    event Action? OnDisconnect;

    /// <summary>Role-aware handshake (crypto key exchange). Returns this.</summary>
    IMessageStream Initialize(bool isServer);
}
