using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Settlement governor (utility AI): every 6 ticks, for every living settlement, it evaluates needs
    /// (<see cref="NeedEvaluator"/>), filters buildable candidates (availability, tier, population,
    /// MaxPerSettlement, hard placement feasible in the cached territory scan), scores them
    /// (<see cref="BuildingScorer"/>) and appends the best one or two <see cref="BuildOrder"/>s to
    /// <see cref="Settlement.BuildQueue"/> for the town planner. It also drops stale or invalid orders and
    /// writes <see cref="Settlement.JobPriority"/>. It never orders wonders (civ strategy does) and never
    /// creates entities itself. Every decision is logged with numbers via <see cref="Settlement.LogAi"/>.
    /// <para>
    /// Deterministic: no randomness, candidates are visited in <see cref="BuildingType"/> order and ties
    /// keep the lowest type id; all persistent decision state lives in <see cref="Settlement.Memory"/>
    /// (keys in <see cref="GovernorKeys"/> and <see cref="TerritoryScanner"/>), so save/load is seamless.
    /// </para>
    /// </summary>
    public sealed class SettlementGovernorSystem : ISimSystem
    {
        private static readonly RaceDefinition DefaultRace = new RaceDefinition();

        /// <summary>Lowest job priority written (after council advice).</summary>
        public const float MinJobPriority = 0.1f;
        /// <summary>Highest job priority written (after council advice).</summary>
        public const float MaxJobPriority = 5f;

        private readonly NeedEvaluator _evaluator;
        private readonly BuildingScorer _scorer;
        private readonly SettlementSurvey _survey = new SettlementSurvey();
        private readonly SettlementNeeds _needs = new SettlementNeeds();
        private readonly SettlementNeeds _reported = new SettlementNeeds();
        private readonly GovernorPersonality _personality = new GovernorPersonality();
        private readonly List<BuildingProfile> _candidates = new List<BuildingProfile>(Counts.Buildings);
        private readonly bool[] _foodJobs = new bool[Counts.Jobs];
        private readonly bool[][] _jobResources = new bool[Counts.Jobs][];
        private BuildingProfile[] _profiles = new BuildingProfile[Counts.Buildings];
        private GameDatabase _boundDb;

        /// <summary>Creates the governor with default settings.</summary>
        public SettlementGovernorSystem() : this(null) { }

        /// <summary>Creates the governor with custom settings (sanitized; null = defaults).</summary>
        public SettlementGovernorSystem(GovernorSettings settings)
        {
            Settings = (settings ?? new GovernorSettings()).Sanitize();
            _evaluator = new NeedEvaluator(Settings);
            _scorer = new BuildingScorer(Settings);
            for (int j = 0; j < _jobResources.Length; j++) _jobResources[j] = new bool[Counts.Resources];
        }

        /// <inheritdoc />
        public string Name => "AI.SettlementGovernor";

        /// <inheritdoc />
        public int TickInterval => 6;

        /// <summary>Tuning in use.</summary>
        public GovernorSettings Settings { get; }

        /// <summary>
        /// Needs of the most recently governed settlement as evaluated before this run's orders (debug/UI;
        /// overwritten for every settlement). The same values are published as <c>ai.need.*</c> memory keys.
        /// </summary>
        public SettlementNeeds LastNeeds => _reported;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            if (ctx != null && ctx.Db != null) BindDatabase(ctx.Db);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            BindDatabase(ctx.Db);
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (s == null || !s.IsAlive) continue;
                GovernSettlement(ctx, s);
            }
        }

        /// <summary>Runs one full governor pass for a single settlement (what <see cref="Tick"/> does for each one).</summary>
        public void GovernSettlement(SimContext ctx, Settlement s)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || s == null || !s.IsAlive) return;
            BindDatabase(ctx.Db);
            var state = ctx.State;
            var db = ctx.Db;
            long tick = ctx.Tick;
            var civ = state.Civilizations.Get(s.CivilizationId);
            if (civ == null || !civ.IsAlive) return;
            var race = RaceOf(db, civ.Race);
            _personality.Load(civ, db); // traits drift with history (AdvisorBoard), race defaults otherwise

            var features = TerritoryScanner.GetOrScan(state.Map, s, tick, Settings.TerritoryRefreshTicks);
            float growth = UpdateGrowth(s, tick);

            _survey.Collect(state, db, s, _profiles);
            bool changed = CleanQueue(db, s, civ, tick);
            changed |= BreakStall(s, tick);
            if (changed) _survey.Collect(state, db, s, _profiles);
            CollectCandidates(state, db, s, civ, features, tick);
            _evaluator.Evaluate(state, s, civ, race, _personality, _survey, _candidates, _profiles, growth, tick, _needs);
            _reported.CopyFrom(_needs);
            WriteDiagnostics(s);

            if (_survey.Population > 0) PlanOrders(state, db, s, civ, race, features, growth, tick);

            WriteJobPriorities(s);
        }

        // ================================================================== database binding
        private void BindDatabase(GameDatabase db)
        {
            if (ReferenceEquals(db, _boundDb)) return;
            _boundDb = db;
            _profiles = new BuildingProfile[Counts.Buildings];
            Array.Clear(_foodJobs, 0, _foodJobs.Length);
            for (int j = 0; j < _jobResources.Length; j++) Array.Clear(_jobResources[j], 0, _jobResources[j].Length);
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (!db.TryGetBuilding((BuildingType)t, out var def)) continue;
                var p = new BuildingProfile(def);
                _profiles[t] = p;
                int job = (int)def.Job;
                if (def.Job == JobType.None || job < 0 || job >= Counts.Jobs) continue;
                for (int r = 0; r < Counts.Resources; r++)
                    if (p.NetOutput[r] > 0f) _jobResources[job][r] = true;
                if (p.ProducesFood) _foodJobs[job] = true;
            }
            _foodJobs[(int)JobType.Gatherer] = _foodJobs[(int)JobType.Hunter] = _foodJobs[(int)JobType.Fisher] = true;
            _foodJobs[(int)JobType.Farmer] = _foodJobs[(int)JobType.Herder] = true;
        }

        private static RaceDefinition RaceOf(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            var races = db.Races;
            return i >= 0 && i < races.Count && races[i] != null ? races[i] : DefaultRace;
        }

        // ================================================================== growth tracking
        private static float UpdateGrowth(Settlement s, long tick)
        {
            int pop = s.Population;
            float growth = MathUtil.Sanitize(s.Mem(GovernorKeys.Growth));
            if (!s.Memory.TryGetValue(GovernorKeys.GrowthTick, out float lastTick) || lastTick > tick)
            {
                s.Memory[GovernorKeys.GrowthPop] = pop;
                s.Memory[GovernorKeys.GrowthTick] = tick;
                return growth;
            }
            long dt = tick - (long)lastTick;
            if (dt < SimTime.TicksPerSeason) return growth;
            float rate = (pop - s.Mem(GovernorKeys.GrowthPop)) * SimTime.TicksPerYear / dt;
            growth += (MathUtil.Sanitize(rate) - growth) * 0.35f;
            growth = MathUtil.Clamp(MathUtil.Sanitize(growth), -1000f, 1000f);
            s.Memory[GovernorKeys.Growth] = growth;
            s.Memory[GovernorKeys.GrowthPop] = pop;
            s.Memory[GovernorKeys.GrowthTick] = tick;
            return growth;
        }

        // ================================================================== queue maintenance
        /// <summary>Drops stale/invalid non-wonder orders and records placement trouble. Returns true if anything changed.</summary>
        private bool CleanQueue(GameDatabase db, Settlement s, Civilization civ, long tick)
        {
            bool changed = false;
            var queue = s.BuildQueue;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var o = queue[i];
                if (o == null) { queue.RemoveAt(i); changed = true; continue; }
                if (!db.TryGetBuilding(o.Type, out var def))
                {
                    queue.RemoveAt(i);
                    changed = true;
                    s.LogAi("Dropped order " + o.Type + ": no building definition");
                    continue;
                }
                if (def.IsWonder || def.Category == BuildingCategory.Wonder) continue; // civ strategy owns wonder orders
                if (o.RequestedTick > tick) o.RequestedTick = tick; // e.g. clock reset by a load: restart the stale timer
                long age = tick - o.RequestedTick;
                string why = null;
                if (age > Settings.StaleOrderTicks) why = "stale (" + GovernorText.F1(age / (float)SimTime.TicksPerYear) + "y old)";
                else if (!db.IsBuildingAvailable(civ, o.Type)) why = "no longer available";
                else if (def.MaxPerSettlement >= 0 && _survey.Existing[(int)o.Type] >= def.MaxPerSettlement) why = "limit of " + def.MaxPerSettlement + " reached";
                if (why != null)
                {
                    queue.RemoveAt(i);
                    changed = true;
                    s.LogAi("Dropped order " + GovernorText.NameOf(def) + ": " + why);
                    continue;
                }
                if (o.Attempts >= Settings.PlacementFailureAttempts && IsGovernorOrder(o))
                {
                    string key = GovernorKeys.BlockedUntilByType[(int)o.Type];
                    if (s.Mem(key, -1f) < tick) s.Memory[key] = tick + Settings.PlacementBlockTicks;
                }
            }
            return changed;
        }

        private static bool IsGovernorOrder(BuildOrder o) =>
            o != null && o.Reason != null && o.Reason.StartsWith(GovernorKeys.ReasonPrefix, StringComparison.Ordinal);

        // ================================================================== candidates
        private void CollectCandidates(WorldState state, GameDatabase db, Settlement s, Civilization civ, TerritoryFeatures features, long tick)
        {
            _candidates.Clear();
            int pop = s.Population;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                var p = _profiles[t];
                if (p == null || p.IsWonder) continue;
                var def = p.Def;
                var type = (BuildingType)t;
                if (!db.IsBuildingAvailable(civ, type)) continue;
                if (s.Tier < def.MinTier || pop < def.MinPopulation) continue;
                if (def.MaxPerSettlement >= 0 && _survey.CountWithQueue(type) >= def.MaxPerSettlement) continue;
                if (!TerritoryScanner.IsPlacementFeasible(def.Placement, features)) continue;
                if (s.Mem(GovernorKeys.BlockedUntilByType[t], -1f) > tick) continue;
                if (def.UniquePerCivilization && CivHas(state, civ, type)) continue;
                _candidates.Add(p);
            }
        }

        private static bool CivHas(WorldState state, Civilization civ, BuildingType type)
        {
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var o = state.Settlements.Get(ids[i]);
                if (o == null || !o.IsAlive) continue;
                for (int b = 0; b < o.BuildingIds.Count; b++)
                {
                    var bld = state.Buildings.Get(o.BuildingIds[b]);
                    if (bld != null && !bld.IsDestroyed && bld.Type == type) return true;
                }
                for (int q = 0; q < o.BuildQueue.Count; q++) if (o.BuildQueue[q] != null && o.BuildQueue[q].Type == type) return true;
            }
            return false;
        }

        private bool QueueAllows(BuildingProfile p)
        {
            int t = (int)p.Type;
            int cap = p.Def.IsDwelling ? Settings.MaxQueuedDwellingsPerType : Settings.MaxQueuedPerType;
            if (_survey.Queued[t] >= cap) return false;
            int max = p.Def.MaxPerSettlement;
            return max < 0 || _survey.CountWithQueue(p.Type) < max;
        }

        // ================================================================== ordering
        private void PlanOrders(WorldState state, GameDatabase db, Settlement s, Civilization civ, RaceDefinition race,
                                TerritoryFeatures features, float growth, long tick)
        {
            int queueLimit = features.FreeTiles > 0 ? Settings.QueueLimit : 1; // no room: one order at a time (planner may expand)
            int added = 0;
            bool evicted = false;
            bool idle = _survey.QueueCount == 0 && _survey.UnderConstructionTotal == 0;

            // A settlement without a town center rebuilds it before anything else.
            var tc = _profiles[(int)BuildingType.TownCenter];
            if (tc != null && !tc.IsWonder && !_survey.HasTownCenter && _survey.Queued[(int)BuildingType.TownCenter] == 0
                && db.IsBuildingAvailable(civ, BuildingType.TownCenter))
            {
                if (_survey.QueueCount >= queueLimit && TryEvict(s, 12f, 1f, true)) { evicted = true; _survey.Collect(state, db, s, _profiles); }
                if (_survey.QueueCount < queueLimit)
                {
                    AddOrder(s, tc, 1.2f, "town center missing", tick);
                    _survey.RegisterQueued(tc);
                    added++;
                }
            }

            bool dirty = added > 0 || evicted;
            while (added < Settings.MaxOrdersPerRun)
            {
                if (dirty)
                {
                    CollectCandidates(state, db, s, civ, features, tick);
                    _evaluator.Evaluate(state, s, civ, race, _personality, _survey, _candidates, _profiles, growth, tick, _needs);
                    dirty = false;
                }
                _scorer.Prepare(_candidates);
                float ageCount = CurrentAgeBuildings(civ);
                BuildingProfile best = null;
                float bestU = 0f;
                int bestSlot = -1;
                for (int c = 0; c < _candidates.Count; c++)
                {
                    var p = _candidates[c];
                    if (!QueueAllows(p)) continue;
                    float u = _scorer.Score(p, _needs, _survey, s, civ, race, _personality, growth, tick, ageCount, out int slot);
                    if (u > bestU) { bestU = u; best = p; bestSlot = slot; }
                }
                float threshold = added > 0 ? Settings.SecondOrderThreshold : idle ? Settings.IdleOrderThreshold : Settings.OrderThreshold;
                if (best == null || bestU < threshold) break;
                if (_survey.QueueCount >= queueLimit)
                {
                    if (added > 0 || evicted || !TryEvict(s, bestU * 10f, bestU, false)) break;
                    evicted = true;
                    _survey.Collect(state, db, s, _profiles);
                    dirty = true;
                    continue;
                }
                AddOrder(s, best, bestU, Explain(best, bestSlot, s), tick);
                if (bestSlot == SettlementNeeds.Storage)
                {
                    s.Memory[GovernorKeys.StorageOrderTick] = tick;
                    s.Memory[GovernorKeys.StorageOrderCapacity] = MathUtil.Sanitize(s.Stockpile.Capacity);
                }
                _survey.RegisterQueued(best);
                added++;
                dirty = true;
            }
        }

        /// <summary>Removes the lowest-priority governor order when a much more urgent candidate waits on a full queue.</summary>
        private bool TryEvict(Settlement s, float newPriority, float newUtility, bool force)
        {
            if (!force && newUtility < Settings.EvictionUtility) return false;
            var queue = s.BuildQueue;
            int victim = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (!IsGovernorOrder(o)) continue;
                int t = (int)o.Type;
                if (t >= 0 && t < _profiles.Length && _profiles[t] != null && _profiles[t].IsWonder) continue;
                if (o.Priority < lowest) { lowest = o.Priority; victim = i; }
            }
            if (victim < 0 || lowest + Settings.EvictionMargin >= newPriority) return false;
            var v = queue[victim];
            queue.RemoveAt(victim);
            s.LogAi("Dropped order " + v.Type + " (p=" + GovernorText.F1(v.Priority) + ") for a more urgent need (p=" + GovernorText.F1(newPriority) + ")");
            return true;
        }

        private void AddOrder(Settlement s, BuildingProfile p, float utility, string explanation, long tick)
        {
            utility = MathUtil.Clamp(MathUtil.Sanitize(utility), 0f, 10f);
            string u = "u=" + GovernorText.F2(utility);
            string logged = u;
            float bias = MathUtil.Sanitize(AdvisorBoard.BuildBias(s, p.Type));
            if (Math.Abs(bias) >= 0.05f)
            {
                // The persisted order reason may only use persisted state (the board total). The per-advisor breakdown is
                // recovered from Settlement.AiLog, which is debug-only and not saved, so it goes to the AI log line only;
                // otherwise a reloaded world would write different order reasons than an uninterrupted run.
                u += "; advisors: council " + GovernorText.Signed(bias);
                logged += "; advisors: " + GovernorText.AdvisorSummary(s.AiLog, p.Type, bias, 2);
            }
            s.BuildQueue.Add(new BuildOrder
            {
                Type = p.Type,
                Priority = utility * 10f,
                RequestedTick = tick,
                Reason = GovernorKeys.ReasonPrefix + explanation + " (" + u + ")",
            });
            s.Memory[GovernorKeys.LastOrderedByType[(int)p.Type]] = tick;
            s.Memory[GovernorKeys.LastOrderTick] = tick;
            s.LogAi("Build " + GovernorText.NameOf(p.Def) + " (" + logged + "): " + explanation);
        }

        /// <summary>Non-dwelling buildings (existing or queued) whose MinAge is the civ's current age.</summary>
        private float CurrentAgeBuildings(Civilization civ)
        {
            int n = 0;
            for (int t = 0; t < _profiles.Length; t++)
            {
                var p = _profiles[t];
                if (p == null || p.Def.IsDwelling || p.Def.MinAge != civ.Age || p.Type == BuildingType.TownCenter) continue;
                n += _survey.CountWithQueue(p.Type);
            }
            return n;
        }

        /// <summary>
        /// Stall breaker: when no building has been added for <see cref="GovernorSettings.StallTicks"/> while governor
        /// orders that cannot be paid for clog the queue, drops the least urgent of them so cheaper, feasible work
        /// (often the producer of the missing resource) can be planned. Returns true if the queue changed.
        /// </summary>
        private bool BreakStall(Settlement s, long tick)
        {
            int count = 0;
            for (int t = 0; t < _survey.Existing.Length; t++) count += _survey.Existing[t];
            if (!s.Memory.TryGetValue(GovernorKeys.ProgressTick, out float since) || since > tick || (int)s.Mem(GovernorKeys.ProgressCount, -1f) != count)
            {
                s.Memory[GovernorKeys.ProgressCount] = count;
                s.Memory[GovernorKeys.ProgressTick] = tick;
                return false;
            }
            if (tick - (long)since < Settings.StallTicks) return false;
            var queue = s.BuildQueue;
            int victim = -1;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (!IsGovernorOrder(o)) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= _profiles.Length || _profiles[t] == null || _profiles[t].IsWonder) continue;
                if (s.Stockpile.CanAfford(_profiles[t].Def.Cost)) continue;
                if (victim < 0 || o.Priority < queue[victim].Priority) victim = i;
            }
            s.Memory[GovernorKeys.ProgressTick] = tick; // check again after another stall period
            if (victim < 0) return false;
            var v = queue[victim];
            queue.RemoveAt(victim);
            s.LogAi("Stall: dropped order " + v.Type + " (unaffordable, no new building for " + GovernorText.F1((tick - (long)since) / (float)SimTime.TicksPerYear) + "y)");
            return true;
        }

        /// <summary>Explains the need that drove a decision, with numbers.</summary>
        private string Explain(BuildingProfile p, int slot, Settlement s)
        {
            var n = _needs;
            var st = s.Stockpile;
            if (slot == SettlementNeeds.Housing)
                return "homeless " + n.Homeless + ", free housing " + GovernorText.I(n.FreeHousing) + ", pending " + GovernorText.I(n.PendingHousing)
                       + ", target " + GovernorText.I(n.TargetFreeHousing) + ", pop " + n.Population;
            if (slot == SettlementNeeds.Storage)
                return n.StorageResource.ToString().ToLowerInvariant() + " at " + GovernorText.I(n.StorageFill * 100f) + "% of storage " + GovernorText.I(st.Capacity);
            if (slot == SettlementNeeds.Defense)
            {
                string cause = n.AtWar ? "at war" : n.HostileNear ? "hostile neighbour" : n.Posture == 2 ? "militarist posture" : n.Posture == 1 ? "defensive posture" : "security";
                return cause + ", defense " + GovernorText.F1(n.DefenseCurrent) + "/" + GovernorText.F1(n.DefenseDesired) + ", threat " + GovernorText.F2(n.Threat);
            }
            if (slot >= SettlementNeeds.FirstResource && slot < SettlementNeeds.FirstService)
            {
                var r = (ResourceType)(slot - SettlementNeeds.FirstResource);
                string rn = r.ToString().ToLowerInvariant();
                if (r == ResourceType.Food)
                    return "food " + GovernorText.I(n.FoodDays) + " days, net " + GovernorText.F2(n.FoodNet) + "/day" + (n.FoodNetMeasured ? "" : " (est.)") + ", pop " + n.Population;
                int chain = n.ChainSource[(int)r];
                if (chain >= 0) return (BuildingType)chain + " lacks " + rn + " (" + GovernorText.I(st[r]) + " in stock)";
                if (r == ResourceType.Gold && n.GoldUpkeep > n.GoldIncome)
                    return "gold upkeep " + GovernorText.F2(n.GoldUpkeep) + " > income " + GovernorText.F2(n.GoldIncome) + "/day";
                return rn + " " + GovernorText.I(st[r]) + "/" + GovernorText.I(n.ResourceDemand[(int)r]) + " for upcoming costs, net " + GovernorText.F2(st.Net(r)) + "/day";
            }
            if (slot >= SettlementNeeds.FirstService && slot < SettlementNeeds.Count)
            {
                var sv = (ServiceType)(slot - SettlementNeeds.FirstService);
                return sv.ToString().ToLowerInvariant() + " coverage " + GovernorText.F2(s.ServiceCoverage[(int)sv]) + ", pop " + n.Population;
            }
            return "general development";
        }

        // ================================================================== outputs
        private void WriteDiagnostics(Settlement s)
        {
            var n = _reported;
            s.Memory[GovernorKeys.NeedHousing] = n.Values[SettlementNeeds.Housing];
            s.Memory[GovernorKeys.NeedFood] = n.Food;
            s.Memory[GovernorKeys.NeedDefense] = n.Values[SettlementNeeds.Defense];
            s.Memory[GovernorKeys.NeedStorage] = n.Values[SettlementNeeds.Storage];
            s.Memory[GovernorKeys.Threat] = n.Threat;
        }

        /// <summary>
        /// Writes job staffing weights: food jobs 2-3 below 20 days of food, Builder 1.5 with work queued,
        /// Soldier 0.5 peaceful / 1.5 defensive or threatened / 2 militarist / 2.5 at war, Scholar by
        /// Curiosity, Priest by Piety (current traits), Merchant when gold is short, resource jobs by their
        /// resource need, everything else 1; then adds <see cref="AdvisorBoard.JobBias"/> and clamps to [0.1, 5].
        /// </summary>
        private void WriteJobPriorities(Settlement s)
        {
            var traits = _personality;
            var jp = s.JobPriority;
            var n = _needs;
            for (int j = 0; j < jp.Length; j++)
            {
                if (j == (int)JobType.None) { jp[j] = 0f; continue; }
                float w = 1f;
                if (j < _jobResources.Length)
                {
                    float best = 0f;
                    var produced = _jobResources[j];
                    for (int r = 0; r < produced.Length; r++)
                        if (produced[r] && r != (int)ResourceType.Food && n.Values[SettlementNeeds.FirstResource + r] > best)
                            best = n.Values[SettlementNeeds.FirstResource + r];
                    w = 1f + 0.6f * best;
                }
                jp[j] = w;
            }

            float days = n.FoodDays;
            float food = days < 20f ? 2f + MathUtil.Clamp01((20f - days) / 20f)
                       : days < 40f ? 1.5f
                       : 1f + 0.3f * n.Food;
            for (int j = 0; j < jp.Length; j++) if (_foodJobs[j]) jp[j] = Math.Max(jp[j], food);

            jp[(int)JobType.Builder] = s.BuildQueue.Count > 0 || _survey.UnderConstructionTotal > 0 ? 1.5f : 1f;

            float soldier = n.AtWar ? 2.5f : n.Posture == 2 ? 2f : (n.Posture == 1 || n.HostileNear) ? 1.5f : 0.5f;
            jp[(int)JobType.Soldier] = soldier;

            jp[(int)JobType.Scholar] = 0.75f + MathUtil.Clamp01(traits.Curiosity) + 0.5f * n.Service(ServiceType.Research);
            jp[(int)JobType.Priest] = 0.75f + MathUtil.Clamp01(traits.Piety) + 0.5f * n.Service(ServiceType.Faith);
            jp[(int)JobType.Merchant] = n.Gold > 0.35f ? 1.5f + 0.5f * n.Gold : 0.8f + 0.4f * MathUtil.Clamp01(traits.Mercantilism);
            jp[(int)JobType.Healer] = Math.Max(jp[(int)JobType.Healer], 1f + 0.6f * n.Service(ServiceType.Health));
            jp[(int)JobType.Entertainer] = Math.Max(jp[(int)JobType.Entertainer], 1f + 0.4f * n.Service(ServiceType.Entertainment));
            jp[(int)JobType.Administrator] = Math.Max(jp[(int)JobType.Administrator], 1f + 0.4f * n.Service(ServiceType.Administration));

            for (int j = 1; j < jp.Length; j++) jp[j] = MathUtil.Clamp(MathUtil.Sanitize(jp[j], 1f), 0.25f, 3f);
            // Advisor Council staffing advice on top of the governor's own weights.
            for (int j = 1; j < jp.Length; j++)
                jp[j] = MathUtil.Clamp(jp[j] + MathUtil.Sanitize(AdvisorBoard.JobBias(s, (JobType)j)), MinJobPriority, MaxJobPriority);
        }
    }
}
