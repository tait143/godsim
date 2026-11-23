using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.History;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.History
{
    public class HistoryRecorderTests
    {
        private GameDatabase _db;
        private SimContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.CreateStub();
            _ctx = TestWorlds.CreateContext(_db);
        }

        private void Advance(HistoryRecorder rec, int ticks) => TestWorlds.Run(rec, _ctx, ticks, false);

        [Test]
        public void SamplesAreRecordedEverySeason()
        {
            var s1 = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(16, 16), 6, out var c1);
            TestWorlds.AddSettlement(_ctx, RaceId.Dwarf, new Vec2i(44, 44), 4, out var c2);
            var rec = new HistoryRecorder();
            Assert.AreEqual(24, rec.TickInterval);
            TestWorlds.Run(rec, _ctx, 24 * 5);

            Assert.AreEqual(5, rec.World.Count);
            Assert.AreEqual(24, rec.World[0].Tick);
            Assert.AreEqual(120, rec.World[4].Tick);
            Assert.AreEqual(10, rec.World[4].Population);
            Assert.AreEqual(2, rec.World[4].Settlements);
            Assert.AreEqual(2, rec.World[4].Buildings, "two town centers");
            Assert.AreEqual(2, rec.World[4].LivingCivilizations);
            Assert.AreEqual(_ctx.State.God.Mana, rec.World[4].Mana, 1e-4f);

            Assert.IsTrue(rec.TryGetCiv(c1.Id, out var h1));
            Assert.AreEqual(5, h1.Count);
            Assert.AreEqual(6, h1.Latest.Population);
            Assert.AreEqual(1, h1.Latest.Settlements);
            Assert.AreEqual(1, h1.Latest.Buildings);
            Assert.AreEqual(s1.Happiness, h1.Latest.Happiness, 1e-4f);
            Assert.AreEqual(new[] { c1.Id, c2.Id }, new List<int>(rec.CivIds).ToArray());
        }

        [Test]
        public void SameTickIsNotSampledTwice()
        {
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 24);
            rec.Tick(_ctx);
            Assert.AreEqual(1, rec.World.Count);
        }

        [Test]
        public void BirthsAndDeathsAreCountedPerSample()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(20, 20), 6, out _);
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 24);
            Assert.AreEqual(0, rec.World[0].Births);

            _ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Female, 0f, -1, -1, true);
            _ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Male, 0f, -1, -1, true);
            var victim = _ctx.State.People.Get(s.ResidentIds[0]);
            _ctx.Ops.KillPerson(victim, DeathCause.Disease);
            _ctx.Ops.FlushRemovals();
            Advance(rec, 24);

            var w = rec.World[1];
            Assert.AreEqual(2, w.Births);
            Assert.AreEqual(1, w.Deaths);
            Assert.AreEqual(1, w.DeathsBy(DeathCause.Disease));
            Assert.AreEqual(0, w.DeathsBy(DeathCause.OldAge));
            Assert.AreEqual(7, w.Population);
            Assert.AreEqual(1, rec.Lifespans.Count);
            Assert.AreEqual(victim.AgeYears, rec.Lifespans.AverageFor(RaceId.Human), 1e-4f);

            Advance(rec, 24);
            Assert.AreEqual(0, rec.World[2].Births);
            Assert.AreEqual(0, rec.World[2].Deaths);
        }

        [Test]
        public void DownsamplingKeepsFirstAndLastAndBoundsMemory()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Elf, new Vec2i(20, 20), 4, out var civ);
            var rec = new HistoryRecorder(16, 100);
            TestWorlds.Run(rec, _ctx, 0);
            const int samples = 300;
            for (int k = 0; k < samples; k++)
            {
                _ctx.Ops.CreatePerson(s, RaceId.Elf, Sex.Female, 0f, -1, -1, true);
                Advance(rec, 24);
                Assert.LessOrEqual(rec.World.Count, 16);
            }
            Assert.AreEqual(samples, rec.SamplesTaken);
            Assert.Greater(rec.WorldStride, 1L);
            Assert.AreEqual(24, rec.World[0].Tick, "first sample kept");
            Assert.AreEqual(24L * samples, rec.World[rec.World.Count - 1].Tick, "latest sample kept");
            int births = 0;
            for (int i = 0; i < rec.World.Count; i++)
            {
                births += rec.World[i].Births;
                if (i > 0) Assert.Greater(rec.World[i].Tick, rec.World[i - 1].Tick);
            }
            Assert.AreEqual(samples, births, "downsampling folds additive counts into kept samples");

            Assert.IsTrue(rec.TryGetCiv(civ.Id, out var h));
            Assert.LessOrEqual(h.Count, 16);
            Assert.AreEqual(24, h.Samples[0].Tick);
            Assert.AreEqual(4 + 1, h.Samples[0].Population);
            Assert.AreEqual(4 + samples, h.Latest.Population);
            Assert.AreEqual(24L * samples, h.LastTick);
        }

        [Test]
        public void PerCivSeriesLengths()
        {
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(12, 12), 5, out var c1);
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 24 * 6);
            TestWorlds.AddSettlement(_ctx, RaceId.Orc, new Vec2i(48, 48), 3, out var c2);
            Advance(rec, 24 * 4);

            Assert.AreEqual(10, rec.Series(c1.Id, CivMetric.Population, 0).Length);
            Assert.AreEqual(10, rec.Series(c1.Id, CivMetric.Population, 100).Length);
            Assert.AreEqual(4, rec.Series(c2.Id, CivMetric.Population, 100).Length);
            var five = rec.Series(c1.Id, CivMetric.Population, 5);
            Assert.AreEqual(5, five.Length);
            Assert.AreEqual(5f, five[0]);
            Assert.AreEqual(5f, five[4]);
            Assert.AreEqual(1, rec.Series(c1.Id, CivMetric.Settlements, 1).Length);
            Assert.AreEqual(0, rec.Series(999, CivMetric.Population, 10).Length);
            Assert.AreEqual(5, rec.SeriesTicks(c1.Id, 5).Length);
            Assert.AreEqual(24L * 10, rec.SeriesTicks(c1.Id, 5)[4]);

            var buf = new float[3];
            Assert.AreEqual(3, rec.Series(c2.Id, CivMetric.Age, buf));
            Assert.AreEqual(7, rec.WorldSeries(WorldMetric.Population, 7).Length);
            Assert.AreEqual(8f, rec.WorldSeries(WorldMetric.Population, 0)[9]);
            Assert.AreEqual(10, rec.WorldDeathSeries(DeathCause.OldAge, 0).Length);
        }

        [Test]
        public void CollapsedCivGetsFinalSampleThenStops()
        {
            TestWorlds.AddSettlement(_ctx, RaceId.Saurian, new Vec2i(20, 20), 3, out var civ);
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 48);
            _ctx.Ops.CollapseCivilization(civ, -1);
            Advance(rec, 72);
            Assert.IsTrue(rec.TryGetCiv(civ.Id, out var h));
            Assert.IsFalse(h.IsAlive);
            Assert.AreEqual(3, h.Count, "two live samples + one final");
            Assert.AreEqual(0, h.Latest.Population);
        }

        [Test]
        public void TimelineCapturesEvents()
        {
            var s1 = TestWorlds.AddSettlement(_ctx, RaceId.Dwarf, new Vec2i(16, 16), 4, out var dwarves);
            TestWorlds.AddSettlement(_ctx, RaceId.Orc, new Vec2i(48, 48), 4, out var orcs);
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 0);
            var bus = _ctx.Events;

            _ctx.State.Tick = 100;
            bus.Publish(new AgeAdvancedEvent { CivilizationId = dwarves.Id, OldAge = AgeId.Dawn, NewAge = AgeId.Bronze });
            bus.Publish(new TechResearchedEvent { CivilizationId = dwarves.Id, TechId = 0 });
            bus.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = orcs.Id, DefenderId = dwarves.Id });
            _ctx.Ops.FoundSettlement(dwarves, new Vec2i(30, 16));
            _ctx.State.Tick = 300;
            _ctx.Ops.TransferSettlement(s1, orcs);
            bus.Publish(new PeaceSignedEvent { WarId = 1, AttackerId = orcs.Id, DefenderId = dwarves.Id, WarScore = 40f });
            bus.Publish(new CivilizationCollapsedEvent { CivilizationId = dwarves.Id, ConquerorId = orcs.Id });

            var kinds = new List<TimelineEventKind>();
            foreach (var e in rec.Eras) { kinds.Add(e.Kind); Assert.IsFalse(string.IsNullOrEmpty(e.Label)); }
            Assert.AreEqual(new[]
            {
                TimelineEventKind.AgeAdvanced, TimelineEventKind.TechResearched, TimelineEventKind.WarDeclared,
                TimelineEventKind.SettlementFounded, TimelineEventKind.SettlementCaptured, TimelineEventKind.PeaceSigned,
                TimelineEventKind.CivilizationCollapsed
            }, kinds.ToArray());
            Assert.AreEqual(100, rec.Eras[0].Tick);
            Assert.AreEqual((int)AgeId.Bronze, rec.Eras[0].Value);
            Assert.AreEqual(orcs.Id, rec.Eras[2].CivId);
            Assert.AreEqual(dwarves.Id, rec.Eras[2].OtherCivId);
            Assert.AreEqual(s1.Id, rec.Eras[4].SubjectId);
            Assert.AreEqual(orcs.Id, rec.Eras[4].CivId);
            Assert.AreEqual(dwarves.Id, rec.Eras[4].OtherCivId);
            Assert.AreEqual(40, rec.Eras[5].Value);
            Assert.AreEqual(orcs.Id, rec.Eras[6].OtherCivId);
            Assert.IsTrue(rec.Eras[1].Label.Contains("Stub Tech"), rec.Eras[1].Label);
        }

        [Test]
        public void CivilizationFoundingIsOnTheTimelineWhenRecorderIsFirst()
        {
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 0);
            TestWorlds.AddSettlement(_ctx, RaceId.Elf, new Vec2i(20, 20), 2, out var elves);
            Assert.AreEqual(TimelineEventKind.CivilizationFounded, rec.Eras[0].Kind);
            Assert.AreEqual((int)RaceId.Elf, rec.Eras[0].Value);
            Assert.AreEqual(TimelineEventKind.SettlementFounded, rec.Eras[1].Kind);
            Assert.AreEqual(elves.Id, rec.Eras[1].CivId);
        }

        [Test]
        public void TimelineIsCapped()
        {
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 0);
            for (int i = 0; i < 6000; i++) _ctx.Events.Publish(new TechResearchedEvent { CivilizationId = 1, TechId = i });
            Assert.AreEqual(HistoryRecorder.DefaultTimelineCapacity, rec.Eras.Count);
            Assert.AreEqual(1000, rec.Eras[0].SubjectId, "oldest evicted first");
            Assert.AreEqual(5999, rec.Eras[rec.Eras.Count - 1].SubjectId);
            Assert.AreEqual(1000, rec.ErasEvicted);

            var small = new HistoryRecorder(16, 10);
            TestWorlds.Run(small, _ctx, 0);
            for (int i = 0; i < 25; i++) _ctx.Events.Publish(new WarDeclaredEvent { WarId = i, AttackerId = 1, DefenderId = 2 });
            Assert.AreEqual(10, small.Eras.Count);
            Assert.AreEqual(15, small.Eras[0].SubjectId);
        }

        [Test]
        public void ZeroCivsDoesNotThrow()
        {
            var rec = new HistoryRecorder();
            Assert.DoesNotThrow(() => TestWorlds.Run(rec, _ctx, 24 * 4));
            Assert.AreEqual(4, rec.World.Count);
            Assert.AreEqual(0, rec.World[3].Population);
            Assert.AreEqual(0f, rec.World[3].Happiness);
            Assert.AreEqual(0, rec.CivCount);
            Assert.IsFalse(rec.CivIds.GetEnumerator().MoveNext());
            Assert.AreEqual(0, rec.Series(1, CivMetric.Population, 10).Length);
            Assert.IsFalse(rec.TryGetCiv(1, out _));
            Assert.AreEqual(0f, WorldStatistics.AverageLifespanAtDeath(rec.Lifespans));
        }

        [Test]
        public void FindLocatesRecorderInEngine()
        {
            Assert.IsNull(HistoryRecorder.Find(null));
            var engine = new SimulationEngine(_ctx);
            Assert.IsNull(HistoryRecorder.Find(engine));
            var rec = new HistoryRecorder();
            engine.AddSystem(rec);
            Assert.AreSame(rec, HistoryRecorder.Find(engine));
            engine.StepMany(48);
            Assert.AreEqual(2, rec.World.Count);
        }

        [Test]
        public void DetachStopsListeningAndClearResets()
        {
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 24);
            rec.Detach();
            _ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = 1, DefenderId = 2 });
            Assert.AreEqual(0, rec.Eras.Count);
            rec.Clear();
            Assert.AreEqual(0, rec.World.Count);
            Assert.AreEqual(0, rec.SamplesTaken);
        }
    }
}
