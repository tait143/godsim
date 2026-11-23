using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Races;
using NUnit.Framework;

namespace GodSim.Tests.Races
{
    /// <summary>Syllable tables and the names the contracts NameGenerator builds from them.</summary>
    public class RaceNameTests
    {
        private const int MaxNameLength = 32;

        // ------------------------------------------------------------------ table sizes & hygiene

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Tables_MeetMinimumSizes(RaceId id)
        {
            var n = RaceCatalog.Create(id).Names;
            Assert.IsNotNull(n);
            Assert.IsTrue(n.PersonPrefixes.Length >= 20, "person prefixes " + n.PersonPrefixes.Length);
            Assert.IsTrue(n.MaleSuffixes.Length >= 12, "male suffixes " + n.MaleSuffixes.Length);
            Assert.IsTrue(n.FemaleSuffixes.Length >= 12, "female suffixes " + n.FemaleSuffixes.Length);
            Assert.IsTrue(n.FamilyNames.Length >= 20, "family names " + n.FamilyNames.Length);
            Assert.IsTrue(n.SettlementPrefixes.Length >= 20, "settlement prefixes " + n.SettlementPrefixes.Length);
            Assert.IsTrue(n.SettlementSuffixes.Length >= 15, "settlement suffixes " + n.SettlementSuffixes.Length);
            Assert.IsTrue(n.CivilizationPatterns.Length >= 6, "civilization patterns " + n.CivilizationPatterns.Length);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Tables_HaveNoDuplicates_CaseInsensitive(RaceId id)
        {
            foreach (var table in Tables(RaceCatalog.Create(id).Names))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in table.Value) Assert.IsTrue(seen.Add(s), id + " " + table.Key + " has duplicate '" + s + "'");
            }
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Syllables_AreLowercaseAsciiLetters(RaceId id)
        {
            var n = RaceCatalog.Create(id).Names;
            foreach (var arr in new[] { n.PersonPrefixes, n.MaleSuffixes, n.FemaleSuffixes, n.SettlementPrefixes, n.SettlementSuffixes })
                foreach (var s in arr)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(s));
                    foreach (char c in s) Assert.IsTrue(c >= 'a' && c <= 'z', id + " syllable '" + s + "' must be lowercase a-z");
                }
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void FamilyNames_AreCapitalizedSingleWords(RaceId id)
        {
            // NameGenerator appends family names verbatim (no Capitalize), and children inherit the text
            // after the parent's first space, so each must be one capitalized word.
            foreach (var f in RaceCatalog.Create(id).Names.FamilyNames)
            {
                Assert.IsTrue(f.Length >= 3, "'" + f + "' too short");
                Assert.IsTrue(f[0] >= 'A' && f[0] <= 'Z', "'" + f + "' must start uppercase");
                for (int i = 1; i < f.Length; i++) Assert.IsTrue(f[i] >= 'a' && f[i] <= 'z', "'" + f + "' must be one word of letters");
            }
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void MaleAndFemaleSuffixes_AreDisjoint(RaceId id)
        {
            var n = RaceCatalog.Create(id).Names;
            var male = new HashSet<string>(n.MaleSuffixes, StringComparer.OrdinalIgnoreCase);
            foreach (var f in n.FemaleSuffixes) Assert.IsFalse(male.Contains(f), id + " suffix '" + f + "' is both male and female");
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void CivilizationPatterns_AreSafeFormatStrings(RaceId id)
        {
            foreach (var p in RaceCatalog.Create(id).Names.CivilizationPatterns)
            {
                int at = p.IndexOf("{0}", StringComparison.Ordinal);
                Assert.IsTrue(at >= 0, "'" + p + "' lacks {0}");
                Assert.AreEqual(-1, p.IndexOf("{0}", at + 3, StringComparison.Ordinal), "'" + p + "' uses {0} twice");
                Assert.AreEqual(2, CountChar(p, '{') + CountChar(p, '}'), "'" + p + "' has stray braces");
                Assert.AreEqual(p.Replace("{0}", "Root"), string.Format(p, "Root"));
                // WorldOps writes "The " + civ.Name into the chronicle, so a leading article would double up.
                Assert.IsFalse(p.StartsWith("The ", StringComparison.OrdinalIgnoreCase), "'" + p + "' must not start with an article");
                Assert.AreEqual(p.Trim(), p);
            }
        }

        // ------------------------------------------------------------------ worst-case lengths (deterministic)

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void WorstCaseNames_FitIn32Chars(RaceId id)
        {
            var n = RaceCatalog.Create(id).Names;
            int person = MaxLen(n.PersonPrefixes) + Math.Max(MaxLen(n.MaleSuffixes), MaxLen(n.FemaleSuffixes)) + 1 + MaxLen(n.FamilyNames);
            Assert.IsTrue(person <= MaxNameLength, id + " longest person name " + person);

            int root = MaxLen(n.SettlementPrefixes) + MaxLen(n.SettlementSuffixes);
            Assert.IsTrue(root <= MaxNameLength, id + " longest settlement name " + root);

            int civ = 0;
            foreach (var p in n.CivilizationPatterns) civ = Math.Max(civ, p.Length - 3 + root);
            Assert.IsTrue(civ <= MaxNameLength, id + " longest civilization name " + civ);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void EveryGivenAndSettlementCombination_IsPronounceable(RaceId id)
        {
            // Exhaustive: no letter tripled at a syllable seam ("garr" + "rog"), at least three letters.
            var n = RaceCatalog.Create(id).Names;
            foreach (var pre in n.PersonPrefixes)
            {
                foreach (var suf in n.MaleSuffixes) AssertPronounceable(pre + suf);
                foreach (var suf in n.FemaleSuffixes) AssertPronounceable(pre + suf);
            }
            foreach (var pre in n.SettlementPrefixes)
                foreach (var suf in n.SettlementSuffixes) AssertPronounceable(pre + suf);
            foreach (var f in n.FamilyNames) AssertPronounceable(f);
        }

        // ------------------------------------------------------------------ generated names (contracts NameGenerator)

        [TestCase(RaceId.Human, 11UL)]
        [TestCase(RaceId.Elf, 22UL)]
        [TestCase(RaceId.Dwarf, 33UL)]
        [TestCase(RaceId.Orc, 44UL)]
        [TestCase(RaceId.Saurian, 55UL)]
        public void PersonNames_AreNonEmpty_Short_AndVaried(RaceId id, ulong seed)
        {
            var race = RaceCatalog.Create(id);
            var rng = new Rng(seed);
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 200; i++)
            {
                string name = NameGenerator.PersonName(race, i % 2 == 0 ? Sex.Female : Sex.Male, rng);
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.IsTrue(name.Length <= MaxNameLength, "'" + name + "' is " + name.Length + " chars");
                Assert.IsTrue(char.IsUpper(name[0]), "'" + name + "'");
                int space = name.IndexOf(' ');
                Assert.IsTrue(space > 0 && char.IsUpper(name[space + 1]), "'" + name + "' must be 'Given Family'");
                distinct.Add(name);
            }
            Assert.IsTrue(distinct.Count >= 150, id + " produced only " + distinct.Count + " distinct names out of 200");
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void ChildNames_InheritTheParentsFamilyName(RaceId id)
        {
            var race = RaceCatalog.Create(id);
            var rng = new Rng(7);
            var parent = new Person { Name = NameGenerator.PersonName(race, Sex.Female, rng) };
            string family = parent.Name.Substring(parent.Name.IndexOf(' ') + 1);
            for (int i = 0; i < 20; i++)
            {
                string child = NameGenerator.PersonName(race, (Sex)(i & 1), rng, parent);
                Assert.IsTrue(child.EndsWith(" " + family, StringComparison.Ordinal), "'" + child + "' lost family '" + family + "'");
                Assert.IsTrue(child.Length <= MaxNameLength);
            }
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void SettlementAndCivilizationNames_AreNonEmpty_Short_AndVaried(RaceId id)
        {
            var race = RaceCatalog.Create(id);
            var rng = new Rng(1000UL + (ulong)id);
            var towns = new HashSet<string>(StringComparer.Ordinal);
            var civs = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 200; i++)
            {
                string town = NameGenerator.SettlementName(race, rng);
                string civ = NameGenerator.CivilizationName(race, rng);
                Assert.IsFalse(string.IsNullOrEmpty(town));
                Assert.IsFalse(string.IsNullOrEmpty(civ));
                Assert.AreNotEqual("Nameless", town);
                Assert.IsTrue(town.Length <= MaxNameLength, "'" + town + "'");
                Assert.IsTrue(civ.Length <= MaxNameLength, "'" + civ + "'");
                Assert.IsFalse(civ.Contains("{") || civ.Contains("}"), "'" + civ + "'");
                towns.Add(town);
                civs.Add(civ);
            }
            Assert.IsTrue(towns.Count >= 100, id + " produced only " + towns.Count + " distinct settlement names");
            Assert.IsTrue(civs.Count >= 100, id + " produced only " + civs.Count + " distinct civilization names");
        }

        [Test]
        public void Names_AreDeterministicForASeed()
        {
            foreach (var race in RaceCatalog.CreateAll())
            {
                var a = new Rng(99);
                var b = new Rng(99);
                for (int i = 0; i < 50; i++)
                {
                    Assert.AreEqual(NameGenerator.PersonName(race, Sex.Male, a), NameGenerator.PersonName(race, Sex.Male, b));
                    Assert.AreEqual(NameGenerator.SettlementName(race, a), NameGenerator.SettlementName(race, b));
                    Assert.AreEqual(NameGenerator.CivilizationName(race, a), NameGenerator.CivilizationName(race, b));
                }
            }
        }

        // ------------------------------------------------------------------ each race sounds distinct

        [Test]
        public void Races_ShareNoPrefixesOrFamilyNames()
        {
            var all = RaceCatalog.CreateAll();
            for (int i = 0; i < all.Length; i++)
                for (int j = i + 1; j < all.Length; j++)
                {
                    AssertDisjoint(all[i].Names.PersonPrefixes, all[j].Names.PersonPrefixes, all[i].Id + "/" + all[j].Id + " person prefix");
                    AssertDisjoint(all[i].Names.SettlementPrefixes, all[j].Names.SettlementPrefixes, all[i].Id + "/" + all[j].Id + " settlement prefix");
                    AssertDisjoint(all[i].Names.FamilyNames, all[j].Names.FamilyNames, all[i].Id + "/" + all[j].Id + " family name");
                    AssertDisjoint(all[i].Names.CivilizationPatterns, all[j].Names.CivilizationPatterns, all[i].Id + "/" + all[j].Id + " civ pattern");
                }
        }

        [Test]
        public void ElvishNames_FlowWithMoreVowels_ThanDwarvishOrOrcish()
        {
            double elf = VowelRatio(RaceId.Elf), dwarf = VowelRatio(RaceId.Dwarf), orc = VowelRatio(RaceId.Orc);
            Assert.IsTrue(elf > dwarf + 0.05, "elf " + elf + " vs dwarf " + dwarf);
            Assert.IsTrue(elf > orc + 0.05, "elf " + elf + " vs orc " + orc);
        }

        [Test]
        public void ElvishNames_AvoidHarshLetters()
        {
            foreach (var given in GivenNames(RaceId.Elf))
                Assert.IsTrue(given.IndexOfAny(new[] { 'k', 'x', 'z', 'q', 'g' }) < 0, "'" + given + "' is too harsh for an Elf");
        }

        [Test]
        public void SaurianNames_AreSibilant()
        {
            var names = GivenNames(RaceId.Saurian);
            int hits = 0;
            foreach (var g in names) if (g.IndexOfAny(new[] { 's', 'x', 'z', 'q' }) >= 0 || g.Contains("th")) hits++;
            Assert.IsTrue(hits >= names.Count * 0.95, hits + " of " + names.Count + " Saurian names hiss");
        }

        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        public void DwarvishAndOrcishNames_UseHardConsonants(RaceId id)
        {
            var names = GivenNames(id);
            int hits = 0;
            foreach (var g in names) if (g.IndexOfAny(new[] { 'k', 'g', 'd', 'b', 'z' }) >= 0) hits++;
            Assert.IsTrue(hits >= names.Count * 0.9, hits + " of " + names.Count + " " + id + " names");
        }

        [Test]
        public void OrcishNames_AreGuttural()
        {
            var names = GivenNames(RaceId.Orc);
            int hits = 0;
            foreach (var g in names) if (g.IndexOfAny(new[] { 'g', 'k', 'z' }) >= 0 || g.Contains("gh") || g.Contains("kh")) hits++;
            Assert.IsTrue(hits >= names.Count * 0.9, hits + " of " + names.Count + " Orc names");
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerable<KeyValuePair<string, string[]>> Tables(NameParts n)
        {
            yield return new KeyValuePair<string, string[]>("PersonPrefixes", n.PersonPrefixes);
            yield return new KeyValuePair<string, string[]>("MaleSuffixes", n.MaleSuffixes);
            yield return new KeyValuePair<string, string[]>("FemaleSuffixes", n.FemaleSuffixes);
            yield return new KeyValuePair<string, string[]>("FamilyNames", n.FamilyNames);
            yield return new KeyValuePair<string, string[]>("SettlementPrefixes", n.SettlementPrefixes);
            yield return new KeyValuePair<string, string[]>("SettlementSuffixes", n.SettlementSuffixes);
            yield return new KeyValuePair<string, string[]>("CivilizationPatterns", n.CivilizationPatterns);
        }

        /// <summary>Every lowercase prefix+suffix given name the race can produce (both sexes).</summary>
        private static List<string> GivenNames(RaceId id)
        {
            var n = RaceCatalog.Create(id).Names;
            var list = new List<string>();
            foreach (var pre in n.PersonPrefixes)
            {
                foreach (var suf in n.MaleSuffixes) list.Add((pre + suf).ToLowerInvariant());
                foreach (var suf in n.FemaleSuffixes) list.Add((pre + suf).ToLowerInvariant());
            }
            return list;
        }

        private static double VowelRatio(RaceId id)
        {
            int vowels = 0, letters = 0;
            foreach (var g in GivenNames(id))
                foreach (char c in g)
                {
                    letters++;
                    if ("aeiouy".IndexOf(c) >= 0) vowels++;
                }
            return letters == 0 ? 0 : vowels / (double)letters;
        }

        private static void AssertPronounceable(string s)
        {
            Assert.IsTrue(s.Length >= 3, "'" + s + "' too short");
            string lower = s.ToLowerInvariant();
            for (int i = 2; i < lower.Length; i++)
                Assert.IsFalse(lower[i] == lower[i - 1] && lower[i] == lower[i - 2], "'" + s + "' triples a letter");
        }

        private static void AssertDisjoint(string[] a, string[] b, string what)
        {
            var set = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
            foreach (var s in b) Assert.IsFalse(set.Contains(s), what + " '" + s + "' is shared");
        }

        private static int MaxLen(string[] arr)
        {
            int m = 0;
            foreach (var s in arr) if (s.Length > m) m = s.Length;
            return m;
        }

        private static int CountChar(string s, char c)
        {
            int n = 0;
            foreach (char x in s) if (x == c) n++;
            return n;
        }
    }
}
