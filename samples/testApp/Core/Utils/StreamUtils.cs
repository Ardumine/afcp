using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace AFCP.Core.Utils;

//Used an LLM to generate this code. Just for testing and to simulate a TCP stream.
public static class StreamUtils
{
    public static (Stream, Stream) CreateBidirectionalStreams(int capacity = int.MaxValue)
    {
        var queueAtoB = new BlockingCollection<byte[]>(capacity);
        var queueBtoA = new BlockingCollection<byte[]>(capacity);

        var streamA = new BidirectionalStream(queueBtoA, queueAtoB);
        var streamB = new BidirectionalStream(queueAtoB, queueBtoA);

        return (streamA, streamB);
    }

    private sealed class BidirectionalStream : Stream
    {
        private readonly BlockingCollection<byte[]> _incomingQueue;
        private readonly BlockingCollection<byte[]> _outgoingQueue;
        private readonly CancellationTokenSource _disposeTokenSource = new();
        private byte[]? _currentBuffer;
        private int _currentIndex;
        private bool _disposed;

        public BidirectionalStream(
            BlockingCollection<byte[]> incomingQueue,
            BlockingCollection<byte[]> outgoingQueue)
        {
            _incomingQueue = incomingQueue;
            _outgoingQueue = outgoingQueue;
        }

        public override bool CanRead => !_disposed;
        public override bool CanWrite => !_disposed;
        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            if (count == 0) return 0;

            // Get new buffer if needed
            if (_currentBuffer == null || _currentIndex >= _currentBuffer.Length)
            {
                try
                {
                    if (!_incomingQueue.TryTake(out _currentBuffer, Timeout.Infinite, _disposeTokenSource.Token))
                        return 0; // End of stream
                }
                catch (OperationCanceledException)
                {
                    ThrowDisposed();
                    return 0; // Unreachable
                }
                _currentIndex = 0;
            }

            // Copy data from current buffer
            int bytesAvailable = _currentBuffer.Length - _currentIndex;
            int bytesToCopy = Math.Min(bytesAvailable, count);
            Array.Copy(_currentBuffer, _currentIndex, buffer, offset, bytesToCopy);
            _currentIndex += bytesToCopy;

            // Clear buffer if fully consumed
            if (_currentIndex >= _currentBuffer.Length)
                _currentBuffer = null;

            return bytesToCopy;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            if (count == 0) return;

            // Copy data to avoid external modification
            byte[] copy = new byte[count];
            Array.Copy(buffer, offset, copy, 0, count);

            try
            {
                _outgoingQueue.Add(copy, _disposeTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                ThrowDisposed();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            _disposed = true;
            _disposeTokenSource.Cancel();
            _disposeTokenSource.Dispose();
            _outgoingQueue.CompleteAdding();
            base.Dispose(disposing);
        }

        private void CheckDisposed()
        {
            if (_disposed) ThrowDisposed();
        }

        private void ThrowDisposed() =>
            throw new ObjectDisposedException(GetType().Name, "Stream has been disposed");

        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
