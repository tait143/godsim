using System.Collections.Generic;

namespace GodSim.Core.Religion
{
    /// <summary>Composition entry point of the Religion module.</summary>
    public static class ReligionModule
    {
        /// <summary>
        /// Systems of the module. Suggested placement: in the Divine group right after FaithSystem (so the faith
        /// reacts to this tick's favour/devotion drift) or after Population (so its resident nudges are re-averaged
        /// by WellbeingSystem the next tick). Either order is deterministic.
        /// </summary>
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[] { new ReligionSystem() };
        }
    }
}
