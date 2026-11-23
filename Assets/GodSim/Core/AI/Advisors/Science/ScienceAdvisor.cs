using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Science advisor plans the path to the next age. Once per run and civilization it builds a
    /// <see cref="SciencePacePlan"/> (research gap, missing current-age techs, EMA research rate, years to the next age
    /// versus a ~100-year, curiosity-scaled pacing target, and unmet MinPopulation/MinSettlements), then per settlement:
    /// <list type="bullet">
    /// <item>Behind pace: votes the best available research building (data-driven: any definition with a Research
    /// service, ranked by research per sqrt(build work), with diminishing returns per copy) at the best-suited
    /// settlements (tier, size, spare labor/housing, capital), a research wonder (e.g. Great Library) at the best
    /// settlement only if no settlement of the civ already built or queued it, and the staffing jobs of research
    /// buildings (Scholar) weighted by how research-focused the building is.</item>
    /// <item>Far ahead of pace: small negative votes to free labor and resources for growth.</item>
    /// <item>Crisis (food/defense urgency from other advisors, or war): damps positive votes, and above 0.6 backs off
    /// entirely and releases scholars.</item>
    /// </list>
    /// It never edits BuildQueue or CurrentResearchTechId; it publishes a research hint (<see cref="ScienceKeys.PreferredTech"/>)
    /// when current-age techs are missing. Deterministic and allocation-free per tick (pooled plans, reused arrays).
    /// </summary>
    public sealed class ScienceAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in board keys and logs.</summary>
        public const string Id = "science";
        /// <summary>Crisis level where damping starts.</summary>
        public const float CrisisStart = 0.3f;
        /// <summary>Crisis level where positive influence reaches zero.</summary>
        public const float CrisisFull = 0.75f;
        /// <summary>Crisis level at which the advisor backs off completely and releases scholars.</summary>
        public const float CrisisBackOff = 0.6f;
        /// <summary>Minimum <see cref="SciencePacePlan.Behind"/> before research wonders are considered.</summary>
        public const float WonderBehindMin = 0.35f;

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        private readonly List<SciencePacePlan> _plans = new List<SciencePacePlan>();
        private int _planCount;

        // data-driven research building table (rebuilt when the database instance changes)
        private GameDatabase _tableDb;
        private readonly BuildingDefinition[] _defs = new BuildingDefinition[Counts.Buildings];
        private readonly float[] _research = new float[Counts.Buildings];
        private readonly float[] _share = new float[Counts.Buildings];
        private readonly bool[] _wonder = new bool[Counts.Buildings];

        // per-settlement survey scratch
        private readonly int[] _existing = new int[Counts.Buildings];
        private readonly int[] _queued = new int[Counts.Buildings];
        private readonly float[] _jobShare = new float[Counts.Jobs];

        public override void Initialize(SimContext ctx)
        {
            _planCount = 0;
            if (ctx != null && ctx.Db != null) EnsureTable(ctx.Db);
        }

        /// <summary>Research points per tick a building type provides (0 if it is not a research building).</summary>
        public float ResearchOf(GameDatabase db, BuildingType type)
        {
            if (db == null) return 0f;
            EnsureTable(db);
            int t = (int)type;
            return t >= 0 && t < _research.Length ? _research[t] : 0f;
        }

        /// <summary>Plan computed for a civ during the current run (computed on demand; updates the civ's EMA memory).</summary>
        public SciencePacePlan PlanFor(SimContext ctx, Civilization civ)
        {
            for (int i = 0; i < _planCount; i++)
            {
                var p = _plans[i];
                if (p.CivId == civ.Id && p.Tick == ctx.Tick) return p;
            }
            if (_planCount == _plans.Count) _plans.Add(new SciencePacePlan());
            var plan = _plans[_planCount++];
            plan.Compute(ctx, civ, true);
            Announce(ctx, civ, plan);
            return plan;
        }

        protected override void AfterRun(SimContext ctx) { _planCount = 0; }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (ctx.Db == null) return;
            EnsureTable(ctx.Db);
            var plan = PlanFor(ctx, civ);
            if (s.Population <= 0 || !plan.HasNextAge) { Urgency(s, 0f); return; }

            Survey(ctx, s);
            float crisis = SciencePacePlan.LocalCrisis(s, civ);

            // ---- crisis: back off, free scholars for food or defense
            if (crisis >= CrisisBackOff)
            {
                Urgency(s, 0f);
                for (int j = 1; j < _jobShare.Length; j++)
                    if (_jobShare[j] > 0f) VoteJob(s, civ, (JobType)j, -0.3f * crisis * _jobShare[j]);
                if (s.Mem(ScienceKeys.CrisisFlag) < 0.5f)
                {
                    s.Memory[ScienceKeys.CrisisFlag] = 1f;
                    s.LogAi("[" + Id + "] backing off research: " + CrisisName(s, civ) + " comes first");
                }
                return;
            }
            if (s.Memory.ContainsKey(ScienceKeys.CrisisFlag)) s.Memory.Remove(ScienceKeys.CrisisFlag);

            float behind = plan.Behind;
            if (behind > SciencePacePlan.BehindThreshold)
            {
                float damp = 1f - MathUtil.SmoothStep(CrisisStart, CrisisFull, crisis);
                float personality = (0.7f + 0.6f * plan.Curiosity) * (plan.ResearchFocus ? 1.25f : 1f);
                float push = MathUtil.Clamp(MathUtil.Sanitize(behind * personality * damp), 0f, 1f);
                Urgency(s, behind * damp);
                if (push <= 0.01f) return;

                int rank = s.Id == plan.BestSettlementId ? 0 : (s.Id == plan.SecondSettlementId ? 1 : 2);
                float siteMul = rank == 0 ? 1f : (rank == 1 ? 0.55f : 0.2f);

                PickBuildings(ctx.Db, civ, s, out int best, out int second);
                if (best >= 0) Vote(s, civ, best, push * siteMul, plan, rank == 0 ? " - best research site" : null);
                if (second >= 0) Vote(s, civ, second, push * siteMul * 0.5f, plan, null);

                if (rank == 0 && behind >= WonderBehindMin)
                {
                    int w = PickWonder(ctx, civ, s);
                    if (w >= 0) Vote(s, civ, w, push * 0.7f, plan, " - wonder");
                }

                float jobMul = rank == 0 ? 1f : 0.7f;
                bool anyJob = false;
                for (int j = 1; j < _jobShare.Length; j++)
                {
                    if (_jobShare[j] <= 0f) continue;
                    VoteJob(s, civ, (JobType)j, push * jobMul * _jobShare[j]);
                    anyJob = true;
                }
                // Anticipate staffing for the building we are pushing.
                if (!anyJob && rank == 0 && best >= 0 && _defs[best].HasJobs) VoteJob(s, civ, _defs[best].Job, push * 0.4f);
            }
            else if (behind < SciencePacePlan.AheadThreshold)
            {
                // Far ahead: ease off research expansion so labor and materials go to growth.
                Urgency(s, 0f);
                float neg = MathUtil.Clamp(behind * 0.45f, -0.45f, 0f);
                for (int t = 0; t < _research.Length; t++)
                {
                    if (_research[t] <= 0f || _wonder[t] || !Eligible(ctx.Db, civ, s, t)) continue;
                    Vote(s, civ, t, neg, plan, null);
                }
                for (int j = 1; j < _jobShare.Length; j++)
                    if (_jobShare[j] > 0f) VoteJob(s, civ, (JobType)j, neg * 0.6f * _jobShare[j]);
            }
            else Urgency(s, Math.Max(0f, behind));
        }

        // ------------------------------------------------------------------ voting helpers
        private void Vote(Settlement s, Civilization civ, int type, float strength, SciencePacePlan plan, string suffix)
        {
            string reason = null;
            if (Math.Abs(strength) * AdvisorBoard.Weight(civ, Id) >= 0.5f)
            {
                reason = plan.Reason;
                if (strength > 0f && _research[type] > 0f) reason += " (+" + _research[type].ToString("0.#") + " research" + (suffix ?? "") + ")";
                else if (strength < 0f) reason = "ahead of pace: " + reason;
            }
            VoteBuild(s, civ, (BuildingType)type, strength, reason);
        }

        private void PickBuildings(GameDatabase db, Civilization civ, Settlement s, out int best, out int second)
        {
            best = -1; second = -1;
            float bestV = 0f, secondV = 0f;
            for (int t = 0; t < _research.Length; t++)
            {
                if (_research[t] <= 0f || _wonder[t]) continue;
                if (!Eligible(db, civ, s, t) || _queued[t] > 0) continue;
                var def = _defs[t];
                float v = _research[t] * (0.5f + 0.5f * _share[t]) / (float)Math.Sqrt(Math.Max(10f, MathUtil.Sanitize(def.BuildWork, 10f)));
                v /= 1f + 0.6f * _existing[t];
                if (def.HasJobs && s.Unemployed < def.JobSlots) v *= 0.8f; // would compete for labor
                v = MathUtil.Sanitize(v);
                if (v <= 0f) continue;
                if (best < 0 || v > bestV + 1e-6f) { second = best; secondV = bestV; best = t; bestV = v; }
                else if (second < 0 || v > secondV + 1e-6f) { second = t; secondV = v; }
            }
        }

        private int PickWonder(SimContext ctx, Civilization civ, Settlement s)
        {
            int best = -1;
            float bestV = 0f;
            for (int t = 0; t < _research.Length; t++)
            {
                if (!_wonder[t] || _research[t] <= 0f) continue;
                if (!Eligible(ctx.Db, civ, s, t)) continue;
                if (CivHasOrQueued(ctx.State, civ, t)) continue; // the strategy (or anyone) already has it in hand
                if (best < 0 || _research[t] > bestV + 1e-6f) { best = t; bestV = _research[t]; }
            }
            return best;
        }

        /// <summary>Available to the civ, settlement meets tier/population, and below MaxPerSettlement (built + queued).</summary>
        private bool Eligible(GameDatabase db, Civilization civ, Settlement s, int t)
        {
            var def = _defs[t];
            if (def == null || !db.IsBuildingAvailable(civ, (BuildingType)t)) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            if (def.MaxPerSettlement >= 0 && _existing[t] + _queued[t] >= def.MaxPerSettlement) return false;
            return true;
        }

        private static bool CivHasOrQueued(WorldState state, Civilization civ, int t)
        {
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive) continue;
                var q = s.BuildQueue;
                for (int k = 0; k < q.Count; k++) if (q[k] != null && (int)q[k].Type == t) return true;
                var b = s.BuildingIds;
                for (int k = 0; k < b.Count; k++)
                {
                    var bld = state.Buildings.Get(b[k]);
                    if (bld != null && !bld.IsDestroyed && (int)bld.Type == t) return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ surveys & tables
        private void Survey(SimContext ctx, Settlement s)
        {
            Array.Clear(_existing, 0, _existing.Length);
            Array.Clear(_queued, 0, _queued.Length);
            Array.Clear(_jobShare, 0, _jobShare.Length);
            var buildings = ctx.State.Buildings;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= _existing.Length) continue;
                _existing[t]++;
                var def = _defs[t];
                if (b.IsComplete && _research[t] > 0f && def != null && def.HasJobs)
                {
                    int j = (int)def.Job;
                    if (j > 0 && j < _jobShare.Length && _share[t] > _jobShare[j]) _jobShare[j] = _share[t];
                }
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                if (q[i] == null) continue;
                int t = (int)q[i].Type;
                if (t >= 0 && t < _queued.Length) _queued[t]++;
            }
        }

        private void EnsureTable(GameDatabase db)
        {
            if (ReferenceEquals(db, _tableDb)) return;
            _tableDb = db;
            for (int t = 0; t < _defs.Length; t++)
            {
                _defs[t] = null; _research[t] = 0f; _share[t] = 0f; _wonder[t] = false;
                if (!db.TryGetBuilding((BuildingType)t, out var def)) continue;
                _defs[t] = def;
                float research = 0f, total = 0f;
                var svc = def.Services;
                if (svc != null)
                    for (int i = 0; i < svc.Length; i++)
                    {
                        float a = Math.Max(0f, MathUtil.Sanitize(svc[i].Amount));
                        total += a;
                        if (svc[i].Type == ServiceType.Research) research += a;
                    }
                _research[t] = research;
                _share[t] = total > 0f ? research / total : 0f;
                _wonder[t] = def.IsWonder || def.UniquePerCivilization || def.Category == BuildingCategory.Wonder;
            }
        }

        // ------------------------------------------------------------------ explainability
        private void Announce(SimContext ctx, Civilization civ, SciencePacePlan plan)
        {
            var host = ctx.State.Settlements.Get(plan.BestSettlementId) ?? ctx.State.Settlements.Get(civ.CapitalId);
            if (host == null || !host.IsAlive) return;
            float last = civ.Mem(ScienceKeys.Band, -99f);
            if (MathUtil.RoundToInt(MathUtil.Sanitize(last, -99f)) != plan.Band)
            {
                civ.Memory[ScienceKeys.Band] = plan.Band;
                host.LogAi("[" + Id + "] " + plan.BandName + ": " + plan.Reason);
            }
            int hint = plan.PreferredTechId;
            if (civ.CurrentResearchTechId == -1 || hint < 0) return;
            var cur = ctx.Db.Tech(civ.CurrentResearchTechId);
            if (cur != null && cur.Age == civ.Age) return; // already working toward the age
            if (MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(ScienceKeys.HintLogged, -1f), -1f)) == hint) return;
            civ.Memory[ScienceKeys.HintLogged] = hint;
            var t = ctx.Db.Tech(hint);
            host.LogAi("[" + Id + "] hint: research " + (t != null ? t.Name : "#" + hint) + " next (" + plan.Reason + ")");
        }

        private static string CrisisName(Settlement s, Civilization civ)
        {
            float food = AdvisorBoard.Urgency(s, ScienceKeys.FoodAdvisorId);
            float def = AdvisorBoard.Urgency(s, ScienceKeys.DefenseAdvisorId);
            if (food >= def && food > 0f) return "food crisis";
            if (def > 0f) return "defense";
            return civ != null && MathUtil.Sanitize(civ.Mem(ScienceKeys.ActiveWars)) > 0.5f ? "war" : "crisis";
        }
    }
}
