using System;

namespace GodSim.Core.AI
{
    /// <summary>Settlement memory keys owned by the <see cref="HealthAdvisor"/> (all floats, persisted with the save).</summary>
    public static class HealthAdvisorKeys
    {
        /// <summary>Disease deaths counted by the event handler since the last advisor run (consumed each run).</summary>
        public const string PendingDeaths = "adv.health.pend";
        /// <summary>Exponential moving average of disease deaths per run (one run = one season).</summary>
        public const string DeathEma = "adv.health.ema";
        /// <summary>Disease deaths of the most recent outbreak season (for the "3 died of fever" explanation).</summary>
        public const string LastSeasonDeaths = "adv.health.last";
        /// <summary>Post-outbreak alarm 0..1 that decays every run.</summary>
        public const string Spike = "adv.health.spike";
        /// <summary>Plague reported nearby (DisasterEvent Plague), 0..1.5, decays every run.</summary>
        public const string Plague = "adv.health.plague";
        /// <summary>Water-borne hazard from floods/droughts nearby, 0..1.5, decays every run.</summary>
        public const string WaterHazard = "adv.health.wdis";
        /// <summary>Injury/weakness hazard from quakes, fires, famine nearby, 0..1.5, decays every run.</summary>
        public const string InjuryHazard = "adv.health.hdis";
        /// <summary>Last computed intrinsic epidemic risk 0..1 (UI/debug).</summary>
        public const string Risk = "adv.health.risk";
        /// <summary>Population at the last run (growth anticipation).</summary>
        public const string LastPopulation = "adv.health.pop";
    }

    /// <summary>Raw, already-surveyed facts about one settlement fed into <see cref="HealthRiskModel.Evaluate"/>.</summary>
    public struct HealthRiskInputs
    {
        public int Population;
        public int BuildingCount;
        /// <summary>Average resident health 0..1.</summary>
        public float AvgHealth;
        /// <summary>Share of residents with health below 0.5 (early warning: the sick today are the dead tomorrow).</summary>
        public float SickFraction;
        /// <summary>Share of children, elders and sickly residents.</summary>
        public float VulnerableFraction;
        /// <summary>Average race DiseaseResistance of residents (1 = baseline).</summary>
        public float DiseaseResistance;
        /// <summary>civ.Modifiers.HealthBonus (0..1 additive).</summary>
        public float HealthBonus;
        public float WaterCoverage;
        public float HealthCoverage;
        /// <summary>Population gained since the last run (anticipate coverage dilution).</summary>
        public float GrowthPerRun;
        public float DeathEma;
        public float Spike;
        public float Plague;
        public float WaterHazard;
        public float InjuryHazard;
        /// <summary>Outbreak pressure from nearby settlements 0..1.</summary>
        public float Neighbor;
        public Season Season;
        public bool Drought;
        /// <summary>True when a water building can be voted for (enables the "water first" rule).</summary>
        public bool WaterBuildable;
    }

    /// <summary>Explainable breakdown of a settlement's epidemic risk and what it needs (all values finite, mostly 0..1).</summary>
    public struct HealthRiskReport
    {
        public float Density, DensityRisk, CrowdRisk, Sickness, Vulnerability, RaceFactor, EpidemicMemory, Seasonal, Climate;
        public float Spike, Plague, Neighbor, WaterHazard, InjuryHazard;
        /// <summary>Intrinsic epidemic risk 0..1 (independent of coverage).</summary>
        public float Risk;
        /// <summary>Coverage projected two seasons ahead (growth-diluted).</summary>
        public float WaterCoverage, HealthCoverage;
        public float WaterGap, HealthGap;
        /// <summary>Recommended vote strength 0..1 before personality/weight.</summary>
        public float WaterNeed, HealthNeed;
        public float Urgency;
        public bool Growing;
    }

    /// <summary>
    /// Pure, deterministic epidemic risk model. Structural exposure (crowding, town size, frail residents, race
    /// resistance, epidemic memory) is combined with acute signals (a recent outbreak, plague or disasters nearby,
    /// sick neighbors, season) and compared with coverage projected two seasons ahead, so towns build wells and
    /// healers before the fever arrives rather than after.
    /// </summary>
    public static class HealthRiskModel
    {
        public static HealthRiskReport Evaluate(in HealthRiskInputs x)
        {
            var r = new HealthRiskReport();
            int pop = Math.Max(0, x.Population);
            int bcount = Math.Max(0, x.BuildingCount);

            r.Density = pop / (float)(bcount + 1);
            r.DensityRisk = MathUtil.InverseLerp(2.5f, 8f, r.Density);
            r.CrowdRisk = MathUtil.InverseLerp(6f, 60f, pop);
            float health = MathUtil.Clamp01(MathUtil.Sanitize(x.AvgHealth, 1f));
            r.Sickness = Math.Max(MathUtil.InverseLerp(0.85f, 0.4f, health), MathUtil.InverseLerp(0.05f, 0.3f, MathUtil.Clamp01(MathUtil.Sanitize(x.SickFraction))));
            r.Vulnerability = MathUtil.InverseLerp(0.2f, 0.6f, MathUtil.Clamp01(MathUtil.Sanitize(x.VulnerableFraction)));
            float resist = MathUtil.Clamp(MathUtil.Sanitize(x.DiseaseResistance, 1f), 0.1f, 10f);
            r.RaceFactor = MathUtil.Clamp(1f / resist, 0.6f, 1.6f);
            float ema = MathUtil.Clamp(MathUtil.Sanitize(x.DeathEma), 0f, 10000f);
            r.EpidemicMemory = MathUtil.Clamp01(ema / Math.Max(1f, pop * 0.04f));
            r.Seasonal = x.Season == Season.Winter ? 0.08f : (x.Season == Season.Autumn ? 0.05f : 0f); // anticipate winter fevers
            r.Climate = (x.Season == Season.Summer ? 0.1f : 0f) + (x.Drought ? 0.2f : 0f);           // heat and drought foul water

            r.Spike = MathUtil.Clamp01(MathUtil.Sanitize(x.Spike));
            r.Plague = MathUtil.Clamp01(MathUtil.Sanitize(x.Plague));
            r.Neighbor = MathUtil.Clamp01(MathUtil.Sanitize(x.Neighbor));
            r.WaterHazard = MathUtil.Clamp01(MathUtil.Sanitize(x.WaterHazard));
            r.InjuryHazard = MathUtil.Clamp01(MathUtil.Sanitize(x.InjuryHazard));

            float bonus = MathUtil.Clamp(MathUtil.Sanitize(x.HealthBonus), 0f, 0.6f);
            float structural = 0.35f * r.DensityRisk + 0.12f * r.CrowdRisk + 0.20f * r.Sickness + 0.10f * r.Vulnerability + 0.23f * r.EpidemicMemory;
            structural *= r.RaceFactor * (1f - 0.5f * bonus);
            r.Risk = MathUtil.Clamp01(structural + 0.45f * r.Plague + 0.25f * r.InjuryHazard + 0.30f * r.Neighbor + r.Seasonal);

            // Anticipation: coverage is per capita, so growth dilutes it before the economy notices.
            float growth = MathUtil.Clamp(MathUtil.Sanitize(x.GrowthPerRun), 0f, 10000f);
            float projected = pop + 2f * growth;
            float dilution = pop <= 0 ? 1f : pop / Math.Max(1f, projected);
            r.Growing = growth >= 1f && dilution < 0.97f;
            r.WaterCoverage = MathUtil.Clamp(MathUtil.Sanitize(x.WaterCoverage), 0f, 3f) * dilution;
            r.HealthCoverage = MathUtil.Clamp(MathUtil.Sanitize(x.HealthCoverage), 0f, 3f) * dilution;
            r.WaterGap = 1f - MathUtil.Clamp01(r.WaterCoverage);
            r.HealthGap = 1f - MathUtil.Clamp01(r.HealthCoverage);

            float sizeScale = 0.25f + 0.75f * MathUtil.InverseLerp(1f, 8f, pop); // tiny camps barely care
            r.WaterNeed = MathUtil.Clamp01(r.WaterGap * MathUtil.Clamp01(0.30f + 0.70f * r.Risk + 0.40f * r.WaterHazard + r.Climate) * sizeScale);
            r.HealthNeed = MathUtil.Clamp01(r.HealthGap * MathUtil.Clamp01(0.20f + 0.80f * r.Risk + 0.60f * r.Spike + 0.30f * r.InjuryHazard) * sizeScale);
            // Water first: clean water prevents more disease per coin than healers, unless people are already dying.
            if (x.WaterBuildable && r.WaterGap > 0.4f && r.Spike < 0.3f) r.HealthNeed *= 0.7f;

            float covGap = Math.Max(r.WaterGap, r.HealthGap);
            float acute = Math.Max(r.Spike, Math.Max(0.8f * r.Plague, 0.6f * r.Neighbor));
            r.Urgency = MathUtil.Clamp01(Math.Max(r.Risk * covGap, acute));
            if (pop <= 0) { r.WaterNeed = 0f; r.HealthNeed = 0f; r.Urgency = 0f; }
            return r;
        }
    }
}
