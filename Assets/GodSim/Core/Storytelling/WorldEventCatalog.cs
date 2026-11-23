using System;
using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    /// <summary>
    /// The built-in world-event deck. Built once (static, deterministic order); the order of <see cref="All"/>
    /// is part of determinism (weighted picks index into it), so append new events at the end of a group.
    /// </summary>
    public static partial class WorldEventCatalog
    {
        private static readonly List<WorldEventDefinition> _all;
        private static readonly Dictionary<string, WorldEventDefinition> _byKey;

        static WorldEventCatalog()
        {
            _all = new List<WorldEventDefinition>(32);
            AddProsperity(_all);
            AddFaith(_all);
            AddCalamities(_all);
            AddPolitics(_all);
            _byKey = new Dictionary<string, WorldEventDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < _all.Count; i++)
                if (!_byKey.ContainsKey(_all[i].Key)) _byKey[_all[i].Key] = _all[i];
        }

        /// <summary>Every world event, in deck order.</summary>
        public static IReadOnlyList<WorldEventDefinition> All => _all;

        /// <summary>Definition by key, or null.</summary>
        public static WorldEventDefinition Find(string key)
        {
            if (key == null) return null;
            return _byKey.TryGetValue(key, out var d) ? d : null;
        }

        /// <summary>Structural validation of the deck. Returns human-readable problems (empty = valid).</summary>
        public static List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal); // membership only, never iterated
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d == null) { errors.Add("null definition at " + i); continue; }
                if (string.IsNullOrEmpty(d.Key)) errors.Add("definition " + i + " has no key");
                else if (!seen.Add(d.Key)) errors.Add("duplicate key " + d.Key);
                if (string.IsNullOrEmpty(d.Title)) errors.Add(d.Key + ": no title");
                if (d.Effect == null) errors.Add(d.Key + ": no effect");
                if (!(d.CooldownYears > 0f)) errors.Add(d.Key + ": cooldown must be > 0");
                if (!(d.BaseWeight > 0f)) errors.Add(d.Key + ": base weight must be > 0");
                if (d.Importance < 0 || d.Importance > 3) errors.Add(d.Key + ": importance out of range");
                if ((d.SeasonMask & Seasons.Any) == 0) errors.Add(d.Key + ": no season allowed");
                if (d.MinAge > d.MaxAge) errors.Add(d.Key + ": MinAge > MaxAge");
                CheckTemplates(d.Key, "Templates", d.Templates, errors);
                if (d.AltTemplates != null) CheckTemplates(d.Key, "AltTemplates", d.AltTemplates, errors);
            }
            return errors;
        }

        private static void CheckTemplates(string key, string field, string[] t, List<string> errors)
        {
            if (t == null || t.Length < 2 || t.Length > 4) { errors.Add(key + ": " + field + " needs 2-4 variants"); return; }
            for (int i = 0; i < t.Length; i++)
            {
                if (string.IsNullOrEmpty(t[i])) errors.Add(key + ": empty template");
                else if (!StoryText.UsesOnlyKnownPlaceholders(t[i])) errors.Add(key + ": unknown placeholder in '" + t[i] + "'");
                else if (t[i].Length > 280) errors.Add(key + ": template too long");
            }
        }

        // ------------------------------------------------------------------ shared selector helpers
        /// <summary>Largest (by alive population) settlement of the civ passing the filter. Ties keep list order.</summary>
        internal static Settlement Largest(WorldEventContext e, Func<Settlement, bool> filter)
        {
            Settlement best = null;
            int bestPop = -1;
            var ids = e.Civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                if (s == null || (filter != null && !filter(s))) continue;
                int pop = StoryUtil.AliveResidents(e.State, s);
                if (pop > bestPop) { bestPop = pop; best = s; }
            }
            return best;
        }

        /// <summary>Settlement with the highest score (score &lt;= float.MinValue = rejected).</summary>
        internal static Settlement BestBy(WorldEventContext e, Func<Settlement, float> score)
        {
            Settlement best = null;
            float bestScore = float.MinValue;
            var ids = e.Civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                if (s == null) continue;
                float v = MathUtil.Sanitize(score(s), float.MinValue);
                if (v > bestScore) { bestScore = v; best = s; }
            }
            return best;
        }

        internal static bool IsAdult(Person p) => p.IsAdult;

        internal static bool NotLeaderNorSoldier(WorldEventContext e, Person p) => p.Id != e.Civ.LeaderId && p.ArmyId < 0;
    }
}
