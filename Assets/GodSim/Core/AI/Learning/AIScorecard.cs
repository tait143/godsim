using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Per-civilization outcome scorecard: nine metrics in 0..1 (higher is better) plus an overall score, computed
    /// from state only (settlements, stockpiles, services, research, wars, strategy blackboard). Results are written
    /// to civ memory "ai.score.{metric}". Trend metrics (growth, research pace, wealth) keep a small EMA state in
    /// "ai.trend.*" that advances at most once per tick, so calling <see cref="Evaluate"/> repeatedly is safe.
    /// Pure arithmetic only (no transcendental functions), allocation-free, NaN-proof.
    /// </summary>
    public static class AIScorecard
    {
        /// <summary>Days of food that count as full food security.</summary>
        public const float FoodDaysForFull = 60f;
        /// <summary>Ticks after a starvation event during which a settlement's food score is halved.</summary>
        public const int StarvationMemoryTicks = 192;
        /// <summary>Target years per age; research pace 1 means the civ would advance in this time.</summary>
        public const float TargetYearsPerAge = 120f;
        /// <summary>A construction site older than this (ticks) counts as stalled.</summary>
        public const int StallTicks = 192;
        /// <summary>Yearly population growth rate mapped to a growth score of 1 (0.5 = stagnation).</summary>
        public const float GrowthRateForFull = 0.1f;
        /// <summary>EMA factor for trend state.</summary>
        public const float TrendAlpha = 0.5f;

        // food, housing, health, happiness, growth, research, military, wealth, construction (sum = 1)
        private static readonly float[] s_weights = { 0.18f, 0.10f, 0.12f, 0.12f, 0.10f, 0.10f, 0.10f, 0.08f, 0.10f };

        /// <summary>Reads a stored metric (0.5 when never evaluated).</summary>
        public static float Get(Civilization civ, AIMetric metric)
            => civ == null ? 0.5f : MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(AILearningKeys.ScoreKey(metric), 0.5f), 0.5f));

        /// <summary>Computes every metric for the civ, writes "ai.score.*" and returns the overall score.</summary>
        public static float Evaluate(WorldState state, GameDatabase db, Civilization civ) => Evaluate(state, db, civ, out _);

        /// <summary>As <see cref="Evaluate(WorldState, GameDatabase, Civilization)"/>, also returning the civ's living population.</summary>
        public static float Evaluate(WorldState state, GameDatabase db, Civilization civ, out int population)
        {
            population = 0;
            if (state == null || civ == null) return 0f;
            long tick = state.Tick;

            float foodW = 0f, healthW = 0f, happyW = 0f, defCovW = 0f;
            int homeless = 0, soldiers = 0;
            float gold = 0f, goldNetPerTick = 0f, researchPerTick = 0f;
            float sites = 0f, stalled = 0f, orders = 0f, stuck = 0f;

            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                int pop = s.Population;
                population += pop;

                float g = MathUtil.Sanitize(s.Stockpile.Get(ResourceType.Gold));
                gold += Math.Max(0f, g);
                goldNetPerTick += MathUtil.Sanitize(s.Stockpile.Net(ResourceType.Gold));
                researchPerTick += Math.Max(0f, MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Research]));

                if (pop > 0)
                {
                    float food = MathUtil.Clamp01(FoodDays(s, db, pop) / FoodDaysForFull);
                    if (s.LastStarvationTick >= 0 && tick - s.LastStarvationTick < StarvationMemoryTicks) food *= 0.5f;
                    foodW += food * pop;
                    homeless += Math.Max(0, Math.Min(pop, s.Homeless));
                    healthW += MathUtil.Clamp01(MathUtil.Sanitize(s.Health, 0.5f)) * pop;
                    float unrest = MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest));
                    happyW += MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.5f)) * (1f - 0.3f * unrest) * pop;
                    defCovW += MathUtil.Clamp01(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Defense])) * pop;
                    soldiers += Math.Max(0, s.Soldiers);
                }

                var bids = s.BuildingIds;
                for (int b = 0; b < bids.Count; b++)
                {
                    var bd = state.Buildings.Get(bids[b]);
                    if (bd == null || bd.IsComplete || bd.IsDestroyed) continue;
                    sites += 1f;
                    long age = tick - bd.PlacedTick;
                    if (age > StallTicks) stalled += 1f;
                    else if (!bd.CostPaid && age > StallTicks / 2) stalled += 0.5f;
                }
                var q = s.BuildQueue;
                for (int o = 0; o < q.Count; o++)
                {
                    if (q[o] == null) continue;
                    orders += 1f;
                    if (q[o].Attempts >= 3) stuck += 1f;
                }
            }

            // ---- trend state (advances at most once per tick)
            var mem = civ.Memory;
            bool hasTrend = mem.TryGetValue(AILearningKeys.TrendTick, out float lastTickF);
            long lastTick = hasTrend ? (long)MathUtil.Sanitize(lastTickF) : tick;
            float years = hasTrend ? (tick - lastTick) / (float)SimTime.TicksPerYear : 0f;
            float popRate = civ.Mem(AILearningKeys.TrendPopulationRate);
            float goldRate = civ.Mem(AILearningKeys.TrendGoldRate);
            float totalResearch = Math.Max(0f, MathUtil.Sanitize(civ.TotalResearch));
            float instantResearchPerYear = researchPerTick * SimTime.TicksPerYear * Math.Max(0f, MathUtil.Sanitize(civ.Modifiers.Research, 1f));
            float researchRate = hasTrend ? civ.Mem(AILearningKeys.TrendResearchRate, instantResearchPerYear) : instantResearchPerYear;
            if (!hasTrend || years > 0f)
            {
                if (hasTrend)
                {
                    float prevPop = Math.Max(5f, civ.Mem(AILearningKeys.TrendPopulation));
                    float rawPop = (population - civ.Mem(AILearningKeys.TrendPopulation)) / prevPop / years;
                    popRate += (MathUtil.Clamp(MathUtil.Sanitize(rawPop), -1f, 1f) - popRate) * TrendAlpha;
                    float rawGold = (gold - civ.Mem(AILearningKeys.TrendGold)) / years;
                    goldRate += (MathUtil.Clamp(MathUtil.Sanitize(rawGold), -1e6f, 1e6f) - goldRate) * TrendAlpha;
                    float rawResearch = Math.Max(0f, totalResearch - civ.Mem(AILearningKeys.TrendResearch)) / years;
                    researchRate += (MathUtil.Clamp(MathUtil.Sanitize(rawResearch), 0f, 1e9f) - researchRate) * TrendAlpha;
                }
                mem[AILearningKeys.TrendTick] = tick;
                mem[AILearningKeys.TrendPopulation] = population;
                mem[AILearningKeys.TrendPopulationRate] = MathUtil.Sanitize(popRate);
                mem[AILearningKeys.TrendGold] = gold;
                mem[AILearningKeys.TrendGoldRate] = MathUtil.Sanitize(goldRate);
                mem[AILearningKeys.TrendResearch] = totalResearch;
                mem[AILearningKeys.TrendResearchRate] = MathUtil.Sanitize(researchRate);
            }

            float popF = population;
            float invPop = population > 0 ? 1f / popF : 0f;

            float mFood = population > 0 ? foodW * invPop : 0f;
            float mHousing = population > 0 ? 1f - homeless * invPop : 0f;
            float mHealth = population > 0 ? healthW * invPop : 0f;
            float mHappy = population > 0 ? happyW * invPop : 0f;
            float mGrowth = population > 0 ? 0.5f + 0.5f * MathUtil.Clamp(popRate / GrowthRateForFull, -1f, 1f) : 0f;
            float mResearch = ResearchScore(db, civ, totalResearch, researchRate);
            float mMilitary = MilitaryScore(state, civ, population > 0 ? defCovW * invPop : 0f, population > 0 ? soldiers * invPop : 0f);
            float scale = 10f + popF * 0.5f;
            float stockX = gold / (2f * scale);
            float stockScore = stockX / (1f + stockX);
            float flow = (0.5f * goldRate + 0.5f * goldNetPerTick * SimTime.TicksPerYear) / scale;
            float flowScore = 0.5f + 0.5f * flow / (1f + Math.Abs(flow));
            float mWealth = 0.5f * stockScore + 0.5f * flowScore;
            float mBuild = 1f - MathUtil.Clamp01((stalled + 0.5f * stuck) / Math.Max(1f, sites + orders));

            Write(mem, AIMetric.Food, mFood);
            Write(mem, AIMetric.Housing, mHousing);
            Write(mem, AIMetric.Health, mHealth);
            Write(mem, AIMetric.Happiness, mHappy);
            Write(mem, AIMetric.Growth, mGrowth);
            Write(mem, AIMetric.Research, mResearch);
            Write(mem, AIMetric.Military, mMilitary);
            Write(mem, AIMetric.Wealth, mWealth);
            Write(mem, AIMetric.Construction, mBuild);

            float weighted = 0f, worst = 1f;
            for (int m = 0; m < s_weights.Length; m++)
            {
                float v = mem[AILearningKeys.ScoreKey((AIMetric)m)];
                weighted += v * s_weights[m];
                if (v < worst) worst = v;
            }
            float overall = population > 0 ? 0.8f * weighted + 0.2f * worst : 0f;
            Write(mem, AIMetric.Overall, overall);
            return mem[AILearningKeys.ScoreKey(AIMetric.Overall)];
        }

        /// <summary>Days of food left: the economy's "econ.foodDays" when present, else stock / (pop * race food per tick).</summary>
        public static float FoodDays(Settlement s, GameDatabase db, int pop)
        {
            if (s == null) return 0f;
            if (s.Memory.TryGetValue(AILearningKeys.EconFoodDays, out float v) && !float.IsNaN(v) && !float.IsInfinity(v)) return Math.Max(0f, v);
            if (pop <= 0) return FoodDaysForFull;
            float perTick = 0.05f;
            if (db != null && (int)s.Race < db.Races.Count && db.Races[(int)s.Race] != null && db.Races[(int)s.Race].FoodPerTick > 0f)
                perTick = db.Races[(int)s.Race].FoodPerTick;
            return Math.Max(0f, MathUtil.Sanitize(s.Stockpile.Get(ResourceType.Food) / (pop * perTick)));
        }

        private static float ResearchScore(GameDatabase db, Civilization civ, float totalResearch, float ratePerYear)
        {
            int age = (int)civ.Age;
            if (db == null || age + 1 >= db.Ages.Count || age < 0) return 1f;
            var cur = db.Ages[age];
            var next = db.Ages[age + 1];
            if (next == null) return 1f;
            float from = cur != null ? MathUtil.Sanitize(cur.ResearchThreshold) : 0f;
            float span = Math.Max(1f, MathUtil.Sanitize(next.ResearchThreshold) - from);
            float pace = Math.Max(0f, ratePerYear) / Math.Max(1f, span / TargetYearsPerAge);
            float progress = MathUtil.Clamp01((totalResearch - from) / span);
            return 0.85f * MathUtil.Clamp01(pace) + 0.15f * progress;
        }

        private static float MilitaryScore(WorldState state, Civilization civ, float defenseCoverage, float soldierShare)
        {
            int wars = 0;
            var list = state.Diplomacy.Wars;
            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                if (w != null && w.IsActive && (w.AttackerId == civ.Id || w.DefenderId == civ.Id)) wars++;
            }
            float threat = Math.Max(MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(AILearningKeys.StrategyThreat))), MathUtil.Clamp01(wars * 0.35f));
            float level = MathUtil.Clamp01(0.6f * defenseCoverage + 0.6f * MathUtil.Clamp01(soldierShare / 0.1f));
            if (civ.Memory.TryGetValue(AILearningKeys.StrategyPowerRatio, out float ratio))
                level = 0.5f * level + 0.5f * MathUtil.Clamp01(MathUtil.Sanitize(ratio) / 2f);
            return 1f - threat * (1f - level) - 0.1f * (1f - level);
        }

        private static void Write(System.Collections.Generic.Dictionary<string, float> mem, AIMetric m, float v)
            => mem[AILearningKeys.ScoreKey(m)] = MathUtil.Clamp01(MathUtil.Sanitize(v, 0.5f));
    }
}
