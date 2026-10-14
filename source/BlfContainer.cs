using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hammer
{
    // reverse blf container reader: big-endian chunk headers, payloads swapped per
    // the start-of-file byte order mark. ported from mapinfo_to_xml.py (`BlfFile`,
    // `Chunk`) with the layout constants from tools/xml_to_mapinfo.py.
    sealed class BlfFile
    {
        sealed class ChunkInfo
        {
            public int Size;
            public int Major;
            public int Minor;
            public int Offset;
        }

        public readonly string Path;
        readonly byte[] data;
        public readonly bool MustByteSwap;
        readonly Dictionary<uint, ChunkInfo> chunks = new Dictionary<uint, ChunkInfo>();

        public BlfFile(string path)
        {
            Path = path;
            data = File.ReadAllBytes(path);

            if (data.Length < Blf.StartOfFileSize)
                throw new ToolError(path + ": file is smaller than a start of file chunk");

            uint chunkType = ReadU32BE(data, 0);
            int chunkSize = (int)ReadU32BE(data, 4);
            int major = ReadU16BE(data, 8);
            int minor = ReadU16BE(data, 10);
            if (chunkType != Blf.Fourcc(Blf.StartOfFileType))
                throw new ToolError(path + ": not a blf file (bad start of file magic)");
            if (chunkSize != Blf.StartOfFileSize)
                throw new ToolError(
                    path + ": start of file chunk is " + chunkSize + " bytes, expected " + Blf.StartOfFileSize);

            // network_blf_verify_start_of_file: the byte order mark is read natively
            // (little-endian here); only a byteswapped 0xFFFE means the payload needs
            // swapping. the stored mark is FF FE bytes => LE word 0xFEFF != 0xFFFE.
            uint byteOrderMark = (uint)(data[0x0C] | (data[0x0D] << 8));
            MustByteSwap = byteOrderMark != 0xFFFE;

            int offset = Blf.StartOfFileSize;
            while (offset + Blf.HeaderSize <= data.Length)
            {
                uint type = ReadU32BE(data, offset);
                long size = ReadU32BE(data, offset + 4);
                int maj = ReadU16BE(data, offset + 8);
                int min = ReadU16BE(data, offset + 10);
                // the size field is untrusted: reject a value that cannot advance the
                // cursor (guards the zero-size infinite loop) or that overruns the file
                // (guards negative-offset / out-of-bounds reads via the (int) cast).
                if (size < Blf.HeaderSize || (long)offset + size > data.Length)
                    throw new ToolError(path + ": chunk at offset " + offset + " has invalid size " + size);
                if (!chunks.ContainsKey(type))
                    chunks[type] = new ChunkInfo { Size = (int)size, Major = maj, Minor = min, Offset = offset };
                offset += (int)size;
            }
            if (offset != data.Length)
                Diag.Warn(path + ": " + (data.Length - offset) + " trailing byte(s) after the last chunk");
            if (!chunks.ContainsKey(Blf.Fourcc(Blf.EndOfFileType)))
                throw new ToolError(path + ": no end of file chunk");
        }

        public Chunk RequireChunk(byte[] chunkTypeBytes, int chunkSize, int major, int minor, string description)
        {
            uint chunkType = Blf.Fourcc(chunkTypeBytes);
            if (!chunks.TryGetValue(chunkType, out var info))
                throw new ToolError(Path + ": no " + description + " chunk");
            if (info.Size != chunkSize)
                throw new ToolError(
                    Path + ": " + description + " chunk is " + info.Size + " bytes, expected " + chunkSize);
            if (info.Major != major || info.Minor != minor)
                Diag.Warn(Path + ": " + description + " chunk version " + info.Major + "." + info.Minor
                    + ", expected " + major + "." + minor);
            return new Chunk(data, info.Offset, MustByteSwap);
        }

        static uint ReadU32BE(byte[] b, int o)
            => ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
        static ushort ReadU16BE(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);

        // typed accessors over one chunk payload, mirroring the reader's byte swapping.
        public sealed class Chunk
        {
            readonly byte[] data;
            readonly int baseOffset;
            readonly bool mustByteSwap;

            public Chunk(byte[] data, int offset, bool mustByteSwap)
            {
                this.data = data;
                this.baseOffset = offset;
                this.mustByteSwap = mustByteSwap;
            }

            public byte U8(int offset) => data[baseOffset + offset];

            public ushort U16(int offset)
            {
                int o = baseOffset + offset;
                if (mustByteSwap)
                    return (ushort)((data[o] << 8) | data[o + 1]);
                return (ushort)(data[o] | (data[o + 1] << 8));
            }

            public uint U32(int offset)
            {
                int o = baseOffset + offset;
                if (mustByteSwap)
                    return ((uint)data[o] << 24) | ((uint)data[o + 1] << 16) | ((uint)data[o + 2] << 8) | data[o + 3];
                return ((uint)data[o + 3] << 24) | ((uint)data[o + 2] << 16) | ((uint)data[o + 1] << 8) | data[o];
            }

            public int I32(int offset) => (int)U32(offset);

            // a fixed size wide string, truncated at its first terminator.
            public string WString(int offset, int wcharCount)
            {
                int o = baseOffset + offset;
                Encoding enc = mustByteSwap ? Text.Utf16BeLossy : Encoding.Unicode;
                string decoded = enc.GetString(data, o, wcharCount * 2);
                int nul = decoded.IndexOf('\0');
                return nul < 0 ? decoded : decoded.Substring(0, nul);
            }

            public string CString(int offset, int length)
            {
                int o = baseOffset + offset;
                string decoded = Text.AsciiLossy.GetString(data, o, length);
                int nul = decoded.IndexOf('\0');
                return nul < 0 ? decoded : decoded.Substring(0, nul);
            }

            public string[] WStringArray(int offset, int entryWcharCount, int entryCount, int entryStrideBytes)
            {
                var result = new string[entryCount];
                for (int i = 0; i < entryCount; i++)
                    result[i] = WString(offset + i * entryStrideBytes, entryWcharCount);
                return result;
            }
        }
    }
}
