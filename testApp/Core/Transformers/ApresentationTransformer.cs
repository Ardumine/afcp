using System;

namespace AFCP.Core.Transformers
{
    public abstract class ApresentationTransformer : Streamy
    {
        protected readonly Streamy _baseStream;

        public ApresentationTransformer(Streamy baseStream)
        {
            _baseStream = baseStream;
        }

        public override Streamy Initialize(StreamyParameters parameters)
        {
            _baseStream.Initialize(parameters);
            return this;
        }

        public override ReadOnlySpan<byte> ReadAll()
        {
            //The lower layer will handle it
            return _baseStream.ReadAll();
        }
    }
}
