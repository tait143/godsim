using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Every two years, judges each civilization's advisors against outcomes and tunes their council weights
    /// ("ai.w.{id}") with a bounded, deterministic credit-assignment rule:
    /// <list type="bullet">
    /// <item>domain metric poor and not improving while the advisor's average urgency was high: raise (it was right to worry);</item>
    /// <item>metric fine (not sliding) while the advisor screamed: lower (crying wolf);</item>
    /// <item>metric fine but sliding while the advisor warned: small raise (foresight);</item>
    /// <item>metric excellent and the advisor calm: relax toward its personality prior (1 for a neutral civ);</item>
    /// <item>otherwise: slow drift toward the prior (regularisation).</item>
    /// </list>
    /// A tiny exploration perturbation (one <c>ctx.Rng</c> draw per civ, hashed per advisor) keeps weights from
    /// freezing. Each change is limited to ±<see cref="MaxStep"/> and weights stay in [<see cref="MinWeight"/>,
    /// <see cref="MaxWeight"/>]. The first evaluation of a civ only observes (baseline). Only advisors that actually
    /// reported urgency (see <see cref="AdvisorUrgencySampler"/>) are judged. The last change and its reason per
    /// advisor are kept in civ memory ("ai.learn.d.{id}", "ai.learn.r.{id}").
    /// </summary>
    public sealed class AdvisorLearningSystem : ISimSystem
    {
        public const float MinWeight = 0.4f;
        public const float MaxWeight = 2.2f;
        public const float MaxStep = 0.15f;
        public const float PoorBelow = 0.45f;
        public const float FineAbove = 0.65f;
        public const float ExcellentAbove = 0.8f;
        public const float HighUrgency = 0.55f;
        public const float LowUrgency = 0.3f;
        public const float ImprovingBy = 0.03f;
        public const float SlidingBy = 0.05f;
        public const float Exploration = 0.02f;

        private readonly float[] _prev = new float[AILearningKeys.MetricCount];
        private readonly float[] _cur = new float[AILearningKeys.MetricCount];

        public string Name => "AI.Learning";
        public int TickInterval => 192;

        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i].IsAlive) SeedPriors(civs[i], ctx.Db);
        }

        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (civs[i].IsAlive) EvaluateCivilization(ctx, civs[i]);
        }

        /// <summary>Runs one learning evaluation for a civ (scorecard, credit assignment, log). Public for tests/tools.</summary>
        public void EvaluateCivilization(SimContext ctx, Civilization civ)
        {
            if (ctx == null || ctx.State == null || civ == null) return;
            var mem = civ.Memory;
            SeedPriors(civ, ctx.Db);

            bool hasPrev = mem.ContainsKey(AILearningKeys.PreviousKey(AIMetric.Overall));
            for (int m = 0; m < AILearningKeys.MetricCount; m++)
                _prev[m] = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(AILearningKeys.PreviousKey((AIMetric)m), 0.5f), 0.5f));

            AIScorecard.Evaluate(ctx.State, ctx.Db, civ, out int population);
            for (int m = 0; m < AILearningKeys.MetricCount; m++) _cur[m] = AIScorecard.Get(civ, (AIMetric)m);

            ulong noiseSeed = ctx.Rng.NextULong(); // exactly one draw per civ per evaluation
            bool learn = hasPrev && population > 0;
            var domains = AdvisorDomains.All;
            for (int i = 0; i < domains.Count; i++)
            {
                var d = domains[i];
                if (!AverageUrgency(ctx.State, civ, d, out float urgency)) continue;
                if (!learn) continue;

                int mi = (int)d.Metric;
                float metric = _cur[mi];
                float trend = metric - _prev[mi];
                float old = AdvisorBoard.Weight(civ, d.Id);
                float prior = AdvisorDomains.Prior(civ, ctx.Db, d);
                float delta = Credit(metric, trend, urgency, old, prior, out LearningReason reason);
                ulong h = Hashing.Combine(noiseSeed, d.Hash);
                float noise = ((h >> 40) * (1f / 16777216f) * 2f - 1f) * Exploration;
                float next = Step(old, delta + noise);
                AdvisorBoard.SetWeight(civ, d.Id, next);
                mem[d.DeltaKey] = MathUtil.Sanitize(next - old);
                mem[d.ReasonKey] = (float)reason;
            }

            // reset the urgency samples for the next period (keys kept: no re-allocation)
            for (int i = 0; i < domains.Count; i++)
            {
                var d = domains[i];
                if (mem.ContainsKey(d.UrgencyWeightKey)) { mem[d.UrgencySumKey] = 0f; mem[d.UrgencyWeightKey] = 0f; }
            }
            for (int m = 0; m < AILearningKeys.MetricCount; m++) mem[AILearningKeys.PreviousKey((AIMetric)m)] = _cur[m];
            mem[AILearningKeys.Evaluations] = Math.Min(1e7f, civ.Mem(AILearningKeys.Evaluations) + 1f);
            mem[AILearningKeys.LastEvaluationTick] = ctx.State.Tick;
        }

        /// <summary>
        /// The credit-assignment rule (pure, bounded): returns the proposed weight change before exploration and
        /// clamping. <paramref name="metric"/> is the domain score 0..1, <paramref name="trend"/> its change since the
        /// last evaluation, <paramref name="urgency"/> the advisor's average urgency 0..1 over the period.
        /// </summary>
        public static float Credit(float metric, float trend, float urgency, float weight, float prior, out LearningReason reason)
        {
            metric = MathUtil.Clamp01(MathUtil.Sanitize(metric, 0.5f));
            trend = MathUtil.Clamp(MathUtil.Sanitize(trend), -1f, 1f);
            urgency = MathUtil.Clamp01(MathUtil.Sanitize(urgency));
            weight = MathUtil.Clamp(MathUtil.Sanitize(weight, 1f), 0f, 3f);
            prior = MathUtil.Clamp(MathUtil.Sanitize(prior, 1f), AdvisorDomains.MinPrior, AdvisorDomains.MaxPrior);
            float conviction = MathUtil.Clamp01((urgency - HighUrgency) / (1f - HighUrgency));

            if (metric < PoorBelow && trend < ImprovingBy && urgency >= HighUrgency)
            {
                reason = LearningReason.Heeded;
                float severity = MathUtil.Clamp01((PoorBelow - metric) / PoorBelow);
                return 0.05f + 0.10f * 0.5f * (severity + conviction);
            }
            if (metric >= FineAbove && urgency >= HighUrgency)
            {
                if (trend <= -SlidingBy) { reason = LearningReason.Foresight; return 0.03f; }
                reason = LearningReason.CryingWolf;
                return -(0.04f + 0.08f * conviction + 0.03f * MathUtil.Clamp01((metric - FineAbove) / (1f - FineAbove)));
            }
            if (metric >= ExcellentAbove && urgency < LowUrgency)
            {
                reason = LearningReason.Relaxed;
                return MathUtil.Clamp((prior - weight) * 0.25f, -0.05f, 0.05f);
            }
            reason = LearningReason.Drift;
            return (prior - weight) * 0.02f;
        }

        /// <summary>Applies a change limited to ±MaxStep and clamps the result to [MinWeight, MaxWeight].</summary>
        public static float Step(float weight, float delta)
        {
            weight = MathUtil.Clamp(MathUtil.Sanitize(weight, 1f), 0f, 3f);
            delta = MathUtil.Clamp(MathUtil.Sanitize(delta), -MaxStep, MaxStep);
            return MathUtil.Clamp(weight + delta, MinWeight, MaxWeight);
        }

        /// <summary>Writes personality priors as starting weights the first time a civ is seen (keeps existing weights).</summary>
        public static void SeedPriors(Civilization civ, GameDatabase db)
        {
            if (civ == null || civ.Memory.ContainsKey(AILearningKeys.PriorsSeeded)) return;
            var domains = AdvisorDomains.All;
            for (int i = 0; i < domains.Count; i++)
            {
                var d = domains[i];
                if (civ.Memory.ContainsKey(d.WeightKey)) continue;
                float p = AdvisorDomains.Prior(civ, db, d);
                if (p != 1f) AdvisorBoard.SetWeight(civ, d.Id, MathUtil.Clamp(p, MinWeight, MaxWeight));
            }
            civ.Memory[AILearningKeys.PriorsSeeded] = 1f;
        }

        /// <summary>
        /// Average urgency of an advisor over the period: the sampler's population-weighted mean when available,
        /// else a population-weighted snapshot of current settlement urgency. False when the advisor never reported.
        /// </summary>
        public static bool AverageUrgency(WorldState state, Civilization civ, AdvisorDomain d, out float urgency)
        {
            urgency = 0f;
            if (civ == null || d == null) return false;
            float w = civ.Mem(d.UrgencyWeightKey);
            if (w > 0f && !float.IsNaN(w) && !float.IsInfinity(w))
            {
                urgency = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(d.UrgencySumKey) / w));
                return true;
            }
            if (state == null) return false;
            float sum = 0f, total = 0f;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                if (!s.Memory.TryGetValue(d.UrgencyKey, out float u)) continue;
                float sw = 1f + s.Population;
                sum += MathUtil.Clamp01(MathUtil.Sanitize(u)) * sw;
                total += sw;
            }
            if (!(total > 0f)) return false;
            urgency = MathUtil.Clamp01(sum / total);
            return true;
        }
    }
}
