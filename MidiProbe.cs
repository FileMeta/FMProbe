using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using FileMeta;
using System.Diagnostics;

/* References
 * http://www.fileformat.info/format/midi/corion.htm
 * http://www.ccarh.org/courses/253/handout/smf/
 * http://www.midi.org/techspecs/rp26.php
 * http://www.midi.org/techspecs/rp17.php
 * http://cs.fit.edu/~ryan/cse4051/projects/midi/midi.html
 * http://www.somascape.org/midi/tech/mfile.html
 */

namespace FMProbe
{
    class MidiProbe
    {
        static readonly byte[] sMidiHeader = { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0};
        static readonly byte[] sTrackHeader = { (byte)'M', (byte)'T', (byte)'r', (byte)'k' };

        Stream mFile;
        TextWriter mOut;

        public MidiProbe(Stream file, TextWriter output)
        {
            mFile = file;
            mOut = output;
        }

        public void Probe()
        {
            mFile.Position = 0;
            // Midi files are divided into chunks, each with a 8-byte header and a body
            for (;;)    // Read all chunks
            {
                // Read the Chunk header
                byte[] header = new byte[8];
                int headerLen = mFile.Read(header, 0, 8);
                if (headerLen == 0) break;
                if (headerLen < 8)
                {
                    mOut.WriteLine("Invalid MIDI file. Chunk header is truncated.");
                    return;
                }
                
                // Get the chunk name and length
                string chunkName = Encoding.ASCII.GetString(header, 0, 4);
                int chunkLen = ByteBuf.Int32FromBytesBE(header, 4);

                switch(chunkName)
                {
                    case "MThd":    // MIDI Header
                        ProbeMidiHeader(chunkLen);
                        break;

                    case "MTrk":    // MIDI Track
                        ProbeMidiTrack(chunkLen);
                        break;

                    default:
                        mOut.WriteLine("-- Unknown chunk: type={0} len={1}", chunkName, chunkLen);
                        mFile.Position += chunkLen;
                        break;
                }
            }
        }

        private void ProbeMidiHeader(int chunkLen)
        {
            mOut.WriteLine("-- Header");
            if (chunkLen != 6)
            {
                throw new ApplicationException(string.Format("Invalid MIDI file. Header length {0}, expected 6.", chunkLen));
            }
            byte[] chunkBuf = new byte[6];
            if (6 != mFile.Read(chunkBuf, 0, 6))
            {
                throw new ApplicationException("MIDI file truncated");
            }

            int midiFileType = (int)ByteBuf.UInt16FromBytesBE(chunkBuf, 0);
            mOut.WriteLine("Midi file type: {0}", midiFileType);
            if (midiFileType > 2)
            {
                mOut.WriteLine("Invalid MIDI file type. Expecting 0, 1, or 2.");
                return;
            }
            int trackCount = (int)ByteBuf.UInt16FromBytesBE(chunkBuf, 2);
            mOut.WriteLine("Tracks: {0}", trackCount);

            // Get delta time format
            int ticksPerBeat = 0;
            int framesPerSec = 0;
            int ticksPerFrame = 0;
            if ((chunkBuf[4] & 0x80) == 0) // ticks per beat
            {
                ticksPerBeat = (int)ByteBuf.Int16FromBytesBE(chunkBuf, 4);
                mOut.WriteLine("Ticks per beat: {0}", ticksPerBeat);
            }
            else
            {
                framesPerSec = (int)(-(sbyte)chunkBuf[4]);
                ticksPerFrame = (int)(uint)chunkBuf[5];
                mOut.WriteLine("SMPTE frames per sec", framesPerSec);
                mOut.WriteLine("SMPTE ticks per frame", ticksPerFrame);
            }
        }

        void ProbeMidiTrack(int chunkLen)
        {
            int noteCount = 0;

            mOut.WriteLine("-- Track");
            mOut.WriteLine("Chunk length: {0}", chunkLen);

            // Read in the chunk
            byte[] chunkBuf = new byte[chunkLen];
            if (mFile.Read(chunkBuf, 0, chunkLen) < chunkLen)
            {
                throw new ApplicationException("Truncated MIDI chunk.");
            }

            // Read the events
            int offset = 0;
            byte status = 0;
            while (offset < chunkLen)
            {
                int deltaTime = ReadVlInt(chunkBuf, ref offset);

                // If high bit set, this is a status byte. Otherwise it inherits the status from the previous item.
                if (chunkBuf[offset] >= (byte)0x80)
                {
                    status = chunkBuf[offset++];
                }
                switch (status & 0xF0)
                {
                    case 0x90: // Note on
                        ++noteCount;
                        offset += 2;
                        break;

                    case 0x80: // Note off
                    case 0xa0: // Key aftertouch
                    case 0xe0: // Pitch bend change
                        offset += 2;    // Two data bytes
                        break;

                    case 0xb0: // Control change
                        switch (chunkBuf[offset])
                        {
                            case 0x00:
                                mOut.WriteLine("Bank Select: {0}", chunkBuf[offset + 1]);
                                break;

                            case 0x78:
                                ReportNoteCount(ref noteCount);
                                mOut.WriteLine("AllSoundOff: channel={0}", status & 0x0F);
                                break;
                            case 0x79:
                                ReportNoteCount(ref noteCount);
                                mOut.WriteLine("AllNotesOff: channel={0}", status & 0x0F);
                                break;
                        }
                        offset += 2;
                        break;

                    case 0xc0: // Program change
                        ReportNoteCount(ref noteCount);
                        mOut.WriteLine("Program Change: channel={0} program={1}", status & 0x0F, chunkBuf[offset + 1]);
                        offset += 1;
                        break;

                    case 0xd0: // Channel aftertouch
                        offset += 1;    // One data byte
                        break;

                    case 0xf0: // System Common Message
                        ReportNoteCount(ref noteCount);
                        switch (status & 0x0F)
                        {
                            case 0x00:  // System exclusive message
                            case 0x07:
                                // Skip to the F7 end-of-message marker
                                while (offset < chunkBuf.Length && chunkBuf[offset] != (byte)0xF7) ++offset;
                                ++offset;
                                break;

                            case 0x01: // MIDI Time Code Quarter Frame
                            case 0x03: // Song Selct
                                offset += 1; // one data byte
                                break;

                            case 0x02: // Song position pointer
                                offset += 2; // two data bytes
                                break;

                            case 0x0F: // Meta event
                                if (chunkBuf[offset] == 0x2F)   // End of track
                                {
                                    mOut.WriteLine("End of Track");
                                    return;
                                }
                                ProbeMetaEvent(chunkBuf, ref offset);
                                break;

                            default:
                                // No data bytes
                                break;                                   
                        }
                        break;

                    default:
                        Debug.WriteLine("Unexpected");
                        break;
                }
            }
            ReportNoteCount(ref noteCount);
        }

        void ReportNoteCount(ref int noteCount)
        {
            if (noteCount > 0)
            {
                mOut.WriteLine("{0} Notes", noteCount);
                noteCount = 0;
            }
        }

        void ProbeMetaEvent(byte[] chunkBuf, ref int offset)
        {
            byte type = chunkBuf[offset++];
            int len = ReadVlInt(chunkBuf, ref offset);
            switch (type)
            {
                case 0x00:  // Sequence number
                    if (len == 2)
                    {
                        uint num = ByteBuf.UInt16FromBytesBE(chunkBuf, offset);
                        mOut.WriteLine("Sequence Number: {0}", num);
                    }
                    break;

                case 0x01:
                    mOut.WriteLine("Text: {0}", Encoding.ASCII.GetString(chunkBuf, offset, len));
                    break;

                case 0x02:
                    mOut.WriteLine("Copyright: {0}", Encoding.ASCII.GetString(chunkBuf, offset, len));
                    break;

                case 0x03:
                    mOut.WriteLine("Track Name: {0}", Encoding.ASCII.GetString(chunkBuf, offset, len));
                    break;

                case 0x04:
                    mOut.WriteLine("Instrument Name: {0}", Encoding.ASCII.GetString(chunkBuf, offset, len));
                    break;

                case 0x05:
                    mOut.WriteLine("Lyric: {0}", Encoding.ASCII.GetString(chunkBuf, offset, len));
                    break;

                case 0x06:
                    mOut.WriteLine("Marker: {0}", Encoding.ASCII.GetString(chunkBuf, offset, len));
                    break;

                case 0x20:
                    mOut.WriteLine("Midi Channel Prefix: Channel={0}", chunkBuf[offset]);
                    break;

                case 0x21:
                    mOut.WriteLine("Midi Port: port={0}", chunkBuf[offset]);
                    break;

                case 0x2F:
                    // Caller should catch this one.
                    throw new ApplicationException("Unexpected end of track.");

                case 0x51: // Tempo change
                    if (len == 3)
                    {
                        int tempo = (((int)chunkBuf[offset]) << 16) | (((int)chunkBuf[offset+1]) << 8) | ((int)chunkBuf[offset+2]);
                        mOut.WriteLine("Tempo (microseconds/beat): {0}", tempo);
                    }
                    break;

                case 0x54: // SMPTE Offset
                    if (len == 5)
                    {
                        mOut.WriteLine("SMPTE Offset: {0:d2}:{1:d2}:{2:d2}.{3:d3} +{4}",
                            chunkBuf[offset], chunkBuf[offset+1], chunkBuf[offset+2], chunkBuf[offset+3], chunkBuf[offset+4]);
                    }
                    break;

                case 0x58: // Time Signature
                    mOut.WriteLine("Time Signature");
                    mOut.Dump(0, chunkBuf, offset, 4);
                    break;

                case 0x59: // Key Signature
                    mOut.WriteLine("Key Signature");
                    mOut.Dump(0, chunkBuf, offset, 2);
                    break;

                case 0x7F:
                    mOut.WriteLine("Sequencer specific event");
                    mOut.Dump(0, chunkBuf, offset, len);
                    break;

                default:
                    mOut.WriteLine("Meta event: type=0x{0:x2} len={1}", type, len);
                    mOut.Dump(0, chunkBuf, offset, len);
                    break;

            }
            offset += len;
        }

        static int ReadVlInt(byte[] buf, ref int offset)
        {
            int val = 0;
            for (; ; )
            {
                byte b = buf[offset++];
                val = (val << 7) | (int)(b & (byte)0x7F);
                if ((b & (byte)0x80) == 0) break;
            }
            return val;
        }
    }
}
