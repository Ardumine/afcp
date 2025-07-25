using System;
using System.IO;

namespace AFCP.Core.Implementations
{
    public class StreamFromStreamy : Stream
    {
        Streamy _originalStream;
        public StreamFromStreamy(Streamy originalStream)
        {
            _originalStream = originalStream;
        }

        public override bool CanRead => true;

        public override bool CanSeek => throw new NotImplementedException();

        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
            
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _originalStream.Read(new Span<byte>(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _originalStream.Write(new Span<byte>(buffer, offset, count));

        }
    }
}