namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys and value constants written/read by the civilization-level AI
    /// (<see cref="CivilizationStrategySystem"/>, <see cref="ResearchDirectorSystem"/>, <see cref="ExpansionSystem"/>).
    /// Other modules (e.g. the settlement governor) read these keys from <c>Civilization.Memory</c> /
    /// <c>Settlement.Memory</c>; values are stored as floats. Existing keys/values never change meaning
    /// (saves and the governor depend on them); new ones are only appended.
    /// </summary>
    public static class CivStrategyKeys
    {
        // ------------------------------------------------------------------ civilization memory (written)
        /// <summary>Grand-strategy posture: <see cref="PosturePeaceful"/>, <see cref="PostureDefensive"/> or <see cref="PostureMilitarist"/>.</summary>
        public const string Posture = "ai.posture";
        /// <summary>Development focus: <see cref="FocusGrowth"/> .. <see cref="FocusRecovery"/>.</summary>
        public const string Focus = "ai.focus";
        /// <summary>Perceived external threat 0..1.</summary>
        public const string Threat = "ai.threat";
        /// <summary>Own military power divided by the strongest contacted rival's ((own+1)/(rival+1)), clamped 0..100.</summary>
        public const string PowerRatio = "ai.powerRatio";
        /// <summary>Number of active wars the civ is fighting.</summary>
        public const string ActiveWars = "ai.wars";
        /// <summary>Number of hostile (or at-war) civilizations with a settlement near one of ours.</summary>
        public const string HostileNeighbours = "ai.hostileNeighbours";
        /// <summary>Tick before which the civ will not found another settlement.</summary>
        public const string NextExpandTick = "ai.nextExpandTick";
        /// <summary>Tick of the last settlement founded by expansion.</summary>
        public const string LastExpandTick = "ai.lastExpandTick";
        /// <summary>BuildingType (as float) of the last wonder queued.</summary>
        public const string WonderType = "ai.wonderType";
        /// <summary>Tick the last wonder was queued.</summary>
        public const string WonderTick = "ai.wonderTick";
        /// <summary>Prefix of the per-wonder "last queued tick" keys (suffix = (int)BuildingType).</summary>
        public const string WonderAttemptPrefix = "ai.wonderTry.";
        /// <summary>1 while the civ has nothing left to research (used to avoid log spam).</summary>
        public const string ResearchIdle = "ai.researchIdle";

        // ------------------------------------------------------------------ civilization memory (v2, appended)
        /// <summary>Tick the posture last changed (dwell-time hysteresis).</summary>
        public const string PostureSince = "ai.postureSince";
        /// <summary>Tick the focus last changed (dwell-time hysteresis).</summary>
        public const string FocusSince = "ai.focusSince";
        /// <summary>The focus mapped onto the legacy range 0..4 (recovery -> growth), for readers that clamp to 0..4.</summary>
        public const string FocusLegacy = "ai.focusLegacy";
        /// <summary>Economy trend over the last 3 samples, -1 (collapsing) .. +1 (booming).</summary>
        public const string EconomyTrend = "ai.econTrend";
        /// <summary>Number of economy samples stored (0..3).</summary>
        public const string EconomySampleCount = "ai.econ.n";
        /// <summary>Tick of the last disaster (or famine / population crash) that hit the civ.</summary>
        public const string DisasterTick = "ai.disasterTick";
        /// <summary>Severity 0..1 of the last disaster that hit the civ.</summary>
        public const string DisasterSeverity = "ai.disasterSeverity";
        /// <summary>Tick of the last famine reported in one of the civ's settlements.</summary>
        public const string FamineTick = "ai.famineTick";
        /// <summary>Wonder pause reason code (0 = not paused, see <see cref="CivilizationStrategySystem"/>).</summary>
        public const string WonderPaused = "ai.wonderPaused";
        /// <summary>Crisis bits the research director last reacted to (1 = food, 2 = war).</summary>
        public const string ResearchCrisis = "ai.researchCrisis";
        /// <summary>24-bit hash of the current research plan (to log only plan changes).</summary>
        public const string ResearchPlanHash = "ai.researchPlanHash";
        /// <summary>Number of techs left on the path to the next age (0 = no plan needed / possible).</summary>
        public const string ResearchPlanLength = "ai.researchPlanLen";
        /// <summary>Estimated years until the next age's tech + research requirements are met (-1 = unknown).</summary>
        public const string ResearchPlanYears = "ai.researchPlanYears";
        /// <summary>Placement-feature bits (<see cref="PlacementRule"/>) present around the civ's settlements (cached).</summary>
        public const string Features = "ai.features";
        /// <summary>Tick the feature cache was computed.</summary>
        public const string FeaturesTick = "ai.featuresTick";
        /// <summary>Settlement count when the feature cache was computed.</summary>
        public const string FeaturesCount = "ai.featuresCount";
        /// <summary>Expansion hold reason code (0 = none, 1 = war, 2 = famine, 3 = hostile armies).</summary>
        public const string ExpansionHold = "ai.expandHold";
        /// <summary>Number of remembered scouted sites (0..<see cref="MaxRememberedSites"/>).</summary>
        public const string SiteCount = "ai.sites";
        /// <summary>Read: war weariness 0..1 written by the diplomacy module (optional).</summary>
        public const string WarWeariness = "war.weariness";
        /// <summary>Read: research points per tick published by progression (optional).</summary>
        public const string ResearchPerTick = "prog.researchPerTick";

        /// <summary>Advisor id used for wonder material votes on the <see cref="AdvisorBoard"/>.</summary>
        public const string WonderAdvisorId = "wonder";
        /// <summary>Number of economy samples kept for the trend.</summary>
        public const int EconomySamples = 3;
        /// <summary>Number of scouted sites remembered per civ.</summary>
        public const int MaxRememberedSites = 3;

        private static readonly string[] s_econ = { "ai.econ.0", "ai.econ.1", "ai.econ.2" };
        private static readonly string[] s_econPop = { "ai.econPop.0", "ai.econPop.1", "ai.econPop.2" };
        private static readonly string[] s_siteX = { "ai.site0.x", "ai.site1.x", "ai.site2.x" };
        private static readonly string[] s_siteY = { "ai.site0.y", "ai.site1.y", "ai.site2.y" };
        private static readonly string[] s_siteScore = { "ai.site0.score", "ai.site1.score", "ai.site2.score" };
        private static readonly string[] s_siteTick = { "ai.site0.tick", "ai.site1.tick", "ai.site2.tick" };

        /// <summary>Economy index sample i (0 = newest).</summary>
        public static string EconomySample(int i) => s_econ[Idx(i, EconomySamples)];
        /// <summary>Population sample i (0 = newest), taken with the economy sample.</summary>
        public static string PopulationSample(int i) => s_econPop[Idx(i, EconomySamples)];
        /// <summary>Remembered site i: tile X.</summary>
        public static string SiteX(int i) => s_siteX[Idx(i, MaxRememberedSites)];
        /// <summary>Remembered site i: tile Y.</summary>
        public static string SiteY(int i) => s_siteY[Idx(i, MaxRememberedSites)];
        /// <summary>Remembered site i: score when scouted.</summary>
        public static string SiteScore(int i) => s_siteScore[Idx(i, MaxRememberedSites)];
        /// <summary>Remembered site i: tick it was scouted.</summary>
        public static string SiteTick(int i) => s_siteTick[Idx(i, MaxRememberedSites)];

        private static int Idx(int i, int n) => i < 0 ? 0 : (i >= n ? n - 1 : i);

        // ------------------------------------------------------------------ settlement memory
        /// <summary>Settlement: tick before which it is not considered as an expansion parent (no site / no settlers).</summary>
        public const string ExpandBlockedUntil = "ai.expandBlockedUntil";
        /// <summary>Settlement: tick it last sent settlers to found a colony.</summary>
        public const string LastSettlersTick = "ai.lastSettlersTick";
        /// <summary>Settlement (read, written by the economy): days of food left at the current consumption.</summary>
        public const string FoodDays = "econ.foodDays";

        // ------------------------------------------------------------------ values
        /// <summary>Posture value: peaceful development.</summary>
        public const int PosturePeaceful = 0;
        /// <summary>Posture value: fortify and deter.</summary>
        public const int PostureDefensive = 1;
        /// <summary>Posture value: arm for conquest.</summary>
        public const int PostureMilitarist = 2;

        /// <summary>Focus value: population growth and food.</summary>
        public const int FocusGrowth = 0;
        /// <summary>Focus value: research.</summary>
        public const int FocusResearch = 1;
        /// <summary>Focus value: faith and religion.</summary>
        public const int FocusFaith = 2;
        /// <summary>Focus value: military.</summary>
        public const int FocusMilitary = 3;
        /// <summary>Focus value: trade and wealth.</summary>
        public const int FocusWealth = 4;
        /// <summary>Focus value (appended): rebuild after a disaster, famine or population crash.</summary>
        public const int FocusRecovery = 5;

        /// <summary>Number of distinct focus values.</summary>
        public const int FocusCount = 6;
        /// <summary>Number of focus values known to legacy readers (0..4).</summary>
        public const int LegacyFocusCount = 5;

        /// <summary>Human-readable posture name (for logs/UI).</summary>
        public static string PostureName(int posture)
        {
            switch (posture)
            {
                case PostureDefensive: return "defensive";
                case PostureMilitarist: return "militarist";
                default: return "peaceful";
            }
        }

        /// <summary>Human-readable focus name (for logs/UI).</summary>
        public static string FocusName(int focus)
        {
            switch (focus)
            {
                case FocusResearch: return "research";
                case FocusFaith: return "faith";
                case FocusMilitary: return "military";
                case FocusWealth: return "wealth";
                case FocusRecovery: return "recovery";
                default: return "growth";
            }
        }

        /// <summary>Maps a focus onto the legacy 0..4 range (recovery behaves like growth).</summary>
        public static int LegacyFocus(int focus) => focus == FocusRecovery ? FocusGrowth : (focus < 0 || focus >= LegacyFocusCount ? FocusGrowth : focus);
    }
}
