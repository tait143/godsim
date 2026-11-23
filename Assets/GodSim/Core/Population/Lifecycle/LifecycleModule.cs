using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Convenience factory for the population lifecycle systems, for use by
    /// <c>Population.PopulationModule.CreateSystems()</c>.
    /// </summary>
    public static class LifecycleModule
    {
        /// <summary>
        /// New instances of the lifecycle systems in their recommended order:
        /// Needs (eat first), Aging, Families (pair before births), Births.
        /// </summary>
        public static List<ISimSystem> CreateSystems()
        {
            return new List<ISimSystem> { new NeedsSystem(), new AgingSystem(), new FamilySystem(), new BirthSystem() };
        }
    }
}
