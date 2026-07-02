namespace AFCP;

/// <summary>
/// Wraps a transport factory and auto-reconnects on disconnect with exponential
/// backoff. <see cref="OnDisconnect"/> fires when the underlying
/// <see cref="IConnection"/> drops; a reconnect is attempted on a background
/// thread, and <see cref="OnReconnect"/> fires on success. Upper layers decide
/// whether to retry in-flight work.
///
/// The wrapped connection is replaced atomically under a lock. Reads/writes
/// see the new connection once the swap completes.
/// </summary>
public sealed class ReconnectingConnection : IConnection
{
    private readonly Func<IConnection> _factory;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly int _maxAttempts;
    private IConnection _current;
    private readonly object _lock = new();
    private int _disposed;
    private int _reconnecting;

    public ReconnectingConnection(Func<IConnection> factory, TimeSpan? initialBackoff = null,
        TimeSpan? maxBackoff = null, int maxAttempts = 0)
    {
        _factory = factory;
        _initialBackoff = initialBackoff ?? TimeSpan.FromMilliseconds(200);
        _maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(10);
        _maxAttempts = maxAttempts;
        _current = factory();
        _current.OnDisconnect += OnCurrentDisconnect;
    }

    public bool IsConnected
    {
        get { lock (_lock) { return _current.IsConnected; } }
    }

    public event Action? OnDisconnect;
    public event Action? OnReconnect;

    private void OnCurrentDisconnect()
    {
        OnDisconnect?.Invoke();
        if (Interlocked.Exchange(ref _reconnecting, 1) == 1) return;
        new Thread(TryReconnect) { IsBackground = true, Name = "AFCP.ReconnectingConnection" }.Start();
    }

    private void TryReconnect()
    {
        if (_disposed != 0) { _reconnecting = 0; return; }
        var delay = _initialBackoff;
        var minBackoff = TimeSpan.FromMilliseconds(1);
        if (delay < minBackoff) delay = minBackoff;

        for (var attempt = 1; _maxAttempts == 0 || attempt <= _maxAttempts; attempt++)
        {
            Thread.Sleep(delay);
            if (_disposed != 0) break;
            try
            {
                var next = _factory();
                IConnection? old;
                lock (_lock)
                {
                    old = _current;
                    _current = next;
                    _current.OnDisconnect += OnCurrentDisconnect;
                }
                if (old != null)
                {
                    old.OnDisconnect -= OnCurrentDisconnect;
                    try { old.Dispose(); } catch { }
                }
                _reconnecting = 0;
                OnReconnect?.Invoke();
                return;
            }
            catch
            {
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxBackoff.Ticks));
            }
        }
        _reconnecting = 0;
    }

    public int Read(Span<byte> buffer)
    {
        lock (_lock) { return _current.Read(buffer); }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        lock (_lock) { _current.Write(data); }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        lock (_lock)
        {
            _current.OnDisconnect -= OnCurrentDisconnect;
            try { _current.Dispose(); } catch { }
        }
    }

    public void Dispose() => Close();
}
