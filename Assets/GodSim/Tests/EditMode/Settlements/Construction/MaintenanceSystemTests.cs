using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;
using Data = GodSim.Tests.Settlements.Construction.ConstructionTestData;

namespace GodSim.Tests.Settlements.Construction
{
    public class MaintenanceSystemTests
    {
        private const float Year = SimTime.TicksPerYear;

        [Test]
        public void SystemContract_NameAndInterval()
        {
            var sys = new MaintenanceSystem();
            Assert.AreEqual(12, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
        }

        [Test]
        public void CompleteBuildings_DecaySlowly()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out _);
            Data.ClearStock(s);
            var store = Data.Place(ctx, s, BuildingType.Storehouse, true);
            var site = Data.Place(ctx, s, BuildingType.Storehouse);
            TestWorlds.Run(new MaintenanceSystem(), ctx, SimTime.TicksPerYear);
            Assert.AreEqual(1f - 0.005f, store.Condition, 1e-4f);
            Assert.AreEqual(1f, site.Condition, 1e-6f, "sites under construction do not decay");
        }

        [Test]
        public void UnstaffedJobBuildings_AndOutdatedBuildings_DecayFaster()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out var civ);
            Data.ClearStock(s);
            var idle = Data.Place(ctx, s, BuildingType.Workshop, true);
            var staffed = Data.Place(ctx, s, BuildingType.Workshop, true);
            Data.AddWorker(ctx, s, JobType.Craftsman, staffed);
            TestWorlds.Run(new MaintenanceSystem(), ctx, SimTime.TicksPerYear);
            Assert.AreEqual(1f - 0.01f, idle.Condition, 1e-4f);
            Assert.AreEqual(1f - 0.005f, staffed.Condition, 1e-4f);

            var ctx2 = Data.CreateContext();
            var t = Data.AddTown(ctx2, 2, out var civ2);
            Data.ClearStock(t);
            var old = Data.Place(ctx2, t, BuildingType.Storehouse, true);
            civ2.Age = AgeId.Iron; // two ages newer than the Dawn style; no stock so no renovation
            TestWorlds.Run(new MaintenanceSystem(), ctx2, SimTime.TicksPerYear);
            Assert.AreEqual(AgeId.Dawn, old.StyleAge);
            Assert.AreEqual(1f - 0.0075f, old.Condition, 1e-4f);
            Assert.AreEqual(1.5f, MaintenanceSystem.DecayMultiplier(ctx2.Db.Building(BuildingType.Storehouse), old, civ2), 1e-5f);
            Assert.AreEqual(3f, MaintenanceSystem.DecayMultiplier(ctx2.Db.Building(BuildingType.Workshop), idle, civ2), 1e-5f);
            Assert.IsNotNull(civ);
        }

        [Test]
        public void Repair_ConsumesWoodAndStoneByFootprint()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out _);
            var store = Data.Place(ctx, s, BuildingType.Storehouse, true); // 2 tiles
            store.Condition = 0.65f;
            Data.Stock(s, 10f, 10f);
            TestWorlds.Run(new MaintenanceSystem(), ctx, 12);
            Assert.AreEqual(0.85f, store.Condition, 1e-3f);
            Assert.AreEqual(6f, s.Stockpile.Get(ResourceType.Wood), 1e-3f);
            Assert.AreEqual(8f, s.Stockpile.Get(ResourceType.Stone), 1e-3f);

            store.Condition = 0.6f;
            Data.Stock(s, 10f, 0f);
            TestWorlds.Run(new MaintenanceSystem(), ctx, 12, false);
            Assert.AreEqual(0.8f, store.Condition, 1e-3f, "wood-only fallback");
            Assert.AreEqual(2f, s.Stockpile.Get(ResourceType.Wood), 1e-3f);

            store.Condition = 0.6f;
            Data.ClearStock(s);
            TestWorlds.Run(new MaintenanceSystem(), ctx, 12, false);
            Assert.Less(store.Condition, 0.6f, "no resources, no repair");
        }

        [Test]
        public void ConditionZero_CollapsesFromDecay()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out _);
            Data.ClearStock(s);
            var store = Data.Place(ctx, s, BuildingType.Storehouse, true);
            int id = store.Id;
            store.Condition = 0.00001f;
            var causes = new List<DestroyCause>();
            ctx.Events.Subscribe<BuildingDestroyedEvent>(e => { if (e.BuildingId == id) causes.Add(e.Cause); });
            TestWorlds.Run(new MaintenanceSystem(), ctx, 12);
            Assert.IsTrue(store.IsDestroyed);
            Assert.AreEqual(1, causes.Count);
            Assert.AreEqual(DestroyCause.Decay, causes[0]);
            Assert.IsNull(ctx.State.Buildings.Get(id));
        }

        [Test]
        public void Renovation_UpdatesStyleAgeLevel_AndPublishesEvent()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out var civ);
            var store = Data.Place(ctx, s, BuildingType.Storehouse, true);
            store.Condition = 0.9f;
            civ.Age = AgeId.Bronze;
            Data.TownCenter(ctx, s).StyleAge = AgeId.Bronze; // isolate the storehouse
            Data.Stock(s, 100f, 100f);
            var events = new List<BuildingUpgradedEvent>();
            ctx.Events.Subscribe<BuildingUpgradedEvent>(e => events.Add(e));
            TestWorlds.Run(new MaintenanceSystem(), ctx, 12);
            Assert.AreEqual(AgeId.Bronze, store.StyleAge);
            Assert.AreEqual(2, store.Level);
            Assert.AreEqual(1f, store.Condition, 1e-5f);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(store.Id, events[0].BuildingId);
            Assert.AreEqual(2, events[0].NewLevel);
            Assert.AreEqual(AgeId.Bronze, events[0].StyleAge);
            Assert.AreEqual(91f, s.Stockpile.Get(ResourceType.Wood), 1e-3f, "30% of 30 wood");
            Assert.AreEqual(97f, s.Stockpile.Get(ResourceType.Stone), 1e-3f, "30% of 10 stone");

            TestWorlds.Run(new MaintenanceSystem(), ctx, 48, false);
            Assert.AreEqual(1, events.Count, "renovated once per age");
        }

        [Test]
        public void Renovation_OldestStyleFirst_WithTierBudget()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out var civ);
            var a = Data.Place(ctx, s, BuildingType.Storehouse, true);
            var b = Data.Place(ctx, s, BuildingType.Storehouse, true);
            var c = Data.Place(ctx, s, BuildingType.Storehouse, true);
            a.StyleAge = AgeId.Bronze;
            civ.Age = AgeId.Iron;
            Data.TownCenter(ctx, s).StyleAge = AgeId.Iron;
            Data.Stock(s, 1000f, 1000f);
            var order = new List<int>();
            ctx.Events.Subscribe<BuildingUpgradedEvent>(e => order.Add(e.BuildingId));
            var sys = new MaintenanceSystem();
            TestWorlds.Run(sys, ctx, 12);
            Assert.AreEqual(1, order.Count, "camp: one renovation per run");
            TestWorlds.Run(sys, ctx, 24, false);
            CollectionAssert.AreEqual(new[] { b.Id, c.Id, a.Id }, order, "Dawn styles before Bronze, then placement order");
            Assert.AreEqual(1, MaintenanceSystem.RenovationsPerRun(SettlementTier.Village));
            Assert.AreEqual(2, MaintenanceSystem.RenovationsPerRun(SettlementTier.City));
            Assert.AreEqual(3, MaintenanceSystem.RenovationsPerRun(SettlementTier.Megalopolis));

            civ.Age = AgeId.Classical;
            s.Tier = SettlementTier.Metropolis;
            order.Clear();
            TestWorlds.Run(sys, ctx, 12, false);
            Assert.AreEqual(3, order.Count, "metropolis: three per run");
        }

        [Test]
        public void Houses_RenovateForFreeButSlower_WhenUnaffordable()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out var civ);
            var house = Data.Place(ctx, s, BuildingType.House, true);
            var store = Data.Place(ctx, s, BuildingType.Storehouse, true);
            civ.Age = AgeId.Bronze;
            Data.TownCenter(ctx, s).StyleAge = AgeId.Bronze;
            Data.ClearStock(s);
            TestWorlds.Run(new MaintenanceSystem(), ctx, 12 * 60);
            Assert.AreEqual(AgeId.Bronze, house.StyleAge, "free renovation eventually happens");
            Assert.AreEqual(AgeId.Dawn, store.StyleAge, "non-dwellings need resources");

            var ctx2 = Data.CreateContext();
            var t = Data.AddTown(ctx2, 2, out var civ2);
            var h2 = Data.Place(ctx2, t, BuildingType.House, true);
            civ2.Age = AgeId.Bronze;
            Data.TownCenter(ctx2, t).StyleAge = AgeId.Bronze;
            Data.Stock(t, 100f, 0f);
            TestWorlds.Run(new MaintenanceSystem(), ctx2, 12);
            Assert.AreEqual(AgeId.Bronze, h2.StyleAge, "affordable: renovates immediately");
            Assert.AreEqual(97f, t.Stockpile.Get(ResourceType.Wood), 1e-3f);
        }

        [Test]
        public void Conquest_RenovatesIntoConquerorStyle()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out var civ);
            var store = Data.Place(ctx, s, BuildingType.Storehouse, true);
            var elves = ctx.Ops.CreateCivilization(RaceId.Elf);
            ctx.Ops.TransferSettlement(s, elves);
            Data.TownCenter(ctx, s).StyleRace = RaceId.Elf;
            Data.Stock(s, 100f, 100f);
            TestWorlds.Run(new MaintenanceSystem(), ctx, 12);
            Assert.AreEqual(RaceId.Elf, store.StyleRace);
            Assert.AreEqual(AgeId.Dawn, store.StyleAge);
            Assert.IsNotNull(civ);
        }

        [Test]
        public void ObsoleteBuildings_DemolishedWhenEmpty_OnePerRun()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out var civ);
            var h1 = Data.Place(ctx, s, BuildingType.GathererHut, true);
            var h2 = Data.Place(ctx, s, BuildingType.GathererHut, true);
            var worker = Data.AddWorker(ctx, s, JobType.Gatherer, h1);
            civ.Age = AgeId.Iron; // MaxAge Bronze
            var causes = new List<DestroyCause>();
            ctx.Events.Subscribe<BuildingDestroyedEvent>(e => causes.Add(e.Cause));
            var sys = new MaintenanceSystem();
            TestWorlds.Run(sys, ctx, 12);
            Assert.IsFalse(h1.IsDestroyed, "still staffed");
            Assert.IsTrue(h2.IsDestroyed);
            Assert.AreEqual(AgeId.Dawn, h1.StyleAge, "obsolete buildings are not renovated");
            ctx.Ops.ClearJob(worker);
            TestWorlds.Run(sys, ctx, 12, false);
            Assert.IsTrue(h1.IsDestroyed);
            CollectionAssert.AreEqual(new[] { DestroyCause.Demolished, DestroyCause.Demolished }, causes);
            bool logged = false;
            foreach (var line in s.AiLog) if (line.Contains("obsolete")) logged = true;
            Assert.IsTrue(logged);

            var ctx2 = Data.CreateContext();
            var t = Data.AddTown(ctx2, 2, out var civ2);
            var a = Data.Place(ctx2, t, BuildingType.GathererHut, true);
            var b = Data.Place(ctx2, t, BuildingType.GathererHut, true);
            civ2.Age = AgeId.Classical;
            TestWorlds.Run(new MaintenanceSystem(), ctx2, 12);
            Assert.IsTrue(a.IsDestroyed);
            Assert.IsFalse(b.IsDestroyed, "at most one demolition per run");
        }

        [Test]
        public void MissingCivilization_StillDecays_NoRenovation()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 2, out _);
            var store = Data.Place(ctx, s, BuildingType.Storehouse, true);
            s.CivilizationId = 4242;
            Data.Stock(s, 100f, 100f);
            TestWorlds.Run(new MaintenanceSystem(), ctx, SimTime.TicksPerYear);
            Assert.AreEqual(1f - 0.005f, store.Condition, 1e-4f);
            Assert.AreEqual(1, store.Level);
            Assert.AreEqual(Year, (float)SimTime.TicksPerYear);
        }
    }
}
