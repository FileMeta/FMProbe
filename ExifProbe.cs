using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using FileMeta;

/* JPEG and EXIF File Info:
 * http://www.media.mit.edu/pia/Research/deepview/exif.html
 * http://www.cipa.jp/std/documents/e/DC-008-2012_E.pdf
 * http://www.fileformat.info/format/tiff/corion.htm
 * http://www.sno.phy.queensu.ca/~phil/exiftool/TagNames/EXIF.html
 */

namespace FMProbe
{
    class ExifProbe
    {
        static readonly byte[] sExifMarker = { 0xff, 0xe1 };
        static readonly byte[] sExifHeader = { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00, };
        static readonly byte[] sXmpHeader = /* http://ns.adobe.com/xap/1.0/ */ { (byte)'h', (byte)'t', (byte)'t', (byte)'p', (byte)':', (byte)'/', (byte)'/', (byte)'n', (byte)'s', (byte)'.', (byte)'a', (byte)'d', (byte)'o', (byte)'b', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m', (byte)'/', (byte)'x', (byte)'a', (byte)'p', (byte)'/', (byte)'1', (byte)'.', (byte)'0', (byte)'/', 0 };
        static readonly byte[] sTiffHeader_I = { (byte)'I', (byte)'I', 42, 0 };
        static readonly byte[] sTiffHeader_M = { (byte)'M', (byte)'M', 0, 42};
        static readonly byte[] sUcASCII = {(byte)'A', (byte)'S', (byte)'C', (byte)'I', (byte)'I', 0, 0, 0};
        static readonly byte[] sUcJIS =   {(byte)'J', (byte)'I', (byte)'S', 0, 0, 0, 0, 0};
        static readonly byte[] sUcUNICODE = {(byte)'U', (byte)'N', (byte)'I', (byte)'C', (byte)'O', (byte)'D', (byte)'E', 00};
        const int sExifHeaderLen = 18;

        Stream mFile;
        TextWriter mOut;
        bool mTiffBigEndian = false;

        public ExifProbe(Stream file, TextWriter output)
        {
            mFile = file;
            mOut = output;
        }

        public void Probe()
        {
            mFile.Position = 0;
            byte[] markerBuf = new byte[4];

            // Read the Start of Image marker
            int headerLen = mFile.Read(markerBuf, 0, 2);
            if (headerLen < 2)
            {
                throw new ApplicationException("Invalid JPEG file. Unexpected end of file.");
            }
            JpegMarker marker = (JpegMarker)ByteBuf.UInt16FromBytesBE(markerBuf, 0);
            if (marker != JpegMarker.StartOfImage)
            {
                throw new ApplicationException("Invalid JPEG file. No Start of Image marker.");
            }

            if (Program.Verbosity > 0) mOut.WriteLine("{0}(0x{1:x4})", marker, (int)marker);

            // Read each JPEG marker
            long markerPos = 2;
            for (; ; )
            {
                mFile.Position = markerPos;
                int markerLen = mFile.Read(markerBuf, 0, 4);
                if (markerLen < 2) break;

                marker = (JpegMarker)ByteBuf.UInt16FromBytesBE(markerBuf, 0);
                int segmentLen = (markerLen >= 4) ? (int)ByteBuf.UInt16FromBytesBE(markerBuf, 2) : 0;
                if (Program.Verbosity > 0)
                {
                    mOut.WriteLine();
                    mOut.WriteLine("{0}(0x{1:x4})", marker, (int)marker);
                    mOut.WriteLine("Length: {0}", segmentLen);
                }

                if (markerLen < 4) break;

                if (marker == JpegMarker.App1)
                {
                    ProbeApp1(markerPos+4, segmentLen-2); // Marker + length = 4 bytes. Segment length includes length but not marker.
                }

                markerPos += segmentLen + 2;
            }
        }

        void ProbeApp1(long pos, int len)
        {
            if (len < sXmpHeader.Length) return;

            byte[] buf = new byte[sXmpHeader.Length];
            mFile.Position = pos;
            if (mFile.Read(buf, 0, sXmpHeader.Length) < sXmpHeader.Length)
            {
                if (Program.Verbosity > 0) mOut.WriteLine("Truncated App1 header");
                return;
            }

            if (ByteBuf.Match(buf, 0, sXmpHeader.Length, sExifHeader))
            {
                ProbeExif(pos + sExifHeader.Length, len - sExifHeader.Length);
            }
            else if (ByteBuf.Match(buf, 0, sXmpHeader.Length, sXmpHeader))
            {
                ProbeXmp(pos + sXmpHeader.Length, len - sXmpHeader.Length);
            }
        }

        void ProbeExif(long tiffPos, int tiffLen)
        {
            byte[] buf = new byte[tiffLen];
            mFile.Position = tiffPos;
            if (mFile.Read(buf, 0, tiffLen) < tiffLen)
            {
                if (Program.Verbosity > 0) mOut.WriteLine("Truncated Exif Data");
                return;
            }
            if (ByteBuf.Match(buf, 0, tiffLen, sTiffHeader_I))
            {
                mTiffBigEndian = false;
            }
            else if (ByteBuf.Match(buf, 0, tiffLen, sTiffHeader_M))
            {
                mTiffBigEndian = true;
            }
            else
            {
                if (Program.Verbosity > 0) mOut.WriteLine("Invalid Exif segment. Missing TIFF header (yes, Exif uses an embedded TIFF header).");
                return;
            }

            // Get the offset of the TIFF IFD0
            int ifd0Offset = (int)ByteBuf.UInt32FromBytes(buf, sTiffHeader_I.Length, mTiffBigEndian);

            // Write the header results
            if (Program.Verbosity > 0)
            {
                mOut.WriteLine("Byte Order: {0}", mTiffBigEndian ? "Big-Endian" : "Little-Endian");
                mOut.WriteLine("ifd0 Offset: {0}", ifd0Offset);
            }

            // Probe each Image File Directory (IFD)
            int ifdOffset = ifd0Offset;
            for (int ifd=0; true; ++ifd)
            {
                mOut.WriteLine("ifd{0}", ifd);

                int entryCount = ProbeIfd(buf, ifdOffset);

                // Get the offset of the next IFD
                ifdOffset = (int)ByteBuf.UInt32FromBytes(buf, ifdOffset + 2 + entryCount * 12, mTiffBigEndian);
                if (ifdOffset == 0) break;
             }
        }

        int ProbeIfd(byte[] buf, int ifdOffset)
        {
            int entryCount = ByteBuf.UInt16FromBytes(buf, ifdOffset, mTiffBigEndian);
            if (Program.Verbosity > 0)
            {
                mOut.WriteLine("   IfdOffset: 0x{0:x8}", ifdOffset);
                mOut.WriteLine("   IfdEntries: {0:d}", entryCount);
            }

            int subIfdOffset = 0;

            // Write entries
            for (int entry = 0; entry < entryCount; ++entry)
            {
                int entryOffset = ifdOffset + 2 + entry * 12;
                int tag = ByteBuf.UInt16FromBytes(buf, entryOffset, mTiffBigEndian);
                int type = ByteBuf.UInt16FromBytes(buf, entryOffset + 2, mTiffBigEndian);
                UInt32 n = ByteBuf.UInt32FromBytes(buf, entryOffset + 4, mTiffBigEndian);

                ExifType exifReportType;
                {
                    ExifType over;
                    exifReportType = sTypeOverride.TryGetValue((ExifTag)tag, out over) ? over : (ExifType)type;
                }

                string value;
                switch (exifReportType)
                {
                    case ExifType.byte_:
                    case ExifType.Undefined:
                    default:
                        {
                            int off = (n <= 8) ? entryOffset + 8 : (int)ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian);
                            StringWriter writer = new StringWriter();
                            writer.Dump(0, buf, off, (int)n);
                            writer.Flush();
                            value = writer.ToString().Trim();
                        }
                        break;

                    case ExifType.ASCII:
                        {
                            int off = (int)ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian);
                            value = Encoding.ASCII.GetString(buf, off, (int)n);
                        }
                        break;

                    case ExifType.UInt16_:
                        value = ByteBuf.UInt16FromBytes(buf, entryOffset + 8, mTiffBigEndian).ToString();
                        break;

                    case ExifType.UInt32_:
                        value = ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian).ToString();
                        break;

                    case ExifType.URational:
                        {
                            int off = (int)ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian);
                            value = string.Format("{0}/{1}",
                                ByteBuf.UInt32FromBytes(buf, off, mTiffBigEndian),
                                ByteBuf.UInt32FromBytes(buf, off+4, mTiffBigEndian));
                        }
                        break;

                    case ExifType.Int32_:
                        value = ByteBuf.Int32FromBytes(buf, entryOffset + 8, mTiffBigEndian).ToString();
                        break;

                    case ExifType.Rational:
                        {
                            int off = (int)ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian);
                            value = string.Format("{0}/{1}",
                                ByteBuf.Int32FromBytes(buf, off, mTiffBigEndian),
                                ByteBuf.Int32FromBytes(buf, off + 4, mTiffBigEndian));
                        }
                        break;

                    case ExifType.xLengthInBytes:
                        value = string.Format("{0} bytes.", n);
                        break;

                    case ExifType.xSubIFD:
                        if (subIfdOffset != 0) throw new ApplicationException("Multiple ExifSubIFD entries!");
                        subIfdOffset = (int)ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian);
                        value = string.Format("SubIfd reported below.");
                        break;

                    case ExifType.xUtf16:
                        {
                            int off = (int)ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian);
                            value = Encoding.Unicode.GetString(buf, off, (int)n);
                        }
                        break;

                    case ExifType.xUserComment:
                        {
                            int off = (int)ByteBuf.UInt32FromBytes(buf, entryOffset + 8, mTiffBigEndian);

                            // Character encoding is in the first eight bytes
                            if (n > 8)
                            {
                                if (ByteBuf.Match(buf, off, (int)n, sUcASCII))
                                {
                                    value = Encoding.ASCII.GetString(buf, off + 8, (int)n - 8);
                                }
                                else if (ByteBuf.Match(buf, off, (int)n, sUcJIS))
                                {
                                    value = "Unsupported: JIS String";
                                }
                                else if (ByteBuf.Match(buf, off, (int)n, sUcUNICODE))
                                {
                                    value = Encoding.Unicode.GetString(buf, off + 8, (int)n - 8);
                                }
                                else
                                {
                                    value = string.Empty;
                                }
                            }
                            else
                            {
                                value = string.Empty;
                            }
                            
                        }
                        break;
                }

                mOut.WriteLine("   {0}(0x{1:x4}): {2}", ExifTagName(tag), tag, value);
                if (Program.Verbosity > 0)
                {
                    mOut.WriteLine("      entry={0:d2}, type={1}, count={2}", entry, ExifTypeName(type), n);
                }
            }

            if (subIfdOffset != 0)
            {
                mOut.WriteLine("ExifSubIFD");
                ProbeIfd(buf, subIfdOffset);
            }

            return entryCount;
        }

        void ProbeXmp(long xmpPos, int xmpLen)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(new SubStream(mFile, xmpPos, xmpLen));

            XmlWriterSettings writerSettings = new XmlWriterSettings();
            writerSettings.OmitXmlDeclaration = true;
            writerSettings.ConformanceLevel = ConformanceLevel.Fragment;
            writerSettings.Indent = true;
            writerSettings.CloseOutput = false;
            using (XmlWriter writer = XmlWriter.Create(mOut, writerSettings))
            {
                xmlDoc.WriteTo(writer);
            }
        }

        enum ExifType
        {
            byte_ = 1,
            ASCII = 2,
            UInt16_ = 3,
            UInt32_ = 4,
            URational = 5,
            Undefined = 7,
            Int32_ = 9,
            Rational = 10,

            // Extended types - for this program
            xLengthInBytes = 1001,
            xSubIFD = 1002,
            xUtf16 = 1003,
            xUserComment = 1004
        }

        static Dictionary<ExifTag, ExifType> sTypeOverride = new Dictionary<ExifTag, ExifType>();

        static ExifProbe()
        {
            sTypeOverride.Add(ExifTag.ExifOffset, ExifType.xSubIFD);
            sTypeOverride.Add(ExifTag.MakerNote, ExifType.xLengthInBytes);
            sTypeOverride.Add(ExifTag.Padding, ExifType.xLengthInBytes);
            sTypeOverride.Add(ExifTag.XPTitle, ExifType.xUtf16);
            sTypeOverride.Add(ExifTag.XPComment, ExifType.xUtf16);
            sTypeOverride.Add(ExifTag.XPAuthor, ExifType.xUtf16);
            sTypeOverride.Add(ExifTag.XPKeywords, ExifType.xUtf16);
            sTypeOverride.Add(ExifTag.XPSubject, ExifType.xUtf16);
            sTypeOverride.Add(ExifTag.UserComment, ExifType.xUserComment);
        }

        static string JpegMarkerName(int tag)
        {
            JpegMarker t = (JpegMarker)tag;
            return Enum.IsDefined(typeof(JpegMarker), t) ? t.ToString() : string.Format("0x{0:x4}", tag);
        }

        static string ExifTagName(int tag)
        {
            ExifTag t = (ExifTag)tag;
            return Enum.IsDefined(typeof(ExifTag), t) ? t.ToString() : string.Format("0x{0:x4}", tag);
        }

        static string ExifTypeName(int type)
        {
            ExifType t = (ExifType)type;
            return Enum.IsDefined(typeof(ExifType), t) ? t.ToString() : string.Format("0x{0:x4}", type);
        }

        enum JpegMarker
        {
            // Start of Frame markers, non-differential, Huffman coding
            HuffBaselineDCT = 0xFFC0,
            HuffExtSequentialDCT = 0xFFC1,
            HuffProgressiveDCT = 0xFFC2,
            HuffLosslessSeq = 0xFFC3,

            // Start of Frame markers, differential, Huffman coding
            HuffDiffSequentialDCT = 0xFFC5,
            HuffDiffProgressiveDCT = 0xFFC6,
            HuffDiffLosslessSeq = 0xFFC7,

            // Start of Frame markers, non-differential, arithmetic coding
            ArthBaselineDCT = 0xFFC8,
            ArthExtSequentialDCT = 0xFFC9,
            ArthProgressiveDCT = 0xFFCA,
            ArthLosslessSeq = 0xFFCB,

            // Start of Frame markers, differential, arithmetic coding
            ArthDiffSequentialDCT = 0xFFCD,
            ArthDiffProgressiveDCT = 0xFFCE,
            ArthDiffLosslessSeq = 0xFFCF,

            // Huffman table spec
            HuffmanTableDef = 0xFFC4,

            // Arithmetic table spec
            ArithmeticTableDef = 0xFFCC,

            // Restart Interval termination
            RestartIntervalStart = 0xFFD0,
            RestartIntervalEnd = 0xFFD7,

            // Other markers
            StartOfImage = 0xFFD8,
            EndOfImage = 0xFFD9,
            StartOfScan = 0xFFDA,
            QuantTableDef = 0xFFDB,
            NumberOfLinesDef = 0xFFDC,
            RestartIntervalDef = 0xFFDD,
            HierarchProgressionDef = 0xFFDE,
            ExpandRefComponents = 0xFFDF,

            // App segments
            App0 = 0xFFE0,
            App1 = 0xFFE1,
            App2 = 0xFFE2,
            App3 = 0xFFE3,
            App4 = 0xFFE4,
            App5 = 0xFFE5,
            App6 = 0xFFE6,
            App7 = 0xFFE7,
            App8 = 0xFFE8,
            App9 = 0xFFE9,
            App10 = 0xFFEA,
            App11 = 0xFFEB,
            App12 = 0xFFEC,
            App13 = 0xFFED,
            App14 = 0xFFEE,
            App15 = 0xFFEF,

            // Jpeg Extensions
            JpegExt0 = 0xFFF0,
            JpegExt1 = 0xFFF1,
            JpegExt2 = 0xFFF2,
            JpegExt3 = 0xFFF3,
            JpegExt4 = 0xFFF4,
            JpegExt5 = 0xFFF5,
            JpegExt6 = 0xFFF6,
            JpegExt7 = 0xFFF7,
            JpegExt8 = 0xFFF8,
            JpegExt9 = 0xFFF9,
            JpegExtA = 0xFFFA,
            JpegExtB = 0xFFFB,
            JpegExtC = 0xFFFC,
            JpegExtD = 0xFFFD,

            // Comments
            Comment = 0xFFFE,

            // Reserved
            ArithTemp = 0xFF01,
            ReservedStart = 0xFF02,
            ReservedEnd = 0xFFBF
        }

        /* Most of the following tags are not used in typical Exif files. They have been gathered from
         * http://www.sno.phy.queensu.ca/~phil/exiftool/TagNames/EXIF.html and include tags from many
         * variants and uses of TIFF files.
         */
        enum ExifTag
        {
            InteropIndex = 0x0001,
            InteropVersion = 0x0002,
            ProcessingSoftware = 0x000b,
            SubfileType = 0x00fe,
            OldSubfileType = 0x00ff,
            ImageWidth = 0x0100,
            ImageHeight = 0x0101,
            BitsPerSample = 0x0102,
            Compression = 0x0103,
            PhotometricInterpretation = 0x0106,
            Thresholding = 0x0107,
            CellWidth = 0x0108,
            CellLength = 0x0109,
            FillOrder = 0x010a,
            DocumentName = 0x010d,
            ImageDescription = 0x010e,
            Make = 0x010f,
            Model = 0x0110,
            StripOffsets = 0x0111, 
            Orientation = 0x0112,
            SamplesPerPixel = 0x0115,
            RowsPerStrip = 0x0116,
            StripByteCounts = 0x0117, 
            MinSampleValue = 0x0118,
            MaxSampleValue = 0x0119,
            XResolution = 0x011a,
            YResolution = 0x011b,
            PlanarConfiguration = 0x011c,
            PageName = 0x011d,
            XPosition = 0x011e,
            YPosition = 0x011f,
            FreeOffsets = 0x0120,
            FreeByteCounts = 0x0121,
            GrayResponseUnit = 0x0122,
            GrayResponseCurve = 0x0123,
            T4Options = 0x0124,
            T6Options = 0x0125,
            ResolutionUnit = 0x0128,
            PageNumber = 0x0129,
            ColorResponseUnit = 0x012c,
            TransferFunction = 0x012d,
            Software = 0x0131,
            ModifyDate = 0x0132,
            Artist = 0x013b,
            HostComputer = 0x013c,
            Predictor = 0x013d,
            WhitePoint = 0x013e,
            PrimaryChromaticities = 0x013f,
            ColorMap = 0x0140,
            HalftoneHints = 0x0141,
            TileWidth = 0x0142,
            TileLength = 0x0143,
            TileOffsets = 0x0144,
            TileByteCounts = 0x0145,
            BadFaxLines = 0x0146,
            CleanFaxData = 0x0147,
            ConsecutiveBadFaxLines = 0x0148,
            SubIFD = 0x014a, 
            InkSet = 0x014c,
            InkNames = 0x014d,
            NumberofInks = 0x014e,
            DotRange = 0x0150,
            TargetPrinter = 0x0151,
            ExtraSamples = 0x0152,
            SampleFormat = 0x0153,
            SMinSampleValue = 0x0154,
            SMaxSampleValue = 0x0155,
            TransferRange = 0x0156,
            ClipPath = 0x0157,
            XClipPathUnits = 0x0158,
            YClipPathUnits = 0x0159,
            Indexed = 0x015a,
            JPEGTables = 0x015b,
            OPIProxy = 0x015f,
            GlobalParametersIFD = 0x0190,
            ProfileType = 0x0191,
            FaxProfile = 0x0192,
            CodingMethods = 0x0193,
            VersionYear = 0x0194,
            ModeNumber = 0x0195,
            Decode = 0x01b1,
            DefaultImageColor = 0x01b2,
            T82Options = 0x01b3,
            JPEGTables_2 = 0x01b5,
            JPEGProc = 0x0200,
            ThumbnailOffset = 0x0201, 
            ThumbnailLength = 0x0202, 
            JPEGRestartInterval = 0x0203,
            JPEGLosslessPredictors = 0x0205,
            JPEGPointTransforms = 0x0206,
            JPEGQTables = 0x0207,
            JPEGDCTables = 0x0208,
            JPEGACTables = 0x0209,
            YCbCrCoefficients = 0x0211,
            YCbCrSubSampling = 0x0212,
            YCbCrPositioning = 0x0213,
            ReferenceBlackWhite = 0x0214,
            StripRowCounts = 0x022f,
            ApplicationNotes = 0x02bc,
            USPTOMiscellaneous = 0x03e7,
            RelatedImageFileFormat = 0x1000,
            RelatedImageWidth = 0x1001,
            RelatedImageHeight = 0x1002,
            Rating = 0x4746,
            XP_DIP_XML = 0x4747,
            StitchInfo = 0x4748,
            RatingPercent = 0x4749,
            ImageID = 0x800d,
            WangTag1 = 0x80a3,
            WangAnnotation = 0x80a4,
            WangTag3 = 0x80a5,
            WangTag4 = 0x80a6,
            Matteing = 0x80e3,
            DataType = 0x80e4,
            ImageDepth = 0x80e5,
            TileDepth = 0x80e6,
            Model2 = 0x827d,
            CFARepeatPatternDim = 0x828d,
            CFAPattern2 = 0x828e,
            BatteryLevel = 0x828f,
            KodakIFD = 0x8290,
            Copyright = 0x8298,
            ExposureTime = 0x829a,
            FNumber = 0x829d,
            MDFileTag = 0x82a5,
            MDScalePixel = 0x82a6,
            MDColorTable = 0x82a7,
            MDLabName = 0x82a8,
            MDSampleInfo = 0x82a9,
            MDPrepDate = 0x82aa,
            MDPrepTime = 0x82ab,
            MDFileUnits = 0x82ac,
            PixelScale = 0x830e,
            AdventScale = 0x8335,
            AdventRevision = 0x8336,
            UIC1Tag = 0x835c,
            UIC2Tag = 0x835d,
            UIC3Tag = 0x835e,
            UIC4Tag = 0x835f,
            IPTC_NAA = 0x83bb,
            IntergraphPacketData = 0x847e,
            IntergraphFlagRegisters = 0x847f,
            IntergraphMatrix = 0x8480,
            INGRReserved = 0x8481,
            ModelTiePoint = 0x8482,
            Site = 0x84e0,
            ColorSequence = 0x84e1,
            IT8Header = 0x84e2,
            RasterPadding = 0x84e3,
            BitsPerRunLength = 0x84e4,
            BitsPerExtendedRunLength = 0x84e5,
            ColorTable = 0x84e6,
            ImageColorIndicator = 0x84e7,
            BackgroundColorIndicator = 0x84e8,
            ImageColorValue = 0x84e9,
            BackgroundColorValue = 0x84ea,
            PixelIntensityRange = 0x84eb,
            TransparencyIndicator = 0x84ec,
            ColorCharacterization = 0x84ed,
            HCUsage = 0x84ee,
            TrapIndicator = 0x84ef,
            CMYKEquivalent = 0x84f0,
            SEMInfo = 0x8546,
            AFCP_IPTC = 0x8568,
            PixelMagicJBIGOptions = 0x85b8,
            ModelTransform = 0x85d8,
            WB_GRGBLevels = 0x8602,
            LeafData = 0x8606,
            PhotoshopSettings = 0x8649,
            ExifOffset = 0x8769,
            ICC_Profile = 0x8773,
            TIFF_FXExtensions = 0x877f,
            MultiProfiles = 0x8780,
            SharedData = 0x8781,
            T88Options = 0x8782,
            ImageLayer = 0x87ac,
            GeoTiffDirectory = 0x87af,
            GeoTiffDoubleParams = 0x87b0,
            GeoTiffAsciiParams = 0x87b1,
            ExposureProgram = 0x8822,
            SpectralSensitivity = 0x8824,
            GPSInfo = 0x8825,
            ISO = 0x8827,
            Opto_ElectricConvFactor = 0x8828,
            Interlace = 0x8829,
            TimeZoneOffset = 0x882a,
            SelfTimerMode = 0x882b,
            SensitivityType = 0x8830,
            StandardOutputSensitivity = 0x8831,
            RecommendedExposureIndex = 0x8832,
            ISOSpeed = 0x8833,
            ISOSpeedLatitudeyyy = 0x8834,
            ISOSpeedLatitudezzz = 0x8835,
            FaxRecvParams = 0x885c,
            FaxSubAddress = 0x885d,
            FaxRecvTime = 0x885e,
            LeafSubIFD = 0x888a,
            ExifVersion = 0x9000,
            DateTimeOriginal = 0x9003,
            CreateDate = 0x9004,
            ComponentsConfiguration = 0x9101,
            CompressedBitsPerPixel = 0x9102,
            ShutterSpeedValue = 0x9201,
            ApertureValue = 0x9202,
            BrightnessValue = 0x9203,
            ExposureCompensation = 0x9204,
            MaxApertureValue = 0x9205,
            SubjectDistance = 0x9206,
            MeteringMode = 0x9207,
            LightSource = 0x9208,
            Flash = 0x9209,
            FocalLength = 0x920a,
            FlashEnergy = 0x920b,
            SpatialFrequencyResponse = 0x920c,
            Noise = 0x920d,
            FocalPlaneXResolution = 0x920e,
            FocalPlaneYResolution = 0x920f,
            FocalPlaneResolutionUnit = 0x9210,
            ImageNumber = 0x9211,
            SecurityClassification = 0x9212,
            ImageHistory = 0x9213,
            SubjectArea = 0x9214,
            ExposureIndex = 0x9215,
            TIFF_EPStandardID = 0x9216,
            SensingMethod = 0x9217,
            CIP3DataFile = 0x923a,
            CIP3Sheet = 0x923b,
            CIP3Side = 0x923c,
            StoNits = 0x923f,
            MakerNote = 0x927c, 
            UserComment = 0x9286,
            SubSecTime = 0x9290,
            SubSecTimeOriginal = 0x9291,
            SubSecTimeDigitized = 0x9292,
            MSDocumentText = 0x932f,
            MSPropertySetStorage = 0x9330,
            MSDocumentTextPosition = 0x9331,
            ImageSourceData = 0x935c,
            XPTitle = 0x9c9b,
            XPComment = 0x9c9c,
            XPAuthor = 0x9c9d,
            XPKeywords = 0x9c9e,
            XPSubject = 0x9c9f,
            FlashpixVersion = 0xa000,
            ColorSpace = 0xa001,
            ExifImageWidth = 0xa002,
            ExifImageHeight = 0xa003,
            RelatedSoundFile = 0xa004,
            InteropOffset = 0xa005,
            FlashEnergy_2 = 0xa20b,
            SpatialFrequencyResponse_2 = 0xa20c,
            Noise_2 = 0xa20d,
            FocalPlaneXResolution_2 = 0xa20e,
            FocalPlaneYResolution_2 = 0xa20f,
            FocalPlaneResolutionUnit_2 = 0xa210,
            ImageNumber_2 = 0xa211,
            SecurityClassification_2 = 0xa212,
            ImageHistory_2 = 0xa213,
            SubjectLocation = 0xa214,
            ExposureIndex_2 = 0xa215,
            TIFF_EPStandardID_2 = 0xa216,
            SensingMethod_2 = 0xa217,
            FileSource = 0xa300,
            SceneType = 0xa301,
            CFAPattern = 0xa302,
            CustomRendered = 0xa401,
            ExposureMode = 0xa402,
            WhiteBalance = 0xa403,
            DigitalZoomRatio = 0xa404,
            FocalLengthIn35mmFormat = 0xa405,
            SceneCaptureType = 0xa406,
            GainControl = 0xa407,
            Contrast = 0xa408,
            Saturation = 0xa409,
            Sharpness = 0xa40a,
            DeviceSettingDescription = 0xa40b,
            SubjectDistanceRange = 0xa40c,
            ImageUniqueID = 0xa420,
            OwnerName = 0xa430,
            SerialNumber = 0xa431,
            LensInfo = 0xa432,
            LensMake = 0xa433,
            LensModel = 0xa434,
            LensSerialNumber = 0xa435,
            GDALMetadata = 0xa480,
            GDALNoData = 0xa481,
            Gamma = 0xa500,
            ExpandSoftware = 0xafc0,
            ExpandLens = 0xafc1,
            ExpandFilm = 0xafc2,
            ExpandFilterLens = 0xafc3,
            ExpandScanner = 0xafc4,
            ExpandFlashLamp = 0xafc5,
            PixelFormat = 0xbc01,
            Transformation = 0xbc02,
            Uncompressed = 0xbc03,
            ImageType = 0xbc04,
            ImageWidth_2 = 0xbc80,
            ImageHeight_2 = 0xbc81,
            WidthResolution = 0xbc82,
            HeightResolution = 0xbc83,
            ImageOffset = 0xbcc0,
            ImageByteCount = 0xbcc1,
            AlphaOffset = 0xbcc2,
            AlphaByteCount = 0xbcc3,
            ImageDataDiscard = 0xbcc4,
            AlphaDataDiscard = 0xbcc5,
            OceScanjobDesc = 0xc427,
            OceApplicationSelector = 0xc428,
            OceIDNumber = 0xc429,
            OceImageLogic = 0xc42a,
            Annotations = 0xc44f,
            PrintIM = 0xc4a5,
            OriginalFileName = 0xc573,
            USPTOOriginalContentType = 0xc580,
            DNGVersion = 0xc612,
            DNGBackwardVersion = 0xc613,
            UniqueCameraModel = 0xc614,
            LocalizedCameraModel = 0xc615,
            CFAPlaneColor = 0xc616,
            CFALayout = 0xc617,
            LinearizationTable = 0xc618,
            BlackLevelRepeatDim = 0xc619,
            BlackLevel = 0xc61a,
            BlackLevelDeltaH = 0xc61b,
            BlackLevelDeltaV = 0xc61c,
            WhiteLevel = 0xc61d,
            DefaultScale = 0xc61e,
            DefaultCropOrigin = 0xc61f,
            DefaultCropSize = 0xc620,
            ColorMatrix1 = 0xc621,
            ColorMatrix2 = 0xc622,
            CameraCalibration1 = 0xc623,
            CameraCalibration2 = 0xc624,
            ReductionMatrix1 = 0xc625,
            ReductionMatrix2 = 0xc626,
            AnalogBalance = 0xc627,
            AsShotNeutral = 0xc628,
            AsShotWhiteXY = 0xc629,
            BaselineExposure = 0xc62a,
            BaselineNoise = 0xc62b,
            BaselineSharpness = 0xc62c,
            BayerGreenSplit = 0xc62d,
            LinearResponseLimit = 0xc62e,
            CameraSerialNumber = 0xc62f,
            DNGLensInfo = 0xc630,
            ChromaBlurRadius = 0xc631,
            AntiAliasStrength = 0xc632,
            ShadowScale = 0xc633,
            SR2Private = 0xc634, 
            MakerNoteSafety = 0xc635,
            RawImageSegmentation = 0xc640,
            CalibrationIlluminant1 = 0xc65a,
            CalibrationIlluminant2 = 0xc65b,
            BestQualityScale = 0xc65c,
            RawDataUniqueID = 0xc65d,
            AliasLayerMetadata = 0xc660,
            OriginalRawFileName = 0xc68b,
            OriginalRawFileData = 0xc68c,
            ActiveArea = 0xc68d,
            MaskedAreas = 0xc68e,
            AsShotICCProfile = 0xc68f,
            AsShotPreProfileMatrix = 0xc690,
            CurrentICCProfile = 0xc691,
            CurrentPreProfileMatrix = 0xc692,
            ColorimetricReference = 0xc6bf,
            PanasonicTitle = 0xc6d2,
            PanasonicTitle2 = 0xc6d3,
            CameraCalibrationSig = 0xc6f3,
            ProfileCalibrationSig = 0xc6f4,
            ProfileIFD = 0xc6f5,
            AsShotProfileName = 0xc6f6,
            NoiseReductionApplied = 0xc6f7,
            ProfileName = 0xc6f8,
            ProfileHueSatMapDims = 0xc6f9,
            ProfileHueSatMapData1 = 0xc6fa,
            ProfileHueSatMapData2 = 0xc6fb,
            ProfileToneCurve = 0xc6fc,
            ProfileEmbedPolicy = 0xc6fd,
            ProfileCopyright = 0xc6fe,
            ForwardMatrix1 = 0xc714,
            ForwardMatrix2 = 0xc715,
            PreviewApplicationName = 0xc716,
            PreviewApplicationVersion = 0xc717,
            PreviewSettingsName = 0xc718,
            PreviewSettingsDigest = 0xc719,
            PreviewColorSpace = 0xc71a,
            PreviewDateTime = 0xc71b,
            RawImageDigest = 0xc71c,
            OriginalRawFileDigest = 0xc71d,
            SubTileBlockSize = 0xc71e,
            RowInterleaveFactor = 0xc71f,
            ProfileLookTableDims = 0xc725,
            ProfileLookTableData = 0xc726,
            OpcodeList1 = 0xc740,
            OpcodeList2 = 0xc741,
            OpcodeList3 = 0xc74e,
            NoiseProfile = 0xc761,
            TimeCodes = 0xc763,
            FrameRate = 0xc764,
            TStop = 0xc772,
            ReelName = 0xc789,
            OriginalDefaultFinalSize = 0xc791,
            OriginalBestQualitySize = 0xc792,
            OriginalDefaultCropSize = 0xc793,
            CameraLabel = 0xc7a1,
            ProfileHueSatMapEncoding = 0xc7a3,
            ProfileLookTableEncoding = 0xc7a4,
            BaselineExposureOffset = 0xc7a5,
            DefaultBlackRender = 0xc7a6,
            NewRawImageDigest = 0xc7a7,
            RawToPreviewGain = 0xc7a8,
            DefaultUserCrop = 0xc7b5,
            Padding = 0xea1c,
            OffsetSchema = 0xea1d,
            OwnerName_2 = 0xfde8,
            SerialNumber_2 = 0xfde9,
            Lens = 0xfdea,
            KDC_IFD = 0xfe00,
            RawFile = 0xfe4c,
            Converter = 0xfe4d,
            WhiteBalance_2 = 0xfe4e,
            Exposure = 0xfe51,
            Shadows = 0xfe52,
            Brightness = 0xfe53,
            Contrast_2 = 0xfe54,
            Saturation_2 = 0xfe55,
            Sharpness_2 = 0xfe56,
            Smoothness = 0xfe57,
            MoireFilter = 0xfe58
        }
    }
}
