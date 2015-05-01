using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace FileMeta
{

    /// <summary>
    /// Determines the type of a file from it's header.
    /// </summary>
    /// <remarks>
    /// In keeping with the FileMeta Manifesto (see http://www.filemeta.org/manifesto) file types
    /// should be determined by their contents, not by their container or by their reference.
    /// </remarks>
    public enum FileTypeId
    {
        unknown = 0,
        mp3 = 1,
        mpeg4 = 2, // MPEG-4 Part 14. Also known as ISOM or ISO/IEC 14496-12:2004
        jpeg = 3, // JPEG (either JFIF or EXIF)
        midi = 4
    };

    /// <summary>
    /// Determines the type of a file from its contents and metadata.
    /// </summary>
    /// <remarks>
    /// <para>Over time this class will be extended to support many different file types. Pesently supports: mp3 but only when id3v2 tags are present.
    /// </para>
    /// <para>File type identification and metadata requires that files meet certain requirements:
    /// </para>
    /// </remarks>
    public static class FileType
    {
        const int cMaxHeaderBytes = 64;
        static readonly byte[] sMp3_Header = {(byte)'I', (byte)'D', (byte)'3'};
        static readonly byte[] sJfifHeader00 = { 0xff, 0xd8, 0xff, 0xe0 };
        static readonly byte[] sJfifHeader06 = { (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0 };
        static readonly byte[] sExifHeader00 = { 0xff, 0xd8, 0xff, 0xe1 };
        static readonly byte[] sExifHeader06 = { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };
        static readonly byte[] sTiffHeader_I = { (byte)'I', (byte)'I', 42, 0 };
        static readonly byte[] sTiffHeader_M = { (byte)'M', (byte)'M', 0, 42 };
        static readonly byte[] sMidiHeader = { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0};
        static readonly byte[] sIsoBaseMediaHeader = { (byte)'f', (byte)'t', (byte)'y', (byte)'p' };

        // Supported MP4 brands
        static readonly byte[][] sMp4Brands =
        {
            new byte[] {(byte)'i', (byte)'s', (byte)'o', (byte)'m'},
            new byte[] {(byte)'i', (byte)'s', (byte)'o', (byte)'2'},
            new byte[] {(byte)'a', (byte)'v', (byte)'c', (byte)'1'},
            new byte[] {(byte)'m', (byte)'p', (byte)'4', (byte)'1'},
        };

        public static FileTypeId GetFileType(byte[] header, int offset, int len)
        {
            if (ByteBuf.Match(header, offset, len, sMp3_Header) // ID3v2 prefix
                && (header[3] >= 3 && header[3] <= 4) // Version 3 or 4
                && (header[4] == 00)) // No minor versions for ID3 have been used.
                return FileTypeId.mp3;

            if (ByteBuf.Match(header, offset, len, sExifHeader00)
                && ByteBuf.Match(header, offset + 6, len - 6, sExifHeader06)
                && (ByteBuf.Match(header, offset + 12, len - 12, sTiffHeader_I) || ByteBuf.Match(header, offset + 12, len - 12, sTiffHeader_M)))
                return FileTypeId.jpeg;

            if (ByteBuf.Match(header, offset, len, sJfifHeader00)
                && ByteBuf.Match(header, offset + 6, len - 6, sJfifHeader06))
                return FileTypeId.jpeg;

            if (ByteBuf.Match(header, offset, len, sMidiHeader)
                && len >= 12 && header[9] <= 2)
                return FileTypeId.midi;

            // If ISO Base Media format
            if (ByteBuf.Match(header, offset+4, len-4, sIsoBaseMediaHeader))
            {
                // See if primary brand matches any of the MP4 brands
                foreach(byte[] brandPattern in sMp4Brands)
                {
                    if (ByteBuf.Match(header, offset + 8, len - 8, brandPattern)) return FileTypeId.mpeg4;
                }

                // Try any "compatible brands"
                Int32 ftypLen = ByteBuf.Int32FromBytesBE(header, 0);
                if (ftypLen > len) ftypLen = len;
                for (int brandOfst = 16; brandOfst + 4 < ftypLen; brandOfst += 4)
                {
                    // See if compatible brand matches any of the MP4 brands
                    foreach (byte[] brandPattern in sMp4Brands)
                    {
                        if (ByteBuf.Match(header, brandOfst, len - brandOfst, brandPattern)) return FileTypeId.mpeg4;
                    }
                }
            }

#if false
            using (StreamReader reader = new StreamReader(new MemoryStream(header, 0, len), Encoding.UTF8, true, cMaxHeaderBytes, false))
            {
                return GetFileTypeFromStringHeader(reader.ReadToEnd());
            }
#endif
            return FileTypeId.unknown;
        }

        public static FileTypeId GetFileType(Stream stream)
        {
            long pos = stream.Position;
            byte[] buf = new byte[cMaxHeaderBytes];
            int bufLen = stream.Read(buf, 0, cMaxHeaderBytes);
            stream.Position = pos;
            return GetFileType(buf, 0, bufLen);
        }

        public static FileTypeId GetFileType(string filename)
        {
            using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, cMaxHeaderBytes))
            {
                return GetFileType(stream);
            }
        }
    }

    static class ByteBuf
    {
        private static UTF8Encoding sUTF8 = new UTF8Encoding(false, false);

        /// <summary>
        /// Compare two byte sequences
        /// </summary>
        /// <param name="a">Byte buffer a</param>
        /// <param name="aOffset">Offset within buffer a at which to begin comparison</param>
        /// <param name="aCount">Length of the sequence in buffer a to compare</param>
        /// <param name="b">Byte buffer b</param>
        /// <param name="bOffset">Offset withing buffer a at which to begin comparison</param>
        /// <param name="bCount">Lenth of the sequence in buffer b to compare</param>
        /// <returns>Returns -1 if a &lt; b, 0 if strings are equal and 1 if a &gt; b.</returns>
        public static int Compare(byte[] a, int aOffset, int aCount, byte[] b, int bOffset, int bCount)
        {
            // Out of range will be detected by built-in array protections
            int aEnd = aOffset + aCount;
            int bEnd = bOffset + bCount;
            while (aOffset < aEnd && bOffset < bEnd)
            {
                if (a[aOffset] < b[bOffset]) return -1;
                if (a[aOffset] > b[bOffset]) return 1;
                ++aOffset;
                ++bOffset;
            }
            if (aOffset < aEnd) return -1;
            if (bOffset < bEnd) return 1;
            return 0;
        }

        /// <summary>
        /// Compare a byte sequene with a string
        /// </summary>
        /// <param name="a">Byte buffer a</param>
        /// <param name="aOffset">Offset within buffer a at which to begin comparison</param>
        /// <param name="aCount">Length of the sequence in buffer a to compare</param>
        /// <param name="b">The string against which to compare</param>
        /// <returns>Returns -1 if a &lt; b, 0 if strings are equal and 1 if a &gt; b.</returns>
        public static int Compare(byte[] a, int aOffset, int aCount, string b)
        {
            byte[] bb = sUTF8.GetBytes(b);
            return Compare(a, aOffset, aCount, bb, 0, bb.Length);
        }

        /// <summary>
        /// Test whether a sequence within a buffer matches particular pattern
        /// </summary>
        /// <param name="buf">Buffer to test</param>
        /// <param name="offset">Offset within the buffer at which to do the comparison</param>
        /// <param name="len">Length of the sequence in the buffer</param>
        /// <param name="pattern">Pattern against which to perform the match.</param>
        /// <returns></returns>
        public static bool Match(byte[] buf, int offset, int len, byte[] pattern)
        {
            if (len > buf.Length - offset) len = buf.Length - offset;
            if (len < pattern.Length) return false;
            return 0 == Compare(buf, offset, pattern.Length, pattern, 0, pattern.Length);
        }

        /// <summary>
        /// Test whether a sequence within a buffer matches particular pattern
        /// </summary>
        /// <param name="buf">Buffer to test</param>
        /// <param name="offset">Offset within the buffer at which to do the comparison</param>
        /// <param name="len">Length of the sequence in the buffer</param>
        /// <param name="pattern">Pattern against which to perform the match.</param>
        /// <returns></returns>
        public static bool Match(byte[] buf, int offset, int len, string pattern)
        {
            byte[] pb = sUTF8.GetBytes(pattern);
            return Match(buf, offset, len, pb);
        }

        /// <summary>
        /// Reads a 32-bit integer from a series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static UInt32 UInt32FromBytes(byte[] buf, int index)
        {
            if (index + 4 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return
                (UInt32)buf[index]
                | ((UInt32)buf[index + 1] << 8)
                | ((UInt32)buf[index + 2] << 16)
                | ((UInt32)buf[index + 3] << 24);
        }

        /// <summary>
        /// Reads a 32-bit unsigned integer from a big-endian series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static UInt32 UInt32FromBytesBE(byte[] buf, int index)
        {
            if (index + 4 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return
                (UInt32)buf[index+3]
                | ((UInt32)buf[index + 2] << 8)
                | ((UInt32)buf[index + 1] << 16)
                | ((UInt32)buf[index] << 24);
        }

        /// <summary>
        /// Reads a 32-bit integrer from a series of bytes with a big-endian flag
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <param name="bigEndian">True if the bytes are big-endian</param>
        /// <returns>The integer read.</returns>
        public static UInt32 UInt32FromBytes(byte[] buf, int index, bool bigEndian)
        {
            return bigEndian ? UInt32FromBytesBE(buf, index) : UInt32FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 32-bit integer from a series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static Int32 Int32FromBytes(byte[] buf, int index)
        {
            return (Int32)UInt32FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 32-bit integer from a big-endian series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static Int32 Int32FromBytesBE(byte[] buf, int index)
        {
            return (Int32)UInt32FromBytesBE(buf, index);
        }

        /// <summary>
        /// Reads a 32-bit integer from a series of bytes with a big-endian flag
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read. Integers are NOT sign-extended so fewer then 4 bytes will never result in a negative number.</returns>
        public static Int32 Int32FromBytes(byte[] buf, int index, bool bigEndian)
        {
            return bigEndian ? (Int32)UInt32FromBytesBE(buf, index) : (Int32)UInt32FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 16-bit integer from a series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static UInt16 UInt16FromBytes(byte[] buf, int index)
        {
            if (index + 2 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return (UInt16)((UInt32)buf[index] | ((UInt32)buf[index + 1] << 8));
        }

        /// <summary>
        /// Reads a 16-bit unsigned integer from a big-endian series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static UInt16 UInt16FromBytesBE(byte[] buf, int index)
        {
            if (index + 2 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return (UInt16)((UInt32)buf[index+1] | ((UInt32)buf[index] << 8));
        }

        /// <summary>
        /// Reads a 16-bit unsigned integrer from a series of bytes with a big-endian flag
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <param name="bigEndian">True if the bytes are big-endian</param>
        /// <returns>The integer read.</returns>
        public static UInt16 UInt16FromBytes(byte[] buf, int index, bool bigEndian)
        {
            return bigEndian ? UInt16FromBytesBE(buf, index) : UInt16FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 16-bit integer from a series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static Int16 Int16FromBytes(byte[] buf, int index)
        {
            return (Int16)UInt16FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 16-bit integer from a big-endian series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static Int16 Int16FromBytesBE(byte[] buf, int index)
        {
            return (Int16)UInt16FromBytesBE(buf, index);
        }

        /// <summary>
        /// Reads a 16-bit integer from a series of bytes with a big-endian flag
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read. Integers are NOT sign-extended so fewer then 4 bytes will never result in a negative number.</returns>
        public static Int16 Int16FromBytes(byte[] buf, int index, bool bigEndian)
        {
            return bigEndian ? (Int16)UInt16FromBytesBE(buf, index) : (Int16)UInt16FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 64-bit integer from a series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static UInt64 UInt64FromBytes(byte[] buf, int index)
        {
            if (index + 8 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return
                (UInt64)buf[index]
                | ((UInt64)buf[index + 1] << 8)
                | ((UInt64)buf[index + 2] << 16)
                | ((UInt64)buf[index + 3] << 24)
                | ((UInt64)buf[index + 4] << 32)
                | ((UInt64)buf[index + 5] << 40)
                | ((UInt64)buf[index + 6] << 48)
                | ((UInt64)buf[index + 7] << 56);
        }

        /// <summary>
        /// Reads a 64-bit unsigned integer from a big-endian series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static UInt64 UInt64FromBytesBE(byte[] buf, int index)
        {
            if (index + 8 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return
                (UInt64)buf[index + 7]
                | ((UInt64)buf[index + 6] << 8)
                | ((UInt64)buf[index + 5] << 16)
                | ((UInt64)buf[index + 4] << 24)
                | ((UInt64)buf[index + 3] << 32)
                | ((UInt64)buf[index + 2] << 40)
                | ((UInt64)buf[index + 1] << 48)
                | ((UInt64)buf[index] << 56);
        }

        /// <summary>
        /// Reads a 64-bit integrer from a series of bytes with a big-endian flag
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <param name="bigEndian">True if the bytes are big-endian</param>
        /// <returns>The integer read.</returns>
        public static UInt64 UInt64FromBytes(byte[] buf, int index, bool bigEndian)
        {
            return bigEndian ? UInt64FromBytesBE(buf, index) : UInt64FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 64-bit integer from a series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static Int64 Int64FromBytes(byte[] buf, int index)
        {
            return (Int64)UInt64FromBytes(buf, index);
        }

        /// <summary>
        /// Reads a 64-bit integer from a big-endian series of bytes
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read.</returns>
        public static Int64 Int64FromBytesBE(byte[] buf, int index)
        {
            return (Int64)UInt64FromBytesBE(buf, index);
        }

        /// <summary>
        /// Reads a 64-bit integer from a series of bytes with a big-endian flag
        /// </summary>
        /// <param name="buf">The buffer containing the bytes</param>
        /// <param name="index">The offset into the buffer from which to read.</param>
        /// <returns>The integer read. Integers are NOT sign-extended so fewer then 4 bytes will never result in a negative number.</returns>
        public static Int64 Int64FromBytes(byte[] buf, int index, bool bigEndian)
        {
            return bigEndian ? (Int64)UInt64FromBytesBE(buf, index) : (Int64)UInt64FromBytes(buf, index);
        }

    }

}
