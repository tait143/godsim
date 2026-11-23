namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys used by <see cref="GrowthAdvisor"/>. Keys it writes live in <see cref="Settlement.Memory"/>
    /// under the <c>adv.growth.</c> prefix (persisted with the save, bounded: a fixed set per settlement).
    /// Keys it only reads are written by other modules (population, economy).
    /// </summary>
    public static class GrowthAdvisorKeys
    {
        // ------------------------------------------------------------------ read (other modules)
        /// <summary>Settlement, written by population housing: free beds in complete dwellings.</summary>
        public const string FreeHousing = "pop.freeHousing";
        /// <summary>Settlement, written by economy: days of food left at current consumption.</summary>
        public const string FoodDays = "econ.foodDays";

        // ------------------------------------------------------------------ written (this advisor)
        /// <summary>Population at the last trend sample.</summary>
        public const string SamplePop = "adv.growth.samplePop";
        /// <summary>Tick of the last trend sample.</summary>
        public const string SampleTick = "adv.growth.sampleTick";
        /// <summary>Number of trend samples taken (capped), drives how much the observed trend is trusted.</summary>
        public const string Samples = "adv.growth.samples";
        /// <summary>EMA of the observed net population change (people per year, includes migration).</summary>
        public const string TrendPerYear = "adv.growth.trend";
        /// <summary>EMA of observed births (surviving newborns per year).</summary>
        public const string BirthsPerYear = "adv.growth.births";
        /// <summary>EMA of observed losses (deaths + emigration per year).</summary>
        public const string LossesPerYear = "adv.growth.losses";
        /// <summary>Projected population one year ahead.</summary>
        public const string Projected1 = "adv.growth.proj1";
        /// <summary>Projected population two years ahead.</summary>
        public const string Projected2 = "adv.growth.proj2";
        /// <summary>Projected population three years ahead.</summary>
        public const string Projected3 = "adv.growth.proj3";
        /// <summary>Beds missing one year ahead (including the tier's spare-bed buffer); negative = surplus.</summary>
        public const string BedDeficit = "adv.growth.bedDeficit";
        /// <summary>Free beds the advisor used in its last run.</summary>
        public const string FreeBeds = "adv.growth.freeBeds";
        /// <summary>Years until every bed (free + under construction) is filled at the projected rate (99 = never).</summary>
        public const string YearsToFull = "adv.growth.yearsToFull";
        /// <summary>Learned water service points needed per resident (from economy coverage).</summary>
        public const string WaterPerCapita = "adv.growth.waterPerCap";
    }
}
