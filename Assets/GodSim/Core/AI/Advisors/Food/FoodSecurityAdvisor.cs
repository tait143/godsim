using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Food-security specialist of the Advisor Council ("food"). Every 12 ticks it forecasts each settlement's food over
    /// the next two years (stock, measured net flow, births vs. deaths, the seasonal cycle with lean ~0.4x winters,
    /// the current weather anomaly and storage capacity), classifies the outlook (crisis, winter shortfall, deficit,
    /// stable, surplus) and votes:
    /// <list type="bullet">
    /// <item>food producers, scored from their definitions (food per worker, inputs, slots), terrain fit (soil fertility
    /// for Fertile buildings, water for fishing, woods for hunting, hard Near* gates) and age (modern beats primitive);
    /// obsolete huts get negative votes once a much better modern producer is available;</item>
    /// <item>storage (Granary preferred) when the surplus is spoiling or a winter's need exceeds what the stores can hold;</item>
    /// <item>food jobs up in shortages (best-yield jobs most) and down in a large surplus (worst-yield jobs first), freeing labor.</item>
    /// </list>
    /// Personality: expansionist civs keep bigger winter buffers (they plan for growth), industrious civs redeploy surplus
    /// labor harder, and a recent famine is remembered for two years as extra caution. Votes are soft influence only.
    /// Publishes "ai.food.sit/cover/days/starve" in settlement memory for UI and other advisors.
    /// </summary>
    public sealed class FoodSecurityAdvisor : AdvisorSystemBase
    {
        public const string Id = "food";
        public const string KeySituation = "ai.food.sit";
        public const string KeyCoverage = "ai.food.cover";
        public const string KeyDays = "ai.food.days";
        public const string KeyStarveDay = "ai.food.starve";

        private const float MinFit = 0.15f;
        private const float ObsoleteRatio = 0.7f;
        private const float StrongVote = 0.5f;
        private const float FamineMemoryTicks = SimTime.TicksPerYear * 2f;

        public override string AdvisorId => Id;
        public override int TickInterval => 12;

        /// <summary>Last forecast computed (for debugging/tests; overwritten per settlement).</summary>
        public FoodSecurityForecast LastForecast => _forecast;

        private readonly FoodSecurityForecast _forecast = new FoodSecurityForecast();
        private readonly FoodSecurityTerrain _terrain = new FoodSecurityTerrain();
        private readonly FoodSecurityCatalog _catalog = new FoodSecurityCatalog();
        private readonly int[] _typeCount = new int[Counts.Buildings];
        private readonly int[] _queued = new int[Counts.Buildings];
        private readonly float[] _score = new float[Counts.Buildings];
        private readonly float[] _expected = new float[Counts.Buildings];
        private readonly int[] _jobSlots = new int[Counts.Jobs];
        private readonly float[] _jobYield = new float[Counts.Jobs];

        // per-advise scratch
        private int _pop, _adults, _unemployed, _elders, _partnered, _singleF, _singleM;
        private int _openFoodSlots;
        private float _estIncome, _pendingOutput;
        private string _reason;

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            var state = ctx.State;
            if (db == null || state == null) return;
            _catalog.Ensure(db);
            _reason = null;

            RaceDefinition race = (int)civ.Race < db.Races.Count ? db.Races[(int)civ.Race] : null;
            CountPeople(state, s, race);
            if (_pop <= 0)
            {
                Urgency(s, 0f);
                s.Memory[KeySituation] = (float)FoodSecuritySituation.None;
                return;
            }

            _terrain.Scan(state.Map, s);
            float prodMul = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Production[(int)ResourceType.Food], 1f), 0f, 5f)
                          * (race != null ? MathUtil.Clamp(MathUtil.Sanitize(race.ProductionMultiplier[(int)ResourceType.Food], 1f), 0f, 5f) : 1f);
            ScanBuildings(state, db, s, prodMul);

            // ---------------------------------------------------------------- forecast
            float perPerson = race != null && race.FoodPerTick > 0f ? race.FoodPerTick : 0.05f;
            float growthPerTick = GrowthPerTick(race, civ);
            var weather = state.Weather;
            float wm = MathUtil.Clamp(MathUtil.Sanitize(weather.FoodYieldMultiplier, 1f), 0.05f, 5f);
            int anomalyTicks = weather.DroughtTicksRemaining > 0 ? weather.DroughtTicksRemaining : (weather.RainTicksRemaining > 0 ? weather.RainTicksRemaining : 0);
            var st = s.Stockpile;
            int fi = (int)ResourceType.Food;
            float income = st.AvgIncome[fi], expense = st.AvgExpense[fi];
            bool measured = MathUtil.Sanitize(income) + MathUtil.Sanitize(expense) > 0.01f;
            if (!measured) { income = _estIncome * wm; expense = _pop * perPerson; }

            var f = _forecast;
            FoodSecurityForecaster.Project(f, ctx.Tick, st[ResourceType.Food], st.Capacity, income, expense, wm, anomalyTicks,
                _pop, perPerson, growthPerTick, _pop * 1.6f + 4f);

            // ---------------------------------------------------------------- personality & memory of famine
            float expansion = AdvisorBoard.Trait(civ, db, AITrait.Expansionism);
            float industry = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            long sinceFamine = s.LastStarvationTick >= 0 ? ctx.Tick - s.LastStarvationTick : long.MaxValue;
            float famineMemory = sinceFamine == long.MaxValue ? 0f : 1f - MathUtil.Clamp01(sinceFamine / FamineMemoryTicks);
            bool recentFamine = sinceFamine >= 0 && sinceFamine <= SimTime.TicksPerSeason;
            float desired = 1.1f + 0.3f * expansion + 0.6f * famineMemory;
            var previous = (FoodSecuritySituation)(int)MathUtil.Clamp(s.Mem(KeySituation), 0f, 5f);
            FoodSecurityForecaster.Classify(f, desired, previous, recentFamine);

            Publish(s, f, previous);
            if (f.Situation == FoodSecuritySituation.None) return;

            // ---------------------------------------------------------------- votes
            float buildPush = BuildPush(f);
            float jobPush = JobPush(f, buildPush, industry);
            if (buildPush > 0f) buildPush *= LaborAndPendingRelief(f);
            VoteProducers(db, civ, s, race, f, buildPush);
            VoteStorage(db, civ, s, f);
            VoteJobs(civ, s, jobPush);
        }

        // ==================================================================== survey
        private void CountPeople(WorldState state, Settlement s, RaceDefinition race)
        {
            _pop = _adults = _unemployed = _elders = _partnered = _singleF = _singleM = 0;
            int maxChildren = race != null ? race.MaxChildren : 5;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = state.People.Get(ids[i]);
                if (p == null || !p.IsAlive) continue;
                _pop++;
                if (p.Stage == LifeStage.Child) continue;
                _adults++;
                if (p.Job == JobType.None && p.ArmyId < 0) _unemployed++;
                if (p.Stage == LifeStage.Elder) { _elders++; continue; }
                if (p.Sex == Sex.Female)
                {
                    if (p.SpouseId >= 0) { if (p.ChildrenCount < maxChildren) _partnered++; }
                    else _singleF++;
                }
                else if (p.SpouseId < 0) _singleM++;
            }
        }

        private float GrowthPerTick(RaceDefinition race, Civilization civ)
        {
            float fert = race != null ? MathUtil.Clamp(MathUtil.Sanitize(race.FertilityPerYear, 0.25f), 0f, 3f) : 0.25f;
            float growthMod = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Growth, 1f), 0f, 5f);
            float births = (_partnered + 0.4f * Math.Min(_singleF, _singleM)) * fert * growthMod;
            float elderYears = race != null ? Math.Max(5f, race.LifeExpectancy - race.ElderAge) : 15f;
            float deaths = _elders / elderYears;
            return MathUtil.Sanitize((births - deaths) / SimTime.TicksPerYear);
        }

        private void ScanBuildings(WorldState state, GameDatabase db, Settlement s, float prodMul)
        {
            Array.Clear(_typeCount, 0, _typeCount.Length);
            Array.Clear(_queued, 0, _queued.Length);
            Array.Clear(_jobSlots, 0, _jobSlots.Length);
            Array.Clear(_jobYield, 0, _jobYield.Length);
            _openFoodSlots = 0; _estIncome = 0f; _pendingOutput = 0f;

            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= _typeCount.Length) continue;
                _typeCount[t]++;
                if (!_catalog.IsProducer[t] || !db.TryGetBuilding(b.Type, out var def)) continue;
                float fit = _terrain.FitFor(def.Placement, def.Width * def.Height);
                if (fit < 0.5f) fit = 0.5f; // it already stands on a site the planner accepted
                float perWorker = _catalog.FoodPerWorker[t] * fit * prodMul;
                if (!b.IsComplete) { _pendingOutput += perWorker * def.JobSlots * 0.75f; continue; }
                int workers = b.Workers.Count;
                _openFoodSlots += Math.Max(0, def.JobSlots - workers);
                int j = (int)def.Job;
                _jobSlots[j] += def.JobSlots;
                if (perWorker > _jobYield[j]) _jobYield[j] = perWorker;
                _estIncome += perWorker * Math.Min(workers, def.JobSlots) * MathUtil.Clamp(b.Condition, 0.2f, 1f);
            }
            var queue = s.BuildQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= _queued.Length) continue;
                _queued[t]++;
                if (_catalog.IsProducer[t] && db.TryGetBuilding(o.Type, out var def))
                    _pendingOutput += _catalog.FoodPerWorker[t] * def.JobSlots * prodMul * 0.6f;
            }
        }

        // ==================================================================== pushes
        private static float BuildPush(FoodSecurityForecast f)
        {
            switch (f.Situation)
            {
                case FoodSecuritySituation.Crisis: return 1f;
                case FoodSecuritySituation.WinterShortfall:
                    return MathUtil.Clamp(0.5f + 0.5f * (1f - MathUtil.Clamp01(f.WinterCoverage / f.DesiredCoverage)), 0.5f, 1f);
                case FoodSecuritySituation.Deficit:
                {
                    float p = 0.35f + 0.4f * MathUtil.Clamp01(-f.AnnualNetRatio / 0.5f);
                    return f.StarveDay >= 0 ? Math.Max(p, 0.45f) : p;
                }
                case FoodSecuritySituation.Stable: return f.WinterCoverage < f.DesiredCoverage * 1.3f ? 0.15f : 0f;
                case FoodSecuritySituation.Surplus: return -0.3f * Math.Max(0.3f, f.SurplusStrength);
                default: return 0f;
            }
        }

        private float JobPush(FoodSecurityForecast f, float buildPush, float industry)
        {
            float p;
            switch (f.Situation)
            {
                case FoodSecuritySituation.Crisis: p = 1f; break;
                case FoodSecuritySituation.WinterShortfall: p = buildPush * 0.9f; break;
                case FoodSecuritySituation.Deficit: p = buildPush * 0.8f; break;
                case FoodSecuritySituation.Surplus:
                    p = -(0.3f + 0.5f * f.SurplusStrength) * (0.6f + 0.8f * industry);
                    return MathUtil.Clamp(p, -1f, 0f);
                default: return 0f;
            }
            // Labor shortage: food jobs stand empty while nobody is idle -> pull workers from elsewhere harder.
            if (_openFoodSlots > 0 && _unemployed == 0) p *= 1.25f;
            return MathUtil.Clamp(p, 0f, 1f);
        }

        /// <summary>Building more is less needed when empty food jobs can be staffed now or producers are already coming.</summary>
        private float LaborAndPendingRelief(FoodSecurityForecast f)
        {
            float winterSpan = Math.Max(12f, f.DaysUntilWinter + f.WinterLength * 0.4f);
            float gap = Math.Max(0f, f.AnnualConsumption - f.AnnualIncome) / SimTime.TicksPerYear;
            gap = Math.Max(gap, Math.Max(0f, f.DesiredCoverage * f.WinterNeed - f.StockAtWinter) / winterSpan);
            if (f.Situation == FoodSecuritySituation.Crisis) gap = Math.Max(gap, Math.Max(0f, f.ConsumptionNow - f.IncomeNow) + f.ConsumptionNow * 0.2f);
            if (gap <= 1e-4f) return 1f;
            float bestJob = 0f;
            for (int j = 0; j < _jobYield.Length; j++) if (_jobYield[j] > bestJob) bestJob = _jobYield[j];
            float staffable = Math.Min(_openFoodSlots, _unemployed) * bestJob * 0.8f;
            float relief = MathUtil.Clamp01((_pendingOutput + staffable) / gap);
            return 1f - 0.6f * relief;
        }

        // ==================================================================== producers
        private void VoteProducers(GameDatabase db, Civilization civ, Settlement s, RaceDefinition race, FoodSecurityForecast f, float push)
        {
            bool crisis = f.Situation == FoodSecuritySituation.Crisis;
            float bestScore = 0f;
            for (int k = 0; k < _catalog.ProducerCount; k++)
            {
                var t = _catalog.Producers[k];
                int ti = (int)t;
                _score[ti] = 0f; _expected[ti] = 0f;
                if (!db.IsBuildingAvailable(civ, t) || !db.TryGetBuilding(t, out var def) || !PassesLocalGates(def, s)) continue;
                float fit = _terrain.FitFor(def.Placement, def.Width * def.Height);
                if (fit < MinFit) continue;
                float expected = _catalog.FoodPerWorker[ti] * fit * _catalog.InputFactor[ti] + 0.3f * _catalog.ExtraPerWorker[ti];
                float score = expected * (1f + 0.06f * (int)def.MinAge) * (0.8f + 0.2f * Math.Min(1f, def.JobSlots / 4f));
                if (IsFavored(race, t)) score *= 1.1f;
                if (crisis) score *= 0.75f + 0.25f * MathUtil.Clamp01(20f / Math.Max(1f, def.BuildWork)); // quick builds when starving
                _expected[ti] = MathUtil.Sanitize(expected);
                _score[ti] = MathUtil.Sanitize(score);
                if (_score[ti] > bestScore) bestScore = _score[ti];
            }
            if (bestScore <= 0f) return;

            for (int k = 0; k < _catalog.ProducerCount; k++)
            {
                var t = _catalog.Producers[k];
                int ti = (int)t;
                if (_score[ti] <= 0f) continue;
                int newer = NewerAlternative(db, t);
                if (newer >= 0)
                {
                    float ratio = _expected[ti] / Math.Max(1e-4f, _expected[newer]);
                    float v = -(0.35f + 0.45f * (1f - MathUtil.Clamp01(ratio / ObsoleteRatio)));
                    string why = -v >= StrongVote
                        ? "obsolete: " + db.Buildings[newer].Name + " yields " + (1f / Math.Max(0.05f, ratio)).ToString("0.0") + "x per worker"
                        : null;
                    VoteBuild(s, civ, t, v, why);
                    continue;
                }
                float rel = _score[ti] / bestScore;
                if (push > 0f)
                {
                    if (rel < 0.35f) continue;
                    float v = push * rel;
                    VoteBuild(s, civ, t, v, v >= StrongVote ? Reason(f) : null);
                }
                else if (push < 0f) VoteBuild(s, civ, t, push * (1.2f - 0.4f * rel));
            }
        }

        /// <summary>Index of a newer-age, available producer that out-yields this one per worker by 1/ObsoleteRatio, else -1.</summary>
        private int NewerAlternative(GameDatabase db, BuildingType t)
        {
            int ti = (int)t;
            var age = db.Buildings[ti].MinAge;
            int best = -1;
            float bestExp = _expected[ti] / ObsoleteRatio;
            for (int k = 0; k < _catalog.ProducerCount; k++)
            {
                int m = (int)_catalog.Producers[k];
                if (m == ti || _score[m] <= 0f) continue;
                if (db.Buildings[m].MinAge <= age) continue;
                if (_expected[m] > bestExp) { bestExp = _expected[m]; best = m; }
            }
            return best;
        }

        private bool PassesLocalGates(BuildingDefinition def, Settlement s)
        {
            if (def.IsWonder || def.UniquePerCivilization) return false;
            if (_pop < def.MinPopulation || s.Tier < def.MinTier) return false;
            int t = (int)def.Type;
            if (def.MaxPerSettlement >= 0 && _typeCount[t] + _queued[t] >= def.MaxPerSettlement) return false;
            return true;
        }

        private static bool IsFavored(RaceDefinition race, BuildingType t)
        {
            if (race == null || race.FavoredBuildings == null) return false;
            var fav = race.FavoredBuildings;
            for (int i = 0; i < fav.Length; i++) if (fav[i] == t) return true;
            return false;
        }

        // ==================================================================== storage
        private void VoteStorage(GameDatabase db, Civilization civ, Settlement s, FoodSecurityForecast f)
        {
            if (_catalog.StorageCount == 0) return;
            float cap = Math.Max(1f, f.Capacity);
            float proximity = f.InWinter ? 1f : 1f - MathUtil.Clamp01(f.DaysUntilWinter / 72f);
            float strength = 0f;
            string why = null;

            bool spoiling = (f.Stock >= 0.85f * cap && f.NetNow > 0f)
                || (f.ProjectedSpoilage > 0.05f * cap && (f.Situation == FoodSecuritySituation.Surplus || f.Situation == FoodSecuritySituation.Stable));
            if (spoiling)
            {
                float fullness = MathUtil.Clamp01(f.Stock / cap);
                strength = 0.45f + 0.45f * Math.Max(f.SurplusStrength, MathUtil.Clamp01((fullness - 0.7f) / 0.3f));
                why = "surplus spoiling: stores " + (fullness * 100f).ToString("0") + "% full";
            }
            float winterNeed = Math.Max(f.WinterNeed, f.SecondWinterNeed) * f.DesiredCoverage;
            bool tooSmall = winterNeed > cap * 0.9f;
            bool wastedBeforeWinter = f.SpoilageBeforeWinter > 0.02f * cap && f.WinterCoverage < f.DesiredCoverage * 1.5f;
            if (tooSmall || wastedBeforeWinter)
            {
                float v = f.Situation == FoodSecuritySituation.Crisis ? 0.3f : 0.55f + 0.35f * proximity;
                if (v > strength)
                {
                    strength = v;
                    why = "before winter: need " + winterNeed.ToString("0") + " food, stores hold " + cap.ToString("0");
                }
            }
            if (strength <= 0f) return;

            int best = -1;
            float bestScore = 0f;
            for (int k = 0; k < _catalog.StorageCount; k++)
            {
                var t = _catalog.Storage[k];
                if (!db.IsBuildingAvailable(civ, t) || !db.TryGetBuilding(t, out var def) || !PassesLocalGates(def, s)) continue;
                float cost = def.BuildWork;
                var c = def.Cost;
                if (c != null) for (int i = 0; i < c.Length; i++) cost += Math.Max(0f, c[i].Amount);
                float score = def.StorageCapacity / (1f + cost);
                if (t == BuildingType.Granary) score *= 1.6f; // purpose-built to keep the harvest
                score = MathUtil.Sanitize(score);
                if (score > bestScore) { bestScore = score; best = (int)t; }
            }
            if (best < 0) return;
            VoteBuild(s, civ, (BuildingType)best, strength, strength >= StrongVote ? why : null);
        }

        // ==================================================================== jobs
        private void VoteJobs(Civilization civ, Settlement s, float push)
        {
            if (push == 0f) return;
            float best = 0f;
            for (int j = 1; j < _jobYield.Length; j++) if (_jobSlots[j] > 0 && _jobYield[j] > best) best = _jobYield[j];
            if (best <= 0f) return;
            for (int j = 1; j < _jobSlots.Length; j++)
            {
                if (_jobSlots[j] <= 0) continue;
                float rel = _jobYield[j] / best;
                float v = push > 0f ? push * (0.5f + 0.5f * rel) : push * (1.5f - 0.5f * rel); // shortage: best jobs most; surplus: worst first
                VoteJob(s, civ, (JobType)j, MathUtil.Clamp(v, -1f, 1f));
            }
        }

        // ==================================================================== explanation & blackboard
        private void Publish(Settlement s, FoodSecurityForecast f, FoodSecuritySituation previous)
        {
            Urgency(s, f.Urgency);
            s.Memory[KeySituation] = (float)f.Situation;
            s.Memory[KeyCoverage] = f.WinterCoverage;
            s.Memory[KeyDays] = Math.Min(9999f, f.DaysOfFood);
            s.Memory[KeyStarveDay] = f.StarveDay;
            if (previous != f.Situation && f.Situation != FoodSecuritySituation.None && previous != FoodSecuritySituation.None)
                s.LogAi("[" + Id + "] " + previous + " -> " + f.Situation + ": " + Reason(f));
        }

        /// <summary>Short, player-facing reason for the current outlook (built at most once per settlement per run).</summary>
        private string Reason(FoodSecurityForecast f)
        {
            if (_reason != null) return _reason;
            switch (f.Situation)
            {
                case FoodSecuritySituation.Crisis:
                    _reason = f.StarveDay >= 0
                        ? "famine: food runs out in " + f.StarveDay + " days"
                        : "famine: " + f.DaysOfFood.ToString("0") + " days of food";
                    break;
                case FoodSecuritySituation.WinterShortfall:
                    _reason = (f.InWinter ? "winter, " + f.WinterLength + " days left" : "winter in " + f.DaysUntilWinter + " days")
                        + "; stock covers " + Pct(f.WinterCoverage) + "%";
                    break;
                case FoodSecuritySituation.Deficit:
                    _reason = f.StarveDay >= 0
                        ? "2-year forecast: stores run dry in " + f.StarveDay + " days"
                        : (f.AnnualNetRatio < 0f ? "annual deficit " + Pct(-f.AnnualNetRatio) + "%" : "next winter covered only " + Pct(f.SecondWinterCoverage) + "%");
                    break;
                case FoodSecuritySituation.Surplus:
                    _reason = "surplus +" + Pct(f.AnnualNetRatio) + "%/yr; freeing labor";
                    break;
                default:
                    _reason = "food stable; winter covered " + Pct(f.WinterCoverage) + "%";
                    break;
            }
            return _reason;
        }

        private static string Pct(float ratio) => (MathUtil.Clamp(MathUtil.Sanitize(ratio), 0f, 9.99f) * 100f).ToString("0");
    }
}
