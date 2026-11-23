using System;

namespace GodSim.Core.AI
{
    /// <summary>Explainable result of the growth advisor's last run (debug overlay / tests). Reused, never reallocated.</summary>
    public sealed class GrowthAdvisorForecast
    {
        public int SettlementId = -1;
        public int Population;
        /// <summary>Demographic model: expected births and deaths per year.</summary>
        public float ModelBirths, ModelDeaths;
        /// <summary>Observed EMA trend (people/year) and how much it was trusted (0..0.5).</summary>
        public float ObservedTrend, TrendTrust;
        /// <summary>Blended, food-capped net change per year.</summary>
        public float NetPerYear;
        public float Projected1, Projected2, Projected3;
        public int FreeBeds, Homeless;
        public float PendingBeds, BedDeficit, YearsToFull;
        public float FoodDays, FoodFactor, Ambition;
        public float HouseVote, WaterVote, TownCenterVote, Urgency;
        public BuildingType Dwelling = BuildingType.House;
        public BuildingType WaterBuilding = BuildingType.Well;

        public void Clear()
        {
            SettlementId = -1; Population = 0;
            ModelBirths = ModelDeaths = ObservedTrend = TrendTrust = NetPerYear = 0f;
            Projected1 = Projected2 = Projected3 = 0f;
            FreeBeds = Homeless = 0;
            PendingBeds = BedDeficit = YearsToFull = 0f;
            FoodDays = FoodFactor = Ambition = 0f;
            HouseVote = WaterVote = TownCenterVote = Urgency = 0f;
            Dwelling = BuildingType.House; WaterBuilding = BuildingType.Well;
        }
    }

    /// <summary>
    /// Growth advisor: projects each settlement's population 1-3 years ahead (couples, fertile women, race fertility and
    /// lifespan, civ growth modifiers, blended with the observed births/losses trend kept as EMAs in
    /// <c>adv.growth.*</c>) and keeps housing AHEAD of that growth, so the "one house per 3-5 people" rhythm emerges
    /// smoothly instead of in panicked bursts:
    /// <list type="bullet">
    /// <item>homeless who cannot be absorbed by free or pending beds: strong House vote (+ Builder staffing);</item>
    /// <item>a bed deficit projected within a year: gentle-to-moderate pre-building, paused while the tier's
    /// pending-dwelling budget is used, scaled by expansionism and dampened by food (no housing beyond what food supports);</item>
    /// <item>more than 2 empty houses and no projected need: slightly negative (no spam);</item>
    /// <item>projected water coverage (2 years ahead) below target: Well/Aqueduct, picked from definition data;</item>
    /// <item>missing town center: strong TownCenter vote.</item>
    /// </list>
    /// Every vote is gated with <see cref="GameDatabase.IsBuildingAvailable"/>; strong votes carry a short reason.
    /// </summary>
    public sealed class GrowthAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in board keys and logs.</summary>
        public const string Id = "growth";

        public override string AdvisorId => Id;
        public override int TickInterval => 12;

        private readonly GrowthAdvisorCensus _census = new GrowthAdvisorCensus();

        /// <summary>Forecast of the most recently advised settlement (for debug UI and tests).</summary>
        public readonly GrowthAdvisorForecast LastForecast = new GrowthAdvisorForecast();

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            var state = ctx.State;
            if (db == null || state == null) return;
            var f = LastForecast;
            f.Clear();
            f.SettlementId = s.Id;
            var mem = s.Memory;
            long now = ctx.Tick;

            // ---------------------------------------------------------------- census + observed trend
            bool hasSample = mem.TryGetValue(GrowthAdvisorKeys.SampleTick, out float sampleTickF);
            sampleTickF = MathUtil.Sanitize(sampleTickF, -1f);
            long sampleTick = (long)sampleTickF;
            if (sampleTickF < 0f || sampleTick > now) hasSample = false;
            _census.Collect(state, db, s, hasSample ? sampleTick : long.MaxValue);
            var c = _census;
            int pop = c.Population;
            f.Population = pop;
            UpdateTrend(s, c, now, hasSample, sampleTick);

            var race = GrowthAdvisorCensus.RaceOf(db, s.Race) ?? GrowthAdvisorCensus.RaceOf(db, civ.Race);
            float foodPerPerson = race != null && race.FoodPerTick > 1e-4f ? MathUtil.Sanitize(race.FoodPerTick, 0.05f) : 0.05f;

            // ---------------------------------------------------------------- food
            float foodDays = FoodDays(s, pop, foodPerPerson);
            float food = GrowthAdvisorModel.FoodGrowthFactor(foodDays);
            f.FoodDays = foodDays;
            f.FoodFactor = food;

            // ---------------------------------------------------------------- demographic projection
            float fertility = race != null ? MathUtil.Sanitize(race.FertilityPerYear, 0.25f) : 0.25f;
            float growthMod = MathUtil.Sanitize(civ.Modifiers.Growth, 1f);
            float perBirth = (race != null ? race.Id : s.Race) == RaceId.Saurian ? GrowthAdvisorModel.SaurianClutch : GrowthAdvisorModel.TwinFactor;
            float mothers = c.Couples + Math.Max(0, c.FertileWomen - c.Couples) * GrowthAdvisorModel.SingleMotherFactor;
            float births = GrowthAdvisorModel.ModelBirths(mothers, fertility, growthMod, perBirth, food);
            float lifespan = (race != null ? race.LifeExpectancy : 70f) + MathUtil.Sanitize(civ.Modifiers.LifespanBonusYears);
            float deaths = GrowthAdvisorModel.ModelDeaths(pop, c.Elders, lifespan, race != null ? race.ElderAge : 55f);
            float modelNet = births - deaths;

            float trend = MathUtil.Sanitize(s.Mem(GrowthAdvisorKeys.TrendPerYear));
            float trust = GrowthAdvisorModel.MaxTrendTrust * MathUtil.Clamp01(s.Mem(GrowthAdvisorKeys.Samples) / GrowthAdvisorModel.SamplesForFullTrust);
            float net = MathUtil.LerpUnclamped(modelNet, trend, trust);

            // Never plan for more mouths than the food economy can feed over the next year.
            var stock = s.Stockpile;
            int fi = (int)ResourceType.Food;
            if (stock.AvgIncome[fi] > 0f || stock.AvgExpense[fi] > 0f)
            {
                float surplusPeople = MathUtil.Sanitize(stock.Net(ResourceType.Food)) / foodPerPerson;
                float stockPeople = stock.Get(ResourceType.Food) / (foodPerPerson * SimTime.TicksPerYear);
                float supportable = Math.Max(0f, surplusPeople + stockPeople);
                if (net > supportable) net = supportable;
            }
            net = MathUtil.Sanitize(net);

            float p1 = GrowthAdvisorModel.Project(pop, net, 1);
            float p2 = GrowthAdvisorModel.Project(pop, net, 2);
            float p3 = GrowthAdvisorModel.Project(pop, net, 3);
            float growth1 = p1 - pop;
            f.ModelBirths = births; f.ModelDeaths = deaths; f.ObservedTrend = trend; f.TrendTrust = trust; f.NetPerYear = net;
            f.Projected1 = p1; f.Projected2 = p2; f.Projected3 = p3;
            mem[GrowthAdvisorKeys.Projected1] = p1;
            mem[GrowthAdvisorKeys.Projected2] = p2;
            mem[GrowthAdvisorKeys.Projected3] = p3;

            float weight = AdvisorBoard.Weight(civ, AdvisorId);
            float urgency = 0f;

            // ---------------------------------------------------------------- town center
            if (!c.HasTownCenter && db.IsBuildingAvailable(civ, BuildingType.TownCenter)
                && db.TryGetBuilding(BuildingType.TownCenter, out var tcDef) && tcDef.MaxPerSettlement != 0)
            {
                f.TownCenterVote = 1f;
                VoteBuild(s, civ, BuildingType.TownCenter, 1f, weight >= 0.5f ? "no town center: the town has no civic heart" : null);
                urgency = 1f;
            }

            // ---------------------------------------------------------------- housing
            int freeBeds = mem.TryGetValue(GrowthAdvisorKeys.FreeHousing, out float fh)
                ? Math.Max(0, MathUtil.RoundToInt(MathUtil.Sanitize(fh)))
                : c.ComputedFreeBeds;
            int homeless = Math.Max(Math.Max(0, s.Homeless), c.Homeless);
            float pending = c.PendingBeds;
            int shortNow = Math.Max(0, homeless - freeBeds);
            float uncovered = Math.Max(0f, shortNow - pending);
            float ambition = 0.8f + 0.4f * AdvisorBoard.Trait(civ, db, AITrait.Expansionism);
            int buffer = GrowthAdvisorModel.SpareBedBuffer(s.Tier);
            float spare = freeBeds + pending - homeless;
            float deficit1 = growth1 + buffer - spare;
            float yearsToFull = spare <= 0f ? 0f : (growth1 > 0.05f ? MathUtil.Clamp(spare / growth1, 0f, 99f) : 99f);
            f.FreeBeds = freeBeds; f.Homeless = homeless; f.PendingBeds = pending; f.BedDeficit = deficit1;
            f.YearsToFull = yearsToFull; f.Ambition = ambition;
            mem[GrowthAdvisorKeys.FreeBeds] = freeBeds;
            mem[GrowthAdvisorKeys.BedDeficit] = MathUtil.Clamp(MathUtil.Sanitize(deficit1), -999f, 999f);
            mem[GrowthAdvisorKeys.YearsToFull] = yearsToFull;

            if (pop > 0 && PickDwelling(db, civ, s, c, pop, out var dwelling, out float bedsPerHouse))
            {
                f.Dwelling = dwelling;
                float vote = 0f;
                float housingU = 0f;
                string reason = null;
                if (shortNow > 0)
                {
                    // People are sleeping outside right now: strong, unless construction already covers them.
                    vote = uncovered > 0.5f ? 0.65f + 0.35f * MathUtil.Clamp01(uncovered / (2f * bedsPerHouse)) : 0.3f;
                    vote *= MathUtil.Lerp(0.55f, 1f, food);
                    housingU = 0.6f + 0.4f * MathUtil.Clamp01(3f * shortNow / Math.Max(1, pop));
                    if (vote * weight >= 0.5f)
                        reason = homeless + " homeless; free beds " + freeBeds + (c.PendingDwellings > 0 ? ", " + c.PendingDwellings + " dwelling(s) underway" : "")
                                 + "; projected " + GrowthAdvisorModel.Signed(growth1) + " next year";
                    VoteJob(s, civ, JobType.Builder, 0.2f + 0.3f * MathUtil.Clamp01(uncovered / 8f));
                }
                else if (deficit1 > 0f && c.PendingDwellings < GrowthAdvisorModel.MaxPendingDwellings(s.Tier))
                {
                    // Projected shortage within a year: pre-build gently, firmer the closer the town is to full.
                    float closeness = 1f - MathUtil.Clamp01(yearsToFull);
                    float houses = MathUtil.Clamp01(deficit1 / bedsPerHouse);
                    vote = Math.Min(0.6f, (0.12f + 0.28f * closeness + 0.15f * houses) * ambition * food);
                    housingU = MathUtil.Clamp01(0.15f + 0.45f * closeness) * food;
                    if (vote * weight >= 0.5f)
                        reason = "projected " + GrowthAdvisorModel.Signed(growth1) + " people next year; free beds " + freeBeds;
                }
                else if (c.EmptyDwellings > 2 && deficit1 <= 0f)
                {
                    // Housing glut: discourage more dwellings (no spam), mildly.
                    vote = -MathUtil.Clamp(0.15f + 0.1f * (c.EmptyDwellings - 3), 0.15f, 0.5f);
                    if (-vote * weight >= 0.5f) reason = c.EmptyDwellings + " empty dwellings; projected " + GrowthAdvisorModel.Signed(growth1) + " next year";
                }
                f.HouseVote = vote;
                if (vote != 0f) VoteBuild(s, civ, dwelling, vote, reason);
                if (housingU > urgency) urgency = housingU;
            }

            // ---------------------------------------------------------------- water (2 years ahead)
            float futurePop = Math.Max(pop, p2);
            if (futurePop >= GrowthAdvisorModel.MinWaterPopulation)
            {
                float output = s.ServiceOutput[(int)ServiceType.Water];
                output = output > 0f ? MathUtil.Sanitize(output) : c.WaterPointsComplete;
                float perCap = LearnWaterPerCapita(s, output, pop);
                float required = perCap * futurePop;
                float coverage = required > 1e-4f ? (output + c.WaterPointsPending) / required : 1f;
                if (coverage < GrowthAdvisorModel.WaterCoverageTarget && c.WaterBuildingsPending == 0)
                {
                    float gap = required - output - c.WaterPointsPending;
                    if (PickWater(db, civ, s, c, pop, gap, out var waterType))
                    {
                        float shortfall = 1f - MathUtil.Clamp01(coverage);
                        float size = MathUtil.SmoothStep(GrowthAdvisorModel.MinWaterPopulation, 30f, futurePop);
                        float vote = Math.Min(0.85f, (0.15f + 0.65f * shortfall) * MathUtil.Lerp(0.4f, 1f, size));
                        f.WaterVote = vote;
                        f.WaterBuilding = waterType;
                        string reason = vote * weight >= 0.5f
                            ? "water for " + pop + "->" + MathUtil.RoundToInt(futurePop) + " people in 2y: coverage " + MathUtil.RoundToInt(MathUtil.Clamp01(coverage) * 100f) + "%"
                            : null;
                        VoteBuild(s, civ, waterType, vote, reason);
                        float waterU = shortfall * 0.6f * size;
                        if (waterU > urgency) urgency = waterU;
                    }
                }
            }

            f.Urgency = urgency;
            Urgency(s, urgency);
        }

        // ==================================================================== helpers
        private static void UpdateTrend(Settlement s, GrowthAdvisorCensus c, long now, bool hasSample, long sampleTick)
        {
            var mem = s.Memory;
            int pop = c.Population;
            if (hasSample && now > sampleTick)
            {
                float dtYears = (now - sampleTick) / (float)SimTime.TicksPerYear;
                float prevPop = MathUtil.Sanitize(s.Mem(GrowthAdvisorKeys.SamplePop, pop), pop);
                float delta = pop - prevPop;
                float cap = 2f * Math.Max(4, pop);
                float inst = MathUtil.Clamp(delta / dtYears, -cap, cap);
                float bornInst = MathUtil.Clamp(c.NewbornsSinceSample / dtYears, 0f, cap);
                float lostInst = MathUtil.Clamp(Math.Max(0f, c.NewbornsSinceSample - delta) / dtYears, 0f, cap);
                int n = (int)MathUtil.Clamp(MathUtil.Sanitize(s.Mem(GrowthAdvisorKeys.Samples)), 0f, 1000f);
                // Running mean for the first samples (unbiased start), then an EMA with a one-year time constant.
                float a = dtYears > 2f ? 1f : Math.Max(GrowthAdvisorModel.EmaAlpha(dtYears), 1f / (n + 1));
                mem[GrowthAdvisorKeys.TrendPerYear] = MathUtil.Sanitize(MathUtil.Lerp(s.Mem(GrowthAdvisorKeys.TrendPerYear), inst, a));
                mem[GrowthAdvisorKeys.BirthsPerYear] = MathUtil.Sanitize(MathUtil.Lerp(s.Mem(GrowthAdvisorKeys.BirthsPerYear), bornInst, a));
                mem[GrowthAdvisorKeys.LossesPerYear] = MathUtil.Sanitize(MathUtil.Lerp(s.Mem(GrowthAdvisorKeys.LossesPerYear), lostInst, a));
                mem[GrowthAdvisorKeys.Samples] = Math.Min(n + 1, 1000);
            }
            if (!hasSample || now > sampleTick)
            {
                mem[GrowthAdvisorKeys.SamplePop] = pop;
                mem[GrowthAdvisorKeys.SampleTick] = now;
            }
        }

        private static float FoodDays(Settlement s, int pop, float foodPerPerson)
        {
            if (s.Memory.TryGetValue(GrowthAdvisorKeys.FoodDays, out float v)) return MathUtil.Clamp(MathUtil.Sanitize(v), 0f, 9999f);
            var st = s.Stockpile;
            float use = st.AvgExpense[(int)ResourceType.Food];
            if (!(use > 1e-3f)) use = pop * foodPerPerson;
            return use > 1e-4f ? MathUtil.Clamp(MathUtil.Sanitize(st.Get(ResourceType.Food) / use), 0f, 9999f) : 9999f;
        }

        /// <summary>Learns water points per resident from unsaturated economy coverage; falls back to the stored or default value.</summary>
        private static float LearnWaterPerCapita(Settlement s, float output, int pop)
        {
            float stored = s.Mem(GrowthAdvisorKeys.WaterPerCapita, GrowthAdvisorModel.DefaultWaterPerCapita);
            stored = MathUtil.Clamp(MathUtil.Sanitize(stored, GrowthAdvisorModel.DefaultWaterPerCapita), 0.01f, 2f);
            float coverage = s.ServiceCoverage[(int)ServiceType.Water];
            if (output > 0f && pop > 0 && coverage > 0.02f && coverage < 0.98f)
            {
                float observed = MathUtil.Clamp(output / (coverage * pop), 0.01f, 2f);
                stored = MathUtil.Lerp(stored, observed, 0.5f);
                s.Memory[GrowthAdvisorKeys.WaterPerCapita] = stored;
            }
            return stored;
        }

        private static bool Buildable(GameDatabase db, Civilization civ, Settlement s, GrowthAdvisorCensus c, BuildingDefinition def, int pop)
        {
            if (def == null || def.IsWonder || !db.IsBuildingAvailable(civ, def.Type)) return false;
            if (def.MinTier > s.Tier || def.MinPopulation > pop) return false;
            return def.MaxPerSettlement < 0 || c.Existing[(int)def.Type] < def.MaxPerSettlement;
        }

        /// <summary>House when available, else the available dwelling with the most beds (data-driven).</summary>
        private static bool PickDwelling(GameDatabase db, Civilization civ, Settlement s, GrowthAdvisorCensus c, int pop, out BuildingType type, out float beds)
        {
            type = BuildingType.House;
            beds = GrowthAdvisorModel.DefaultBedsPerDwelling;
            if (db.TryGetBuilding(BuildingType.House, out var house) && house.IsDwelling && Buildable(db, civ, s, c, house, pop))
            {
                beds = GrowthAdvisorModel.ExpectedBeds(house);
                return true;
            }
            var defs = db.Buildings;
            int best = -1;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null || !def.IsDwelling || !Buildable(db, civ, s, c, def, pop)) continue;
                if (best < 0 || def.HousingMax > defs[best].HousingMax) best = i;
            }
            if (best < 0) return false;
            type = defs[best].Type;
            beds = GrowthAdvisorModel.ExpectedBeds(defs[best]);
            return true;
        }

        /// <summary>Smallest available water building that closes the gap, else the largest (Well early, Aqueduct for cities).</summary>
        private static bool PickWater(GameDatabase db, Civilization civ, Settlement s, GrowthAdvisorCensus c, int pop, float gap, out BuildingType type)
        {
            type = BuildingType.Well;
            var defs = db.Buildings;
            int best = -1;
            float bestAmount = 0f;
            bool bestCloses = false;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null || def.IsDwelling || !GrowthAdvisorModel.IsPrimaryWater(def) || !Buildable(db, civ, s, c, def, pop)) continue;
                float amount = GrowthAdvisorModel.WaterAmount(def);
                bool closes = amount >= gap;
                bool better = best < 0
                              || (closes && !bestCloses)
                              || (closes && bestCloses && amount < bestAmount)
                              || (!closes && !bestCloses && amount > bestAmount);
                if (!better) continue;
                best = i; bestAmount = amount; bestCloses = closes;
            }
            if (best < 0) return false;
            type = defs[best].Type;
            return true;
        }
    }
}
