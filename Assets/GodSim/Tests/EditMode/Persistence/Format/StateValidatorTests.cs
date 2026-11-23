using System;
using GodSim.Core;
using GodSim.Core.Persistence;
using NUnit.Framework;

namespace GodSim.Tests.Persistence.Format
{
    public class StateValidatorTests
    {
        private GameDatabase _db;
        private WorldState _world;

        [SetUp]
        public void SetUp()
        {
            _db = SaveTestWorld.CreateDb();
            _world = SaveTestWorld.Build(_db);
        }

        private WorldState Reload(WorldState s, GameDatabase db = null) =>
            SaveSerializer.Deserialize(SaveSerializer.Serialize(s, new SaveOptions { Compress = false }), db ?? _db, null);

        private static bool HasFatal(System.Collections.Generic.List<string> issues)
        {
            foreach (var i in issues) if (i.StartsWith("fatal:", StringComparison.Ordinal)) return true;
            return false;
        }

        private void AssertFatalOnLoad()
        {
            Assert.IsTrue(HasFatal(StateValidator.Validate(_world, _db)));
            byte[] data = SaveSerializer.Serialize(_world, new SaveOptions());
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(data, _db, null));
            Assert.IsTrue(ex.Message.Contains("validation"), ex.Message);
        }

        private Person HousedWorker()
        {
            foreach (var p in _world.People.Items) if (p.IsAlive && p.HomeId >= 0 && p.WorkplaceId >= 0) return p;
            throw new InvalidOperationException("fixture has no housed worker");
        }

        // ------------------------------------------------------------------ benign problems are repaired
        [Test]
        public void DanglingHomeAndWorkplace_AreCleared()
        {
            var p = HousedWorker();
            _world.Buildings.Get(p.HomeId).Residents.Remove(p.Id); // home no longer lists them
            p.WorkplaceId = _world.Ids.NextBuildingId + 50;        // no such building
            var issues = StateValidator.Validate(_world, _db);
            Assert.IsTrue(issues.Count >= 2 && !HasFatal(issues), string.Join("\n", issues));
            var loaded = Reload(_world);
            var q = loaded.People.Get(p.Id);
            Assert.AreEqual(-1, q.HomeId);
            Assert.AreEqual(-1, q.WorkplaceId);
            Assert.AreEqual(JobType.None, q.Job);
            Assert.AreEqual(0, StateValidator.Validate(loaded, _db).Count);
        }

        [Test]
        public void StaleMembershipLists_AreCompacted()
        {
            var st = _world.Settlements.Items[0];
            var civ = _world.Civilizations.Get(st.CivilizationId);
            var army = _world.Diplomacy.Armies.Items[0];
            var house = _world.Buildings.Get(HousedWorker().HomeId);
            int residents = st.ResidentIds.Count, buildings = st.BuildingIds.Count, settlements = civ.SettlementIds.Count;
            int soldiers = army.SoldierIds.Count, houseResidents = house.Residents.Count;
            st.ResidentIds.Add(99999);
            st.ResidentIds.Add(st.ResidentIds[0]); // duplicate
            st.BuildingIds.Add(424242);
            civ.SettlementIds.Add(31337);
            army.SoldierIds.Add(-5);
            house.Residents.Add(house.Residents[0]);
            var loaded = Reload(_world);
            Assert.AreEqual(residents, loaded.Settlements.Get(st.Id).ResidentIds.Count);
            Assert.AreEqual(buildings, loaded.Settlements.Get(st.Id).BuildingIds.Count);
            Assert.AreEqual(settlements, loaded.Civilizations.Get(civ.Id).SettlementIds.Count);
            Assert.AreEqual(soldiers, loaded.Diplomacy.Armies.Get(army.Id).SoldierIds.Count);
            Assert.AreEqual(houseResidents, loaded.Buildings.Get(house.Id).Residents.Count);
            Assert.AreEqual(0, StateValidator.Validate(loaded, _db).Count);
        }

        [Test]
        public void MapOccupancyAndOwnership_AreRebuilt()
        {
            var map = _world.Map;
            var b = _world.Buildings.Items[0];
            int freeTile = -1;
            for (int i = 0; i < map.TileCount && freeTile < 0; i++) if (map.BuildingAt[i] < 0) freeTile = i;
            map.BuildingAt[freeTile] = 777;
            map.BuildingAt[map.Index(b.Origin)] = -1;
            map.Owner[freeTile] = 4242;
            var loaded = Reload(_world);
            Assert.AreEqual(-1, loaded.Map.BuildingAt[freeTile]);
            Assert.AreEqual(b.Id, loaded.Map.BuildingAt[loaded.Map.Index(b.Origin)]);
            Assert.AreEqual(-1, loaded.Map.Owner[freeTile]);
        }

        [Test]
        public void ArmyPathOffTheMap_AndBadOptionalIds_AreCleared()
        {
            var army = _world.Diplomacy.Armies.Items[0];
            army.Path.Add(new Vec2i(500, 500));
            army.TargetSettlementId = 9999;
            var civ = _world.Civilizations.Items[0];
            civ.LeaderId = 123456;
            civ.CapitalId = 777;
            var chosen = _world.People.Items[1];
            chosen.MotherId = chosen.Id;
            var loaded = Reload(_world);
            var a = loaded.Diplomacy.Armies.Get(army.Id);
            Assert.AreEqual(0, a.Path.Count);
            Assert.AreEqual(0, a.PathIndex);
            Assert.AreEqual(-1, a.TargetSettlementId);
            Assert.AreEqual(-1, loaded.Civilizations.Get(civ.Id).LeaderId);
            Assert.AreEqual(-1, loaded.Civilizations.Get(civ.Id).CapitalId);
            Assert.AreEqual(-1, loaded.People.Get(chosen.Id).MotherId);
        }

        [Test]
        public void UnknownTechs_AreForgotten_OnlyWhenADatabaseIsGiven()
        {
            var civ = _world.Civilizations.Items[0];
            civ.KnownTechs.Add(500);
            civ.CurrentResearchTechId = 77;
            var withDb = Reload(_world, _db);
            Assert.IsFalse(withDb.Civilizations.Get(civ.Id).KnownTechs.Contains(500));
            Assert.AreEqual(-1, withDb.Civilizations.Get(civ.Id).CurrentResearchTechId);
            var noDb = SaveSerializer.Deserialize(SaveSerializer.Serialize(_world, null), null, null);
            Assert.IsTrue(noDb.Civilizations.Get(civ.Id).KnownTechs.Contains(500));
        }

        // ------------------------------------------------------------------ fatal problems reject the save
        [Test]
        public void FootprintOutOfBounds_IsFatal()
        {
            var b = _world.Buildings.Items[0];
            b.Origin = new Vec2i(_world.Map.Width - 1, _world.Map.Height - 1);
            b.Width = 2;
            AssertFatalOnLoad();
        }

        [Test]
        public void HugeFootprint_IsFatal()
        {
            _world.Buildings.Items[0].Width = int.MaxValue;
            AssertFatalOnLoad();
        }

        [Test]
        public void OverlappingBuildings_AreFatal()
        {
            var a = _world.Buildings.Items[1];
            var b = _world.Buildings.Items[2];
            b.Origin = a.Origin;
            AssertFatalOnLoad();
        }

        [Test]
        public void PersonNotListedByTheirSettlement_IsFatal()
        {
            var st = _world.Settlements.Items[0];
            st.ResidentIds.RemoveAt(0);
            AssertFatalOnLoad();
        }

        [Test]
        public void PersonListedByTheWrongSettlement_IsFatal()
        {
            var a = _world.Settlements.Items[0];
            var b = _world.Settlements.Items[1];
            b.ResidentIds.Add(a.ResidentIds[0]);
            AssertFatalOnLoad();
        }

        [Test]
        public void IdAtOrAboveAllocatorCounter_IsFatal()
        {
            _world.Ids.NextPersonId = 2;
            AssertFatalOnLoad();
        }

        [Test]
        public void RelationWithUnknownCivilization_IsFatal()
        {
            _world.Diplomacy.AddRelation(new Relation { CivA = 1, CivB = 999 });
            AssertFatalOnLoad();
        }

        [Test]
        public void ArmyOfUnknownCivilization_IsFatal()
        {
            _world.Diplomacy.Armies.Items[0].CivilizationId = 999;
            AssertFatalOnLoad();
        }

        [Test]
        public void NonFiniteValuesInState_AreReportedAsFatal()
        {
            _world.Settlements.Items[0].Memory["x"] = float.NaN;
            _world.Civilizations.Items[0].Modifiers.Production[0] = float.PositiveInfinity;
            var issues = StateValidator.Validate(_world, _db);
            Assert.IsTrue(HasFatal(issues), string.Join("\n", issues));
        }

        // ------------------------------------------------------------------ API behavior
        [Test]
        public void Validate_DoesNotModifyTheState()
        {
            var twin = SaveTestWorld.Build(_db);
            foreach (var w in new[] { _world, twin })
            {
                var p = w.People.Items[0];
                p.HomeId = 99999;
                w.Map.Owner[0] = 5555;
                w.Settlements.Items[0].ResidentIds.Add(88888);
            }
            var issues = StateValidator.Validate(_world, _db);
            Assert.IsTrue(issues.Count >= 3, string.Join("\n", issues));
            var diffs = DeepComparer.Compare(twin, _world);
            Assert.AreEqual(0, diffs.Count, string.Join("\n", diffs));
        }

        [Test]
        public void ValidateAndRepair_IsIdempotent()
        {
            _world.People.Items[0].SpouseId = _world.People.Items[0].Id;
            _world.Buildings.Items[0].Rotation = 7;
            _world.Chronicle.Entries[0] = new ChronicleEntry { Text = "x", Importance = 42 };
            var first = StateValidator.ValidateAndRepair(_world, _db);
            Assert.IsTrue(first.IsUsable);
            Assert.IsTrue(first.Repaired.Count >= 3);
            Assert.AreEqual(0, StateValidator.Validate(_world, _db).Count);
        }

        [Test]
        public void NullOrMaplessState_IsReportedNotThrown()
        {
            Assert.IsTrue(HasFatal(StateValidator.Validate(null, _db)));
            _world.Map = null;
            Assert.IsTrue(HasFatal(StateValidator.Validate(_world, null)));
        }
    }
}
