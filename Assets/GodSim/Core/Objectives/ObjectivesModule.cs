using System.Collections.Generic;

namespace GodSim.Core.Objectives
{
    /// <summary>Composition entry point for the objectives module (divine quests, milestones, rewards, ascension victory).</summary>
    public static class ObjectivesModule
    {
        /// <summary>Systems to register with the engine (order-independent; suggested after Diplomacy).</summary>
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[] { new ObjectiveSystem() };
        }
    }
}
