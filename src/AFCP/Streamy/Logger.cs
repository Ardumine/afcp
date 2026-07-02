using System.Text;

namespace AFCP;

/// <summary>
/// A debug decorator that logs every read/write as UTF-8 text. For development
/// only — do not use in production (it prints raw bytes, including ciphertext).
/// </summary>
public sealed class Logger : Streamy
{
    private readonly Streamy _base;
    private readonly string _name;

    public Logger(Streamy baseStream, string name) { _base = baseStream; _name = name; }

    public override int Read(Span<byte> buffer)
    {
        var n = _base.Read(buffer);
        if (n > 0)
            Console.WriteLine($"[{_name}] r({n}): {BitConverter.ToString(buffer[..n].ToArray())}");
        return n;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Console.WriteLine($"[{_name}] w({buffer.Length}): {BitConverter.ToString(buffer.ToArray())}");
        _base.Write(buffer);
    }

    public override bool IsConnected => _base.IsConnected;
    public override event Action? OnDisconnect { add => _base.OnDisconnect += value; remove => _base.OnDisconnect -= value; }
}
