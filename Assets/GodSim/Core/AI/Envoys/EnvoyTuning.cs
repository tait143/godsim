namespace GodSim.Core.AI
{
    /// <summary>
    /// Balancing constants for envoy statecraft (gifts, tribute, coalitions, pacts, betrayals, royal marriages).
    /// Opinion changes are impulses: the relations module drifts opinion 10% per 24 ticks back toward its
    /// target, so a one-off impulse fades over a few years while a repeated one holds a steady offset of
    /// roughly 4x its size (at the 48-tick envoy interval).
    /// </summary>
    public static class EnvoyTuning
    {
        // ------------------------------------------------------------------ shared
        /// <summary>Opinion bounds (mirrors the relations module).</summary>
        public const float MinOpinion = -100f, MaxOpinion = 100f;
        /// <summary>Opinion at which a relation counts as friendly enough for pacts.</summary>
        public const float PactMinOpinion = 30f;
        /// <summary>Years after a war during which the pair still nurses a grievance.</summary>
        public const float WarGrievanceYears = 20f;

        // ------------------------------------------------------------------ gifts
        /// <summary>Capital gold needed before a civ considers itself rich enough to give.</summary>
        public const float GiftMinStock = 60f;
        /// <summary>Largest share of the capital's gold given at once.</summary>
        public const float GiftMaxShare = 0.10f;
        /// <summary>Smallest gift worth sending.</summary>
        public const float GiftMinAmount = 5f;
        /// <summary>Gifts this large reach the full impulse.</summary>
        public const float GiftGrandAmount = 20f;
        /// <summary>Gifts at least this large are chronicled.</summary>
        public const float GiftChronicleAmount = 12f;
        /// <summary>Opinion impulse range of a gift.</summary>
        public const float GiftMinImpulse = 8f, GiftMaxImpulse = 20f;
        /// <summary>Minimum gift propensity (0.55 Diplomacy + 0.45 Mercantilism).</summary>
        public const float GiftMinPropensity = 0.45f;
        /// <summary>Per-year chance of a gift at propensity 1.</summary>
        public const float GiftChancePerYear = 0.8f;
        /// <summary>Years between two gifts of the same giver.</summary>
        public const float GiftCooldownYears = 2f;
        /// <summary>A target this much stronger (military ratio) is appeased.</summary>
        public const float AppeaseStrengthRatio = 1.3f;
        /// <summary>Befriend targets sit in this opinion window (below friendship, above open hatred).</summary>
        public const float BefriendMinOpinion = -30f, BefriendMaxOpinion = 25f;

        // ------------------------------------------------------------------ tribute
        /// <summary>The bully must be at least this much stronger.</summary>
        public const float TributeStrengthRatio = 2f;
        /// <summary>Proud races (Orcs, Dwarves) only pay when outmatched at least this much.</summary>
        public const float DesperateStrengthRatio = 4f;
        /// <summary>Minimum Aggression of the bully.</summary>
        public const float TributeMinBullyAggression = 0.55f;
        /// <summary>Payers above this Aggression refuse unless desperate.</summary>
        public const float TributeMaxPayerAggression = 0.8f;
        /// <summary>Share of the payer capital's gold (or food) paid.</summary>
        public const float TributeShare = 0.15f;
        /// <summary>Minimum gold to pay in gold; otherwise food is sent.</summary>
        public const float TributeMinGold = 8f;
        /// <summary>Minimum food stock to pay in food.</summary>
        public const float TributeMinFood = 40f;
        /// <summary>Food per gold of tribute value.</summary>
        public const float FoodPerGold = 3f;
        /// <summary>Opinion impulse range of a tribute.</summary>
        public const float TributeMinImpulse = 10f, TributeMaxImpulse = 18f;
        /// <summary>Truce bought by a tribute.</summary>
        public const float TributeTruceYears = 3f;
        /// <summary>Tribute is only renewed when the current truce ends sooner than this.</summary>
        public const float TributeRenewYears = 0.75f;
        /// <summary>Years between two tributes (or refusals) of the same payer.</summary>
        public const float TributeCooldownYears = 2.5f;

        // ------------------------------------------------------------------ coalitions
        /// <summary>A civ is a hegemon when its military or settlement count exceeds the runner-up by this ratio.</summary>
        public const float HegemonRatio = 1.6f;
        /// <summary>An existing coalition dissolves once the ratio falls below this (hysteresis).</summary>
        public const float HegemonLeaveRatio = 1.3f;
        /// <summary>Military power below this never makes a hegemon.</summary>
        public const float HegemonMinPower = 5f;
        /// <summary>Mutual opinion impulse between members per run.</summary>
        public const float CoalitionImpulse = 10f;
        /// <summary>Opinion impulse of each member toward the hegemon per run.</summary>
        public const float CoalitionHegemonImpulse = -2f;
        /// <summary>Members friendlier than this with the hegemon stay out.</summary>
        public const float CoalitionMaxHegemonOpinion = 30f;
        /// <summary>Minimum willingness (0.5 Diplomacy + 0.3 (1 - Xenophobia) + 0.2 Aggression).</summary>
        public const float CoalitionMinWillingness = 0.3f;

        // ------------------------------------------------------------------ pacts & betrayal
        /// <summary>Length of a non-aggression pact.</summary>
        public const float PactYears = 10f;
        /// <summary>Pacts are renewed when they expire sooner than this.</summary>
        public const float PactRenewYears = 2f;
        /// <summary>Minimum Diplomacy of both signatories.</summary>
        public const float PactMinDiplomacy = 0.4f;
        /// <summary>Signatories above this Aggression do not bind themselves.</summary>
        public const float PactMaxAggression = 0.75f;
        /// <summary>Per-year chance of signing at full willingness.</summary>
        public const float PactChancePerYear = 0.6f;
        /// <summary>Opinion impulse on signing.</summary>
        public const float PactImpulse = 6f;
        /// <summary>How long a betrayal is remembered.</summary>
        public const float BetrayalYears = 60f;
        /// <summary>Immediate impulse between the betrayer and the betrayed.</summary>
        public const float BetrayalShock = -35f;
        /// <summary>Immediate impulse between the betrayer and each friend of the betrayed.</summary>
        public const float BetrayalFriendShock = -15f;
        /// <summary>Lasting per-run impulse (fades linearly over <see cref="BetrayalYears"/>) for the betrayed.</summary>
        public const float BetrayalGrudge = -6f;
        /// <summary>Lasting per-run impulse for each friend of the betrayed.</summary>
        public const float BetrayalFriendGrudge = -3f;

        // ------------------------------------------------------------------ royal marriages
        /// <summary>Different races need at least this mutual affinity to intermarry.</summary>
        public const float MarriageMinAffinity = 0.5f;
        /// <summary>Minimum average Diplomacy of the two houses.</summary>
        public const float MarriageMinDiplomacy = 0.35f;
        /// <summary>Different races above this Xenophobia refuse.</summary>
        public const float MarriageMaxXenophobia = 0.6f;
        /// <summary>Per-year chance at full willingness.</summary>
        public const float MarriageChancePerYear = 0.15f;
        /// <summary>Opinion impulse of a royal marriage.</summary>
        public const float MarriageImpulse = 25f;
    }
}
