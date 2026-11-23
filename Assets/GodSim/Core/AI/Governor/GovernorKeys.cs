namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys read and written by the settlement governor. Written keys live in
    /// <see cref="Settlement.Memory"/> (namespaced <c>ai.</c>); read keys come from other modules
    /// (population, economy, civ strategy). Territory cache keys are in <see cref="TerritoryScanner"/>.
    /// </summary>
    public static class GovernorKeys
    {
        // ------------------------------------------------------------------ read (other modules)
        /// <summary>Settlement, written by population: free housing in complete dwellings.</summary>
        public const string FreeHousing = "pop.freeHousing";
        /// <summary>Settlement, written by economy: days of food left at current consumption.</summary>
        public const string FoodDays = "econ.foodDays";
        /// <summary>Civilization, written by strategy: 0 peaceful, 1 defensive, 2 militarist.</summary>
        public const string Posture = "ai.posture";
        /// <summary>Civilization, written by strategy: development focus (0 growth, 1 research, 2 faith, 3 military, 4 wealth).</summary>
        public const string Focus = "ai.focus";
        /// <summary>Civilization, written by strategy: perceived external threat 0..1.</summary>
        public const string CivThreat = "ai.threat";

        // ------------------------------------------------------------------ written (settlement)
        /// <summary>Housing need 0..1 at the last run.</summary>
        public const string NeedHousing = "ai.need.housing";
        /// <summary>Food need 0..1 at the last run.</summary>
        public const string NeedFood = "ai.need.food";
        /// <summary>Defense need 0..1 at the last run.</summary>
        public const string NeedDefense = "ai.need.defense";
        /// <summary>Storage need 0..1 at the last run.</summary>
        public const string NeedStorage = "ai.need.storage";
        /// <summary>Threat level 0..1 used by the governor at the last run.</summary>
        public const string Threat = "ai.gov.threat";
        /// <summary>Smoothed population growth (people per year).</summary>
        public const string Growth = "ai.gov.growth";
        /// <summary>Population at the last growth sample.</summary>
        public const string GrowthPop = "ai.gov.popPrev";
        /// <summary>Tick of the last growth sample.</summary>
        public const string GrowthTick = "ai.gov.popTick";
        /// <summary>Tick the governor last ordered a building for its storage capacity.</summary>
        public const string StorageOrderTick = "ai.gov.storeTick";
        /// <summary>Stockpile capacity when the governor last ordered storage (to verify it actually grew).</summary>
        public const string StorageOrderCapacity = "ai.gov.storeCap";
        /// <summary>Building count at the last progress check (stall detection).</summary>
        public const string ProgressCount = "ai.gov.progress";
        /// <summary>Tick the building count last changed (stall detection).</summary>
        public const string ProgressTick = "ai.gov.progressTick";
        /// <summary>Tick of the last order the governor queued.</summary>
        public const string LastOrderTick = "ai.gov.lastOrder";

        /// <summary>Per type: tick the governor last ordered it ("ai.gov.last.&lt;Type&gt;").</summary>
        public static readonly string[] LastOrderedByType = BuildKeys("ai.gov.last.");
        /// <summary>Per type: tick until which the type is not re-ordered after repeated placement failures.</summary>
        public static readonly string[] BlockedUntilByType = BuildKeys("ai.gov.block.");

        /// <summary>Prefix of <see cref="BuildOrder.Reason"/> for orders created by the governor.</summary>
        public const string ReasonPrefix = "gov: ";

        private static string[] BuildKeys(string prefix)
        {
            var keys = new string[Counts.Buildings];
            for (int i = 0; i < keys.Length; i++) keys[i] = prefix + ((BuildingType)i).ToString();
            return keys;
        }
    }
}
