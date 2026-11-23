using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Storytelling;
using NUnit.Framework;

namespace GodSim.Tests.Storytelling
{
    public class SecessionTests
    {
        private const float Eps = 1e-3f;

        private static Settlement RemoteRebelColony(StoryWorld.World w, Vec2i at)
        {
            var colony = StoryWorld.AddTown(w, w.A, at, 10, RaceId.Human);
            colony.Tier = SettlementTier.Village;
            colony.Unrest = 0.8f;
            return colony;
        }

        private static StoryWorld.World KnowledgeableWorld()
        {
            var w = StoryWorld.Create();
            w.A.Age = AgeId.Iron;
            w.A.AgeEnteredTick = 1234;
            w.A.KnownTechs.Add(0); w.A.KnownTechs.Add(1); w.A.KnownTechs.Add(2);
            w.A.UnlockedBuildings[(int)BuildingType.Granary] = true;
            w.A.TotalResearch = 500f;
            return w;
        }

        [Test]
        public void Secession_CreatesNewCivWithParentKnowledge_TransfersSettlementAndPeople()
        {
            var w = KnowledgeableWorld();
            var colony = RemoteRebelColony(w, new Vec2i(100, 30));
            var houses = StoryWorld.AddBuildings(w, colony, BuildingType.House, 2, true);
            var people = StoryWorld.Residents(w, colony);
            int civs0 = w.State.Civilizations.Count;

            StoryWorld.Fire(w, w.A, WorldEventKeys.Secession);

            Assert.AreEqual(civs0 + 1, w.State.Civilizations.Count);
            var nc = w.State.Civilizations.Items[civs0];
            Assert.IsTrue(nc.IsAlive);
            Assert.AreEqual(RaceId.Human, nc.Race);
            Assert.AreEqual(AgeId.Iron, nc.Age);
            Assert.AreEqual(1234L, nc.AgeEnteredTick);
            Assert.IsTrue(nc.KnownTechs.SetEquals(w.A.KnownTechs));
            Assert.IsTrue(nc.UnlockedBuildings[(int)BuildingType.Granary]);
            Assert.AreEqual(500f, nc.TotalResearch, Eps);

            Assert.AreEqual(nc.Id, colony.CivilizationId);
            Assert.AreEqual(colony.Id, nc.CapitalId);
            Assert.IsTrue(colony.IsCapital);
            Assert.IsTrue(nc.SettlementIds.Contains(colony.Id));
            Assert.IsFalse(w.A.SettlementIds.Contains(colony.Id));
            Assert.IsTrue(w.A.IsAlive);
            Assert.AreEqual(w.CapA.Id, w.A.CapitalId);
            Assert.IsTrue(w.CapA.IsCapital);

            Assert.AreEqual(people.Count, StoryWorld.Alive(w, colony));
            foreach (var p in people) { Assert.IsTrue(p.IsAlive); Assert.AreEqual(nc.Id, p.CivilizationId); }
            foreach (var b in houses) Assert.AreEqual(nc.Id, b.CivilizationId);
            var leader = w.State.People.Get(nc.LeaderId);
            Assert.IsNotNull(leader);
            Assert.AreEqual(colony.Id, leader.SettlementId);
            Assert.IsTrue(colony.Unrest < 0.8f);

            Relation rel;
            Assert.IsTrue(w.State.Diplomacy.TryGet(w.A.Id, nc.Id, out rel));
            Assert.AreEqual(DiplomaticStance.Hostile, rel.Stance);
            Assert.AreEqual(-50f, rel.Opinion, Eps);
            Assert.IsTrue(rel.HasContact);
            Assert.AreEqual((float)w.A.Id, nc.Mem(Secession.ParentCivKey, -1f), Eps);

            var entry = StoryWorld.LastChronicle(w);
            Assert.AreEqual(3, entry.Importance);
            Assert.AreEqual(ChronicleCategory.Civilization, entry.Category);
            Assert.IsTrue(entry.Text.Contains(colony.Name), entry.Text);
            Assert.IsTrue(entry.Text.Contains(nc.Name), entry.Text);
            Assert.IsTrue(entry.Text.Contains(w.A.Name) || entry.Text.Contains(leader.Name), entry.Text);
            StoryWorld.AssertSane(w);
        }

        [Test]
        public void Secession_ForeignMajorityNearCapital_FoundsStateOfThatRace()
        {
            var w = StoryWorld.Create();
            var colony = StoryWorld.AddTown(w, w.A, new Vec2i(55, 30), 9, RaceId.Elf); // ~24 tiles: near
            colony.Tier = SettlementTier.Village;
            StoryWorld.SetMood(w, colony, 0.2f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.Secession);
            var nc = w.State.Civilizations.Get(colony.CivilizationId);
            Assert.AreNotEqual(w.A.Id, nc.Id);
            Assert.AreEqual(RaceId.Elf, nc.Race);
            Assert.AreEqual(RaceId.Elf, colony.Race);
        }

        [Test]
        public void Secession_NearSameRaceSettlement_StaysLoyal()
        {
            var w = StoryWorld.Create();
            var colony = StoryWorld.AddTown(w, w.A, new Vec2i(55, 30), 9, RaceId.Human);
            colony.Tier = SettlementTier.Town;
            colony.Unrest = 0.95f;
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession));
            Assert.AreEqual(w.A.Id, colony.CivilizationId);
        }

        [Test]
        public void Secession_NeverTargetsTheCapital()
        {
            var w = StoryWorld.Create();
            var colony = StoryWorld.AddTown(w, w.A, new Vec2i(100, 30), 10, RaceId.Human);
            colony.Tier = SettlementTier.Village; // remote but content
            w.CapA.Tier = SettlementTier.City;
            w.CapA.Unrest = 0.95f;
            StoryWorld.SetMood(w, w.CapA, 0.05f);
            foreach (var p in StoryWorld.Residents(w, w.CapA)) p.Race = RaceId.Orc; // even a foreign-majority capital
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession));
            Assert.IsFalse(Secession.IsEligible(w.State, w.A, w.CapA));
            Assert.AreEqual(w.A.Id, w.CapA.CivilizationId);
            Assert.AreEqual(2, w.State.Civilizations.Count);
        }

        [Test]
        public void Secession_NeverTakesTheLastSettlement()
        {
            var w = StoryWorld.Create();
            var colony = RemoteRebelColony(w, new Vec2i(100, 30));
            // The capital is gone (abandoned): the colony is now the civ's only settlement.
            w.Ops.DestroySettlement(w.CapA, DestroyCause.Abandoned);
            w.Ops.FlushRemovals();
            Assert.IsTrue(w.A.IsAlive);
            Assert.AreEqual(1, w.A.SettlementIds.Count);
            colony.Unrest = 0.9f;
            colony.IsCapital = false; // even if the capital flag were missing
            w.A.CapitalId = -1;
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession));
            Assert.IsFalse(Secession.IsEligible(w.State, w.A, colony));
            Assert.AreEqual(w.A.Id, colony.CivilizationId);
        }

        [Test]
        public void Secession_RequiresVillageAndDiscontent()
        {
            var w = StoryWorld.Create();
            var colony = RemoteRebelColony(w, new Vec2i(100, 30));
            colony.Tier = SettlementTier.Hamlet;
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession), "hamlet");
            colony.Tier = SettlementTier.Village;
            colony.Unrest = 0.5f;
            StoryWorld.SetMood(w, colony, 0.5f);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession), "content");
            StoryWorld.SetMood(w, colony, 0.25f);
            Assert.IsTrue(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession), "unhappy enough");
        }

        [Test]
        public void Secession_AtMostOncePerFortyYears()
        {
            var w = StoryWorld.Create();
            var first = RemoteRebelColony(w, new Vec2i(100, 30));
            var second = RemoteRebelColony(w, new Vec2i(100, 70));
            StoryWorld.Fire(w, w.A, WorldEventKeys.Secession);
            Assert.AreNotEqual(w.A.Id, first.CivilizationId == w.A.Id ? second.CivilizationId : first.CivilizationId);
            var loyal = first.CivilizationId == w.A.Id ? first : second;
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession), "cooldown");
            w.State.Tick += SimTime.YearsToTicks(39f);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Secession), "still within 40 years");
            w.State.Tick += SimTime.YearsToTicks(1f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.Secession);
            Assert.AreNotEqual(w.A.Id, loyal.CivilizationId);
            Assert.IsTrue(w.A.IsAlive);
            Assert.AreEqual(1, w.A.SettlementIds.Count, "parent keeps its capital");
            Assert.AreEqual(4, w.State.Civilizations.Count);
        }

        [Test]
        public void Secession_ParentLeaderInsideIsReleased()
        {
            var w = StoryWorld.Create();
            var colony = RemoteRebelColony(w, new Vec2i(100, 30));
            var governor = StoryWorld.Residents(w, colony)[0];
            w.A.LeaderId = governor.Id;
            StoryWorld.Fire(w, w.A, WorldEventKeys.Secession);
            Assert.AreEqual(-1, w.A.LeaderId, "the parent no longer claims a foreign leader");
        }

        [Test]
        public void Secession_ExecuteRejectsIneligibleInput()
        {
            var w = StoryWorld.Create();
            Assert.IsNull(Secession.Execute(w.Ctx, w.A, w.CapA));
            Assert.IsNull(Secession.Execute(w.Ctx, null, null));
            Assert.IsNull(Secession.Execute(null, w.A, w.CapA));
            Assert.IsNull(Secession.FindCandidate(w.State, w.A));
            var list = new List<Civilization>(w.State.Civilizations.Items);
            Assert.AreEqual(2, list.Count);
        }
    }
}
