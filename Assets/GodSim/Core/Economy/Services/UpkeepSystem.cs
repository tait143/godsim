namespace GodSim.Core.Economy
{
    /// <summary>
    /// Charges gold upkeep for every complete building (def.UpkeepGold per tick × DeltaTicks). When the
    /// treasury cannot cover the bill, the unpaid share is published as
    /// <c>Settlement.Memory["econ.upkeepShortfall"]</c> (0..1) and every building with upkeep decays by
    /// 0.01 × shortfall Condition per run (scaled by DeltaTicks / 24).
    /// </summary>
    public sealed class UpkeepSystem : ISimSystem
    {
        /// <summary>Condition lost per (24-tick) run by buildings with upkeep at a 100% shortfall.</summary>
        public const float ConditionLossPerRun = 0.01f;
        /// <summary>Reference run length the condition loss is expressed for.</summary>
        public const int ReferenceInterval = 24;

        /// <inheritdoc />
        public string Name => "Economy.Upkeep";

        /// <inheritdoc />
        public int TickInterval => 24;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <summary>Pays upkeep for every living settlement and applies decay on shortfall.</summary>
        public void Tick(SimContext ctx)
        {
            if (!EconomyServicesShared.IsUsable(ctx)) return;
            int dt = EconomyServicesShared.Delta(ctx, TickInterval);
            var state = ctx.State;
            var db = ctx.Db;
            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                PayUpkeep(state, db, s, dt);
            }
        }

        /// <summary>Total gold upkeep per tick of a settlement's complete, non-destroyed buildings.</summary>
        public static float UpkeepPerTick(WorldState state, GameDatabase db, Settlement s)
        {
            if (state == null || db == null || s == null) return 0f;
            float total = 0f;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = EconomyServicesShared.ActiveBuilding(state, db, s, ids[k], out var def);
                if (b == null) continue;
                total += EconomyServicesShared.NonNegative(def.UpkeepGold);
            }
            return EconomyServicesShared.NonNegative(total);
        }

        /// <summary>Pays one run of upkeep for a settlement; returns the shortfall ratio (0..1).</summary>
        public static float PayUpkeep(WorldState state, GameDatabase db, Settlement s, int deltaTicks)
        {
            if (state == null || db == null || s == null || deltaTicks <= 0) return 0f;
            float due = EconomyServicesShared.NonNegative(UpkeepPerTick(state, db, s) * deltaTicks);
            if (!(due > 0f))
            {
                s.Memory[EconomyServiceKeys.UpkeepShortfall] = 0f;
                s.Memory[EconomyServiceKeys.UpkeepDue] = 0f;
                s.Memory[EconomyServiceKeys.UpkeepPaid] = 0f;
                return 0f;
            }

            float paid = s.Stockpile.ConsumeUpTo(ResourceType.Gold, due);
            float ratio = MathUtil.Clamp01(MathUtil.Sanitize((due - paid) / due));
            if (ratio < 1e-6f) ratio = 0f;
            s.Memory[EconomyServiceKeys.UpkeepShortfall] = ratio;
            s.Memory[EconomyServiceKeys.UpkeepDue] = due;
            s.Memory[EconomyServiceKeys.UpkeepPaid] = MathUtil.Sanitize(paid);

            if (ratio > 0f)
            {
                float loss = ConditionLossPerRun * ratio * (deltaTicks / (float)ReferenceInterval);
                var ids = s.BuildingIds;
                for (int k = 0; k < ids.Count; k++)
                {
                    var b = EconomyServicesShared.ActiveBuilding(state, db, s, ids[k], out var def);
                    if (b == null || !(EconomyServicesShared.NonNegative(def.UpkeepGold) > 0f)) continue;
                    float c = EconomyServicesShared.Condition01(b) - loss;
                    b.Condition = c < 0f ? 0f : c;
                }
            }
            return ratio;
        }
    }
}
