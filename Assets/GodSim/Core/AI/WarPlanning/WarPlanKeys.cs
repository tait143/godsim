namespace GodSim.Core.AI
{
    /// <summary>
    /// Civilization blackboard keys written by <see cref="WarPlannerSystem"/> (all floats, persisted with the save).
    /// Other modules may READ these keys; only the war planner writes them.
    /// <list type="bullet">
    /// <item><c>war.phase</c>: <see cref="WarPhase"/> as a float (0 peace, 1 tension, 2 mobilizing, 3 at war, 4 demobilizing).</item>
    /// <item><c>war.target</c>: civilization id of the rival the plan is aimed at (-1 none). At war: the primary enemy.</item>
    /// <item><c>war.objective</c>: settlement id of the war objective (-1 none): the nearest weak enemy settlement,
    /// chosen while mobilizing and at war. <b>For the military module:</b> prefer it as the army target when it is
    /// reachable and the odds allow; it is re-evaluated every planner run (12 ticks).</item>
    /// <item><c>war.weariness</c>: 0..1 war weariness (own losses vs population at war start, war duration and a
    /// losing war score; scaled by Diplomacy, damped by Aggression). Decays after peace. <b>For the relations
    /// module:</b> a value near 1 means this civ wants peace; it can be read as an extra exhaustion / peace-chance
    /// factor (e.g. end a war when both sides exceed 0.8, or raise the stalemate peace chance by weariness).</item>
    /// <item><c>war.tension</c>: 0..1 tension toward <c>war.target</c> (1 at war).</item>
    /// <item><c>war.phaseTick</c>: tick the current phase was entered.</item>
    /// <item><c>war.start</c>, <c>war.popStart</c>: tick and own population when the current war phase began.</item>
    /// <item><c>war.demobUntil</c>, <c>war.demobLen</c>: end tick and length (ticks) of the demobilization phase.</item>
    /// <item><c>war.op.{civId}</c>, <c>war.trend.{civId}</c>: last seen opinion of a rival and its smoothed trend per year.</item>
    /// </list>
    /// </summary>
    public static class WarPlanKeys
    {
        public const string Phase = "war.phase";
        public const string Target = "war.target";
        public const string Objective = "war.objective";
        public const string Weariness = "war.weariness";
        public const string Tension = "war.tension";
        public const string PhaseTick = "war.phaseTick";
        public const string WarStart = "war.start";
        public const string PopulationAtStart = "war.popStart";
        public const string DemobUntil = "war.demobUntil";
        public const string DemobLength = "war.demobLen";
        public const string OpinionPrefix = "war.op.";
        public const string TrendPrefix = "war.trend.";

        /// <summary>Reads a civ's war phase (Peace for null/missing/garbage values).</summary>
        public static WarPhase PhaseOf(Civilization civ)
        {
            if (civ == null) return WarPhase.Peace;
            float v = MathUtil.Sanitize(civ.Mem(Phase));
            int p = MathUtil.Clamp(MathUtil.RoundToInt(v), 0, 4);
            return (WarPhase)p;
        }

        /// <summary>Reads a civ's war weariness 0..1.</summary>
        public static float WearinessOf(Civilization civ) => civ == null ? 0f : MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(Weariness)));

        /// <summary>Reads the war objective settlement id (-1 none).</summary>
        public static int ObjectiveOf(Civilization civ) => civ == null ? -1 : (int)MathUtil.Sanitize(civ.Mem(Objective, -1f), -1f);

        /// <summary>Reads the target civilization id (-1 none).</summary>
        public static int TargetOf(Civilization civ) => civ == null ? -1 : (int)MathUtil.Sanitize(civ.Mem(Target, -1f), -1f);
    }

    /// <summary>Strategic war phases of a civilization (stored as <c>war.phase</c>).</summary>
    public enum WarPhase : byte { Peace = 0, Tension = 1, Mobilizing = 2, AtWar = 3, Demobilizing = 4 }
}
