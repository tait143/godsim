namespace GodSim.Core.Progression
{
    /// <summary>
    /// Snapshot of a civilization's standing against the requirements of its next age.
    /// Produced by <see cref="TechQueries.EvaluateNextAge"/>; used by <see cref="AgeProgressionSystem"/> and the UI.
    /// </summary>
    public struct AgeAdvanceCheck
    {
        /// <summary>False when the civ is at the final age or the next age is not registered.</summary>
        public bool HasNextAge;
        /// <summary>The civilization's current age.</summary>
        public AgeId CurrentAge;
        /// <summary>The age being checked (only meaningful when <see cref="HasNextAge"/>).</summary>
        public AgeId NextAge;

        /// <summary>Lifetime research of the civ.</summary>
        public float TotalResearch;
        /// <summary>Cumulative research needed to enter <see cref="NextAge"/>.</summary>
        public float ResearchThreshold;
        /// <summary>0..1 progress from the current age threshold to the next one.</summary>
        public float ResearchProgress;

        /// <summary>Techs of the current age the civ knows.</summary>
        public int TechsKnown;
        /// <summary>Techs of the current age required, capped at those the civ knows or can still obtain (<see cref="TechQueries.TechsAvailableToCiv"/>).</summary>
        public int TechsRequired;

        /// <summary>Living civ population (sum of alive settlement residents).</summary>
        public int Population;
        /// <summary>Population required by the next age.</summary>
        public int MinPopulation;

        /// <summary>Alive settlements owned by the civ.</summary>
        public int Settlements;
        /// <summary>Settlements required by the next age.</summary>
        public int MinSettlements;

        /// <summary>0..1 overall progress estimate (minimum over all criteria). 1 at the final age.</summary>
        public float Progress;

        /// <summary>True if the research threshold is met.</summary>
        public bool ResearchMet => TotalResearch >= ResearchThreshold;
        /// <summary>True if enough techs of the current age are known.</summary>
        public bool TechsMet => TechsKnown >= TechsRequired;
        /// <summary>True if the population requirement is met.</summary>
        public bool PopulationMet => Population >= MinPopulation;
        /// <summary>True if the settlement requirement is met.</summary>
        public bool SettlementsMet => Settlements >= MinSettlements;
        /// <summary>True if the civ may advance now.</summary>
        public bool AllMet => HasNextAge && ResearchMet && TechsMet && PopulationMet && SettlementsMet;
    }
}
