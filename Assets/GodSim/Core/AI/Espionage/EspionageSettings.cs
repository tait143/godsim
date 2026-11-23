namespace GodSim.Core.AI
{
    /// <summary>Balancing knobs for <see cref="EspionageSystem"/>. Defaults are the shipped tuning; tests may tighten them.</summary>
    public sealed class EspionageSettings
    {
        // ---- network
        public float MaxNetwork = 100f;
        /// <summary>Yearly growth = (Base + Curiosity * CuriosityGrowth) * tradeFactor * populationFactor.</summary>
        public float BaseGrowthPerYear = 2f;
        public float CuriosityGrowthPerYear = 8f;
        /// <summary>Each trade partner (TradeAgreement) adds this * (0.5 + Mercantilism) to the growth factor.</summary>
        public float TradeContactBonus = 0.25f;
        public int MaxTradeContacts = 4;
        /// <summary>Fraction of the network that withers per year when there is nobody worth spying on.</summary>
        public float IdleDecayPerYear = 0.1f;

        // ---- operations
        public float StealCost = 25f;
        public float SabotageCost = 30f;
        /// <summary>Offense multiplier when the target is a trade partner (merchants make good cover).</summary>
        public float TradeCoverBonus = 0.25f;
        public float MinSuccessChance = 0.05f;
        public float MaxSuccessChance = 0.95f;
        /// <summary>Scales the minimum success chance a civ accepts before risking its agents (0 = reckless).</summary>
        public float RiskAversion = 1f;

        // ---- counter-intelligence (defense points vs network offense)
        public float BaseCounterIntel = 20f;
        public float AdministrationWeight = 40f;
        public float XenophobiaWeight = 40f;
        public float GuardWeight = 50f;
        public float GuardDecayPerYear = 0.35f;
        public float GuardOnTheft = 0.1f;
        public float GuardOnSabotage = 0.2f;
        public float GuardOnExposure = 0.3f;
        public float GuardOnFailedAttempt = 0.05f;

        // ---- exposure & fallout
        public float ExposureBase = 0.45f;
        public float ExposureMin = 0.15f;
        public float ExposureMax = 0.9f;
        public float OpinionPenaltyMin = 20f;
        public float OpinionPenaltyMax = 40f;
        /// <summary>Fraction of the spy network lost when a ring is rolled up.</summary>
        public float NetworkLossOnExposure = 0.5f;
        /// <summary>After being exposed by a civ, spies avoid it for this long.</summary>
        public int LayLowTicks = 3 * SimTime.TicksPerYear;

        // ---- sabotage
        public float SabotageMinAggression = 0.5f;
        public float SabotageMinAggressionAtWar = 0.3f;
        public float SabotageDamageMin = 0.3f;
        public float SabotageDamageMax = 0.6f;
        /// <summary>Sabotage cripples but never razes a building.</summary>
        public float MinConditionAfterSabotage = 0.1f;
        public float FoodBurnMin = 0.3f;
        public float FoodBurnMax = 0.6f;
        public float MaxFoodBurn = 200f;
    }
}
