namespace GodSim.Core.AI
{
    /// <summary>Outcome metrics of the civilization scorecard (all 0..1, higher is better).</summary>
    public enum AIMetric : byte
    {
        Food = 0, Housing = 1, Health = 2, Happiness = 3, Growth = 4,
        Research = 5, Military = 6, Wealth = 7, Construction = 8, Overall = 9
    }

    /// <summary>Why the learner last changed an advisor's weight (stored as a float in civ memory).</summary>
    public enum LearningReason : byte
    {
        /// <summary>No evaluation yet (first observation only).</summary>
        None = 0,
        /// <summary>The domain was poor and not improving while the advisor worried: listen more.</summary>
        Heeded = 1,
        /// <summary>The advisor screamed while its domain was fine: listen less.</summary>
        CryingWolf = 2,
        /// <summary>The domain is excellent and the advisor is calm: relax toward the prior.</summary>
        Relaxed = 3,
        /// <summary>The domain was fine but sliding and the advisor warned early: a small raise.</summary>
        Foresight = 4,
        /// <summary>No strong evidence: slow regularisation toward the personality prior.</summary>
        Drift = 5
    }

    /// <summary>
    /// Blackboard keys of the AI learning module (all floats, persisted with the save).
    /// civ: "ai.score.{metric}" (0..1), "ai.learn.prev.{metric}" (metrics at the last evaluation),
    /// "ai.trend.*" (scorecard trend state), "ai.learn.d.{advisor}" / "ai.learn.r.{advisor}" (last change / reason),
    /// "ai.learn.us.{advisor}" / "ai.learn.uw.{advisor}" (urgency sample sums since the last evaluation).
    /// </summary>
    public static class AILearningKeys
    {
        /// <summary>Number of metrics including <see cref="AIMetric.Overall"/>.</summary>
        public const int MetricCount = 10;

        private static readonly string[] s_names = { "food", "housing", "health", "happiness", "growth", "research", "military", "wealth", "construction", "overall" };
        private static readonly string[] s_scoreKeys = Prefixed("ai.score.");
        private static readonly string[] s_prevKeys = Prefixed("ai.learn.prev.");

        /// <summary>Tick of the last scorecard trend update.</summary>
        public const string TrendTick = "ai.trend.tick";
        public const string TrendPopulation = "ai.trend.pop";
        public const string TrendPopulationRate = "ai.trend.popRate";
        public const string TrendGold = "ai.trend.gold";
        public const string TrendGoldRate = "ai.trend.goldRate";
        public const string TrendResearch = "ai.trend.research";
        public const string TrendResearchRate = "ai.trend.researchRate";

        /// <summary>Number of learning evaluations performed for the civ.</summary>
        public const string Evaluations = "ai.learn.evals";
        /// <summary>Tick of the last learning evaluation.</summary>
        public const string LastEvaluationTick = "ai.learn.tick";
        /// <summary>1 once personality priors were seeded.</summary>
        public const string PriorsSeeded = "ai.learn.init";

        /// <summary>Settlement memory key the economy writes with days of food left.</summary>
        public const string EconFoodDays = "econ.foodDays";
        /// <summary>Civ memory key the strategy AI writes with perceived threat 0..1.</summary>
        public const string StrategyThreat = "ai.threat";
        /// <summary>Civ memory key the strategy AI writes with own/rival power ratio.</summary>
        public const string StrategyPowerRatio = "ai.powerRatio";

        public static string MetricName(AIMetric m) => s_names[Index(m)];
        public static string ScoreKey(AIMetric m) => s_scoreKeys[Index(m)];
        public static string PreviousKey(AIMetric m) => s_prevKeys[Index(m)];

        private static int Index(AIMetric m) { int i = (int)m; return i >= 0 && i < MetricCount ? i : MetricCount - 1; }

        private static string[] Prefixed(string prefix)
        {
            var a = new string[MetricCount];
            for (int i = 0; i < a.Length; i++) a[i] = prefix + s_names[i];
            return a;
        }
    }
}
