using System;

namespace GodSim.Core.Settlements
{
    /// <summary>Small helpers shared by the construction, growth and maintenance systems (module-internal).</summary>
    internal static class ConstructionShared
    {
        /// <summary>The settlement's owning civilization, or null when the id is -1 or dangling.</summary>
        internal static Civilization CivOf(WorldState state, Settlement s)
        {
            if (state == null || s == null || s.CivilizationId < 0) return null;
            return state.Civilizations.Get(s.CivilizationId);
        }

        /// <summary>Footprint tile count (at least 1, clamped against corrupt sizes).</summary>
        internal static int Footprint(Building b)
        {
            if (b == null) return 1;
            return MathUtil.Clamp(b.Width, 1, 16) * MathUtil.Clamp(b.Height, 1, 16);
        }

        /// <summary>True if <see cref="Settlement.TownCenterId"/> points at a live Town Center of this settlement.</summary>
        internal static bool HasValidTownCenter(WorldState state, Settlement s)
        {
            if (state == null || s == null || s.TownCenterId < 0) return false;
            var tc = state.Buildings.Get(s.TownCenterId);
            return tc != null && !tc.IsDestroyed && tc.Type == BuildingType.TownCenter && tc.SettlementId == s.Id;
        }

        /// <summary>Non-negative modulo for stagger phases.</summary>
        internal static long Mod(long value, long period)
        {
            if (period <= 0) return 0;
            long m = value % period;
            return m < 0 ? m + period : m;
        }

        /// <summary>Writes an integer counter to the blackboard, removing the key when it is zero (keeps saves small).</summary>
        internal static void SetOrClear(Settlement s, string key, int value)
        {
            if (value <= 0) s.Memory.Remove(key);
            else s.Memory[key] = value;
        }

        /// <summary>Building display name for logs.</summary>
        internal static string NameOf(BuildingDefinition def, Building b)
        {
            if (def != null && !string.IsNullOrEmpty(def.Name)) return def.Name;
            return b != null ? b.Type.ToString() : "building";
        }

        internal static float SafeCondition(float condition) => MathUtil.Clamp01(MathUtil.Sanitize(condition, 1f));

        internal static int AgeGap(AgeId civAge, AgeId styleAge) => Math.Max(0, (int)civAge - (int)styleAge);
    }
}
