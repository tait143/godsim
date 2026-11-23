using System;

namespace GodSim.Core.Economy
{
    /// <summary>
    /// Small, allocation-free helpers shared by the economy service systems (storage, services,
    /// upkeep, trade, statistics). Internal so it never collides with types owned by other teams.
    /// </summary>
    internal static class EconomyServicesShared
    {
        /// <summary>Neutral modifiers used when a settlement's civilization cannot be resolved. Never mutated.</summary>
        internal static readonly ModifierSet DefaultModifiers = new ModifierSet();

        /// <summary>Smallest stockpile capacity the economy will ever set (avoids divide-by-zero ratios).</summary>
        internal const float MinCapacity = 1f;

        /// <summary>Resolves the civilization that owns a settlement (null if missing).</summary>
        internal static Civilization CivOf(WorldState state, Settlement s)
        {
            if (state == null || s == null || s.CivilizationId < 0) return null;
            return state.Civilizations.Get(s.CivilizationId);
        }

        /// <summary>The modifier set of the owning civ, or neutral modifiers when the civ is missing.</summary>
        internal static ModifierSet ModifiersOf(WorldState state, Settlement s)
        {
            var civ = CivOf(state, s);
            return civ != null ? civ.Modifiers : DefaultModifiers;
        }

        /// <summary>Sanitizes a multiplier: NaN/Infinity become 1, negatives become 0.</summary>
        internal static float Multiplier(float v)
        {
            float m = MathUtil.Sanitize(v, 1f);
            return m < 0f ? 0f : m;
        }

        /// <summary>Sanitizes a non-negative quantity: NaN/Infinity and negatives become 0.</summary>
        internal static float NonNegative(float v)
        {
            float m = MathUtil.Sanitize(v, 0f);
            return m < 0f ? 0f : m;
        }

        /// <summary>Building condition clamped to 0..1 (NaN counts as ruined).</summary>
        internal static float Condition01(Building b) => MathUtil.Clamp01(MathUtil.Sanitize(b.Condition, 0f));

        /// <summary>Staffing ratio 0..1: workers / slots for job buildings, 1 for buildings without jobs.</summary>
        internal static float Staffing(BuildingDefinition def, Building b)
        {
            if (!def.HasJobs) return 1f;
            return MathUtil.Clamp01(b.Workers.Count / (float)def.JobSlots);
        }

        /// <summary>
        /// Returns the building with the given id if it exists, belongs to the settlement, is complete and is
        /// not destroyed, and outputs its definition; otherwise null.
        /// </summary>
        internal static Building ActiveBuilding(WorldState state, GameDatabase db, Settlement s, int buildingId, out BuildingDefinition def)
        {
            def = null;
            if (buildingId < 0) return null;
            var b = state.Buildings.Get(buildingId);
            if (b == null || b.IsDestroyed || !b.IsComplete || b.SettlementId != s.Id) return null;
            if (!db.TryGetBuilding(b.Type, out def) || def == null) return null;
            return b;
        }

        /// <summary>Stockpile capacity guarded against NaN and non-positive values.</summary>
        internal static float SafeCapacity(Stockpile sp)
        {
            float c = MathUtil.Sanitize(sp.Capacity, Stockpile.DefaultCapacity);
            return c < MinCapacity ? MinCapacity : c;
        }

        /// <summary>Effective delta ticks: ctx.DeltaTicks when positive, otherwise the system's interval.</summary>
        internal static int Delta(SimContext ctx, int fallbackInterval)
        {
            int dt = ctx.DeltaTicks;
            return dt > 0 ? dt : Math.Max(1, fallbackInterval);
        }

        /// <summary>True when the context has the minimum required pieces to run a system.</summary>
        internal static bool IsUsable(SimContext ctx) => ctx != null && ctx.State != null && ctx.Db != null;
    }
}
