using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Eating. Every tick each living settlement feeds its residents from its stockpile:
    /// need = sum of race.FoodPerTick (children 0.6x, elders 0.8x) x DeltaTicks; the fed ratio is
    /// take / need. Satiation follows the ratio; starving people (satiation &lt; 0.15) lose health and
    /// die when it reaches 0, and people at satiation &lt;= 0.02 may die of starvation at random
    /// (0.6 per year). A fed ratio below 0.7 is a famine: a <see cref="FamineEvent"/> at most every
    /// 24 ticks and a chronicle entry at most once a year per settlement.
    /// Also writes <see cref="LifecycleMemoryKeys.FoodNeedPerTick"/> and <see cref="LifecycleMemoryKeys.FoodRatio"/>.
    /// O(people), allocation-free in steady state.
    /// </summary>
    public sealed class NeedsSystem : ISimSystem
    {
        private readonly List<Person> _residents = new List<Person>(128);
        private readonly float[] _foodNeed = new float[LifecycleRules.FoodNeedTableSize];

        /// <summary>Creates the system.</summary>
        public NeedsSystem() { }

        /// <inheritdoc />
        public string Name => "Population.Needs";

        /// <inheritdoc />
        public int TickInterval => 1;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            LifecycleRules.FillFoodNeedTable(ctx.Db, _foodNeed);
            float deathChance = Rng.PerYearToPerTicks(LifecycleBalance.StarvationDeathPerYear, dt);
            float smoothing = 1f - (float)Math.Pow(1.0 - LifecycleBalance.FoodRatioSmoothingPerTick, dt);

            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                FeedSettlement(ctx, s, dt, deathChance, smoothing);
            }
            _residents.Clear();
        }

        private void FeedSettlement(SimContext ctx, Settlement s, int dt, float deathChance, float smoothing)
        {
            var people = ctx.State.People;
            var ids = s.ResidentIds;
            _residents.Clear();
            float needPerTick = 0f;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = people.Get(ids[i]);
                if (p == null || !p.IsAlive) continue;
                _residents.Add(p);
                needPerTick += LifecycleRules.FoodNeedOf(_foodNeed, p);
            }
            if (_residents.Count == 0) return;

            needPerTick = Math.Max(0f, MathUtil.Sanitize(needPerTick));
            float need = needPerTick * dt;
            float taken = 0f;
            float ratio = 1f;
            if (need > MathUtil.Epsilon)
            {
                taken = s.Stockpile.ConsumeUpTo(ResourceType.Food, need);
                ratio = MathUtil.Clamp01(MathUtil.Sanitize(taken / need));
                if (ratio > 0.9999f) ratio = 1f; // absorb float rounding when fully fed
            }

            s.Memory[LifecycleMemoryKeys.FoodNeedPerTick] = needPerTick;
            float prevRatio = MathUtil.Clamp01(s.Mem(LifecycleMemoryKeys.FoodRatio, 1f));
            s.Memory[LifecycleMemoryKeys.FoodRatio] = MathUtil.Clamp01(MathUtil.Sanitize(prevRatio + (ratio - prevRatio) * smoothing, 1f));

            var rng = ctx.Rng;
            for (int i = 0; i < _residents.Count; i++)
            {
                var p = _residents[i];
                if (!p.IsAlive) continue;
                float sat = LifecycleRules.StepSatiation(p.Satiation, ratio, dt);
                p.Satiation = sat;
                if (sat >= LifecycleBalance.StarvingSatiation) continue;

                float health = MathUtil.Clamp01(MathUtil.Sanitize(p.Health, 1f)) -LifecycleBalance.StarvationHealthLossPerTick * dt;
                p.Health = health < 0f ? 0f : health;
                if (p.Health <= 0f || (sat <= LifecycleBalance.StarvationDeathSatiation && rng.Chance(deathChance)))
                    ctx.Ops.KillPerson(p, DeathCause.Starvation); // removes p from s.ResidentIds; we iterate the buffer
            }

            if (need > MathUtil.Epsilon && ratio < LifecycleBalance.FamineRatio)
                ReportFamine(ctx, s, need - taken);
        }

        private static void ReportFamine(SimContext ctx, Settlement s, float shortfall)
        {
            long tick = ctx.Tick;
            long last = s.LastStarvationTick;
            if (last >= 0 && last <= tick && tick - last < LifecycleBalance.FamineEventCooldownTicks) return;
            s.LastStarvationTick = tick;
            if (ctx.Events != null)
                ctx.Events.Publish(new FamineEvent { SettlementId = s.Id, Shortfall = Math.Max(0f, MathUtil.Sanitize(shortfall)) });

            float lastChronicle = s.Mem(LifecycleMemoryKeys.LastFamineChronicleTick, -1f);
            bool chronicleDue = lastChronicle < 0f || lastChronicle > tick || tick - (long)lastChronicle >= LifecycleBalance.FamineChronicleCooldownTicks;
            if (!chronicleDue) return;
            s.Memory[LifecycleMemoryKeys.LastFamineChronicleTick] = tick;
            ctx.Ops.AddChronicle(ChronicleCategory.Disaster, "Famine grips " + s.Name + "; its granaries are empty and its people go hungry.",
                s.CivilizationId, s.Id, s.Center, 1);
        }
    }
}
