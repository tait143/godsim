namespace GodSim.Core.AI
{
    /// <summary>
    /// Tunables for <see cref="InfrastructureAdvisor"/>. Plain fields so designers (and tests) can tweak them;
    /// every value is sanitized at the point of use, so a bad edit cannot inject NaN into settlement memory.
    /// </summary>
    public sealed class InfraSettings
    {
        // ------------------------------------------------------------------ anticipation
        /// <summary>How far ahead (ticks) the advisor projects population growth when judging coverage (2 years).</summary>
        public float LookaheadTicks = 192f;
        /// <summary>Smoothing (0..1) of the per-tick population growth estimate kept in settlement memory.</summary>
        public float GrowthSmoothing = 0.35f;
        /// <summary>Fuel runway horizon (ticks): stock + net income over this window must cover a plant's demand.</summary>
        public float FuelHorizonTicks = 192f;
        /// <summary>Assumed staffing share for buildings that are planned or currently have no workers.</summary>
        public float AssumedStaffing = 0.5f;

        // ------------------------------------------------------------------ coverage targets
        public float WaterTarget = 1f;
        /// <summary>Extra water coverage wanted while a drought lasts.</summary>
        public float DroughtWaterMargin = 0.2f;
        /// <summary>Vote multiplier for water while a drought lasts.</summary>
        public float DroughtWeight = 1.2f;
        public float AdminTarget = 0.9f;
        /// <summary>The +25% factory bonus is binary at coverage 1, so aim slightly above it.</summary>
        public float PowerTarget = 1.05f;
        public float HubTarget = 1.1f;
        /// <summary>Coverage (current + pending) at or above which more providers get a negative vote.</summary>
        public float OverCoverage = 1.2f;

        // ------------------------------------------------------------------ vote shaping
        public float OverVoteBase = 0.3f;
        public float OverVoteSlope = 0.8f;
        public float OverVoteMax = 0.8f;
        public float BaseNeedVote = 0.25f;
        public float NeedVoteSlope = 1.1f;
        /// <summary>Coverage beyond the gap that still counts as useful when ranking providers.</summary>
        public float UsefulMargin = 0.25f;
        /// <summary>Share of the main vote given to the runner-up when the best provider cannot close the gap alone.</summary>
        public float SecondChoiceShare = 0.45f;
        /// <summary>How much an understaffed-provider gap damps new construction (staff first, build second).</summary>
        public float StaffingDamp = 0.6f;
        /// <summary>Extra vote strength after a provider was lost (disaster, fire, war).</summary>
        public float RebuildBoost = 0.3f;
        /// <summary>How long (ticks) a lost provider keeps the rebuild boost alive.</summary>
        public long RebuildMemoryTicks = 192;
        /// <summary>Production bonus factories get at full power (economy: x1.25).</summary>
        public float PowerBonus = 0.25f;
        /// <summary>Power benefit (extra units/tick) at which the power vote saturates.</summary>
        public float PowerBenefitRef = 0.3f;
        public float NoIndustryPenalty = 0.25f;
        public float NoFuelPenalty = 0.4f;
        /// <summary>Normalizer for construction cost (resources + labor + upkeep) in provider ranking.</summary>
        public float CostScale = 150f;
        public float MinVote = 0.02f;
        /// <summary>Tiles beyond the territory radius searched for fuel deposits.</summary>
        public int DepositSearchMargin = 3;

        // ------------------------------------------------------------------ gates
        public AgeId PowerMinAge = AgeId.Industrial;
        /// <summary>Commerce providers from this age on count as transport hubs (RailStation, Airport).</summary>
        public AgeId HubMinAge = AgeId.Industrial;
        public SettlementTier HubMinTier = SettlementTier.Town;

        /// <summary>
        /// Fallback per-resident service need (points per tick for coverage 1), used only when the economy has not yet
        /// published output and coverage (mirrors the economy defaults). Indexed by (int)ServiceType.
        /// </summary>
        public readonly float[] NeedPerCapita = DefaultNeeds();

        /// <summary>Sanitized fallback need for a service (never NaN, never below 0.0001).</summary>
        public float NeedFor(ServiceType svc)
        {
            int i = (int)svc;
            if (i < 0 || i >= NeedPerCapita.Length) return 0.05f;
            float v = MathUtil.Sanitize(NeedPerCapita[i], 0.05f);
            return v < 0.0001f ? 0.0001f : v;
        }

        private static float[] DefaultNeeds()
        {
            var a = new float[Counts.Services];
            for (int i = 0; i < a.Length; i++) a[i] = 0.05f;
            a[(int)ServiceType.Research] = 0.03f;
            a[(int)ServiceType.Defense] = 0.04f;
            a[(int)ServiceType.Water] = 0.1f;
            a[(int)ServiceType.Power] = 0.15f;
            a[(int)ServiceType.Commerce] = 0.04f;
            a[(int)ServiceType.Administration] = 0.04f;
            return a;
        }
    }
}
