using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys and tuning constants of <see cref="DwarfCultureAdvisor"/>. Every key lives under the
    /// "race.dwarf." namespace. Other modules may read the grudge keys (e.g. diplomacy, storytelling).
    /// </summary>
    public static class DwarfCultureKeys
    {
        /// <summary>Civ memory: grudge intensity (0..1) against civ id N lives at "race.dwarf.grudge.N".</summary>
        public const string GrudgePrefix = "race.dwarf.grudge.";
        /// <summary>Civ memory: settlement id chosen to host the DeepForge (capital unless it has no mountain).</summary>
        public const string ForgeSeat = "race.dwarf.forgeSeat";
        /// <summary>Civ memory: DeepForge progress across the civ (0 none, 1 queued, 2 under construction, 3 complete).</summary>
        public const string ForgeState = "race.dwarf.forgeState";
        /// <summary>Civ memory: tick of the last civ-level pass (grudge decay, forge bookkeeping).</summary>
        public const string LastCivPass = "race.dwarf.lastPass";
        /// <summary>Settlement memory: tick of the last earthquake (or wildfire) that shook the hold.</summary>
        public const string QuakeTick = "race.dwarf.quakeTick";
        /// <summary>Settlement memory: severity (0..1) of that disaster.</summary>
        public const string QuakeSeverity = "race.dwarf.quakeSev";

        // ------------------------------------------------------------------ tuning
        /// <summary>Grudges halve every 30 years: a dwarf lifetime (~200 years) remembers several generations of wrongs.</summary>
        public const float GrudgeHalfLifeYears = 30f;
        /// <summary>Below this a grudge is considered settled and forgotten.</summary>
        public const float GrudgeForgetThreshold = 0.03f;
        public const float GrudgeBattleLost = 0.45f;
        public const float GrudgeAttacked = 0.15f;
        public const float GrudgeBuildingLost = 0.2f;
        public const float GrudgeSettlementLost = 1f;
        /// <summary>Offender settlements within this distance (tiles) count as fully "near".</summary>
        public const float GrudgeNearTiles = 20f;
        /// <summary>Beyond this distance an offender is out of mind (unless at war).</summary>
        public const float GrudgeFarTiles = 56f;
        /// <summary>How long (ticks) after a quake the hold keeps rebuilding in stone: three years.</summary>
        public const int RebuildWindowTicks = 3 * SimTime.TicksPerYear;

        private static readonly Dictionary<int, string> s_grudgeKeys = new Dictionary<int, string>();

        /// <summary>Cached "race.dwarf.grudge.{civId}" key (bounded cache; no per-tick allocation once warm).</summary>
        public static string GrudgeKey(int civId)
        {
            lock (s_grudgeKeys) // thread-safe: several simulations may share the process
            {
                if (s_grudgeKeys.TryGetValue(civId, out var k)) return k;
                k = GrudgePrefix + civId;
                if (s_grudgeKeys.Count < 4096) s_grudgeKeys[civId] = k;
                return k;
            }
        }
    }
}
