using System.Collections.Generic;

namespace GodSim.Core.Modding
{
    /// <summary>Limits and settings for <see cref="ModLoader"/>.</summary>
    public sealed class ModLoaderOptions
    {
        /// <summary>Game version every pack's gameVersionRange must contain (null = skip the check).</summary>
        public SemVersion GameVersion = ModdingInfo.GameVersion;
        /// <summary>Parser limits for mod.json and every data file.</summary>
        public JsonLimits Limits = JsonLimits.Default;
        public int MaxPacks = 64;
        public int MaxDataFilesPerPack = ModManifest.MaxDataFiles;
        /// <summary>Per-file cap (bytes); also capped by Limits.MaxInputBytes.</summary>
        public long MaxFileBytes = 2 * 1024 * 1024;
        /// <summary>Cap for mod.json (bytes).</summary>
        public long MaxManifestBytes = 64 * 1024;
        /// <summary>Cap for all files of one pack (bytes).</summary>
        public long MaxPackBytes = 16L * 1024 * 1024;
        /// <summary>Cap for all packs together (bytes).</summary>
        public long MaxTotalBytes = 64L * 1024 * 1024;
        /// <summary>Directory listings longer than this are refused (defends against huge directories).</summary>
        public int MaxDirectoryEntries = 1024;
    }

    /// <summary>Two packs change the same field; the later one (in load order) wins.</summary>
    public sealed class ModConflict
    {
        public string Path = "";
        public string OverriddenModId = "";
        public string WinningModId = "";
        public override string ToString() => Path + ": '" + WinningModId + "' overrides '" + OverriddenModId + "'";
    }

    /// <summary>Output of <see cref="ModLoader"/>: packs in final application order plus diagnostics.</summary>
    public sealed class ModLoadResult
    {
        /// <summary>Accepted packs in application order (dependencies first, then loadOrder, then id).</summary>
        public List<ModPack> Packs { get; } = new List<ModPack>();
        public List<ModDiagnostic> Warnings { get; } = new List<ModDiagnostic>();
        public List<ModDiagnostic> Errors { get; } = new List<ModDiagnostic>();
        public List<ModConflict> Conflicts { get; } = new List<ModConflict>();
        /// <summary>Folder names or ids of packs that were refused.</summary>
        public List<string> Rejected { get; } = new List<string>();

        internal void Error(string modId, string source, string message, int line = 0, int col = 0) =>
            Errors.Add(new ModDiagnostic { ModId = modId ?? "", Source = source ?? "", Line = line, Column = col, Message = message ?? "" });

        internal void Warn(string modId, string source, string message, int line = 0, int col = 0) =>
            Warnings.Add(new ModDiagnostic { ModId = modId ?? "", Source = source ?? "", Line = line, Column = col, Message = message ?? "" });

        internal void Error(string modId, ModLoadException e) => Error(modId, e.SourceName, e.Reason, e.Line, e.Column);
    }
}
