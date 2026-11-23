namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys written by the <see cref="CapitalAdvisor"/> (all floats, persisted with the save).
    /// </summary>
    public static class CapitalKeys
    {
        /// <summary>Civ: settlement id of the capital seen on the last advisor run (absent = never seen).</summary>
        public const string LastCapital = "ai.capital.last";
        /// <summary>Civ: number of capital relocations the advisor has observed.</summary>
        public const string Relocations = "ai.capital.moves";
        /// <summary>Settlement: tick at which it became the capital through a relocation (absent = founding capital).</summary>
        public const string SeatSince = "ai.capital.since";
        /// <summary>Settlement: 1 while it was the capital at the last run, 0 after it lost the status.</summary>
        public const string WasCapital = "ai.capital.was";
        /// <summary>Settlement: tick at which it lost the capital status (conquest or relocation).</summary>
        public const string LostTick = "ai.capital.lost";
        /// <summary>Settlement: overall progress 0..1 of the wonder project the capital supports (0..0.5 gathering, 0.5..1 raising).</summary>
        public const string WonderProgress = "ai.capital.wonder";
    }

    /// <summary>Tuning constants of the <see cref="CapitalAdvisor"/>. Pure data; tweak freely.</summary>
    public static class CapitalTuning
    {
        /// <summary>How long a relocated capital keeps pushing for its instruments of rule (2 years).</summary>
        public const int RelocationWindowTicks = SimTime.TicksPerYear * 2;

        /// <summary>Administration points a lone capital needs.</summary>
        public const float AdminBase = 2f;
        /// <summary>Additional administration points per extra settlement of the empire.</summary>
        public const float AdminPerSettlement = 3f;
        /// <summary>Additional administration points per citizen of the empire.</summary>
        public const float AdminPerCitizen = 0.015f;
        /// <summary>Expansionist civs plan administration ahead for this many future settlements (x trait).</summary>
        public const float AdminProjection = 0.75f;
        /// <summary>Below this administration gap ratio no build vote is cast (staffing only).</summary>
        public const float AdminGapThreshold = 0.2f;
        /// <summary>Queued / under-construction administration counts at this share (anticipation).</summary>
        public const float PendingAdminShare = 0.8f;

        /// <summary>Base strengths of the civic grandeur pillars at the capital.</summary>
        public const float FaithGrandeur = 0.5f;
        public const float LearningGrandeur = 0.45f;
        public const float CitadelGrandeur = 0.4f;
        public const float AdminGrandeur = 0.2f;
        public const float CultureGrandeur = 0.3f;
        /// <summary>Bonus for grandeur buildings the race favors.</summary>
        public const float FavoredBonus = 0.15f;
        /// <summary>Grandeur multiplier while the capital is in crisis (bread before monuments).</summary>
        public const float CrisisGrandeurDamp = 0.35f;
        /// <summary>Wonder support multiplier while the capital is in crisis.</summary>
        public const float CrisisWonderDamp = 0.6f;

        /// <summary>Province (non-capital) penalties: prestige concentrates in the capital.</summary>
        public const float ProvinceWonderPenalty = 0.15f;
        public const float ProvinceFaithPenalty = 0.2f;
        public const float ProvinceLearningPenalty = 0.1f;
        public const float ProvinceAdminPenalty = 0.15f;
        /// <summary>A pillar building counts as capital-only grandeur from this age on.</summary>
        public const AgeId GrandMinAge = AgeId.Medieval;

        /// <summary>Stock of a producer's input below which the advisor also pushes the input chain.</summary>
        public const float InputBuffer = 30f;
    }
}
