using System;

namespace AFCP.Core
{
    public abstract class Streamy
    {
        public abstract void Write(ReadOnlySpan<byte> buffer);
        public abstract int Read(Span<byte> buffer);

        public virtual ReadOnlySpan<byte> ReadAll()
        {
           throw new NotImplementedException("ReadAll method is not implemented in the base Streamy class. Please override it in derived classes.");
        }

        //Initialize before data is sent. Optional.
        public virtual Streamy Initialize(StreamyParameters parameters)
        {
            return this;
        }
    }
}
