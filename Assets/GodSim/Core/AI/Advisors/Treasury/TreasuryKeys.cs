namespace GodSim.Core.AI
{
    /// <summary>Treasury mood of a settlement, published as <see cref="TreasuryKeys.Mode"/> (float of the enum value).</summary>
    public enum TreasuryMode : byte
    {
        /// <summary>Income roughly matches obligations; invest calmly.</summary>
        Balanced = 0,
        /// <summary>A deficit is visible on the horizon (often from buildings still being built); start hedging.</summary>
        Caution = 1,
        /// <summary>Reserves are draining or upkeep goes unpaid; push revenue, cut luxuries.</summary>
        Austerity = 2,
        /// <summary>Large reserves and a healthy surplus; patronage of culture is affordable.</summary>
        Prosperity = 3
    }

    /// <summary>Memory keys read and written by the <see cref="TreasuryAdvisor"/> (all floats, namespaced "ai.treasury.").</summary>
    public static class TreasuryKeys
    {
        /// <summary>Economy-published unpaid upkeep share 0..1 (read only; written by Economy.Upkeep).</summary>
        public const string EconUpkeepShortfall = "econ.upkeepShortfall";

        /// <summary>Settlement: years until the gold stock is exhausted at the current drain (99 = no drain).</summary>
        public const string Runway = "ai.treasury.runway";
        /// <summary>Settlement: years of projected upkeep the stock covers with zero income (99 = no upkeep).</summary>
        public const string CoverYears = "ai.treasury.cover";
        /// <summary>Settlement: financial stress 0..1.</summary>
        public const string Stress = "ai.treasury.stress";
        /// <summary>Settlement: wealth 0..1.</summary>
        public const string Wealth = "ai.treasury.wealth";
        /// <summary>Settlement: <see cref="TreasuryMode"/> as float.</summary>
        public const string Mode = "ai.treasury.mode";
        /// <summary>Settlement: gross gold income per tick the advisor believes in.</summary>
        public const string Income = "ai.treasury.income";
        /// <summary>Settlement: upkeep per tick of complete buildings.</summary>
        public const string Upkeep = "ai.treasury.upkeep";
        /// <summary>Settlement: upkeep per tick of buildings under construction or queued (future burden).</summary>
        public const string PendingUpkeep = "ai.treasury.pendingUpkeep";
        /// <summary>Settlement: gold stock at the previous run (trend detection).</summary>
        public const string LastGold = "ai.treasury.lastGold";
        /// <summary>Civilization: id of the richest settlement (banking seat).</summary>
        public const string Richest = "ai.treasury.richest";
    }
}
