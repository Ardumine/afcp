using System;
using System.IO;

namespace AFCP.Core.Implementations
{
    public class StreamyFromStream : Streamy
    {
        private readonly Stream _stream;
        private readonly bool _logging;
        public StreamyFromStream(Stream stream, bool enableLogging = false)
        {
            _stream = stream;
            _logging = enableLogging;
        }



        public override int Read(Span<byte> buffer)
        {
            if(_logging) Console.WriteLine($"StreamyFromStream Read called with buffer length: {buffer.Length}");
            return _stream.Read(buffer);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
           if(_logging) Console.WriteLine($"StreamyFromStream Write called with buffer length: {buffer.Length}");
            _stream.Write(buffer);
        }
             
    }
}
