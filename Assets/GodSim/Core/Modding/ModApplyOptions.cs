namespace GodSim.Core.Modding
{
    /// <summary>Options for <see cref="ModPatchApplier"/>. Options can only tighten, never loosen, the hard caps.</summary>
    public sealed class ModApplyOptions
    {
        /// <summary>When true (default), unknown sections/fields are errors; when false they are warnings and ignored.
        /// Protected (known but non-whitelisted) fields are always errors.</summary>
        public bool StrictUnknownFields = true;
        /// <summary>Allow the "newTechs" section (default true).</summary>
        public bool AllowNewTechs = true;
        /// <summary>When set, the manifest's gameVersionRange must contain it.</summary>
        public SemVersion GameVersion = ModdingInfo.GameVersion;
        /// <summary>Emit a warning whenever sanitization altered a string (default true).</summary>
        public bool WarnOnSanitizedText = true;
    }

    /// <summary>Global modding constants.</summary>
    public static class ModdingInfo
    {
        /// <summary>Game version mods are checked against (mods declare gameVersionRange).</summary>
        public static readonly SemVersion GameVersion = new SemVersion(0, 1, 0);
        /// <summary>Patch format version (documented in docs/MODDING.md).</summary>
        public const int FormatVersion = 1;
    }
}
