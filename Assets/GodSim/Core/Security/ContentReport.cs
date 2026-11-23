using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Result of <see cref="ContentValidator.Analyze"/>: errors (content that is broken, unsafe or would crash/soft-lock
    /// the simulation) and warnings (suspicious or unbalanced content that still runs). Both lists are bounded to
    /// <see cref="MaxEntriesPerList"/> entries so a hostile mod with millions of bad entries cannot exhaust memory;
    /// suppressed entries are still counted.
    /// </summary>
    public sealed class ContentReport
    {
        /// <summary>Maximum number of messages stored per list.</summary>
        public const int MaxEntriesPerList = 1000;

        /// <summary>Blocking problems. Content with errors should not be frozen into a playable database.</summary>
        public readonly List<string> Errors = new List<string>();
        /// <summary>Non-blocking findings (balance, unusual values, stub content).</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>Total errors found, including suppressed ones.</summary>
        public int ErrorCount { get; private set; }
        /// <summary>Total warnings found, including suppressed ones.</summary>
        public int WarningCount { get; private set; }

        /// <summary>True when no errors were found (warnings allowed).</summary>
        public bool IsValid => ErrorCount == 0;

        /// <summary>Records an error (stored while under the cap).</summary>
        public void AddError(string message)
        {
            ErrorCount++;
            if (Errors.Count < MaxEntriesPerList) Errors.Add(message ?? "");
        }

        /// <summary>Records a warning (stored while under the cap).</summary>
        public void AddWarning(string message)
        {
            WarningCount++;
            if (Warnings.Count < MaxEntriesPerList) Warnings.Add(message ?? "");
        }

        /// <summary>Multi-line human-readable summary.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Content report: ").Append(ErrorCount).Append(" error(s), ").Append(WarningCount).Append(" warning(s)");
            foreach (var e in Errors) sb.Append("\n  ERROR: ").Append(e);
            if (ErrorCount > Errors.Count) sb.Append("\n  ... ").Append(ErrorCount - Errors.Count).Append(" more error(s) suppressed");
            foreach (var w in Warnings) sb.Append("\n  WARN:  ").Append(w);
            if (WarningCount > Warnings.Count) sb.Append("\n  ... ").Append(WarningCount - Warnings.Count).Append(" more warning(s) suppressed");
            return sb.ToString();
        }
    }
}
