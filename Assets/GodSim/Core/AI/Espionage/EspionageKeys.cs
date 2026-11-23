namespace GodSim.Core.AI
{
    /// <summary>
    /// Civilization memory keys written/read by the espionage AI (floats, persisted with the save).
    ///
    ///   civ "esp.network"            [0, MaxNetwork]  spy-network strength (grows yearly, spent on operations)
    ///   civ "esp.guard"              [0, 1]           counter-intelligence alertness after being targeted (decays)
    ///   civ "esp.lastOp"             tick             last operation this civ launched
    ///   civ "esp.ops/.stolen/.sabotage/.exposed"      lifetime counters (UI / learning)
    ///   civ "esp.exposedBy.{civId}"  tick             written on the VICTIM: spies of {civId} were caught at that
    ///                                                 tick. A casus belli flag for diplomacy (victim vs civId).
    ///   civ "prog.dirty"             1                asks progression to recompute modifiers after a theft
    /// </summary>
    public static class EspionageKeys
    {
        public const string Network = "esp.network";
        public const string Guard = "esp.guard";
        public const string LastOpTick = "esp.lastOp";
        public const string Operations = "esp.ops";
        public const string TechsStolen = "esp.stolen";
        public const string Sabotages = "esp.sabotage";
        public const string TimesExposed = "esp.exposed";
        public const string ProgressionDirty = "prog.dirty";
        public const string ExposedByPrefix = "esp.exposedBy.";

        private const int CacheSize = 1024;
        private static readonly string[] s_exposedBy = new string[CacheSize];

        /// <summary>Key (on the victim) recording the tick at which spies of <paramref name="spyCivId"/> were exposed. Cached: no per-call allocation.</summary>
        public static string ExposedBy(int spyCivId)
        {
            if (spyCivId >= 0 && spyCivId < CacheSize)
            {
                string k = s_exposedBy[spyCivId];
                if (k == null) { k = ExposedByPrefix + spyCivId; s_exposedBy[spyCivId] = k; }
                return k;
            }
            return ExposedByPrefix + spyCivId;
        }
    }
}
