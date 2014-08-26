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
        class FrameInfo
        {
            public FrameInfo(string name)
            {
                Name = name;
                HasTextEncoding = false;
            }

            public FrameInfo(string name, bool hasTextEncoding)
            {
                Name = name;
                HasTextEncoding = hasTextEncoding;
            }

            public string Name { get; private set; }
            public bool HasTextEncoding { get; private set; }
        }

        Stream mFile;
        TextWriter mOut;

        static readonly string sErrTruncate = "MP3 file is truncated.";
        static readonly Encoding sEncodingLatin1 = Encoding.GetEncoding("iso-8859-1");
        static Dictionary<string, FrameInfo> sFrameIdToInfo;

        static string[] cId3ContentTypes =
        {
            "Blues", // 0
            "Classic Rock", // 1
            "Country", // 2
            "Dance", // 3
            "Disco", // 4
            "Funk", // 5
            "Grunge", // 6
            "Hip-Hop", // 7
            "Jazz", // 8
            "Metal", // 9
            "New Age", // 10
            "Oldies", // 11
            "Other", // 12
            "Pop", // 13
            "R&B", // 14
            "Rap", // 15
            "Reggae", // 16
            "Rock", // 17
            "Techno", // 18
            "Industrial", // 19
            "Alternative", // 20
            "Ska", // 21
            "Death Metal", // 22
            "Pranks", // 23
            "Soundtrack", // 24
            "Euro-Techno", // 25
            "Ambient", // 26
            "Trip-Hop", // 27
            "Vocal", // 28
            "Jazz", // 29+Funk
            "Fusion", // 30
            "Trance", // 31
            "Classical", // 32
            "Instrumental", // 33
            "Acid", // 34
            "House", // 35
            "Game", // 36
            "Sound Clip", // 37
            "Gospel", // 38
            "Noise", // 39
            "AlternRock", // 40
            "Bass", // 41
            "Soul", // 42
            "Punk", // 43
            "Space", // 44
            "Meditative", // 45
            "Instrumental Pop", // 46
            "Instrumental Rock", // 47
            "Ethnic", // 48
            "Gothic", // 49
            "Darkwave", // 50
            "Techno-Industrial", // 51
            "Electronic", // 52
            "Pop-Folk", // 53
            "Eurodance", // 54
            "Dream", // 55
            "Southern Rock", // 56
            "Comedy", // 57
            "Cult", // 58
            "Gangsta", // 59
            "Top ", // 6040
            "Christian Rap", // 61
            "Pop", // 62/Funk
            "Jungle", // 63
            "Native American", // 64
            "Cabaret", // 65
            "New Wave", // 66
            "Psychadelic", // 67
            "Rave", // 68
            "Showtunes", // 69
            "Trailer", // 70
            "Lo-Fi", // 71
            "Tribal", // 72
            "Acid Punk", // 73
            "Acid Jazz", // 74
            "Polka", // 75
            "Retro", // 76
            "Musical", // 77
            "Rock & Roll", // 78
            "Hard Rock", // 79
            //The following genres are Winamp extensions
            "Folk", // 80
            "Folk-Rock", // 81
            "National Folk", // 82
            "Swing", // 83
            "Fast Fusion", // 84
            "Bebob", // 85
            "Latin", // 86
            "Revival", // 87
            "Celtic", // 88
            "Bluegrass", // 89
            "Avantgarde", // 90
            "Gothic Rock", // 91
            "Progressive Rock", // 92
            "Psychedelic Rock", // 93
            "Symphonic Rock", // 94
            "Slow Rock", // 95
            "Big Band", // 96
            "Chorus", // 97
            "Easy Listening", // 98
            "Acoustic", // 99
            "Humour", // 100
            "Speech", // 101
            "Chanson", // 102
            "Opera", // 103
            "Chamber Music", // 104
            "Sonata", // 105
            "Symphony", // 106
            "Booty Bass", // 107
            "Primus", // 108
            "Porn Groove", // 109
            "Satire", // 110
            "Slow Jam", // 111
            "Club", // 112
            "Tango", // 113
            "Samba", // 114
            "Folklore", // 115
            "Ballad", // 116
            "Power Ballad", // 117
            "Rhythmic Soul", // 118
            "Freestyle", // 119
            "Duet", // 120
            "Punk Rock", // 121
            "Drum Solo", // 122
            "A capella", // 123
            "Euro-House", // 124
            "Dance Hall", // 125
        };

        static Mp3Probe()
        {
            sFrameIdToInfo = new Dictionary<string, FrameInfo>();
            sFrameIdToInfo.Add("AENC", new FrameInfo("AudioEncryption"));
            sFrameIdToInfo.Add("APIC", new FrameInfo("AttachedPicture"));
            sFrameIdToInfo.Add("COMM", new FrameInfo("Comments", true));
            sFrameIdToInfo.Add("COMR", new FrameInfo("CommercialFrame"));
            sFrameIdToInfo.Add("ENCR", new FrameInfo("EncryptionMethodRegistration"));
            sFrameIdToInfo.Add("EQUA", new FrameInfo("Equalization"));
            sFrameIdToInfo.Add("ETCO", new FrameInfo("EventTimingCodes"));
            sFrameIdToInfo.Add("GEOB", new FrameInfo("GeneralEncapsulatedObject"));
            sFrameIdToInfo.Add("GRID", new FrameInfo("GroupIdentificationRegistration"));
            sFrameIdToInfo.Add("IPLS", new FrameInfo("InvolvedPeopleList"));
            sFrameIdToInfo.Add("LINK", new FrameInfo("LinkedInformation"));
            sFrameIdToInfo.Add("MCDI", new FrameInfo("MusicCdIdentifier"));
            sFrameIdToInfo.Add("MLLT", new FrameInfo("MpegLocationLookupTable"));
            sFrameIdToInfo.Add("OWNE", new FrameInfo("OwnershipFrame"));
            sFrameIdToInfo.Add("PRIV", new FrameInfo("PrivateFrame"));
            sFrameIdToInfo.Add("PCNT", new FrameInfo("PlayCounter"));
            sFrameIdToInfo.Add("POPM", new FrameInfo("Popularimeter"));
            sFrameIdToInfo.Add("POSS", new FrameInfo("PositionSynchronizationFrame"));
            sFrameIdToInfo.Add("RBUF", new FrameInfo("RecommendedBufferSize"));
            sFrameIdToInfo.Add("RVAD", new FrameInfo("RelativeVolumeAdjustment"));
            sFrameIdToInfo.Add("RVRB", new FrameInfo("Reverb"));
            sFrameIdToInfo.Add("SYLT", new FrameInfo("SynchronizedLyricText"));
            sFrameIdToInfo.Add("SYTC", new FrameInfo("SynchronizedTempoCodes"));
            sFrameIdToInfo.Add("TALB", new FrameInfo("AlbumTitle", true));
            sFrameIdToInfo.Add("TBPM", new FrameInfo("BeatsPerMinute", true));
            sFrameIdToInfo.Add("TCOM", new FrameInfo("Composer", true));
            sFrameIdToInfo.Add("TCON", new FrameInfo("Genre", true));
            sFrameIdToInfo.Add("TCOP", new FrameInfo("Copyright", true));
            sFrameIdToInfo.Add("TDAT", new FrameInfo("Date", true));
            sFrameIdToInfo.Add("TDLY", new FrameInfo("PlaylistDelay"));
            sFrameIdToInfo.Add("TENC", new FrameInfo("EncodedBy", true));
            sFrameIdToInfo.Add("TEXT", new FrameInfo("Lyricist", true));
            sFrameIdToInfo.Add("TFLT", new FrameInfo("FileType", true));
            sFrameIdToInfo.Add("TIME", new FrameInfo("Time", true));
            sFrameIdToInfo.Add("TIT1", new FrameInfo("ContentGroupDescription", true));
            sFrameIdToInfo.Add("TIT2", new FrameInfo("Title", true));
            sFrameIdToInfo.Add("TIT3", new FrameInfo("Subtitle", true));
            sFrameIdToInfo.Add("TKEY", new FrameInfo("Key", true));
            sFrameIdToInfo.Add("TLAN", new FrameInfo("Language", true));
            sFrameIdToInfo.Add("TLEN", new FrameInfo("Length", true));
            sFrameIdToInfo.Add("TMED", new FrameInfo("MediaType", true));
            sFrameIdToInfo.Add("TOAL", new FrameInfo("OriginalAlbumTitle", true));
            sFrameIdToInfo.Add("TOFN", new FrameInfo("OriginalFilename", true));
            sFrameIdToInfo.Add("TOLY", new FrameInfo("OriginalLyricist", true));
            sFrameIdToInfo.Add("TOPE", new FrameInfo("OriginalArtist", true));
            sFrameIdToInfo.Add("TORY", new FrameInfo("OriginalReleaseYear", true));
            sFrameIdToInfo.Add("TOWN", new FrameInfo("FileOwner", true));
            sFrameIdToInfo.Add("TPE1", new FrameInfo("Artist", true));
            sFrameIdToInfo.Add("TPE2", new FrameInfo("AlbumArtist", true));
            sFrameIdToInfo.Add("TPE3", new FrameInfo("Conductor", true));
            sFrameIdToInfo.Add("TPE4", new FrameInfo("MixedBy", true));
            sFrameIdToInfo.Add("TPOS", new FrameInfo("PartOfSet", true));
            sFrameIdToInfo.Add("TPUB", new FrameInfo("Publisher", true));
            sFrameIdToInfo.Add("TRCK", new FrameInfo("TrackNumber", true));
            sFrameIdToInfo.Add("TRDA", new FrameInfo("RecordingDates", true));
            sFrameIdToInfo.Add("TRSN", new FrameInfo("InternetRadioStationName", true));
            sFrameIdToInfo.Add("TRSO", new FrameInfo("InternetRadioStationOwner", true));
            sFrameIdToInfo.Add("TSIZ", new FrameInfo("Size", true));
            sFrameIdToInfo.Add("TSRC", new FrameInfo("ISRC", true));
            sFrameIdToInfo.Add("TSSE", new FrameInfo("EncodingSettings", true));
            sFrameIdToInfo.Add("TYER", new FrameInfo("Year", true));
            sFrameIdToInfo.Add("TXXX", new FrameInfo("UserText", true));
            sFrameIdToInfo.Add("UFID", new FrameInfo("UniqueFileId"));
            sFrameIdToInfo.Add("USER", new FrameInfo("TermsOfUse"));
            sFrameIdToInfo.Add("USLT", new FrameInfo("UnsynchronizedLyric"));
            sFrameIdToInfo.Add("WCOM", new FrameInfo("CommercialInfoUrl"));
            sFrameIdToInfo.Add("WCOP", new FrameInfo("CopyrightInfoUrl"));
            sFrameIdToInfo.Add("WOAF", new FrameInfo("AudioFileUrl"));
            sFrameIdToInfo.Add("WOAR", new FrameInfo("ArtistUrl"));
            sFrameIdToInfo.Add("WOAS", new FrameInfo("AudioSourceUrl"));
            sFrameIdToInfo.Add("WORS", new FrameInfo("InternetRadioStationUrl"));
            sFrameIdToInfo.Add("WPAY", new FrameInfo("PaymentUrl"));
            sFrameIdToInfo.Add("WPUB", new FrameInfo("PublisherUrl"));
            sFrameIdToInfo.Add("WXXX", new FrameInfo("UserDefinedUrl"));
        }

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

            // Read Tag Body
            int tagBodySize = tagSize - extendedHeaderSize;
            byte[] tagBody = new byte[tagBodySize];
            long tagBodyPos = mFile.Position;
            if (tagBodySize != mFile.Read(tagBody, 0, tagBodySize)) throw new FormatException(sErrTruncate);

            // Remove unsynchronization
            if (unsync)
            {
                int i;
                for (i = 0; i<tagBodySize-2; ++i)
                {
                    if (tagBody[i] == (byte)0xFF && tagBody[i+1] == (byte)0) break;
                }
                int o = i;
                for (; i<tagBodySize-2; ++i)
                {
                    tagBody[o++] = tagBody[i];
                    if (tagBody[i] == (byte)0xFF && tagBody[i+1] == (byte)0)
                    {
                        ++i; // Skip the extra byte
                    }
                }
                for (; i<tagBodySize; ++i)
                {
                    tagBody[o++] = tagBody[i];
                }
                tagBodySize = o;
            }

            // Dump the frames
            for (int i=0; i < tagBodySize; )
            {
                if (tagBody[i] == 0) break; // We're into the padding

                mOut.WriteLine(" --- Frame ---");
                string frameId = sEncodingLatin1.GetString(tagBody, i, 4);
                int frameSize = Int32FromBytes(tagBody, i + 4);
                try
                {
                    bool tagAlterPreservation = (tagBody[i+8] & 0x80) != 0;
                    bool fileAlterPreservation = (tagBody[i+8] & 0x40) != 0;
                    bool readOnly = (tagBody[i+8] & 0x20) != 0;
                    bool compression = (tagBody[i+9] & 0x80) != 0;
                    bool encryption = (tagBody[i+9] & 0x40) != 0;
                    bool grouping = (tagBody[i+9] & 0x20) != 0;

                    mOut.Dump((int)tagBodyPos + i, tagBody, i, frameSize+10);
                    mOut.WriteLine("FrameId: {0}", frameId);
                    mOut.WriteLine("FrameSize: {0:d}", frameSize);
                    mOut.WriteLine("TagAlterPreservation: {0}", tagAlterPreservation ? 1 : 0);
                    mOut.WriteLine("FileAlterPreservation: {0}", fileAlterPreservation ? 1 : 0);
                    mOut.WriteLine("ReadOnly: {0}", readOnly ? 1 : 0);
                    mOut.WriteLine("compression: {0}", compression ? 1 : 0);
                    mOut.WriteLine("encryption: {0}", encryption ? 1 : 0);
                    mOut.WriteLine("grouping: {0}", grouping ? 1 : 0);

                    int ii = i + 10;
                    int frameEnd = ii + frameSize;
                    FrameInfo fi = FrameIdToInfo(frameId);

                    byte textEncoding = 0;
                    if (fi.HasTextEncoding)
                    {
                        textEncoding = tagBody[ii++];
                        mOut.WriteLine("TextEncoding: {0}", TeTranslate(textEncoding));
                    }

                    mOut.WriteLine();
                    mOut.WriteLine("Name: {0}", fi.Name);

                    if (string.Equals(frameId, "TXXX", StringComparison.Ordinal) || string.Equals(frameId, "WXXX", StringComparison.Ordinal)) // user-defined text frame
                    {
                        string description = DecodeText(textEncoding, tagBody, ref ii, frameEnd);
                        string value = DecodeText(textEncoding, tagBody, ref ii, frameEnd);
                        mOut.WriteLine("Description: {0}", description);
                        mOut.WriteLine("Value: {0}", value);
                    }
                    else if (string.Equals(frameId, "TCON", StringComparison.Ordinal)) // Genre
                    {
                        string value = DecodeText(textEncoding, tagBody, ref ii, frameEnd).Trim();
                        if (value[0] == '(') // Translate numeric genres
                        {
                            int end = value.IndexOf(')');
                            if (end < 0) end = value.Length;
                            int genreNum = int.Parse(value.Substring(1, end - 1));
                            if (genreNum < cId3ContentTypes.Length) value = cId3ContentTypes[genreNum];
                        }
                        mOut.WriteLine("Value: {0}", value);
                    }
                    else if (frameId[0] == 'T' || frameId[0] == 'W') // all other text frames
                    {
                        string value = DecodeText(textEncoding, tagBody, ref ii, frameEnd);
                        mOut.WriteLine("Value: {0}", value);
                    }
                    else
                    {
                        switch (frameId)
                        {
                            case "PRIV":
                                {
                                    string ownerId = DecodeText(0, tagBody, ref ii, frameEnd);
                                    mOut.WriteLine("OwnerId: {0}", ownerId);
                                }
                                break;

                            case "COMM":
                                {
                                    string language = sEncodingLatin1.GetString(tagBody, ii, 3);
                                    ii += 3;
                                    string description = DecodeText(0, tagBody, ref ii, frameEnd);
                                    string value = DecodeText(0, tagBody, ref ii, frameEnd);
                                    mOut.WriteLine("Language: {0}", language);
                                    mOut.WriteLine("Description: {0}", description);
                                    mOut.WriteLine("Value: {0}", value);
                                }
                                break;
                        }
                    }
                }
                catch (Exception err)
                {
                    mOut.WriteLine(err.Message);
                }
                mOut.WriteLine();

                i += frameSize + 10;
            }

            //mOut.Dump((int)tagBodyPos, tagBody, 0, tagBodySize);

        }

        static string DecodeText(byte textEncoding, byte[] tagBody, ref int rii, int frameEnd)
        {
            // Find a terminating null or the end of the frame whichever comes first
            int begin = rii;
            int end = begin;
            if (textEncoding == 1 || textEncoding == 2) // double-byte characters
            {
                while (end < frameEnd-1 && (tagBody[end] != 0 || tagBody[end+1] != 0)) end += 2;
                rii = (end < frameEnd-1) ? end+2 : frameEnd;
            }
            else
            {
                while (end < frameEnd && tagBody[end] != 0) ++end;
                rii = (end < frameEnd) ? end+1 : frameEnd;
            }

            // Choose the decoder
            Encoding encoding;
            switch (textEncoding)
            {
                case 0:
                    encoding = sEncodingLatin1;
                    break;

                case 1:
                    encoding = Encoding.UTF32;
                    break;

                case 2:
                    encoding = Encoding.BigEndianUnicode;
                    break;

                case 3:
                    encoding = Encoding.UTF8;
                    break;

                default:
                    throw new ApplicationException(string.Format("Invalid TextEncoding: {0:d}", textEncoding));
            }

            // We use this awkward stream framework in order to take advantage of StreamReader's byte-order-mark detection
            using (MemoryStream stream = new MemoryStream(tagBody, begin, end-begin, false))
            {
                using (StreamReader reader = new StreamReader(stream, encoding, true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        static int FromSevenBitEncoding(byte[] buf, int index)
        {
            if (index + 4 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return ((int)(buf[index] & (byte)0x7F) << 21)
                | ((int)(buf[index + 1] & (byte)0x7F) << 14)
                | ((int)(buf[index + 2] & (byte)0x7F) << 7)
                | ((int)(buf[index + 3] & (byte)0x7F));
        }

        static int Int32FromBytes(byte[] buf, int index)
        {
            if (index + 4 > buf.Length) throw new ArgumentException("Read beyond end of buffer.");
            return (int)(((uint)buf[index] << 24)
                | ((uint)buf[index + 1] << 16)
                | ((uint)buf[index + 2] << 8)
                | ((uint)buf[index + 3]));
        }

        static FrameInfo FrameIdToInfo(string frameId)
        {
            FrameInfo result;
            if (!sFrameIdToInfo.TryGetValue(frameId, out result))
            {
                result = new FrameInfo(string.Concat("<", frameId, ">"));
            }
            return result;
        }

        static string TeTranslate(byte te)
        {
            switch (te)
            {
                case 0:
                    return "LATIN-1";
                case 1:
                    return "UTF-16";
                case 2:
                    return "UTF-16BE";
                case 3:
                    return "UTF-8";
                default:
                    throw new ApplicationException(string.Format("Invalid TextEncoding: {0:d}", te));
            }
        }

    }
}
