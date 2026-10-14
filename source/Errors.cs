using System;

namespace Hammer
{
    // carries a value-level error message that the per-file and bulk entry points
    // report as `error: <message>` and exit non-zero. models the union of python's
    // ValueError / BlfFileError which both message the same way.
    sealed class ToolError : Exception
    {
        public ToolError(string message) : base(message) { }
    }
}
