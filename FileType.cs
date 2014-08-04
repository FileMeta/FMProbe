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
        jpeg = 3
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
        const int cMaxHeaderBytes = 32;
        static readonly byte[] sMp3_Header = {(byte)'I', (byte)'D', (byte)'3'};

        public static FileTypeId GetFileType(byte[] header, int offset, int len)
        {
            if (ByteBuf.Match(header, offset, len, sMp3_Header) // ID3v2 prefix
                && (header[3] >= 3 && header[3] <= 4) // Version 3 or 4
                && (header[4] == 00)) // No minor versions for ID3 have been used.
                return FileTypeId.mp3;

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
            while (aOffset < aCount && bOffset < bCount)
            {
                if (a[aOffset] < b[bOffset]) return -1;
                if (a[aOffset] > b[bOffset]) return 1;
                ++aOffset;
                ++bOffset;
            }
            if (aOffset < aCount) return -1;
            if (bOffset < bCount) return 1;
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

    }

}
