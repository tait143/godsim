using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Chooses what each civilization researches. Every <see cref="Interval"/> ticks, per alive civ:
    /// <list type="number">
    /// <item><b>Plan.</b> When the next age still needs <c>RequiredTechsFromPreviousAge</c> techs of the current age, a
    /// multi-step path is planned: repeatedly the current-age target with the lowest value-adjusted cost
    /// (unknown prerequisite closure cost / techs it adds / (1 + 0.15 * effect value)) is added together with its
    /// prerequisites, in research order. Techs whose closure needs a tech the race can never research are dead ends and
    /// are skipped. The plan is logged at the capital when it changes, e.g.
    /// <c>Path to Iron: Smelting -&gt; Iron Working (2 techs, ~14 yrs)</c>.</item>
    /// <item><b>Crisis.</b> A food crisis (any town under <see cref="FoodCrisisDays"/> days or a recent famine) or a war is a
    /// crisis. At its onset the director decides once: it switches to the best crisis tech (food production / food
    /// buildings, military / weapons / defense), or to the first step of the cheapest path to one, even when another
    /// valid tech is being researched (the research pool is shared, so nothing is lost).</item>
    /// <item><b>Choice.</b> Otherwise a valid current choice is kept. A new choice follows the crisis, then the plan (best
    /// researchable step), then the best overall score.</item>
    /// </list>
    /// <para>Score = (0.5 base + effect values weighted by dynamic personality (<see cref="AdvisorBoard.Trait"/>), focus and
    /// needs + unlock value + gateway bonus + current-age bonus) * 10 / sqrt(cost). Unlocks whose hard placement rules the
    /// civ's terrain cannot satisfy, and production boosts of resources it cannot source (no deposit / forest), are worth
    /// little; techs leading to further techs get a gateway bonus; techs with no usable effect and no successors are dead
    /// ends and are discounted.</para>
    /// </summary>
    public sealed class ResearchDirectorSystem : ISimSystem
    {
        /// <summary>Tick interval of this system.</summary>
        public const int Interval = 24;
        /// <summary>A settlement with fewer food days than this triggers the food-need bonus.</summary>
        public const float FoodCrisisDays = StrategyHelpers.FoodCrisisDays;
        /// <summary>Crisis bit: food.</summary>
        public const int CrisisFood = 1;
        /// <summary>Crisis bit: war.</summary>
        public const int CrisisWar = 2;
        /// <summary>Score bonus per tech this tech is a direct prerequisite of (capped at 4 successors).</summary>
        public const float GatewayBonus = 0.3f;
        /// <summary>How much effect value lowers a tech's planning cost.</summary>
        public const float PlanValueWeight = 0.15f;
        /// <summary>Value multiplier of an unlock/production boost the civ cannot use on its terrain.</summary>
        public const float UnusableFactor = 0.15f;

        // Scratch (reused; never allocated per tick once sized).
        private readonly List<int> _plan = new List<int>();
        private readonly List<int> _closure = new List<int>();
        private bool[] _inPlan = new bool[0];
        private int[] _visitStamp = new int[0];
        private int _stamp;
        private GameDatabase _succDb;
        private List<int>[] _successors = new List<int>[0];
        private readonly StringBuilder _sb = new StringBuilder(160);
        private int _planShortBy;
        private AgeId _planAge;

        /// <inheritdoc />
        public string Name => "AI.ResearchDirector";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            var db = ctx.Db;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                var race = StrategyHelpers.RaceOf(db, civ.Race);
                var capital = StrategyHelpers.Capital(ctx.State, civ);
                var needs = AssessNeeds(ctx, civ, race);
                BuildPlan(db, civ, race, needs);
                StorePlan(ctx, civ, capital);

                int prevCrisis = MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.ResearchCrisis)));
                civ.Memory[CivStrategyKeys.ResearchCrisis] = needs.Crisis;
                bool onset = (needs.Crisis & ~prevCrisis) != 0;

                var current = db.Tech(civ.CurrentResearchTechId);
                bool currentValid = current != null && db.CanResearch(civ, current);
                int best;
                float score;
                string reason;
                if (currentValid)
                {
                    // Keep a valid choice, except once at a crisis onset when it does not address the crisis.
                    if (!onset || CrisisRelevance(db, civ, current, needs.Crisis, needs) > 0f) continue;
                    best = CrisisTech(db, civ, needs, out score, out bool viaPrereq);
                    if (best < 0 || best == current.Id) continue;
                    reason = "crisis: " + CrisisName(needs) + (viaPrereq ? ", step toward crisis tech" : "");
                }
                else best = Decide(ctx, civ, race, needs, out score, out reason);
                if (best >= 0)
                {
                    civ.CurrentResearchTechId = best;
                    civ.Memory[CivStrategyKeys.ResearchIdle] = 0f;
                    if (currentValid) reason += ", was " + StrategyHelpers.NameOf(current);
                    capital?.LogAi("Research: " + StrategyHelpers.NameOf(db.Tech(best)) + " (score " + StrategyHelpers.Fmt1(score) + "; " + reason + ")");
                }
                else if (!currentValid)
                {
                    civ.CurrentResearchTechId = -1;
                    if (civ.Mem(CivStrategyKeys.ResearchIdle) < 0.5f)
                    {
                        civ.Memory[CivStrategyKeys.ResearchIdle] = 1f;
                        capital?.LogAi("Research: nothing left to research");
                    }
                }
            }
        }

        /// <summary>
        /// The director's decision for the civ right now (crisis tech, then the age plan, then the best score); -1 when
        /// nothing is researchable. Deterministic: ties go to the lowest tech id.
        /// </summary>
        public int ChooseTech(SimContext ctx, Civilization civ, out float bestScore)
        {
            bestScore = 0f;
            if (ctx == null || ctx.Db == null || ctx.State == null || civ == null) return -1;
            var race = StrategyHelpers.RaceOf(ctx.Db, civ.Race);
            var needs = AssessNeeds(ctx, civ, race);
            BuildPlan(ctx.Db, civ, race, needs);
            return Decide(ctx, civ, race, needs, out bestScore, out _);
        }

        /// <summary>Score of one tech for the civ (higher = more desirable). Returns 0 for null input.</summary>
        public float ScoreTech(SimContext ctx, Civilization civ, TechDefinition tech)
        {
            if (ctx == null || ctx.Db == null || ctx.State == null || civ == null || tech == null) return 0f;
            var race = StrategyHelpers.RaceOf(ctx.Db, civ.Race);
            return Score(ctx.Db, civ, tech, AssessNeeds(ctx, civ, race));
        }

        /// <summary>
        /// The current multi-step plan toward the next age (tech ids in research order), as computed by the last
        /// <see cref="Tick"/> / <see cref="ChooseTech"/> call. Empty when no plan is needed or possible.
        /// </summary>
        public IReadOnlyList<int> LastPlan => _plan;

        // ================================================================== needs
        private sealed class Needs
        {
            public int Focus = -1;
            public bool AtWar;
            public bool Militarist;
            public bool FoodCrisis;
            public float WorstFoodDays;
            public bool NeedCurrentAgeTechs;
            public int Crisis;
            public PlacementRule Features;
            public float Cur, Agg, Pie, Ind, Mer, Exp;
        }

        // One reusable instance: systems run single-threaded.
        private readonly Needs _needs = new Needs();

        private Needs AssessNeeds(SimContext ctx, Civilization civ, RaceDefinition race)
        {
            var state = ctx.State;
            var db = ctx.Db;
            var n = _needs;
            n.Focus = -1;
            if (civ.Memory.TryGetValue(CivStrategyKeys.Focus, out float f) && !float.IsNaN(f) && !float.IsInfinity(f))
                n.Focus = MathUtil.Clamp(MathUtil.RoundToInt(f), 0, CivStrategyKeys.FocusCount - 1);
            n.AtWar = StrategyHelpers.ActiveWars(state, civ.Id, out _) > 0;
            n.Militarist = MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.Posture))) == CivStrategyKeys.PostureMilitarist;
            n.FoodCrisis = StrategyHelpers.InFoodCrisis(state, civ, race, ctx.Tick, out n.WorstFoodDays);
            n.Crisis = (n.FoodCrisis ? CrisisFood : 0) | (n.AtWar ? CrisisWar : 0);
            n.NeedCurrentAgeTechs = false;
            if (civ.Age < AgeId.Ascension)
            {
                int next = (int)civ.Age + 1;
                var nextAge = next < db.Ages.Count ? db.Ages[next] : null;
                if (nextAge != null) n.NeedCurrentAgeTechs = KnownOfAge(db, civ, civ.Age) < nextAge.RequiredTechsFromPreviousAge;
            }
            n.Features = StrategyHelpers.CivFeatures(state, civ, ctx.Tick);
            n.Cur = StrategyHelpers.Trait(civ, db, AITrait.Curiosity);
            n.Agg = StrategyHelpers.Trait(civ, db, AITrait.Aggression);
            n.Pie = StrategyHelpers.Trait(civ, db, AITrait.Piety);
            n.Ind = StrategyHelpers.Trait(civ, db, AITrait.Industriousness);
            n.Mer = StrategyHelpers.Trait(civ, db, AITrait.Mercantilism);
            n.Exp = StrategyHelpers.Trait(civ, db, AITrait.Expansionism);
            return n;
        }

        private static int KnownOfAge(GameDatabase db, Civilization civ, AgeId age)
        {
            var ids = db.TechsOfAge(age);
            int known = 0;
            for (int i = 0; i < ids.Count; i++) if (civ.KnownTechs.Contains(ids[i])) known++;
            return known;
        }

        // ================================================================== decision
        private int Decide(SimContext ctx, Civilization civ, RaceDefinition race, Needs n, out float bestScore, out string reason)
        {
            var db = ctx.Db;
            bestScore = 0f;
            reason = "best value";
            if (n.Crisis != 0)
            {
                int c = CrisisTech(db, civ, n, out bestScore, out bool viaPrereq);
                if (c >= 0)
                {
                    reason = "crisis: " + CrisisName(n) + (viaPrereq ? ", step toward crisis tech" : "");
                    return c;
                }
            }
            if (_plan.Count > 0)
            {
                int bestPlan = -1;
                float bestPlanScore = 0f;
                int stepIndex = 0;
                for (int i = 0; i < _plan.Count; i++)
                {
                    var t = db.Tech(_plan[i]);
                    if (t == null || !db.CanResearch(civ, t)) continue;
                    float s = Score(db, civ, t, n);
                    if (bestPlan < 0 || s > bestPlanScore + 1e-6f || (Math.Abs(s - bestPlanScore) <= 1e-6f && t.Id < bestPlan))
                    {
                        bestPlan = t.Id;
                        bestPlanScore = s;
                        stepIndex = i;
                    }
                }
                if (bestPlan >= 0)
                {
                    bestScore = bestPlanScore;
                    reason = "path to " + AgeName(db, _planAge) + " step " + (stepIndex + 1) + "/" + _plan.Count;
                    return bestPlan;
                }
            }
            int best = BestByScore(db, civ, n, -1, out bestScore);
            if (best >= 0 && n.Focus >= 0) reason = "best value, focus " + CivStrategyKeys.FocusName(n.Focus);
            return best;
        }

        /// <summary>Best researchable tech by score (optionally only crisis-relevant ones for the given crisis bits).</summary>
        private int BestByScore(GameDatabase db, Civilization civ, Needs n, int crisisBits, out float bestScore)
        {
            bestScore = 0f;
            int best = -1;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (t == null || !db.CanResearch(civ, t)) continue;
                if (crisisBits > 0 && CrisisRelevance(db, civ, t, crisisBits, n) <= 0f) continue;
                float s = Score(db, civ, t, n);
                if (best < 0 || s > bestScore + 1e-6f || (Math.Abs(s - bestScore) <= 1e-6f && t.Id < best))
                {
                    best = t.Id;
                    bestScore = s;
                }
            }
            return best;
        }

        /// <summary>
        /// Best researchable crisis tech; when none is researchable, the first step of the cheapest reachable path to one.
        /// </summary>
        private int CrisisTech(GameDatabase db, Civilization civ, Needs n, out float score, out bool viaPrereq)
        {
            viaPrereq = false;
            int direct = BestByScore(db, civ, n, n.Crisis, out score);
            if (direct >= 0) return direct;

            EnsureScratch(db);
            int bestStep = -1;
            float bestEff = float.MaxValue;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (t == null || civ.KnownTechs.Contains(t.Id) || t.Age > civ.Age || !t.AllowedRaces.Allows(civ.Race)) continue;
                float rel = CrisisRelevance(db, civ, t, n.Crisis, n);
                if (rel <= 0f) continue;
                if (!Closure(db, civ, t, false)) continue;
                float cost = ClosureCost(db);
                float eff = cost / rel;
                int step = _closure.Count > 0 ? _closure[0] : -1;
                if (step < 0 || !db.CanResearch(civ, db.Tech(step))) continue;
                if (eff < bestEff - 1e-6f || (Math.Abs(eff - bestEff) <= 1e-6f && step < bestStep))
                {
                    bestEff = eff;
                    bestStep = step;
                }
            }
            if (bestStep >= 0)
            {
                viaPrereq = true;
                score = Score(db, civ, db.Tech(bestStep), n);
            }
            return bestStep;
        }

        private static string CrisisName(Needs n)
        {
            if (n.FoodCrisis && n.AtWar) return "food " + MathUtil.RoundToInt(Math.Min(999f, n.WorstFoodDays)) + "d + war";
            if (n.FoodCrisis) return "food " + MathUtil.RoundToInt(Math.Min(999f, n.WorstFoodDays)) + "d";
            return "war";
        }

        /// <summary>How directly a tech addresses the crisis bits (0 = not at all; +10% effect = 1 point).</summary>
        private float CrisisRelevance(GameDatabase db, Civilization civ, TechDefinition t, int bits, Needs n)
        {
            if (t == null || bits == 0 || t.Effects == null) return 0f;
            float r = 0f;
            bool food = (bits & CrisisFood) != 0, war = (bits & CrisisWar) != 0;
            for (int i = 0; i < t.Effects.Length; i++)
            {
                var e = t.Effects[i];
                float v = MathUtil.Clamp(MathUtil.Sanitize(e.Value) * 10f, 0f, 5f);
                switch (e.Kind)
                {
                    case TechEffectKind.ProductionMultiplier:
                        if (food && e.Resource == ResourceType.Food) r += v;
                        if (war && e.Resource == ResourceType.Weapons) r += v;
                        break;
                    case TechEffectKind.MilitaryMultiplier: if (war) r += v; break;
                    case TechEffectKind.ServiceMultiplier: if (war && e.Service == ServiceType.Defense) r += v; break;
                    case TechEffectKind.UnlockBuilding:
                        if (!db.TryGetBuilding(e.Building, out var def) || db.IsBuildingAvailable(civ, e.Building)) break;
                        if (!def.AllowedRaces.Allows(civ.Race) || civ.Age > def.MaxAge) break;
                        if (!StrategyHelpers.PlacementSatisfied(n.Features, def.Placement)) break;
                        if (food && def.Category == BuildingCategory.Food) r += 1.5f;
                        if (war && def.Category == BuildingCategory.Military) r += 1.5f;
                        break;
                }
            }
            return r;
        }

        // ================================================================== plan
        private void BuildPlan(GameDatabase db, Civilization civ, RaceDefinition race, Needs n)
        {
            _plan.Clear();
            _planShortBy = 0;
            _planAge = civ.Age;
            if (civ.Age >= AgeId.Ascension) return;
            int next = (int)civ.Age + 1;
            var nextAge = next < db.Ages.Count ? db.Ages[next] : null;
            if (nextAge == null) return;
            _planAge = (AgeId)next;
            int need = nextAge.RequiredTechsFromPreviousAge - KnownOfAge(db, civ, civ.Age);
            if (need <= 0) return;
            EnsureScratch(db);
            Array.Clear(_inPlan, 0, _inPlan.Length);
            var ids = db.TechsOfAge(civ.Age);
            for (int guard = 0; need > 0 && guard <= ids.Count; guard++)
            {
                int bestId = -1, bestCount = 0;
                float bestEff = float.MaxValue;
                for (int i = 0; i < ids.Count; i++)
                {
                    var t = db.Tech(ids[i]);
                    if (t == null || civ.KnownTechs.Contains(t.Id) || _inPlan[t.Id] || !t.AllowedRaces.Allows(civ.Race)) continue;
                    if (!Closure(db, civ, t, true)) continue; // dead end for this race
                    float cost = ClosureCost(db);
                    int count = 0;
                    for (int c = 0; c < _closure.Count; c++) if (db.Tech(_closure[c]).Age == civ.Age) count++;
                    if (count <= 0) continue;
                    float value = Math.Max(0f, EffectValue(db, civ, t, n));
                    float eff = cost / Math.Min(count, need) / (1f + PlanValueWeight * value);
                    if (eff < bestEff - 1e-6f || (Math.Abs(eff - bestEff) <= 1e-6f && t.Id < bestId))
                    {
                        bestEff = eff;
                        bestId = t.Id;
                        bestCount = count;
                    }
                }
                if (bestId < 0) break;
                Closure(db, civ, db.Tech(bestId), true);
                for (int c = 0; c < _closure.Count; c++)
                {
                    _plan.Add(_closure[c]);
                    _inPlan[_closure[c]] = true;
                }
                need -= bestCount;
            }
            _planShortBy = Math.Max(0, need);
        }

        /// <summary>
        /// Fills <see cref="_closure"/> with the unknown (and, if <paramref name="skipPlanned"/>, not yet planned) techs needed
        /// for <paramref name="target"/>, prerequisites first. False when any of them can never be researched by the civ.
        /// </summary>
        private bool Closure(GameDatabase db, Civilization civ, TechDefinition target, bool skipPlanned)
        {
            _closure.Clear();
            _stamp++;
            if (_stamp == int.MaxValue) { Array.Clear(_visitStamp, 0, _visitStamp.Length); _stamp = 1; }
            return Visit(db, civ, target, skipPlanned, 0);
        }

        private bool Visit(GameDatabase db, Civilization civ, TechDefinition t, bool skipPlanned, int depth)
        {
            if (t == null || depth > 64) return false;
            if (t.Id < 0 || t.Id >= _visitStamp.Length) return false;
            if (civ.KnownTechs.Contains(t.Id) || (skipPlanned && _inPlan[t.Id])) return true;
            if (_visitStamp[t.Id] == _stamp) return true;
            _visitStamp[t.Id] = _stamp;
            if (!t.AllowedRaces.Allows(civ.Race) || t.Age > civ.Age) return false;
            var pre = t.Prerequisites;
            if (pre != null)
                for (int i = 0; i < pre.Length; i++)
                    if (!Visit(db, civ, db.Tech(pre[i]), skipPlanned, depth + 1)) return false;
            _closure.Add(t.Id);
            return true;
        }

        private float ClosureCost(GameDatabase db)
        {
            float cost = 0f;
            for (int i = 0; i < _closure.Count; i++) cost += Math.Max(1f, MathUtil.Sanitize(db.Tech(_closure[i]).Cost, 100f));
            return cost;
        }

        private void StorePlan(SimContext ctx, Civilization civ, Settlement capital)
        {
            var db = ctx.Db;
            civ.Memory[CivStrategyKeys.ResearchPlanLength] = _plan.Count;
            if (_plan.Count == 0)
            {
                civ.Memory[CivStrategyKeys.ResearchPlanHash] = 0f;
                civ.Memory[CivStrategyKeys.ResearchPlanYears] = -1f;
                return;
            }
            float planCost = 0f;
            ulong h = 1469598103934665603UL ^ (ulong)_planAge;
            for (int i = 0; i < _plan.Count; i++)
            {
                planCost += Math.Max(1f, MathUtil.Sanitize(db.Tech(_plan[i]).Cost, 100f));
                unchecked { h = (h ^ (ulong)(_plan[i] + 1)) * 1099511628211UL; }
            }
            float rate = ResearchRate(ctx.State, civ);
            var nextAge = db.Ages[(int)_planAge];
            float threshold = nextAge != null ? MathUtil.Sanitize(nextAge.ResearchThreshold) - MathUtil.Sanitize(civ.TotalResearch) : 0f;
            float needed = Math.Max(planCost - Math.Max(0f, MathUtil.Sanitize(civ.ResearchPool)), threshold);
            float years = rate > 1e-4f ? MathUtil.Clamp(Math.Max(0f, needed) / (rate * SimTime.TicksPerYear), 0f, 9999f) : -1f;
            civ.Memory[CivStrategyKeys.ResearchPlanYears] = years;

            float hash = (float)(h & 0xFFFFFFUL);
            if (MathUtil.Approximately(civ.Mem(CivStrategyKeys.ResearchPlanHash, -1f), hash, 0.5f)) return;
            civ.Memory[CivStrategyKeys.ResearchPlanHash] = hash;
            if (capital == null) return;
            _sb.Length = 0;
            _sb.Append("Path to ").Append(AgeName(db, _planAge)).Append(": ");
            int shown = Math.Min(_plan.Count, 5);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) _sb.Append(" -> ");
                _sb.Append(StrategyHelpers.NameOf(db.Tech(_plan[i])));
            }
            if (_plan.Count > shown) _sb.Append(" -> ...");
            _sb.Append(" (").Append(_plan.Count).Append(_plan.Count == 1 ? " tech, ~" : " techs, ~")
               .Append(years >= 0f ? MathUtil.RoundToInt(years).ToString() : "?").Append(" yrs");
            if (_planShortBy > 0) _sb.Append("; ").Append(_planShortBy).Append(" more unreachable");
            _sb.Append(")");
            capital.LogAi(_sb.ToString());
        }

        /// <summary>Research points per tick: progression's published rate, else research service output x modifier.</summary>
        private static float ResearchRate(WorldState state, Civilization civ)
        {
            float published = MathUtil.Sanitize(civ.Mem(CivStrategyKeys.ResearchPerTick));
            if (published > 0f) return published;
            float sum = 0f;
            for (int i = 0; i < civ.SettlementIds.Count; i++)
            {
                var s = state.Settlements.Get(civ.SettlementIds[i]);
                if (!StrategyHelpers.IsOwnAlive(s, civ.Id)) continue;
                sum += Math.Max(0f, MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Research]));
            }
            return MathUtil.Sanitize(sum * Math.Max(0f, MathUtil.Sanitize(civ.Modifiers.Research, 1f)));
        }

        private static string AgeName(GameDatabase db, AgeId age)
        {
            int i = (int)age;
            var def = i >= 0 && i < db.Ages.Count ? db.Ages[i] : null;
            return def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : age.ToString();
        }

        private void EnsureScratch(GameDatabase db)
        {
            int n = db.Techs.Count;
            if (_inPlan.Length != n) { _inPlan = new bool[n]; _visitStamp = new int[n]; _stamp = 0; }
            if (!ReferenceEquals(_succDb, db) || _successors.Length != n)
            {
                _succDb = db;
                _successors = new List<int>[n];
                for (int i = 0; i < n; i++) _successors[i] = new List<int>(2);
                for (int i = 0; i < n; i++)
                {
                    var pre = db.Techs[i].Prerequisites;
                    if (pre == null) continue;
                    for (int k = 0; k < pre.Length; k++) if (pre[k] >= 0 && pre[k] < n) _successors[pre[k]].Add(i);
                }
            }
        }

        // ================================================================== scoring
        private static float P(float v) => MathUtil.Clamp01(MathUtil.Sanitize(v, 0.5f));

        private float Score(GameDatabase db, Civilization civ, TechDefinition t, Needs n)
        {
            float raw = 0.5f + EffectValue(db, civ, t, n);
            if (t.Age == civ.Age) raw += n.NeedCurrentAgeTechs ? 2.5f : 1f;
            raw = Math.Max(0.05f, MathUtil.Sanitize(raw, 0.05f));
            float cost = Math.Max(1f, MathUtil.Sanitize(t.Cost, 100f));
            return MathUtil.Sanitize(raw * 10f / (float)Math.Sqrt(cost));
        }

        /// <summary>Effect value + gateway bonus; techs with no usable effect and no successors (dead ends) are discounted.</summary>
        private float EffectValue(GameDatabase db, Civilization civ, TechDefinition t, Needs n)
        {
            float effects = EffectPoints(db, civ, t, n);
            int successors = AllowedSuccessors(db, civ, t);
            float gateway = GatewayBonus * Math.Min(4, successors);
            if (effects < 0.25f && successors == 0) effects -= 0.3f; // dead end
            return MathUtil.Sanitize(effects + gateway);
        }

        private int AllowedSuccessors(GameDatabase db, Civilization civ, TechDefinition t)
        {
            EnsureScratch(db);
            if (t.Id < 0 || t.Id >= _successors.Length || !ReferenceEquals(db.Tech(t.Id), t)) return 0;
            var list = _successors[t.Id];
            int c = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var s = db.Tech(list[i]);
                if (s != null && !civ.KnownTechs.Contains(s.Id) && s.AllowedRaces.Allows(civ.Race)) c++;
            }
            return c;
        }

        private static float EffectPoints(GameDatabase db, Civilization civ, TechDefinition t, Needs n)
        {
            float cur = P(n.Cur), agg = P(n.Agg), pie = P(n.Pie), ind = P(n.Ind), mer = P(n.Mer), exp = P(n.Exp);
            bool recovery = n.Focus == CivStrategyKeys.FocusRecovery;
            float fResearch = n.Focus == CivStrategyKeys.FocusResearch ? 2f : 1f;
            float fFaith = n.Focus == CivStrategyKeys.FocusFaith ? 2f : 1f;
            float fMilitary = n.Focus == CivStrategyKeys.FocusMilitary ? 2f : 1f;
            float fWealth = n.Focus == CivStrategyKeys.FocusWealth ? 2f : 1f;
            float fGrowth = n.Focus == CivStrategyKeys.FocusGrowth || recovery ? 2f : 1f;
            float fBuild = recovery ? 2f : 1f;
            float war = (n.AtWar ? 2f : 1f) * (n.Militarist ? 1.3f : 1f);
            float food = n.FoodCrisis ? 3f : 1f;

            float raw = 0f;
            var effects = t.Effects;
            if (effects != null)
                for (int i = 0; i < effects.Length; i++)
                {
                    var e = effects[i];
                    float v = MathUtil.Clamp(MathUtil.Sanitize(e.Value), -5f, 5f) * 10f; // +10% -> 1 point
                    switch (e.Kind)
                    {
                        case TechEffectKind.ResearchMultiplier: raw += v * (0.5f + cur * 1.5f) * fResearch; break;
                        case TechEffectKind.MilitaryMultiplier: raw += v * (0.5f + agg * 1.5f) * fMilitary * war; break;
                        case TechEffectKind.FaithMultiplier: raw += v * (0.5f + pie * 1.5f) * fFaith; break;
                        case TechEffectKind.AllProductionMultiplier: raw += v * (0.8f + ind * 1.5f) * 1.5f; break;
                        case TechEffectKind.BuildSpeedMultiplier: raw += v * (0.5f + ind * 1.5f) * fBuild; break;
                        case TechEffectKind.TradeMultiplier: raw += v * (0.5f + mer * 1.5f) * fWealth; break;
                        case TechEffectKind.GrowthMultiplier: raw += v * (0.5f + exp) * fGrowth * (n.FoodCrisis ? 2f : 1f); break;
                        case TechEffectKind.LifespanBonus: raw += MathUtil.Sanitize(e.Value) * 0.1f * fGrowth; break;
                        case TechEffectKind.HealthBonus: raw += v * 0.8f * fGrowth; break;
                        case TechEffectKind.HappinessBonus: raw += v * 0.8f; break;
                        case TechEffectKind.StorageMultiplier: raw += v * 0.5f; break;
                        case TechEffectKind.MoveSpeedMultiplier: raw += v * 0.3f; break;
                        case TechEffectKind.ProductionMultiplier:
                            {
                                float w = 0.5f + ind;
                                switch (e.Resource)
                                {
                                    case ResourceType.Food: w *= food * fGrowth; break;
                                    case ResourceType.Gold: case ResourceType.Goods: w *= (0.5f + mer) * fWealth; break;
                                    case ResourceType.Weapons: w *= (0.5f + agg) * fMilitary * war; break;
                                    default: w *= 0.8f; break;
                                }
                                if (!StrategyHelpers.CanSource(n.Features, e.Resource)) w *= UnusableFactor;
                                raw += v * w;
                                break;
                            }
                        case TechEffectKind.ServiceMultiplier:
                            {
                                float w;
                                switch (e.Service)
                                {
                                    case ServiceType.Research: w = (0.5f + cur * 1.5f) * fResearch; break;
                                    case ServiceType.Faith: w = (0.5f + pie * 1.5f) * fFaith; break;
                                    case ServiceType.Defense: w = (0.5f + agg * 1.5f) * fMilitary * war; break;
                                    case ServiceType.Commerce: w = (0.5f + mer * 1.5f) * fWealth; break;
                                    case ServiceType.Health: w = 0.6f * fGrowth; break;
                                    default: w = 0.5f; break;
                                }
                                raw += v * w;
                                break;
                            }
                        case TechEffectKind.UnlockBuilding:
                            raw += UnlockValue(db, civ, e.Building, cur, agg, pie, mer, fResearch, fFaith, fMilitary, fWealth, fGrowth, n);
                            break;
                    }
                }
            return MathUtil.Sanitize(raw);
        }

        private static float UnlockValue(GameDatabase db, Civilization civ, BuildingType type, float cur, float agg, float pie, float mer,
            float fResearch, float fFaith, float fMilitary, float fWealth, float fGrowth, Needs n)
        {
            if (!db.TryGetBuilding(type, out var def)) return 0f;
            if (!def.AllowedRaces.Allows(civ.Race) || civ.Age > def.MaxAge) return 0f;
            if (db.IsBuildingAvailable(civ, type)) return 0.2f; // already have access: little value
            float v = 1.5f;
            switch (def.Category)
            {
                case BuildingCategory.Food: v += n.FoodCrisis ? 2.5f : 0.5f * fGrowth; break;
                case BuildingCategory.Housing: v += 0.5f * fGrowth; break;
                case BuildingCategory.Military: v += (n.AtWar ? 1.5f : 0f) + agg * 0.5f * fMilitary; break;
                case BuildingCategory.Education: v += cur * fResearch; break;
                case BuildingCategory.Religion: v += pie * fFaith; break;
                case BuildingCategory.Commerce: v += mer * fWealth; break;
                case BuildingCategory.Health: v += 0.4f * fGrowth; break;
                case BuildingCategory.Wonder: v += 0.5f; break;
            }
            // Terrain the civ cannot offer (coast, ore, coal...): the building could not be placed anywhere it lives.
            if (!StrategyHelpers.PlacementSatisfied(n.Features, def.Placement)) v *= UnusableFactor;
            return v;
        }
    }
}
