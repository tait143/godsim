using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>Entry point for Composition: the urgency sampler and the advisor learner (register after the advisors).</summary>
    public static class AILearningModule
    {
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[] { new AdvisorUrgencySampler(), new AdvisorLearningSystem() };
        }
    }
}
