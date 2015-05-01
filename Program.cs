using System;
using System.Collections.Generic;
using System.Text;
using FileMeta;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;

namespace FMProbe
{
    static class Program
    {
        static readonly string sSyntax = 
@"Syntax: FmProbe [-wps] [-v<n>] <filename>
    -v      Verbose
    -v[0-4] Verbosity level
    -wps    Option to use Windows Property System to enumerate metadata.
    Filename may include wildcards.";
        static readonly char[] sWildcards = new char[] { '*', '?' };

        static int mVerbosity = 0;
        static public int Verbosity
        {
            get { return mVerbosity; }
        }

        static void FmProbe(Stream file, TextWriter output)
        {
            FileTypeId typeId = FileType.GetFileType(file);
            output.WriteLine("FileType: {0}", typeId);

            try
            {
                switch (typeId)
                {
                    case FileTypeId.mp3:
                        {
                            Mp3Probe mp3Probe = new Mp3Probe(file, output);
                            mp3Probe.Probe();
                        }
                        break;

                    case FileTypeId.jpeg:
                        {
                            ExifProbe probe = new ExifProbe(file, output);
                            probe.Probe();
                        }
                        break;

                    case FileTypeId.midi:
                        {
                            MidiProbe probe = new MidiProbe(file, output);
                            probe.Probe();
                        }
                        break;

                    case FileTypeId.mpeg4:
                        {
                            Mp4Probe mp4Probe = new Mp4Probe(file, output);
                            mp4Probe.Probe();
                        }
                        break;

                    case FileTypeId.unknown:
                        {
                            UnknownProbe probe = new UnknownProbe(file, output);
                            probe.Probe();
                        }
                        break;
                }
            }
            catch (Exception err)
            {
#if DEBUG
                output.WriteLine(err.ToString());
#else
                output.WriteLine(err.Message);
#endif
            }
            output.WriteLine();
        }

        static void FmProbe(string filename, bool useWps, TextWriter output)
        {
            output.WriteLine("------------------------------------");
            output.WriteLine("File: {0}", filename);
            if (useWps)
            {
                WpsProbe probe = new WpsProbe(filename, output);
                probe.Probe();
            }
            else
            {
                FmProbe(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read), output);
            }
        }

        static void Main(string[] args)
        {
            bool useWps = false;

            try
            {
                foreach (string arg in args)
                {
                    if (arg[0] == '-')
                    {
                        if (arg.StartsWith("-v"))
                        {
                            mVerbosity = (arg.Length > 2) ? int.Parse(arg.Substring(2)) : 1;
                        }
                        else if (arg.Equals("-wps", StringComparison.Ordinal)) // Use Windows Property System
                        {
                            useWps = true;
                        }
                        else
                        {
                            throw new ArgumentException(string.Format("Unknown command-line argument '{0}'", arg));
                        }
                    }

                    else
                    {
                        string filenamePart = Path.GetFileName(arg);
                        if (filenamePart.IndexOfAny(sWildcards) >= 0)
                        {
                            foreach (string filename in Directory.EnumerateFiles(Path.GetDirectoryName(arg), filenamePart))
                            {
                                FmProbe(filename, useWps, Console.Out);
                            }
                        }
                        else
                        {
                            FmProbe(arg, useWps, Console.Out);
                        }
                    }
                }

            }
            catch (Exception err)
            {
                Console.WriteLine();
#if DEBUG
                Console.WriteLine(err.ToString());
#else
                Console.WriteLine(err.Message);
#endif
            }

            if (ConsoleHelper.IsSoleConsoleOwner)
            {
                Console.Write("Press any key to exit.");
                Console.ReadKey(true);
            }
        }
    }

    static class ConsoleHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList(
            uint[] ProcessList,
            uint ProcessCount
            );

        public static bool IsSoleConsoleOwner
        {
            get
            {
                uint[] procIds = new uint[4];
                uint count = GetConsoleProcessList(procIds, (uint)procIds.Length);
                return count <= 1;
            }
        }
    }

    static class ExtendTextWriter
    {
        public static void Dump(this TextWriter writer, int displayOffset, byte[] buffer, int offset, int length)
        {
            int bufEnd = offset+length;

            // For each line
            for (int ln=0; ln < length; ln += 16)
            {
                // Write the offset in hex
                writer.Write("{0:x4}: ", displayOffset + ln);

                int lnCt = Math.Min(length-ln, 16);

                // Write the bytes in hex
                for (int i=0; i<lnCt; ++i)
                {
                    writer.Write("{0:x2} ", buffer[offset + ln + i]);

                    // add extra space after 8th byte
                    if (i == 7) writer.Write(" ");
                }

                // If at least one preceding line, pad out so that text column aligns
                if (ln > 0 && lnCt < 16)
                {
                    writer.Write(new String(' ', (16-lnCt)*3 + ((lnCt<8)?1:0)));
                }

                // Pad with two extra spaces
                writer.Write("  ");

                // Write out the characters that are in ASCII range
                for (int i=0; i<lnCt; ++i)
                {
                    char c = (char)buffer[offset + ln + i];
                    writer.Write((c >= ' ' && c < '\x7F') ? c : '\xB7');

                    // Extra space on byte 8
                    if (i == 7) writer.Write(" ");
                }

                writer.WriteLine();
            }
        }
    }

    static class ExtendBinaryReader
    {
        public static UInt16 ReadUInt16BE(this BinaryReader reader)
        {
            UInt16 value = reader.ReadUInt16();
            return (UInt16)(((uint)value << 8) | ((uint)value >> 8));
        }

        public static Int16 ReadInt16BE(this BinaryReader reader)
        {
            return (Int16)ReadUInt16BE(reader);
        }

        public static UInt16 ReadUInt16(this BinaryReader reader, bool bigEndian)
        {
            return (bigEndian) ? ReadUInt16BE(reader) : reader.ReadUInt16();
        }

        public static Int16 ReadInt16(this BinaryReader reader, bool bigEndian)
        {
            return (bigEndian) ? (Int16)ReadUInt16BE(reader) : reader.ReadInt16();
        }

        public static UInt32 ReadUInt32BE(this BinaryReader reader)
        {
            UInt32 value = reader.ReadUInt32();
            return (UInt16)(
                (value << 24)
                | ((value & (uint)0x0000FF00) << 8)
                | ((value & (uint)0x00FF0000) >> 8)
                | (value >> 24));
        }

        public static Int32 ReadInt32BE(this BinaryReader reader)
        {
            return (Int32)ReadUInt32BE(reader);
        }

        public static UInt32 ReadUInt32(this BinaryReader reader, bool bigEndian)
        {
            return (bigEndian) ? ReadUInt32BE(reader) : reader.ReadUInt32();
        }

        public static Int32 ReadInt32(this BinaryReader reader, bool bigEndian)
        {
            return (bigEndian) ? (Int32)ReadUInt32BE(reader) : reader.ReadInt32();
        }

    }

    class UnknownProbe
    {
        const int cDumpCount = 256;

        Stream mFile;
        TextWriter mOut;

        public UnknownProbe(Stream file, TextWriter output)
        {
            mFile = file;
            mOut = output;
        }

        public void Probe()
        {
            byte[] buf = new byte[cDumpCount];
            mFile.Position = 0;
            int count = mFile.Read(buf, 0, cDumpCount);
            mOut.Dump(0, buf, 0, count);
        }
    }
}
