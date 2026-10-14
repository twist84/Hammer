using System;
using System.IO;
using System.Text;

namespace Hammer
{
    // stdout/stderr writers mirroring the python tools. line endings are forced to
    // "\n" (LF) so text output is platform-independent (python's redirected stdout
    // is not byte-portable anyway; the byte-exact contract applies to written files).
    static class Diag
    {
        static readonly StreamWriter Out;
        static readonly StreamWriter Err;

        static Diag()
        {
            var utf8 = new UTF8Encoding(false);
            Out = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            Err = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };
        }

        public static void WriteLine(string text)
        {
            Out.Write(text);
            Out.Write("\n");
            Out.Flush();
        }

        // python `warn`: "warning: <message>" to stderr.
        public static void Warn(string message)
        {
            Err.Write("warning: ");
            Err.Write(message);
            Err.Write("\n");
            Err.Flush();
        }

        // python `print(..., file=sys.stderr)`.
        public static void Stderr(string text)
        {
            Err.Write(text);
            Err.Write("\n");
            Err.Flush();
        }
    }
}
