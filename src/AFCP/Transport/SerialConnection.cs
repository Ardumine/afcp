using System.IO.Ports;

namespace AFCP;

/// <summary>
/// Serial-port transport. This is the case <c>testeMulti</c> was built for: a
/// single channel where request/response multiplexing (<see cref="RequestChannel"/>)
/// is needed because there is no full-duplex multi-stream facility.
///
/// Opens the port with 8N1 + the given baud; reads block on the underlying
/// <see cref="SerialPort.BaseStream"/>. <see cref="IsConnected"/> tracks open state;
/// <see cref="OnDisconnect"/> fires on read/write error or close.
/// </summary>
public sealed class SerialConnection : IConnection
{
    private readonly SerialPort _port;
    private int _disposed;
    private int _disconnected;

    public SerialConnection(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate)
        {
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            Handshake = Handshake.None,
            ReadTimeout = Timeout.Infinite,
            WriteTimeout = Timeout.Infinite,
        };
        _port.Open();
    }

    public bool IsConnected => _port.IsOpen && Volatile.Read(ref _disposed) == 0;

    public event Action? OnDisconnect;

    public int Read(Span<byte> buffer)
    {
        try
        {
            return _port.BaseStream.Read(buffer);
        }
        catch
        {
            RaiseDisconnect();
            return 0;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        try { _port.BaseStream.Write(data); }
        catch { RaiseDisconnect(); throw; }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { _port.Dispose(); } catch { }
        RaiseDisconnect();
    }

    private void RaiseDisconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) == 1) return;
        try { OnDisconnect?.Invoke(); } catch { }
    }

    public void Dispose() => Close();
}
