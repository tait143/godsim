using System;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Per-schema upgrade hook. The payload reader is schema-aware (fields added in schema N are only read
    /// when the file's schema is at least N; older files get the constructor defaults), and after parsing,
    /// <see cref="Upgrade"/> runs one step per version to fix up semantics (e.g. derive a new field from old
    /// ones) until the state matches <see cref="WorldState.SchemaVersion"/>.
    /// <para>Schema history: 1 = initial; 2 = Stockpile current-period flow counters (PeriodProduced/PeriodConsumed)
    /// are persisted per settlement (schema-1 files load them as 0, which is what schema-1 builds resumed with).</para>
    /// <para>Policy for bumping <see cref="WorldState.SchemaVersion"/> to N+1: (1) gate new payload fields on
    /// <c>schema &gt;= N+1</c> in the payload codec, (2) append exactly one step to <c>Steps</c>,
    /// (3) keep the old read path until <see cref="MinSupportedSchemaVersion"/> is raised deliberately.</para>
    /// </summary>
    public static class SaveMigrator
    {
        /// <summary>Oldest schema this build can load.</summary>
        public const int MinSupportedSchemaVersion = 1;

        /// <summary>Schema written by this build.</summary>
        public static int CurrentSchemaVersion => WorldState.SchemaVersion;

        /// <summary>
        /// Steps[k] upgrades a state loaded from schema (MinSupportedSchemaVersion + k) to the next schema.
        /// Steps must be deterministic and must not throw on valid data.
        /// </summary>
        private static readonly Action<WorldState, GameDatabase>[] Steps =
        {
            // 1 -> 2: period flow counters did not exist in the payload; the reader left them at 0 (the schema-1 resume value).
            (state, db) => { },
        };

        /// <summary>Number of registered upgrade steps (must equal Current - MinSupported).</summary>
        public static int StepCount => Steps.Length;

        /// <summary>True if a file with this schema version can be loaded by this build.</summary>
        public static bool IsSupported(int schemaVersion) =>
            schemaVersion >= MinSupportedSchemaVersion && schemaVersion <= CurrentSchemaVersion;

        /// <summary>
        /// Upgrades a freshly parsed state from <paramref name="fromSchemaVersion"/> to the current schema.
        /// No-op for current-version files. Throws <see cref="SaveCorruptException"/> for unsupported versions.
        /// </summary>
        public static void Upgrade(WorldState state, GameDatabase db, int fromSchemaVersion)
        {
            if (state == null) throw new SaveCorruptException("No state to migrate.");
            if (!IsSupported(fromSchemaVersion))
                throw new SaveCorruptException("Unsupported save schema version " + fromSchemaVersion + " (supported " + MinSupportedSchemaVersion + ".." + CurrentSchemaVersion + ").");
            if (Steps.Length != CurrentSchemaVersion - MinSupportedSchemaVersion)
                throw new InvalidOperationException("SaveMigrator is missing upgrade steps for schema " + CurrentSchemaVersion + ".");
            for (int v = fromSchemaVersion; v < CurrentSchemaVersion; v++)
                Steps[v - MinSupportedSchemaVersion](state, db);
        }
    }
}
