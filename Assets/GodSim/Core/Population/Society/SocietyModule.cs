using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Convenience factory for the population society systems, in their recommended order
    /// (housing before jobs before wellbeing; migration and leadership last). The population module's
    /// <c>CreateSystems()</c> can splice these after the lifecycle systems (needs/aging/birth/family).
    /// </summary>
    public static class SocietyModule
    {
        /// <summary>Creates fresh instances of every society system.</summary>
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[]
            {
                new HousingSystem(),
                new JobAssignmentSystem(),
                new WellbeingSystem(),
                new MigrationSystem(),
                new LeadershipSystem(),
            };
        }
    }
}
