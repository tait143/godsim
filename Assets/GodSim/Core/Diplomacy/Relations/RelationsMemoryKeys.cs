using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Namespaced <see cref="WorldState.Memory"/> keys used by the diplomacy relation systems.
    /// Ticks are stored as floats (exact up to 2^24 ticks, about 174,000 game years).
    /// </summary>
    public static class RelationsMemoryKeys
    {
        /// <summary>Prefix of every diplomacy blackboard key.</summary>
        public const string Prefix = "dipl.";

        /// <summary>Key holding the tick the last war between two civs ended: <c>dipl.lastWar.&lt;lo&gt;.&lt;hi&gt;</c>.</summary>
        public static string LastWar(int civA, int civB)
        {
            int lo = Math.Min(civA, civB), hi = Math.Max(civA, civB);
            return "dipl.lastWar." + lo + "." + hi;
        }

        /// <summary>Key holding the combined military power at the start of a war: <c>dipl.war.&lt;id&gt;.initialPower</c>.</summary>
        public static string WarInitialPower(int warId) => "dipl.war." + warId + ".initialPower";
    }

    /// <summary>Per-system cache of blackboard key strings so per-run loops do not allocate.</summary>
    internal sealed class RelationsKeyCache
    {
        private const int MaxEntries = 4096;
        private readonly Dictionary<long, string> _lastWar = new Dictionary<long, string>();
        private readonly Dictionary<int, string> _initialPower = new Dictionary<int, string>();

        public string LastWar(int a, int b)
        {
            long k = DiplomacyState.Key(a, b);
            if (_lastWar.TryGetValue(k, out var s)) return s;
            if (_lastWar.Count >= MaxEntries) _lastWar.Clear();
            s = RelationsMemoryKeys.LastWar(a, b);
            _lastWar[k] = s;
            return s;
        }

        public string InitialPower(int warId)
        {
            if (_initialPower.TryGetValue(warId, out var s)) return s;
            if (_initialPower.Count >= MaxEntries) _initialPower.Clear();
            s = RelationsMemoryKeys.WarInitialPower(warId);
            _initialPower[warId] = s;
            return s;
        }
    }
}
