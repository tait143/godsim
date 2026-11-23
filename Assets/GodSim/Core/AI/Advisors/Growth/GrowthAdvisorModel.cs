using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Tuning constants and pure, allocation-free math of the growth advisor (demographic projection,
    /// food dampening, tier rhythm, data-driven building facts). Kept separate so it is easy to test and balance.
    /// </summary>
    public static class GrowthAdvisorModel
    {
        /// <summary>Beds expected from a dwelling whose capacity is not rolled yet ("expect 4 each").</summary>
        public const float DefaultBedsPerDwelling = 4f;
        /// <summary>Water points per resident assumed until the economy's coverage lets the advisor learn the real ratio.</summary>
        public const float DefaultWaterPerCapita = 0.12f;
        /// <summary>Background mortality per person per year (accidents, disease, raids).</summary>
        public const float BaselineMortality = 0.006f;
        /// <summary>Share of unpartnered fertile women expected to pair up and give birth within the horizon.</summary>
        public const float SingleMotherFactor = 0.25f;
        /// <summary>Average children per Saurian birth (clutches).</summary>
        public const float SaurianClutch = 1.5f; // balance pass 2026-11-19: clutches are 1-2 eggs (LifecycleBalance), was 2 for 1-3
        /// <summary>Average children per non-Saurian birth (twins).</summary>
        public const float TwinFactor = 1.02f;
        /// <summary>Projected yearly growth rate is clamped to [-MaxDeclineRate, MaxGrowthRate].</summary>
        public const float MaxGrowthRate = 0.6f;
        public const float MaxDeclineRate = 0.5f;
        /// <summary>EMA time constant of the observed trend, in years.</summary>
        public const float TrendTimeConstantYears = 1f;
        /// <summary>Samples after which the observed trend gets its full blend weight.</summary>
        public const int SamplesForFullTrust = 6;
        /// <summary>Maximum blend weight of the observed trend vs. the demographic model.</summary>
        public const float MaxTrendTrust = 0.5f;
        /// <summary>Population from which water starts to matter.</summary>
        public const int MinWaterPopulation = 8;
        /// <summary>Projected water coverage below which the advisor asks for water.</summary>
        public const float WaterCoverageTarget = 0.9f;

        /// <summary>0 when food would run out within ~10 days, 1 with 45+ days of food. Dampens growth-driven votes.</summary>
        public static float FoodGrowthFactor(float foodDays) => MathUtil.SmoothStep(10f, 45f, MathUtil.Sanitize(foodDays, 0f));

        /// <summary>Compounds a yearly net change (people/year at the current size) over whole years.</summary>
        public static float Project(int population, float netPerYear, int years)
        {
            if (population <= 0) return 0f;
            float r = MathUtil.Clamp(MathUtil.Sanitize(netPerYear) / population, -MaxDeclineRate, MaxGrowthRate);
            float p = population;
            for (int k = 0; k < years; k++) p *= 1f + r;
            return Math.Max(0f, MathUtil.Sanitize(p, population));
        }

        /// <summary>Expected births per year from the mothers available now.</summary>
        public static float ModelBirths(float mothers, float fertilityPerYear, float growthModifier, float childrenPerBirth, float foodFactor)
        {
            float v = Math.Max(0f, mothers) * Math.Max(0f, fertilityPerYear) * MathUtil.Clamp(growthModifier, 0f, 3f)
                      * Math.Max(0f, childrenPerBirth) * MathUtil.Lerp(0.2f, 1f, foodFactor);
            return MathUtil.Sanitize(v);
        }

        /// <summary>Expected deaths per year: elders die over their remaining life expectancy, plus background mortality.</summary>
        public static float ModelDeaths(int population, int elders, float lifespanYears, float elderAge)
        {
            float remaining = Math.Max(3f, MathUtil.Sanitize(lifespanYears, 70f) - MathUtil.Sanitize(elderAge, 55f));
            return MathUtil.Sanitize(Math.Max(0, elders) / remaining + Math.Max(0, population) * BaselineMortality);
        }

        /// <summary>EMA blend factor for a sample covering dtYears (rational form, no exp).</summary>
        public static float EmaAlpha(float dtYears)
        {
            float dt = Math.Max(0f, MathUtil.Sanitize(dtYears));
            return dt / (dt + TrendTimeConstantYears);
        }

        /// <summary>How many dwellings may be pending at once before pre-building pauses (smooth rhythm, no bursts).</summary>
        public static int MaxPendingDwellings(SettlementTier tier) => 1 + (int)tier / 2;

        /// <summary>Spare beds a settlement of this tier likes to keep for new couples and migrants.</summary>
        public static int SpareBedBuffer(SettlementTier tier) => tier <= SettlementTier.Hamlet ? 1 : (tier <= SettlementTier.Town ? 2 : 3);

        /// <summary>Expected residents of a dwelling definition (mean of its housing roll).</summary>
        public static float ExpectedBeds(BuildingDefinition def)
        {
            if (def == null || def.HousingMax <= 0) return DefaultBedsPerDwelling;
            return Math.Max(1f, (Math.Max(0, def.HousingMin) + def.HousingMax) * 0.5f);
        }

        /// <summary>Water points a definition provides (sum of its Water services).</summary>
        public static float WaterAmount(BuildingDefinition def)
        {
            if (def == null || def.Services == null) return 0f;
            float w = 0f;
            for (int i = 0; i < def.Services.Length; i++)
                if (def.Services[i].Type == ServiceType.Water) w += Math.Max(0f, MathUtil.Sanitize(def.Services[i].Amount));
            return w;
        }

        /// <summary>True if Water is the definition's largest service (wells, aqueducts; not bathhouses).</summary>
        public static bool IsPrimaryWater(BuildingDefinition def)
        {
            float water = WaterAmount(def);
            if (water <= 0f) return false;
            for (int i = 0; i < def.Services.Length; i++)
                if (def.Services[i].Type != ServiceType.Water && def.Services[i].Amount > water) return false;
            return true;
        }

        /// <summary>"+7" / "-2" for reason strings.</summary>
        public static string Signed(float v)
        {
            int n = MathUtil.RoundToInt(MathUtil.Sanitize(v));
            return n >= 0 ? "+" + n : n.ToString();
        }
    }
}
