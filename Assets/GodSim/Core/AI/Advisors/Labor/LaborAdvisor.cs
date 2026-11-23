using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Labor-market advisor ("labor"). Per settlement it compares idle adults with open job slots (complete job
    /// buildings' JobSlots minus Workers), builders with DesiredBuilders, jobs already in the construction pipeline,
    /// youths about to come of age and the soldier share, smooths the signed balance with an EMA and switches
    /// between three hysteresis modes so advice never flip-flops:
    /// <list type="bullet">
    /// <item><b>Surplus</b> (idle hands): votes the job buildings with the best value per worker for this settlement's
    /// needs (weighted by other advisors' urgencies such as food/supply/science/treasury and by personality), plus
    /// Builder jobs while construction is active.</item>
    /// <item><b>Shortage</b> (unfilled jobs): votes against new job buildings ("12 jobs unfilled: build homes, not
    /// workshops"), for dwellings (growth), and raises staffing priority of the most critical unfilled jobs, food first.</item>
    /// <item><b>Balanced</b>: anticipates jobs coming online (homes first) and fills open slots when people idle.</item>
    /// </list>
    /// It also demobilizes an over-militarized peacetime workforce, mobilizes idle hands in wartime, and after a
    /// population crash (plague, war, famine) prioritizes food jobs and builders to recover. Votes are soft influence
    /// through <see cref="AdvisorBoard"/>; the build queue is never touched. Deterministic and allocation-free per run
    /// (reason strings are only built when a decision changes or once a year).
    /// </summary>
    public sealed class LaborAdvisor : AdvisorSystemBase
    {
        public const string Id = "labor";
        public override string AdvisorId => Id;
        public override int TickInterval => 12;

        /// <summary>EMA smoothing of the labor balance and soldier share per run.</summary>
        public const float EmaAlpha = 0.35f;
        /// <summary>Slow population EMA used to detect disasters.</summary>
        public const float PopEmaAlpha = 0.1f;
        /// <summary>Balance rate below which surplus/shortage modes end (hysteresis band).</summary>
        public const float ExitThreshold = 0.04f;
        /// <summary>Balance rate (negative) at which the shortage mode begins.</summary>
        public const float ShortageEnter = 0.12f;
        /// <summary>Re-explain an unchanged decision at most once a year.</summary>
        public const int LogEveryTicks = SimTime.TicksPerYear;

        private static readonly float[] s_rank = { 1f, 0.6f, 0.4f };

        private readonly LaborMarketSurvey _survey = new LaborMarketSurvey();
        private readonly LaborJobValuator _value = new LaborJobValuator();
        private readonly int[] _topBuildings = new int[3];
        private readonly int[] _topJobs = new int[3];
        private readonly float[] _jobCrit = new float[Counts.Jobs];

        /// <summary>Factor the advisor board applies to each vote of this advisor (per-council-cycle normalization).</summary>
        public float VoteScale => CycleScale;

        /// <summary>Survey of the settlement advised last (debug/tests).</summary>
        public LaborMarketSurvey LastSurvey => _survey;
        /// <summary>Valuation of the settlement advised last (debug/tests).</summary>
        public LaborJobValuator LastValuation => _value;

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (ctx == null || ctx.Db == null || ctx.State == null) return;
            var db = ctx.Db;
            var sv = _survey;
            sv.Gather(ctx, civ, s);
            if (sv.Adults <= 0) { Urgency(s, 0f); return; }
            _value.Evaluate(ctx, civ, s, sv);
            var mem = s.Memory;

            // ------------------------------------------------------------ raw signals
            int civilianOpen = sv.OpenCivilian + (sv.AtWar ? sv.OpenMilitary : 0);
            int gap = sv.BuilderGap;
            // + idle workers / - unfilled jobs; pipeline jobs will need workers, youths will soon supply them.
            float balance = sv.Unemployed - civilianOpen - gap - 0.5f * sv.PipelineCivilian + 0.5f * sv.Youths;
            float denom = Math.Max(4, sv.Adults);
            float rawRate = MathUtil.Clamp(balance / denom, -1f, 1f);
            float soldierShare = sv.Soldiers / (float)sv.Adults;

            bool init = s.Mem(LaborKeys.Initialized) > 0.5f;
            float ema = init ? Ema(s.Mem(LaborKeys.Balance), rawRate, EmaAlpha) : rawRate;
            float soldierEma = init ? Ema(s.Mem(LaborKeys.SoldierShare), soldierShare, EmaAlpha) : soldierShare;
            float prevPop = init ? Math.Max(0f, MathUtil.Sanitize(s.Mem(LaborKeys.PopEma), sv.Population)) : sv.Population;
            float popEma = init ? Ema(prevPop, sv.Population, PopEmaAlpha) : sv.Population;

            // ------------------------------------------------------------ hysteresis mode
            float industrious = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            float enterSurplus = 0.16f - 0.08f * industrious; // industrious peoples hate idleness
            int mode = init ? MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(s.Mem(LaborKeys.Mode))), -1, 1) : 0;
            if (mode == 1 && ema < ExitThreshold) mode = 0;
            else if (mode == -1 && ema > -ExitThreshold) mode = 0;
            if (mode == 0)
            {
                if (ema >= enterSurplus && balance >= 2f) mode = 1;
                else if (ema <= -ShortageEnter && balance <= -3f) mode = -1;
            }
            float surplusSev = mode == 1 ? 0.25f + 0.75f * MathUtil.SmoothStep(ExitThreshold, 0.4f, ema) : 0f;
            float shortageSev = mode == -1 ? 0.25f + 0.75f * MathUtil.SmoothStep(ExitThreshold, 0.4f, -ema) : 0f;
            float foodNeed = _value.Need(LaborDomain.Food);

            // ------------------------------------------------------------ disaster recovery
            float recovery = 0f;
            if (prevPop >= 8f && sv.Population < 0.7f * prevPop) recovery = MathUtil.Clamp01(1f - sv.Population / prevPop);

            // ------------------------------------------------------------ military posture
            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float soldierTarget = 0.04f + 0.08f * aggression + 0.2f * sv.Threat + (sv.AtWar ? 0.12f : 0f);
            bool peace = !sv.AtWar && sv.Threat < 0.4f;
            float militaryExcess = 0f;
            if (peace && sv.Soldiers >= 2 && soldierEma > soldierTarget * 1.4f + 0.02f)
                militaryExcess = 0.2f + 0.8f * MathUtil.SmoothStep(0.4f, 2f, (soldierEma - soldierTarget) / Math.Max(0.05f, soldierTarget));

            // ------------------------------------------------------------ explainability throttle
            int nTop = mode == 1 ? PickTopBuildings(sv.AtWar) : 0;
            int overbuilt = mode == -1 ? FindOverbuilt(db, sv) : -1;
            int focus = mode == 1 ? (nTop > 0 ? _topBuildings[0] : 999) : (mode == -1 ? 500 + Math.Max(0, overbuilt) : 0);
            float sig = (mode + 2) * 1000f + focus + (militaryExcess > 0f ? 20000f : 0f) + (recovery > 0f ? 40000f : 0f) + (sv.AtWar ? 80000f : 0f);
            bool explain = !init || s.Mem(LaborKeys.LogSignature, -1f) != sig || ctx.Tick - (long)s.Mem(LaborKeys.LogTick) >= LogEveryTicks;
            float top = -1f;

            // ------------------------------------------------------------ surplus: create the best jobs
            if (mode == 1)
            {
                float needSlots = Math.Max(1f, balance);
                float best = _value.BestScore;
                for (int r = 0; r < nTop; r++)
                {
                    int i = _topBuildings[r];
                    var def = db.Buildings[i];
                    float rel = best > 0f ? _value.Score[i] / best : 0f;
                    float absorb = 0.75f + 0.25f * Math.Min(1f, def.JobSlots / needSlots);
                    float strength = surplusSev * (0.4f + 0.6f * rel) * s_rank[r] * absorb;
                    string reason = null;
                    if (r == 0 && explain) reason = SurplusReason(sv, def, _value.MainDomain[i]);
                    VoteExplained(s, civ, (BuildingType)i, strength, reason);
                    if (r == 0) top = i;
                }
            }

            // ------------------------------------------------------------ shortage: homes, not workshops
            if (mode == -1)
            {
                int shortfall = Math.Max(1, MathUtil.RoundToInt(-balance));
                string msg = null;
                if (explain)
                    msg = shortfall + " jobs unfilled: build homes, not " + (overbuilt >= 0 ? Plural(db.Buildings[overbuilt].Name) : "workshops");

                // Growth: dwellings, stronger when beds are scarce.
                float freeRatio = (sv.FreeBeds + sv.PendingHousing) / Math.Max(1f, sv.Population);
                float pressure = s.Homeless > 0 ? 1f : 1f - MathUtil.SmoothStep(0.05f, 0.3f, freeRatio);
                float houseStrength = shortageSev * (0.45f + 0.55f * pressure);
                bool houseVoted = false;
                for (int i = 0; i < db.Buildings.Count && i < Counts.Buildings; i++)
                {
                    var def = db.Buildings[i];
                    if (def == null || !def.IsDwelling || def.HasJobs || def.IsWonder) continue;
                    if (!db.IsBuildingAvailable(civ, (BuildingType)i)) continue;
                    if (def.MaxPerSettlement >= 0 && sv.ExistingByType[i] + sv.QueuedByType[i] >= def.MaxPerSettlement) continue;
                    bool primary = i == (int)BuildingType.House;
                    VoteExplained(s, civ, (BuildingType)i, houseStrength * (primary ? 1f : 0.6f), primary || !houseVoted ? msg : null);
                    if (primary || !houseVoted) top = i;
                    houseVoted = true;
                }

                // Brake on new job buildings (keep food buildings when hungry and every farm is staffed).
                for (int i = 0; i < Counts.Buildings; i++)
                {
                    if (!_value.Candidate[i]) continue;
                    var job = _value.JobOf[i];
                    if (job == JobType.Soldier && !peace) continue;
                    int open = sv.OpenByJob[(int)job];
                    if (_value.MainDomain[i] == LaborDomain.Food && foodNeed >= 0.6f && open == 0) continue;
                    float rel = _value.MaxJobValue > 0f ? _value.BaseScore[i] / _value.MaxJobValue : 0f;
                    float strength = open > 0 ? -0.9f * shortageSev : -0.45f * shortageSev * (1f - 0.5f * rel);
                    VoteExplained(s, civ, (BuildingType)i, strength, i == overbuilt ? msg : null);
                }

                // Staff the most critical unfilled jobs first (food first).
                int nJobs = PickCriticalJobs(sv, sv.AtWar, foodNeed);
                for (int r = 0; r < nJobs; r++)
                {
                    int j = _topJobs[r];
                    VoteJob(s, civ, (JobType)j, shortageSev * (0.35f + 0.65f * _jobCrit[j]) * s_rank[r]);
                }
                for (int j = 1; j < Counts.Jobs; j++)
                {
                    if (sv.OpenByJob[j] <= 0 || _jobCrit[j] >= 0.3f || IsTop(j, nJobs)) continue;
                    if (j == (int)JobType.Soldier && !sv.AtWar) continue;
                    if (_value.JobKnown[j] && _value.JobDomain[j] == LaborDomain.Food) continue;
                    VoteJob(s, civ, (JobType)j, -0.25f * shortageSev);
                }
                if (sv.Builders > sv.DesiredBuilders + 1) VoteJob(s, civ, JobType.Builder, -0.35f * shortageSev);
            }

            // ------------------------------------------------------------ balanced: anticipate & fill
            if (mode == 0)
            {
                float incoming = sv.PipelineCivilian - Math.Max(0, sv.Unemployed - civilianOpen) - sv.Youths;
                if (incoming >= 3f && db.IsBuildingAvailable(civ, BuildingType.House))
                {
                    string reason = explain ? MathUtil.RoundToInt(incoming) + " jobs coming online soon: homes first" : null;
                    VoteExplained(s, civ, BuildingType.House, 0.2f + 0.3f * Math.Min(1f, incoming * 2f / denom), reason);
                    top = (int)BuildingType.House;
                }
            }
            if (mode != -1 && sv.Unemployed > 0 && civilianOpen > 0)
            {
                // People idle while slots are open: push assignment toward the valuable ones.
                int nJobs = PickCriticalJobs(sv, sv.AtWar, foodNeed);
                for (int r = 0; r < nJobs; r++) VoteJob(s, civ, (JobType)_topJobs[r], (0.2f + 0.3f * _jobCrit[_topJobs[r]]) * s_rank[r]);
            }

            // ------------------------------------------------------------ critical food staffing (any mode)
            if (foodNeed >= 0.5f || recovery > 0f)
            {
                float fs = Math.Max(0.3f + 0.7f * foodNeed, recovery > 0f ? 0.5f + 0.5f * recovery : 0f);
                for (int j = 1; j < Counts.Jobs; j++)
                    if (sv.OpenByJob[j] > 0 && _value.JobKnown[j] && _value.JobDomain[j] == LaborDomain.Food && j != (int)JobType.Soldier)
                        VoteJob(s, civ, (JobType)j, fs);
            }

            // ------------------------------------------------------------ builders
            if (sv.ConstructionActive)
            {
                float bs = 0f;
                if (gap > 0 && sv.Unemployed > 0) bs = 0.3f + 0.6f * Math.Min(1f, Math.Min(gap, sv.Unemployed) / (float)Math.Max(1, sv.DesiredBuilders));
                if (mode == 1) bs = Math.Max(bs, 0.45f * surplusSev + (gap > 0 ? 0.3f : 0f));
                if (recovery > 0f) bs = Math.Max(bs, 0.4f + 0.4f * recovery);
                if (bs > 0f) VoteJob(s, civ, JobType.Builder, bs);
            }

            // ------------------------------------------------------------ soldiers
            if (militaryExcess > 0f)
            {
                VoteJob(s, civ, JobType.Soldier, -militaryExcess);
                string reason = explain ? "peacetime: " + Pct(soldierEma) + "% of adults under arms (target " + Pct(soldierTarget) + "%)" : null;
                for (int i = 0; i < Counts.Buildings; i++)
                {
                    if (!_value.Candidate[i] || _value.JobOf[i] != JobType.Soldier) continue;
                    VoteExplained(s, civ, (BuildingType)i, -0.7f * militaryExcess, reason);
                    reason = null;
                }
            }
            else if (sv.AtWar && soldierShare < soldierTarget)
            {
                float deficit = MathUtil.Clamp01(1f - soldierShare / Math.Max(0.01f, soldierTarget));
                if (sv.Unemployed > 0 || mode == 1) VoteJob(s, civ, JobType.Soldier, 0.3f + 0.6f * deficit);
                if (sv.OpenMilitary == 0 && deficit > 0.3f)
                {
                    int bestMil = -1;
                    for (int i = 0; i < Counts.Buildings; i++)
                        if (_value.Candidate[i] && _value.JobOf[i] == JobType.Soldier && (bestMil < 0 || _value.Score[i] > _value.Score[bestMil])) bestMil = i;
                    if (bestMil >= 0)
                        VoteExplained(s, civ, (BuildingType)bestMil, 0.3f + 0.5f * deficit,
                            explain ? "at war with " + Pct(soldierShare) + "% under arms: raise a garrison" : null);
                }
            }

            if (recovery > 0f && explain)
                s.LogAi("[" + Id + "] recovering: population down " + Pct(recovery) + "%, staffing food and builders first");

            // ------------------------------------------------------------ urgency & memory
            float urgency = Math.Max(surplusSev, shortageSev * 0.9f);
            urgency = Math.Max(urgency, militaryExcess * 0.6f);
            urgency = Math.Max(urgency, recovery);
            if (sv.ConstructionActive && gap > 0) urgency = Math.Max(urgency, 0.4f * Math.Min(1f, gap / (float)Math.Max(1, sv.DesiredBuilders)));
            Urgency(s, urgency);

            mem[LaborKeys.Initialized] = 1f;
            mem[LaborKeys.Balance] = MathUtil.Clamp(MathUtil.Sanitize(ema), -1f, 1f);
            mem[LaborKeys.SoldierShare] = MathUtil.Clamp01(MathUtil.Sanitize(soldierEma));
            mem[LaborKeys.PopEma] = Math.Max(0f, MathUtil.Sanitize(popEma));
            mem[LaborKeys.Mode] = mode;
            mem[LaborKeys.Unemployed] = sv.Unemployed;
            mem[LaborKeys.OpenSlots] = civilianOpen;
            mem[LaborKeys.Top] = top;
            mem[LaborKeys.Recovery] = recovery;
            if (explain) { mem[LaborKeys.LogTick] = ctx.Tick; mem[LaborKeys.LogSignature] = sig; }
        }

        // ================================================================== helpers
        /// <summary>
        /// Build vote whose strength is expressed per council cycle (the board scales each run by <see cref="VoteScale"/>).
        /// Strong votes (|strength x weight| &gt;= 0.5 per cycle) are explained in the settlement's AI log.
        /// </summary>
        private void VoteExplained(Settlement s, Civilization civ, BuildingType t, float strength, string reason)
        {
            VoteBuild(s, civ, t, strength, null);
            if (reason == null) return;
            float v = MathUtil.Clamp(MathUtil.Sanitize(strength), -1f, 1f) * AdvisorBoard.Weight(civ, Id);
            if (Math.Abs(v) >= 0.5f) s.LogAi("[" + Id + "] " + (v > 0f ? "+" : "") + v.ToString("0.00") + " " + t + ": " + reason);
        }

        private static float Ema(float prev, float raw, float alpha)
        {
            prev = MathUtil.Sanitize(prev, raw);
            return MathUtil.Sanitize(prev + (raw - prev) * alpha, raw);
        }

        /// <summary>Top 3 candidate job buildings by score (ties keep the lower type index; deterministic).</summary>
        private int PickTopBuildings(bool atWar)
        {
            int n = 0;
            for (int k = 0; k < _topBuildings.Length; k++) _topBuildings[k] = -1;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                if (!_value.Candidate[i]) continue;
                float sc = _value.Score[i];
                if (sc <= MathUtil.Epsilon) continue;
                if (!atWar && _value.JobOf[i] == JobType.Soldier) continue; // idle hands are not a reason for barracks in peace
                n = InsertTop(_topBuildings, n, i, sc, _value.Score);
            }
            return n;
        }

        /// <summary>Ranks jobs with open slots by criticality (normalized job value; food floors at 0.5 + half its need).</summary>
        private int PickCriticalJobs(LaborMarketSurvey sv, bool atWar, float foodNeed)
        {
            int n = 0;
            for (int k = 0; k < _topJobs.Length; k++) _topJobs[k] = -1;
            float maxV = _value.MaxJobValue;
            for (int j = 0; j < Counts.Jobs; j++)
            {
                _jobCrit[j] = 0f;
                if (j == 0 || sv.OpenByJob[j] <= 0) continue;
                if (j == (int)JobType.Soldier && !atWar) continue;
                float crit = _value.JobKnown[j] && maxV > 0f ? _value.JobValue[j] / maxV : 0.5f;
                if (_value.JobKnown[j] && _value.JobDomain[j] == LaborDomain.Food) crit = Math.Max(crit, 0.5f + 0.5f * foodNeed);
                _jobCrit[j] = MathUtil.Clamp01(MathUtil.Sanitize(crit));
            }
            for (int j = 1; j < Counts.Jobs; j++)
            {
                if (sv.OpenByJob[j] <= 0 || (j == (int)JobType.Soldier && !atWar)) continue;
                n = InsertTop(_topJobs, n, j, _jobCrit[j], _jobCrit);
            }
            return n;
        }

        private static int InsertTop(int[] top, int n, int idx, float score, float[] scores)
        {
            int pos = n;
            while (pos > 0 && scores[top[pos - 1]] < score) pos--;
            if (pos >= top.Length) return n;
            int last = Math.Min(n, top.Length - 1);
            for (int k = last; k > pos; k--) top[k] = top[k - 1];
            top[pos] = idx;
            return Math.Min(n + 1, top.Length);
        }

        private bool IsTop(int j, int n)
        {
            for (int k = 0; k < n; k++) if (_topJobs[k] == j) return true;
            return false;
        }

        /// <summary>The existing job building type whose job has the most unfilled slots (the thing not to build more of).</summary>
        private int FindOverbuilt(GameDatabase db, LaborMarketSurvey sv)
        {
            int best = -1, bestOpen = 0;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                if (!_value.Relevant[i] || sv.ExistingByType[i] <= 0) continue;
                var job = _value.JobOf[i];
                if (job == JobType.Soldier && !sv.AtWar) continue;
                int open = sv.OpenByJob[(int)job];
                if (open > bestOpen) { bestOpen = open; best = i; }
            }
            return best;
        }

        private string SurplusReason(LaborMarketSurvey sv, BuildingDefinition def, LaborDomain domain)
        {
            string text = sv.Unemployed + " idle (" + Pct(sv.Unemployed / (float)Math.Max(1, sv.Adults)) + "%): " + def.Name + " employs " + def.JobSlots + " in " + LaborDomains.Name(domain);
            int d = (int)domain;
            if (_value.DomainFromAdvisor[d]) text += " (urgency " + _value.DomainNeed[d].ToString("0.00") + ")";
            return text;
        }

        private static string Plural(string name)
        {
            if (string.IsNullOrEmpty(name)) return "workshops";
            string lower = name.ToLowerInvariant();
            return lower.EndsWith("s", StringComparison.Ordinal) ? lower : lower + "s";
        }

        private static string Pct(float v) => MathUtil.RoundToInt(MathUtil.Clamp01(MathUtil.Sanitize(v)) * 100f).ToString();
    }
}
