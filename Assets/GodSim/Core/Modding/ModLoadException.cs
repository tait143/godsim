using System;

namespace GodSim.Core.Modding
{
    /// <summary>
    /// The only exception type the modding pipeline raises for bad mod content (malformed JSON, invalid
    /// manifests, unreadable packs). Carries the source name and a 1-based line:column position when known.
    /// </summary>
    public sealed class ModLoadException : Exception
    {
        /// <summary>File or logical name of the offending document (may be empty).</summary>
        public string SourceName { get; }
        /// <summary>1-based line, or 0 when unknown.</summary>
        public int Line { get; }
        /// <summary>1-based column (UTF-16 code units), or 0 when unknown.</summary>
        public int Column { get; }
        /// <summary>The message without the position prefix.</summary>
        public string Reason { get; }

        public ModLoadException(string reason) : this(reason, null, 0, 0) { }

        public ModLoadException(string reason, string sourceName, int line, int column)
            : base(Format(reason, sourceName, line, column))
        {
            Reason = reason ?? "";
            SourceName = sourceName ?? "";
            Line = line;
            Column = column;
        }

        public ModLoadException(string reason, string sourceName, int line, int column, Exception inner)
            : base(Format(reason, sourceName, line, column), inner)
        {
            Reason = reason ?? "";
            SourceName = sourceName ?? "";
            Line = line;
            Column = column;
        }

        /// <summary>"source:line:col: reason" (parts omitted when unknown).</summary>
        public static string Format(string reason, string sourceName, int line, int column)
        {
            string prefix = string.IsNullOrEmpty(sourceName) ? "" : sourceName;
            if (line > 0) prefix += (prefix.Length > 0 ? ":" : "") + line + ":" + column;
            return prefix.Length > 0 ? prefix + ": " + (reason ?? "") : (reason ?? "");
        }
    }
}
