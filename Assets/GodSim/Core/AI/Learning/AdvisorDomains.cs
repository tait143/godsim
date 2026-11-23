using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>One advisor known to the learner: which outcome metric judges it, its council title and precomputed keys.</summary>
    public sealed class AdvisorDomain
    {
        public readonly string Id;
        public readonly AIMetric Metric;
        /// <summary>Council title without article, e.g. "Treasurer".</summary>
        public readonly string Title;
        public readonly string WeightKey, UrgencyKey, DeltaKey, ReasonKey, UrgencySumKey, UrgencyWeightKey;
        /// <summary>Stable hash of the id (exploration noise).</summary>
        public readonly ulong Hash;
        internal readonly AITrait TraitA, TraitB;
        internal readonly float GainA, GainB;

        internal AdvisorDomain(string id, AIMetric metric, string title, AITrait traitA, float gainA, AITrait traitB, float gainB)
        {
            Id = id; Metric = metric; Title = title;
            TraitA = traitA; GainA = gainA; TraitB = traitB; GainB = gainB;
            WeightKey = AdvisorBoard.WeightKey(id);
            UrgencyKey = AdvisorBoard.UrgencyKey(id);
            DeltaKey = "ai.learn.d." + id;
            ReasonKey = "ai.learn.r." + id;
            UrgencySumKey = "ai.learn.us." + id;
            UrgencyWeightKey = "ai.learn.uw." + id;
            Hash = Hashing.Fnv1a64(id);
        }
    }

    /// <summary>
    /// Static, ordered table of advisor ids the learner understands (domain metric + personality prior).
    /// Advisors absent from the table are never touched. Order is fixed, so iteration is deterministic.
    /// </summary>
    public static class AdvisorDomains
    {
        public const float MinPrior = 0.6f;
        public const float MaxPrior = 1.6f;

        private static readonly AdvisorDomain[] s_all =
        {
            D("food", AIMetric.Food, "Granary-keeper"),
            D("growth", AIMetric.Housing, "Steward of Hearths"),
            D("supply", AIMetric.Construction, "Quartermaster", AITrait.Industriousness, 0.3f),
            D("defense", AIMetric.Military, "Marshal", AITrait.Aggression, 0.5f, AITrait.Xenophobia, 0.3f),
            D("faith", AIMetric.Happiness, "High Priest", AITrait.Piety, 0.8f),
            D("science", AIMetric.Research, "Loremaster", AITrait.Curiosity, 0.8f),
            D("treasury", AIMetric.Wealth, "Treasurer", AITrait.Mercantilism, 0.8f),
            D("health", AIMetric.Health, "Physician"),
            D("happiness", AIMetric.Happiness, "Master of Revels"),
            D("labor", AIMetric.Wealth, "Foreman", AITrait.Industriousness, 0.6f),
            D("seasons", AIMetric.Food, "Almanac-keeper"),
            D("ages", AIMetric.Research, "Chronicler", AITrait.Curiosity, 0.5f),
            D("recovery", AIMetric.Overall, "Restorer"),
            D("urban", AIMetric.Housing, "City Planner", AITrait.Industriousness, 0.2f),
            D("frontier", AIMetric.Growth, "Pathfinder", AITrait.Expansionism, 0.8f),
            D("prospect", AIMetric.Wealth, "Prospector", AITrait.Industriousness, 0.4f, AITrait.Mercantilism, 0.3f),
            D("specialize", AIMetric.Wealth, "Guildmaster", AITrait.Mercantilism, 0.5f),
            D("capital", AIMetric.Growth, "Chancellor", AITrait.Expansionism, 0.3f),
            D("logistics", AIMetric.Construction, "Roadwarden", AITrait.Industriousness, 0.3f),
            D("infra", AIMetric.Construction, "Master Builder", AITrait.Industriousness, 0.4f),
            D("race_human", AIMetric.Overall, "Elder of Men"),
            D("race_elf", AIMetric.Overall, "Elder of the Groves", AITrait.Xenophobia, 0.3f),
            D("race_dwarf", AIMetric.Overall, "Elder of the Halls", AITrait.Industriousness, 0.3f),
            D("race_orc", AIMetric.Overall, "Elder of the Clans", AITrait.Aggression, 0.3f),
            D("race_saurian", AIMetric.Overall, "Elder of the Mire", AITrait.Piety, 0.3f),
            D("god", AIMetric.Overall, "Oracle", AITrait.Piety, 0.6f),
            D("war", AIMetric.Military, "Warlord", AITrait.Aggression, 1.0f, AITrait.Diplomacy, -0.6f),
        };

        /// <summary>All known advisors in fixed order.</summary>
        public static IReadOnlyList<AdvisorDomain> All => s_all;

        /// <summary>Finds a known advisor by id (ordinal), or null.</summary>
        public static AdvisorDomain Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < s_all.Length; i++) if (string.Equals(s_all[i].Id, id, StringComparison.Ordinal)) return s_all[i];
            return null;
        }

        /// <summary>
        /// Personality prior for an advisor: 1 + sum(gain * (trait - 0.5)), clamped to [MinPrior, MaxPrior].
        /// A neutral civ (all traits 0.5) gets exactly 1; e.g. a fully aggressive civ heeds 'defense'/'war' more.
        /// </summary>
        public static float Prior(Civilization civ, GameDatabase db, AdvisorDomain d)
        {
            if (civ == null || d == null) return 1f;
            float p = 1f;
            if (d.GainA != 0f) p += d.GainA * (AdvisorBoard.Trait(civ, db, d.TraitA) - 0.5f);
            if (d.GainB != 0f) p += d.GainB * (AdvisorBoard.Trait(civ, db, d.TraitB) - 0.5f);
            return MathUtil.Clamp(MathUtil.Sanitize(p, 1f), MinPrior, MaxPrior);
        }

        private static AdvisorDomain D(string id, AIMetric m, string title, AITrait a = AITrait.Aggression, float ga = 0f, AITrait b = AITrait.Aggression, float gb = 0f)
            => new AdvisorDomain(id, m, title, a, ga, b, gb);
    }
}
