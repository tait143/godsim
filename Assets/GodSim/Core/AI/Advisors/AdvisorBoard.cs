using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>Personality traits a civilization's AI uses. Defaults come from the race; history makes them drift.</summary>
    public enum AITrait : byte { Aggression, Expansionism, Piety, Industriousness, Curiosity, Diplomacy, Xenophobia, Mercantilism }

    /// <summary>
    /// The Advisor Council blackboard (owned by the AI lead; specialist advisors must use it, not edit it).
    ///
    /// Many specialist advisor systems (food, industry, defense, faith, race-specific...) each vote on what a
    /// settlement should build and how it should staff jobs. Votes accumulate in sparse, bounded,
    /// deterministic settlement memory keys; <see cref="AdvisorCouncilSystem"/> decays them every 6 ticks, and the
    /// <c>SettlementGovernorSystem</c> adds <see cref="BuildBias"/> to its utility scores and <see cref="JobBias"/>
    /// to job priorities. Each advisor's influence is scaled by a per-civ learned weight (<see cref="Weight"/>),
    /// tuned over time by the AI learning module.
    ///
    /// Key layout (all floats, persisted with the save):
    ///   settlement: "ai.bias.b{BuildingType}" in [-MaxTotal, MaxTotal]   (build utility bias)
    ///               "ai.bias.j{JobType}"      in [-1, MaxTotal]          (job priority delta)
    ///               "ai.adv.{advisorId}.u"    in [0, 1]                  (advisor urgency, for UI/debug)
    ///   civ:        "ai.w.{advisorId}"        in [0, 3], default 1       (learned advisor weight)
    ///               "ai.trait.{trait}"        in [0, 1], default race    (dynamic personality)
    ///               "ai.god.gratitude", "ai.god.fear" in [0, 1]           (how the civ feels about the player-god)
    /// </summary>
    public static class AdvisorBoard
    {
        public const float MaxContribution = 1f;
        public const float MaxTotal = 3f;
        public const float DecayPerCouncil = 0.5f;

        private static readonly string[] s_buildKeys = BuildKeyTable();
        private static readonly string[] s_jobKeys = JobKeyTable();
        private static readonly string[] s_traitKeys = { "ai.trait.aggression", "ai.trait.expansionism", "ai.trait.piety", "ai.trait.industriousness", "ai.trait.curiosity", "ai.trait.diplomacy", "ai.trait.xenophobia", "ai.trait.mercantilism" };
        private static readonly Dictionary<string, string> s_weightKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> s_urgencyKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        public const string GodGratitudeKey = "ai.god.gratitude";
        public const string GodFearKey = "ai.god.fear";

        public static string BuildKey(BuildingType t) => s_buildKeys[(int)t];
        public static string JobKey(JobType j) => s_jobKeys[(int)j];
        public static string TraitKey(AITrait t) => s_traitKeys[(int)t];

        public static string WeightKey(string advisorId) => Cached(s_weightKeys, advisorId, "ai.w.", "");
        public static string UrgencyKey(string advisorId) => Cached(s_urgencyKeys, advisorId, "ai.adv.", ".u");

        // ------------------------------------------------------------------ voting (advisors)
        /// <summary>Adds a weighted vote (strength clamped to ±1) for building a type in a settlement.</summary>
        /// <param name="logScale">Share of a council cycle this vote represents (e.g. <see cref="AdvisorSystemBase"/>'s CycleScale).
        /// The reason is logged when the per-cycle strength |v / logScale| reaches 0.5; the default 1 keeps the old behaviour.</param>
        public static void VoteBuild(Settlement s, Civilization civ, string advisorId, BuildingType type, float strength, string reason = null, float logScale = 1f)
        {
            if (s == null || !s.IsAlive) return;
            float w = Weight(civ, advisorId);
            float v = MathUtil.Clamp(MathUtil.Sanitize(strength), -MaxContribution, MaxContribution) * w;
            if (v == 0f) return;
            string key = BuildKey(type);
            float total = MathUtil.Clamp(s.Mem(key) + v, -MaxTotal, MaxTotal);
            s.Memory[key] = total;
            float perCycle = logScale > 0f && !float.IsNaN(logScale) && !float.IsInfinity(logScale) ? v / Math.Min(1f, logScale) : v;
            if (reason != null && Math.Abs(perCycle) >= 0.5f) s.LogAi("[" + advisorId + "] " + (v > 0 ? "+" : "") + v.ToString("0.00") + " " + type + ": " + reason);
        }

        /// <summary>Adds a weighted vote (clamped to ±1) to a job's staffing priority in a settlement.</summary>
        public static void VoteJob(Settlement s, Civilization civ, string advisorId, JobType job, float strength)
        {
            if (s == null || !s.IsAlive || job == JobType.None) return;
            float v = MathUtil.Clamp(MathUtil.Sanitize(strength), -MaxContribution, MaxContribution) * Weight(civ, advisorId);
            if (v == 0f) return;
            string key = JobKey(job);
            s.Memory[key] = MathUtil.Clamp(s.Mem(key) + v, -1f, MaxTotal);
        }

        /// <summary>Publishes how urgent an advisor considers its domain in a settlement (0..1, UI/debug + learning).</summary>
        public static void ReportUrgency(Settlement s, string advisorId, float urgency)
        {
            if (s == null || !s.IsAlive) return;
            s.Memory[UrgencyKey(advisorId)] = MathUtil.Clamp01(MathUtil.Sanitize(urgency));
        }

        // ------------------------------------------------------------------ reading (governor, UI)
        public static float BuildBias(Settlement s, BuildingType type) => s == null ? 0f : s.Mem(BuildKey(type));
        public static float JobBias(Settlement s, JobType job) => s == null ? 0f : s.Mem(JobKey(job));
        public static float Urgency(Settlement s, string advisorId) => s == null ? 0f : s.Mem(UrgencyKey(advisorId));

        /// <summary>Learned per-civ advisor weight (default 1, clamped to [0, 3]).</summary>
        public static float Weight(Civilization civ, string advisorId)
        {
            if (civ == null || string.IsNullOrEmpty(advisorId)) return 1f;
            return MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(WeightKey(advisorId), 1f), 1f), 0f, 3f);
        }

        public static void SetWeight(Civilization civ, string advisorId, float weight)
        {
            if (civ == null || string.IsNullOrEmpty(advisorId)) return;
            civ.Memory[WeightKey(advisorId)] = MathUtil.Clamp(MathUtil.Sanitize(weight, 1f), 0f, 3f);
        }

        // ------------------------------------------------------------------ personality & feelings
        /// <summary>Current personality trait value: dynamic value if set by the personality module, else the race default.</summary>
        public static float Trait(Civilization civ, GameDatabase db, AITrait trait)
        {
            if (civ == null) return 0.5f;
            if (civ.Memory.TryGetValue(TraitKey(trait), out float v)) return MathUtil.Clamp01(MathUtil.Sanitize(v, 0.5f));
            return RaceTrait(db, civ.Race, trait);
        }

        public static float RaceTrait(GameDatabase db, RaceId race, AITrait trait)
        {
            RaceDefinition r = null;
            if (db != null && (int)race < db.Races.Count) r = db.Races[(int)race];
            if (r == null) return 0.5f;
            switch (trait)
            {
                case AITrait.Aggression: return r.Aggression;
                case AITrait.Expansionism: return r.Expansionism;
                case AITrait.Piety: return r.Piety;
                case AITrait.Industriousness: return r.Industriousness;
                case AITrait.Curiosity: return r.Curiosity;
                case AITrait.Diplomacy: return r.Diplomacy;
                case AITrait.Xenophobia: return r.Xenophobia;
                case AITrait.Mercantilism: return r.Mercantilism;
                default: return 0.5f;
            }
        }

        public static void SetTrait(Civilization civ, AITrait trait, float value)
        {
            if (civ == null) return;
            civ.Memory[TraitKey(trait)] = MathUtil.Clamp01(MathUtil.Sanitize(value, 0.5f));
        }

        public static float GodGratitude(Civilization civ) => civ == null ? 0f : MathUtil.Clamp01(civ.Mem(GodGratitudeKey));
        public static float GodFear(Civilization civ) => civ == null ? 0f : MathUtil.Clamp01(civ.Mem(GodFearKey));

        // ------------------------------------------------------------------ council maintenance
        /// <summary>Multiplies all bias keys of a settlement by factor; removes near-zero entries. Deterministic, allocation-free.</summary>
        public static void Decay(Settlement s, float factor)
        {
            if (s == null) return;
            var mem = s.Memory;
            for (int i = 0; i < s_buildKeys.Length; i++) DecayKey(mem, s_buildKeys[i], factor);
            for (int i = 0; i < s_jobKeys.Length; i++) DecayKey(mem, s_jobKeys[i], factor);
        }

        private static void DecayKey(Dictionary<string, float> mem, string key, float factor)
        {
            if (!mem.TryGetValue(key, out float v)) return;
            v *= factor;
            if (Math.Abs(v) < 0.01f || float.IsNaN(v)) mem.Remove(key); else mem[key] = v;
        }

        private static string Cached(Dictionary<string, string> cache, string id, string prefix, string suffix)
        {
            if (id == null) id = "";
            lock (cache) // several simulations may run in one process (arena, tests): keep the shared cache thread-safe
            {
                if (!cache.TryGetValue(id, out var key))
                {
                    key = prefix + id + suffix;
                    if (cache.Count < 4096) cache[id] = key; // bounded cache; ids are code constants
                }
                return key;
            }
        }

        private static string[] BuildKeyTable()
        {
            var a = new string[Counts.Buildings];
            for (int i = 0; i < a.Length; i++) a[i] = "ai.bias.b" + i;
            return a;
        }

        private static string[] JobKeyTable()
        {
            var a = new string[Counts.Jobs];
            for (int i = 0; i < a.Length; i++) a[i] = "ai.bias.j" + i;
            return a;
        }
    }

    /// <summary>
    /// Runs before the advisors each council cycle: halves every settlement's accumulated advisor votes once per 24-tick cycle so stale
    /// advice fades and fresh advice dominates. Composition order: AdvisorCouncil -> advisors -> SettlementGovernor.
    /// </summary>
    public sealed class AdvisorCouncilSystem : ISimSystem
    {
        public string Name => "AI.AdvisorCouncil";
        /// <summary>One council cycle = 24 ticks: every advisor (intervals 6..48) votes at least once per cycle.</summary>
        public int TickInterval => 24;
        public void Initialize(SimContext ctx) { }

        public void Tick(SimContext ctx)
        {
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
                if (items[i].IsAlive) AdvisorBoard.Decay(items[i], AdvisorBoard.DecayPerCouncil);
        }
    }

    /// <summary>
    /// Optional base class for advisors: iterates alive settlements of alive civs and calls <see cref="Advise"/>.
    /// </summary>
    public abstract class AdvisorSystemBase : ISimSystem
    {
        /// <summary>Stable id used in memory keys and logs, e.g. "food". Lowercase, [a-z0-9_].</summary>
        public abstract string AdvisorId { get; }
        public virtual string Name => "AI.Advisor." + AdvisorId;
        public virtual int TickInterval => 12;
        public virtual void Initialize(SimContext ctx) { }

        public void Tick(SimContext ctx)
        {
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (!s.IsAlive) continue;
                var civ = ctx.State.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;
                Advise(ctx, civ, s);
            }
            AfterRun(ctx);
        }

        /// <summary>Called once per alive settlement per run.</summary>
        protected abstract void Advise(SimContext ctx, Civilization civ, Settlement s);

        /// <summary>Hook after all settlements were advised (civ-level work).</summary>
        protected virtual void AfterRun(SimContext ctx) { }

        /// <summary>Votes are normalized per council cycle (24 ticks) so fast advisors do not out-shout slow ones.</summary>
        protected float CycleScale => TickInterval >= 24 ? 1f : TickInterval / 24f;
        protected void VoteBuild(Settlement s, Civilization civ, BuildingType t, float strength, string reason = null) => AdvisorBoard.VoteBuild(s, civ, AdvisorId, t, strength * CycleScale, reason, CycleScale);
        protected void VoteJob(Settlement s, Civilization civ, JobType j, float strength) => AdvisorBoard.VoteJob(s, civ, AdvisorId, j, strength * CycleScale);
        protected void Urgency(Settlement s, float u) => AdvisorBoard.ReportUrgency(s, AdvisorId, u);
    }
}
