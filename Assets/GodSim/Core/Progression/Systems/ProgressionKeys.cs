namespace GodSim.Core.Progression
{
    /// <summary>
    /// Namespaced blackboard keys (<see cref="Civilization.Memory"/>) written or read by the progression systems.
    /// Any module may set <see cref="Dirty"/> to a value &gt; 0 to force an immediate modifier recompute.
    /// </summary>
    public static class ProgressionKeys
    {
        /// <summary>
        /// Set &gt; 0 to request a <see cref="ModifierSystem"/> recompute on its next tick (out of its 12-tick schedule).
        /// The next <see cref="ModifierSystem.Tick"/> consumes and removes it. NaN also counts as a request, and other
        /// non-positive values are simply removed. The research and age systems set it after a learn or an advance as a
        /// signal, but they also recompute modifiers immediately, so it never means stale modifiers for those changes.
        /// A module that creates a civilization or changes its techs, age or race after the Progression phase of a tick
        /// should set it, so a world saved at that tick boundary and reloaded matches an uninterrupted run.
        /// </summary>
        public const string Dirty = "prog.dirty";

        /// <summary>0..1 estimate of progress toward the next age (minimum over all criteria). Written by <see cref="AgeProgressionSystem"/>.</summary>
        public const string AgeProgress = "prog.ageProgress";

        /// <summary>
        /// Canonical record of the tech the research system is pursuing. It is the AI's
        /// <see cref="Civilization.CurrentResearchTechId"/> when that is valid, otherwise the deterministic fallback (the
        /// cheapest researchable tech, ties broken by lowest id), and -1 when nothing is researchable. Refreshed on every
        /// research run (every 4 ticks); <see cref="TechQueries.EffectiveResearchTarget"/> gives the live value.
        /// <para><c>CurrentResearchTechId</c> holds only the AI's explicit choice. The research system never writes the
        /// fallback there, because the AI keeps any valid choice and would then never choose. It resets the field to -1
        /// after each learn and whenever the choice is invalid. UI and saves should read this key, not the field, to show
        /// what is being researched.</para>
        /// </summary>
        public const string ResearchTarget = "prog.researchTarget";

        /// <summary>Research points per tick measured on the last research run (informational, for UI/AI).</summary>
        public const string ResearchPerTick = "prog.researchPerTick";
    }
}
