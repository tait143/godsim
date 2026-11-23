using System.Text;

namespace GodSim.Core
{
    /// <summary>Deterministic, race-flavored name generation from RaceDefinition.Names syllable tables.</summary>
    public static class NameGenerator
    {
        private static readonly string[] FallbackPrefix = { "Ka", "Mo", "Ri", "Ta", "El", "Va", "Do", "Sa" };
        private static readonly string[] FallbackSuffix = { "ran", "lia", "dor", "mir", "ka", "nos", "the", "vin" };
        private static readonly string[] FallbackPattern = { "{0} Tribe", "Kingdom of {0}", "{0} Dominion" };

        public static string PersonName(RaceDefinition race, Sex sex, Rng rng, Person parent = null)
        {
            var n = race?.Names;
            var pre = Pick(n?.PersonPrefixes, FallbackPrefix, rng);
            var suf = Pick(sex == Sex.Male ? n?.MaleSuffixes : n?.FemaleSuffixes, FallbackSuffix, rng);
            string given = Capitalize(pre + suf);
            string family = null;
            if (parent != null && !string.IsNullOrEmpty(parent.Name))
            {
                int sp = parent.Name.IndexOf(' ');
                if (sp > 0 && sp < parent.Name.Length - 1) family = parent.Name.Substring(sp + 1);
            }
            if (family == null && n?.FamilyNames != null && n.FamilyNames.Length > 0) family = n.FamilyNames[rng.Range(0, n.FamilyNames.Length)];
            return family != null ? given + " " + family : given;
        }

        public static string SettlementName(RaceDefinition race, Rng rng)
        {
            var n = race?.Names;
            return Capitalize(Pick(n?.SettlementPrefixes, FallbackPrefix, rng) + Pick(n?.SettlementSuffixes, FallbackSuffix, rng));
        }

        public static string CivilizationName(RaceDefinition race, Rng rng)
        {
            var n = race?.Names;
            string root = Capitalize(Pick(n?.SettlementPrefixes, FallbackPrefix, rng) + Pick(n?.SettlementSuffixes, FallbackSuffix, rng));
            string pattern = Pick(n?.CivilizationPatterns, FallbackPattern, rng);
            try { return string.Format(pattern, root); }
            catch (System.FormatException) { return root; } // defensive: content patterns are data
        }

        private static string Pick(string[] arr, string[] fallback, Rng rng)
        {
            var a = arr != null && arr.Length > 0 ? arr : fallback;
            return a[rng.Range(0, a.Length)] ?? "";
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Nameless";
            var sb = new StringBuilder(s.ToLowerInvariant());
            sb[0] = char.ToUpperInvariant(sb[0]);
            return sb.ToString();
        }
    }
}
