using System;
using System.Collections.Generic;
using System.Text;
using FileMeta;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;

namespace FMProbe
{
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

    static class Program
    {
        static readonly string sSyntax = "Syntax: FmProbe <filename>";

        static void FmProbe(Stream file, TextWriter output)
        {
            FileTypeId typeId = FileType.GetFileType(file);
            output.WriteLine("FileType: {0}", typeId);
            
            switch(typeId)
            {
                case FileTypeId.mp3:
                    Mp3Probe mp3Probe = new Mp3Probe(file, output);
                    mp3Probe.Probe();
                    break;
            }
        }

        static void FmProbe(string filename, TextWriter output)
        {
            FmProbe(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read), output);
        }

        static void Main(string[] args)
        {
            try
            {
                if (args.Length != 1)
                {
                    throw new ApplicationException(sSyntax);
                }

                FmProbe(args[0], Console.Out);
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
                Console.WriteLine();
                Console.Write("Press any key to exit.");
                Console.ReadKey(true);
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
}
