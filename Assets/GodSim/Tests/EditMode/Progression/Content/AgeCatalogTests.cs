using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Progression;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Progression.Content
{
    public class AgeCatalogTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = new GameDatabase();
            AgeCatalog.Register(_db);
            TechCatalog.Register(_db);
            TestDb.FillMissing(_db);
            _db.Freeze();
        }

        [Test]
        public void RegistersAllTenAgesWithMatchingIds()
        {
            Assert.AreEqual(10, Counts.Ages);
            for (int i = 0; i < Counts.Ages; i++)
            {
                var def = _db.Age((AgeId)i);
                Assert.AreEqual((AgeId)i, def.Id);
                Assert.IsTrue(def.Name.EndsWith(" Age", StringComparison.Ordinal), def.Name);
                Assert.IsTrue(def.Name.StartsWith(((AgeId)i).ToString(), StringComparison.Ordinal), def.Name);
            }
            Assert.AreEqual("Dawn Age", _db.Age(AgeId.Dawn).Name);
            Assert.AreEqual("Bronze Age", _db.Age(AgeId.Bronze).Name);
        }

        [Test]
        public void DescriptionsHaveTwoToThreeSentences()
        {
            foreach (var age in EnumUtil.AllAges)
            {
                string d = _db.Age(age).Description;
                int sentences = 0;
                for (int i = 0; i < d.Length; i++) if (d[i] == '.' || d[i] == '!' || d[i] == '?') sentences++;
                Assert.That(sentences >= 2 && sentences <= 3, age + ": " + d);
            }
        }

        // Research thresholds re-scaled by the 2026-11-19 balance pass (was 400 / 1,400 / 3,200 / 6,200 / 10,500 /
        // 16,500 / 25,000 / 36,000 / 50,000; ages passed in 5-20 years). See docs/BALANCE_PASS.md.
        [TestCase(AgeId.Dawn, 0f, 0, 1, 1f)]
        [TestCase(AgeId.Bronze, 800f, 25, 1, 1.4f)]
        [TestCase(AgeId.Iron, 3800f, 50, 1, 2f)]
        [TestCase(AgeId.Classical, 12800f, 90, 2, 2.6f)]
        [TestCase(AgeId.Medieval, 36800f, 160, 2, 3.5f)]
        [TestCase(AgeId.Renaissance, 88400f, 260, 3, 5f)]
        [TestCase(AgeId.Industrial, 184400f, 400, 3, 8f)]
        [TestCase(AgeId.Modern, 354400f, 600, 4, 14f)]
        [TestCase(AgeId.Information, 618400f, 850, 4, 22f)]
        [TestCase(AgeId.Ascension, 1010400f, 1200, 5, 35f)]
        public void AdvancementNumbersMatchDesign(AgeId age, float threshold, int minPop, int minSettlements, float soldier)
        {
            var def = _db.Age(age);
            Assert.AreEqual(threshold, def.ResearchThreshold, 1e-3f);
            Assert.AreEqual(minPop, def.MinPopulation);
            Assert.AreEqual(minSettlements, def.MinSettlements);
            Assert.AreEqual(soldier, def.BaseSoldierStrength, 1e-4f);
        }

        [Test]
        public void ThresholdsAndRequirementsStrictlyIncrease()
        {
            for (int i = 1; i < Counts.Ages; i++)
            {
                var prev = _db.Age((AgeId)(i - 1));
                var cur = _db.Age((AgeId)i);
                Assert.IsTrue(cur.ResearchThreshold > prev.ResearchThreshold, "threshold " + cur.Id);
                Assert.IsTrue(cur.MinPopulation > prev.MinPopulation, "population " + cur.Id);
                Assert.IsTrue(cur.MinSettlements >= prev.MinSettlements, "settlements " + cur.Id);
                Assert.IsTrue(cur.BaseSoldierStrength > prev.BaseSoldierStrength, "soldier " + cur.Id);
            }
        }

        [Test]
        public void BonusesIncreaseWithinDesignRanges()
        {
            var dawn = _db.Age(AgeId.Dawn);
            Assert.AreEqual(0f, dawn.ProductionBonus);
            Assert.AreEqual(0f, dawn.ResearchBonus);
            Assert.AreEqual(0f, dawn.LifespanBonusYears);
            Assert.AreEqual(0f, dawn.HealthBonus);
            for (int i = 1; i < Counts.Ages; i++)
            {
                var prev = _db.Age((AgeId)(i - 1));
                var cur = _db.Age((AgeId)i);
                Assert.IsTrue(cur.ProductionBonus > prev.ProductionBonus, "production " + cur.Id);
                Assert.IsTrue(cur.ResearchBonus > prev.ResearchBonus, "research " + cur.Id);
                Assert.IsTrue(cur.LifespanBonusYears > prev.LifespanBonusYears, "lifespan " + cur.Id);
                Assert.IsTrue(cur.HealthBonus > prev.HealthBonus, "health " + cur.Id);
            }
            var top = _db.Age(AgeId.Ascension);
            Assert.That(top.ProductionBonus >= 0.8f && top.ProductionBonus <= 1f, "production cap");
            Assert.That(top.ResearchBonus >= 0.8f && top.ResearchBonus <= 1f, "research cap");
            Assert.AreEqual(25f, top.LifespanBonusYears, 1e-4f);
            Assert.AreEqual(0.3f, top.HealthBonus, 1e-4f);
        }

        [Test]
        public void RequiredTechsAreAboutSixtyPercentOfPreviousGeneralTechs()
        {
            Assert.AreEqual(0, _db.Age(AgeId.Dawn).RequiredTechsFromPreviousAge);
            for (int i = 1; i < Counts.Ages; i++)
            {
                var age = (AgeId)i;
                int required = _db.Age(age).RequiredTechsFromPreviousAge;
                int general = 0;
                foreach (var t in _db.Techs) if (t.Age == age - 1 && t.AllowedRaces == RaceMask.All) general++;
                Assert.AreEqual(general, TechCatalog.GeneralTechCount(age - 1));
                Assert.That(required >= 1 && required <= general, age + " requires " + required + " of " + general);
                float fraction = required / (float)general;
                Assert.That(fraction >= 0.5f && fraction <= 0.7f, age + " fraction " + fraction);
                Assert.AreEqual(AgeCatalog.RequiredTechsFor(age), required);
            }
        }

        [TestCase(AgeId.Dawn, "thatch")]
        [TestCase(AgeId.Bronze, "mudbrick")]
        [TestCase(AgeId.Iron, "fieldstone")]
        [TestCase(AgeId.Classical, "marble")]
        [TestCase(AgeId.Classical, "column")]
        [TestCase(AgeId.Medieval, "stone")]
        [TestCase(AgeId.Medieval, "timber")]
        [TestCase(AgeId.Renaissance, "terracotta")]
        [TestCase(AgeId.Renaissance, "dome")]
        [TestCase(AgeId.Industrial, "red brick")]
        [TestCase(AgeId.Industrial, "smokestack")]
        [TestCase(AgeId.Modern, "concrete")]
        [TestCase(AgeId.Modern, "steel")]
        [TestCase(AgeId.Information, "glass")]
        [TestCase(AgeId.Ascension, "luminous")]
        [TestCase(AgeId.Ascension, "gold")]
        public void ArchitectureNotesDescribeMaterials(AgeId age, string material)
        {
            string notes = _db.Age(age).ArchitectureNotes.ToLowerInvariant();
            Assert.IsTrue(notes.Contains(material), age + ": " + notes);
        }

        [Test]
        public void ThemeColorsAreOpaqueAndDistinct()
        {
            var seen = new HashSet<uint>();
            foreach (var age in EnumUtil.AllAges)
            {
                uint c = _db.Age(age).ThemeColor;
                Assert.AreEqual(0xFFu, c & 0xFFu, age + " alpha");
                Assert.IsTrue(seen.Add(c), age + " duplicate color");
            }
        }

        [Test]
        public void CreateDefinitionsReturnsFreshInstances()
        {
            var a = AgeCatalog.CreateDefinitions();
            var b = AgeCatalog.CreateDefinitions();
            Assert.AreEqual(Counts.Ages, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.IsFalse(ReferenceEquals(a[i], b[i]));
                Assert.AreEqual((AgeId)i, a[i].Id);
                Assert.AreEqual(a[i].ResearchThreshold, b[i].ResearchThreshold);
            }
        }

        [Test]
        public void RegisterRejectsNullAndFrozenDatabase()
        {
            Assert.Throws<ArgumentNullException>(() => AgeCatalog.Register(null));
            Assert.Throws<InvalidOperationException>(() => AgeCatalog.Register(_db));
        }
    }
}
