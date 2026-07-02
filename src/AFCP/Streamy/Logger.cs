using System.Text;

namespace AFCP;

/// <summary>
/// A debug decorator that logs every read/write as UTF-8 text. For development
/// only — do not use in production (it prints raw bytes, including ciphertext).
/// </summary>
public sealed class Logger : StreamyTransformer
{
    private readonly string _name;

    public Logger(Streamy baseStream, string name) : base(baseStream) { _name = name; }

    public override int Read(Span<byte> buffer)
    {
        var n = Base.Read(buffer);
        if (n > 0)
            Console.WriteLine($"[{_name}] r({n}): {BitConverter.ToString(buffer[..n].ToArray())}");
        return n;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Console.WriteLine($"[{_name}] w({buffer.Length}): {BitConverter.ToString(buffer.ToArray())}");
        Base.Write(buffer);
    }
}
