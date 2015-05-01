using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using FileMeta;
using System.Globalization;

namespace FMProbe
{
    class Mp4Probe
    {
        static readonly Encoding sLatin1Encoding = Encoding.GetEncoding("ISO-8859-1");

        Stream mFile;
        TextWriter mOut;

        public Mp4Probe(Stream file, TextWriter output)
        {
            mFile = file;
            mOut = output;
        }

        public void Probe()
        {
            ProbeBoxes(0, 0, mFile.Length);
        }

        public void ProbeBoxes(int nesting, long pos, long len)
        {
            long end = pos + len;
            string indent = new string(' ', nesting * 3);
            while (pos < end)
            {
                mFile.Position = pos;

                byte[] boxHeader = new byte[8];
                int readCount = mFile.Read(boxHeader, 0, 8);
                if (readCount != 8)
                {
                    mOut.WriteLine("Invalid Mp4 box -- header too short.");
                    break;
                }

                Int32 boxLen = ByteBuf.Int32FromBytesBE(boxHeader, 0);
                string boxType = sLatin1Encoding.GetString(boxHeader, 4, 4);
                mOut.WriteLine("{0}{1}: len=0x{2:x8}", indent, boxType, boxLen);

                switch (boxType)
                {
                    // Nested Boxes
                    case "moov":
                    case "udta":
                        ProbeBoxes(nesting + 1, pos + 8, boxLen - 8);
                        break;

                    // Video header
                    case "mvhd":
                        Probe_mvhd(nesting + 1, pos, boxLen);
                        break;

                    // Metadata
                    case "meta":
                        // Meta box includes 4 bytes of flags (presently all zero) before the nested boxes. Hence, offset is 12 rather than 8.
                        ProbeBoxes(nesting + 1, pos + 12, boxLen - 12);
                        break;

                    case "hdlr":
                        Probe_hdlr(nesting + 1, pos, boxLen);
                        break;

                    case "ilst":
                        Probe_ilst(nesting + 1, pos, boxLen);
                        break;

                    case "Xtra":
                        Probe_Xtra(nesting + 1, pos, boxLen);
                        break;
                }

                pos += boxLen;
            }
        }

        void Probe_mvhd(int nesting, long pos, int len)
        {
            string indent = new string(' ', nesting * 3);

            byte[] buf = new byte[len];
            mFile.Position = pos;
            int rbytes = mFile.Read(buf, 0, len);
            if (rbytes < len) throw new ApplicationException("Unexpected end of file.");

            if (Program.Verbosity > 0) mOut.Dump(0, buf, 0, rbytes);

            int i = 8;  // Skip header.
            byte version = buf[i];
            UInt32 flags = ByteBuf.UInt32FromBytesBE(buf, i) & 0x00FFFFFF;
            i += 4;

            // Get creation time and modification time
            DateTime dtCreation;
            DateTime dtModification;
            if (version == 0)
            {
                dtCreation = DateTimeFromMp4(ByteBuf.Int32FromBytesBE(buf, i));
                i += 4;
                dtModification = DateTimeFromMp4(ByteBuf.Int32FromBytesBE(buf, i));
                i += 4;
            }
            else
            {
                dtCreation = DateTimeFromMp4(ByteBuf.Int64FromBytesBE(buf, i));
                i += 8;
                dtModification = DateTimeFromMp4(ByteBuf.Int64FromBytesBE(buf, i));
                i += 8;
            }

            mOut.WriteLine("{0}version: {1}", indent, version);
            mOut.WriteLine("{0}DateCreated (utc): {1}", indent, dtCreation);
            mOut.WriteLine("{0}DateModified (utc): {1}", indent, dtCreation);
        }

        void Probe_hdlr(int nesting, long pos, int len)
        {
            string indent = new string(' ', nesting * 3);

            byte[] buf = new byte[len];
            mFile.Position = pos;
            int rbytes = mFile.Read(buf, 0, len);
            if (rbytes < len) throw new ApplicationException("Unexpected end of file.");

            if (Program.Verbosity > 0) mOut.Dump(0, buf, 0, rbytes);

            UInt32 versionFlags = ByteBuf.UInt32FromBytesBE(buf, 8);
            string quicktimeType = GetStringNullTerminated(buf, 12, 4);
            string subType = GetStringNullTerminated(buf, 16, 4);
            string reserved = GetStringNullTerminated(buf, 20, 4);

            mOut.WriteLine("{0}version: {1:x8}", indent, versionFlags);
            mOut.WriteLine("{0}Type: {1}", indent, quicktimeType);
            mOut.WriteLine("{0}Subtype: {1}", indent, subType);
            mOut.WriteLine("{0}reserved: {1}", indent, reserved);
        }

        void Probe_ilst(int nesting, long pos, int len)
        {
            string indent = new string(' ', nesting * 3);

            byte[] buf = new byte[len];
            mFile.Position = pos;
            int rbytes = mFile.Read(buf, 0, len);
            if (rbytes < len) throw new ApplicationException("Unexpected end of file.");

            if (Program.Verbosity > 0) mOut.Dump(0, buf, 0, rbytes);

            for (int i=8; i<len;)
            {
                int itemLen = ByteBuf.Int32FromBytesBE(buf, i);
                if (itemLen < 8)
                {
                    mOut.WriteLine("Invalid item length.");
                    break;
                }
                string itemName = GetStringNullTerminated(buf, i + 4, 4);

                string itemValue = string.Empty;
                for (int j = i+8; j < i+itemLen; )
                {
                    int subItemLen = ByteBuf.Int32FromBytesBE(buf, j);
                    string subItemName = GetStringNullTerminated(buf, j + 4, 4);

                    if (subItemName.Equals("data", StringComparison.Ordinal))
                    {
                        int type = ByteBuf.Int32FromBytesBE(buf, j + 8);
                        int locale = ByteBuf.Int32FromBytesBE(buf, j + 12);

                        if (string.IsNullOrEmpty(itemValue)) // There may be multiple values (due to locale) but we only care about the first one encountered
                        {
                            switch (type)
                            {
                                case 1: // UTF8
                                case 4:
                                    itemValue = Encoding.UTF8.GetString(buf, j + 16, subItemLen - 16);
                                    break;

                                case 2: // UTF16
                                case 5:
                                    itemValue = Encoding.BigEndianUnicode.GetString(buf, j + 16, subItemLen = 16);
                                    break;

                                default:
                                    itemValue = string.Format("Unsupported type {0}", type);
                                    break;
                            }
                        }
                    }

                    j += subItemLen;
                }

                mOut.WriteLine("{0}{1}: {2}", indent, itemName, itemValue);
                i += itemLen;
            }
        }

        void Probe_Xtra(int nesting, long pos, int len)
        {
            string indent = new string(' ', nesting * 3);

            byte[] buf = new byte[len];
            mFile.Position = pos;
            int rbytes = mFile.Read(buf, 0, len);
            if (rbytes < len) throw new ApplicationException("Unexpected end of file.");

            /* Each entry in Xtra consists of the following
             * 4 bytes - entry length in bytes (including the length field itself)
             * 4 bytes - label length
             * n bytes - label
             * 4 bytes - number of values
             * for each value
             *     4 bytes - data length (includes everything from these bytes on)
             *     2 bytes - data type: 00 08 = Unicode little endian
             *     n bytes - data
             * 
             * Data Types:
             *    8 - UTF-16 (Little Endian)
             *   19 - Int64
             *   21 - FILETIME
             *   72 - GUID
             */

            for (int pEntry = 8; pEntry < len; )
            {
                int cbEntry = ByteBuf.Int32FromBytesBE(buf, pEntry);
                int cbLabel = ByteBuf.Int32FromBytesBE(buf, pEntry + 4);
                string label = sLatin1Encoding.GetString(buf, pEntry + 8, cbLabel);
                int nValues = ByteBuf.Int32FromBytesBE(buf, pEntry+8+cbLabel);

                string value = string.Empty;

                int pValue = pEntry + 12 + cbLabel;
                for (int nValue = 0; nValue < nValues && pValue < pEntry + cbEntry; ++nValue)
                {
                    int cbValue = ByteBuf.Int32FromBytesBE(buf, pValue);
                    Int16 dataType = ByteBuf.Int16FromBytesBE(buf, pValue + 4);

                    string val = string.Empty;
                    switch (dataType)
                    {
                        case 8: // UTF-16 little-endian
                            val = Encoding.Unicode.GetString(buf, pValue + 6, cbValue - 6);
                            break;

                        case 19: // Int64
                            val = ByteBuf.Int64FromBytes(buf, pValue + 6).ToString();
                            break;

                        case 21: // FILETIME
                            {
                                Int64 ft = ByteBuf.Int64FromBytes(buf, pValue + 6);
                                DateTime dt = DateTime.FromFileTimeUtc(ft);
                                val = dt.ToString("yyyy'-'MM'-'dd' 'HH':'mm':'ss", CultureInfo.InvariantCulture);
                            }
                            break;

                        default:
                            val = string.Format("Unsupported: dataType={0} dataLen={1}", dataType, cbValue-6);
                            break;
                    }

                    if (string.IsNullOrEmpty(value))
                        value = val;
                    else
                        value = string.Concat(value, "; ", val);
                    
                    pValue += cbValue;
                }
                if (pValue != pEntry + cbEntry)
                {
                    mOut.WriteLine("Data length mismatch.");
                }

                mOut.WriteLine("{0}{1,24} {2}", indent, label, value);

                pEntry += cbEntry;
            }
        }

        void Dump(long pos, int len)
        {
            byte[] buf = new byte[len];
            mFile.Position = pos;
            int rlen = mFile.Read(buf, 0, len);
            mOut.Dump((int)pos, buf, 0, rlen);
        }

        static readonly DateTime cDtBase = new DateTime(1904, 1, 1, 0, 0, 0, 0, CultureInfo.InvariantCulture.Calendar,  DateTimeKind.Utc);
        static DateTime DateTimeFromMp4(long seconds)
        {
            return cDtBase.AddSeconds(seconds);
        }

        string GetStringNullTerminated(byte[] buf, int pos, int maxLen)
        {
            if (maxLen > buf.Length-pos) maxLen = buf.Length-pos;
            int len;
            for (len = 0; len<maxLen; ++len)
            {
                if (buf[pos+len] == 0) break;
            }

            return sLatin1Encoding.GetString(buf, pos, len);
        }


    }
}
