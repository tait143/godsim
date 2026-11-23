namespace GodSim.Core.Population
{
    /// <summary>
    /// Balancing constants for the population lifecycle systems (<see cref="NeedsSystem"/>,
    /// <see cref="AgingSystem"/>, <see cref="BirthSystem"/>, <see cref="FamilySystem"/>).
    /// "PerTick" values are per day and are scaled by <c>ctx.DeltaTicks</c>; "PerYear" values are
    /// probabilities converted with <see cref="Rng.PerYearToPerTicks"/>.
    /// </summary>
    public static class LifecycleBalance
    {
        // ------------------------------------------------------------------ needs / eating
        /// <summary>Food eaten by a child relative to an adult.</summary>
        public const float ChildFoodFactor = 0.6f;
        /// <summary>Food eaten by an elder relative to an adult.</summary>
        public const float ElderFoodFactor = 0.8f;
        /// <summary>Satiation regained per tick while the settlement can feed its people.</summary>
        public const float SatiationGainPerTick = 0.08f;
        /// <summary>Satiation lost per tick at a total shortage; scaled by (1 - fedRatio).</summary>
        public const float SatiationLossPerTick = 0.05f;
        /// <summary>Below this satiation a person is starving and loses health.</summary>
        public const float StarvingSatiation = 0.15f;
        /// <summary>Health lost per tick while starving. Reaching 0 health while starving is fatal.</summary>
        public const float StarvationHealthLossPerTick = 0.01f;
        /// <summary>At or below this satiation a person may die of starvation at random.</summary>
        public const float StarvationDeathSatiation = 0.02f;
        /// <summary>Per-year chance of a starvation death for a person at or below <see cref="StarvationDeathSatiation"/>.</summary>
        public const float StarvationDeathPerYear = 0.6f;
        /// <summary>A settlement whose fed ratio drops below this suffers a famine.</summary>
        public const float FamineRatio = 0.7f;
        /// <summary>Minimum ticks between two <see cref="FamineEvent"/>s of one settlement.</summary>
        public const int FamineEventCooldownTicks = 24;
        /// <summary>Minimum ticks between two famine chronicle entries of one settlement.</summary>
        public const int FamineChronicleCooldownTicks = SimTime.TicksPerYear;
        /// <summary>Per-tick smoothing of the remembered fed ratio (<see cref="LifecycleMemoryKeys.FoodRatio"/>).</summary>
        public const float FoodRatioSmoothingPerTick = 0.05f;

        // ------------------------------------------------------------------ aging / mortality
        /// <summary>Base yearly child mortality, scaled by (1.2 - Health) and health-service coverage.</summary>
        public const float ChildBaseHazard = 0.02f;
        /// <summary>How much full health-service coverage reduces child mortality (0.5 = halves it).</summary>
        public const float ChildHealthCareReduction = 0.5f;
        /// <summary>Yearly mortality of adults before senescence.</summary>
        public const float AdultHazard = 0.003f;
        /// <summary>Senescence (rising old-age mortality) starts at this fraction of life expectancy.</summary>
        public const float SenescenceOnset = 0.6f;
        /// <summary>Base yearly mortality at exactly the life expectancy.</summary>
        public const float HazardAtLifeExpectancy = 0.5f;
        /// <summary>Fraction of life expectancy at which the base hazard reaches <see cref="HazardAtLateAge"/>.</summary>
        public const float LateAgeFraction = 1.3f;
        /// <summary>Base yearly mortality at <see cref="LateAgeFraction"/> of life expectancy.</summary>
        public const float HazardAtLateAge = 0.95f;
        /// <summary>Hard cap: nobody lives longer than this multiple of the effective life expectancy.</summary>
        public const float MaxAgeFactor = 1.5f;
        /// <summary>Adult/elder hazard is multiplied by (HealthHazardOffset - Health).</summary>
        public const float HealthHazardOffset = 1.5f;
        /// <summary>Child hazard is multiplied by (ChildHealthHazardOffset - Health).</summary>
        public const float ChildHealthHazardOffset = 1.2f;
        /// <summary>Mortality multiplier for the Sickly trait.</summary>
        public const float SicklyHazardMultiplier = 1.5f;
        /// <summary>Mortality multiplier for the Hardy trait.</summary>
        public const float HardyHazardMultiplier = 0.7f;

        // ------------------------------------------------------------------ births
        /// <summary>Minimum mother satiation for a pregnancy.</summary>
        public const float MinMotherSatiation = 0.4f;
        /// <summary>Minimum mother health for a pregnancy.</summary>
        public const float MinMotherHealth = 0.3f;
        /// <summary>Food stock (in days of consumption) at or below which <see cref="LowFoodFactor"/> applies.</summary>
        public const float LowFoodDays = 10f;
        /// <summary>Food stock (in days of consumption) at or above which <see cref="HighFoodFactor"/> applies.</summary>
        public const float HighFoodDays = 60f;
        /// <summary>Fertility multiplier when food stocks are low.</summary>
        public const float LowFoodFactor = 0.3f;
        /// <summary>Fertility multiplier when food stocks are plentiful.</summary>
        public const float HighFoodFactor = 1.2f;
        /// <summary>
        /// Housing gate (balance pass 2026-11-19): homeless share at or below which housing does not brake births.
        /// Above it the factor falls linearly to <see cref="CrowdedHousingFactor"/> at <see cref="CrowdedHomelessShare"/>.
        /// </summary>
        public const float HomelessSoftShare = 0.05f;
        /// <summary>Homeless share of the population at or above which <see cref="CrowdedHousingFactor"/> applies (was 0.25, a step).</summary>
        public const float CrowdedHomelessShare = 0.20f;
        /// <summary>Fertility multiplier when too many people are homeless (was 0.4; now the floor of a ramp).</summary>
        public const float CrowdedHousingFactor = 0.1f;
        /// <summary>Fertility multiplier when the settlement has no free bed at all (new: births slow sharply until houses are built).</summary>
        public const float NoFreeHousingFactor = 0.3f;
        /// <summary>Settlements below this many residents (a band that can still build huts) never drop below <see cref="SmallBandHousingFloor"/>.</summary>
        public const int HousingGateMinPopulation = 12;
        /// <summary>Housing-factor floor for small bands (the pre-pass crowding factor), so a houseless camp is never doomed.</summary>
        public const float SmallBandHousingFloor = 0.4f;
        /// <summary>
        /// Civ-wide crowding brake (balance pass 2026-11-19): fertility x 1 / (1 + (civ population / this)^2), so 0.78 at
        /// 400 people, 0.5 at 750 and 0.2 at 1,500. Without it every civ kept growing 2.5-3%/yr and reached 1.6-4.5k by
        /// year 200 (design: about 1,000-1,600 per civ even at year 1,000).
        /// </summary>
        public const float CivPopulationSoftCap = 750f;
        /// <summary>Happiness factor = HappinessFactorBase + HappinessFactorSlope * Happiness.</summary>
        public const float HappinessFactorBase = 0.6f;
        /// <summary>Happiness factor = HappinessFactorBase + HappinessFactorSlope * Happiness.</summary>
        public const float HappinessFactorSlope = 0.8f;
        /// <summary>
        /// Hunger brake on fertility: the settlement's smoothed fed ratio maps linearly from this value
        /// (factor 0) to 1 (factor 1). Keeps the Malthusian equilibrium mildly fed instead of in famine.
        /// </summary>
        public const float HungerFertilityFloorRatio = 0.5f;
        /// <summary>Fertility multiplier for a mother with the Fertile trait.</summary>
        public const float FertileTraitFactor = 1.4f;
        /// <summary>Chance that a non-Saurian birth produces twins.</summary>
        public const float TwinChance = 0.02f;
        /// <summary>Smallest Saurian clutch.</summary>
        public const int SaurianClutchMin = 1;
        /// <summary>Largest Saurian clutch (was 3; balance pass 2026-11-19, Saurians out-grew every other race ~10x).</summary>
        public const int SaurianClutchMax = 2;
        /// <summary>Settlements with fewer couples than this let unpartnered women bear children.</summary>
        public const int TinyTribeCouples = 2;
        /// <summary>Fertility multiplier for an unpartnered woman in a tiny tribe.</summary>
        public const float TinyTribeFactor = 0.3f;

        // ------------------------------------------------------------------ families
        /// <summary>Cross-race marriages need the affinity of both races toward each other above this.</summary>
        public const float CrossRaceMinAffinity = 0.3f;
        /// <summary>Maximum age gap between partners, as a fraction of the (smaller) race life expectancy.</summary>
        public const float MaxAgeGapFraction = 0.25f;
        /// <summary>Pairings allowed per settlement per run: this base ...</summary>
        public const int PairingsPerSettlementBase = 2;
        /// <summary>... plus one per this many residents.</summary>
        public const int ResidentsPerExtraPairing = 20;
        /// <summary>Global cap on pairings in one FamilySystem run.</summary>
        public const int MaxPairingsPerRun = 256;
    }
}
