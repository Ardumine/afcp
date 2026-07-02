namespace AFCP;

/// <summary>
/// Fluent builder for an AFCP byte-stream stack. Composes the layers in the
/// canonical order and runs the role-aware handshake bottom-up:
///
/// <code>
/// IConnection → StreamyFromConnection → [Camouflage] → Framing → [Checksum] → [Crypto]
///   → [RequestChannel]
/// </code>
///
/// Each optional layer is added with its <c>With*</c> method; <see cref="Build"/>
/// returns the top <see cref="IMessageStream"/> after calling
/// <see cref="IMessageStream.Initialize"/>. The caller owns the
/// <see cref="IConnection"/>; disposing the built stream disposes the chain
/// (except the connection, which the caller manages — or pass
/// <c>ownsConnection: true</c>).
///
/// Example (client + server over in-memory):
/// <code>
/// var (a, b) = InMemoryConnection.CreatePair();
/// var server = new AfcpStackBuilder(b).WithFraming().WithChecksum().WithCrypto().Build(isServer: true);
/// var client = new AfcpStackBuilder(a).WithFraming().WithChecksum().WithCrypto().Build(isServer: false);
/// </code>
/// </summary>
public sealed class AfcpStackBuilder
{
    private readonly IConnection _conn;
    private bool _camouflage;
    private bool _checksum;
    private bool _crypto;
    private string? _loggerName;

    public AfcpStackBuilder(IConnection connection) => _conn = connection;

    /// <summary>Disguise the link as HTTP during handshake (optional, byte-level).</summary>
    public AfcpStackBuilder WithCamouflage() { _camouflage = true; return this; }

    /// <summary>Add per-message integrity (checksum).</summary>
    public AfcpStackBuilder WithChecksum() { _checksum = true; return this; }

    /// <summary>Add per-message confidentiality (ECDH + AES-CFB).</summary>
    public AfcpStackBuilder WithCrypto() { _crypto = true; return this; }

    /// <summary>Insert a <see cref="Logger"/> debug decorator at the byte-stream layer with the given tag.</summary>
    public AfcpStackBuilder WithLogger(string name) { _loggerName = name; return this; }

    /// <summary>Compose the stack and run the handshake. Returns the top layer.</summary>
    public IMessageStream Build(bool isServer)
    {
        Streamy streamy = new StreamyFromConnection(_conn);
        if (_camouflage)
            streamy = new Camouflage(streamy);
        if (_loggerName is not null)
            streamy = new Logger(streamy, _loggerName);

        IMessageStream msg = new Framing(streamy);
        if (_checksum)
            msg = new Checksum(msg);
        if (_crypto)
            msg = new Crypto(msg);

        msg.Initialize(isServer);
        return msg;
    }

    /// <summary>Compose, handshake, and start a <see cref="RequestChannel"/>. See <see cref="WithRequestChannel"/>.</summary>
    public (IMessageStream stream, RequestChannel channel) BuildWithRequestChannel(bool isServer)
    {
        var stream = Build(isServer);
        var channel = new RequestChannel(stream).Start();
        return (stream, channel);
    }
}
