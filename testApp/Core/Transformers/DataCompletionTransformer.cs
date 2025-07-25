using System;
using System.Text;
using AFCP.Core.Utils;

namespace AFCP.Core.Transformers
{
    public class DataCompletionTransformer : ApresentationTransformer
    {
        public DataCompletionTransformer(Streamy baseStream) : base(baseStream)
        {
        }

        public override ApresentationTransformer Initialize(StreamyParameters parameters)
        {
            //Initialize the parent stream
            _baseStream.Initialize(parameters);
            return this;
        }


        private uint ReadDataCount()
        {
            //UInt 4 byte
            Span<byte> bufferLen = stackalloc byte[4];
            _baseStream.Read(bufferLen);
            return Tools.GetUInt(bufferLen);
        }


        public override ReadOnlySpan<byte> ReadAll()
        {
            if (_totalBytes == _bytesAlreadyRead)
            {
                _totalBytes = (int)ReadDataCount();
                _bytesAlreadyRead = 0;
            }

            Span<byte> actualData = new byte[_totalBytes];


            var bytesUnread = _totalBytes - _bytesAlreadyRead;
            while (bytesUnread > 0)
            {
                _bytesAlreadyRead += _baseStream.Read(actualData.Slice((int)_bytesAlreadyRead, (int)bytesUnread));
                bytesUnread = _totalBytes - _bytesAlreadyRead;
            }

            if (bytesUnread < 0)
                throw new InvalidOperationException("Read operation exceeded expected length.");
            return actualData;
        }

        
        private int _bytesAlreadyRead = 0;
        private int _totalBytes = 0;

        public override int Read(Span<byte> buffer)
        {
            //To handle layers that only support reading a fixed amount of bytes
            if (_totalBytes == _bytesAlreadyRead)
            {
                _totalBytes = (int)ReadDataCount();
                _bytesAlreadyRead = 0;
            }

            var bytesCurrentlyRead = _baseStream.Read(buffer);
            _bytesAlreadyRead += bytesCurrentlyRead;
            return bytesCurrentlyRead;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _baseStream.Write(Tools.GetBytes((uint)buffer.Length));
            _baseStream.Write(buffer);
        }
    }
}
