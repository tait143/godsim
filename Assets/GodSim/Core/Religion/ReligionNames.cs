using System.Text;

namespace GodSim.Core.Religion
{
    /// <summary>
    /// Pure, deterministic faith names built from the race's syllable tables (<see cref="RaceDefinition.Names"/>).
    /// The name is never stored: it is recomputed from (civ id, world seed, race) whenever it is needed.
    /// </summary>
    public static class ReligionNames
    {
        private const int MaxRootLength = 24;

        private static readonly string[] Patterns =
        {
            "The Way of {0}", "Church of {0}", "Followers of {0}", "The {0} Covenant", "Mysteries of {0}",
            "The {0} Communion", "Children of {0}", "The Path of {0}", "The Order of {0}", "The Faith of {0}",
            "The {0} Rite", "Disciples of {0}"
        };

        private static readonly string[] FallbackPrefix = { "Ka", "Mo", "Ri", "Ta", "El", "Va", "Do", "Sa" };
        private static readonly string[] FallbackSuffix = { "ran", "lia", "dor", "mir", "ka", "nos", "the", "vin" };

        /// <summary>
        /// The faith name of a civilization, e.g. "The Way of Kaldor". Same inputs always give the same name.
        /// <paramref name="race"/> may be null (built-in syllables are used).
        /// </summary>
        public static string FaithName(int civId, ulong seed, RaceDefinition race)
        {
            ulong h = Hash(civId, seed, race);
            string root = Root(h, race != null ? race.Names : null);
            string pattern = Patterns[(int)((h >> 48) % (ulong)Patterns.Length)];
            return string.Format(pattern, root);
        }

        /// <summary>The prophet-like root of a faith name ("Kaldor"), from person syllables, falling back to place syllables.</summary>
        public static string Root(int civId, ulong seed, RaceDefinition race)
        {
            return Root(Hash(civId, seed, race), race != null ? race.Names : null);
        }

        private static ulong Hash(int civId, ulong seed, RaceDefinition race) =>
            Hashing.Combine(Hashing.Combine(seed ^ 0x2E11610BUL, unchecked((ulong)(uint)civId)), Hashing.Fnv1a64(race != null ? race.Name : ""));

        private static string Root(ulong h, NameParts names)
        {
            string[] pre = NonEmpty(names != null ? names.PersonPrefixes : null);
            bool male = ((h >> 40) & 1UL) == 0;
            string[] suf = NonEmpty(names != null ? (male ? names.MaleSuffixes : names.FemaleSuffixes) : null);
            if (pre == null || suf == null)
            {
                pre = NonEmpty(names != null ? names.SettlementPrefixes : null) ?? FallbackPrefix;
                suf = NonEmpty(names != null ? names.SettlementSuffixes : null) ?? FallbackSuffix;
            }
            string a = pre[(int)((h & 0xFFFFUL) % (ulong)pre.Length)] ?? "";
            string b = suf[(int)(((h >> 16) & 0xFFFFUL) % (ulong)suf.Length)] ?? "";
            return Capitalize(Clean(a + b));
        }

        private static string[] NonEmpty(string[] arr) => arr != null && arr.Length > 0 ? arr : null;

        /// <summary>Keeps letters, apostrophes and hyphens only (names are content data), bounded length.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length && sb.Length < MaxRootLength; i++)
            {
                char c = s[i];
                if (char.IsLetter(c) || c == '\'' || c == '-') sb.Append(c);
            }
            return sb.ToString();
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
