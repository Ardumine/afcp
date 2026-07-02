namespace AFCP;

/// <summary>
/// Wraps a transport factory and auto-reconnects on disconnect with exponential
/// backoff. This is the "handle disconnects" layer: when the underlying
/// <see cref="IConnection"/> drops, <see cref="OnDisconnect"/> fires, a reconnect
/// is attempted (after the backoff delay), and <see cref="OnReconnect"/> fires on
/// success. In-flight reads/writes during the gap throw; upper layers decide
/// whether to retry (e.g. <see cref="RequestChannel"/> can resend pending
/// requests after <see cref="OnReconnect"/>).
///
/// The wrapped connection is replaced atomically on reconnect. Reads/writes
/// block during the reconnect window and resume once a new link is up.
/// </summary>
public sealed class ReconnectingConnection : IConnection
{
    private readonly Func<IConnection> _factory;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly int _maxAttempts; // 0 = infinite
    private IConnection _current;
    private readonly object _lock = new();
    private int _disposed;

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
        TryReconnect();
    }

    private void TryReconnect()
    {
        if (_disposed != 0) return;
        var delay = _initialBackoff;
        for (var attempt = 1; _maxAttempts == 0 || attempt <= _maxAttempts; attempt++)
        {
            Thread.Sleep(delay);
            if (_disposed != 0) return;
            try
            {
                var next = _factory();
                lock (_lock)
                {
                    _current = next;
                    _current.OnDisconnect += OnCurrentDisconnect;
                }
                OnReconnect?.Invoke();
                return;
            }
            catch
            {
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxBackoff.Ticks));
            }
        }
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
        lock (_lock) { _current.Close(); }
    }

    public void Dispose() => Close();
}
