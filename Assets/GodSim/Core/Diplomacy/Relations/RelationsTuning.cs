namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Balancing constants for contact, opinions, stances, trade pacts, war declaration and peace.
    /// Centralized so designers (and tests) can reason about the thresholds in one place.
    /// Distances are Chebyshev tile distances between settlement centers.
    /// </summary>
    public static class RelationsTuning
    {
        // ------------------------------------------------------------------ contact
        /// <summary>Two civs gain contact when any of their settlements are within this many tiles.</summary>
        public const int ContactRange = 45;

        // ------------------------------------------------------------------ opinion
        /// <summary>Fraction of the gap to the target opinion closed per 24-tick run.</summary>
        public const float OpinionDriftPerRun = 0.10f;
        /// <summary>Reference interval (ticks) that <see cref="OpinionDriftPerRun"/> refers to.</summary>
        public const int OpinionDriftReferenceTicks = 24;
        /// <summary>Weight of mutual race affinity (-1..1) in the target opinion.</summary>
        public const float AffinityWeight = 30f;
        /// <summary>Bonus while a trade agreement is in force.</summary>
        public const float TradeBonus = 15f;
        /// <summary>Bonus when both civs are at war with the same third civ.</summary>
        public const float SharedEnemyBonus = 10f;
        /// <summary>Bonus when both civs perceive the god the same way (GodFavor of equal sign).</summary>
        public const float SharedFavorBonus = 8f;
        /// <summary>GodFavor magnitudes at or below this count as indifferent (no sign).</summary>
        public const float GodFavorDeadZone = 0.05f;
        /// <summary>Settlement pairs closer than this many tiles cause border friction.</summary>
        public const int BorderRange = 20;
        /// <summary>Friction per close settlement pair.</summary>
        public const float BorderFrictionPerPair = 10f;
        /// <summary>Maximum border friction.</summary>
        public const float MaxBorderFriction = 30f;
        /// <summary>Weight of average xenophobia (different races only).</summary>
        public const float XenophobiaWeight = 20f;
        /// <summary>Penalty while at war or within <see cref="RecentWarYears"/> after a war.</summary>
        public const float RecentWarPenalty = 25f;
        /// <summary>How long the memory of a war sours relations.</summary>
        public const float RecentWarYears = 20f;
        /// <summary>Weight of the average Diplomacy personality.</summary>
        public const float DiplomacyWeight = 10f;
        /// <summary>Opinion bounds.</summary>
        public const float MinOpinion = -100f, MaxOpinion = 100f;

        // ------------------------------------------------------------------ stances (with hysteresis)
        /// <summary>Opinion needed to become allies (plus both Diplomacy personalities above <see cref="AllyMinDiplomacy"/>).</summary>
        public const float AllyEnter = 60f;
        /// <summary>Allies break up below this opinion.</summary>
        public const float AllyLeave = 45f;
        /// <summary>Minimum Diplomacy personality of both races to form an alliance.</summary>
        public const float AllyMinDiplomacy = 0.4f;
        /// <summary>Opinion needed to become friendly.</summary>
        public const float FriendlyEnter = 25f;
        /// <summary>Friendly (or allied) relations fall back to neutral below this opinion.</summary>
        public const float FriendlyLeave = 15f;
        /// <summary>Relations are neutral above this opinion and hostile at or below it.</summary>
        public const float NeutralFloor = -25f;

        // ------------------------------------------------------------------ trade pacts
        /// <summary>Opinion needed to sign a trade pact.</summary>
        public const float TradeOn = 15f;
        /// <summary>Trade pacts lapse below this opinion.</summary>
        public const float TradeOff = 0f;
        /// <summary>Minimum average Mercantilism of both races to sign a trade pact.</summary>
        public const float TradeMinMercantilism = 0.4f;

        // ------------------------------------------------------------------ war declaration
        /// <summary>An aggressor with this many active wars does not start another.</summary>
        public const int MaxActiveWarsToDeclare = 1; // balance pass 2026-11-19, was 2
        /// <summary>Hatred path: opinion below this ...</summary>
        public const float HatredOpinion = -40f;
        /// <summary>... and aggression above this.</summary>
        public const float HatredMinAggression = 0.35f;
        /// <summary>Opportunist path: aggression above this ...</summary>
        public const float OpportunistMinAggression = 0.6f;
        /// <summary>... opinion below this ...</summary>
        public const float OpportunistOpinion = -10f;
        /// <summary>... and military strength ratio above this.</summary>
        public const float OpportunistStrengthRatio = 1.6f; // balance pass 2026-11-19, was 1.3 (orcs declared 111 of 133 arena wars)
        /// <summary>Per-year declaration chance once all conditions hold, multiplied by the aggressor's Aggression.</summary>
        public const float DeclarationChancePerYear = 0.06f; // balance pass 2026-11-19, was 0.3: 7-19 wars by year 200 (target 1-4)

        // ------------------------------------------------------------------ peace
        /// <summary>Wars longer than this with a small score can end in a stalemate.</summary>
        public const float StalemateYears = 8f;
        /// <summary>|WarScore| below this counts as a stalemate.</summary>
        public const float StalemateScore = 25f;
        /// <summary>Per-year chance that a stalemated war ends.</summary>
        public const float StalematePeaceChancePerYear = 0.4f;
        /// <summary>|WarScore| at or above this makes the loser sue for peace.</summary>
        public const float DecisiveScore = 60f;
        /// <summary>Share of the loser capital's gold paid to the winner capital.</summary>
        public const float TributeFraction = 0.3f;
        /// <summary>Combined losses above this share of the initial combined military exhaust both sides.</summary>
        public const float ExhaustionLossFraction = 0.4f;
        /// <summary>Floor for the recorded initial combined military (avoids instant exhaustion of tiny militaries).</summary>
        public const float MinInitialPower = 10f;
        /// <summary>Hard cap on war duration.</summary>
        public const float MaxWarYears = 25f;
        /// <summary>Truce length after peace.</summary>
        public const float TruceYears = 20f; // balance pass 2026-11-19, was 10
        /// <summary>Most recent inactive wars kept in <see cref="DiplomacyState.Wars"/>.</summary>
        public const int MaxInactiveWars = 200;

        // ------------------------------------------------------------------ casus belli
        /// <summary>Casus belli: settlements of both civs within <see cref="BorderRange"/>.</summary>
        public const string CasusBorderDispute = "border dispute";
        /// <summary>Casus belli: GodFavor signs differ.</summary>
        public const string CasusHolyWar = "holy war";
        /// <summary>Casus belli: plain expansion.</summary>
        public const string CasusConquest = "conquest";
        /// <summary>Casus belli: the two civs fought before.</summary>
        public const string CasusOldGrudge = "old grudge";
        /// <summary>Casus belli of an ally joining a defensive war.</summary>
        public const string CasusCallToArms = "call to arms";
    }
}
