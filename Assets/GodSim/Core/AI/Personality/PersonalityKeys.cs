namespace GodSim.Core.AI
{
    /// <summary>The kinds of lived experience that shape a civilization's personality over time.</summary>
    public enum PersonalityCause : byte
    {
        Victory, Defeat, Peace, Trade, Hardship, Blessing, Wrath, Learning, Expansion, Crowding, Contact, Invaded
    }

    /// <summary>
    /// Civilization-memory keys owned by the personality module (all floats, persisted with the save).
    ///   "pers.init"            1 once the civ's traits were seeded from its race defaults
    ///   "pers.exp.{cause}"     decaying experience counter, >= 0 (see <see cref="PersonalityCause"/>)
    ///   "pers.band.{trait}"    -1/0/+1: trait currently notably below / near / above its race default
    ///   "pers.chron.{trait}"   year of the last chronicle entry about this trait
    ///   "pers.run.famine"      famine experience gathered since the last run (rate cap)
    /// Traits themselves live in "ai.trait.*" and are read/written through <see cref="AdvisorBoard"/>.
    /// </summary>
    public static class PersonalityKeys
    {
        public const int TraitCount = 8;
        public const int CauseCount = 12;

        public const string Initialized = "pers.init";
        public const string FamineThisRun = "pers.run.famine";
        /// <summary>Settlement key written by the expansion AI when no site/settlers are available (read only).</summary>
        public const string ExpandBlockedUntil = "ai.expandBlockedUntil";

        private static readonly string[] s_exp =
        {
            "pers.exp.victory", "pers.exp.defeat", "pers.exp.peace", "pers.exp.trade", "pers.exp.hardship", "pers.exp.blessing",
            "pers.exp.wrath", "pers.exp.learning", "pers.exp.expansion", "pers.exp.crowding", "pers.exp.contact", "pers.exp.invaded"
        };

        private static readonly string[] s_band =
        {
            "pers.band.aggression", "pers.band.expansionism", "pers.band.piety", "pers.band.industriousness",
            "pers.band.curiosity", "pers.band.diplomacy", "pers.band.xenophobia", "pers.band.mercantilism"
        };

        private static readonly string[] s_chron =
        {
            "pers.chron.aggression", "pers.chron.expansionism", "pers.chron.piety", "pers.chron.industriousness",
            "pers.chron.curiosity", "pers.chron.diplomacy", "pers.chron.xenophobia", "pers.chron.mercantilism"
        };

        public static string ExperienceKey(PersonalityCause c) => s_exp[(int)c];
        public static string BandKey(AITrait t) => s_band[(int)t];
        public static string ChronicleYearKey(AITrait t) => s_chron[(int)t];
    }
}
