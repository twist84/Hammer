using System;
using System.IO;

namespace Hammer
{
    // low-level forward byte writers and signature handling (start-of-file / end-of-file
    // container assembly). the `levl`/`cmpn` payload builders live in ForwardConverter.
    static class BlfWriter
    {
        public static byte[] PackHeader(byte[] chunkType, int chunkSize, int major, int minor)
        {
            var h = new byte[Blf.HeaderSize];
            Array.Copy(chunkType, h, 4);
            h[4] = (byte)(chunkSize >> 24);
            h[5] = (byte)(chunkSize >> 16);
            h[6] = (byte)(chunkSize >> 8);
            h[7] = (byte)chunkSize;
            h[8] = (byte)(major >> 8);
            h[9] = (byte)major;
            h[10] = (byte)(minor >> 8);
            h[11] = (byte)minor;
            return h;
        }

        public static byte[] MakeStartOfFile()
        {
            var chunk = new byte[Blf.StartOfFileSize];
            Array.Copy(PackHeader(Blf.StartOfFileType, Blf.StartOfFileSize,
                Blf.StartOfFileMajor, Blf.StartOfFileMinor), chunk, Blf.HeaderSize);
            Array.Copy(Blf.ByteOrderMarkBytes, 0, chunk, Blf.HeaderSize, 2);
            // file_type and alignment padding stay zero
            return chunk;
        }

        public static byte[] MakeEndOfFileWithRsa(int totalFileSize, byte[] signature)
        {
            var chunk = new byte[Blf.EndOfFileWithRsaSize];
            Array.Copy(PackHeader(Blf.EndOfFileType, Blf.EndOfFileWithRsaSize,
                Blf.EndOfFileMajor, Blf.EndOfFileMinor), chunk, Blf.HeaderSize);
            chunk[12] = (byte)(totalFileSize >> 24);
            chunk[13] = (byte)(totalFileSize >> 16);
            chunk[14] = (byte)(totalFileSize >> 8);
            chunk[15] = (byte)totalFileSize;
            chunk[Blf.HeaderSize + 4] = (byte)Blf.RsaAuthenticationType;
            Array.Copy(signature, 0, chunk, Blf.HeaderSize + 5, Blf.EndOfFileWithRsaSignatureSize);
            return chunk;
        }

        public static byte[] Assemble(byte[] payload, byte[] signature)
        {
            int totalFileSize = Blf.StartOfFileSize + payload.Length;
            var result = new byte[totalFileSize + Blf.EndOfFileWithRsaSize];
            Array.Copy(MakeStartOfFile(), 0, result, 0, Blf.StartOfFileSize);
            Array.Copy(payload, 0, result, Blf.StartOfFileSize, payload.Length);
            Array.Copy(MakeEndOfFileWithRsa(totalFileSize, signature), 0,
                result, Blf.StartOfFileSize + payload.Length, Blf.EndOfFileWithRsaSize);
            return result;
        }

        public static byte[] ReadSignature(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < Blf.EndOfFileWithRsaSize)
                throw new ToolError("file is smaller than an rsa end-of-file chunk");
            var hdr = PackHeader(Blf.EndOfFileType, Blf.EndOfFileWithRsaSize,
                Blf.EndOfFileMajor, Blf.EndOfFileMinor);
            for (int i = 0; i < Blf.HeaderSize; i++)
                if (data[data.Length - Blf.EndOfFileWithRsaSize + i] != hdr[i])
                    throw new ToolError("file does not end with an rsa end-of-file chunk");
            var signature = new byte[Blf.EndOfFileWithRsaSignatureSize];
            Array.Copy(data, data.Length - Blf.EndOfFileWithRsaSignatureSize, signature, 0,
                Blf.EndOfFileWithRsaSignatureSize);
            return signature;
        }

        public static byte[] ResolveSignature(string signatureProvider, string copySignatureFrom, string outputPath)
        {
            var zeros = new byte[Blf.EndOfFileWithRsaSignatureSize];
            if (signatureProvider == "none")
                return zeros;
            if (copySignatureFrom != null)
                return ReadSignature(copySignatureFrom);
            // the secrets key file is not part of this repository, so real signatures
            // cannot be produced; keep the signature of the file being regenerated
            // (byte-identical output) and write zeros otherwise.
            if (File.Exists(outputPath))
            {
                try
                {
                    return ReadSignature(outputPath);
                }
                catch (IOException error)
                {
                    Diag.Warn("could not reuse the signature of " + outputPath + " (" + error.Message + "); writing zeros");
                }
                catch (UnauthorizedAccessException error)
                {
                    Diag.Warn("could not reuse the signature of " + outputPath + " (" + error.Message + "); writing zeros");
                }
                catch (ToolError error)
                {
                    Diag.Warn("could not reuse the signature of " + outputPath + " (" + error.Message + "); writing zeros");
                }
            }
            return zeros;
        }

        public static void WriteOutput(string outputPath, byte[] contents)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(outputPath, contents);
        }

        // encode per-language strings as a fixed size big-endian utf-16 array; a string
        // that exactly fills its slot is stored without a terminator.
        public static byte[] EncodeWcharArray(string[] values, int elementLength, int count, string clampName)
        {
            var result = new byte[count * elementLength * 2];
            int pos = 0;
            for (int languageIndex = 0; languageIndex < count; languageIndex++)
            {
                string value = languageIndex < values.Length ? values[languageIndex] : "";
                if (value.Length > elementLength)
                {
                    Diag.Warn(clampName + " string for language " + languageIndex + " is " + value.Length
                        + " characters, clamped to " + elementLength);
                    value = value.Substring(0, elementLength);
                }
                var encoded = System.Text.Encoding.BigEndianUnicode.GetBytes(value);
                Array.Copy(encoded, 0, result, pos, encoded.Length);
                // remaining bytes of the slot stay zero (padding)
                pos += elementLength * 2;
            }
            return result;
        }

        public static byte[] EncodeAsciiField(string value, int length, string fieldName, string xmlPath)
        {
            var full = System.Text.Encoding.ASCII.GetBytes(value);
            if (full.Length > length - 1)
            {
                Diag.Warn(xmlPath + ": <" + fieldName + "> value " + Text.Repr(value)
                    + " is longer than " + (length - 1) + " characters, clamped");
            }
            var result = new byte[length];
            int copy = Math.Min(full.Length, length - 1);
            Array.Copy(full, 0, result, 0, copy);
            // rest stays zero (including the terminator)
            return result;
        }
    }
}
