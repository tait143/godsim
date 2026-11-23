using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Player-facing reasons for infrastructure votes ("2 factories idle without power: +25% ... from a Power Plant").
    /// Strings are built once per distinct (kind, service, building, values) and cached in a bounded table, so steady
    /// state advice does not allocate.
    /// </summary>
    public sealed class InfraText
    {
        public const int Low = 1;
        public const int Soon = 2;
        public const int Rebuild = 3;
        public const int Drought = 4;
        public const int Over = 5;
        public const int PowerIdle = 6;
        public const int PowerPlanned = 7;
        public const int FuelFree = 8;
        public const int FuelProducer = 9;
        public const int FuelChain = 10;
        public const int NoFuel = 11;
        public const int NoIndustry = 12;
        public const int Hub = 13;

        /// <summary>Upper bound of cached strings (further distinct reasons are built but not cached).</summary>
        public const int MaxCached = 1024;
        private const int MaxValue = 0xFFFFF;

        private readonly Dictionary<long, string> _cache = new Dictionary<long, string>();

        public int CachedCount => _cache.Count;
        public void Clear() => _cache.Clear();

        /// <summary>Reason text for a vote; <paramref name="a"/>/<paramref name="b"/> are kind-specific integers (percent, count, hundredths).</summary>
        public string Get(int kind, ServiceType svc, BuildingDefinition def, int a, int b)
        {
            if (def == null) return null;
            a = MathUtil.Clamp(a, 0, MaxValue);
            b = MathUtil.Clamp(b, 0, MaxValue);
            long key = ((long)(kind & 0xFF) << 56) | ((long)((int)svc & 0xFF) << 48) | ((long)((int)def.Type & 0xFF) << 40) | ((long)a << 20) | (long)b;
            if (_cache.TryGetValue(key, out var text)) return text;
            text = Build(kind, svc, def, a, b);
            if (_cache.Count < MaxCached) _cache[key] = text;
            return text;
        }

        private static string Build(int kind, ServiceType svc, BuildingDefinition def, int a, int b)
        {
            string name = string.IsNullOrEmpty(def.Name) ? def.Type.ToString() : def.Name;
            string an = Article(name) + " " + name;
            string service = svc.ToString();
            string lower = service.ToLowerInvariant();
            switch (kind)
            {
                case Low: return service + " at " + a + "%: " + an + " adds +" + b + "%";
                case Soon: return "Growth will cut " + lower + " to " + a + "%: " + an + " adds +" + b + "% ahead of time";
                case Rebuild: return "Rebuilding after losses: " + lower + " at " + a + "%, " + an + " adds +" + b + "%";
                case Drought: return "Drought: " + lower + " at " + a + "%, " + an + " adds +" + b + "%";
                case Over: return service + " already at " + a + "%: no more " + name + " needed";
                case PowerIdle: return (a == 1 ? "Factory idles" : a + " factories idle") + " without power: +25% (+" + Fixed2(b) + " output/day) from " + an;
                case PowerPlanned: return a + (a == 1 ? " factory" : " factories") + " planned: " + an + " gets power ready first";
                case FuelFree: return "Fuel covers only " + a + "% of a fuel plant's needs: " + an + " burns none";
                case FuelProducer: return "Power needs " + Fixed2(a) + " fuel/day: " + an + " taps the local deposit";
                case FuelChain: return "Factories idle without power: " + an + " planned with its fuel supply (" + a + "% secured)";
                case NoFuel: return "No fuel source nearby: " + an + " would sit cold";
                case NoIndustry: return "No factories to power yet: " + an + " would idle";
                case Hub: return "Commerce at " + a + "%" + (b > 0 ? ", " + b + (b == 1 ? " sister town" : " sister towns") : "") + ": " + an + " links distant markets";
                default: return name;
            }
        }

        private static string Article(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a";
            char c = char.ToUpperInvariant(name[0]);
            return c == 'A' || c == 'E' || c == 'I' || c == 'O' || c == 'U' ? "an" : "a";
        }

        /// <summary>Formats hundredths as a fixed two-decimal number without culture dependence (42 -> "0.42").</summary>
        private static string Fixed2(int hundredths)
        {
            int whole = hundredths / 100, frac = hundredths % 100;
            return whole + "." + (frac < 10 ? "0" : "") + frac;
        }
    }
}
