# AFCP Architecture

AFCP is a 4-layer composable protocol stack. Each layer wraps the one below using a decorator pattern.

## Layer model

```
┌─────────────────────────────────────────────────┐
│ Layer 3  RequestChannel                      │
│          req/resp multiplex (kind+id+payload)   │
├─────────────────────────────────────────────────┤
│ Layer 2  IMessageStream                      │
│          [Framing] → [Checksum] → [Crypto]     │
│          [u32 len][payload] per message        │
├─────────────────────────────────────────────────┤
│ Layer 1  Streamy                             │
│          [Camouflage] → [Logger]               │
│          span-level byte stream                │
├─────────────────────────────────────────────────┤
│ Layer 0  IConnection                         │
│          TCP / Serial / InMemory               │
│          raw duplex byte pipe                  │
└─────────────────────────────────────────────────┘
```

## Core concepts

### Decorator chains

Each layer has an abstract decorator base:

- **StreamyTransformer** (Layer 1) — wraps a `Streamy`, propagates
  `Initialize`/`IsConnected`/`OnDisconnect`. Subclass to transform bytes.

- **MessageTransformer** (Layer 2) — wraps an `IMessageStream`, propagates
  `Initialize`/`IsConnected`/`OnDisconnect`/`Dispose`. Subclass to transform messages.

Decorators call through to their wrapped base. The chain is built bottom-up by
`AfcpStackBuilder`.

### Role-aware handshake

Layers that need a handshake (Camouflage, Crypto) use `Initialize(isServer/bool)`.
The server/client role determines who speaks first:

| Layer | Server | Client |
|-------|--------|--------|
| Camouflage | Reads fake HTTP request, sends 200 response | Sends fake GET, reads response |
| Crypto | Reads peer ECDH key, sends own key | Sends own key, reads peer key |

Handshakes propagate bottom-up: `Camouflage.Initialize` calls `Base.Initialize`
first (lower layers init), then runs its own exchange. `Crypto.Initialize` does the
same through `MessageTransformer`.

Framing has no handshake of its own but propagates to the Streamy layer.

### Message framing

Every message on the wire is wrapped:

```
[u32 little-endian length][payload bytes]
```

Framing reads 4 bytes to get the length, then reads exactly that many payload
bytes. A zero-length message is valid. Disconnect is detected when the underlying
stream returns 0 bytes (EOF).

`MaxMessageLength` (default 16 MB) rejects oversized frames to prevent OOM from
malicious peers.

### Checksum + Crypto ordering

The recommended stack order is `Framing → Checksum → Crypto`:

1. Framing wraps the raw message in `[len][payload]`
2. Checksum appends a 4-byte integrity value to the framed payload
3. Crypto encrypts the checksummed data as a unit

This means the checksum protects the plaintext (detects corruption before
decryption), and the ciphertext is what traverses the wire. If the order were
reversed (`Crypto` below `Checksum`), the checksum would be computed over
ciphertext, which provides no benefit — the ciphertext changes unpredictably
with each encryption.

AFCP's additive checksum is fast (vectorized, 8× u32 lanes) but not
cryptographic. For tamper resistance, use `Crypto`. The checksum catches
line noise and accidental corruption.

### RequestChannel multiplexing

On a serial link, there is one byte channel. Multiple request/response pairs
must share it. RequestChannel demuxes them by a unique 32-bit `RequestId`:

```
[1 byte kind][4 byte reqId LE][4 byte payloadLen LE][payload]
```

- `kind = 1` → Request (client → server)
- `kind = 2` → Response (server → client)

Each `SendRequest` assigns a new ID via `Interlocked.Increment`. The response
is matched by ID via a `ConcurrentDictionary<uint, TaskCompletionSource>`.

Requests arriving before a handler is subscribed are buffered and drained on
the first `OnRequest +=` subscription.

### Disconnect handling

`IConnection` surfaces `IsConnected` and `OnDisconnect`. The disconnect is
detected during I/O — a `Read` returning 0 or a `Write` throwing. The event
fires exactly once (guarded by `Interlocked.Exchange` on an internal flag).

`ReconnectingConnection` wraps a factory and auto-reconnects with exponential
backoff on a background thread.

### Extensibility points

Write a custom transform by subclassing either base:

```csharp
// Byte-level transform (Layer 1)
public sealed class XorTransformer : StreamyTransformer
{
    private readonly byte _key;
    public XorTransformer(Streamy baseStream, byte key) : base(baseStream) => _key = key;
    public override int Read(Span<byte> buffer) { /* ... */ }
    public override void Write(ReadOnlySpan<byte> buffer) { /* ... */ }
}

// Message-level transform (Layer 2)
public sealed class ReverseTransformer : MessageTransformer
{
    public ReverseTransformer(IMessageStream baseStream) : base(baseStream) { }
    public override void Write(ReadOnlySpan<byte> message) { /* ... */ }
    public override ReadOnlySpan<byte> Read() { /* ... */ }
}
```

Slot them into the stack by constructing the chain manually (above or below
Framing depending on the layer) and passing the result to the next builder step.
