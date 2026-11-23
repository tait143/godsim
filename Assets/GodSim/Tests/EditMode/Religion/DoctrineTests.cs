using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Religion;
using NUnit.Framework;

namespace GodSim.Tests.Religion
{
    public class DoctrineTests
    {
        [Test]
        public void Catalog_Has16Doctrines_FourPerAxis_WithStableIdsAndKeys()
        {
            Assert.IsTrue(DoctrineCatalog.Count >= 16);
            var keys = new HashSet<string>();
            for (int i = 0; i < DoctrineCatalog.Count; i++)
            {
                var d = DoctrineCatalog.All[i];
                Assert.AreEqual(i, (int)d.Id, "catalog index must equal id");
                Assert.IsFalse(string.IsNullOrEmpty(d.Name));
                Assert.IsFalse(string.IsNullOrEmpty(d.Description));
                Assert.IsFalse(string.IsNullOrEmpty(d.Tenet));
                Assert.IsTrue(keys.Add(d.Key), "duplicate key " + d.Key);
                Assert.IsTrue(System.Math.Abs(d.FaithNudge) <= ReligionTuning.MaxNudgePerRun);
                Assert.IsTrue(System.Math.Abs(d.HappinessNudge) <= ReligionTuning.MaxNudgePerRun);
                Assert.IsTrue(System.Math.Abs(d.UnrestNudge) <= ReligionTuning.MaxNudgePerRun);
            }
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
                Assert.AreEqual(4, DoctrineCatalog.OfAxis((DoctrineAxis)a).Count);
            Assert.AreEqual("rel.doctrine.worship", DoctrineCatalog.MemoryKey(DoctrineAxis.Worship));
            Assert.AreEqual("rel.doctrine.view", DoctrineCatalog.MemoryKey(DoctrineAxis.View));
            Assert.AreEqual(DoctrineAxis.View, DoctrineCatalog.Get(DoctrineId.WrathfulJudge).Axis);
            Assert.IsTrue(DoctrineCatalog.Get(DoctrineId.MartialZeal).DefenseBonus > 0f);
            Assert.IsTrue(DoctrineCatalog.Get(DoctrineId.Monasticism).ResearchPerFaith > 0f);
            Assert.IsFalse(DoctrineCatalog.TryGet(999, out _));
            Assert.IsFalse(DoctrineCatalog.TryGet(-1, out _));
        }

        [Test]
        public void ViewsOppose_IsSymmetric()
        {
            Assert.IsTrue(DoctrineCatalog.ViewsOppose(DoctrineId.LovingParent, DoctrineId.WrathfulJudge));
            Assert.IsTrue(DoctrineCatalog.ViewsOppose(DoctrineId.WrathfulJudge, DoctrineId.LovingParent));
            Assert.IsTrue(DoctrineCatalog.ViewsOppose(DoctrineId.Trickster, DoctrineId.DistantWatcher));
            Assert.IsFalse(DoctrineCatalog.ViewsOppose(DoctrineId.LovingParent, DoctrineId.LovingParent));
            Assert.IsFalse(DoctrineCatalog.ViewsOppose(DoctrineId.LovingParent, DoctrineId.Trickster));
        }

        [Test]
        public void Selection_IsDeterministic()
        {
            var race = ReligionTestKit.Race(RaceId.Human, 0.5f, 0.45f, 0.6f, 0.8f);
            var god = new GodState { Alignment = 0.1f, PowersCast = 3 };
            var f1 = DoctrineSelector.Features(race, god, false);
            var f2 = DoctrineSelector.Features(race, god, false);
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
                for (int civ = 1; civ < 10; civ++)
                    Assert.AreEqual(DoctrineSelector.Select((DoctrineAxis)a, f1, 42UL, civ), DoctrineSelector.Select((DoctrineAxis)a, f2, 42UL, civ));
            Assert.AreEqual(DoctrineSelector.Jitter(7UL, 3, DoctrineId.Mercy), DoctrineSelector.Jitter(7UL, 3, DoctrineId.Mercy));
            Assert.IsTrue(System.Math.Abs(DoctrineSelector.Jitter(7UL, 3, DoctrineId.Mercy)) <= ReligionTuning.SelectionJitter);
        }

        [Test]
        public void Selection_IsPersonalityDriven()
        {
            var god = new GodState();
            var aggressive = DoctrineSelector.Features(ReligionTestKit.Race(RaceId.Orc, 0.3f, 1f, 0.1f, 0.1f), god, false);
            var curious = DoctrineSelector.Features(ReligionTestKit.Race(RaceId.Elf, 0.3f, 0.2f, 1f, 0.2f), god, false);
            var mercantile = DoctrineSelector.Features(ReligionTestKit.Race(RaceId.Human, 0.3f, 0.2f, 0.2f, 1f), god, false);
            var pious = DoctrineSelector.Features(ReligionTestKit.Race(RaceId.Saurian, 1f, 0.1f, 0.2f, 0f), god, false);
            for (ulong seed = 1; seed < 6; seed++)
                for (int civ = 1; civ < 20; civ++)
                {
                    Assert.AreEqual(DoctrineId.MartialZeal, DoctrineSelector.Select(DoctrineAxis.Ethos, aggressive, seed, civ));
                    Assert.AreEqual(DoctrineId.Monasticism, DoctrineSelector.Select(DoctrineAxis.Practice, curious, seed, civ));
                    Assert.AreEqual(DoctrineId.ProsperityGospel, DoctrineSelector.Select(DoctrineAxis.Ethos, mercantile, seed, civ));
                    Assert.AreEqual(DoctrineId.Pilgrimage, DoctrineSelector.Select(DoctrineAxis.Practice, pious, seed, civ));
                }
        }

        [Test]
        public void Selection_GodHistoryDrivesView()
        {
            var race = ReligionTestKit.Race(RaceId.Human, 0.5f, 0.5f, 0.5f, 0.5f);
            var wrathful = DoctrineSelector.Features(race, new GodState { Alignment = -0.8f, PowersCast = 10 }, false);
            var kind = DoctrineSelector.Features(race, new GodState { Alignment = 0.8f, PowersCast = 10 }, false);
            var silent = DoctrineSelector.Features(race, new GodState { Alignment = 0f, PowersCast = 0 }, false);
            for (int civ = 1; civ < 20; civ++)
            {
                Assert.AreEqual(DoctrineId.WrathfulJudge, DoctrineSelector.Select(DoctrineAxis.View, wrathful, 9UL, civ));
                Assert.AreEqual(DoctrineId.LovingParent, DoctrineSelector.Select(DoctrineAxis.View, kind, 9UL, civ));
                Assert.AreEqual(DoctrineId.DistantWatcher, DoctrineSelector.Select(DoctrineAxis.View, silent, 9UL, civ));
            }
        }

        [Test]
        public void Select_WithExclude_PicksADifferentDoctrine()
        {
            var f = DoctrineSelector.Features(null, null, false);
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
            {
                var best = DoctrineSelector.Select((DoctrineAxis)a, f, 3UL, 1);
                var other = DoctrineSelector.Select((DoctrineAxis)a, f, 3UL, 1, (int)best);
                Assert.AreNotEqual(best, other);
                Assert.AreEqual((DoctrineAxis)a, DoctrineCatalog.Get(other).Axis);
            }
        }

        [Test]
        public void Features_AreBoundedEvenForCorruptInputs()
        {
            var race = ReligionTestKit.Race(RaceId.Human, float.NaN, 5f, -3f, float.PositiveInfinity);
            var f = DoctrineSelector.Features(race, new GodState { Alignment = float.NaN, PowersCast = -5 }, true);
            Assert.AreEqual(DoctrineSelector.TraitCount, f.Length);
            foreach (float v in f) Assert.IsTrue(v >= 0f && v <= 1f, "feature out of range: " + v);
        }

        [Test]
        public void ViewConflict_MatchesGodAlignment()
        {
            Assert.AreEqual(1f, ReligionSystem.ViewConflict(DoctrineId.LovingParent, -1f, 0), 1e-5f);
            Assert.AreEqual(0f, ReligionSystem.ViewConflict(DoctrineId.LovingParent, 1f, 0), 1e-5f);
            Assert.AreEqual(1f, ReligionSystem.ViewConflict(DoctrineId.WrathfulJudge, 1f, 0), 1e-5f);
            Assert.AreEqual(0f, ReligionSystem.ViewConflict(DoctrineId.WrathfulJudge, -1f, 0), 1e-5f);
            Assert.IsTrue(ReligionSystem.ViewConflict(DoctrineId.DistantWatcher, 0f, 20) > 0.9f);
            Assert.AreEqual(0f, ReligionSystem.ViewConflict(DoctrineId.DistantWatcher, 0f, 0), 1e-5f);
            Assert.AreEqual(0f, ReligionSystem.ViewConflict(DoctrineId.Mercy, -1f, 50), 1e-5f);
        }

        [Test]
        public void FaithName_IsPureDeterministicAndRaceFlavoured()
        {
            var race = ReligionTestKit.Race(RaceId.Dwarf, 0.5f, 0.5f, 0.5f, 0.5f);
            string a = ReligionNames.FaithName(3, 1234UL, race);
            Assert.AreEqual(a, ReligionNames.FaithName(3, 1234UL, race));
            Assert.IsFalse(string.IsNullOrEmpty(a));
            string root = ReligionNames.Root(3, 1234UL, race);
            Assert.IsTrue(a.Contains(root));
            Assert.IsTrue(root.StartsWith("Kal") || root.StartsWith("Ves") || root.StartsWith("Oru"), root);

            var distinct = new HashSet<string>();
            for (int civ = 1; civ <= 12; civ++) distinct.Add(ReligionNames.FaithName(civ, 1234UL, race));
            Assert.IsTrue(distinct.Count > 3, "names should vary across civs");

            Assert.IsFalse(string.IsNullOrEmpty(ReligionNames.FaithName(1, 0UL, null)));
            var broken = new RaceDefinition { Name = null };
            broken.Names = null;
            Assert.IsFalse(string.IsNullOrEmpty(ReligionNames.FaithName(-5, ulong.MaxValue, broken)));
        }
    }
}
