namespace GodSim.Core.Progression
{
    /// <summary>Per-age statistics of the technology tree, produced by <see cref="TechTreeReport"/>.</summary>
    public sealed class AgeTechSummary
    {
        /// <summary>The age summarized.</summary>
        public AgeId Age;
        /// <summary>All techs of this age (general + racial).</summary>
        public int TechCount;
        /// <summary>Techs every race may research.</summary>
        public int GeneralTechCount;
        /// <summary>Techs restricted to a subset of races.</summary>
        public int RacialTechCount;
        /// <summary>General techs whose prerequisites all belong to earlier ages (entry points of the age).</summary>
        public int RootCount;
        /// <summary>Sum of the research cost of all techs of this age.</summary>
        public float TotalCost;
        /// <summary>Sum of the research cost of the general techs of this age.</summary>
        public float GeneralCost;
        /// <summary>Cheapest tech of this age (0 when the age has no techs).</summary>
        public float MinCost;
        /// <summary>Most expensive tech of this age (0 when the age has no techs).</summary>
        public float MaxCost;
        /// <summary>Research gap the age's techs are balanced against (see <see cref="TechTreeReport.ResearchWindow"/>).</summary>
        public float ResearchWindow;
        /// <summary><see cref="TotalCost"/> divided by <see cref="ResearchWindow"/> (0 when the window is 0).</summary>
        public float CostToWindowRatio;
    }
}
