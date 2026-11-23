namespace GodSim.Core.AI
{
    /// <summary>Main cause behind a settlement's discontent, as diagnosed by <see cref="HappinessAdvisor"/>.</summary>
    public enum HappinessCause : byte
    {
        None = 0,
        Housing = 1,
        Food = 2,
        Entertainment = 3,
        Faith = 4,
        War = 5,
        Grief = 6,
        Remoteness = 7
    }

    /// <summary>
    /// Settlement blackboard keys owned by <see cref="HappinessAdvisor"/> (all floats, persisted with the save).
    /// Other modules and the UI may read them; only the happiness advisor writes them.
    /// </summary>
    public static class HappinessAdvisorKeys
    {
        /// <summary>Number of advisor runs observed for the settlement (0 = never seen).</summary>
        public const string Samples = "adv.happy.n";
        /// <summary>Happiness value seen on the previous run.</summary>
        public const string LastHappiness = "adv.happy.lastH";
        /// <summary>Unrest value seen on the previous run.</summary>
        public const string LastUnrest = "adv.happy.lastU";
        /// <summary>Population seen on the previous run.</summary>
        public const string LastPopulation = "adv.happy.lastPop";
        /// <summary>Exponential moving average of happiness.</summary>
        public const string HappinessEma = "adv.happy.hEma";
        /// <summary>Exponential moving average of unrest.</summary>
        public const string UnrestEma = "adv.happy.uEma";
        /// <summary>Smoothed change of happiness per advisor run (negative = souring).</summary>
        public const string HappinessTrend = "adv.happy.hTrend";
        /// <summary>Smoothed change of unrest per advisor run (positive = rising).</summary>
        public const string UnrestTrend = "adv.happy.uTrend";
        /// <summary>Smoothed population change per advisor run (used to anticipate service and housing gaps).</summary>
        public const string PopulationTrend = "adv.happy.popTrend";
        /// <summary>Decaying weighted count of recent deaths (written by the advisor's death listener).</summary>
        public const string RecentDeaths = "adv.happy.deaths";
        /// <summary>Unrest risk 0..1 (forecast-based, amplified by remoteness).</summary>
        public const string Risk = "adv.happy.risk";
        /// <summary>Dominant <see cref="HappinessCause"/> as a float.</summary>
        public const string Cause = "adv.happy.cause";
        /// <summary>Remoteness from the capital 0..1 (0 = capital or nearby).</summary>
        public const string Remote = "adv.happy.remote";
    }
}
