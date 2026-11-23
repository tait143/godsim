using System;

namespace GodSim.Core.AI
{
    /// <summary>How a settlement's food outlook is classified by the <see cref="FoodSecurityAdvisor"/>.</summary>
    public enum FoodSecuritySituation : byte
    {
        /// <summary>No residents / nothing to forecast.</summary>
        None = 0,
        /// <summary>Starvation now or within the next few weeks: all hands to food.</summary>
        Crisis = 1,
        /// <summary>The coming winter will not be covered by the stock expected at its start.</summary>
        WinterShortfall = 2,
        /// <summary>No immediate danger, but the 2-year projection runs dry (structural deficit or growth).</summary>
        Deficit = 3,
        /// <summary>Balanced with a comfortable winter buffer.</summary>
        Stable = 4,
        /// <summary>Food piles up faster than it is eaten: labor can be freed.</summary>
        Surplus = 5
    }

    /// <summary>
    /// Result of one food forecast (reused instance; no per-tick allocation). All rates are food per tick (day);
    /// all amounts are food units. Filled by <see cref="FoodSecurityForecaster.Project"/> and
    /// <see cref="FoodSecurityForecaster.Classify"/>.
    /// </summary>
    public sealed class FoodSecurityForecast
    {
        public FoodSecuritySituation Situation;
        public float Urgency;
        public float DesiredCoverage = 1f;

        // ---- now
        public float Stock;
        public float Capacity;
        public float IncomeNow;
        public float ConsumptionNow;
        public float NetNow;
        public float BaseIncome;            // de-seasonalized income (yield multiplier 1)
        public float DaysOfFood;
        public float Population;
        public float PopulationAtHorizon;

        // ---- winter (the next one, and the one after it)
        public bool InWinter;
        public int DaysUntilWinter;
        public int WinterLength;
        public float WinterNeed;            // shortfall to be eaten out of stores during the next winter
        public float StockAtWinter;         // projected stock at the start of the next winter
        public float WinterCoverage;        // StockAtWinter / WinterNeed (MaxCoverage when no need)
        public float SecondWinterNeed;
        public float StockAtSecondWinter;
        public float SecondWinterCoverage;

        // ---- horizon
        public int StarveDay = -1;          // first day the projection runs dry (-1 = never within horizon)
        public float MinStock;
        public float AnnualIncome;          // projected over the next year
        public float AnnualConsumption;
        public float AnnualNetRatio;        // (income - consumption) / consumption over the next year
        public float ProjectedSpoilage;     // food lost to full stores over the horizon
        public float SpoilageBeforeWinter;
        public float SurplusStrength;       // 0..1 how large the surplus is (Surplus only)

        public void Reset()
        {
            Situation = FoodSecuritySituation.None;
            Urgency = 0f; DesiredCoverage = 1f;
            Stock = Capacity = IncomeNow = ConsumptionNow = NetNow = BaseIncome = DaysOfFood = Population = PopulationAtHorizon = 0f;
            InWinter = false; DaysUntilWinter = 0; WinterLength = 0;
            WinterNeed = StockAtWinter = WinterCoverage = SecondWinterNeed = StockAtSecondWinter = SecondWinterCoverage = 0f;
            StarveDay = -1; MinStock = 0f;
            AnnualIncome = AnnualConsumption = AnnualNetRatio = ProjectedSpoilage = SpoilageBeforeWinter = SurplusStrength = 0f;
        }
    }

    /// <summary>
    /// Pure, deterministic, allocation-free food projection: steps day-by-day (in <see cref="Step"/>-day chunks) over
    /// a 2-year horizon applying the seasonal yield cycle (winter ~0.4x), the current weather anomaly for the rest of
    /// the season (or the remaining drought/rain), population growth and storage capacity (spoilage).
    /// </summary>
    public static class FoodSecurityForecaster
    {
        public const int Horizon = SimTime.TicksPerYear * 2;
        public const int Step = 2;
        public const float MaxCoverage = 9.99f;
        public const int WinterStartDay = SimTime.TicksPerSeason * (int)Season.Winter;

        /// <summary>Expected seasonal food-yield multiplier (matches the season/weather model).</summary>
        public static float SeasonYield(Season s)
        {
            switch (s)
            {
                case Season.Spring: return 1.05f;
                case Season.Summer: return 1.25f;
                case Season.Autumn: return 1.1f;
                case Season.Winter: return 0.4f;
                default: return 1f;
            }
        }

        /// <summary>Days until the next winter starts (0 while in winter) and how many winter days remain/come.</summary>
        public static void WinterWindow(long tick, out bool inWinter, out int daysUntil, out int length)
        {
            int doy = SimTime.DayOfYear(Math.Max(0L, tick));
            inWinter = doy >= WinterStartDay;
            if (inWinter) { daysUntil = 0; length = SimTime.TicksPerYear - doy; }
            else { daysUntil = WinterStartDay - doy; length = SimTime.TicksPerSeason; }
        }

        /// <summary>
        /// Projects stock over <see cref="Horizon"/> days. <paramref name="income"/>/<paramref name="expense"/> are the measured
        /// (or estimated) current rates; <paramref name="weatherMultiplier"/> is the food-yield multiplier those rates were
        /// produced under. <paramref name="anomalyTicks"/> &lt;= 0 means "current anomaly lasts until the season ends".
        /// </summary>
        public static void Project(FoodSecurityForecast f, long tick, float stock, float capacity, float income, float expense,
            float weatherMultiplier, int anomalyTicks, float population, float foodPerPerson, float growthPerTick, float maxPopulation)
        {
            f.Reset();
            stock = Math.Max(0f, MathUtil.Sanitize(stock));
            capacity = Math.Max(1f, MathUtil.Sanitize(capacity, Stockpile.DefaultCapacity));
            income = Math.Max(0f, MathUtil.Sanitize(income));
            expense = Math.Max(0f, MathUtil.Sanitize(expense));
            population = Math.Max(0f, MathUtil.Sanitize(population));
            foodPerPerson = Math.Max(0f, MathUtil.Sanitize(foodPerPerson, 0.05f));
            growthPerTick = MathUtil.Clamp(MathUtil.Sanitize(growthPerTick), -population / Horizon, population + 1f);
            maxPopulation = Math.Max(population, MathUtil.Sanitize(maxPopulation, population));
            if (tick < 0) tick = 0;

            float baseCons = population * foodPerPerson;
            float consNow = Math.Max(baseCons, expense);
            float otherExpense = Math.Max(0f, expense - baseCons); // food used by non-residents/inputs, assumed constant

            Season cur = SimTime.SeasonOf(tick);
            float wm = MathUtil.Clamp(MathUtil.Sanitize(weatherMultiplier, 1f), 0.05f, 5f);
            float baseIncome = income / wm;
            float anomaly = MathUtil.Clamp(wm / SeasonYield(cur), 0.2f, 2.5f);
            if (anomalyTicks <= 0) anomalyTicks = SimTime.TicksPerSeason - SimTime.DayOfYear(tick) % SimTime.TicksPerSeason;

            WinterWindow(tick, out bool inWinter, out int w0, out int len0);
            int w0End = w0 + len0;
            int w1 = inWinter ? len0 + (SimTime.TicksPerYear - SimTime.TicksPerSeason) : w0 + SimTime.TicksPerYear;
            int w1End = w1 + SimTime.TicksPerSeason;

            f.Stock = stock; f.Capacity = capacity; f.IncomeNow = income; f.ConsumptionNow = consNow; f.NetNow = income - consNow;
            f.BaseIncome = baseIncome; f.Population = population;
            f.DaysOfFood = consNow > 1e-4f ? Math.Min(9999f, stock / consNow) : 9999f;
            f.InWinter = inWinter; f.DaysUntilWinter = w0; f.WinterLength = len0;

            float s = stock, minStock = stock, spoil = 0f, spoilBefore = 0f, need0 = 0f, need1 = 0f, at0 = -1f, at1 = -1f;
            float annInc = 0f, annCons = 0f, people = population;
            int starve = -1;
            for (int d = 0; d < Horizon; d += Step)
            {
                Season se = SimTime.SeasonOf(tick + d);
                float inc = baseIncome * SeasonYield(se) * (d < anomalyTicks ? anomaly : 1f);
                people = MathUtil.Clamp(population + growthPerTick * d, 0f, maxPopulation);
                float cons = people * foodPerPerson + otherExpense;
                float deficit = Math.Max(0f, cons - inc) * Step;

                if (d >= w0 && at0 < 0f) at0 = s;
                if (d >= w0 && d < w0End) need0 += deficit;
                if (d >= w1 && at1 < 0f) at1 = s;
                if (d >= w1 && d < w1End) need1 += deficit;
                if (d < SimTime.TicksPerYear) { annInc += inc * Step; annCons += cons * Step; }

                float next = s + (inc - cons) * Step;
                if (next > capacity)
                {
                    spoil += next - capacity;
                    if (d < w0) spoilBefore += next - capacity;
                    next = capacity;
                }
                if (next <= 0f)
                {
                    if (starve < 0)
                    {
                        float drain = cons - inc;
                        starve = d + (drain > 1e-5f ? Math.Min(Step, MathUtil.FloorToInt(s / drain)) : Step);
                    }
                    next = 0f;
                }
                if (next < minStock) minStock = next;
                s = next;
            }

            f.PopulationAtHorizon = people;
            f.StarveDay = starve;
            f.MinStock = minStock;
            f.WinterNeed = need0;
            f.StockAtWinter = Math.Max(0f, at0);
            f.WinterCoverage = Coverage(f.StockAtWinter, need0);
            f.SecondWinterNeed = need1;
            f.StockAtSecondWinter = Math.Max(0f, at1);
            f.SecondWinterCoverage = Coverage(f.StockAtSecondWinter, need1);
            f.AnnualIncome = annInc;
            f.AnnualConsumption = annCons;
            f.AnnualNetRatio = annCons > 1e-3f ? MathUtil.Clamp((annInc - annCons) / annCons, -1f, 10f) : (annInc > 0f ? 10f : 0f);
            f.ProjectedSpoilage = spoil;
            f.SpoilageBeforeWinter = spoilBefore;
        }

        private static float Coverage(float stockAt, float need) =>
            need <= 0.01f ? MaxCoverage : MathUtil.Clamp(MathUtil.Sanitize(stockAt / need), 0f, MaxCoverage);

        /// <summary>
        /// Classifies the projection and sets urgency (0..1). <paramref name="desiredCoverage"/> is the winter buffer the
        /// civ wants (personality + famine memory); <paramref name="previous"/> adds hysteresis to the Surplus state.
        /// </summary>
        public static void Classify(FoodSecurityForecast f, float desiredCoverage, FoodSecuritySituation previous, bool recentFamine)
        {
            f.DesiredCoverage = desiredCoverage = MathUtil.Clamp(MathUtil.Sanitize(desiredCoverage, 1.2f), 1f, 3f);
            f.SurplusStrength = 0f;
            if (f.Population <= 0f) { f.Situation = FoodSecuritySituation.None; f.Urgency = 0f; return; }

            float proximity = f.InWinter ? 1f : 1f - MathUtil.Clamp01(f.DaysUntilWinter / 72f);
            bool imminentStarvation = f.StarveDay >= 0 && f.StarveDay <= 16;
            if (recentFamine || imminentStarvation || (f.DaysOfFood < 6f && f.NetNow < 0f))
            {
                f.Situation = FoodSecuritySituation.Crisis;
                float soon = f.StarveDay >= 0 ? 1f - MathUtil.Clamp01(f.StarveDay / 16f) : 0.5f;
                f.Urgency = recentFamine ? 1f : 0.85f + 0.15f * soon;
                return;
            }
            if (f.WinterCoverage < desiredCoverage)
            {
                f.Situation = FoodSecuritySituation.WinterShortfall;
                float deficiency = 1f - MathUtil.Clamp01(f.WinterCoverage / desiredCoverage);
                f.Urgency = MathUtil.Clamp(0.3f + 0.5f * deficiency + 0.15f * proximity, 0.3f, 0.95f);
                return;
            }
            if (f.StarveDay >= 0 || f.AnnualNetRatio < 0f || f.SecondWinterCoverage < 1f)
            {
                f.Situation = FoodSecuritySituation.Deficit;
                float depth = Math.Max(MathUtil.Clamp01(-f.AnnualNetRatio / 0.5f), f.StarveDay >= 0 ? 1f - MathUtil.Clamp01(f.StarveDay / (float)Horizon) : 0f);
                f.Urgency = 0.3f + 0.3f * depth;
                return;
            }
            float h = previous == FoodSecuritySituation.Surplus ? 0.8f : 1f; // hysteresis: stay in surplus a bit longer
            bool nearFull = f.Stock >= 0.85f * f.Capacity && f.NetNow > 0f;
            bool bigNet = f.AnnualNetRatio >= 0.3f * h;
            if ((bigNet || nearFull) && f.WinterCoverage >= desiredCoverage * 1.3f * h && f.DaysOfFood >= 40f * h && f.SecondWinterCoverage >= 1.2f)
            {
                f.Situation = FoodSecuritySituation.Surplus;
                float byNet = MathUtil.Clamp01((f.AnnualNetRatio - 0.25f) / 0.75f);
                float bySpoil = nearFull ? 0.5f + 0.5f * MathUtil.Clamp01(f.ProjectedSpoilage / Math.Max(1f, f.Capacity)) : 0f;
                f.SurplusStrength = Math.Max(byNet, bySpoil);
                f.Urgency = 0.05f;
                return;
            }
            f.Situation = FoodSecuritySituation.Stable;
            f.Urgency = 0.1f + 0.1f * (1f - MathUtil.Clamp01(f.WinterCoverage - desiredCoverage));
        }
    }
}
