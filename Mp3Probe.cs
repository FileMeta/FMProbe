using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using FileMeta;

namespace FMProbe
{

    /// <summary>
    /// Probes ID3v2.3 files. Today, that version is used almost
    /// exclusively. Future enhancements may include other versions
    /// of ID3.
    /// </summary>
    class Mp3Probe
    {
        Stream mFile;
        TextWriter mOut;

        static readonly string sErrTruncate = "MP3 file is truncated.";

        public Mp3Probe(Stream file, TextWriter output)
        {
            mFile = file;
            mOut = output;
        }

        public void Probe()
        {
            // Read the ID3v2 header
            byte[] header = new byte[10];
            mFile.Position = 0;
            int headerLen = mFile.Read(header, 0, 10);

            // Dump the header
            mOut.WriteLine(" --- Header ---");
            mOut.Dump(0, header, 0, headerLen);

            // Check the header and version
            if (headerLen < 10)
            {
                mOut.WriteLine("Invalid MP3/ID3 file. File too short.");
                return;
            }
            if (!ByteBuf.Match(header, 0, headerLen, "ID3")
                || (header[3] == (byte)0xFF || header[4] == (byte)0xFF)
                || (header[6] >= (byte)0x8F || header[7] >= (byte)0x8F || header[8] >= (byte)0x8F || header[9] >= (byte)0x8F)
                )
            {
                mOut.WriteLine("Invalid MP3/ID3 file header.");
                return;
            }
            mOut.WriteLine("FileType: MP3 with ID3v2 metadata.");
            mOut.WriteLine("ID3v2_Version: {0:d}.{1:d}", header[3], header[4]);
            bool unsync = (header[5] & 0x80) != 0;
            mOut.WriteLine("Unsynchronization: {0}", unsync ? 1 : 0);
            bool extendedHeader = (header[5] & 0x40) != 0;
            mOut.WriteLine("ExtendedHeader: {0}", extendedHeader ? 1 : 0);
            bool experimental = (header[5] & 0x40) != 0;
            mOut.WriteLine("Experimental: {0}", experimental ? 1 : 0);

            int tagSize = FromSevenBitEncoding(header, 6);
            mOut.WriteLine("TagSize: {0:d}", tagSize);
            mOut.WriteLine();

            // Extended header
            int extendedHeaderSize = 0;
            if (extendedHeader)
            {
                mOut.WriteLine(" --- Extended Header ---");
                byte[] xhSize = new byte[4];
                if (4 != mFile.Read(xhSize, 0, 4)) throw new FormatException(sErrTruncate);
                extendedHeaderSize = FromSevenBitEncoding(xhSize, 0);  // Not clear whether 7 bit encoding is really used here but since values will be no more than 10 it doesn't matter.

                // Now we know the size, read the whole extended header
                mFile.Seek(-4, SeekOrigin.Current);
                byte[] xheader = new byte[extendedHeaderSize+4];
                if ((extendedHeaderSize+4) != mFile.Read(xheader, 0, extendedHeaderSize+4)) throw new FormatException(sErrTruncate);
                mOut.Dump(10, xheader, 0, extendedHeaderSize + 4);
            }

            // Tag Body
            int tagBodySize = tagSize - extendedHeaderSize;
            byte[] tagBody = new byte[tagBodySize];
            long tagBodyPos = mFile.Position;
            if (tagBodySize != mFile.Read(tagBody, 0, tagBodySize)) throw new FormatException(sErrTruncate);
            mOut.Dump((int)tagBodyPos, tagBody, 0, tagBodySize);

        }

        private int FromSevenBitEncoding(byte[] buf, int offset)
        {
            if (offset + 4 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return ((int)(buf[offset] & (byte)0x7F) << 21)
                | ((int)(buf[offset + 1] & (byte)0x7F) << 14)
                | ((int)(buf[offset + 2] & (byte)0x7F) << 7)
                | ((int)(buf[offset + 3] & (byte)0x7F));
        }

    }
}
