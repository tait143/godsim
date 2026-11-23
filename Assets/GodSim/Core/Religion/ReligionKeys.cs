namespace GodSim.Core.Religion
{
    /// <summary>
    /// Blackboard keys owned by the Religion module. Everything the module persists lives in these numeric
    /// memory entries (never strings), so saves round-trip without contract changes and a fresh
    /// <see cref="ReligionSystem"/> instance resumes exactly where the last one stopped.
    /// <para>Other modules may READ these keys (they are a stable public interface):</para>
    /// <list type="bullet">
    /// <item><c>rel.founded</c> (civ) 1 once the faith exists.</item>
    /// <item><c>rel.doctrine.worship|ethos|practice|view</c> (civ) <see cref="DoctrineId"/> as a float.</item>
    /// <item><c>rel.holySite</c> (civ) settlement id of the holy site, -1 = none.</item>
    /// <item><c>rel.heresy</c> (civ) 0..1; above 0.7 the faith splinters (schism).</item>
    /// <item><c>rel.holyWarTarget</c> (civ) id of the civ this faith calls a holy war against. ABSENT (or -1) = none.
    /// Written only for Martial Zeal faiths facing a hostile/at-war contact whose View of the God opposes theirs.
    /// The Diplomacy module may treat it as a casus belli / war-desire bonus.</item>
    /// <item><c>rel.defenseBonus</c> (civ and settlement) fractional defense bonus (0.1 = +10%) from doctrines
    /// (Martial Zeal, Sacrifice). The Military module may multiply DefenseStrength by (1 + value).</item>
    /// <item><c>rel.researchBonus</c> (civ) research points per tick the faith adds to ResearchPool (Monasticism, Celestial Order).</item>
    /// </list>
    /// </summary>
    public static class ReligionKeys
    {
        /// <summary>Namespace prefix of every key owned by this module.</summary>
        public const string Prefix = "rel.";

        // ---------------------------------------------------------------- civilization keys
        /// <summary>1 once the civilization has founded its faith.</summary>
        public const string Founded = "rel.founded";
        /// <summary>Tick the faith was founded.</summary>
        public const string FoundedTick = "rel.foundedTick";
        /// <summary>Prefix of the per-axis doctrine keys ("rel.doctrine.worship", ...).</summary>
        public const string DoctrinePrefix = "rel.doctrine.";
        /// <summary>Settlement id of the holy site (-1 = none).</summary>
        public const string HolySite = "rel.holySite";
        /// <summary>1 when the holy site was chosen for a Sacred tile (0 = chosen for its faith building).</summary>
        public const string HolySiteSacred = "rel.holySiteSacred";
        /// <summary>Heresy 0..1.</summary>
        public const string Heresy = "rel.heresy";
        /// <summary>Holy war target civ id (absent or -1 = none). Public interface for Diplomacy.</summary>
        public const string HolyWarTarget = "rel.holyWarTarget";
        /// <summary>Tick the current holy war call was last announced in the chronicle.</summary>
        public const string HolyWarAnnouncedTick = "rel.holyWarTick";
        /// <summary>Tick at which a pending Reformation happens (absent = none pending).</summary>
        public const string ReformationDueTick = "rel.reformDue";
        /// <summary>Number of schisms so far.</summary>
        public const string SchismCount = "rel.schisms";
        /// <summary>Tick of the last schism (civ) / of the schism that broke out here (settlement).</summary>
        public const string LastSchismTick = "rel.schismTick";
        /// <summary>Number of reformations so far.</summary>
        public const string ReformationCount = "rel.reformations";
        /// <summary>Tick of the last doctrine conversion by a foreign faith.</summary>
        public const string LastConversionTick = "rel.conversionTick";
        /// <summary>Id of the civ whose missionaries last converted this faith.</summary>
        public const string ConvertedBy = "rel.convertedBy";
        /// <summary>Current conversion pressure 0..1 from neighbouring faiths (informational).</summary>
        public const string ConversionPressure = "rel.conversionPressure";
        /// <summary>GodState.PowersCast seen at the last run (detects divine meddling for the Distant Watcher view).</summary>
        public const string SeenCasts = "rel.seenCasts";
        /// <summary>Research points per tick added by the faith (informational + integration).</summary>
        public const string ResearchBonus = "rel.researchBonus";
        /// <summary>Fractional defense bonus from doctrines (civ and settlement).</summary>
        public const string DefenseBonus = "rel.defenseBonus";

        // ---------------------------------------------------------------- settlement keys
        /// <summary>DefenseStrength value after the faith's last bump (avoids compounding when nothing recomputed it).</summary>
        public const string DefenseApplied = "rel.defenseApplied";
    }
}
