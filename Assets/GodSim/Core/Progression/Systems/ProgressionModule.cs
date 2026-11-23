using System.Collections.Generic;

namespace GodSim.Core.Progression
{
    /// <summary>
    /// Progression entry point used by Composition (Engineering Contract §6):
    /// <c>engine.AddSystems(ProgressionModule.CreateSystems())</c>.
    /// Content (ages, techs) is registered separately through <c>AgeCatalog.Register</c> and <c>TechCatalog.Register</c>
    /// (Progression/Content). These systems work with any valid <see cref="GameDatabase"/> content.
    /// </summary>
    public static class ProgressionModule
    {
        /// <summary>
        /// Creates fresh instances of the progression systems in their required order:
        /// <see cref="ModifierSystem"/>, then <see cref="ResearchSystem"/>, then <see cref="AgeProgressionSystem"/>.
        /// Modifiers come first, so research uses the current Modifiers.Research and the refreshed civ population.
        /// Research comes before age advancement, so techs learned this tick count toward the next age.
        /// Each call returns new instances, so engines never share system state.
        /// </summary>
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[] { new ModifierSystem(), new ResearchSystem(), new AgeProgressionSystem() };
        }
    }
}
