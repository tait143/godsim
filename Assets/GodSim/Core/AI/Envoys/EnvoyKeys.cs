using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys used by the envoy systems. Ticks are stored as floats.
    /// <list type="bullet">
    /// <item><c>civ.Memory["env.coalition"]</c> = id of the hegemon the civ stands against.</item>
    /// <item><c>civ.Memory["env.cd.*"]</c> = tick of the civ's last gift / tribute / pact / marriage (rate limits).</item>
    /// <item><c>civ.Memory["env.tributeTo"]</c> = id of the civ it last paid tribute to.</item>
    /// <item><c>state.Memory["env.pact.&lt;lo&gt;.&lt;hi&gt;"]</c> = tick a non-aggression pact expires.</item>
    /// <item><c>state.Memory["env.betray.&lt;betrayer&gt;.&lt;betrayed&gt;"]</c> = tick of the betrayal.</item>
    /// <item><c>state.Memory["env.marry.&lt;lo&gt;.&lt;hi&gt;"]</c> = tick the two royal houses were united.</item>
    /// <item><c>state.Memory["env.coal.&lt;hegemon&gt;"]</c> = tick the coalition against a hegemon was chronicled.</item>
    /// </list>
    /// </summary>
    public static class EnvoyKeys
    {
        /// <summary>civ.Memory: hegemon id the civ is in a coalition against.</summary>
        public const string Coalition = "env.coalition";
        /// <summary>civ.Memory: last gift tick.</summary>
        public const string GiftCooldown = "env.cd.gift";
        /// <summary>civ.Memory: last tribute (or refusal) tick.</summary>
        public const string TributeCooldown = "env.cd.tribute";
        /// <summary>civ.Memory: id of the civ last paid tribute to.</summary>
        public const string TributeTo = "env.tributeTo";
        /// <summary>civ.Memory: last pact signing tick.</summary>
        public const string PactCooldown = "env.cd.pact";
        /// <summary>civ.Memory: last royal marriage tick.</summary>
        public const string MarriageCooldown = "env.cd.marry";
        /// <summary>Key the relations module uses for the end tick of the last war of a pair (read only).</summary>
        public const string LastWarPrefix = "dipl.lastWar.";

        /// <summary>state.Memory key of the pact between two civs (symmetric).</summary>
        public static string Pact(int a, int b) => "env.pact." + Math.Min(a, b) + "." + Math.Max(a, b);
        /// <summary>state.Memory key of a betrayal (ordered: betrayer, betrayed).</summary>
        public static string Betrayal(int betrayer, int betrayed) => "env.betray." + betrayer + "." + betrayed;
        /// <summary>state.Memory key of a royal marriage (symmetric).</summary>
        public static string Marriage(int a, int b) => "env.marry." + Math.Min(a, b) + "." + Math.Max(a, b);
        /// <summary>state.Memory key marking the chronicled coalition against a hegemon.</summary>
        public static string CoalitionFormed(int hegemonId) => "env.coal." + hegemonId;
        /// <summary>The relations module's last-war key (symmetric).</summary>
        public static string LastWar(int a, int b) => LastWarPrefix + Math.Min(a, b) + "." + Math.Max(a, b);
    }

    /// <summary>Caches key strings so per-run loops do not allocate. Bounded.</summary>
    internal sealed class EnvoyKeyCache
    {
        private const int MaxEntries = 4096;
        private readonly Dictionary<long, string> _pact = new Dictionary<long, string>();
        private readonly Dictionary<long, string> _betray = new Dictionary<long, string>();
        private readonly Dictionary<long, string> _marry = new Dictionary<long, string>();
        private readonly Dictionary<long, string> _lastWar = new Dictionary<long, string>();
        private readonly Dictionary<long, string> _coal = new Dictionary<long, string>();

        private static long Ordered(int a, int b) => ((long)a << 32) | (uint)b;

        public string Pact(int a, int b) => Get(_pact, DiplomacyState.Key(a, b), a, b, 0);
        public string Betrayal(int betrayer, int betrayed) => Get(_betray, Ordered(betrayer, betrayed), betrayer, betrayed, 1);
        public string Marriage(int a, int b) => Get(_marry, DiplomacyState.Key(a, b), a, b, 2);
        public string LastWar(int a, int b) => Get(_lastWar, DiplomacyState.Key(a, b), a, b, 3);
        public string CoalitionFormed(int hegemon) => Get(_coal, hegemon, hegemon, 0, 4);

        private static string Get(Dictionary<long, string> map, long k, int a, int b, int kind)
        {
            if (map.TryGetValue(k, out var s)) return s;
            if (map.Count >= MaxEntries) map.Clear();
            switch (kind)
            {
                case 0: s = EnvoyKeys.Pact(a, b); break;
                case 1: s = EnvoyKeys.Betrayal(a, b); break;
                case 2: s = EnvoyKeys.Marriage(a, b); break;
                case 3: s = EnvoyKeys.LastWar(a, b); break;
                default: s = EnvoyKeys.CoalitionFormed(a); break;
            }
            map[k] = s;
            return s;
        }
    }
}
