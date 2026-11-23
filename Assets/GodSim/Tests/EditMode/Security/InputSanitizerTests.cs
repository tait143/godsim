using GodSim.Core;
using GodSim.Core.Security;
using NUnit.Framework;

namespace GodSim.Tests.Security
{
    public class InputSanitizerTests
    {
        private static void AssertNoLoneSurrogates(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i])) { Assert.IsTrue(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]), "split surrogate pair in: " + s); i++; }
                else Assert.IsFalse(char.IsLowSurrogate(s[i]), "lone low surrogate in: " + s);
            }
        }

        [Test]
        public void PlainNames_ArePreserved()
        {
            Assert.AreEqual("Oakford", InputSanitizer.SanitizeName("Oakford"));
            Assert.AreEqual("Anne-Marie O'Neil", InputSanitizer.SanitizeName("Anne-Marie O'Neil"));
            Assert.AreEqual("Town 42", InputSanitizer.SanitizeName("Town 42"));
        }

        [Test]
        public void BidiAndControlCharacters_AreStripped()
        {
            Assert.AreEqual("Alice", InputSanitizer.SanitizeName("Ali‮ce"));
            Assert.AreEqual("evilexe", InputSanitizer.SanitizeName("‪evil‫‬‭⁦⁧⁨⁩exe"));
            Assert.AreEqual("Bobby", InputSanitizer.SanitizeName("Bob\u0000\u0007\u001B\u007Fby"));
            Assert.AreEqual("a b", InputSanitizer.SanitizeName("a\u0085b"), "NEL is whitespace, not content");
            Assert.AreEqual("Zero Width", InputSanitizer.SanitizeName("Zero​‌‍﻿ Width⁠"));
            string result = InputSanitizer.SanitizeName("a‮b⁦c\u0000d");
            foreach (char c in result) Assert.IsFalse(char.IsControl(c) || (c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩'));
        }

        [Test]
        public void Whitespace_IsCollapsedAndTrimmed()
        {
            Assert.AreEqual("multiple spaces here", InputSanitizer.SanitizeName("   multiple    spaces \t\n here  "));
            Assert.AreEqual("Tab Name", InputSanitizer.SanitizeName("Tab\tName"));
            Assert.AreEqual("Ideo Space", InputSanitizer.SanitizeName("Ideo　 Space"));
        }

        [Test]
        public void Accents_AreKept_AndNormalizedToNfc()
        {
            Assert.AreEqual("Zoë", InputSanitizer.SanitizeName("Zoë"));
            Assert.AreEqual("Zoë", InputSanitizer.SanitizeName("Zoë"), "decomposed input composes to NFC");
            Assert.AreEqual("Crème Brûlée", InputSanitizer.SanitizeName("Crème Brûlée"));
            Assert.AreEqual("Åsa", InputSanitizer.SanitizeName("A‍̊sa"), "a stripped joiner does not block composition");
            Assert.AreEqual("東京", InputSanitizer.SanitizeName("東京"));
        }

        [Test]
        public void CombiningMarkStacks_AreCapped()
        {
            string zalgo = "Z" + new string('́', 40) + "a" + new string('̖', 40);
            string s = InputSanitizer.SanitizeName(zalgo);
            Assert.IsTrue((s.Length) <= (2 + 2 * InputSanitizer.MaxCombiningMarksPerBase));
            Assert.IsTrue(s.Contains("a"));
        }

        [Test]
        public void SymbolsAndEmoji_AreRemoved()
        {
            Assert.AreEqual("CoolName", InputSanitizer.SanitizeName("Cool😀Name"));
            Assert.AreEqual("scriptalert1script", InputSanitizer.SanitizeName("<script>alert(1)</script>"));
            Assert.AreEqual("etcpasswd", InputSanitizer.SanitizeName("../etc/passwd"));
        }

        [Test]
        public void EmptyOrInvisibleInput_ReturnsFallback()
        {
            Assert.AreEqual("Unnamed", InputSanitizer.SanitizeName(null));
            Assert.AreEqual("Unnamed", InputSanitizer.SanitizeName(""));
            Assert.AreEqual("Unnamed", InputSanitizer.SanitizeName("   \t "));
            Assert.AreEqual("Unnamed", InputSanitizer.SanitizeName("ㅤㅤᅟ"), "Hangul fillers are blank letters");
            Assert.AreEqual("Unnamed", InputSanitizer.SanitizeName("‮​"));
            Assert.AreEqual("Unnamed", InputSanitizer.SanitizeName("--- ''' ---"), "punctuation alone is not a name");
            Assert.AreEqual("Nobody", InputSanitizer.SanitizeName("\u0000", 32, "Nobody"));
            Assert.AreEqual("Unnamed", InputSanitizer.SanitizeName("\u0000", 32, null));
        }

        [Test]
        public void LoneSurrogates_AreRemoved_WithoutThrowing()
        {
            string s = InputSanitizer.SanitizeName("ab\uD800cd\uDC00ef\uD83D");
            Assert.AreEqual("abcdef", s);
        }

        [Test]
        public void Truncation_NeverSplitsSurrogatePairs()
        {
            // U+10400 DESERET CAPITAL LETTER LONG I is a letter outside the BMP (2 UTF-16 units each).
            string deseret = "𐐀";
            string input = deseret + deseret + deseret + deseret;
            for (int max = 1; max <= 9; max++)
            {
                string s = InputSanitizer.SanitizeName(input, max, "x");
                Assert.IsTrue((s.Length) <= (max));
                AssertNoLoneSurrogates(s);
            }
            Assert.AreEqual(deseret + deseret, InputSanitizer.SanitizeName(input, 5, "x"));
        }

        [Test]
        public void Truncation_KeepsBaseAndMarksTogether()
        {
            // "e" + combining dot below + combining caron has no single precomposed form.
            string cluster = "ẹ̌";
            string s = InputSanitizer.SanitizeName("ab" + cluster + "cd", 4, "x");
            Assert.AreEqual("ab", s.Substring(0, 2));
            Assert.IsTrue((s.Length) <= (4));
            Assert.IsFalse(s.Length == 3, "the base letter must not be separated from its marks");
        }

        [Test]
        public void Length_IsRespected()
        {
            Assert.AreEqual("abcde", InputSanitizer.SanitizeName("abcdefghij", 5));
            Assert.AreEqual(32, InputSanitizer.SanitizeName(new string('x', 1000)).Length);
            Assert.AreEqual("ab", InputSanitizer.SanitizeName("ab cd", 3), "no trailing space after truncation");
            Assert.AreEqual("a", InputSanitizer.SanitizeName("abc", 0), "maxLength clamps to at least 1");
            Assert.IsTrue((InputSanitizer.SanitizeName(new string('y', 5000), 100000).Length) <= (InputSanitizer.MaxNameLengthLimit));
        }

        [Test]
        public void HugeInput_IsBoundedAndFast()
        {
            string huge = new string('‮', 5000000) + "Name";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string s = InputSanitizer.SanitizeName(huge);
            sw.Stop();
            Assert.AreEqual("Unnamed", s, "only a bounded prefix is examined");
            Assert.IsTrue((sw.ElapsedMilliseconds) < (1000));
            Assert.AreEqual(32, InputSanitizer.SanitizeName(new string('n', 5000000)).Length);
        }

        [TestCase("save1", true)]
        [TestCase("My Save_2-final", true)]
        [TestCase("_quick", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("..", false)]
        [TestCase("../evil", false)]
        [TestCase("a/b", false)]
        [TestCase("a\\b", false)]
        [TestCase("C:", false)]
        [TestCase("save.dat", false)]
        [TestCase(" lead", false)]
        [TestCase("trail ", false)]
        [TestCase("-flag", false)]
        [TestCase("CON", false)]
        [TestCase("nul", false)]
        [TestCase("Com1", false)]
        [TestCase("café", false)]
        [TestCase("tab\tname", false)]
        [TestCase("abcdefghijabcdefghijabcdefghijab", true)]
        [TestCase("abcdefghijabcdefghijabcdefghijabc", false)]
        public void IsValidSlotName_Works(string name, bool expected)
        {
            Assert.AreEqual(expected, InputSanitizer.IsValidSlotName(name));
        }

        [Test]
        public void SanitizeSlotName_ProducesValidNames()
        {
            string[] inputs = { "../../etc/passwd", "CON", "   ", null, "My Save", "‮evil", new string('a', 500), "--x", "a:b|c" };
            foreach (var input in inputs)
                Assert.IsTrue(InputSanitizer.IsValidSlotName(InputSanitizer.SanitizeSlotName(input)), "input: " + input);
            Assert.AreEqual("My_Save", InputSanitizer.SanitizeSlotName("My Save"));
            Assert.AreEqual("save", InputSanitizer.SanitizeSlotName("..."));
        }

        [Test]
        public void SeedText_NumbersParse_TextHashes()
        {
            Assert.AreEqual(12345UL, InputSanitizer.SanitizeSeedText("12345"));
            Assert.AreEqual(12345UL, InputSanitizer.SanitizeSeedText("  12345 "));
            Assert.AreEqual(ulong.MaxValue, InputSanitizer.SanitizeSeedText("18446744073709551615"));
            Assert.AreEqual(Hashing.Fnv1a64("18446744073709551616"), InputSanitizer.SanitizeSeedText("18446744073709551616"), "overflow falls back to hashing");
            Assert.AreEqual(Hashing.Fnv1a64("Dragon Isles"), InputSanitizer.SanitizeSeedText("Dragon Isles"));
            Assert.AreEqual(InputSanitizer.SanitizeSeedText("Dragon Isles"), InputSanitizer.SanitizeSeedText(" Dragon ‮  Isles\t"));
            Assert.AreEqual(InputSanitizer.SanitizeSeedText("Zoë"), InputSanitizer.SanitizeSeedText("Zoë"));
            Assert.AreEqual(Hashing.Fnv1a64(""), InputSanitizer.SanitizeSeedText(null));
            Assert.AreEqual(Hashing.Fnv1a64(""), InputSanitizer.SanitizeSeedText("   "));
            Assert.AreEqual(InputSanitizer.SanitizeSeedText(new string('s', 256)), InputSanitizer.SanitizeSeedText(new string('s', 100000)));
        }

        [Test]
        public void ClampConfig_SanitizesAndCapsMemory()
        {
            var c = new SimConfig { MapWidth = 100000, MapHeight = -5, MaxCommandsPerTick = 1000, StartingRaces = new RaceId[16], GrowthMultiplier = float.NaN };
            InputSanitizer.ClampConfig(c);
            Assert.IsTrue(((long)c.MapWidth * c.MapHeight * c.StartingRaces.Length) <= (InputSanitizer.MaxTileRaceBudget));
            Assert.IsTrue((c.MapWidth) >= (WorldMap.MinSize * 4));
            Assert.IsTrue((c.MapHeight) >= (WorldMap.MinSize * 4));
            Assert.IsTrue((c.MapWidth) <= (WorldMap.MaxSize));
            Assert.AreEqual(64, c.MaxCommandsPerTick);
            Assert.AreEqual(1f, c.GrowthMultiplier);

            var big = InputSanitizer.ClampConfig(new SimConfig { MapWidth = 1024, MapHeight = 1024 });
            Assert.IsTrue(((long)big.MapWidth * big.MapHeight * big.StartingRaces.Length) <= (InputSanitizer.MaxTileRaceBudget));
            Assert.AreEqual(big.MapWidth, big.MapHeight, "aspect ratio preserved");

            var single = InputSanitizer.ClampConfig(new SimConfig { MapWidth = 1024, MapHeight = 1024, StartingRaces = new[] { RaceId.Elf } });
            Assert.AreEqual(1024, single.MapWidth, "a single race may use the largest map");

            var crowd = InputSanitizer.ClampConfig(new SimConfig { MaxPopulation = 100, InitialPopulationPerRace = 200, StartingRaces = new RaceId[16] });
            Assert.IsTrue((crowd.InitialPopulationPerRace * crowd.StartingRaces.Length) <= (100 + 2 * 16));

            Assert.IsNotNull(InputSanitizer.ClampConfig(null));
            var def = InputSanitizer.ClampConfig(new SimConfig());
            Assert.AreEqual(192, def.MapWidth, "the default config is untouched");
        }

        [Test]
        public void IsSafeDisplayText_DetectsInjection()
        {
            Assert.IsTrue(InputSanitizer.IsSafeDisplayText("Grand Café", false));
            Assert.IsTrue(InputSanitizer.IsSafeDisplayText("line1\nline2", true));
            Assert.IsFalse(InputSanitizer.IsSafeDisplayText("line1\nline2", false));
            Assert.IsFalse(InputSanitizer.IsSafeDisplayText("evil‮txt", true));
            Assert.IsFalse(InputSanitizer.IsSafeDisplayText("x⁦y", true));
            Assert.IsFalse(InputSanitizer.IsSafeDisplayText("bad\uD800", true));
            Assert.IsFalse(InputSanitizer.IsSafeDisplayText(null, true));
            Assert.IsFalse(InputSanitizer.IsSafeDisplayText("beam\uD834\uDD73", true), "astral format characters (U+1D173)");
            Assert.IsFalse(InputSanitizer.IsSafeDisplayText("tag\uDB40\uDC41", true), "tag characters (U+E0041)");
            Assert.IsTrue(InputSanitizer.IsSafeDisplayText("\uD801\uDC00", false), "astral letters are fine");
        }
    }
}
