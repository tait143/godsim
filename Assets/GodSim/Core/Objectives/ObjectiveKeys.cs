namespace GodSim.Core.Objectives
{
    /// <summary>
    /// Every WorldState.Memory / Civilization.Memory key written by the objectives module. Keys are namespaced
    /// ("obj." and "game.") so they persist in saves without contract changes. Values are floats.
    /// </summary>
    public static class ObjectiveKeys
    {
        /// <summary>Civilization.Memory flag (1) on civilizations created by the god's SpawnTribe.</summary>
        public const string SpawnedByGod = "obj.spawnedByGod";

        /// <summary>WorldState.Memory flag (1) set once an Ascension Gate is completed (endgame victory).</summary>
        public const string Ascended = "game.ascended";
        /// <summary>Tick of the ascension.</summary>
        public const string AscendedTick = "game.ascendedTick";
        /// <summary>Id of the civilization that ascended (-1 unknown).</summary>
        public const string AscendedCivilization = "game.ascendedCiv";

        public const string CountCasts = "obj.count.casts";
        public const string CountBattles = "obj.count.battles";
        public const string CountWars = "obj.count.wars";
        public const string CountBuildings = "obj.count.buildings";
        public const string CountWonders = "obj.count.wonders";
        public const string CountCivilizations = "obj.count.civs";
        public const string CountCollapses = "obj.count.collapses";
        public const string CountCaptures = "obj.count.captures";
        public const string CountDisasters = "obj.count.disasters";
        public const string CountAgeAdvances = "obj.count.ages";
        public const string CountPhoenix = "obj.count.phoenix";
        public const string CountSpawned = "obj.count.spawned";

        /// <summary>Highest settlement tier ever reached anywhere (sticky).</summary>
        public const string BestTier = "obj.best.tier";
        /// <summary>Highest age ever reached by any civilization (sticky).</summary>
        public const string BestAge = "obj.best.age";
        /// <summary>Last tick at which a war was active (or fewer than two civilizations lived).</summary>
        public const string PeaceLastWarTick = "obj.peace.lastWarTick";

        public const string MeteorTick = "obj.phoenix.meteorTick";
        public const string MeteorX = "obj.phoenix.meteorX";
        public const string MeteorY = "obj.phoenix.meteorY";
        public const string MeteorRadius = "obj.phoenix.meteorR";
        /// <summary>Tick a settlement fell to the god's meteor (-1 / absent = no pending site).</summary>
        public const string PhoenixSiteTick = "obj.phoenix.siteTick";
        public const string PhoenixSiteX = "obj.phoenix.siteX";
        public const string PhoenixSiteY = "obj.phoenix.siteY";

        private static readonly string[] PowerKeys = BuildPowerKeys();
        private static readonly string[] BuiltKeys = BuildBuiltKeys();

        /// <summary>"obj.&lt;key&gt;.progress"</summary>
        public static string Progress(string key) => "obj." + key + ".progress";
        /// <summary>"obj.&lt;key&gt;.done"</summary>
        public static string Done(string key) => "obj." + key + ".done";
        /// <summary>"obj.&lt;key&gt;.base"</summary>
        public static string Base(string key) => "obj." + key + ".base";
        /// <summary>"obj.&lt;key&gt;.armed"</summary>
        public static string Armed(string key) => "obj." + key + ".armed";

        /// <summary>Successful casts of a power, e.g. "obj.count.rain".</summary>
        public static string PowerCount(GodPowerType power)
        {
            int i = (int)power;
            return i >= 0 && i < PowerKeys.Length ? PowerKeys[i] : null;
        }

        /// <summary>Completions of a building type, e.g. "obj.count.built.ascensiongate".</summary>
        public static string BuiltCount(BuildingType type)
        {
            int i = (int)type;
            return i >= 0 && i < BuiltKeys.Length ? BuiltKeys[i] : null;
        }

        private static string[] BuildPowerKeys()
        {
            var keys = new string[Counts.GodPowers];
            var all = EnumUtil.AllGodPowers;
            for (int i = 0; i < all.Length; i++)
            {
                int idx = (int)all[i];
                if (idx >= 0 && idx < keys.Length) keys[idx] = "obj.count." + all[i].ToString().ToLowerInvariant();
            }
            return keys;
        }

        private static string[] BuildBuiltKeys()
        {
            var keys = new string[Counts.Buildings];
            var all = EnumUtil.AllBuildings;
            for (int i = 0; i < all.Length; i++)
            {
                int idx = (int)all[i];
                if (idx >= 0 && idx < keys.Length) keys[idx] = "obj.count.built." + all[i].ToString().ToLowerInvariant();
            }
            return keys;
        }
    }
}
