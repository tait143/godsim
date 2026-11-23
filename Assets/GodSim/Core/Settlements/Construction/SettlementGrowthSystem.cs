using System;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Settlement growth tiers, territory and survival (runs every 24 ticks, once per season):
    /// <list type="bullet">
    /// <item>Tier from alive population with hysteresis: upgrade at Hamlet 10, Village 30, Town 80, City 200,
    /// Metropolis 500, Megalopolis 1200; downgrade only below 80% of the current tier's threshold. A change
    /// publishes <see cref="SettlementTierChangedEvent"/>; upgrades to Town or above are chronicled.</item>
    /// <item>Territory radius by tier (6, 8, 10, 13, 16, 20, 24) claimed through <see cref="WorldOps.ClaimTerritory"/>
    /// when it grows (never shrinks); the current radius is re-claimed every four years to absorb freed land.</item>
    /// <item>Without a Town Center, a <see cref="BuildOrder"/> (TownCenter, priority 10, "rebuild seat of power")
    /// is queued unless one is queued, under construction, or a complete one can be adopted.</item>
    /// <item>A settlement with no alive residents for two consecutive years is abandoned
    /// (<see cref="WorldOps.DestroySettlement"/> with <see cref="DestroyCause.Abandoned"/>).</item>
    /// </list>
    /// </summary>
    public sealed class SettlementGrowthSystem : ISimSystem
    {
        /// <summary>Settlement memory key: consecutive ticks with no alive residents (absent when populated).</summary>
        public const string EmptyTicksKey = "growth.emptyTicks";
        /// <summary>Ticks without residents before a settlement is abandoned (two years).</summary>
        public const int AbandonAfterTicks = 2 * SimTime.TicksPerYear;
        /// <summary>A tier is lost only when population falls below this fraction of its threshold.</summary>
        public const float DowngradeFraction = 0.8f;
        /// <summary>Priority of the Town Center rebuild order.</summary>
        public const float TownCenterOrderPriority = 10f;
        /// <summary>Reason text of the Town Center rebuild order.</summary>
        public const string TownCenterOrderReason = "rebuild seat of power";
        /// <summary>Runs between periodic re-claims of the current territory radius (16 runs = 4 years).</summary>
        public const int ReclaimPeriodRuns = 16;

        private static readonly int[] Thresholds = { 0, 10, 30, 80, 200, 500, 1200 };
        private static readonly int[] Radii = { 6, 8, 10, 13, 16, 20, 24 };

        /// <inheritdoc />
        public string Name => "SettlementGrowth";

        /// <inheritdoc />
        public int TickInterval => 24;

        /// <summary>Population needed to reach a tier (Camp 0 .. Megalopolis 1200).</summary>
        public static int PopulationThreshold(SettlementTier tier) => Thresholds[MathUtil.Clamp((int)tier, 0, Thresholds.Length - 1)];

        /// <summary>Territory radius (tiles) a settlement of the given tier claims.</summary>
        public static int TerritoryRadiusFor(SettlementTier tier) => Radii[MathUtil.Clamp((int)tier, 0, Radii.Length - 1)];

        /// <summary>
        /// Next tier for a settlement currently at <paramref name="current"/> with the given population: climbs
        /// while population reaches the next threshold, otherwise drops while population is below 80% of the
        /// current tier's threshold. May move several tiers at once (e.g. after a disaster).
        /// </summary>
        public static SettlementTier EvaluateTier(SettlementTier current, int population)
        {
            int max = Thresholds.Length - 1;
            int t = MathUtil.Clamp((int)current, 0, max);
            int start = t;
            population = Math.Max(0, population);
            while (t < max && population >= Thresholds[t + 1]) t++;
            if (t == start)
                while (t > 0 && population * 5 < Thresholds[t] * 4) t--; // population < 80% of threshold, exact integer math
            return (SettlementTier)t;
        }

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(0, ctx.DeltaTicks);
            long run = ctx.State.Tick / Math.Max(1, TickInterval);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                int pop = CountAlive(ctx.State, s);

                if (pop == 0)
                {
                    float empty = Math.Max(0f, MathUtil.Sanitize(s.Mem(EmptyTicksKey), 0f)) + dt;
                    if (empty >= AbandonAfterTicks)
                    {
                        s.Memory.Remove(EmptyTicksKey);
                        ctx.Ops.DestroySettlement(s, DestroyCause.Abandoned);
                        continue;
                    }
                    s.Memory[EmptyTicksKey] = empty;
                }
                else s.Memory.Remove(EmptyTicksKey);

                UpdateTier(ctx, s, pop);
                UpdateTerritory(ctx, s, run);
                EnsureTownCenter(ctx, s, pop);
            }
        }

        private static int CountAlive(WorldState state, Settlement s)
        {
            int n = 0;
            var ids = s.ResidentIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var p = state.People.Get(ids[k]);
                if (p != null && p.IsAlive) n++;
            }
            return n;
        }

        private static void UpdateTier(SimContext ctx, Settlement s, int pop)
        {
            var old = s.Tier;
            var next = EvaluateTier(old, pop);
            if (next == old) return;
            s.Tier = next;
            ctx.Events?.Publish(new SettlementTierChangedEvent { SettlementId = s.Id, OldTier = old, NewTier = next });
            s.LogAi((next > old ? "Grew from " : "Shrank from ") + old + " to " + next + " (" + pop + " people)");
            if (next > old && next >= SettlementTier.Town)
                ctx.Ops.AddChronicle(ChronicleCategory.Settlement, s.Name + " has grown into a " + next + ".",
                    s.CivilizationId, s.Id, s.Center, next >= SettlementTier.City ? 2 : 1);
        }

        private static void UpdateTerritory(SimContext ctx, Settlement s, long run)
        {
            int target = TerritoryRadiusFor(s.Tier);
            if (target > s.TerritoryRadius) ctx.Ops.ClaimTerritory(s, target);
            else if (ConstructionShared.Mod(run + s.Id, ReclaimPeriodRuns) == 0) ctx.Ops.ClaimTerritory(s, s.TerritoryRadius);
        }

        private static void EnsureTownCenter(SimContext ctx, Settlement s, int pop)
        {
            var state = ctx.State;
            if (ConstructionShared.HasValidTownCenter(state, s)) return;
            s.TownCenterId = -1;
            if (pop == 0) return;

            bool underConstruction = false;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b == null || b.IsDestroyed || b.Type != BuildingType.TownCenter || b.SettlementId != s.Id) continue;
                if (b.IsComplete) { s.TownCenterId = b.Id; return; } // adopt an existing seat of power
                underConstruction = true;
            }
            if (underConstruction || !ctx.Db.TryGetBuilding(BuildingType.TownCenter, out _)) return;
            var civ = ConstructionShared.CivOf(state, s);
            if (civ != null && !ctx.Db.IsBuildingAvailable(civ, BuildingType.TownCenter)) return;

            var queue = s.BuildQueue;
            for (int k = 0; k < queue.Count; k++)
                if (queue[k] != null && queue[k].Type == BuildingType.TownCenter) return;

            queue.Add(new BuildOrder
            {
                Type = BuildingType.TownCenter,
                Priority = TownCenterOrderPriority,
                RequestedTick = ctx.Tick,
                Reason = TownCenterOrderReason,
            });
            s.LogAi("Queued a Town Center: " + TownCenterOrderReason);
        }
    }
}
