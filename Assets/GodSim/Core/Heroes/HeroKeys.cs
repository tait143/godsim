using System;
using System.Collections.Generic;

namespace GodSim.Core.Heroes
{
    /// <summary>
    /// Blackboard keys used by the hero module. Every hero is registered in <see cref="WorldState.Memory"/>
    /// (serialized) under <c>hero.&lt;personId&gt;.*</c>; the presence of the <c>.role</c> key is what makes a
    /// person a hero. Nothing else is persisted, so heroes survive save/load and new system instances.
    /// </summary>
    public static class HeroKeys
    {
        /// <summary>Common key prefix.</summary>
        public const string Prefix = "hero.";
        /// <summary>Suffix of the role key (numeric <see cref="HeroRole"/>).</summary>
        public const string RoleSuffix = ".role";

        /// <summary>Civilization.Memory key: number of heroes this civilization has ever raised.</summary>
        public const string CivRaisedCount = "hero.raised";

        /// <summary>Numeric <see cref="HeroRole"/>.</summary>
        public static string Role(int personId) => Prefix + personId + RoleSuffix;
        /// <summary>Accumulated deeds (float; displayed floored).</summary>
        public static string Deeds(int personId) => Prefix + personId + ".deeds";
        /// <summary>Tick the hero rose (float).</summary>
        public static string Since(int personId) => Prefix + personId + ".since";
        /// <summary>Settlement id where the hero rose ("the Architect of &lt;home&gt;").</summary>
        public static string Home(int personId) => Prefix + personId + ".home";
        /// <summary>Civilization id the hero rose in.</summary>
        public static string Civ(int personId) => Prefix + personId + ".civ";

        /// <summary>Removes every key belonging to a hero.</summary>
        public static void Clear(Dictionary<string, float> memory, int personId)
        {
            if (memory == null) return;
            memory.Remove(Role(personId));
            memory.Remove(Deeds(personId));
            memory.Remove(Since(personId));
            memory.Remove(Home(personId));
            memory.Remove(Civ(personId));
        }

        /// <summary>Parses "hero.&lt;digits&gt;.role" without allocating. Returns false for any other key.</summary>
        public static bool TryParseRoleKey(string key, out int personId)
        {
            personId = -1;
            if (key == null) return false;
            int len = key.Length;
            if (len <= Prefix.Length + RoleSuffix.Length) return false;
            if (string.CompareOrdinal(key, 0, Prefix, 0, Prefix.Length) != 0) return false;
            if (!key.EndsWith(RoleSuffix, StringComparison.Ordinal)) return false;
            int end = len - RoleSuffix.Length;
            if (end - Prefix.Length > 9) return false; // avoid int overflow
            int v = 0;
            for (int i = Prefix.Length; i < end; i++)
            {
                char c = key[i];
                if (c < '0' || c > '9') return false;
                v = v * 10 + (c - '0');
            }
            personId = v;
            return true;
        }

        /// <summary>Fills <paramref name="into"/> with every registered hero id, sorted ascending (deterministic).</summary>
        public static void CollectHeroIds(Dictionary<string, float> memory, List<int> into)
        {
            into.Clear();
            if (memory == null) return;
            foreach (var kv in memory)
                if (TryParseRoleKey(kv.Key, out int id)) into.Add(id);
            into.Sort();
        }

        /// <summary>Reads a valid role for a person, or false if the key is absent or corrupt.</summary>
        public static bool TryGetRole(Dictionary<string, float> memory, int personId, out HeroRole role)
        {
            role = HeroRole.General;
            if (memory == null || !memory.TryGetValue(Role(personId), out float v)) return false;
            return TryDecodeRole(v, out role);
        }

        /// <summary>Validates a stored numeric role.</summary>
        public static bool TryDecodeRole(float v, out HeroRole role)
        {
            role = HeroRole.General;
            if (float.IsNaN(v) || float.IsInfinity(v)) return false;
            int i = (int)Math.Round(v);
            if (i < 0 || i > (int)HeroRole.Bard || Math.Abs(v - i) > 0.01f) return false;
            role = (HeroRole)i;
            return true;
        }

        /// <summary>Reads a float key with NaN/Infinity protection.</summary>
        public static float Read(Dictionary<string, float> memory, string key, float fallback)
        {
            if (memory == null || !memory.TryGetValue(key, out float v)) return fallback;
            return MathUtil.Sanitize(v, fallback);
        }

        /// <summary>Reads an id stored as float (-1 if missing/corrupt).</summary>
        public static int ReadId(Dictionary<string, float> memory, string key)
        {
            float v = Read(memory, key, -1f);
            if (v < 0f || v > 2e9f) return -1;
            return (int)v;
        }
    }
}
