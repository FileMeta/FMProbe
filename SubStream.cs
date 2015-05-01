using System;
using System.IO;

namespace FMProbe
{
    /// <summary>
    /// A read-only stream that is a subset of another stream
    /// </summary>
    class SubStream : Stream
    {
        Stream mSuper;
        long mSubStart;
        long mSubLen;
        long mPosition;

        public SubStream(Stream stream, long pos, long len)
        {
            mSuper = stream;
            mSubStart = pos;
            mSubLen = len;
            if (mSubStart > mSuper.Length)
            {
                mSubStart = 0;
                mSubLen = 0;
            }
            else if (mSubLen > mSuper.Length - mSubStart)
            {
                mSubLen = mSuper.Length - mSubStart;
            }
            mPosition = 0;
        }

        public override bool CanRead
        {
            get { return mSuper.CanRead; }
        }

        public override bool CanSeek
        {
            get { return mSuper.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            // do nothing
        }

        public override long Length
        {
            get { return mSubLen; }
        }

        public override long Position
        {
            get
            {
                return mPosition;
            }
            set
            {
                mPosition = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (mPosition >= mSuper.Length) return 0;
            if ((long)count > mSubLen - mPosition) count = (int)(mSubLen - mPosition);
            mSuper.Position = mSubStart + mPosition;
            int result = mSuper.Read(buffer, offset, count);
            if (result > 0) mPosition += result;
            return result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    mPosition = offset;
                    break;
                case SeekOrigin.Current:
                    mPosition += offset;
                    break;
                case SeekOrigin.End:
                    mPosition = mSubLen + offset;
                    break;
            }
            return mPosition;
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }
    }
}
