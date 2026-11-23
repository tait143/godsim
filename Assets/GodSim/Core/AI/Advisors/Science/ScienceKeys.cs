namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys and tuning constants of the <see cref="ScienceAdvisor"/>. Keys it writes live in
    /// <c>Civilization.Memory</c> under the "adv.sci." prefix; keys it reads belong to other modules and are
    /// duplicated here as string constants so the advisor stays decoupled from their types.
    /// </summary>
    public static class ScienceKeys
    {
        // ------------------------------------------------------------------ civ memory (written)
        /// <summary>Civ: EMA of the estimated research rate (points per tick).</summary>
        public const string Rate = "adv.sci.rate";
        /// <summary>Civ: estimated years until the next age's research and tech requirements are met (capped at <see cref="MaxYears"/>).</summary>
        public const string YearsToNextAge = "adv.sci.years";
        /// <summary>Civ: pace pressure = years needed / years left in the pacing budget (&gt;1 = behind pace).</summary>
        public const string Pressure = "adv.sci.pressure";
        /// <summary>Civ: techs of the current age still required by the next age.</summary>
        public const string TechsLeft = "adv.sci.techsLeft";
        /// <summary>Civ: research hint for the ResearchDirector (cheapest researchable current-age tech when age techs are missing, else -1).
        /// Never written while <c>civ.CurrentResearchTechId == -1</c>. The advisor never writes CurrentResearchTechId itself.</summary>
        public const string PreferredTech = "adv.sci.prefTech";
        /// <summary>Civ: last announced pace band (see <see cref="SciencePacePlan.Band"/>), used to log only on change.</summary>
        public const string Band = "adv.sci.band";
        /// <summary>Civ: last tech id hinted in the AI log (avoids log spam).</summary>
        public const string HintLogged = "adv.sci.hintLogged";
        /// <summary>Settlement: 1 while the advisor is backing off because of a local crisis (log on change).</summary>
        public const string CrisisFlag = "adv.sci.crisis";

        // ------------------------------------------------------------------ read (owned by other modules)
        /// <summary>Urgency id published by the food advisor.</summary>
        public const string FoodAdvisorId = "food";
        /// <summary>Urgency id published by the defense advisor.</summary>
        public const string DefenseAdvisorId = "defense";
        /// <summary>Settlement: free housing slots (population module).</summary>
        public const string FreeHousing = "pop.freeHousing";
        /// <summary>Civ: development focus (strategy module); 1 = research.</summary>
        public const string Focus = "ai.focus";
        /// <summary>Value of <see cref="Focus"/> meaning "research".</summary>
        public const int FocusResearch = 1;
        /// <summary>Civ: number of active wars (strategy module).</summary>
        public const string ActiveWars = "ai.wars";

        // ------------------------------------------------------------------ tuning
        /// <summary>Pacing target per age for a civ of average curiosity.</summary>
        public const float BaseYearsPerAge = 100f;
        /// <summary>Research points per person per tick assumed on top of building output.</summary>
        public const float ResearchPerPersonPerTick = 0.004f;
        /// <summary>EMA smoothing of the research rate per run.</summary>
        public const float RateAlpha = 0.3f;
        /// <summary>Faster EMA smoothing when the rate collapses (disaster), so the plan reacts quickly.</summary>
        public const float RateAlphaFast = 0.6f;
        /// <summary>Cap for year estimates.</summary>
        public const float MaxYears = 999f;
    }
}
