namespace GodSim.Core.AI
{
    /// <summary>Civilization memory keys written by <see cref="GovernanceSystem"/> (all floats, persisted with the save).</summary>
    public static class GovernanceKeys
    {
        /// <summary>Advisor id used for votes and the learned weight key "ai.w.gov".</summary>
        public const string AdvisorId = "gov";

        /// <summary>Current <see cref="GovernmentType"/> as a float index.</summary>
        public const string Type = "gov.type";
        /// <summary>Tick at which the current government was established.</summary>
        public const string Since = "gov.since";
        /// <summary>Regime stability in [0, 1]; very low values cause revolutions.</summary>
        public const string Stability = "gov.stability";
        /// <summary>Tax rate policy in [0, 1].</summary>
        public const string Tax = "gov.tax";
        /// <summary>Conscription policy in [0, 1].</summary>
        public const string Conscription = "gov.conscription";
        /// <summary>Current <see cref="FaithPolicy"/> as a float index.</summary>
        public const string FaithPolicy = "gov.faithPolicy";

        /// <summary>Last age the governance system saw (detects age advances). Internal.</summary>
        public const string SeenAge = "gov.age";
        /// <summary>Number of revolutions this civ has suffered.</summary>
        public const string Revolutions = "gov.revolutions";
        /// <summary>Number of peaceful reforms.</summary>
        public const string Reforms = "gov.reforms";
        /// <summary>1 while the civ is at war (as last evaluated).</summary>
        public const string AtWar = "gov.atWar";
        /// <summary>Last computed stability target in [0, 1] (UI/debug: where stability is heading).</summary>
        public const string StabilityTarget = "gov.stabilityTarget";
    }
}
