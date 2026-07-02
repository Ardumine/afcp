# AFCP

A composable, library-agnostic byte-stream protocol stack. Stackable layers —
transport, framing, integrity, confidentiality, camouflage — carried by any
duplex byte stream (TCP over WiFi/Ethernet, serial, in-memory).

Built as the remote foundation for HCore inter-instance communication, but
usable standalone for any RPC/streaming application.

> **Relationship to [KASerializer](https://github.com/Ardumine/kaserializer):**
> AFCP is a pure byte-stream stack. It does not serialize typed messages and
> does not reference KASerializer. Serialization of HCore-shaped verbs is the
> HCore connector module's job. KASerializer is a sibling dependency used
> elsewhere in the ecosystem, not within AFCP itself.

## Layered design

```
Layer 0  Transport      IConnection (duplex byte stream)
                          TcpConnection     — TCP (WiFi or Ethernet)
                          TcpServer         — TCP listener (Accept → IConnection)
                          SerialConnection  — serial port (the single-channel case)
                          InMemoryConnection — in-process test pair
                          ReconnectingConnection — auto-reconnect w/ backoff
                          TransportRegistry — target-spec factory (tcp://, serial://, inmem://)
Layer 1  Streamy        span-level byte-stream decorators
                          StreamyFromConnection (base, from IConnection)
                          StreamyFromStream    (base, from any System.IO.Stream)
                          StreamFromStreamy    (reverse: Streamy → Stream for interop)
                          StreamyTransformer   (abstract decorator base — write your own)
                          Camouflage — HTTP-header disguise (optional)
                          Logger     — debug (optional)
Layer 2  IMessageStream message-oriented (length-prefix boundaries)
                          Framing    — [u32 le length][payload] over a Streamy
                          MessageTransformer — abstract decorator base (write your own)
                          Checksum   — per-message additive integrity (decorator)
                          Crypto     — ECDH key exchange + AES-CFB (decorator)
Layer 3  RequestChannel request/response multiplex over a single channel
                          (for serial: one channel, many in-flight req/resp
                           pairs demuxed by RequestId)
```

Each layer composes on the one below. `Framing` is the bridge from bytes to
messages; `Checksum`/`Crypto`/`RequestChannel` stack on an `IMessageStream`.
`AfcpStackBuilder` wires the canonical order and runs the role-aware handshake
(camouflage, ECDH) bottom-up.

**Extensibility:** the two abstract decorator bases — `StreamyTransformer` (byte
layer) and `MessageTransformer` (message layer) — hold the wrapped lower stream
and propagate `Initialize`/`IsConnected`/`OnDisconnect`/`Dispose`. Subclass
either to add a custom transform (e.g. a compressor, a custom cipher) and slot
it into the stack. The test suite includes both an `XorTransformer` and a
`ReverseTransformer` proving the pattern.

## Usage

```csharp
using AFCP;

// Echo server + client over an in-memory pair, full stack.
// The server must run on a background thread — Build() with crypto
// blocks until the ECDH handshake completes with the client.
var (a, b) = InMemoryConnection.CreatePair();
IMessageStream? srv = null;
var srvThread = new Thread(() =>
{
    srv = new AfcpStackBuilder(b).WithChecksum().WithCrypto().Build(isServer: true);
    var msg = srv.Read();
    srv.Write(msg); // echo
}) { IsBackground = true };
srvThread.Start();

var cli = new AfcpStackBuilder(a).WithChecksum().WithCrypto().Build(isServer: false);
cli.Write("hello"u8);
var echo = cli.Read(); // "hello"

srvThread.Join();
cli.Dispose(); srv?.Dispose();
```

```csharp
// Request/response (single channel, multiplexed).
var (sa, sb) = InMemoryConnection.CreatePair();

var srvReady = new ManualResetEventSlim();
RequestChannel? srvChan = null;
var srvThread = new Thread(() =>
{
    var (_, ch) = new AfcpStackBuilder(sb).WithChecksum().WithCrypto()
        .BuildWithRequestChannel(isServer: true);
    srvChan = ch;
    ch.OnRequest += ctx => ctx.Respond(ctx.Payload);
    srvReady.Set();
}) { IsBackground = true };
srvThread.Start();

var (_, cliChan) = new AfcpStackBuilder(sa).WithChecksum().WithCrypto()
    .BuildWithRequestChannel(isServer: false);
srvReady.Wait();
var resp = cliChan.SendRequest("ping"u8); // "ping"
cliChan.Dispose(); srvChan?.Dispose();
```

```csharp
// Multi-transport via target spec.
var conn = TransportRegistry.Open("tcp://192.168.1.10:8000");
var conn2 = TransportRegistry.Open("serial:///dev/ttyUSB0?baud=115200");
```

## What this is NOT

- **No typed messages.** AFCP carries bytes and messages (length-prefixed). Typed
  serialization is the caller's concern.
- **No HCore dependency.** Nothing here knows about `/proc`, facets, or modules.
- **No capability model.** Any peer that connects can exchange messages; access
  control is the caller's responsibility.

## Layout

```
src/AFCP/
  Transport/   IConnection, TcpConnection, TcpServer, SerialConnection,
               InMemoryConnection, ReconnectingConnection, TransportRegistry
  Streamy/     Streamy (abstract), StreamyTransformer (abstract decorator),
               StreamyFromConnection, StreamyFromStream, StreamFromStreamy,
               Camouflage, Logger
  Message/     IMessageStream, MessageTransformer (abstract decorator),
               Framing, Checksum, Crypto, RequestChannel
  AfcpStackBuilder.cs

test/AFCP.Tests/       51 xUnit tests

samples/
  EchoServer/           TCP echo, full stack (checksum + crypto)
  RequestResponse/      RequestChannel over TCP
```

## Build & test

```bash
dotnet test
```

## Further reading

- [Architecture overview](docs/architecture.md) — layered design, handshake flow, extensibility
- [Layer reference](docs/layers.md) — API details and usage for every layer
