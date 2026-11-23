namespace GodSim.Core.AI
{
    /// <summary>Blackboard keys written by the <see cref="AgeTransitionAdvisor"/> (all floats, persisted with the save).</summary>
    public static class AgesKeys
    {
        // ---- civilization memory
        /// <summary>Tick at which the civ entered its current age (written on <see cref="AgeAdvancedEvent"/>).</summary>
        public const string EnteredTick = "adv.ages.enteredTick";
        /// <summary>The age <see cref="EnteredTick"/> refers to (as a number); used to detect missed events after loads.</summary>
        public const string EnteredAge = "adv.ages.enteredAge";
        /// <summary>TotalResearch sampled on the previous advisor run (research-rate estimation).</summary>
        public const string LastResearch = "adv.ages.lastResearch";
        /// <summary>Tick of the previous research sample.</summary>
        public const string LastResearchTick = "adv.ages.lastResearchTick";
        /// <summary>Smoothed research points per tick.</summary>
        public const string ResearchRate = "adv.ages.researchRate";
        /// <summary>0..1 readiness for the next age (min of research, population and tech requirements).</summary>
        public const string Progress = "adv.ages.progress";
        /// <summary>Estimated years until the research threshold of the next age is met (UI/debug, capped).</summary>
        public const string EtaYears = "adv.ages.etaYears";
        /// <summary>0..1 strength of the pre-transition preparation phase.</summary>
        public const string Prep = "adv.ages.prep";

        // ---- settlement memory
        /// <summary>Dominant phase of the advisor in a settlement (see <see cref="AgesPhase"/>).</summary>
        public const string Phase = "adv.ages.phase";
        /// <summary>0..1 fraction of this age's signature buildings already present (during the adoption wave).</summary>
        public const string Adoption = "adv.ages.adoption";
    }

    /// <summary>What the age advisor is mainly doing in a settlement (stored as a float in <see cref="AgesKeys.Phase"/>).</summary>
    public enum AgesPhase : byte { Idle = 0, Adoption = 1, Modernizing = 2, Preparing = 3 }

    /// <summary>Tuning constants for the age advisor (data-free knobs; all buildings are read from definitions).</summary>
    public static class AgesTuning
    {
        /// <summary>Length of the post-advance adoption wave (~20 years).</summary>
        public const int WaveTicks = 20 * SimTime.TicksPerYear;
        /// <summary>Base strength of the headline adoption vote in the capital.</summary>
        public const float WaveBase = 0.9f;
        /// <summary>Wave intensity at the very end of the wave (it fades linearly from 1 to this).</summary>
        public const float WaveTail = 0.25f;
        /// <summary>Next-age readiness at which pre-transition stockpiling starts.</summary>
        public const float PrepStart = 0.85f;
        /// <summary>Readiness at which an ETA-based early warning may start when research is fast.</summary>
        public const float PrepEarlyStart = 0.6f;
        /// <summary>Years-to-next-age under which the early warning kicks in.</summary>
        public const float PrepEtaYears = 3f;
        /// <summary>Vote against building more of an obsolete type.</summary>
        public const float ObsoleteNegative = 0.6f;
        /// <summary>Vote against investing in a type that becomes obsolete in the next age (scaled by prep).</summary>
        public const float SunsetNegative = 0.55f;
        /// <summary>Storage pressure ((stock + next-era need) / capacity) above which storage is voted.</summary>
        public const float StoragePressureStart = 0.6f;
        /// <summary>Research-rate smoothing per run.</summary>
        public const float RateAlpha = 0.35f;
        /// <summary>Planning horizon for projected stock income.</summary>
        public const int MaxHorizonTicks = 2 * SimTime.TicksPerYear;
        /// <summary>Site surveys are refreshed this often (or when terrain/territory changes).</summary>
        public const int SiteRefreshTicks = 96;
    }
}
