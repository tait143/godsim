namespace GodSim.Core.Religion
{
    /// <summary>Balancing constants of the Religion module (per-tick rates are scaled by ctx.DeltaTicks).</summary>
    public static class ReligionTuning
    {
        /// <summary>ReligionSystem interval (ticks).</summary>
        public const int TickInterval = 48;

        /// <summary>Hard cap on any summed Faith/Happiness/Unrest/Devotion nudge per run (complements FaithSystem; never fights it).</summary>
        public const float MaxNudgePerRun = 0.02f;

        // ---------------------------------------------------------------- founding
        /// <summary>Devotion above which a faith is founded even without a religious building.</summary>
        public const float FoundingDevotion = 0.6f;

        // ---------------------------------------------------------------- selection
        /// <summary>Magnitude of the deterministic per-civ selection jitter.</summary>
        public const float SelectionJitter = 0.05f;
        /// <summary>Powers cast at which the god counts as fully "active" for doctrine selection.</summary>
        public const float ActivitySaturationCasts = 12f;

        // ---------------------------------------------------------------- holy site & pilgrimage (per run)
        /// <summary>Faith nudge at the holy site.</summary>
        public const float HolySiteFaith = 0.008f;
        /// <summary>Happiness nudge at the holy site.</summary>
        public const float HolySiteHappiness = 0.006f;
        /// <summary>Happiness nudge in other settlements of the civ (returning pilgrims).</summary>
        public const float PilgrimageHappiness = 0.003f;
        /// <summary>Extra tiles beyond TerritoryRadius scanned for Sacred tiles owned by the settlement.</summary>
        public const int SacredScanMargin = 2;

        // ---------------------------------------------------------------- heresy (per tick)
        /// <summary>GodFavor below which (together with low devotion) heresy grows.</summary>
        public const float HeresyFavorThreshold = -0.3f;
        /// <summary>Devotion below which (together with bad favor) heresy grows.</summary>
        public const float HeresyDevotionThreshold = 0.4f;
        /// <summary>Base heresy growth per tick under bad favor and low devotion.</summary>
        public const float HeresyRisePerTick = 0.001f;
        /// <summary>Heresy growth per tick at full conflict between the god's alignment and the View doctrine.</summary>
        public const float HeresyConflictPerTick = 0.0008f;
        /// <summary>Heresy growth per tick at full foreign conversion pressure.</summary>
        public const float ConversionHeresyPerTick = 0.0006f;
        /// <summary>Heresy decay per tick when nothing feeds it (scaled by 0.5 + devotion).</summary>
        public const float HeresyDecayPerTick = 0.0005f;
        /// <summary>Heresy above which settlements grow restless (small unrest nudge).</summary>
        public const float HeresyUnrestThreshold = 0.5f;
        /// <summary>Unrest per run per unit of heresy above the threshold.</summary>
        public const float HeresyUnrestPerRun = 0.02f;
        /// <summary>Casts since the last run above which a Distant Watcher faith feels contradicted.</summary>
        public const int DistantWatcherCastTolerance = 2;

        // ---------------------------------------------------------------- schism & reformation
        /// <summary>Heresy above which a schism breaks out.</summary>
        public const float SchismThreshold = 0.7f;
        /// <summary>Unrest added to the schismatic settlement.</summary>
        public const float SchismUnrest = 0.4f;
        /// <summary>Heresy right after a schism (the heretics have split away).</summary>
        public const float HeresyAfterSchism = 0.4f;
        /// <summary>Delay between a schism and the Reformation that answers it.</summary>
        public const int ReformationDelayTicks = SimTime.TicksPerYear * 2;
        /// <summary>Heresy multiplier applied by a Reformation.</summary>
        public const float HeresyAfterReformation = 0.5f;

        // ---------------------------------------------------------------- conversion
        /// <summary>Average-faith gap a neighbour must exceed before it exerts conversion pressure.</summary>
        public const float ConversionFaithGap = 0.15f;
        /// <summary>Per-year chance (at pressure 1) that a doctrine is swapped for the neighbour's.</summary>
        public const float ConversionSwapPerYear = 0.3f;
        /// <summary>Minimum ticks between two conversion swaps of the same faith.</summary>
        public const int ConversionCooldownTicks = SimTime.TicksPerYear * 2;

        // ---------------------------------------------------------------- holy war
        /// <summary>Minimum ticks between two chronicle announcements of a holy war call.</summary>
        public const int HolyWarAnnounceCooldownTicks = SimTime.TicksPerYear * 2;

        // ---------------------------------------------------------------- research / defense caps
        /// <summary>Upper bound on faith research per tick (whole civ).</summary>
        public const float MaxResearchPerTick = 0.5f;
        /// <summary>Upper bound on the published defense bonus.</summary>
        public const float MaxDefenseBonus = 0.25f;
        /// <summary>Safety bound for ResearchPool after the faith adds to it.</summary>
        public const float MaxResearchPool = 1e9f;
    }
}
