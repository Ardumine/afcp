using System;
using System.Text;

namespace AFCP.Core.Transformers
{
    public class Logger : ApresentationTransformer
    {
        private readonly string _name;

        public Logger(Streamy baseStream, string name) : base(baseStream)
        {
            _name = name;
        }

        public override int Read(Span<byte> buffer)
        {
            int count = _baseStream.Read(buffer);
            Console.WriteLine($"[{_name}]ʀ{Encoding.UTF8.GetString(buffer.Slice(0, count))}");
            return count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Console.WriteLine($"[{_name}]ᴡ{Encoding.UTF8.GetString(buffer)}");
            _baseStream.Write(buffer);
        }

        public override ReadOnlySpan<byte> ReadAll()
        {
            var data = _baseStream.ReadAll();
            Console.WriteLine($"[{_name}]ᴀ{Encoding.UTF8.GetString(data)}");
            return data;
        }
    }
}
