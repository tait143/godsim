using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.Modding
{
    /// <summary>A warning or error with its location (source file, line:column, logical field path).</summary>
    public sealed class ModDiagnostic
    {
        public string ModId = "";
        public string Source = "";
        public int Line;
        public int Column;
        /// <summary>Logical path such as "buildings.House.buildWork" ("" when not field-specific).</summary>
        public string Path = "";
        public string Message = "";

        public override string ToString()
        {
            string where = ModLoadException.Format("", Source, Line, Column).TrimEnd(' ', ':');
            var sb = new StringBuilder();
            if (ModId.Length > 0) sb.Append('[').Append(ModId).Append("] ");
            if (where.Length > 0) sb.Append(where).Append(": ");
            if (Path.Length > 0) sb.Append(Path).Append(": ");
            sb.Append(Message);
            return sb.ToString();
        }
    }

    /// <summary>One applied (or, before commit, staged) field change.</summary>
    public sealed class ModChange
    {
        public string Path = "";
        public string OldValue = "";
        public string NewValue = "";
        public string Source = "";
        public int Line;
        public int Column;
        public override string ToString() => Path + ": " + OldValue + " -> " + NewValue;
    }

    /// <summary>Result of <see cref="ModPatchApplier.Apply"/>. When <see cref="Applied"/> is false nothing changed.</summary>
    public sealed class ModApplyReport
    {
        public string ModId { get; }
        /// <summary>True when every patch validated and was committed to the database.</summary>
        public bool Applied { get; internal set; }
        /// <summary>Committed changes (empty when not applied).</summary>
        public List<ModChange> Changes { get; } = new List<ModChange>();
        /// <summary>Keys of techs added by this pack (empty when not applied).</summary>
        public List<string> AddedTechs { get; } = new List<string>();
        public List<ModDiagnostic> Warnings { get; } = new List<ModDiagnostic>();
        public List<ModDiagnostic> Errors { get; } = new List<ModDiagnostic>();

        public ModApplyReport(string modId) { ModId = modId ?? ""; }

        public bool HasErrors => Errors.Count > 0;

        internal ModDiagnostic AddError(string source, JsonValue at, string path, string message) => Add(Errors, source, at, path, message);
        internal ModDiagnostic AddWarning(string source, JsonValue at, string path, string message) => Add(Warnings, source, at, path, message);

        /// <summary>Errors and warnings are each capped at this many entries (a hostile pack cannot flood memory/logs).</summary>
        public const int MaxDiagnostics = 500;

        internal void AddError(ModDiagnostic d) => Append(Errors, d);
        internal void AddWarning(ModDiagnostic d) => Append(Warnings, d);

        private void Append(List<ModDiagnostic> list, ModDiagnostic d)
        {
            if (list.Count < MaxDiagnostics) { list.Add(d); return; }
            if (list.Count == MaxDiagnostics)
                list.Add(new ModDiagnostic { ModId = ModId, Message = "Too many diagnostics; further ones were suppressed." });
        }

        private ModDiagnostic Add(List<ModDiagnostic> list, string source, JsonValue at, string path, string message)
        {
            var d = new ModDiagnostic
            {
                ModId = ModId,
                Source = source ?? "",
                Line = at != null ? at.Line : 0,
                Column = at != null ? at.Column : 0,
                Path = path ?? "",
                Message = message ?? ""
            };
            Append(list, d);
            return d;
        }

        /// <summary>Throws a <see cref="ModLoadException"/> listing the errors when the pack was not applied.</summary>
        public void ThrowIfFailed()
        {
            if (Applied && Errors.Count == 0) return;
            throw new ModLoadException("Mod '" + ModId + "' was not applied:\n - " + string.Join("\n - ", Lines(Errors)));
        }

        private static List<string> Lines(List<ModDiagnostic> list)
        {
            var r = new List<string>();
            foreach (var d in list) r.Add(d.ToString());
            if (r.Count == 0) r.Add("(no errors recorded)");
            return r;
        }

        /// <summary>Multi-line human-readable summary.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Mod '").Append(ModId).Append("': ").Append(Applied ? "applied" : "NOT applied")
              .Append(" (").Append(Changes.Count).Append(" changes, ").Append(Warnings.Count).Append(" warnings, ")
              .Append(Errors.Count).Append(" errors)");
            foreach (var e in Errors) sb.Append("\n  error: ").Append(e);
            foreach (var w in Warnings) sb.Append("\n  warning: ").Append(w);
            return sb.ToString();
        }
    }
}
