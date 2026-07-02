using System.Collections.Concurrent;

namespace AFCP;

/// <summary>
/// An in-memory bidirectional byte-pair for tests — two <see cref="IConnection"/>s
/// backed by <see cref="BlockingCollection{T}"/> queues, each one's write side
/// feeding the other's read side. No network, no threading surprises: reads block
/// until bytes are available or the pair is closed.
///
/// Use <see cref="CreatePair"/> to get the (A, B) endpoints.
/// </summary>
public sealed class InMemoryConnection : IConnection
{
    private readonly BlockingCollection<byte[]> _incoming;
    private readonly BlockingCollection<byte[]> _outgoing;
    private readonly CancellationTokenSource _cts = new();
    private byte[]? _current;
    private int _currentOffset;
    private int _disposed;

    internal InMemoryConnection(BlockingCollection<byte[]> incoming, BlockingCollection<byte[]> outgoing)
    {
        _incoming = incoming;
        _outgoing = outgoing;
    }

    /// <summary>Create a connected (A, B) pair: bytes written to A are read by B, and vice versa.</summary>
    public static (InMemoryConnection A, InMemoryConnection B) CreatePair(int capacity = int.MaxValue)
    {
        var aToB = new BlockingCollection<byte[]>(capacity);
        var bToA = new BlockingCollection<byte[]>(capacity);
        return (new InMemoryConnection(bToA, aToB), new InMemoryConnection(aToB, bToA));
    }

    public bool IsConnected => !_cts.IsCancellationRequested;

    public event Action? OnDisconnect;

    public int Read(Span<byte> buffer)
    {
        if (_current == null || _currentOffset >= _current.Length)
        {
            try
            {
                if (!_incoming.TryTake(out _current!, Timeout.Infinite, _cts.Token))
                {
                    // Peer completed the queue (close) — this side is effectively disconnected.
                    RaiseDisconnect();
                    return 0;
                }
            }
            catch (OperationCanceledException)
            {
                RaiseDisconnect();
                return 0;
            }
            _currentOffset = 0;
        }

        var available = _current!.Length - _currentOffset;
        var toCopy = Math.Min(available, buffer.Length);
        _current.AsSpan(_currentOffset, toCopy).CopyTo(buffer);
        _currentOffset += toCopy;
        if (_currentOffset >= _current.Length) _current = null;
        return toCopy;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed != 0) throw new ObjectDisposedException(nameof(InMemoryConnection));
        var copy = data.ToArray();
        try { _outgoing.Add(copy, _cts.Token); }
        catch (OperationCanceledException) { RaiseDisconnect(); throw; }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();
        _outgoing.CompleteAdding();
        RaiseDisconnect();
    }

    private void RaiseDisconnect()
    {
        try { OnDisconnect?.Invoke(); } catch { }
    }

    public void Dispose() => Close();
}
