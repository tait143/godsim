using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Races;
using NUnit.Framework;

namespace GodSim.Tests.Races
{
    /// <summary>RaceLore: every race has complete, well-formed long-form lore.</summary>
    public class RaceLoreTests
    {
        private static readonly string[] OtherRaceStems = { "Human", "Elv", "Dwar", "Orc", "Saurian" };

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void EverySection_IsPresent(RaceId id)
        {
            Assert.IsTrue(RaceLore.Summary(id).Length >= 120, "summary");
            Assert.IsTrue(RaceLore.Summary(id).Length <= 400, "summary should stay a short paragraph");
            Assert.IsTrue(RaceLore.CreationMyth(id).Length >= 200, "creation myth");
            Assert.IsTrue(RaceLore.Values(id).Length >= 200, "values");
            Assert.IsTrue(RaceLore.Relations(id).Length >= 200, "relations");
            Assert.IsTrue(RaceLore.GodName(id).Length > 0, "god name");
            Assert.AreEqual(1, RaceLore.ParagraphCount(RaceLore.Summary(id)));
            Assert.AreEqual(1, RaceLore.ParagraphCount(RaceLore.CreationMyth(id)));
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Backstory_HasTwoToThreeSubstantialParagraphs(RaceId id)
        {
            string text = RaceLore.Backstory(id);
            int paragraphs = RaceLore.ParagraphCount(text);
            Assert.IsTrue(paragraphs >= 2 && paragraphs <= 3, id + " backstory has " + paragraphs + " paragraphs");
            foreach (var p in text.Split(new[] { RaceLore.ParagraphBreak }, System.StringSplitOptions.RemoveEmptyEntries))
                Assert.IsTrue(p.Length >= 250, id + " paragraph too short: " + p.Length);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Relations_MentionEveryOtherRace(RaceId id)
        {
            string text = RaceLore.Relations(id);
            for (int i = 0; i < OtherRaceStems.Length; i++)
                if (i != (int)id) Assert.IsTrue(text.Contains(OtherRaceStems[i]), id + " relations never mention " + (RaceId)i);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Text_IsPlainAscii_WithCleanSpacing(RaceId id)
        {
            string full = RaceLore.FullText(id);
            Assert.IsTrue(full.Length > 1500);
            foreach (char c in full) Assert.IsTrue(c == '\n' || (c >= ' ' && c <= '~'), id + " has non-ASCII/control char U+" + ((int)c).ToString("X4"));
            Assert.IsFalse(full.Contains("  "), id + " has a double space");
            Assert.IsFalse(full.Contains("\n\n\n"), id + " has a triple newline");
            Assert.IsFalse(full.Contains(" \n") || full.Contains("\n "), id + " has stray spaces around a newline");
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void FullText_ContainsEverySectionInOrder(RaceId id)
        {
            string full = RaceLore.FullText(id);
            int s = full.IndexOf(RaceLore.Summary(id), System.StringComparison.Ordinal);
            int b = full.IndexOf(RaceLore.Backstory(id), System.StringComparison.Ordinal);
            int m = full.IndexOf(RaceLore.CreationMyth(id), System.StringComparison.Ordinal);
            int v = full.IndexOf(RaceLore.Values(id), System.StringComparison.Ordinal);
            int r = full.IndexOf(RaceLore.Relations(id), System.StringComparison.Ordinal);
            Assert.IsTrue(s == 0 && s < b && b < m && m < v && v < r, "sections missing or out of order");
            Assert.IsTrue(full.Contains(RaceLore.GodName(id)));
        }

        [Test]
        public void Lore_IsDistinctPerRace()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < Counts.Races; i++)
            {
                var id = (RaceId)i;
                Assert.IsTrue(seen.Add(RaceLore.Summary(id)));
                Assert.IsTrue(seen.Add(RaceLore.Backstory(id)));
                Assert.IsTrue(seen.Add(RaceLore.CreationMyth(id)));
                Assert.IsTrue(seen.Add(RaceLore.Values(id)));
                Assert.IsTrue(seen.Add(RaceLore.Relations(id)));
                Assert.IsTrue(seen.Add(RaceLore.GodName(id)));
            }
        }

        [Test]
        public void UnknownRace_YieldsEmptyStrings()
        {
            var bad = (RaceId)200;
            Assert.AreEqual("", RaceLore.Summary(bad));
            Assert.AreEqual("", RaceLore.Backstory(bad));
            Assert.AreEqual("", RaceLore.CreationMyth(bad));
            Assert.AreEqual("", RaceLore.Values(bad));
            Assert.AreEqual("", RaceLore.Relations(bad));
            Assert.AreEqual("", RaceLore.GodName(bad));
            Assert.AreEqual("", RaceLore.FullText(bad));
        }

        [Test]
        public void ParagraphCount_HandlesEdgeCases()
        {
            Assert.AreEqual(0, RaceLore.ParagraphCount(null));
            Assert.AreEqual(0, RaceLore.ParagraphCount(""));
            Assert.AreEqual(0, RaceLore.ParagraphCount("\n\n  \n\n"));
            Assert.AreEqual(1, RaceLore.ParagraphCount("one"));
            Assert.AreEqual(2, RaceLore.ParagraphCount("one\n\ntwo\n\n"));
            Assert.AreEqual(3, RaceLore.ParagraphCount("a\n\nb\n\nc"));
        }
    }
}
