# Layer Reference

## Layer 0 — Transport (`IConnection`)

`IConnection` is the raw duplex byte pipe. No message boundaries, no handshake.

### Implementations

| Class | Description |
|-------|-------------|
| `TcpConnection` | TCP client. Constructor accepts `TcpClient` or `IPEndPoint`. Sets `NoDelay = true`. |
| `TcpServer` | TCP listener. Binds an `IPEndPoint`. `Accept()` blocks; returns `TcpConnection`. |
| `SerialConnection` | Serial port (8N1). Constructor opens the port immediately. |
| `InMemoryConnection` | In-process pair backed by `BlockingCollection<byte[]>`. `CreatePair()` returns (A, B). |
| `ReconnectingConnection` | Wraps a factory. Auto-reconnects on disconnect with exponential backoff. |
| `TransportRegistry` | Static registry. Opens by URI: `tcp://`, `serial://`, `inmem://`. |

### Interface

```csharp
public interface IConnection : IDisposable
{
    int Read(Span<byte> buffer);
    void Write(ReadOnlySpan<byte> data);
    bool IsConnected { get; }
    event Action? OnDisconnect;
    void Close();
}
```

- `Read` returns 0 on EOF/disconnect.
- `Write` throws if the connection is dropped.
- `OnDisconnect` fires exactly once.
- `Close()` is idempotent.

---

## Layer 1 — Streamy

`Streamy` is a span-level byte stream — the decorator base above `IConnection`.

### Base classes

| Class | Description |
|-------|-------------|
| `Streamy` (abstract) | `Read(Span<byte>)`, `Write(ReadOnlySpan<byte>)`, `Initialize(StreamyParameters)`, `IsConnected`, `OnDisconnect` |
| `StreamyTransformer` (abstract) | Decorator base. Holds `Base` stream, propagates init/events. |

### Adaptors

| Class | Description |
|-------|-------------|
| `StreamyFromConnection` | Wraps an `IConnection` as a `Streamy`. |
| `StreamyFromStream` | Wraps any `System.IO.Stream` as a `Streamy`. `OnDisconnect` is a no-op. |
| `StreamFromStreamy` | The reverse: wraps a `Streamy` as a `System.IO.Stream`. No seek support. |

### Decorators

| Class | Description |
|-------|-------------|
| `Camouflage` | HTTP-header disguise. Client sends fake GET, server replies with fake 200 chunked response. Byte-level — must stack below `Framing`. |
| `Logger` | Debug decorator. Logs every read/write as hex to stdout. |

### Writing a custom transformer

```csharp
public sealed class MyTransformer : StreamyTransformer
{
    public MyTransformer(Streamy baseStream) : base(baseStream) { }
    public override int Read(Span<byte> buffer) { /* transform */ return Base.Read(buffer); }
    public override void Write(ReadOnlySpan<byte> buffer) { /* transform */ Base.Write(buffer); }
}
```

---

## Layer 2 — IMessageStream

`IMessageStream` is message-oriented. Each `Write` sends one discrete message;
each `Read` returns exactly one message.

### Interface

```csharp
public interface IMessageStream : IDisposable
{
    void Write(ReadOnlySpan<byte> message);
    ReadOnlySpan<byte> Read();
    bool IsConnected { get; }
    event Action? OnDisconnect;
    IMessageStream Initialize(bool isServer);
}
```

### Framing

`Framing` is the bridge from bytes to messages. Wire format: `[u32 LE length][payload]`.

**Security:** `MaxMessageLength` (default 16 MB) prevents memory exhaustion from
malicious length headers.

### MessageTransformer

Abstract decorator base. Holds `Base` stream, propagates init/events/dispose.

### Decorators

| Class | Description |
|-------|-------------|
| `Checksum` | Appends 4-byte additive checksum. Verifies on read. Fast, not cryptographic. |
| `Crypto` | ECDH (nistP256) key exchange + AES-CFB (feedback size 8). HKDF-SHA256 key derivation. Encrypts each message as a unit. |

### Writing a custom transformer

```csharp
public sealed class MyMessageTransformer : MessageTransformer
{
    public MyMessageTransformer(IMessageStream baseStream) : base(baseStream) { }
    public override void Write(ReadOnlySpan<byte> message) { /* transform */ Base.Write(message); }
    public override ReadOnlySpan<byte> Read() { var m = Base.Read(); /* transform */ return m; }
}
```

---

## Layer 3 — RequestChannel

Request/response multiplexing over a single `IMessageStream`.

### Wire format

```
[1 byte  kind]    1 = Request, 2 = Response
[4 bytes reqId]   Little-endian uint32
[4 bytes len]     Little-endian uint32 payload length
[N bytes payload]
```

### Usage

**Server:**
```csharp
var (_, ch) = new AfcpStackBuilder(conn).WithChecksum().WithCrypto()
    .BuildWithRequestChannel(isServer: true);
ch.OnRequest += ctx => ctx.Respond(responsePayload);
```

**Client:**
```csharp
var (_, ch) = new AfcpStackBuilder(conn).WithChecksum().WithCrypto()
    .BuildWithRequestChannel(isServer: false);
var resp = ch.SendRequest(payload);
```

### Features

- **Multiplexing:** Concurrent requests demuxed by `RequestId`.
- **Timeout:** Default 30s per `SendRequest`, overridable via `CancellationToken`.
- **Buffering:** Requests arriving before handler is subscribed are queued and drained on subscribe.
- **Thread safety:** `SendRequest` and `SendResponse` are serialized via an internal write lock.

---

## AfcpStackBuilder

Fluent builder. Wires the canonical order:

```
IConnection → StreamyFromConnection → [Camouflage] → [Logger]
  → Framing → [Checksum] → [Crypto] → [RequestChannel]
```

```csharp
var cli = new AfcpStackBuilder(conn)
    .WithCamouflage()
    .WithLogger("debug")
    .WithChecksum()
    .WithCrypto()
    .Build(isServer: false);
```

`BuildWithRequestChannel` returns both the `IMessageStream` and `RequestChannel`:

```csharp
var (stream, channel) = new AfcpStackBuilder(conn)
    .WithChecksum().WithCrypto()
    .BuildWithRequestChannel(isServer: false);
```
