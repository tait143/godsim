using System;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// The mana/faith economy. Every run (interval 6) it:
    /// <list type="bullet">
    /// <item>computes mana regeneration per tick = sum over alive civilizations of
    /// (sum of settlement ServiceOutput[Faith]) x Devotion x 0.02 x Modifiers.Faith + population x Devotion x 0.0004,
    /// times Config.ManaRegenMultiplier, writes it to God.ManaPerTick and adds it x DeltaTicks;</item>
    /// <item>sets MaxMana = 300 + 0.5 x followers (capped at 5000) and clamps Mana to [0, MaxMana];</item>
    /// <item>drifts each civilization's Devotion toward clamp(0.3 + 0.5 x average settlement Faith + 0.2 x GodFavor);</item>
    /// <item>decays GodFavor by 1% toward 0.</item>
    /// </list>
    /// Allocation-free; cost is O(civilizations + settlements).
    /// </summary>
    public sealed class FaithSystem : ISimSystem
    {
        /// <summary>Mana per point of faith service per tick (before devotion).</summary>
        public const float FaithServiceToMana = 0.02f;
        /// <summary>Mana per follower per tick (before devotion).</summary>
        public const float FollowerToMana = 0.0004f;
        /// <summary>Base mana capacity.</summary>
        public const float BaseMaxMana = 300f;
        /// <summary>Additional mana capacity per follower.</summary>
        public const float MaxManaPerFollower = 0.5f;
        /// <summary>Absolute mana capacity cap.</summary>
        public const float MaxManaCap = 5000f;
        /// <summary>Fraction of the gap to the target devotion closed per tick.</summary>
        public const float DevotionDriftPerTick = 0.002f;
        /// <summary>Multiplicative GodFavor decay per run.</summary>
        public const float FavorDecayPerRun = 0.01f;

        /// <inheritdoc />
        public string Name => "Divine.Faith";
        /// <inheritdoc />
        public int TickInterval => 6;

        /// <summary>
        /// Sanitizes Mana (a no-op on valid state). On a world that has not ticked yet (<c>State.Tick == 0</c>) it also
        /// computes MaxMana and ManaPerTick, without adding mana or clamping. On a loaded world both are persisted and
        /// kept as saved, matching an uninterrupted run (which holds the values of the last Faith run until the next one).
        /// </summary>
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var god = ctx.State.God;
            god.Mana = Math.Max(0f, MathUtil.Sanitize(god.Mana));
            if (ctx.State.Tick != 0) return;
            god.ManaPerTick = ComputeRegenPerTick(ctx, out int followers);
            god.MaxMana = MaxManaFor(followers);
        }

        /// <summary>Regenerates mana, updates capacity, drifts devotion and decays favor.</summary>
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var state = ctx.State;
            var god = state.God;
            int dt = Math.Max(1, ctx.DeltaTicks);

            float regen = ComputeRegenPerTick(ctx, out int followers);
            god.ManaPerTick = regen;
            god.MaxMana = MaxManaFor(followers);
            god.Mana = MathUtil.Clamp(MathUtil.Sanitize(god.Mana) + regen * dt, 0f, god.MaxMana);

            float drift = 1f - (float)Math.Pow(1.0 - DevotionDriftPerTick, dt);
            float decay = 1f - FavorDecayPerRun;
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                float favor = MathUtil.Clamp(MathUtil.Sanitize(civ.GodFavor), -1f, 1f);
                float avgFaith = AverageSettlementFaith(state, civ);
                float target = MathUtil.Clamp01(0.3f + 0.5f * avgFaith + 0.2f * favor);
                float devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f));
                civ.Devotion = MathUtil.Clamp01(devotion + (target - devotion) * drift);
                civ.GodFavor = Math.Abs(favor) < 1e-5f ? 0f : favor * decay;
            }
        }

        /// <summary>MaxMana for a follower count: 300 + 0.5 per follower, capped at 5000.</summary>
        public static float MaxManaFor(int followers) => Math.Min(MaxManaCap, BaseMaxMana + MaxManaPerFollower * Math.Max(0, followers));

        /// <summary>
        /// Mana regenerated per tick by all alive civilizations (already multiplied by Config.ManaRegenMultiplier).
        /// <paramref name="followers"/> receives the total population of alive civilizations.
        /// </summary>
        public static float ComputeRegenPerTick(SimContext ctx, out int followers)
        {
            followers = 0;
            if (ctx == null || ctx.State == null) return 0f;
            var state = ctx.State;
            double total = 0.0;
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                float devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f));
                float faithMod = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Faith, 1f), 0f, 10f);
                double faith = 0.0;
                int pop = 0;
                for (int k = 0; k < civ.SettlementIds.Count; k++)
                {
                    var s = state.Settlements.Get(civ.SettlementIds[k]);
                    if (s == null || !s.IsAlive) continue;
                    faith += Math.Max(0f, MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Faith]));
                    pop += s.Population;
                }
                followers += pop;
                total += faith * devotion * FaithServiceToMana * faithMod + pop * devotion * FollowerToMana;
            }
            float mult = ctx.Config != null ? MathUtil.Clamp(MathUtil.Sanitize(ctx.Config.ManaRegenMultiplier, 1f), 0f, 100f) : 1f;
            return MathUtil.Clamp(MathUtil.Sanitize((float)(total * mult)), 0f, MaxManaCap);
        }

        private static float AverageSettlementFaith(WorldState state, Civilization civ)
        {
            float sum = 0f;
            int n = 0;
            for (int k = 0; k < civ.SettlementIds.Count; k++)
            {
                var s = state.Settlements.Get(civ.SettlementIds[k]);
                if (s == null || !s.IsAlive) continue;
                sum += MathUtil.Clamp01(MathUtil.Sanitize(s.Faith, 0.5f));
                n++;
            }
            return n > 0 ? sum / n : 0.5f;
        }
    }
}
