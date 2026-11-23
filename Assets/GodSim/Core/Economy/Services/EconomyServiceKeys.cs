namespace GodSim.Core.Economy
{
    /// <summary>
    /// Blackboard keys written by the economy service systems. Other modules (AI, UI) must read them by
    /// their literal string values because modules never reference each other's types.
    /// </summary>
    public static class EconomyServiceKeys
    {
        /// <summary>Settlement: days of food left at the current average consumption (capped at 9999).</summary>
        public const string FoodDays = "econ.foodDays";
        /// <summary>Settlement: average food income minus average food expense per tick.</summary>
        public const string NetFood = "econ.netFood";
        /// <summary>Settlement: share (0..1) of the last upkeep bill that could not be paid.</summary>
        public const string UpkeepShortfall = "econ.upkeepShortfall";
        /// <summary>Settlement: gold owed for upkeep in the last upkeep run.</summary>
        public const string UpkeepDue = "econ.upkeepDue";
        /// <summary>Settlement: gold actually paid for upkeep in the last upkeep run.</summary>
        public const string UpkeepPaid = "econ.upkeepPaid";
        /// <summary>Settlement: food lost to spoilage in the last storage run.</summary>
        public const string FoodSpoiled = "econ.foodSpoiled";
        /// <summary>Settlement: resources discarded in the last storage run because they exceeded capacity.</summary>
        public const string StorageOverflow = "econ.storageOverflow";
        /// <summary>Settlement: gold produced by commerce in the last service run.</summary>
        public const string CommerceGold = "econ.commerceGold";
        /// <summary>Settlement: units received through trade (intra- and inter-civ) in the last trade run.</summary>
        public const string TradeImported = "econ.tradeIn";
        /// <summary>Settlement: units sent through trade (intra- and inter-civ) in the last trade run.</summary>
        public const string TradeExported = "econ.tradeOut";
        /// <summary>Civilization: gold earned from inter-civ trade in the last trade run.</summary>
        public const string TradeIncome = "econ.tradeIncome";
        /// <summary>WorldState: prefix of the "first trade chronicled" flag, completed as "trade.first.&lt;lo&gt;.&lt;hi&gt;".</summary>
        public const string FirstTradePrefix = "trade.first.";

        /// <summary>Builds the WorldState.Memory key that marks the first trade between two civs as chronicled.</summary>
        public static string FirstTradeKey(int civA, int civB)
        {
            int lo = civA < civB ? civA : civB;
            int hi = civA < civB ? civB : civA;
            return FirstTradePrefix + lo + "." + hi;
        }
    }
}
