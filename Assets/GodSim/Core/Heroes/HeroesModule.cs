using System.Collections.Generic;

namespace GodSim.Core.Heroes
{
    /// <summary>Composition entry point for the hero module.</summary>
    public static class HeroesModule
    {
        /// <summary>Systems of this module (run after Population/Economy so effects land on fresh values).</summary>
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[] { new HeroSystem() };
        }
    }
}
