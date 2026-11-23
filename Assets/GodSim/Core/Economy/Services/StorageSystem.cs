namespace GodSim.Core.Economy
{
    /// <summary>
    /// Recomputes every living settlement's stockpile capacity from its storage buildings, spoils part of
    /// the food held above <see cref="SpoilageThreshold"/> of capacity, then discards anything above capacity.
    /// <para>capacity = (150 + Σ complete buildings def.StorageCapacity × Condition) × civ.Modifiers.Storage.</para>
    /// </summary>
    public sealed class StorageSystem : ISimSystem
    {
        /// <summary>Capacity every settlement has without storage buildings.</summary>
        public const float BaseCapacity = Stockpile.DefaultCapacity;
        /// <summary>Fraction of the food above the threshold that spoils per tick (0.15%).</summary>
        public const float SpoilageRatePerTick = 0.0015f;
        /// <summary>Fraction of capacity below which food never spoils.</summary>
        public const float SpoilageThreshold = 0.6f;

        /// <inheritdoc />
        public string Name => "Economy.Storage";

        /// <inheritdoc />
        public int TickInterval => 12;

        /// <summary>Sets capacities immediately on a world that has not ticked yet, so other systems see correct storage before the first run.</summary>
        public void Initialize(SimContext ctx)
        {
            if (!EconomyServicesShared.IsUsable(ctx)) return;
            // Capacity is persisted: a loaded world keeps the value of the last Storage run, exactly like an uninterrupted run.
            if (ctx.State.Tick != 0) return;
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                s.Stockpile.Capacity = ComputeCapacity(ctx.State, ctx.Db, s);
            }
        }

        /// <summary>Updates capacity, applies food spoilage (scaled by DeltaTicks) and clamps to capacity.</summary>
        public void Tick(SimContext ctx)
        {
            if (!EconomyServicesShared.IsUsable(ctx)) return;
            int dt = EconomyServicesShared.Delta(ctx, TickInterval);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                var sp = s.Stockpile;
                float capacity = ComputeCapacity(ctx.State, ctx.Db, s);
                sp.Capacity = capacity;

                float spoil = ComputeSpoilage(sp.Get(ResourceType.Food), capacity, dt);
                float spoiled = spoil > 0f ? sp.ConsumeUpTo(ResourceType.Food, spoil) : 0f;
                float overflow = sp.ClampToCapacity();

                s.Memory[EconomyServiceKeys.FoodSpoiled] = MathUtil.Sanitize(spoiled);
                s.Memory[EconomyServiceKeys.StorageOverflow] = MathUtil.Sanitize(overflow);
            }
        }

        /// <summary>
        /// Capacity of a settlement: (base + Σ def.StorageCapacity × Condition over complete, non-destroyed
        /// buildings) × the owning civ's Storage modifier. Never NaN and never below 1.
        /// </summary>
        public static float ComputeCapacity(WorldState state, GameDatabase db, Settlement s)
        {
            if (state == null || db == null || s == null) return BaseCapacity;
            float sum = BaseCapacity;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = EconomyServicesShared.ActiveBuilding(state, db, s, ids[k], out var def);
                if (b == null) continue;
                float add = EconomyServicesShared.NonNegative(def.StorageCapacity);
                if (add <= 0f) continue;
                sum += add * EconomyServicesShared.Condition01(b);
            }
            float mod = EconomyServicesShared.Multiplier(EconomyServicesShared.ModifiersOf(state, s).Storage);
            float capacity = MathUtil.Sanitize(sum * mod, BaseCapacity);
            return capacity < EconomyServicesShared.MinCapacity ? EconomyServicesShared.MinCapacity : capacity;
        }

        /// <summary>
        /// Food that spoils over <paramref name="deltaTicks"/>: 0.15%/tick of the amount above 60% of capacity,
        /// never more than that excess.
        /// </summary>
        public static float ComputeSpoilage(float food, float capacity, int deltaTicks)
        {
            if (deltaTicks <= 0) return 0f;
            float excess = MathUtil.Sanitize(food) - SpoilageThreshold * MathUtil.Sanitize(capacity);
            if (!(excess > 0f)) return 0f;
            float spoil = excess * SpoilageRatePerTick * deltaTicks;
            return spoil > excess ? excess : spoil;
        }
    }
}
