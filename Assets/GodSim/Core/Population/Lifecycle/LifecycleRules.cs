using System;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Pure, allocation-free formulas shared by the lifecycle systems (eating, mortality, fertility,
    /// partner compatibility). Every input is sanitized, so no NaN/Infinity can leak into state.
    /// </summary>
    public static class LifecycleRules
    {
        /// <summary>Number of life stages (Child, Adult, Elder).</summary>
        public const int StageCount = 3;

        /// <summary>Size of a food-need table filled by <see cref="FillFoodNeedTable"/> (races x stages).</summary>
        public static readonly int FoodNeedTableSize = Counts.Races * StageCount;

        // Gompertz rates, in units of (age / lifeExpectancy).
        private static readonly float EarlySenescenceRate =
            (float)(Math.Log(LifecycleBalance.HazardAtLifeExpectancy / LifecycleBalance.AdultHazard) / (1.0 - LifecycleBalance.SenescenceOnset));
        private static readonly float LateSenescenceRate =
            (float)(Math.Log(LifecycleBalance.HazardAtLateAge / LifecycleBalance.HazardAtLifeExpectancy) / (LifecycleBalance.LateAgeFraction - 1.0));

        // ------------------------------------------------------------------ lookups
        /// <summary>Race definition for an id, or null when the id is out of range or unregistered (never throws).</summary>
        public static RaceDefinition RaceOf(GameDatabase db, RaceId race)
        {
            if (db == null) return null;
            int i = (int)race;
            var races = db.Races;
            return i >= 0 && i < races.Count ? races[i] : null;
        }

        /// <summary>Food-need multiplier for a life stage (children 0.6x, elders 0.8x).</summary>
        public static float FoodFactor(LifeStage stage)
        {
            switch (stage)
            {
                case LifeStage.Child: return LifecycleBalance.ChildFoodFactor;
                case LifeStage.Elder: return LifecycleBalance.ElderFoodFactor;
                default: return 1f;
            }
        }

        /// <summary>Fills <paramref name="table"/> (length >= <see cref="FoodNeedTableSize"/>) with food per tick by race and stage.</summary>
        public static void FillFoodNeedTable(GameDatabase db, float[] table)
        {
            if (table == null) return;
            for (int r = 0; r < Counts.Races; r++)
            {
                var def = RaceOf(db, (RaceId)r);
                float perTick = def != null ? Math.Max(0f, MathUtil.Sanitize(def.FoodPerTick)) : 0f;
                for (int st = 0; st < StageCount; st++)
                {
                    int idx = r * StageCount + st;
                    if (idx < table.Length) table[idx] = perTick * FoodFactor((LifeStage)st);
                }
            }
        }

        /// <summary>Food per tick a person needs, read from a table filled by <see cref="FillFoodNeedTable"/>. 0 for invalid data.</summary>
        public static float FoodNeedOf(float[] table, Person p)
        {
            if (table == null || p == null) return 0f;
            int r = (int)p.Race, st = (int)p.Stage;
            if (r < 0 || r >= Counts.Races || st < 0 || st >= StageCount) return 0f;
            int idx = r * StageCount + st;
            return idx < table.Length ? table[idx] : 0f;
        }

        // ------------------------------------------------------------------ needs
        /// <summary>
        /// Moves satiation toward the fed ratio: up by 0.08/tick when food allows, down by
        /// (1 - ratio) * 0.05/tick when short. Result is clamped to 0..1.
        /// </summary>
        public static float StepSatiation(float current, float fedRatio, int deltaTicks)
        {
            float cur = MathUtil.Clamp01(MathUtil.Sanitize(current, 1f));
            float ratio = MathUtil.Clamp01(MathUtil.Sanitize(fedRatio, 0f));
            int dt = Math.Max(1, deltaTicks);
            if (cur < ratio) return Math.Min(ratio, cur + LifecycleBalance.SatiationGainPerTick * dt);
            if (cur > ratio) return Math.Max(ratio, cur - (1f - ratio) * LifecycleBalance.SatiationLossPerTick * dt);
            return cur;
        }

        // ------------------------------------------------------------------ mortality
        /// <summary>Race life expectancy plus the civilization's lifespan bonus (years, at least adulthood + 1).</summary>
        public static float EffectiveLifeExpectancy(RaceDefinition race, Civilization civ)
        {
            if (race == null) return 1f;
            float l = MathUtil.Sanitize(race.LifeExpectancy, 70f);
            if (civ != null) l += MathUtil.Sanitize(civ.Modifiers.LifespanBonusYears);
            float min = Math.Max(1f, MathUtil.Sanitize(race.AdulthoodAge, 1f) + 1f);
            return l < min ? min : l;
        }

        /// <summary>
        /// Base (health- and trait-independent) yearly hazard for an adult or elder at
        /// <paramref name="ageFraction"/> = age / life expectancy: 0.003 until 0.6, then a Gompertz rise
        /// reaching 0.5 at 1.0 and 0.95 at 1.3, and still rising beyond.
        /// </summary>
        public static float SenescenceHazard(float ageFraction)
        {
            float x = MathUtil.Sanitize(ageFraction);
            if (x < LifecycleBalance.SenescenceOnset) return LifecycleBalance.AdultHazard;
            if (x < 1f) return LifecycleBalance.AdultHazard * (float)Math.Exp(EarlySenescenceRate * (x - LifecycleBalance.SenescenceOnset));
            if (x > 3f) x = 3f; // keep exp() bounded; the hazard is far above 1 anyway
            return LifecycleBalance.HazardAtLifeExpectancy * (float)Math.Exp(LateSenescenceRate * (x - 1f));
        }

        /// <summary>
        /// Yearly probability of a natural death (may exceed 1 for the very old; callers convert with
        /// <see cref="Rng.PerYearToPerTicks"/>, which clamps). Children: 0.02 * (1.2 - health) *
        /// (1 - 0.5 * min(1, healthCoverage)). Adults/elders: <see cref="SenescenceHazard"/> * (1.5 - health).
        /// Sickly x1.5, Hardy x0.7.
        /// </summary>
        public static float MortalityPerYear(LifeStage stage, float ageYears, float lifeExpectancy, float health, PersonTraits traits, float healthCoverage)
        {
            float l = Math.Max(1f, MathUtil.Sanitize(lifeExpectancy, 1f));
            float x = Math.Max(0f, MathUtil.Sanitize(ageYears)) / l;
            float h = MathUtil.Clamp01(MathUtil.Sanitize(health, 1f));
            float hazard;
            if (stage == LifeStage.Child && x < LifecycleBalance.SenescenceOnset)
            {
                float cov = MathUtil.Clamp01(MathUtil.Sanitize(healthCoverage));
                hazard = LifecycleBalance.ChildBaseHazard * (LifecycleBalance.ChildHealthHazardOffset - h) * (1f - LifecycleBalance.ChildHealthCareReduction * cov);
            }
            else
            {
                hazard = SenescenceHazard(x) * (LifecycleBalance.HealthHazardOffset - h);
            }
            if ((traits & PersonTraits.Sickly) != 0) hazard *= LifecycleBalance.SicklyHazardMultiplier;
            if ((traits & PersonTraits.Hardy) != 0) hazard *= LifecycleBalance.HardyHazardMultiplier;
            hazard = MathUtil.Sanitize(hazard);
            return hazard < 0f ? 0f : hazard;
        }

        /// <summary>Natural-death cause: OldAge from 0.6 x life expectancy on, otherwise Disease.</summary>
        public static DeathCause NaturalDeathCause(float ageYears, float lifeExpectancy) =>
            ageYears >= LifecycleBalance.SenescenceOnset * lifeExpectancy ? DeathCause.OldAge : DeathCause.Disease;

        // ------------------------------------------------------------------ fertility
        /// <summary>Fertility factor from food stock in days of consumption: 0.3 below 10 days, 1.2 above 60, linear between.</summary>
        public static float FoodStockFactor(float daysOfFood)
        {
            float d = MathUtil.Sanitize(daysOfFood);
            if (d <= LifecycleBalance.LowFoodDays) return LifecycleBalance.LowFoodFactor;
            if (d >= LifecycleBalance.HighFoodDays) return LifecycleBalance.HighFoodFactor;
            float t = (d - LifecycleBalance.LowFoodDays) / (LifecycleBalance.HighFoodDays - LifecycleBalance.LowFoodDays);
            return MathUtil.Lerp(LifecycleBalance.LowFoodFactor, LifecycleBalance.HighFoodFactor, t);
        }

        /// <summary>
        /// Fertility factor from the settlement's smoothed fed ratio: 0 at or below
        /// <see cref="LifecycleBalance.HungerFertilityFloorRatio"/>, rising linearly to 1 when fully fed.
        /// </summary>
        public static float HungerFactor(float fedRatio) =>
            MathUtil.InverseLerp(LifecycleBalance.HungerFertilityFloorRatio, 1f, MathUtil.Clamp01(MathUtil.Sanitize(fedRatio, 1f)));

        /// <summary>Days of food in stock for a consumption per tick (a huge value when nobody eats).</summary>
        public static float DaysOfFood(float stock, float consumptionPerTick)
        {
            float s = Math.Max(0f, MathUtil.Sanitize(stock));
            float c = MathUtil.Sanitize(consumptionPerTick);
            if (c <= MathUtil.Epsilon) return s > 0f ? 1e6f : 0f;
            return Math.Min(1e6f, s / c);
        }

        /// <summary>
        /// Housing gate on fertility: 1 while the settlement has a free bed and at most
        /// <see cref="LifecycleBalance.HomelessSoftShare"/> of its people are homeless; falls linearly to
        /// <see cref="LifecycleBalance.CrowdedHousingFactor"/> at <see cref="LifecycleBalance.CrowdedHomelessShare"/>,
        /// times <see cref="LifecycleBalance.NoFreeHousingFactor"/> when no bed is free. Bands smaller than
        /// <see cref="LifecycleBalance.HousingGateMinPopulation"/> keep at least <see cref="LifecycleBalance.SmallBandHousingFloor"/>.
        /// </summary>
        public static float HousingFactor(int homeless, int population, int freeBeds)
        {
            if (population <= 0) return 1f;
            float share = MathUtil.Clamp01(homeless / (float)population);
            float factor = 1f;
            if (share > LifecycleBalance.HomelessSoftShare)
            {
                float t = MathUtil.InverseLerp(LifecycleBalance.HomelessSoftShare, LifecycleBalance.CrowdedHomelessShare, share);
                factor = MathUtil.Lerp(1f, LifecycleBalance.CrowdedHousingFactor, t);
            }
            if (freeBeds <= 0) factor *= LifecycleBalance.NoFreeHousingFactor;
            if (population < LifecycleBalance.HousingGateMinPopulation && factor < LifecycleBalance.SmallBandHousingFloor)
                factor = LifecycleBalance.SmallBandHousingFloor;
            return factor;
        }

        /// <summary>Civ-wide crowding brake: 1 / (1 + (civPopulation / <see cref="LifecycleBalance.CivPopulationSoftCap"/>)^2).</summary>
        public static float CivCrowdingFactor(int civPopulation)
        {
            if (civPopulation <= 0) return 1f;
            float x = civPopulation / LifecycleBalance.CivPopulationSoftCap;
            return 1f / (1f + x * x);
        }

        /// <summary>Living residents of the civ's alive settlements (0 for a null civ).</summary>
        public static int CivPopulation(WorldState state, Civilization civ)
        {
            if (state == null || civ == null) return 0;
            int pop = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var st = state.Settlements.Get(ids[i]);
                if (st != null && st.IsAlive && st.CivilizationId == civ.Id) pop += st.Population;
            }
            return pop;
        }

        /// <summary>Free beds in the settlement's complete, standing buildings.</summary>
        public static int FreeBeds(WorldState state, Settlement s)
        {
            if (state == null || s == null) return 0;
            int free = 0;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed || !b.IsComplete) continue;
                free += b.FreeHousing;
            }
            return free;
        }

        /// <summary>Fertility factor from happiness: 0.6 + 0.8 * happiness.</summary>
        public static float HappinessFactor(float happiness) =>
            LifecycleBalance.HappinessFactorBase + LifecycleBalance.HappinessFactorSlope * MathUtil.Clamp01(MathUtil.Sanitize(happiness, 0.5f));

        // ------------------------------------------------------------------ families
        /// <summary>True for the same race, or when both races' affinity toward each other exceeds 0.3.</summary>
        public static bool AreCompatibleRaces(GameDatabase db, RaceId a, RaceId b)
        {
            if (a == b) return true;
            var ra = RaceOf(db, a);
            var rb = RaceOf(db, b);
            if (ra == null || rb == null) return false;
            return Affinity(ra, b) > LifecycleBalance.CrossRaceMinAffinity && Affinity(rb, a) > LifecycleBalance.CrossRaceMinAffinity;
        }

        private static float Affinity(RaceDefinition from, RaceId to)
        {
            var arr = from.RaceAffinity;
            int i = (int)to;
            return arr != null && i >= 0 && i < arr.Length ? MathUtil.Sanitize(arr[i], -1f) : -1f;
        }

        /// <summary>True for parent/child and (half-)siblings, who never marry.</summary>
        public static bool AreCloseKin(Person a, Person b)
        {
            if (a == null || b == null) return false;
            if (a.Id == b.MotherId || a.Id == b.FatherId || b.Id == a.MotherId || b.Id == a.FatherId) return true;
            if (a.MotherId >= 0 && a.MotherId == b.MotherId) return true;
            if (a.FatherId >= 0 && a.FatherId == b.FatherId) return true;
            return false;
        }

        /// <summary>Largest allowed age gap (years) between two partners: 0.25 x the smaller race life expectancy.</summary>
        public static float MaxAgeGap(RaceDefinition a, RaceDefinition b)
        {
            float la = a != null ? Math.Max(0f, MathUtil.Sanitize(a.LifeExpectancy)) : 0f;
            float lb = b != null ? Math.Max(0f, MathUtil.Sanitize(b.LifeExpectancy)) : 0f;
            return LifecycleBalance.MaxAgeGapFraction * Math.Min(la, lb);
        }
    }
}
