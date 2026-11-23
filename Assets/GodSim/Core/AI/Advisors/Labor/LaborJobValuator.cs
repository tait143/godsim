using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven value of one worker in each job building for a specific settlement. Every job building's
    /// per-worker outputs (<see cref="BuildingDefinition.OutputsPerWorker"/>) and per-worker services are mapped
    /// to <see cref="LaborDomain"/>s, normalized against the best building of that domain (scale-free across
    /// resources and services) and weighted by the domain's need: other advisors' urgencies ("food", "supply",
    /// "science", "treasury"...) when reported, otherwise a local estimate, times the civ's personality.
    /// Allocation-free; all arrays are reused.
    /// </summary>
    public sealed class LaborJobValuator
    {
        private const int C = LaborDomains.Count;

        /// <summary>Need of each domain 0..1 (advisor urgency or local estimate).</summary>
        public readonly float[] DomainNeed = new float[C];
        /// <summary>True when the need came from another advisor's reported urgency.</summary>
        public readonly bool[] DomainFromAdvisor = new bool[C];
        /// <summary>Final weight per domain (need + baseline, times personality).</summary>
        public readonly float[] DomainWeight = new float[C];

        /// <summary>True for job buildings that are available or exist in the settlement.</summary>
        public readonly bool[] Relevant = new bool[Counts.Buildings];
        /// <summary>True for job buildings the settlement could order now (available, tier, population, caps).</summary>
        public readonly bool[] Candidate = new bool[Counts.Buildings];
        /// <summary>Job of each relevant building type.</summary>
        public readonly JobType[] JobOf = new JobType[Counts.Buildings];
        /// <summary>Value per worker (inputs considered), before open-slot / pipeline penalties.</summary>
        public readonly float[] BaseScore = new float[Counts.Buildings];
        /// <summary>Value of ordering one more of this type now (penalized when the job is unstaffed or one is pending).</summary>
        public readonly float[] Score = new float[Counts.Buildings];
        /// <summary>Domain contributing most to a building's value.</summary>
        public readonly LaborDomain[] MainDomain = new LaborDomain[Counts.Buildings];

        /// <summary>Best per-worker value of any building employing the job.</summary>
        public readonly float[] JobValue = new float[Counts.Jobs];
        public readonly LaborDomain[] JobDomain = new LaborDomain[Counts.Jobs];
        public readonly bool[] JobKnown = new bool[Counts.Jobs];

        public float BestScore;
        public float MaxJobValue;

        private readonly float[] _raw = new float[Counts.Buildings * C];
        private readonly float[] _max = new float[C];

        public float Need(LaborDomain d) => DomainNeed[(int)d];

        /// <summary>Evaluates domain needs and every job building for the settlement.</summary>
        public void Evaluate(SimContext ctx, Civilization civ, Settlement s, LaborMarketSurvey sv)
        {
            Array.Clear(_raw, 0, _raw.Length);
            Array.Clear(_max, 0, _max.Length);
            Array.Clear(Relevant, 0, Relevant.Length);
            Array.Clear(Candidate, 0, Candidate.Length);
            Array.Clear(BaseScore, 0, BaseScore.Length);
            Array.Clear(Score, 0, Score.Length);
            Array.Clear(JobValue, 0, JobValue.Length);
            Array.Clear(JobKnown, 0, JobKnown.Length);
            BestScore = 0f;
            MaxJobValue = 0f;
            if (ctx == null || ctx.Db == null || civ == null || s == null || sv == null) return;
            var db = ctx.Db;
            EvaluateNeeds(db, civ, s, sv);

            var mods = civ.Modifiers;
            var defs = db.Buildings;
            int n = Math.Min(defs.Count, Counts.Buildings);
            for (int i = 0; i < n; i++)
            {
                var def = defs[i];
                JobOf[i] = JobType.None;
                if (def == null || !def.HasJobs || def.IsDwelling) continue;
                var type = (BuildingType)i;
                bool available = db.IsBuildingAvailable(civ, type);
                if (!available && sv.ExistingByType[i] <= 0) continue;
                Relevant[i] = true;
                JobOf[i] = def.Job;
                int baseIdx = i * C;

                var outs = def.OutputsPerWorker;
                if (outs != null)
                    for (int k = 0; k < outs.Length; k++)
                    {
                        int r = (int)outs[k].Type;
                        if (r < 0 || r >= Counts.Resources) continue;
                        float a = Math.Max(0f, MathUtil.Sanitize(outs[k].Amount)) * Positive(mods.Production[r]);
                        _raw[baseIdx + (int)LaborDomains.OfResource(outs[k].Type)] += a;
                    }
                var svcs = def.Services;
                if (svcs != null)
                    for (int k = 0; k < svcs.Length; k++)
                    {
                        int sx = (int)svcs[k].Type;
                        if (sx < 0 || sx >= Counts.Services) continue;
                        float a = Math.Max(0f, MathUtil.Sanitize(svcs[k].Amount)) / def.JobSlots * Positive(mods.Service[sx]);
                        _raw[baseIdx + (int)LaborDomains.OfService(svcs[k].Type)] += a;
                    }
                for (int d = 0; d < C; d++) if (_raw[baseIdx + d] > _max[d]) _max[d] = _raw[baseIdx + d];

                if (available && !def.IsWonder && !def.UniquePerCivilization && def.MinTier <= s.Tier && def.MinPopulation <= sv.Population
                    && (def.MaxPerSettlement < 0 || sv.ExistingByType[i] + sv.QueuedByType[i] < def.MaxPerSettlement))
                    Candidate[i] = true;
            }

            for (int i = 0; i < n; i++)
            {
                if (!Relevant[i]) continue;
                var def = defs[i];
                int baseIdx = i * C;
                float score = 0f, best = -1f;
                int main = 0;
                for (int d = 0; d < C; d++)
                {
                    if (_max[d] <= MathUtil.Epsilon) continue;
                    float c = _raw[baseIdx + d] / _max[d] * DomainWeight[d];
                    score += c;
                    if (c > best) { best = c; main = d; }
                }

                // Inputs the settlement neither stocks nor produces make a job far less valuable (no smelter without ore).
                var ins = def.InputsPerWorker;
                if (ins != null)
                    for (int k = 0; k < ins.Length; k++)
                    {
                        int r = (int)ins[k].Type;
                        if (r < 0 || r >= Counts.Resources) continue;
                        float perRun = Math.Max(0f, MathUtil.Sanitize(ins[k].Amount)) * def.JobSlots * 12f;
                        if (perRun > 0f && s.Stockpile.Get(ins[k].Type) < perRun * 2f && s.Stockpile.Net(ins[k].Type) <= 0f) score *= 0.45f;
                    }
                score = MathUtil.Sanitize(score);
                BaseScore[i] = score;
                MainDomain[i] = (LaborDomain)main;

                int j = (int)def.Job;
                if (!JobKnown[j] || score > JobValue[j]) { JobValue[j] = score; JobDomain[j] = (LaborDomain)main; }
                JobKnown[j] = true;
                if (score > MaxJobValue) MaxJobValue = score;

                if (Candidate[i])
                {
                    // Anticipation: do not order more of a job that is already unstaffed or of a type already in the pipeline.
                    float c = score / (1f + 0.5f * sv.OpenByJob[j]) / (1f + sv.PendingByType[i]);
                    c = MathUtil.Sanitize(c);
                    Score[i] = c;
                    if (c > BestScore) BestScore = c;
                }
            }
        }

        private void EvaluateNeeds(GameDatabase db, Civilization civ, Settlement s, LaborMarketSurvey sv)
        {
            bool peace = !sv.AtWar && sv.Threat < 0.4f;
            for (int d = 0; d < C; d++)
            {
                var dom = (LaborDomain)d;
                bool fromAdvisor = LaborDomains.TryReadUrgency(s, dom, out float need);
                if (!fromAdvisor) need = LocalNeed(db, s, sv, dom);
                need = MathUtil.Clamp01(MathUtil.Sanitize(need));
                DomainNeed[d] = need;
                DomainFromAdvisor[d] = fromAdvisor;
                float w = (0.15f + need) * LaborDomains.TraitMultiplier(civ, db, dom);
                if (dom == LaborDomain.Food && need >= 0.6f) w *= 1.25f;   // food first
                if (dom == LaborDomain.Defense && peace) w *= 0.6f;
                DomainWeight[d] = MathUtil.Sanitize(w);
            }
        }

        /// <summary>Fallback need estimate when no specialist advisor reported an urgency for the domain.</summary>
        private static float LocalNeed(GameDatabase db, Settlement s, LaborMarketSurvey sv, LaborDomain d)
        {
            var st = s.Stockpile;
            switch (d)
            {
                case LaborDomain.Food:
                {
                    if (sv.Population <= 0) return 0.2f;
                    float days;
                    if (s.Memory.TryGetValue(LaborKeys.FoodDays, out float fd)) days = MathUtil.Sanitize(fd, 30f);
                    else
                    {
                        float perTick = 0.05f;
                        if ((int)s.Race < db.Races.Count && db.Races[(int)s.Race] != null) perTick = Math.Max(0.005f, db.Races[(int)s.Race].FoodPerTick);
                        days = st.Get(ResourceType.Food) / Math.Max(0.01f, sv.Population * perTick);
                    }
                    float need = 1f - MathUtil.InverseLerp(15f, 90f, days);
                    if (st.Net(ResourceType.Food) < 0f) need = Math.Max(need, 0.55f);
                    return need;
                }
                case LaborDomain.Materials: return 0.15f + 0.5f * (1f - MathUtil.InverseLerp(20f, 150f, st.Get(ResourceType.Wood) + st.Get(ResourceType.Stone)));
                case LaborDomain.Industry: return 0.2f;
                case LaborDomain.Wealth: return st.Net(ResourceType.Gold) < 0f ? 0.6f : 0.15f;
                case LaborDomain.Science: return 0.3f;
                case LaborDomain.Faith: return 0.15f + 0.25f * (1f - MathUtil.Clamp01(s.ServiceCoverage[(int)ServiceType.Faith]));
                case LaborDomain.Health: return 0.1f + 0.5f * (1f - MathUtil.Clamp01(s.Health));
                case LaborDomain.Defense: return 0.05f + 0.9f * Math.Max(sv.Threat, sv.AtWar ? 0.7f : 0f);
                case LaborDomain.Culture: return 0.1f + 0.4f * (1f - MathUtil.Clamp01(s.Happiness));
                default: return 0.2f;
            }
        }

        private static float Positive(float v) { v = MathUtil.Sanitize(v, 1f); return v > 0f ? v : 0f; }
    }
}
