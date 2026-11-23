using System;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Trade Specialization advisor ("specialize"): turns a civilization's settlements into one cooperating economy built
    /// on comparative advantage. The economy's trade system moves surpluses between same-civ settlements within
    /// <see cref="TradeRange"/> tiles, so instead of every town growing a little of everything, each one leans into what
    /// its land, existing industry and labor make it relatively best at.
    ///
    /// Each run (every 48 ticks), per civilization with at least two settlements:
    /// 1. Survey every settlement's hinterland (fertility, deposits, trees, water, sacred sites), its existing producers
    ///    (classified data-driven from <see cref="BuildingDefinition"/> outputs/services) and its labor force.
    /// 2. Score an advantage per role (breadbasket, timber town, mining town, forge town, market town, temple city,
    ///    scholar city), gated by buildings the civ can actually build and place there. Personality traits tilt the
    ///    scores (mercantile civs favor market towns, pious ones temple cities, curious ones scholar cities).
    /// 3. Assign roles greedily by comparative advantage with a diversity penalty, then apply hysteresis: a role only
    ///    changes after a minimum tenure and a clear margin, or when it becomes unviable (e.g. the deposits are gone).
    /// 4. Vote: producers of the specialty above local need (the surplus feeds siblings, anticipating their growth);
    ///    gentle negatives for producers where a sibling is far stronger (never when local need is critical); commerce
    ///    at the trade hub (the most central, best connected settlement); civic/research/commerce lean in the capital.
    ///
    /// Settlements of a single-settlement civ (or out of trade range of every sibling) are left alone.
    /// </summary>
    public sealed class TradeSpecializationAdvisor : AdvisorSystemBase
    {
        public const string Id = "specialize";
        /// <summary>Range (tiles) within which the economy trades between same-civ settlements.</summary>
        public const float TradeRange = 80f;
        /// <summary>Maximum settlements per civ considered (deterministic: the first ones in civ order).</summary>
        public const int MaxGroup = 64;
        /// <summary>Minimum ticks a role is kept before it may change (unless it becomes unviable).</summary>
        public const int MinTenureTicks = SimTime.TicksPerYear * 12;
        /// <summary>A challenger role must beat the current one by this factor to replace it after tenure.</summary>
        public const float SwitchMargin = 1.35f;
        /// <summary>Score multiplier given to the current role while assigning (stability).</summary>
        public const float Stickiness = 1.25f;
        public const float StickinessBonus = 0.08f;
        /// <summary>Per already-assigned settlement of a role, later candidates are divided by (1 + penalty * taken).</summary>
        public const float DiversityPenalty = 0.6f;
        public const float MinRoleScore = 0.12f;
        public const string FoodAdvisorId = "food";
        public const string FoodDaysKey = "econ.foodDays";

        private const int R = SpecializeRoles.Count;
        private const float RangeSq = TradeRange * TradeRange;

        private readonly SpecializeBuildingCatalog _catalog = new SpecializeBuildingCatalog();
        private readonly SpecializeSiteSurvey _survey = new SpecializeSiteSurvey();
        private readonly StringBuilder _sb = new StringBuilder(128);

        // per-civ scratch (reused; no per-run allocations)
        private readonly Settlement[] _m = new Settlement[MaxGroup];
        private int _lastN;
        private readonly float[] _terrain = new float[MaxGroup * R];
        private readonly float[] _prod = new float[MaxGroup * R];
        private readonly float[] _adv = new float[MaxGroup * R];
        private readonly float[] _score = new float[MaxGroup * R];
        private readonly float[] _net = new float[MaxGroup * R];
        private readonly float[] _deficit = new float[MaxGroup * R];
        private readonly PlacementRule[] _placeOk = new PlacementRule[MaxGroup];
        private readonly int[] _counts = new int[MaxGroup * Counts.Buildings];
        private readonly int[] _inRange = new int[MaxGroup];
        private readonly float[] _central = new float[MaxGroup];
        private readonly float[] _labor = new float[MaxGroup];
        private readonly int[] _role = new int[MaxGroup];
        private readonly int[] _proposed = new int[MaxGroup];
        private readonly float[] _maxProd = new float[R];
        private readonly int[] _taken = new int[R];
        private readonly bool[] _jobSeen = new bool[Counts.Jobs];

        private float _merc, _piety, _curiosity, _industry;

        public override string AdvisorId => Id;
        public override int TickInterval => 48;

        /// <summary>The data-driven building classification in use (for UI/debug).</summary>
        public SpecializeBuildingCatalog Catalog => _catalog;

        /// <summary>Current specialization of a settlement (None if unassigned).</summary>
        public static SpecializeRole RoleOf(Settlement s) => SpecializeRoles.Of(s);

        public override void Initialize(SimContext ctx)
        {
            if (ctx != null && ctx.Db != null) _catalog.Build(ctx.Db);
        }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s) { /* civ-level work happens in AfterRun */ }

        protected override void AfterRun(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            if (_catalog.Source != ctx.Db) _catalog.Build(ctx.Db);
            var civs = ctx.State.Civilizations.Items;
            for (int c = 0; c < civs.Count; c++)
            {
                var civ = civs[c];
                if (civ == null || !civ.IsAlive) continue;
                int n = Gather(ctx, civ);
                if (n < 2)
                {
                    for (int i = 0; i < n; i++) Clear(_m[i]);
                    civ.Memory.Remove(SpecializeRoles.CivHubKey);
                    continue;
                }
                Survey(ctx, civ, n);
                Assign(ctx, civ, n);
                VoteAll(ctx, civ, n);
            }
            for (int i = 0; i < _lastN; i++) _m[i] = null; // do not pin dead settlements
            _lastN = 0;
        }

        // =====================================================================================================
        // 1. gather + survey
        private int Gather(SimContext ctx, Civilization civ)
        {
            for (int i = 0; i < _lastN; i++) _m[i] = null;
            int n = 0;
            var ids = civ.SettlementIds;
            for (int k = 0; k < ids.Count && n < MaxGroup; k++)
            {
                var s = ctx.State.Settlements.Get(ids[k]);
                if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                bool dup = false;
                for (int q = 0; q < n; q++) if (_m[q] == s) { dup = true; break; }
                if (!dup) _m[n++] = s;
            }
            _lastN = n;
            return n;
        }

        private void Survey(SimContext ctx, Civilization civ, int n)
        {
            var map = ctx.State.Map;
            var buildings = ctx.State.Buildings;
            int nb = Counts.Buildings;
            int maxPop = 1;
            for (int r = 0; r < R; r++) _maxProd[r] = 0f;

            for (int i = 0; i < n; i++)
            {
                var s = _m[i];
                _survey.Scan(map, s);
                _placeOk[i] = _survey.Satisfiable;
                int o = i * R;
                for (int r = 0; r < R; r++) { _terrain[o + r] = _survey.Richness[r]; _prod[o + r] = 0f; _net[o + r] = 0f; _deficit[o + r] = 0f; }

                int cb = i * nb;
                for (int t = 0; t < nb; t++) _counts[cb + t] = 0;
                var bids = s.BuildingIds;
                for (int k = 0; k < bids.Count; k++)
                {
                    var b = buildings.Get(bids[k]);
                    if (b == null || b.IsDestroyed) continue;
                    int t = (int)b.Type;
                    if (t < 0 || t >= nb) continue;
                    _counts[cb + t]++;
                    var role = _catalog.RoleOf[t];
                    if (role == SpecializeRole.None) continue;
                    float f = b.IsComplete ? MathUtil.Clamp(MathUtil.Sanitize(b.Condition, 1f), 0.25f, 1f) : 0.4f;
                    _prod[o + (int)role] += _catalog.Weight[t] * f;
                }
                // measured service output can exceed the nominal building weights (modifiers)
                Max(ref _prod[o + (int)SpecializeRole.MarketTown], s.ServiceOutput[(int)ServiceType.Commerce]);
                Max(ref _prod[o + (int)SpecializeRole.TempleCity], s.ServiceOutput[(int)ServiceType.Faith]);
                Max(ref _prod[o + (int)SpecializeRole.ScholarCity], s.ServiceOutput[(int)ServiceType.Research]);

                // flows: net per role, and projected deficit (current shortfall + growth-driven extra demand)
                float prevPop = s.Mem(SpecializeRoles.PopKey, -1f);
                int pop = s.Population;
                float growth = prevPop > 0f ? MathUtil.Clamp(MathUtil.Sanitize((pop - prevPop) / prevPop), 0f, 0.5f) : 0f;
                var sp = s.Stockpile;
                for (int res = 0; res < Counts.Resources; res++)
                {
                    var role = SpecializeRoles.RoleForResource((ResourceType)res);
                    if (role == SpecializeRole.None) continue;
                    float net = MathUtil.Sanitize(sp.Net((ResourceType)res));
                    _net[o + (int)role] += net;
                    if (net < 0f) _deficit[o + (int)role] += -net;
                    _deficit[o + (int)role] += growth * Math.Max(0f, MathUtil.Sanitize(sp.AvgExpense[res]));
                }
                if (pop > maxPop) maxPop = pop;
                for (int r = 1; r < R; r++) if (_prod[o + r] > _maxProd[r]) _maxProd[r] = _prod[o + r];
            }

            for (int i = 0; i < n; i++)
            {
                _labor[i] = MathUtil.Clamp01(_m[i].Population / (float)maxPop);
                int inRange = 0; float sum = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    int d2 = _m[i].Center.DistanceSqTo(_m[j].Center);
                    if (d2 > RangeSq) continue;
                    inRange++;
                    sum += (float)Math.Sqrt(d2);
                }
                _inRange[i] = inRange;
                _central[i] = inRange == 0 ? 0f : MathUtil.Clamp01(0.6f * inRange / (n - 1) + 0.4f * (1f - sum / inRange / TradeRange));
            }

            var db = ctx.Db;
            _merc = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
            _piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            _curiosity = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            _industry = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);

            for (int i = 0; i < n; i++)
            {
                int o = i * R;
                _adv[o] = 0f;
                bool capital = _m[i].IsCapital || _m[i].Id == civ.CapitalId;
                for (int r = 1; r < R; r++)
                {
                    if (_inRange[i] == 0 || !Viable(ctx, civ, i, (SpecializeRole)r)) { _adv[o + r] = 0f; continue; }
                    float t = _terrain[o + r];
                    float p = _maxProd[r] > 0f ? MathUtil.Clamp01(_prod[o + r] / _maxProd[r]) : 0f;
                    float l = _labor[i];
                    float a;
                    switch ((SpecializeRole)r)
                    {
                        case SpecializeRole.Breadbasket: a = 1.0f * t + 0.45f * p + 0.15f * l; break;
                        case SpecializeRole.TimberTown: a = 1.0f * t + 0.45f * p + 0.10f * l; break;
                        case SpecializeRole.MiningTown: a = (1.0f * t + 0.45f * p + 0.15f * l) * (0.85f + 0.3f * _industry); break;
                        case SpecializeRole.ForgeTown: a = (0.5f * t + 0.5f * p + 0.35f * l) * (0.8f + 0.4f * _industry); break;
                        case SpecializeRole.MarketTown: a = (0.55f * _central[i] + 0.3f * t + 0.35f * p + 0.25f * l + (capital ? 0.25f : 0f)) * (0.7f + 0.6f * _merc); break;
                        case SpecializeRole.TempleCity: a = (0.6f * t + 0.45f * p + 0.1f * l) * (0.6f + 0.8f * _piety); break;
                        default: a = (0.2f * t + 0.45f * p + 0.35f * l + (capital ? 0.25f : 0f)) * (0.6f + 0.8f * _curiosity); break;
                    }
                    _adv[o + r] = Math.Max(0f, MathUtil.Sanitize(a));
                }
            }
        }

        private static void Max(ref float a, float b) { b = MathUtil.Sanitize(b); if (b > a) a = b; }

        /// <summary>A role is viable only if the civ can build one of its producers and the site can host it.</summary>
        private bool Viable(SimContext ctx, Civilization civ, int i, SpecializeRole role)
        {
            int c = _catalog.CountFor(role);
            for (int k = 0; k < c; k++)
            {
                var t = _catalog.At(role, k);
                if (!ctx.Db.IsBuildingAvailable(civ, t)) continue;
                if (!ctx.Db.TryGetBuilding(t, out var def)) continue;
                if (SpecializeBuildingCatalog.PlacementOk(def, _placeOk[i])) return true;
            }
            return false;
        }

        // =====================================================================================================
        // 2. assignment (comparative advantage + diversity + hysteresis)
        private void Assign(SimContext ctx, Civilization civ, int n)
        {
            for (int r = 1; r < R; r++)
            {
                float sum = 0f;
                for (int i = 0; i < n; i++) sum += _adv[i * R + r];
                for (int i = 0; i < n; i++)
                {
                    int idx = i * R + r;
                    float a = _adv[idx];
                    if (a <= 0f) { _score[idx] = 0f; continue; }
                    float meanOthers = (sum - a) / (n - 1);
                    float ratio = MathUtil.Clamp(a / (meanOthers + 0.1f), 0f, 2.5f);
                    float sc = a * (0.4f + 0.6f * ratio);
                    if ((int)SpecializeRoles.Of(_m[i]) == r) sc = sc * Stickiness + StickinessBonus;
                    _score[idx] = MathUtil.Sanitize(sc);
                }
            }

            for (int r = 0; r < R; r++) _taken[r] = 0;
            for (int i = 0; i < n; i++) _proposed[i] = -1;
            for (int step = 0; step < n; step++)
            {
                int bi = -1, br = 0; float bv = MinRoleScore;
                for (int i = 0; i < n; i++)
                {
                    if (_proposed[i] >= 0) continue;
                    for (int r = 1; r < R; r++)
                    {
                        float v = _score[i * R + r] / (1f + DiversityPenalty * _taken[r]);
                        if (v > bv) { bv = v; bi = i; br = r; }
                    }
                }
                if (bi < 0) break;
                _proposed[bi] = br;
                _taken[br]++;
            }

            long tick = ctx.Tick;
            for (int i = 0; i < n; i++)
            {
                var s = _m[i];
                int p = _proposed[i] < 0 ? 0 : _proposed[i];
                bool has = s.Memory.ContainsKey(SpecializeRoles.RoleKey);
                int cur = (int)SpecializeRoles.Of(s);
                if (has && cur != 0 && p != cur && _adv[i * R + cur] > 0.05f)
                {
                    long since = (long)MathUtil.Sanitize(s.Mem(SpecializeRoles.SinceKey, 0f));
                    float rawCur = (_score[i * R + cur] - StickinessBonus) / Stickiness;
                    float rawNew = p > 0 ? _score[i * R + p] : 0f;
                    if (tick - since < MinTenureTicks || rawNew < rawCur * SwitchMargin) p = cur;
                }
                _role[i] = p;
                if (p != cur || (!has && p != 0)) SetRole(ctx, civ, s, (SpecializeRole)cur, (SpecializeRole)p, i);
            }
        }

        private void SetRole(SimContext ctx, Civilization civ, Settlement s, SpecializeRole from, SpecializeRole to, int i)
        {
            if (to == SpecializeRole.None)
            {
                ClearRoleKeys(s);
                if (from != SpecializeRole.None) s.LogAi("[" + Id + "] No longer a " + SpecializeRoles.Name(from) + ": self-sufficient again");
                return;
            }
            s.Memory[SpecializeRoles.RoleKey] = (float)(int)to;
            s.Memory[SpecializeRoles.SinceKey] = (float)ctx.Tick;
            s.Memory.Remove(SpecializeRoles.ProdKey);
            string civPhrase = CivPhrase(civ);
            s.LogAi("[" + Id + "] Role: " + SpecializeRoles.Name(to) + " of " + civPhrase + " (advantage " + _adv[i * R + (int)to].ToString("0.00") + ")");
            if (ctx.Ops == null) return;
            if (from != SpecializeRole.None)
                ctx.Ops.AddChronicle(ChronicleCategory.Settlement, s.Name + ", once a " + SpecializeRoles.Name(from) + ", has become the " + SpecializeRoles.Name(to) + " of " + civPhrase + ".", civ.Id, s.Id, s.Center, 1);
            else if (s.Tier >= SettlementTier.Village)
                ctx.Ops.AddChronicle(ChronicleCategory.Settlement, s.Name + " has become the " + SpecializeRoles.Name(to) + " of " + civPhrase + ".", civ.Id, s.Id, s.Center, 1);
        }

        // =====================================================================================================
        // 3. votes
        private void VoteAll(SimContext ctx, Civilization civ, int n)
        {
            for (int r = 0; r < R; r++) _taken[r] = 0;
            for (int i = 0; i < n; i++) _taken[_role[i]]++;

            int hub = -1; float hubV = -1f;
            for (int i = 0; i < n; i++)
            {
                if (_inRange[i] == 0) continue;
                float v = _central[i] + 0.15f * _labor[i] + (_m[i].IsCapital ? 0.05f : 0f);
                if (v > hubV + 1e-6f) { hubV = v; hub = i; }
            }
            civ.Memory[SpecializeRoles.CivHubKey] = hub >= 0 ? _m[hub].Id : -1f;

            for (int i = 0; i < n; i++)
            {
                var s = _m[i];
                if (_inRange[i] == 0) { Clear(s); continue; }
                var role = (SpecializeRole)_role[i];
                int o = i * R;

                int feeds = 0;
                for (int j = 0; j < n; j++)
                    if (j != i && _role[j] != (int)role && InRange(i, j)) feeds++;

                float hubLevel = 0f;
                if (i == hub) hubLevel = 1f;
                else if (hub >= 0 && _inRange[i] >= 3 && _central[i] >= 0.85f * _central[hub]) hubLevel = 0.5f;

                float urgency = hubLevel * 0.15f;
                if (role != SpecializeRole.None)
                {
                    float conf = Confidence(i, role, n);
                    urgency = SpecialtyVotes(ctx, civ, i, role, conf, feeds) + hubLevel * 0.1f;
                    s.Memory[SpecializeRoles.ConfidenceKey] = conf;
                    s.Memory[SpecializeRoles.FeedsKey] = feeds;
                    s.Memory[SpecializeRoles.ProdKey] = MathUtil.Sanitize(_prod[o + (int)role]);
                }
                else
                {
                    s.Memory.Remove(SpecializeRoles.ConfidenceKey);
                    s.Memory.Remove(SpecializeRoles.FeedsKey);
                    s.Memory.Remove(SpecializeRoles.ProdKey);
                }

                SiblingNegatives(ctx, civ, i, role, n);
                if (hubLevel > 0f) HubVotes(ctx, civ, i, role, hubLevel);
                if (s.IsCapital || s.Id == civ.CapitalId) CapitalLean(ctx, civ, i, role, hubLevel > 0f);

                if (hubLevel > 0f) s.Memory[SpecializeRoles.HubKey] = hubLevel; else s.Memory.Remove(SpecializeRoles.HubKey);
                s.Memory[SpecializeRoles.PopKey] = s.Population;
                Urgency(s, urgency);
            }
        }

        private float Confidence(int i, SpecializeRole role, int n)
        {
            int r = (int)role;
            float a = _adv[i * R + r];
            float sum = 0f;
            for (int j = 0; j < n; j++) if (j != i) sum += _adv[j * R + r];
            float meanOthers = sum / Math.Max(1, n - 1);
            float ratio = a / (meanOthers + 0.1f);
            return MathUtil.Clamp01(MathUtil.Sanitize(MathUtil.Clamp01(a) * MathUtil.Clamp(0.55f + 0.2f * ratio, 0.5f, 1f)));
        }

        /// <summary>Votes the settlement's specialty producers; returns the urgency to report.</summary>
        private float SpecialtyVotes(SimContext ctx, Civilization civ, int i, SpecializeRole role, float conf, int feeds)
        {
            var s = _m[i];
            int o = i * R, r = (int)role;
            float prevProd = s.Mem(SpecializeRoles.ProdKey, -1f);
            bool rebuilding = prevProd > 0.5f && _prod[o + r] < prevProd * 0.6f;

            float need; bool glut = false;
            if (SpecializeRoles.IsResourceRole(role))
            {
                float target = 0f;
                for (int j = 0; j < _lastN; j++)
                    if (j != i && _role[j] != r && InRange(i, j)) target += _deficit[j * R + r];
                target /= Math.Max(1, _taken[r]);
                target += 0.02f * s.Population + 0.2f;
                float localNet = _net[o + r];
                need = MathUtil.Clamp01(MathUtil.Sanitize((target - localNet) / Math.Max(0.5f, target)));
                glut = localNet > target * 1.5f;
            }
            else
            {
                float cov = MathUtil.Sanitize(s.ServiceCoverage[(int)SpecializeRoles.ServiceOf(role)]);
                float goal = role == SpecializeRole.MarketTown ? 1.4f : 1.6f;
                need = MathUtil.Clamp01((goal - cov) / goal);
            }

            float strength = conf * (0.55f + 0.45f * need);
            if (rebuilding) strength += 0.25f;
            if (glut) strength *= 0.4f;
            strength = MathUtil.Clamp01(strength);

            string reason = null;
            int voted = 0, count = _catalog.CountFor(role);
            for (int k = 0; k < count && voted < 2; k++)
            {
                var t = _catalog.At(role, k);
                if (!Eligible(ctx, civ, i, t)) continue;
                float v = voted == 0 ? strength : strength * 0.6f;
                if (reason == null && v * AdvisorBoard.Weight(civ, Id) >= 0.5f) reason = SpecialtyReason(civ, role, feeds, rebuilding);
                VoteBuild(s, civ, t, v, v * AdvisorBoard.Weight(civ, Id) >= 0.5f ? reason : null);
                voted++;
            }

            // staff the specialty: jobs of the specialty producers this town already has
            for (int j = 0; j < _jobSeen.Length; j++) _jobSeen[j] = false;
            int cb = i * Counts.Buildings;
            for (int k = 0; k < count; k++)
            {
                var t = _catalog.At(role, k);
                if (_counts[cb + (int)t] <= 0 || !ctx.Db.TryGetBuilding(t, out var def) || !def.HasJobs) continue;
                int jb = (int)def.Job;
                if (jb <= 0 || jb >= _jobSeen.Length || _jobSeen[jb]) continue;
                _jobSeen[jb] = true;
                VoteJob(s, civ, def.Job, 0.2f + 0.3f * conf + (rebuilding ? 0.2f : 0f));
            }

            return MathUtil.Clamp01(0.15f + 0.55f * need * conf + (rebuilding ? 0.25f : 0f));
        }

        /// <summary>Gently discourages producers in which an in-range sibling specialist is much stronger (unless critical here).</summary>
        private void SiblingNegatives(SimContext ctx, Civilization civ, int i, SpecializeRole role, int n)
        {
            var s = _m[i];
            for (int d = (int)SpecializeRole.Breadbasket; d <= (int)SpecializeRole.ForgeTown; d++)
            {
                if (d == (int)role) continue;
                float mine = _adv[i * R + d];
                float best = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (j == i || _role[j] != d || !InRange(i, j)) continue;
                    float a = _adv[j * R + d];
                    if (a > best) best = a;
                }
                if (best <= 0f || best < mine * 1.5f + 0.1f) continue;
                if (Critical(ctx, s, i, (SpecializeRole)d)) continue;
                float neg = -MathUtil.Clamp(0.12f + 0.2f * (best - mine), 0.12f, 0.3f);
                int voted = 0, count = _catalog.CountFor((SpecializeRole)d);
                for (int k = 0; k < count && voted < 2; k++)
                {
                    var t = _catalog.At((SpecializeRole)d, k);
                    if (!Eligible(ctx, civ, i, t)) continue;
                    VoteBuild(s, civ, t, neg);
                    voted++;
                }
            }
        }

        private bool Critical(SimContext ctx, Settlement s, int i, SpecializeRole d)
        {
            int pop = s.Population;
            if (d == SpecializeRole.Breadbasket)
            {
                if (AdvisorBoard.Urgency(s, FoodAdvisorId) >= 0.5f) return true;
                if (s.Memory.TryGetValue(FoodDaysKey, out float days) && MathUtil.Sanitize(days, 999f) < 30f) return true;
                if (s.LastStarvationTick >= 0 && ctx.Tick - s.LastStarvationTick < SimTime.TicksPerYear * 2) return true;
                if (_net[i * R + (int)d] < 0f && s.Stockpile.Get(ResourceType.Food) < (pop + 5) * 3f) return true;
                return false;
            }
            float stock = 0f;
            for (int res = 0; res < Counts.Resources; res++)
                if (SpecializeRoles.RoleForResource((ResourceType)res) == d) stock += s.Stockpile.Get((ResourceType)res);
            return _net[i * R + (int)d] < 0f && stock < 15f;
        }

        private void HubVotes(SimContext ctx, Civilization civ, int i, SpecializeRole role, float hubLevel)
        {
            var s = _m[i];
            float v = (hubLevel >= 1f ? 0.6f : 0.3f) * (0.7f + 0.6f * _merc) * (role == SpecializeRole.MarketTown ? 0.5f : 1f);
            v = MathUtil.Clamp01(v);
            int cb = i * Counts.Buildings, voted = 0, count = _catalog.CountFor(SpecializeRole.MarketTown);
            string reason = null;
            for (int k = 0; k < count && voted < 3; k++)
            {
                var t = _catalog.At(SpecializeRole.MarketTown, k);
                if (_counts[cb + (int)t] > 0 || !Eligible(ctx, civ, i, t)) continue;
                bool strong = v * AdvisorBoard.Weight(civ, Id) >= 0.5f;
                if (strong && reason == null)
                {
                    _sb.Length = 0;
                    _sb.Append("Trade hub of ").Append(CivPhrase(civ)).Append(": links ").Append(_inRange[i]).Append(_inRange[i] == 1 ? " town" : " towns");
                    reason = _sb.ToString();
                }
                VoteBuild(s, civ, t, v, strong ? reason : null);
                voted++;
            }
            for (int k = 0; k < count; k++)
            {
                var t = _catalog.At(SpecializeRole.MarketTown, k);
                if (_counts[cb + (int)t] <= 0 || !ctx.Db.TryGetBuilding(t, out var def) || !def.HasJobs) continue;
                VoteJob(s, civ, def.Job, 0.25f * hubLevel);
                break;
            }
        }

        private void CapitalLean(SimContext ctx, Civilization civ, int i, SpecializeRole role, bool isHub)
        {
            var s = _m[i];
            int cb = i * Counts.Buildings;
            for (int k = 0; k < _catalog.CivicCount; k++)
            {
                var t = _catalog.CivicAt(k);
                if (_counts[cb + (int)t] > 0 || !Eligible(ctx, civ, i, t)) continue;
                VoteBuild(s, civ, t, 0.35f);
                break;
            }
            if (role != SpecializeRole.ScholarCity) VoteFirstMissing(ctx, civ, i, SpecializeRole.ScholarCity, 0.3f * (0.6f + 0.8f * _curiosity));
            if (!isHub && role != SpecializeRole.MarketTown) VoteFirstMissing(ctx, civ, i, SpecializeRole.MarketTown, 0.3f * (0.7f + 0.6f * _merc));
        }

        private void VoteFirstMissing(SimContext ctx, Civilization civ, int i, SpecializeRole role, float v)
        {
            int cb = i * Counts.Buildings, count = _catalog.CountFor(role);
            for (int k = 0; k < count; k++)
            {
                var t = _catalog.At(role, k);
                if (_counts[cb + (int)t] > 0 || !Eligible(ctx, civ, i, t)) continue;
                VoteBuild(_m[i], civ, t, MathUtil.Clamp(v, 0f, 0.45f));
                return;
            }
        }

        /// <summary>Available to the civ, placeable here, tier/population met, and below the per-settlement cap.</summary>
        private bool Eligible(SimContext ctx, Civilization civ, int i, BuildingType t)
        {
            var db = ctx.Db;
            if (!db.IsBuildingAvailable(civ, t) || !db.TryGetBuilding(t, out var def)) return false;
            if (def.IsWonder || def.UniquePerCivilization) return false;
            if (!SpecializeBuildingCatalog.PlacementOk(def, _placeOk[i])) return false;
            var s = _m[i];
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            if (def.MaxPerSettlement >= 0 && _counts[i * Counts.Buildings + (int)t] >= def.MaxPerSettlement) return false;
            return true;
        }

        private bool InRange(int i, int j) => _m[i].Center.DistanceSqTo(_m[j].Center) <= RangeSq;

        // =====================================================================================================
        // text + cleanup
        private string SpecialtyReason(Civilization civ, SpecializeRole role, int feeds, bool rebuilding)
        {
            _sb.Length = 0;
            _sb.Append(rebuilding ? "Rebuilding the " : "Specialty: ").Append(SpecializeRoles.Name(role)).Append(" of ").Append(CivPhrase(civ));
            if (feeds <= 0) _sb.Append(" (stockpiling for the realm)");
            else
            {
                string verb;
                switch (role)
                {
                    case SpecializeRole.Breadbasket: verb = "feeds "; break;
                    case SpecializeRole.MarketTown: verb = "trades for "; break;
                    case SpecializeRole.TempleCity: verb = "prays for "; break;
                    case SpecializeRole.ScholarCity: verb = "studies for "; break;
                    default: verb = "supplies "; break;
                }
                _sb.Append(" (").Append(verb).Append(feeds).Append(feeds == 1 ? " town)" : " towns)");
            }
            if (rebuilding) _sb.Append(" after losses");
            return _sb.ToString();
        }

        private static string CivPhrase(Civilization civ)
        {
            string name = civ == null || string.IsNullOrEmpty(civ.Name) ? "realm" : civ.Name;
            if (name.StartsWith("The ", StringComparison.Ordinal)) return "the " + name.Substring(4);
            if (name.StartsWith("the ", StringComparison.Ordinal)) return name;
            return "the " + name;
        }

        private static void ClearRoleKeys(Settlement s)
        {
            s.Memory.Remove(SpecializeRoles.RoleKey);
            s.Memory.Remove(SpecializeRoles.SinceKey);
            s.Memory.Remove(SpecializeRoles.ConfidenceKey);
            s.Memory.Remove(SpecializeRoles.FeedsKey);
            s.Memory.Remove(SpecializeRoles.ProdKey);
        }

        /// <summary>Removes every key this advisor owns from a settlement (no-op if none were written).</summary>
        private void Clear(Settlement s)
        {
            if (s == null) return;
            ClearRoleKeys(s);
            s.Memory.Remove(SpecializeRoles.HubKey);
            s.Memory.Remove(SpecializeRoles.PopKey);
            s.Memory.Remove(AdvisorBoard.UrgencyKey(Id));
        }
    }
}
