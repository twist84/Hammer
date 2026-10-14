using System;
using System.Text;

namespace Hammer
{
    // text helpers whose exact behavior must match python's stdlib.
    static class Text
    {
        // big-endian utf-16 with U+FFFD replacement, matching python
        // `bytes.decode("utf-16-be", "replace")`.
        public static readonly Encoding Utf16BeLossy = MakeUtf16BeLossy();

        // ascii with U+FFFD replacement on decode, matching python
        // `bytes.decode("ascii", "replace")` (which yields U+FFFD, not "?").
        public static readonly Encoding AsciiLossy = MakeAsciiLossy();

        static Encoding MakeUtf16BeLossy()
        {
            var enc = (Encoding)Encoding.BigEndianUnicode.Clone();
            enc.DecoderFallback = new DecoderReplacementFallback("\uFFFD");
            enc.EncoderFallback = new EncoderReplacementFallback("\uFFFD");
            return enc;
        }

        static Encoding MakeAsciiLossy()
        {
            var enc = (Encoding)Encoding.ASCII.Clone();
            enc.DecoderFallback = new DecoderReplacementFallback("\uFFFD");
            return enc;
        }

        // python str.title(): a character is uppercased after an uncased character
        // and lowercased after a cased one ("crow's nest" -> "Crow'S Nest").
        public static string Title(string value)
        {
            var sb = new StringBuilder(value.Length);
            bool prevCased = false;
            foreach (char c in value)
            {
                if (prevCased)
                    sb.Append(char.ToLowerInvariant(c));
                else
                    sb.Append(char.ToUpperInvariant(c));
                prevCased = IsCased(c);
            }
            return sb.ToString();
        }

        static bool IsCased(char c)
        {
            return char.ToUpperInvariant(c) != c || char.ToLowerInvariant(c) != c;
        }

        // python repr() for the string values used in warning messages (ascii only).
        public static string Repr(string value)
        {
            bool hasSingle = value.IndexOf('\'') >= 0;
            bool hasDouble = value.IndexOf('"') >= 0;
            char quote = (hasSingle && !hasDouble) ? '"' : '\'';
            var sb = new StringBuilder(value.Length + 2);
            sb.Append(quote);
            foreach (char c in value)
            {
                if (c == '\\')
                    sb.Append("\\\\");
                else if (c == quote)
                    sb.Append('\\').Append(c);
                else if (c == '\n')
                    sb.Append("\\n");
                else if (c == '\r')
                    sb.Append("\\r");
                else if (c == '\t')
                    sb.Append("\\t");
                else if (c < 0x20 || c == 0x7F)
                    sb.Append("\\x").Append(((int)c).ToString("x2"));
                else
                    sb.Append(c);
            }
            sb.Append(quote);
            return sb.ToString();
        }
    }
}
