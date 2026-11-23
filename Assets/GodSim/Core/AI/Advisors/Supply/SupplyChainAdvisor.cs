using System;

namespace GodSim.Core.AI
{
    /// <summary>Settlement memory keys written by the supply-chain advisor (UI / debug / learning).</summary>
    public static class SupplyChainKeys
    {
        /// <summary>Worst bottleneck resource + 1 (0 = none).</summary>
        public const string WorstResource = "ai.supply.worst";
        /// <summary>Severity 0..1 of the worst bottleneck.</summary>
        public const string WorstSeverity = "ai.supply.sev";
    }

    /// <summary>
    /// Supply-chain advisor ("supply"): models every production chain from building data and keeps them flowing.
    /// <para>Each run, per settlement, it detects</para>
    /// <list type="bullet">
    /// <item><b>Starvation</b>: staffed consumers whose input stock is below one run of need while the input is not
    /// sustained (Net &lt; 0 or income below their draw) -> "Blacksmith idle: Metal short 0.9/run".</item>
    /// <item><b>Construction demand</b>: unpaid costs of buildings under construction and queued orders that stock plus
    /// expected income (48 ticks) cannot cover.</item>
    /// <item><b>Anticipation</b>: resources the current age's newly unlocked buildings will cost, stockpiled early.</item>
    /// <item><b>Overproduction</b>: outputs piling up near storage capacity with nothing in the build plan using them.</item>
    /// </list>
    /// Needs propagate upstream through producers' inputs (Tools -> Metal -> Ore, Goods -> Wood + Cloth, Machinery -> Metal +
    /// Fuel, Electronics -> Metal + Gold), decaying per step. Producers are voted by need x output share x racial
    /// production modifier x terrain feasibility (deposits / trees / fertility / water in territory) x upstream chain
    /// reachability, damped when existing producers still have open slots or one is already under construction. Consumers
    /// of a starved input and producers of an overflowing output get negative votes; job votes shift workers from
    /// saturated or idle chains to starved ones. Personality: industrious civs push harder; mercantile civs tolerate
    /// surpluses (trade goods). Deterministic, index-ordered, allocation-free except reason strings for strong votes.
    /// </summary>
    public sealed class SupplyChainAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in board keys.</summary>
        public const string Id = "supply";
        /// <summary>Planning horizon (ticks) used for coverage of costs and new buildings' inputs.</summary>
        public const float HorizonTicks = 48f;
        /// <summary>Need multiplier per upstream chain step.</summary>
        public const float ChainDecay = 0.8f;
        /// <summary>Maximum upstream propagation depth.</summary>
        public const int MaxChainDepth = 4;
        /// <summary>Storage fill ratio from which an output counts as piling up.</summary>
        public const float OverflowFill = 0.85f;
        /// <summary>How many queued orders are inspected for costs.</summary>
        public const int QueueLookahead = 10;
        /// <summary>Strength of the anticipation signal (current-age building costs).</summary>
        public const float AnticipationWeight = 0.35f;
        /// <summary>Minimum ticks between two logged reasons for the same building type in a settlement.</summary>
        public const long ReasonCooldownTicks = 48;

        private const float MinNeed = 0.02f;
        private const float ReachDiscount = 0.7f;
        private const float FoodDamp = 0.5f;

        private const byte KindNone = 0, KindStarved = 1, KindConstruction = 2, KindChain = 3, KindAnticipation = 4;

        private static readonly int R = Counts.Resources;
        private static readonly int B = Counts.Buildings;
        private static readonly string[] s_resNames = ResourceNames();

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 12;

        private readonly SupplyChainModel _model = new SupplyChainModel();
        private readonly SupplyChainTerrain _terrain = new SupplyChainTerrain();
        private int _runs;

        // ---- per-building scratch (reused every settlement)
        private readonly int[] _count = new int[Counts.Buildings];
        private readonly int[] _workers = new int[Counts.Buildings];
        private readonly int[] _open = new int[Counts.Buildings];
        private readonly int[] _pending = new int[Counts.Buildings];
        private readonly bool[] _available = new bool[Counts.Buildings];
        private readonly bool[] _canBuild = new bool[Counts.Buildings];
        private readonly float[] _terrainF = new float[Counts.Buildings];
        private readonly float[] _chainOk = new float[Counts.Buildings];

        // ---- per-resource scratch
        private readonly float[] _stock = new float[Counts.Resources];
        private readonly float[] _net = new float[Counts.Resources];
        private readonly float[] _income = new float[Counts.Resources];
        private readonly float[] _demand = new float[Counts.Resources];
        private readonly float[] _supply = new float[Counts.Resources];
        private readonly float[] _costNeed = new float[Counts.Resources];
        private readonly float[] _starve = new float[Counts.Resources];
        private readonly float[] _starveAmt = new float[Counts.Resources];
        private readonly int[] _starveBy = new int[Counts.Resources];
        private readonly float[] _build = new float[Counts.Resources];
        private readonly float[] _buildShort = new float[Counts.Resources];
        private readonly float[] _ageCost = new float[Counts.Resources];
        private readonly float[] _antic = new float[Counts.Resources];
        private readonly float[] _over = new float[Counts.Resources];
        private readonly float[] _need = new float[Counts.Resources];
        private readonly byte[] _kind = new byte[Counts.Resources];
        private readonly float[] _chain = new float[Counts.Resources];
        private readonly int[] _chainFrom = new int[Counts.Resources];
        private readonly int[] _chainVia = new int[Counts.Resources];
        private readonly float[] _total = new float[Counts.Resources];
        private readonly float[] _availMaxRate = new float[Counts.Resources];
        private readonly float[] _reach = new float[Counts.Resources];
        private readonly byte[] _reachState = new byte[Counts.Resources];
        private readonly float[] _jobVote = new float[Counts.Jobs];
        private float _cap = Stockpile.DefaultCapacity;

        /// <summary>The cached chain model (read-only use; diagnostics and tests).</summary>
        public SupplyChainModel Model => _model;

        /// <inheritdoc />
        public override void Initialize(SimContext ctx)
        {
            _terrain.Clear();
            _runs = 0;
            if (ctx != null && ctx.Db != null) _model.Build(ctx.Db);
        }

        /// <inheritdoc />
        protected override void AfterRun(SimContext ctx)
        {
            _runs++;
            if ((_runs & 15) == 0 && ctx != null && ctx.State != null) _terrain.Prune(ctx.State.Settlements);
        }

        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null || ctx.State == null || ctx.State.Map == null || s == null || civ == null) return;
            if (!ReferenceEquals(_model.Source, db)) _model.Build(db);
            float run = Math.Max(1, ctx.DeltaTicks);
            long tick = ctx.Tick;

            ResetScratch();
            Survey(ctx.State, s);
            var info = _terrain.Get(ctx.State.Map, s, tick);
            EvaluateBuildability(db, civ, s, info);
            DetectResources(s, run);
            Anticipate(civ);
            CombineNeeds();
            for (int r = 0; r < R; r++) if (_need[r] > 0.05f) Propagate(r, _need[r], 0);
            for (int r = 0; r < R; r++)
            {
                _total[r] = Math.Max(_need[r], _chain[r]);
                if (_chain[r] > _need[r] + 1e-4f) _kind[r] = KindChain;
            }

            float scale = 0.8f + 0.4f * AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            float overTol = 1.2f - 0.6f * AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
            float weight = AdvisorBoard.Weight(civ, Id);
            EmitBuildVotes(civ, s, info, tick, scale, overTol, weight);
            EmitJobVotes(civ, s, scale, overTol);
            EmitUrgency(s);
        }

        // ================================================================== survey
        private void ResetScratch()
        {
            Array.Clear(_count, 0, B); Array.Clear(_workers, 0, B); Array.Clear(_open, 0, B); Array.Clear(_pending, 0, B);
            Array.Clear(_demand, 0, R); Array.Clear(_supply, 0, R); Array.Clear(_costNeed, 0, R);
            Array.Clear(_starve, 0, R); Array.Clear(_starveAmt, 0, R); Array.Clear(_build, 0, R); Array.Clear(_buildShort, 0, R);
            Array.Clear(_ageCost, 0, R); Array.Clear(_antic, 0, R); Array.Clear(_over, 0, R); Array.Clear(_need, 0, R);
            Array.Clear(_kind, 0, R); Array.Clear(_chain, 0, R); Array.Clear(_total, 0, R); Array.Clear(_reachState, 0, R);
            for (int r = 0; r < R; r++) { _starveBy[r] = -1; _chainFrom[r] = -1; _chainVia[r] = -1; }
        }

        private void Survey(WorldState state, Settlement s)
        {
            var ids = s.BuildingIds;
            var store = state.Buildings;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = store.Get(ids[k]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= B) continue;
                var def = _model.Definitions[t];
                if (def == null) continue;
                if (!b.IsComplete)
                {
                    _pending[t]++;
                    if (!b.CostPaid) AddCost(def.Cost, 1f);
                    continue;
                }
                _count[t]++;
                if (!def.HasJobs) continue;
                int w = Math.Min(b.Workers.Count, def.JobSlots);
                _workers[t] += w;
                _open[t] += def.JobSlots - w;
                if (w <= 0) continue;
                AddFlow(def.InputsPerWorker, _demand, w);
                AddFlow(def.OutputsPerWorker, _supply, w * MathUtil.Clamp01(b.Condition));
            }
            var q = s.BuildQueue;
            int n = Math.Min(q.Count, QueueLookahead);
            for (int i = 0; i < n; i++)
            {
                var order = q[i];
                if (order == null) continue;
                int t = (int)order.Type;
                if (t < 0 || t >= B || _model.Definitions[t] == null) continue;
                AddCost(_model.Definitions[t].Cost, i < 4 ? 1f : 0.5f);
            }
        }

        private void AddCost(ResourceAmount[] costs, float w)
        {
            if (costs == null) return;
            for (int i = 0; i < costs.Length; i++)
            {
                int r = (int)costs[i].Type;
                float a = MathUtil.Sanitize(costs[i].Amount);
                if (r >= 0 && r < R && a > 0f) _costNeed[r] += a * w;
            }
        }

        private static void AddFlow(ResourceAmount[] flows, float[] into, float workers)
        {
            if (flows == null) return;
            for (int i = 0; i < flows.Length; i++)
            {
                int r = (int)flows[i].Type;
                float a = MathUtil.Sanitize(flows[i].Amount);
                if (r >= 0 && r < R && a > 0f) into[r] += a * workers;
            }
        }

        private void EvaluateBuildability(GameDatabase db, Civilization civ, Settlement s, SupplyChainTerrainInfo info)
        {
            int pop = s.Population;
            for (int b = 0; b < B; b++)
            {
                var def = _model.Definitions[b];
                bool avail = def != null && db.IsBuildingAvailable(civ, (BuildingType)b);
                _available[b] = avail;
                _canBuild[b] = avail && !def.IsWonder && !def.UniquePerCivilization && s.Tier >= def.MinTier && pop >= def.MinPopulation &&
                               (def.MaxPerSettlement < 0 || _count[b] + _pending[b] < def.MaxPerSettlement);
                _terrainF[b] = avail ? SupplyChainTerrain.Feasibility(def.Placement, info, _count[b] + _pending[b]) : 0f;
                _chainOk[b] = -1f;
            }
            for (int r = 0; r < R; r++)
            {
                float best = 0f;
                var prods = _model.ProducersOf(r);
                for (int k = 0; k < prods.Length; k++)
                {
                    int p = prods[k];
                    if (_canBuild[p] && _terrainF[p] > 0f) best = Math.Max(best, _model.ProduceRate(p, r));
                }
                _availMaxRate[r] = best;
            }
        }

        // ================================================================== detection
        private void DetectResources(Settlement s, float run)
        {
            var sp = s.Stockpile;
            _cap = Math.Max(1f, MathUtil.Sanitize(sp.Capacity, Stockpile.DefaultCapacity));
            for (int r = 0; r < R; r++)
            {
                var rt = (ResourceType)r;
                float stock = Math.Max(0f, MathUtil.Sanitize(sp.Get(rt)));
                float net = MathUtil.Sanitize(sp.Net(rt));
                float income = Math.Max(0f, MathUtil.Sanitize(sp.AvgIncome[r]));
                _stock[r] = stock; _net[r] = net; _income[r] = income;

                // A) staffed consumers starving for this input
                float demand = _demand[r];
                if (demand > 1e-5f)
                {
                    float needRun = demand * run;
                    if (stock < needRun && (net < -1e-4f || income < demand * 0.9f))
                    {
                        float deficit = Math.Max(needRun - stock, (demand - income) * run);
                        _starveAmt[r] = deficit;
                        _starve[r] = MathUtil.Clamp01(deficit / needRun);
                        _starveBy[r] = WorstConsumer(r);
                    }
                }

                // C) construction costs (incomplete unpaid buildings + queued orders) that stock + income cannot cover
                float cost = _costNeed[r];
                if (cost > 0.01f)
                {
                    float cover = stock + Math.Max(0f, net) * HorizonTicks;
                    if (cover < cost)
                    {
                        float shortBy = cost - cover;
                        _buildShort[r] = shortBy;
                        _build[r] = MathUtil.Clamp01(shortBy / cost) * (0.55f + 0.45f * MathUtil.Clamp01(shortBy / 25f));
                    }
                }

                // B) overproduction: piling up near capacity, not needed by the build plan (food left to the food advisor)
                if (rt != ResourceType.Food && stock >= _cap * OverflowFill && net > -0.02f && cost < stock * 0.5f)
                    _over[r] = MathUtil.InverseLerp(0.8f, 1f, stock / _cap) * (net >= 0f ? 1f : 0.6f);
            }
        }

        private int WorstConsumer(int r)
        {
            var cons = _model.ConsumersOf(r);
            int best = -1; float bestDraw = 0f;
            for (int k = 0; k < cons.Length; k++)
            {
                int c = cons[k];
                float draw = _workers[c] * _model.ConsumeRate(c, r);
                if (draw > bestDraw) { bestDraw = draw; best = c; }
            }
            return best;
        }

        private void Anticipate(Civilization civ)
        {
            for (int b = 0; b < B; b++)
            {
                var def = _model.Definitions[b];
                if (def == null || !_canBuild[b] || def.MinAge != civ.Age || _count[b] + _pending[b] > 0 || def.Cost == null) continue;
                for (int i = 0; i < def.Cost.Length; i++)
                {
                    int r = (int)def.Cost[i].Type;
                    float a = MathUtil.Sanitize(def.Cost[i].Amount);
                    if (r >= 0 && r < R && a > _ageCost[r]) _ageCost[r] = a;
                }
            }
            for (int r = 0; r < R; r++)
            {
                float need = Math.Min(_ageCost[r], _cap);
                if (need <= 0.01f) continue;
                float cover = _stock[r] + Math.Max(0f, _net[r]) * HorizonTicks * 2f;
                if (cover < need) _antic[r] = AnticipationWeight * (1f - cover / need);
            }
        }

        private void CombineNeeds()
        {
            for (int r = 0; r < R; r++)
            {
                float n = _starve[r]; byte k = KindStarved;
                if (_build[r] > n) { n = _build[r]; k = KindConstruction; }
                if (_antic[r] > n) { n = _antic[r]; k = KindAnticipation; }
                _need[r] = n;
                _kind[r] = n > 0f ? k : KindNone;
            }
        }

        // ================================================================== chain reasoning
        /// <summary>Pushes need for resource r upstream into the uncovered inputs of its buildable producers.</summary>
        private void Propagate(int r, float n, int depth)
        {
            var prods = _model.ProducersOf(r);
            for (int k = 0; k < prods.Length; k++)
            {
                int p = prods[k];
                if (!_canBuild[p] || _terrainF[p] <= 0f) continue;
                var def = _model.Definitions[p];
                var inputs = def.InputsPerWorker;
                if (inputs == null || inputs.Length == 0) continue;
                float share = Share(p, r);
                int slots = Math.Max(1, def.JobSlots);
                for (int i = 0; i < inputs.Length; i++)
                {
                    int ir = (int)inputs[i].Type;
                    float rate = MathUtil.Sanitize(inputs[i].Amount);
                    if (ir < 0 || ir >= R || ir == r || rate <= 0f) continue;
                    float gap = 1f - Coverage(ir, rate * slots);
                    if (gap <= 0.01f) continue;
                    float m = n * ChainDecay * gap * share * (0.5f + 0.5f * _terrainF[p]);
                    if (m > _chain[ir] + 0.01f)
                    {
                        _chain[ir] = m; _chainFrom[ir] = r; _chainVia[ir] = p;
                        if (depth < MaxChainDepth && m > 0.05f) Propagate(ir, m, depth + 1);
                    }
                }
            }
        }

        /// <summary>0..1: how well stock + surplus over the horizon covers an extra draw of ratePerTick.</summary>
        private float Coverage(int r, float ratePerTick)
        {
            float req = Math.Max(1e-4f, ratePerTick * HorizonTicks);
            return MathUtil.Clamp01((_stock[r] + Math.Max(0f, _net[r]) * HorizonTicks) / req);
        }

        private float Share(int p, int r)
        {
            float max = _availMaxRate[r];
            return max <= 0f ? 1f : 0.5f + 0.5f * MathUtil.Clamp01(_model.ProduceRate(p, r) / max);
        }

        private float SupplyNow(int r)
        {
            if (_income[r] > 1e-4f) return _demand[r] > 1e-5f ? MathUtil.Clamp01(0.5f + 0.5f * _income[r] / _demand[r]) : 1f;
            return _stock[r] >= 10f ? 0.5f : _stock[r] / 20f;
        }

        /// <summary>0..1: can this settlement obtain resource r at all (now, or by building a feasible chain)?</summary>
        private float Reach(int r)
        {
            if (_reachState[r] == 2) return _reach[r];
            if (_reachState[r] == 1) return 0f; // cycle guard
            _reachState[r] = 1;
            float best = SupplyNow(r);
            var prods = _model.ProducersOf(r);
            for (int k = 0; k < prods.Length && best < 1f; k++)
            {
                int p = prods[k];
                if (!_canBuild[p] || _terrainF[p] <= 0f) continue;
                float v = Math.Min(_terrainF[p], InputsOk(p, true));
                if (v > best) best = v;
            }
            _reach[r] = best;
            _reachState[r] = 2;
            return best;
        }

        private float InputsOk(int b, bool withReach)
        {
            var def = _model.Definitions[b];
            var inputs = def.InputsPerWorker;
            if (inputs == null || inputs.Length == 0) return 1f;
            int slots = Math.Max(1, def.JobSlots);
            float v = 1f;
            for (int i = 0; i < inputs.Length; i++)
            {
                int ir = (int)inputs[i].Type;
                float rate = MathUtil.Sanitize(inputs[i].Amount);
                if (ir < 0 || ir >= R || rate <= 0f) continue;
                float c = Coverage(ir, rate * slots);
                if (withReach && c < 1f) c = Math.Max(c, Reach(ir) * ReachDiscount);
                if (c < v) v = c;
            }
            return v;
        }

        private float ChainOk(int b)
        {
            if (_chainOk[b] < 0f) _chainOk[b] = InputsOk(b, true);
            return _chainOk[b];
        }

        // ================================================================== votes
        private void EmitBuildVotes(Civilization civ, Settlement s, SupplyChainTerrainInfo info, long tick, float scale, float overTol, float weight)
        {
            for (int b = 0; b < B; b++)
            {
                if (!_available[b]) continue;
                var def = _model.Definitions[b];
                if (!def.HasJobs) continue;
                var outs = def.OutputsPerWorker;
                var ins = def.InputsPerWorker;
                int nOut = outs == null ? 0 : outs.Length, nIn = ins == null ? 0 : ins.Length;
                if (nOut == 0 && nIn == 0) continue;

                float pos = 0f; int posRes = -1;
                if (_canBuild[b] && _terrainF[b] > 0f)
                {
                    for (int o = 0; o < nOut; o++)
                    {
                        int r = (int)outs[o].Type;
                        if (r < 0 || r >= R || _total[r] <= MinNeed || _model.ProduceRate(b, r) <= 0f) continue;
                        float c = _total[r] * Share(b, r) * ProdMod(civ, r) * (r == (int)ResourceType.Food ? FoodDamp : 1f);
                        if (c > pos) { pos = c; posRes = r; }
                    }
                    if (pos > 0f)
                    {
                        pos *= _terrainF[b] * (0.15f + 0.85f * ChainOk(b));
                        pos /= 1f + 0.5f * _open[b] + _pending[b]; // staff or finish what exists before adding more
                    }
                }

                float neg = 0f; int negRes = -1; bool negStarved = false;
                for (int o = 0; o < nOut; o++)
                {
                    int r = (int)outs[o].Type;
                    if (r < 0 || r >= R || _over[r] <= 0f) continue;
                    float c = _over[r] * 0.8f * overTol * (1f - OtherOutputNeed(def, r));
                    if (c > neg) { neg = c; negRes = r; negStarved = false; }
                }
                for (int i = 0; i < nIn; i++)
                {
                    int r = (int)ins[i].Type;
                    if (r < 0 || r >= R || _starve[r] <= 0f) continue;
                    float c = _starve[r] * 0.7f;
                    if (c > neg) { neg = c; negRes = r; negStarved = true; }
                }

                float v = MathUtil.Clamp(MathUtil.Sanitize((pos - neg) * scale), -1f, 1f);
                if (Math.Abs(v) < 0.02f) continue;
                string reason = null;
                if (Math.Abs(v) * weight >= 0.5f && info != null && info.CanLog(b, tick, ReasonCooldownTicks))
                {
                    reason = v > 0f ? PositiveReason(civ, posRes) : NegativeReason(negRes, negStarved);
                    if (reason != null) info.MarkLogged(b, tick);
                }
                VoteBuild(s, civ, (BuildingType)b, v, reason);
            }
        }

        private float OtherOutputNeed(BuildingDefinition def, int except)
        {
            float m = 0f;
            var outs = def.OutputsPerWorker;
            if (outs == null) return 0f;
            for (int o = 0; o < outs.Length; o++)
            {
                int r = (int)outs[o].Type;
                if (r >= 0 && r < R && r != except && _total[r] > m) m = _total[r];
            }
            return MathUtil.Clamp01(m);
        }

        private static float ProdMod(Civilization civ, int r)
        {
            float m = civ.Modifiers != null && r < civ.Modifiers.Production.Length ? MathUtil.Sanitize(civ.Modifiers.Production[r], 1f) : 1f;
            return 0.75f + 0.25f * MathUtil.Clamp(m, 0.5f, 2f);
        }

        private void EmitJobVotes(Civilization civ, Settlement s, float scale, float overTol)
        {
            Array.Clear(_jobVote, 0, _jobVote.Length);
            for (int b = 0; b < B; b++)
            {
                if (_count[b] == 0) continue;
                var def = _model.Definitions[b];
                if (def == null || !def.HasJobs) continue;
                int j = (int)def.Job;
                if (j <= 0 || j >= _jobVote.Length) continue;
                int slots = def.JobSlots * _count[b];
                if (slots <= 0) continue;
                float openFrac = _open[b] / (float)slots;
                var outs = def.OutputsPerWorker ?? ResourceAmount.None;
                var ins = def.InputsPerWorker ?? ResourceAmount.None;

                float plus = 0f;
                for (int o = 0; o < outs.Length; o++)
                {
                    int r = (int)outs[o].Type;
                    if (r >= 0 && r < R && _total[r] > MinNeed && _over[r] <= 0f) plus = Math.Max(plus, _total[r] * (0.3f + 0.7f * openFrac));
                }
                if (plus > 0f) plus *= InputsOk(b, false); // staffing a building with no inputs is wasted labor

                float minus = 0f;
                for (int o = 0; o < outs.Length; o++)
                {
                    int r = (int)outs[o].Type;
                    if (r >= 0 && r < R && _over[r] > 0f) minus = Math.Max(minus, _over[r] * 0.6f * overTol * (1f - OtherOutputNeed(def, r)));
                }
                for (int i = 0; i < ins.Length; i++)
                {
                    int r = (int)ins[i].Type;
                    if (r >= 0 && r < R && _starve[r] > 0f) minus = Math.Max(minus, _starve[r] * 0.5f);
                }
                _jobVote[j] += plus - minus;
            }
            for (int j = 1; j < _jobVote.Length; j++)
            {
                float v = MathUtil.Clamp(MathUtil.Sanitize(_jobVote[j] * scale * 0.8f), -1f, 1f);
                if (Math.Abs(v) >= 0.02f) VoteJob(s, civ, (JobType)j, v);
            }
        }

        private void EmitUrgency(Settlement s)
        {
            float u = 0f; int worst = -1;
            for (int r = 0; r < R; r++)
            {
                float x = Math.Max(_starve[r], Math.Max(_build[r] * 0.85f, Math.Max(_chain[r] * 0.7f, Math.Max(_antic[r] * 0.6f, _over[r] * 0.4f))));
                if (x > u) { u = x; worst = r; }
            }
            u = MathUtil.Clamp01(MathUtil.Sanitize(u));
            Urgency(s, u);
            s.Memory[SupplyChainKeys.WorstResource] = worst + 1;
            s.Memory[SupplyChainKeys.WorstSeverity] = u;
        }

        // ================================================================== explanations
        private string PositiveReason(Civilization civ, int r)
        {
            if (r < 0 || r >= R) return null;
            string res = s_resNames[r];
            switch (_kind[r])
            {
                case KindStarved:
                    return BuildingName(_starveBy[r], "Workers") + " idle: " + res + " short " + _starveAmt[r].ToString("0.0") + "/run";
                case KindConstruction:
                    return res + " short " + _buildShort[r].ToString("0") + " for construction";
                case KindChain:
                {
                    int from = _chainFrom[r];
                    string fromName = from >= 0 && from < R ? s_resNames[from] : "industry";
                    return res + " feeds " + BuildingName(_chainVia[r], "industry") + " -> " + fromName;
                }
                case KindAnticipation:
                    return "stockpiling " + res + " for " + civ.Age + " builds";
                default:
                    return null;
            }
        }

        private string NegativeReason(int r, bool starved)
        {
            if (r < 0 || r >= R) return null;
            if (starved) return s_resNames[r] + " starved: no more consumers";
            return s_resNames[r] + " piling up " + _stock[r].ToString("0") + "/" + _cap.ToString("0");
        }

        private string BuildingName(int b, string fallback)
        {
            if (b < 0 || b >= B) return fallback;
            var def = _model.Definitions[b];
            return def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : ((BuildingType)b).ToString();
        }

        private static string[] ResourceNames()
        {
            var a = new string[Counts.Resources];
            for (int i = 0; i < a.Length; i++) a[i] = ((ResourceType)i).ToString();
            return a;
        }
    }
}
