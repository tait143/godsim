using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    /// <summary>Read-only queries over storytelling state (for UI, AI flavour and other modules via Composition).</summary>
    public static class StoryQueries
    {
        /// <summary>Window used by <see cref="RecentEventKeys(WorldState, int)"/>.</summary>
        public const float DefaultRecentYears = 10f;

        /// <summary>Keys of events that involved the civ in the last <see cref="DefaultRecentYears"/> years, newest first.</summary>
        public static IEnumerable<string> RecentEventKeys(WorldState s, int civId) => RecentEventKeys(s, civId, DefaultRecentYears);

        /// <summary>Keys of events that involved the civ within the given number of years, newest first (ties in deck order).</summary>
        public static IEnumerable<string> RecentEventKeys(WorldState s, int civId, float withinYears)
        {
            var keys = new List<string>();
            var civ = s != null ? s.Civilizations.Get(civId) : null;
            if (civ == null) return keys;
            long window = SimTime.YearsToTicks(MathUtil.Clamp(MathUtil.Sanitize(withinYears), 0f, 100000f));
            var ticks = new List<long>();
            var all = WorldEventCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                long t = LastFiredTick(civ, all[i]);
                if (t < 0 || t > s.Tick || s.Tick - t > window) continue;
                int at = ticks.Count;
                while (at > 0 && ticks[at - 1] < t) at--;
                ticks.Insert(at, t);
                keys.Insert(at, all[i].Key);
            }
            return keys;
        }

        /// <summary>Tick the event last involved the civ, or -1.</summary>
        public static long LastFiredTick(WorldState s, int civId, string key)
        {
            var civ = s != null ? s.Civilizations.Get(civId) : null;
            return LastFiredTick(civ, WorldEventCatalog.Find(key));
        }

        /// <summary>True if the event cannot hit the civ yet because of its cooldown.</summary>
        public static bool IsOnCooldown(WorldState s, int civId, string key)
        {
            var civ = s != null ? s.Civilizations.Get(civId) : null;
            return WorldEventSystem.IsOnCooldown(s, civ, WorldEventCatalog.Find(key));
        }

        /// <summary>Total world events fired in this world (persisted in WorldState.Memory).</summary>
        public static int TotalEvents(WorldState s)
        {
            if (s == null || !s.Memory.TryGetValue(WorldEventSystem.TotalEventsKey, out float v)) return 0;
            return (int)MathUtil.Clamp(MathUtil.Sanitize(v), 0f, 1e7f);
        }

        private static long LastFiredTick(Civilization civ, WorldEventDefinition def)
        {
            if (civ == null || def == null || !civ.Memory.TryGetValue(def.CooldownMemoryKey, out float v)) return -1;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return -1;
            return (long)v;
        }
    }
}
