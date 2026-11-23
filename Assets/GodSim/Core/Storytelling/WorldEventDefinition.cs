using System;

namespace GodSim.Core.Storytelling
{
    /// <summary>Season bit masks used by <see cref="WorldEventDefinition.SeasonMask"/>.</summary>
    public static class Seasons
    {
        public const int Spring = 1;
        public const int Summer = 2;
        public const int Autumn = 4;
        public const int Winter = 8;
        public const int Any = Spring | Summer | Autumn | Winter;

        /// <summary>The mask bit of a season.</summary>
        public static int Of(Season s) => 1 << ((int)s & 3);
    }

    /// <summary>
    /// One card of the data-driven world-event deck. Evaluation order (all deterministic, no RNG):
    /// declarative gates (age, season, settlement count, cooldown) -> <see cref="Conditions"/> (civ-level) ->
    /// <see cref="SelectTarget"/> (resolves the settlement / partner civ / tile into the context) ->
    /// <see cref="Weight"/>. When chosen, <see cref="Effect"/> runs (it may use RNG) and, if it returns true,
    /// the cooldown is stamped and one of the chronicle <see cref="Templates"/> is written.
    /// </summary>
    public sealed class WorldEventDefinition
    {
        private static readonly Func<WorldEventContext, bool>[] NoConditions = new Func<WorldEventContext, bool>[0];

        /// <summary>Stable unique key (snake_case). Persisted in memory keys "story.cd.&lt;key&gt;".</summary>
        public string Key = "";
        public string Title = "";
        public string Description = "";
        public ChronicleCategory Category = ChronicleCategory.World;
        /// <summary>Chronicle importance 0..3.</summary>
        public int Importance = 1;
        /// <summary>Years before this event may hit the same civilization again.</summary>
        public float CooldownYears = 20f;
        /// <summary>When true the cooldown is shared world-wide (stored in WorldState.Memory as well).</summary>
        public bool WorldCooldown;
        /// <summary>Pairwise events: the partner civ (context OtherCiv) is also put on cooldown.</summary>
        public bool CooldownAppliesToOther;
        /// <summary>Relative weight in the deck (multiplied by <see cref="Weight"/>).</summary>
        public float BaseWeight = 1f;
        public AgeId MinAge = AgeId.Dawn;
        public AgeId MaxAge = AgeId.Ascension;
        /// <summary>Allowed seasons (see <see cref="Seasons"/>).</summary>
        public int SeasonMask = Seasons.Any;
        public int MinSettlements = 1;

        /// <summary>Civ-level predicates; all must hold. Must not use RNG or mutate state.</summary>
        public Func<WorldEventContext, bool>[] Conditions = NoConditions;
        /// <summary>Resolves targets into the context; false = no valid target. Must not use RNG or mutate state.</summary>
        public Func<WorldEventContext, bool> SelectTarget;
        /// <summary>Optional weight multiplier evaluated after target selection (no RNG, no mutation).</summary>
        public Func<WorldEventContext, float> Weight;
        /// <summary>Applies the event (through ctx.Ops for entity changes; values clamped). False = aborted (no cooldown, no chronicle).</summary>
        public Func<WorldEventContext, bool> Effect;

        /// <summary>2-4 chronicle variants. Placeholders: {settlement} {civ} {race} {leader} {other} {othersettlement} {person} {amount} {count}.</summary>
        public string[] Templates = new string[0];
        /// <summary>Optional 2-4 variants used when the effect sets <see cref="WorldEventContext.UseAltTemplates"/> (alternate outcome).</summary>
        public string[] AltTemplates;

        private string _cooldownKey;

        /// <summary>Memory key holding the tick this event last fired ("story.cd.&lt;key&gt;").</summary>
        public string CooldownMemoryKey => _cooldownKey ?? (_cooldownKey = WorldEventSystem.CooldownPrefix + Key);

        public long CooldownTicks => SimTime.YearsToTicks(MathUtil.Clamp(MathUtil.Sanitize(CooldownYears), 0f, 100000f));

        public override string ToString() => Key;
    }
}
