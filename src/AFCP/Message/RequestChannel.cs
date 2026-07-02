using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace AFCP;

/// <summary>
/// Request/response multiplexing over a single <see cref="IMessageStream"/> —
/// the <c>testeMulti</c> lineage. On a serial link there is one channel, so
/// multiple in-flight request/response pairs must share it; this demuxes them by
/// a 32-bit <see cref="RequestId"/>. (Over full-duplex TCP you don't strictly
/// need it, but it's a clean top semantic either way.)
///
/// Wire format per message (riding the underlying <c>IMessageStream</c>):
/// <c>[u8 kind][u32 reqId][u32 payloadLen][payload]</c>.
/// <c>kind</c> = <c>1</c> Request, <c>2</c> Response.
///
/// The server raises <see cref="OnRequest"/> per incoming request; the handler
/// calls <see cref="RequestContext.Respond"/>. The client awaits the matching
/// response via a <see cref="TaskCompletionSource{TResult}"/> keyed by
/// <see cref="RequestId"/>. A default per-call timeout (TODO §C7f) is enforced
/// when the caller doesn't supply a <see cref="CancellationToken"/>.
/// </summary>
public sealed class RequestChannel : IDisposable
{
    private const byte KindRequest = 1;
    private const byte KindResponse = 2;

    private readonly IMessageStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _reader;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<byte[]>> _pending = new();
    private readonly ConcurrentQueue<RequestContext> _buffered = new();
    private readonly object _handlerLock = new();
    private Action<RequestContext>? _onRequest;
    private uint _nextId;
    private int _disposed;

    /// <summary>Fires on each incoming request. The handler MUST call <see cref="RequestContext.Respond"/>.
    /// Requests that arrive before a handler is subscribed are buffered and drained
    /// on the first subscribe — so it is safe to call <see cref="Start"/> before
    /// registering.</summary>
    public event Action<RequestContext> OnRequest
    {
        add
        {
            lock (_handlerLock)
            {
                _onRequest += value;
                while (_buffered.TryDequeue(out var ctx))
                    value(ctx);
            }
        }
        remove
        {
            lock (_handlerLock) { _onRequest -= value; }
        }
    }

    public RequestChannel(IMessageStream stream)
    {
        _stream = stream;
        _reader = new Thread(ReaderLoop) { IsBackground = true, Name = "AFCP.RequestChannel.Reader" };
    }

    /// <summary>Start the reader loop. Call after <see cref="IMessageStream.Initialize"/>.</summary>
    public RequestChannel Start()
    {
        _reader.Start();
        return this;
    }

    public bool IsConnected => _stream.IsConnected;
    public event Action? OnDisconnect { add => _stream.OnDisconnect += value; remove => _stream.OnDisconnect -= value; }

    /// <summary>Send a request and await the response. Throws on timeout or disconnect.</summary>
    public byte[] SendRequest(ReadOnlySpan<byte> payload, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        WriteFrame(KindRequest, id, payload);

        // Default timeout (§C7f) — 30s unless the caller cancels sooner.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var reg = linked.Token.Register(() => tcs.TrySetCanceled());
            return tcs.Task.GetAwaiter().GetResult();
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private void ReaderLoop()
    {
        var header = new byte[9]; // kind(1) + reqId(4) + payloadLen(4)
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var frame = _stream.Read();
                if (frame.Length == 0) break; // disconnect
                if (frame.Length < 9) continue;
                var kind = frame[0];
                var reqId = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(1, 4));
                var payload = frame[9..];
                if (kind == KindRequest)
                {
                    var ctx = new RequestContext(reqId, this);
                    ctx.PayloadBytes = payload.ToArray();
                    lock (_handlerLock)
                    {
                        if (_onRequest != null) _onRequest(ctx);
                        else _buffered.Enqueue(ctx);
                    }
                }
                else if (kind == KindResponse)
                {
                    if (_pending.TryRemove(reqId, out var tcs))
                        tcs.TrySetResult(payload.ToArray());
                }
            }
            catch
            {
                break; // stream error → treat as disconnect
            }
        }
        // Fail all pending requests.
        foreach (var kv in _pending)
            kv.Value.TrySetException(new IOException("RequestChannel: connection closed."));
        _pending.Clear();
    }

    internal void SendResponse(uint reqId, ReadOnlySpan<byte> payload)
        => WriteFrame(KindResponse, reqId, payload);

    private void WriteFrame(byte kind, uint reqId, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(1, 4), reqId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(5, 4), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(9));
        _stream.Write(frame);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        try { _reader.Join(2000); } catch { }
        _stream.Dispose();
        _cts.Dispose();
    }
}

/// <summary>One inbound request on the server side.</summary>
public sealed class RequestContext
{
    private readonly uint _reqId;
    private readonly RequestChannel _channel;
    internal byte[] PayloadBytes = Array.Empty<byte>();

    internal RequestContext(uint reqId, RequestChannel channel) { _reqId = reqId; _channel = channel; }

    public ReadOnlySpan<byte> Payload => PayloadBytes;

    /// <summary>Reply to this request. Call exactly once.</summary>
    public void Respond(ReadOnlySpan<byte> response) => _channel.SendResponse(_reqId, response);
}
