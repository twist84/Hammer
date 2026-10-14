using System;
using System.IO;
using System.Text;

namespace Hammer
{
    // write_text: write lines as text, preserving an existing file's byte conventions
    // (BOM, CRLF vs LF, trailing newline). new files use the supplied defaults.
    // explicit byte/newline control — no auto-translating writer.
    static class TextFile
    {
        public static void WriteText(string path, string[] lines,
            bool defaultBom, bool defaultCrlf, bool defaultTrailingNewline)
        {
            bool? bom = null, crlf = null, trailingNewline = null;
            if (File.Exists(path))
            {
                byte[] raw = File.ReadAllBytes(path);
                bom = HasBom(raw);
                crlf = !ContainsByte(raw, (byte)'\n') || ContainsSequence(raw, (byte)'\r', (byte)'\n');
                trailingNewline = raw.Length > 0 && raw[raw.Length - 1] == (byte)'\n';
            }
            if (bom == null) bom = defaultBom;
            if (crlf == null) crlf = defaultCrlf;
            if (trailingNewline == null) trailingNewline = defaultTrailingNewline;

            string newline = crlf.Value ? "\r\n" : "\n";
            // normalize embedded newlines (e.g. the multi-line `<!-- ... -->` comment)
            string text = string.Join(newline, lines);
            text = text.Replace("\r\n", "\n").Replace("\n", newline);
            if (trailingNewline.Value)
                text += newline;

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var enc = new UTF8Encoding(bom.Value);
            File.WriteAllText(path, text, enc);
        }

        static bool HasBom(byte[] raw)
            => raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;

        static bool ContainsByte(byte[] raw, byte value)
        {
            foreach (byte b in raw)
                if (b == value) return true;
            return false;
        }

        static bool ContainsSequence(byte[] raw, byte a, byte b)
        {
            for (int i = 0; i + 1 < raw.Length; i++)
                if (raw[i] == a && raw[i + 1] == b) return true;
            return false;
        }
    }
}
