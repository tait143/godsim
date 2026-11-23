using GodSim.Core;
using GodSim.Core.History;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.History
{
    public class ChronicleNarratorTests
    {
        private GameDatabase _db;
        private SimContext _ctx;
        private HistoryRecorder _rec;
        private Civilization _dwarves;
        private Civilization _orcs;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.CreateStub();
            _ctx = TestWorlds.CreateContext(_db);
            _rec = new HistoryRecorder();
            TestWorlds.Run(_rec, _ctx, 0);
        }

        /// <summary>Two civs over ~5 centuries: founding, expansion, techs, an age, a war, a capture and a collapse.</summary>
        private void BuildHistory()
        {
            var bus = _ctx.Events;
            var st = _ctx.State;
            TestWorlds.AddSettlement(_ctx, RaceId.Dwarf, new Vec2i(16, 16), 4, out _dwarves);
            var orcHome = TestWorlds.AddSettlement(_ctx, RaceId.Orc, new Vec2i(48, 48), 4, out _orcs);
            st.Tick = SimTime.TicksPerYear * 40;
            _ctx.Ops.FoundSettlement(_dwarves, new Vec2i(30, 16));
            _ctx.Ops.FoundSettlement(_dwarves, new Vec2i(16, 30));
            bus.Publish(new TechResearchedEvent { CivilizationId = _dwarves.Id, TechId = 0 });
            st.Tick = SimTime.TicksPerYear * 120;
            _dwarves.Age = AgeId.Bronze;
            bus.Publish(new AgeAdvancedEvent { CivilizationId = _dwarves.Id, OldAge = AgeId.Dawn, NewAge = AgeId.Bronze });
            _ctx.Ops.FoundSettlement(_dwarves, new Vec2i(30, 30));
            st.Tick = SimTime.TicksPerYear * 380;
            bus.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = _dwarves.Id, DefenderId = _orcs.Id });
            st.Tick = SimTime.TicksPerYear * 405;
            _ctx.Ops.TransferSettlement(orcHome, _dwarves); // the orcs lose their only town and collapse
            st.Tick = SimTime.TicksPerYear * 412;
            bus.Publish(new PeaceSignedEvent { WarId = 1, AttackerId = _dwarves.Id, DefenderId = _orcs.Id, WarScore = 80f });
        }

        [Test]
        public void CivErasAreDeterministicNonEmptyAndBounded()
        {
            BuildHistory();
            var a = ChronicleNarrator.CivEras(_ctx.State, _db, _rec.Eras, _dwarves.Id);
            var b = ChronicleNarrator.CivEras(_ctx.State, _db, _rec.Eras, _dwarves.Id);
            Assert.AreEqual(2, a.Count, "Dawn and Bronze eras");
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Text, b[i].Text);
                Assert.IsFalse(string.IsNullOrEmpty(a[i].Text));
                Assert.LessOrEqual(a[i].Text.Length, ChronicleNarrator.MaxSummaryLength);
                Assert.IsTrue(a[i].Text.EndsWith("."));
            }
            Assert.AreEqual(AgeId.Dawn, a[0].Age);
            Assert.AreEqual("The Age of Dawn", a[0].Title);
            Assert.IsTrue(a[0].Text.StartsWith("The Age of Dawn: the Dwarfs of " + _dwarves.Name + " "), a[0].Text);
            Assert.IsTrue(a[0].Text.Contains(" 3 "), a[0].Text); // 2 founded (+ first) settlements
            Assert.IsTrue(a[0].Text.Contains("Bronze Age in 120"), a[0].Text);
            Assert.AreEqual(AgeId.Bronze, a[1].Age);
            Assert.IsTrue(a[1].Text.Contains("in 412"), a[1].Text);
            Assert.IsTrue(a[1].Text.Contains("the Orcs"), a[1].Text);

            var orcEras = ChronicleNarrator.CivEras(_ctx.State, _db, _rec.Eras, _orcs.Id);
            Assert.AreEqual(1, orcEras.Count);
            Assert.IsTrue(orcEras[0].Text.Contains("fell to the Dwarfs in 405"), orcEras[0].Text);
        }

        [Test]
        public void CenturiesAndNarrationAreBoundedAndDeterministic()
        {
            BuildHistory();
            var c1 = ChronicleNarrator.Centuries(_ctx.State, _db, _rec.Eras);
            var c2 = ChronicleNarrator.Centuries(_ctx.State, _db, _rec.Eras);
            Assert.AreEqual(4, c1.Count, "centuries 0, 1, 3 and 4");
            Assert.AreEqual(0, c1[0].Century);
            Assert.AreEqual(1, c1[1].Century);
            Assert.AreEqual(3, c1[2].Century);
            Assert.AreEqual(4, c1[3].Century);
            Assert.AreEqual("Years 400-499", c1[3].Title);
            for (int i = 0; i < c1.Count; i++)
            {
                Assert.AreEqual(c1[i].Text, c2[i].Text);
                Assert.LessOrEqual(c1[i].Text.Length, ChronicleNarrator.MaxSummaryLength);
                Assert.IsTrue(c1[i].Text.StartsWith(c1[i].Title + ": "), c1[i].Text);
            }
            Assert.AreEqual(1, ChronicleNarrator.Centuries(_ctx.State, _db, _rec.Eras, 1).Count);

            string full = ChronicleNarrator.Narrate(_ctx.State, _db, _rec.Eras);
            Assert.AreEqual(full, ChronicleNarrator.Narrate(_ctx.State, _db, _rec.Eras));
            Assert.IsTrue(full.Contains("Years 0-99"));
            Assert.LessOrEqual(full.Length, 4000);
            string tiny = ChronicleNarrator.Narrate(_ctx.State, _db, _rec.Eras, 100);
            Assert.LessOrEqual(tiny.Length, 100);
            Assert.IsTrue(tiny.Length > 0);
        }

        [Test]
        public void ManyEventsStayBounded()
        {
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(16, 16), 2, out var humans);
            TestWorlds.AddSettlement(_ctx, RaceId.Elf, new Vec2i(48, 48), 2, out var elves);
            for (int i = 0; i < 400; i++)
            {
                _ctx.State.Tick = i * 7;
                _ctx.Events.Publish(new WarDeclaredEvent { WarId = i, AttackerId = humans.Id, DefenderId = elves.Id });
                _ctx.Events.Publish(new TechResearchedEvent { CivilizationId = elves.Id, TechId = 0 });
            }
            foreach (var e in ChronicleNarrator.CivEras(_ctx.State, _db, _rec.Eras, humans.Id))
                Assert.LessOrEqual(e.Text.Length, ChronicleNarrator.MaxSummaryLength);
            foreach (var e in ChronicleNarrator.Centuries(_ctx.State, _db, _rec.Eras))
                Assert.LessOrEqual(e.Text.Length, ChronicleNarrator.MaxSummaryLength);
        }

        [Test]
        public void EpitaphForFallenCiv()
        {
            BuildHistory();
            Assert.IsFalse(_orcs.IsAlive);
            string e1 = ChronicleNarrator.Epitaph(_orcs);
            Assert.AreEqual(e1, ChronicleNarrator.Epitaph(_orcs));
            Assert.IsTrue(e1.Contains(_orcs.Name), e1);
            Assert.IsTrue(e1.Contains("405 years"), e1);
            Assert.LessOrEqual(e1.Length, ChronicleNarrator.MaxEpitaphLength);

            string rich = ChronicleNarrator.Epitaph(_orcs, _ctx.State, _db, _rec.Eras);
            Assert.IsTrue(rich.Contains("fell to the Dwarfs"), rich);
            Assert.LessOrEqual(rich.Length, ChronicleNarrator.MaxEpitaphLength);

            Assert.AreEqual("", ChronicleNarrator.Epitaph(null));
            Assert.IsTrue(ChronicleNarrator.Epitaph(_dwarves).Contains("still endure"));
        }

        [Test]
        public void NoCivsNoExceptions()
        {
            Assert.AreEqual(0, ChronicleNarrator.CivEras(_ctx.State, _db, _rec.Eras, 1).Count);
            Assert.AreEqual(0, ChronicleNarrator.Centuries(_ctx.State, _db, _rec.Eras).Count);
            Assert.IsFalse(string.IsNullOrEmpty(ChronicleNarrator.Narrate(_ctx.State, _db, _rec.Eras)));
            Assert.IsFalse(string.IsNullOrEmpty(ChronicleNarrator.Narrate(_ctx.State, _db, null)));
            Assert.AreEqual(0, ChronicleNarrator.CivEras(null, null, null, 1).Count);
            Assert.AreEqual(0, ChronicleNarrator.Centuries(null, null, null).Count);
            Assert.AreEqual("a forgotten people", ChronicleNarrator.CivPhrase(_ctx.State, _db, 42));
        }
    }
}
