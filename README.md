# AFCP

A composable, HCore-free byte-stream protocol stack. Stackable layers — transport,
framing, integrity, confidentiality, camouflage — ridden by any duplex byte stream
(TCP over WiFi/Ethernet, serial, in-memory). The remote foundation for HCore
inter-instance communication, but usable standalone for any RPC/streaming app.

> **Relationship to [KASerializer](https://github.com/Ardumine/kaserializer):** this
> library is a **pure byte-stream stack** — it does not serialize typed messages and
> does not reference KASerializer. Serialization of HCore-shaped verbs
> (`Sync`/`Read`/`Write`/`Subscribe`/`Call`) is the HCore connector module's job
> (Phase 2 of the HCore AFCP organization). KASerializer is "shared by both" in
> availability, not in that AFCP itself uses it.

## Layered design

```
Layer 0  Transport      IConnection (duplex byte stream)
                          TcpConnection     — TCP (WiFi or Ethernet)
                          SerialConnection  — serial port (the single-channel case)
                          InMemoryConnection — in-process test pair
                          ReconnectingConnection — auto-reconnect w/ backoff
Layer 1  Streamy        span-level byte-stream decorators
                          StreamyFromConnection (base)
                          Camouflage — HTTP-header disguise (optional)
                          Logger     — debug (optional)
Layer 2  IMessageStream message-oriented (length-prefix boundaries)
                          Framing    — [u32 len][payload] over a Streamy
                          Checksum   — per-message additive integrity (decorator)
                          Crypto     — ECDH key exchange + AES-CFB (decorator)
Layer 3  RequestChannel request/response multiplex over a single channel
                          (the testeMulti lineage — for serial: one channel,
                           many in-flight req/resp pairs demuxed by RequestId)
```

Each layer composes on the one below. `Framing` is the bridge from bytes to
messages; `Checksum`/`Crypto`/`RequestChannel` stack on an `IMessageStream`. The
`AfcpStackBuilder` wires the canonical order and runs the role-aware handshake
(camouflage, ECDH) bottom-up via `IMessageStream.Initialize(isServer)`.

## Usage

```csharp
using AFCP;

// Client + server over an in-memory pair, full stack.
var (a, b) = InMemoryConnection.CreatePair();
var srv = new AfcpStackBuilder(b).WithChecksum().WithCrypto().Build(isServer: true);
var cli = new AfcpStackBuilder(a).WithChecksum().WithCrypto().Build(isServer: false);

cli.Write("hello"u8);
var msg = srv.Read();        // "hello"
```

```csharp
// Request/response (single channel, multiplexed).
var (sa, sb) = InMemoryConnection.CreatePair();
var (_, srvChan) = new AfcpStackBuilder(sb).WithChecksum().WithCrypto()
    .WithRequestChannel().BuildWithRequestChannel(isServer: true);
srvChan.OnRequest += ctx => ctx.Respond(ctx.Payload);   // echo

var (_, cliChan) = new AfcpStackBuilder(sa).WithChecksum().WithCrypto()
    .WithRequestChannel().BuildWithRequestChannel(isServer: false);
var resp = cliChan.SendRequest("ping"u8);   // "ping"
```

```csharp
// Multi-transport via target spec.
var conn = TransportRegistry.Open("tcp://192.168.1.10:8000");
var conn2 = TransportRegistry.Open("serial:///dev/ttyUSB0?baud=115200");
```

## Hardening (over the original `testApp`/`testeMulti`)

The upstream experiments were "very very simple". This lib adds:

- **Disconnect handling.** `IConnection.IsConnected` + `OnDisconnect` event; reads
  return 0 on EOF, writes throw on a dropped link. `TcpConnection` surfaces socket
  errors as a disconnect.
- **Auto-reconnect.** `ReconnectingConnection` wraps a transport factory, retries
  with exponential backoff (bounded), fires `OnDisconnect`/`OnReconnect`. Upper
  layers decide whether to retry in-flight work.
- **Multi-transport selection.** `TransportRegistry` parses a target spec
  (`tcp://`, `serial://`, `inmem://`); custom schemes via `Register`. WiFi and
  Ethernet are both TCP — the same `TcpConnection`.
- **Call timeout.** `RequestChannel.SendRequest` enforces a default 30s timeout
  (TODO §C7f moved down to the protocol layer) unless the caller cancels sooner.
- **Single-channel multiplex.** `RequestChannel` demuxes concurrent
  request/response pairs by `RequestId` — what `testeMulti`'s `RequestBasedStream`
  was reaching for, but one-shot. This is what makes AFCP work over a serial link
  (one channel, many in-flight calls).

## What this is NOT

- **No typed messages.** AFCP carries bytes and messages (length-prefixed). Typed
  serialization is the caller's concern (KASerializer, JSON, MemoryPack — your
  choice).
- **No HCore dependency.** Nothing here knows about `/proc`, facets, or modules.
- **No capability model.** Any peer that connects can exchange messages; access
  control is the caller's concern (the HCore connector's §C3 gap).

## Layout

```
src/AFCP/
  Transport/   IConnection, TcpConnection, SerialConnection, InMemoryConnection,
               ReconnectingConnection, TransportRegistry
  Streamy/     Streamy (abstract), StreamyFromConnection, Camouflage, Logger
  Message/     IMessageStream, Framing, Checksum, Crypto, RequestChannel
  AfcpStackBuilder.cs
test/AFCP.Tests/   9 end-to-end tests (in-memory + TCP loopback)
samples/      the original testApp (Streamy) and testeMulti (request/stream),
              preserved as usage reference
```

## Build & test

```bash
dotnet build
dotnet run --project test/AFCP.Tests
```

Expects `passed: 9 / failed: 0 / ALL OK`.
