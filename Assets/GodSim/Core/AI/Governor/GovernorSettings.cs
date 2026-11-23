namespace GodSim.Core.AI
{
    /// <summary>
    /// Tuning knobs of the settlement governor. Defaults implement the shared design rules (free housing
    /// &gt;= max(2, 8% of population), queue of at most 4 orders, Houses stacking up to 2, stale orders
    /// dropped after 3 years, territory rescans every year). Values are sanitized by <see cref="Sanitize"/>.
    /// </summary>
    public sealed class GovernorSettings
    {
        /// <summary>Maximum number of orders in a settlement's build queue (all origins) before the governor stops adding.</summary>
        public int QueueLimit = 4;
        /// <summary>Maximum queued orders of one dwelling type (Houses may stack).</summary>
        public int MaxQueuedDwellingsPerType = 2;
        /// <summary>Maximum queued orders of one non-dwelling type.</summary>
        public int MaxQueuedPerType = 1;
        /// <summary>At most this many new orders per settlement per run.</summary>
        public int MaxOrdersPerRun = 2;
        /// <summary>Minimum utility for the first order of a run.</summary>
        public float OrderThreshold = 0.2f;
        /// <summary>Minimum utility for an order when nothing is queued or under construction (an idle town keeps improving).</summary>
        public float IdleOrderThreshold = 0.12f;
        /// <summary>Minimum utility for additional orders in the same run.</summary>
        public float SecondOrderThreshold = 0.35f;
        /// <summary>Non-wonder orders older than this are dropped (the planner could not place them).</summary>
        public int StaleOrderTicks = 3 * SimTime.TicksPerYear;
        /// <summary>Territory feature cache lifetime.</summary>
        public int TerritoryRefreshTicks = SimTime.TicksPerYear;
        /// <summary>Minimum free housing to keep available.</summary>
        public int MinFreeHousing = 2;
        /// <summary>Free housing to keep as a fraction of population.</summary>
        public float FreeHousingFraction = 0.08f;
        /// <summary>Expected residents of a queued/incomplete dwelling whose definition gives no range.</summary>
        public float DefaultDwellingCapacity = 4f;
        /// <summary>How far ahead (years) population growth is anticipated when sizing housing.</summary>
        public float HousingLeadYears = 0.25f;
        /// <summary>Hostile settlements closer than this (tiles) raise the defense need.</summary>
        public int ThreatRadius = 40;
        /// <summary>A non-dwelling type ordered within this many ticks is penalised (prevents rapid repeats).</summary>
        public int RepeatCooldownTicks = SimTime.TicksPerYear / 2;
        /// <summary>A type whose order the planner kept failing to place is not re-ordered for this long.</summary>
        public int PlacementBlockTicks = SimTime.TicksPerYear;
        /// <summary>Planner attempts after which an order's type is considered hard to place.</summary>
        public int PlacementFailureAttempts = 2;
        /// <summary>Queue-full eviction: minimum utility of the new candidate.</summary>
        public float EvictionUtility = 0.7f;
        /// <summary>Queue-full eviction: priority margin over the evicted governor order.</summary>
        public float EvictionMargin = 3f;

        /// <summary>Utility added per unit of Advisor Council build bias (<see cref="AdvisorBoard.BuildBias"/>).</summary>
        public float AdvisorBiasScale = 0.35f;
        /// <summary>Council bias at or below this vetoes a candidate unless a need it serves is critical.</summary>
        public float AdvisorVetoBias = -1f;
        /// <summary>A need at or above this is critical: it overrides negative council advice.</summary>
        public float CriticalNeed = 0.8f;
        /// <summary>With no new building for this long while unaffordable governor orders wait, the least urgent is dropped.</summary>
        public int StallTicks = SimTime.TicksPerYear;

        /// <summary>Clamps every field to a safe range. Returns this for chaining.</summary>
        public GovernorSettings Sanitize()
        {
            QueueLimit = MathUtil.Clamp(QueueLimit, 1, 32);
            MaxQueuedDwellingsPerType = MathUtil.Clamp(MaxQueuedDwellingsPerType, 1, QueueLimit);
            MaxQueuedPerType = MathUtil.Clamp(MaxQueuedPerType, 1, QueueLimit);
            MaxOrdersPerRun = MathUtil.Clamp(MaxOrdersPerRun, 1, QueueLimit);
            OrderThreshold = MathUtil.Clamp(MathUtil.Sanitize(OrderThreshold, 0.2f), 0f, 10f);
            IdleOrderThreshold = MathUtil.Clamp(MathUtil.Sanitize(IdleOrderThreshold, 0.12f), 0f, OrderThreshold);
            SecondOrderThreshold = MathUtil.Clamp(MathUtil.Sanitize(SecondOrderThreshold, 0.35f), OrderThreshold, 10f);
            StaleOrderTicks = MathUtil.Clamp(StaleOrderTicks, SimTime.TicksPerSeason, 100 * SimTime.TicksPerYear);
            TerritoryRefreshTicks = MathUtil.Clamp(TerritoryRefreshTicks, 6, 10 * SimTime.TicksPerYear);
            MinFreeHousing = MathUtil.Clamp(MinFreeHousing, 0, 100);
            FreeHousingFraction = MathUtil.Clamp(MathUtil.Sanitize(FreeHousingFraction, 0.08f), 0f, 1f);
            DefaultDwellingCapacity = MathUtil.Clamp(MathUtil.Sanitize(DefaultDwellingCapacity, 4f), 1f, 1000f);
            HousingLeadYears = MathUtil.Clamp(MathUtil.Sanitize(HousingLeadYears, 0.25f), 0f, 5f);
            ThreatRadius = MathUtil.Clamp(ThreatRadius, 1, 1024);
            RepeatCooldownTicks = MathUtil.Clamp(RepeatCooldownTicks, 0, 10 * SimTime.TicksPerYear);
            PlacementBlockTicks = MathUtil.Clamp(PlacementBlockTicks, 0, 10 * SimTime.TicksPerYear);
            PlacementFailureAttempts = MathUtil.Clamp(PlacementFailureAttempts, 1, 100);
            EvictionUtility = MathUtil.Clamp(MathUtil.Sanitize(EvictionUtility, 0.7f), 0f, 10f);
            EvictionMargin = MathUtil.Clamp(MathUtil.Sanitize(EvictionMargin, 3f), 0f, 100f);
            AdvisorBiasScale = MathUtil.Clamp(MathUtil.Sanitize(AdvisorBiasScale, 0.35f), 0f, 5f);
            AdvisorVetoBias = MathUtil.Clamp(MathUtil.Sanitize(AdvisorVetoBias, -1f), -AdvisorBoard.MaxTotal - 1f, 0f);
            CriticalNeed = MathUtil.Clamp(MathUtil.Sanitize(CriticalNeed, 0.8f), 0f, 1f);
            StallTicks = MathUtil.Clamp(StallTicks, SimTime.TicksPerSeason, 20 * SimTime.TicksPerYear);
            return this;
        }
    }
}
