using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys written by <see cref="SaurianCultureAdvisor"/> (namespaced <c>race.saurian.</c>).
    /// Other modules may read them (UI, diplomacy, expansion) but only the Saurian advisor writes them.
    /// </summary>
    public static class SaurianCultureKeys
    {
        /// <summary>Civ, per offender: tick the grudge against civ &lt;id&gt; began ("race.saurian.wronged.&lt;civId&gt;").</summary>
        public const string WrongedPrefix = "race.saurian.wronged.";
        /// <summary>Civ, per offender: tick of the most recent wrong by civ &lt;id&gt;.</summary>
        public const string WrongedLastPrefix = "race.saurian.wrongedLast.";
        /// <summary>Civ, per offender: grievance severity 0..1.</summary>
        public const string GrievancePrefix = "race.saurian.grievance.";

        /// <summary>Civ: current vengeance drive 0..1 (max over offenders; ramps up over years).</summary>
        public const string Vengeance = "race.saurian.vengeance";
        /// <summary>Civ: 1 while the capital is stockpiling for an unqueued Sun Pyramid, else 0.</summary>
        public const string PyramidSaving = "race.saurian.pyramidSaving";
        /// <summary>Civ: 1 when a Sun Pyramid is built, under construction or queued anywhere in the civ.</summary>
        public const string PyramidPresent = "race.saurian.pyramidPresent";
        /// <summary>Civ: weight (1..2) the expansion AI may add to water when scoring new town sites.</summary>
        public const string SettleWaterWeight = "race.saurian.settleWaterWeight";
        /// <summary>Civ: 1 during the first year of any grudge (the wrong is sung at the temple), else 0.</summary>
        public const string FreshWound = "race.saurian.freshWound";
        /// <summary>Civ: tick the civ-level cache was last refreshed (internal).</summary>
        public const string CivRefreshTick = "race.saurian.civTick";

        /// <summary>Settlement: water identity 0..1 (water, river and swamp tiles in territory).</summary>
        public const string Water = "race.saurian.water";
        /// <summary>Settlement: chill 0..1 felt by cold-blooded residents (cold land, winter, cold weather).</summary>
        public const string Chill = "race.saurian.chill";
        /// <summary>Settlement: projected hatchlings over the next year (clutch anticipation).</summary>
        public const string ExpectedHatchlings = "race.saurian.hatchlings";

        private static readonly Dictionary<int, string> s_wronged = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> s_wrongedLast = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> s_grievance = new Dictionary<int, string>();

        /// <summary>"race.saurian.wronged.&lt;civId&gt;" (cached, allocation-free after first use).</summary>
        public static string WrongedKey(int civId) => Cached(s_wronged, WrongedPrefix, civId);
        /// <summary>"race.saurian.wrongedLast.&lt;civId&gt;" (cached).</summary>
        public static string WrongedLastKey(int civId) => Cached(s_wrongedLast, WrongedLastPrefix, civId);
        /// <summary>"race.saurian.grievance.&lt;civId&gt;" (cached).</summary>
        public static string GrievanceKey(int civId) => Cached(s_grievance, GrievancePrefix, civId);

        private static string Cached(Dictionary<int, string> cache, string prefix, int id)
        {
            lock (cache) // thread-safe: several simulations may share the process
            {
                if (cache.TryGetValue(id, out var key)) return key;
                key = prefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (cache.Count < 4096) cache[id] = key; // bounded; civ ids are few
                return key;
            }
        }
    }
}
