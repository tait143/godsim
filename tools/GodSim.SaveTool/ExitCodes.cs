using System;

namespace GodSim.SaveTool
{
    /// <summary>Process exit codes (documented in --help and README.md). Stable: CI scripts depend on them.</summary>
    public static class ExitCodes
    {
        /// <summary>Success (verify: file valid; diff: saves identical; replay-verify: hash matched or no expectation).</summary>
        public const int Ok = 0;
        /// <summary>Unexpected internal error (bug in the tool or in Core outside the save loader).</summary>
        public const int InternalError = 1;
        /// <summary>Invalid command line.</summary>
        public const int Usage = 2;
        /// <summary>File-system problem: missing/unreadable file, file too large, output already exists.</summary>
        public const int IoError = 3;
        /// <summary>Save (or replay) is corrupt, truncated, unsupported or fails state validation.</summary>
        public const int Corrupt = 4;
        /// <summary>Signature problem: wrong key, tampered signed file, missing signature, or a signed file without a key.</summary>
        public const int SignatureFailure = 5;
        /// <summary>diff: the saves differ; replay-verify: the final hash differs from --expect-hash.</summary>
        public const int Mismatch = 6;
        /// <summary>fuzz: the loader threw something other than SaveCorruptException at least once.</summary>
        public const int FuzzFailure = 7;
    }

    /// <summary>An expected, user-facing failure. Its message is printed without a stack trace.</summary>
    public sealed class ToolException : Exception
    {
        public ToolException(int exitCode, string message) : base(message) { ExitCode = exitCode; }
        public ToolException(int exitCode, string message, Exception inner) : base(message, inner) { ExitCode = exitCode; }

        /// <summary>Process exit code to return.</summary>
        public int ExitCode { get; }
    }
}
