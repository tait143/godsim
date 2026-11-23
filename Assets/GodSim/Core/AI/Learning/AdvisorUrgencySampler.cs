namespace GodSim.Core.AI
{
    /// <summary>
    /// Samples every known advisor's settlement urgency once per season and accumulates population-weighted sums
    /// into civ memory ("ai.learn.us.{id}" / "ai.learn.uw.{id}"), so <see cref="AdvisorLearningSystem"/> judges the
    /// advisor's average urgency over the whole two-year period instead of a single snapshot. Allocation-free.
    /// Register after the advisors (before or after the learner; the learner falls back to a snapshot without it).
    /// </summary>
    public sealed class AdvisorUrgencySampler : ISimSystem
    {
        /// <summary>Accumulated weight above which sums are halved (keeps floats well-conditioned).</summary>
        public const float MaxAccumulatedWeight = 1e6f;

        public string Name => "AI.Learning.Sampler";
        public int TickInterval => 24;
        public void Initialize(SimContext ctx) { }

        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var settlements = ctx.State.Settlements.Items;
            var domains = AdvisorDomains.All;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (!s.IsAlive) continue;
                var civ = ctx.State.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;
                float w = 1f + s.Population;
                var mem = civ.Memory;
                for (int d = 0; d < domains.Count; d++)
                {
                    var dom = domains[d];
                    if (!s.Memory.TryGetValue(dom.UrgencyKey, out float u)) continue;
                    float sum = MathUtil.Sanitize(civ.Mem(dom.UrgencySumKey)) + MathUtil.Clamp01(MathUtil.Sanitize(u)) * w;
                    float tw = MathUtil.Sanitize(civ.Mem(dom.UrgencyWeightKey)) + w;
                    if (tw > MaxAccumulatedWeight) { sum *= 0.5f; tw *= 0.5f; }
                    mem[dom.UrgencySumKey] = sum;
                    mem[dom.UrgencyWeightKey] = tw;
                }
            }
        }
    }
}
