using System.Collections.Generic;
using System.Linq;
using GodSim.Core;
using GodSim.Core.Security;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Security
{
    public class IntegrityCheckerTests
    {
        private GameDatabase _db;
        private SimContext _ctx;
        private Settlement _town;
        private Civilization _civ;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.CreateStub();
            _ctx = TestWorlds.CreateContext(_db, 64);
            _town = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(20, 20), 8, out _civ);
        }

        private Person PersonAt(int index) => _ctx.State.People.Get(_town.ResidentIds[index]);

        private Building PlaceHouse(Vec2i near)
        {
            var site = _ctx.Ops.FindFreeSite(near, 1, 1, 6);
            var house = _ctx.Ops.CreateBuilding(_town, BuildingType.House, site, 0, true);
            Assert.IsNotNull(house);
            return house;
        }

        private static string Dump(List<string> issues) => string.Join("\n", issues);

        private static bool Has(List<string> issues, params string[] fragments) =>
            issues.Any(m => fragments.All(f => m.Contains(f)));

        [Test]
        public void HealthyWorld_HasNoIssues()
        {
            var house = PlaceHouse(new Vec2i(25, 25));
            for (int i = 0; i < house.HousingCapacity; i++) Assert.IsTrue(_ctx.Ops.AssignHome(PersonAt(i), house));
            var issues = IntegrityChecker.Check(_ctx.State, _db);
            Assert.AreEqual(0, issues.Count, Dump(issues));
            Assert.AreEqual(0, IntegrityChecker.Repair(_ctx.State, _db));
        }

        [Test]
        public void NaNHealth_IsDetected_AndRepaired()
        {
            var p = PersonAt(0);
            p.Health = float.NaN;
            p.Happiness = float.PositiveInfinity;
            var issues = IntegrityChecker.Check(_ctx.State, _db);
            Assert.IsTrue(Has(issues, "Person #" + p.Id, "Health"), Dump(issues));
            Assert.IsTrue(float.IsNaN(p.Health), "Check must not mutate state");
            Assert.IsTrue((IntegrityChecker.Repair(_ctx.State)) > (0));
            Assert.AreEqual(1f, p.Health);
            Assert.AreEqual(0.6f, p.Happiness);
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, _db).Count);
        }

        [Test]
        public void DanglingHomeId_IsDetected_AndRepaired()
        {
            var p = PersonAt(1);
            p.HomeId = 99999;
            var issues = IntegrityChecker.Check(_ctx.State, _db);
            Assert.IsTrue(Has(issues, "Person #" + p.Id, "HomeId"), Dump(issues));
            IntegrityChecker.Repair(_ctx.State);
            Assert.AreEqual(-1, p.HomeId);
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, _db).Count);
        }

        [Test]
        public void OverCapacityHouse_IsDetected_AndRepaired()
        {
            var house = PlaceHouse(new Vec2i(25, 25));
            house.HousingCapacity = 3;
            for (int i = 0; i < 3; i++) Assert.IsTrue(_ctx.Ops.AssignHome(PersonAt(i), house));
            for (int i = 3; i < 5; i++) { var p = PersonAt(i); house.Residents.Add(p.Id); p.HomeId = house.Id; } // bypass WorldOps
            var issues = IntegrityChecker.Check(_ctx.State, _db);
            Assert.IsTrue(Has(issues, "Building #" + house.Id, "exceed its capacity"), Dump(issues));
            IntegrityChecker.Repair(_ctx.State, _db);
            Assert.AreEqual(3, house.Residents.Count);
            int homed = 0;
            for (int i = 0; i < 5; i++) if (PersonAt(i).HomeId == house.Id) homed++;
            Assert.AreEqual(3, homed, "evicted residents become homeless");
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, _db).Count);
        }

        [Test]
        public void AsymmetricLinks_AreDetected_AndRepaired()
        {
            var house = PlaceHouse(new Vec2i(25, 25));
            var a = PersonAt(0);
            var b = PersonAt(1);
            house.Residents.Add(a.Id);              // building lists a, but a.HomeId = -1
            house.Residents.Add(424242);            // missing person
            b.HomeId = house.Id;                    // b claims a home that does not list it
            var c = PersonAt(2);
            _town.ResidentIds.Remove(c.Id);             // c claims the town but is not listed
            _town.ResidentIds.Add(_town.ResidentIds[0]); // duplicate entry
            _town.BuildingIds.Add(777);              // dangling building id
            _civ.LeaderId = 55555;

            var issues = IntegrityChecker.Check(_ctx.State, _db);
            Assert.IsTrue(Has(issues, "Building #" + house.Id, "Residents"), Dump(issues));
            Assert.IsTrue(Has(issues, "Person #" + b.Id, "home"), Dump(issues));
            Assert.IsTrue(Has(issues, "Person #" + c.Id, "ResidentIds"), Dump(issues));
            Assert.IsTrue(Has(issues, "Settlement #" + _town.Id, "twice"), Dump(issues));
            Assert.IsTrue(Has(issues, "Settlement #" + _town.Id, "BuildingIds"), Dump(issues));
            Assert.IsTrue(Has(issues, "LeaderId"), Dump(issues));

            IntegrityChecker.Repair(_ctx.State, _db);
            Assert.AreEqual(0, house.Residents.Count);
            Assert.AreEqual(-1, b.HomeId);
            Assert.IsTrue(_town.ResidentIds.Contains(c.Id), "forward settlement link is authoritative");
            Assert.AreEqual(_town.ResidentIds.Count, _town.ResidentIds.Distinct().Count());
            Assert.IsFalse(_town.BuildingIds.Contains(777));
            Assert.AreEqual(-1, _civ.LeaderId);
            var after = IntegrityChecker.Check(_ctx.State, _db);
            Assert.AreEqual(0, after.Count, Dump(after));
        }

        [Test]
        public void WorkplaceLinks_AndOverstaffing_AreRepaired()
        {
            var hut = _ctx.Ops.CreateBuilding(_town, BuildingType.GathererHut, _ctx.Ops.FindFreeSite(new Vec2i(28, 20), 1, 1, 6), 0, true);
            // Stub GathererHut has 0 job slots: anyone working there is over-staffing.
            var p = PersonAt(0);
            hut.Workers.Add(p.Id);
            p.WorkplaceId = hut.Id;
            p.Job = JobType.Gatherer;
            var q = PersonAt(1);
            q.WorkplaceId = 31337;
            q.Job = JobType.Smith;
            var issues = IntegrityChecker.Check(_ctx.State, _db);
            Assert.IsTrue(Has(issues, "job slots"), Dump(issues));
            Assert.IsTrue(Has(issues, "Person #" + q.Id, "WorkplaceId"), Dump(issues));
            IntegrityChecker.Repair(_ctx.State, _db);
            Assert.AreEqual(0, hut.Workers.Count);
            Assert.AreEqual(-1, p.WorkplaceId);
            Assert.AreEqual(JobType.None, p.Job);
            Assert.AreEqual(-1, q.WorkplaceId);
            Assert.AreEqual(JobType.None, q.Job);
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, _db).Count);
        }

        [Test]
        public void MapBuildingAt_IsReconciledWithFootprints()
        {
            var house = PlaceHouse(new Vec2i(25, 25));
            var map = _ctx.State.Map;
            map.BuildingAt[map.Index(2, 2)] = 123456;              // stale reference
            map.BuildingAt[map.Index(house.Origin)] = -1;           // footprint tile unmarked
            var issues = IntegrityChecker.Check(_ctx.State, _db);
            Assert.IsTrue(Has(issues, "(2,2)", "BuildingAt"), Dump(issues));
            Assert.IsTrue(Has(issues, "Building #" + house.Id, "footprint"), Dump(issues));
            IntegrityChecker.Repair(_ctx.State);
            Assert.AreEqual(-1, map.BuildingAt[map.Index(2, 2)]);
            Assert.AreEqual(house.Id, map.BuildingAt[map.Index(house.Origin)]);
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, _db).Count);
        }

        [Test]
        public void NonFiniteGlobals_StockpileAndTiles_AreRepaired()
        {
            var s = _ctx.State;
            s.God.Mana = float.NaN;
            s.Weather.Rainfall = float.NegativeInfinity;
            s.Memory["ai.test"] = float.NaN;
            _town.Stockpile.SetRaw(ResourceType.Food, 10f);
            _town.ServiceOutput[(int)ServiceType.Faith] = float.NaN;
            _town.Stockpile.AvgIncome[0] = float.PositiveInfinity;
            _civ.Modifiers.Research = float.NaN;
            s.Map.Fertility[5] = float.NaN;
            s.Map.Biomes[6] = (Biome)250;
            var issues = IntegrityChecker.Check(s, _db);
            Assert.IsTrue((issues.Count) >= (8), Dump(issues));
            IntegrityChecker.Repair(s);
            Assert.AreEqual(0f, s.God.Mana);
            Assert.AreEqual(1f, s.Weather.Rainfall);
            Assert.AreEqual(0f, s.Memory["ai.test"]);
            Assert.AreEqual(0f, _town.ServiceOutput[(int)ServiceType.Faith]);
            Assert.AreEqual(1f, _civ.Modifiers.Research);
            Assert.AreEqual(0f, s.Map.Fertility[5]);
            Assert.IsTrue((int)s.Map.Biomes[6] < Counts.Biomes);
            Assert.AreEqual(0, IntegrityChecker.Check(s, _db).Count);
        }

        [Test]
        public void GameplayValues_AreNotTreatedAsCorruption()
        {
            _ctx.State.God.Mana = 2000f; // StartingMana may exceed MaxMana; that is the divine module's business
            _ctx.State.God.MaxMana = 500f;
            PersonAt(0).Happiness = 1.2f;
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, _db).Count);
            Assert.AreEqual(2000f, _ctx.State.God.Mana);
            _ctx.State.God.Mana = -3f;
            IntegrityChecker.Repair(_ctx.State);
            Assert.AreEqual(0f, _ctx.State.God.Mana, "negative mana is corruption");
        }

        [Test]
        public void StaleIdAllocator_IsAdvanced()
        {
            _ctx.State.Ids.NextPersonId = 1;
            Assert.IsTrue(Has(IntegrityChecker.Check(_ctx.State, _db), "NextPersonId"));
            IntegrityChecker.Repair(_ctx.State);
            _ctx.Ops.CreatePerson(_town, RaceId.Human, Sex.Male, 20f);
        }

        [Test]
        public void PopulationCap_IsReported_NotRepaired()
        {
            var issues = IntegrityChecker.Check(_ctx.State, _db, 100, 5);
            Assert.IsTrue(Has(issues, "Population"), Dump(issues));
            int before = _ctx.State.LivingPopulation;
            IntegrityChecker.Repair(_ctx.State, _db);
            Assert.AreEqual(before, _ctx.State.LivingPopulation);
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, _db, 100, 8).Count, "8 people fit a cap of 8");
        }

        [Test]
        public void MaxIssues_BoundsTheReport()
        {
            for (int i = 0; i < _ctx.State.Map.TileCount; i++) _ctx.State.Map.Moisture[i] = float.NaN;
            Assert.AreEqual(10, IntegrityChecker.Check(_ctx.State, _db, 10).Count);
            Assert.AreEqual(1, IntegrityChecker.Check(_ctx.State, _db, -5).Count);
            Assert.AreEqual(_ctx.State.Map.TileCount, IntegrityChecker.Repair(_ctx.State));
        }

        [Test]
        public void NullInputs_DoNotThrow()
        {
            Assert.AreEqual(1, IntegrityChecker.Check(null, _db).Count);
            Assert.AreEqual(0, IntegrityChecker.Repair(null));
            Assert.AreEqual(0, IntegrityChecker.Check(_ctx.State, null).Count);
        }

        [Test]
        public void IntegritySystem_RunsWithoutThrowing_AndCountsIssues()
        {
            var system = new IntegritySystem();
            Assert.AreEqual(96, system.TickInterval);
            TestWorlds.Run(system, _ctx, 200);
            Assert.AreEqual(0f, _ctx.State.Memory[IntegritySystem.IssuesKey]);

            PersonAt(0).Health = float.NaN;
            PersonAt(1).HomeId = 4242;
            TestWorlds.Run(system, _ctx, 96, false);
            Assert.AreEqual(2f, _ctx.State.Memory[IntegritySystem.IssuesKey]);
            Assert.AreEqual(2f, _ctx.State.Memory[IntegritySystem.RepairsKey]);
            Assert.AreEqual(1f, PersonAt(0).Health);
            Assert.AreEqual(-1, PersonAt(1).HomeId);
        }

        [Test]
        public void IntegritySystem_WorksInsideTheEngine()
        {
            var engine = new SimulationEngine(_ctx);
            engine.AddSystems(SecurityModule.CreateSystems());
            engine.StepMany(300);
            Assert.AreEqual(0, engine.Profiles[0].TotalFaults);
        }

        [Test]
        public void SecurityModule_CreatesIntegritySystem()
        {
            var systems = SecurityModule.CreateSystems().ToList();
            Assert.AreEqual(1, systems.Count);
            Assert.IsTrue(systems[0] is IntegritySystem);
        }
    }
}
