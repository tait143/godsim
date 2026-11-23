using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Governor
{
    public class SettlementGovernorSystemTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = GovernorTestKit.CreateDb(); }

        private static void RunOnce(SettlementGovernorSystem gov, SimContext ctx)
        {
            ctx.State.Tick += gov.TickInterval;
            ctx.DeltaTicks = gov.TickInterval;
            gov.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        /// <summary>A town whose housing is comfortably sufficient (so other needs can be observed).</summary>
        private SimContext HousedTown(int adults, out Settlement s, out Civilization civ, int houses = -1)
        {
            var ctx = GovernorTestKit.CreateWorld(_db, adults, out s, out civ);
            int n = houses >= 0 ? houses : adults / 5 + 2;
            for (int i = 0; i < n; i++) GovernorTestKit.Place(ctx, s, BuildingType.House, 5);
            return ctx;
        }

        [Test]
        public void SystemContract()
        {
            var gov = new SettlementGovernorSystem();
            Assert.AreEqual(6, gov.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(gov.Name));
            Assert.IsNotNull(gov.Settings);
        }

        [Test]
        public void Homeless_HouseOrderedFirst_AndStacksUpToTwo()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 12, out var s, out _);
            s.Homeless = 12;
            var gov = new SettlementGovernorSystem();
            gov.Initialize(ctx);
            RunOnce(gov, ctx);
            Assert.IsTrue(s.BuildQueue.Count >= 1);
            Assert.AreEqual(BuildingType.House, s.BuildQueue[0].Type);
            Assert.AreEqual(2, GovernorTestKit.QueueCount(s, BuildingType.House), "big shortfall: two houses stack");
            Assert.IsTrue(s.BuildQueue[0].Priority > 5f, "priority = utility * 10: " + s.BuildQueue[0].Priority);
            Assert.AreEqual(ctx.Tick, s.BuildQueue[0].RequestedTick);
            Assert.IsTrue(s.BuildQueue[0].Reason.StartsWith("gov: "), s.BuildQueue[0].Reason);
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Build House (u=") && l.Contains("homeless 12")), string.Join("\n", s.AiLog));

            for (int i = 0; i < 10; i++) RunOnce(gov, ctx);
            Assert.IsTrue(GovernorTestKit.QueueCount(s, BuildingType.House) <= 2, "houses never stack beyond 2");
        }

        [Test]
        public void FreshCamp_BootstrapsFirstWoodProducerBeforeStockRunsOut()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 14, out var s, out _);
            GovernorTestKit.Flag(ctx, new Vec2i(s.Center.X + 7, s.Center.Y), 1, TileFlags.Trees);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx); // two houses (max two orders per run)
            RunOnce(gov, ctx);
            Assert.AreEqual(BuildingType.House, s.BuildQueue[0].Type);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.WoodcutterCamp), string.Join("\n", s.AiLog));
            float woodcutter = s.BuildQueue.Find(o => o.Type == BuildingType.WoodcutterCamp).Priority;
            foreach (var o in s.BuildQueue)
                if (o.Type != BuildingType.House) Assert.IsTrue(woodcutter >= o.Priority, "wood producer funded before other consumers of wood");
        }

        [Test]
        public void SmallShortfall_SingleHouseThenWaits()
        {
            // 10 people, 9 beds: one house closes the gap (target free = 2).
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out var s, out _);
            GovernorTestKit.Place(ctx, s, BuildingType.House, 5);
            GovernorTestKit.Place(ctx, s, BuildingType.House, 4);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.AreEqual(1, GovernorTestKit.QueueCount(s, BuildingType.House));
            for (int i = 0; i < 5; i++) RunOnce(gov, ctx);
            Assert.AreEqual(1, GovernorTestKit.QueueCount(s, BuildingType.House), "pending house capacity counts: no second house");
        }

        [Test]
        public void FreeHousing_NoHouseSpam()
        {
            var ctx = HousedTown(20, out var s, out _, 6); // 30 beds for 20 people
            var gov = new SettlementGovernorSystem();
            var planner = new InstantPlannerSystem();
            TestWorlds.RunAll(new List<ISimSystem> { gov, planner }, ctx, 120);
            Assert.IsFalse(planner.Built.Contains(BuildingType.House), "built: " + string.Join(",", planner.Built));
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.House));
        }

        [Test]
        public void FreeHousingFromPopulationMemory_IsRespected()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out var s, out _);
            s.Memory["pop.freeHousing"] = 6f;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.House), "population module says 6 beds are free");
            s.Memory["pop.freeHousing"] = 0f;
            RunOnce(gov, ctx);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.House));
        }

        [Test]
        public void FoodDeficit_OrdersFoodBuilding_AndRaisesFoodJobs()
        {
            var ctx = HousedTown(10, out var s, out _);
            s.Stockpile.SetRaw(ResourceType.Food, 2f);
            GovernorTestKit.SetCoverage(s, 1f);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsTrue(s.BuildQueue.Count > 0);
            var first = ctx.Db.Building(s.BuildQueue[0].Type);
            Assert.IsTrue(new BuildingProfile(first).ProducesFood, "first order " + first.Type);
            Assert.IsTrue(s.AiLog.Exists(l => l.Contains("food ") && l.Contains(" days")), string.Join("\n", s.AiLog));
            Assert.IsTrue(s.JobPriority[(int)JobType.Gatherer] >= 2f && s.JobPriority[(int)JobType.Gatherer] <= 3f);
            Assert.IsTrue(s.JobPriority[(int)JobType.Farmer] >= 2f);
        }

        [Test]
        public void FoodDaysFromEconomyMemory_IsUsed()
        {
            var ctx = HousedTown(10, out var s, out _);
            GovernorTestKit.SetCoverage(s, 1f);
            s.Stockpile.SetRaw(ResourceType.Food, 140f);
            s.Memory["econ.foodDays"] = 3f;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsTrue(gov.LastNeeds.Food > 0.8f, "food need " + gov.LastNeeds.Food);
            Assert.AreEqual(gov.LastNeeds.Food, s.Mem("ai.need.food"), 1e-6f);
            Assert.AreEqual(3f, gov.LastNeeds.FoodDays, 1e-4f);
        }

        [Test]
        public void UnavailableAgeGatedWonderAndCappedBuildings_NeverOrdered()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 14, out var s, out var civ);
            GovernorTestKit.Place(ctx, s, BuildingType.Well);                   // MaxPerSettlement 1 reached
            var gov = new SettlementGovernorSystem();
            var planner = new InstantPlannerSystem();
            var town = new FakeTownSystem { GrowEveryTicks = 10, MaxPopulation = 60 };
            TestWorlds.RunAll(new List<ISimSystem> { town, gov, planner }, ctx, 600);
            Assert.IsTrue(planner.Built.Count > 5, "the town did build: " + string.Join(",", planner.Built));
            Assert.IsFalse(planner.Built.Contains(BuildingType.Temple), "age-gated");
            Assert.IsFalse(planner.Built.Contains(BuildingType.Herbalist), "race-restricted");
            Assert.IsFalse(planner.Built.Contains(BuildingType.GreatLibrary), "wonder");
            Assert.IsFalse(planner.Built.Contains(BuildingType.Well), "MaxPerSettlement");
            Assert.IsFalse(planner.Built.Contains(BuildingType.TownCenter));
            Assert.IsFalse(planner.Built.Contains(BuildingType.StoneQuarry), "no stone in territory");
            Assert.IsFalse(planner.Built.Contains(BuildingType.WoodcutterCamp), "no trees in territory");
            Assert.IsTrue(planner.Built.Contains(BuildingType.Shrine), "faith is wanted and the shrine is buildable");
            Assert.AreEqual(AgeId.Dawn, civ.Age);
        }

        [Test]
        public void TechUnlockedBuilding_CanBeOrdered()
        {
            var ctx = HousedTown(14, out var s, out var civ);
            GovernorTestKit.SetCoverage(s, 1f);
            s.ServiceCoverage[(int)ServiceType.Faith] = 0f;
            civ.UnlockedBuildings[(int)BuildingType.Temple] = true;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Temple), string.Join("\n", s.AiLog));
        }

        [Test]
        public void QuarryNeedsStoneInTerritory()
        {
            // Stone is badly needed (no stone, stone costs everywhere) but there is no deposit: never a quarry.
            var ctx = HousedTown(14, out var s, out _);
            GovernorTestKit.SetCoverage(s, 1f);
            s.Stockpile.SetRaw(ResourceType.Stone, 0f);
            s.Stockpile.SetRaw(ResourceType.Wood, 140f);
            var gov = new SettlementGovernorSystem();
            for (int i = 0; i < 20; i++) RunOnce(gov, ctx);
            Assert.IsTrue(gov.LastNeeds.Resource(ResourceType.Stone) > 0.3f, "stone need " + gov.LastNeeds.Resource(ResourceType.Stone));
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.StoneQuarry));

            // Same situation with a deposit in reach: the quarry is ordered.
            var ctx2 = HousedTown(14, out var s2, out _);
            GovernorTestKit.SetCoverage(s2, 1f);
            s2.Stockpile.SetRaw(ResourceType.Stone, 0f);
            s2.Stockpile.SetRaw(ResourceType.Wood, 140f);
            GovernorTestKit.Flag(ctx2, new Vec2i(s2.Center.X + 5, s2.Center.Y + 5), 1, TileFlags.StoneDeposit);
            var gov2 = new SettlementGovernorSystem();
            RunOnce(gov2, ctx2);
            Assert.IsTrue(GovernorTestKit.QueueHas(s2, BuildingType.StoneQuarry), string.Join("\n", s2.AiLog));
            Assert.AreEqual(1f, s2.Mem("ai.terr.stone"));
        }

        [Test]
        public void QueueLength_IsBounded()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 40, out var s, out _);
            s.Homeless = 40;
            s.Stockpile.SetRaw(ResourceType.Food, 1f);
            GovernorTestKit.Flag(ctx, s.Center, 9, TileFlags.Trees | TileFlags.StoneDeposit);
            var gov = new SettlementGovernorSystem();
            for (int i = 0; i < 60; i++)
            {
                RunOnce(gov, ctx);
                Assert.IsTrue(s.BuildQueue.Count <= 4, "queue " + s.BuildQueue.Count);
                Assert.IsTrue(GovernorTestKit.QueueCount(s, BuildingType.House) <= 2);
                foreach (var t in EnumUtil.AllBuildings)
                    if (t != BuildingType.House) Assert.IsTrue(GovernorTestKit.QueueCount(s, t) <= 1, t.ToString());
            }
            Assert.AreEqual(4, s.BuildQueue.Count, "the governor fills the queue when needs are high");
        }

        [Test]
        public void UrgentNeed_EvictsLowPriorityGovernorOrder_ButNeverForeignOrders()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out var s, out _);
            for (int i = 0; i < 3; i++) s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Storehouse, Priority = 1f, RequestedTick = 0, Reason = "gov: filler" });
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Barracks, Priority = 0.5f, RequestedTick = 0, Reason = "strategy: keep" });
            s.Homeless = 10;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.AreEqual(4, s.BuildQueue.Count);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.House));
            Assert.IsTrue(s.BuildQueue.Exists(o => o.Reason == "strategy: keep"), "orders from other modules are never evicted");
            Assert.AreEqual(2, GovernorTestKit.QueueCount(s, BuildingType.Storehouse), "only one eviction per run");
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Dropped order Storehouse")));
        }

        [Test]
        public void War_OrdersBarracks_AndSoldierPriority()
        {
            var ctx = HousedTown(12, out var s, out var civ);
            GovernorTestKit.SetCoverage(s, 1f);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(55, 55), 4, out var enemy);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.AreEqual(0.5f, s.JobPriority[(int)JobType.Soldier], 1e-5f, "peaceful");
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.Barracks));

            civ.Memory["ai.posture"] = 1f;
            RunOnce(gov, ctx);
            Assert.AreEqual(1.5f, s.JobPriority[(int)JobType.Soldier], 1e-5f, "defensive posture");

            civ.Memory["ai.posture"] = 0f;
            var rel = ctx.State.Diplomacy.Get(civ.Id, enemy.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.War;
            rel.WarId = 1;
            s.BuildQueue.Clear();
            RunOnce(gov, ctx);
            Assert.IsTrue(ctx.State.Diplomacy.AreAtWar(civ.Id, enemy.Id));
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Barracks), string.Join("\n", s.AiLog));
            Assert.AreEqual(BuildingType.Barracks, s.BuildQueue[0].Type, "defense is the top need at war");
            Assert.AreEqual(2.5f, s.JobPriority[(int)JobType.Soldier], 1e-5f);
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Build Barracks") && l.Contains("at war")));
        }

        [Test]
        public void HostileNeighbourWithinRadius_RaisesDefense()
        {
            var ctx = HousedTown(12, out var s, out var civ);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(55, 50), 4, out var orcs);
            var rel = ctx.State.Diplomacy.Get(civ.Id, orcs.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Hostile;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.AreEqual(1.5f, s.JobPriority[(int)JobType.Soldier], 1e-5f);
            Assert.IsTrue(gov.LastNeeds.Defense() > 0.3f, "defense need " + gov.LastNeeds.Defense());
            Assert.AreEqual(0.65f, s.Mem("ai.gov.threat"), 1e-5f);
        }

        [Test]
        public void JobPriorities_WrittenEveryRun()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out var s, out _);
            s.Homeless = 10;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            var race = ctx.Db.Race(RaceId.Human);
            Assert.AreEqual(1.5f, s.JobPriority[(int)JobType.Builder], 1e-5f, "queue is not empty");
            Assert.AreEqual(0.5f, s.JobPriority[(int)JobType.Soldier], 1e-5f);
            Assert.IsTrue(s.JobPriority[(int)JobType.Scholar] >= 0.75f + race.Curiosity - 1e-4f);
            Assert.IsTrue(s.JobPriority[(int)JobType.Priest] >= 0.75f + race.Piety - 1e-4f);
            Assert.AreEqual(0f, s.JobPriority[(int)JobType.None]);
            for (int j = 1; j < s.JobPriority.Length; j++)
                Assert.IsTrue(s.JobPriority[j] >= 0.25f && s.JobPriority[j] <= 3f, ((JobType)j) + "=" + s.JobPriority[j]);

            s.BuildQueue.Clear();
            s.Homeless = 0;
            s.Memory["pop.freeHousing"] = 10f;
            GovernorTestKit.SetCoverage(s, 1f);
            s.Stockpile.SetRaw(ResourceType.Food, 140f);
            RunOnce(gov, ctx);
            if (s.BuildQueue.Count == 0) Assert.AreEqual(1f, s.JobPriority[(int)JobType.Builder], 1e-5f);
        }

        [Test]
        public void ProductionChain_ConsumerWithoutInputs_OrdersProducer()
        {
            var db = GovernorTestKit.CreateDb(GovernorTestKit.AddChainBuildings);
            var ctx = GovernorTestKit.CreateWorld(db, 12, out var s, out _);
            for (int i = 0; i < 5; i++) GovernorTestKit.Place(ctx, s, BuildingType.House, 5);
            GovernorTestKit.Place(ctx, s, BuildingType.Smelter);
            GovernorTestKit.SetCoverage(s, 1f);
            GovernorTestKit.Flag(ctx, new Vec2i(s.Center.X - 5, s.Center.Y + 4), 0, TileFlags.OreDeposit);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsTrue(gov.LastNeeds.Resource(ResourceType.Ore) >= 0.45f, "ore need " + gov.LastNeeds.Resource(ResourceType.Ore));
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Mine), string.Join("\n", s.AiLog));
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Build Mine") && l.Contains("Smelter lacks ore")), string.Join("\n", s.AiLog));
        }

        [Test]
        public void FullStorage_OrdersStorage()
        {
            var ctx = HousedTown(10, out var s, out _);
            GovernorTestKit.SetCoverage(s, 1f);
            s.Stockpile.SetRaw(ResourceType.Wood, 148f);
            s.Stockpile.SetRaw(ResourceType.Food, 80f);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsTrue(gov.LastNeeds.Values[SettlementNeeds.Storage] >= 0.35f, "storage need " + gov.LastNeeds.Values[SettlementNeeds.Storage]);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Storehouse), string.Join("\n", s.AiLog));
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Build Storehouse") && l.Contains("wood at 99%")), string.Join("\n", s.AiLog));
        }

        [Test]
        public void Storage_NotReorderedWhenCapacityDoesNotGrow()
        {
            var ctx = HousedTown(10, out var s, out _);
            GovernorTestKit.SetCoverage(s, 1f);
            var gov = new SettlementGovernorSystem();
            var planner = new InstantPlannerSystem();
            gov.Initialize(ctx);
            for (int t = 0; t < 2 * SimTime.TicksPerYear; t++)
            {
                s.Stockpile.SetRaw(ResourceType.Wood, 148f); // economy never credits the new storage capacity
                s.Stockpile.SetRaw(ResourceType.Food, 80f);
                ctx.State.Tick++;
                if (ctx.State.Tick % gov.TickInterval == 0) gov.Tick(ctx);
                planner.Tick(ctx);
            }
            int storehouses = 0;
            foreach (var b in planner.Built) if (b == BuildingType.Storehouse) storehouses++;
            Assert.IsTrue(storehouses >= 1 && storehouses <= 2, "storehouses " + storehouses);
        }

        [Test]
        public void MissingTownCenter_IsRebuiltFirst()
        {
            var ctx = HousedTown(10, out var s, out _);
            var tc = ctx.State.Buildings.Get(s.TownCenterId);
            ctx.Ops.DestroyBuilding(tc, DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            s.Stockpile.SetRaw(ResourceType.Food, 2f);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.AreEqual(BuildingType.TownCenter, s.BuildQueue[0].Type);
            Assert.AreEqual(12f, s.BuildQueue[0].Priority, 1e-4f);
            Assert.AreEqual(1, GovernorTestKit.QueueCount(s, BuildingType.TownCenter), "not ordered twice in one run");

            // Full queue of governor orders: the town center still gets in (evicting the least urgent one).
            s.BuildQueue.Clear();
            for (int i = 0; i < 4; i++) s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Shrine, Priority = 2f + i, RequestedTick = ctx.Tick, Reason = "gov: filler" });
            RunOnce(gov, ctx);
            Assert.AreEqual(1, GovernorTestKit.QueueCount(s, BuildingType.TownCenter));
            Assert.AreEqual(4, s.BuildQueue.Count);
            Assert.IsFalse(s.BuildQueue.Exists(o => o.Type == BuildingType.Shrine && o.Priority == 2f), "lowest-priority filler evicted");
            RunOnce(gov, ctx);
            Assert.AreEqual(1, GovernorTestKit.QueueCount(s, BuildingType.TownCenter), "not ordered twice");
        }

        [Test]
        public void StaleAndInvalidOrders_Dropped_WondersKept()
        {
            var ctx = HousedTown(10, out var s, out _);
            ctx.State.Tick = 1000;
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Shrine, Priority = 5f, RequestedTick = 1000 - 3 * 96 - 10, Reason = "gov: old" });
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.GreatLibrary, Priority = 5f, RequestedTick = 0, Reason = "wonder: Great Library" });
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Temple, Priority = 5f, RequestedTick = 990, Reason = "strategy" });
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.TownCenter, Priority = 5f, RequestedTick = 990, Reason = "strategy" });
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsFalse(s.BuildQueue.Exists(o => o.Type == BuildingType.Shrine && o.Reason == "gov: old"), "stale");
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.Temple), "not available");
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.TownCenter), "limit reached");
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.GreatLibrary), "wonder orders belong to civ strategy");
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Dropped order Shrine: stale")), string.Join("\n", s.AiLog));
        }

        [Test]
        public void RepeatedPlacementFailures_BlockTheType()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out var s, out _);
            s.Homeless = 10;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            foreach (var o in s.BuildQueue) if (o.Type == BuildingType.House) o.Attempts = 5;
            RunOnce(gov, ctx);
            Assert.IsTrue(s.Mem("ai.gov.block.House", -1f) > ctx.Tick);
            s.BuildQueue.Clear(); // planner gave up
            RunOnce(gov, ctx);
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.House), "blocked for a while after repeated failures");
        }

        [Test]
        public void JobBuildings_PenalisedWithoutWorkers()
        {
            var ctx = HousedTown(10, out var s, out _);
            GovernorTestKit.SetCoverage(s, 1f);
            GovernorTestKit.Flag(ctx, s.Center, 9, TileFlags.Trees);
            s.Stockpile.SetRaw(ResourceType.Wood, 0f);
            // everyone employed already
            foreach (int id in s.ResidentIds) ctx.State.People.Get(id).Job = JobType.Laborer;
            s.Unemployed = 0;
            var gov = new SettlementGovernorSystem();
            ctx.State.Tick += 6;
            gov.GovernSettlement(ctx, s);
            float busy = WoodcutterUtility(s);
            var ctx2 = HousedTown(10, out var s2, out _);
            GovernorTestKit.SetCoverage(s2, 1f);
            GovernorTestKit.Flag(ctx2, s2.Center, 9, TileFlags.Trees);
            s2.Stockpile.SetRaw(ResourceType.Wood, 0f);
            s2.Unemployed = 6;
            var gov2 = new SettlementGovernorSystem();
            ctx2.State.Tick += 6;
            gov2.GovernSettlement(ctx2, s2);
            float idle = WoodcutterUtility(s2);
            Assert.IsTrue(idle > 0f, string.Join("\n", s2.AiLog));
            Assert.IsTrue(busy < idle * 0.5f, "busy " + busy + " idle " + idle);
        }

        private static float WoodcutterUtility(Settlement s)
        {
            foreach (var o in s.BuildQueue) if (o.Type == BuildingType.WoodcutterCamp) return o.Priority / 10f;
            return 0f;
        }

        [Test]
        public void DeadSettlement_DeadCiv_AndEmptyTown_AreSafe()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out var s, out var civ);
            s.Homeless = 10;
            var gov = new SettlementGovernorSystem();
            s.IsAlive = false;
            RunOnce(gov, ctx);
            Assert.AreEqual(0, s.BuildQueue.Count);
            s.IsAlive = true;
            civ.IsAlive = false;
            RunOnce(gov, ctx);
            Assert.AreEqual(0, s.BuildQueue.Count);
            civ.IsAlive = true;
            s.CivilizationId = 999;
            RunOnce(gov, ctx);
            Assert.AreEqual(0, s.BuildQueue.Count);
            s.CivilizationId = civ.Id;

            var empty = ctx.Ops.FoundSettlement(civ, new Vec2i(10, 10));
            RunOnce(gov, ctx);
            Assert.AreEqual(0, empty.BuildQueue.Count, "nobody lives there: nothing to build");
            Assert.AreEqual(0f, empty.JobPriority[(int)JobType.None]);
            Assert.AreEqual(1.5f, s.JobPriority[(int)JobType.Builder], 1e-5f);
        }

        [Test]
        public void EmptyTerritory_OnlyOneOrderAtATime()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out var s, out _);
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++) if (map.BuildingAt[i] < 0) map.Set(i, TileFlags.Road); // no free tile anywhere
            s.Homeless = 10;
            var gov = new SettlementGovernorSystem();
            for (int i = 0; i < 5; i++) RunOnce(gov, ctx);
            Assert.AreEqual(0f, s.Mem(TerritoryScanner.KeyFree, -1f));
            Assert.AreEqual(1, s.BuildQueue.Count);
        }

        [Test]
        public void BadData_DoesNotThrow()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 6, out var s, out _);
            s.ServiceCoverage[0] = float.NaN;
            s.Stockpile.Capacity = 0f;
            s.Memory["econ.foodDays"] = float.PositiveInfinity;
            s.Memory["pop.freeHousing"] = float.NaN;
            s.BuildQueue.Add(null);
            s.BuildQueue.Add(new BuildOrder { Type = (BuildingType)200, Reason = null });
            s.Center = new Vec2i(-50, 9999);
            s.TerritoryRadius = 5000;
            s.ResidentIds.Add(123456); // dangling id
            var gov = new SettlementGovernorSystem();
            for (int i = 0; i < 4; i++) RunOnce(gov, ctx); // strict-mode context: any exception fails the test
            foreach (var o in s.BuildQueue)
            {
                Assert.IsNotNull(o);
                Assert.IsFalse(float.IsNaN(o.Priority) || float.IsInfinity(o.Priority));
            }
            foreach (var kv in s.Memory)
                if (kv.Key.StartsWith("ai.")) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            foreach (var v in s.JobPriority) Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
        }

        [Test]
        public void NewAgeBuildings_AreAdopted()
        {
            var ctx = HousedTown(14, out var s, out var civ);
            GovernorTestKit.SetCoverage(s, 1f);
            s.ServiceCoverage[(int)ServiceType.Faith] = 0f;
            civ.Age = AgeId.Iron; // Temple (Iron, 8 faith) now available alongside the Dawn-age Shrine
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Temple), string.Join("\n", s.AiLog));
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.Shrine), "the stronger current-age building wins");
        }

        [Test]
        public void ObsoleteBuildings_NotOrderedAfterTheirMaxAge()
        {
            var ctx = HousedTown(10, out var s, out var civ);
            GovernorTestKit.SetCoverage(s, 1f);
            s.Stockpile.SetRaw(ResourceType.Food, 1f);
            civ.Age = AgeId.Classical; // GathererHut MaxAge = Iron
            var gov = new SettlementGovernorSystem();
            for (int i = 0; i < 5; i++) RunOnce(gov, ctx);
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.GathererHut));
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Farm));
        }

        [Test]
        public void LongRun_HousingTracksPopulation_NoSpamNoStall()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 8, out var s, out _);
            var gov = new SettlementGovernorSystem();
            var planner = new InstantPlannerSystem();
            var town = new FakeTownSystem { GrowEveryTicks = 8, MaxPopulation = 400 };
            TestWorlds.RunAll(new List<ISimSystem> { town, gov, planner }, ctx, 10 * SimTime.TicksPerYear);
            int pop = s.Population;
            int houses = 0, beds = 0, others = 0;
            foreach (int id in s.BuildingIds)
            {
                var b = ctx.State.Buildings.Get(id);
                if (b.Type == BuildingType.House) { houses++; beds += b.HousingCapacity; }
                else others++;
            }
            Assert.IsTrue(pop > 100, "pop " + pop);
            Assert.IsTrue(beds >= pop, "beds " + beds + " pop " + pop);
            Assert.IsTrue(beds - pop <= 0.08f * pop + 12, "no house spam: beds " + beds + " pop " + pop);
            Assert.IsTrue(houses >= pop / 5 && houses <= pop / 3 + 3, "one house per 3-5 people: " + houses + " for " + pop);
            Assert.IsTrue(others >= 3 && others < 60, "other buildings " + others + ": " + string.Join(",", planner.Built));
            Assert.IsTrue(s.AiLog.Count > 0);
        }

        [Test]
        public void SameSeed_IsDeterministic()
        {
            string a = RunScenario(42);
            string b = RunScenario(42);
            Assert.AreEqual(a, b);
            Assert.IsTrue(a.Length > 100);
        }

        private string RunScenario(ulong seed)
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 9, out var s, out _, seed);
            GovernorTestKit.Flag(ctx, new Vec2i(s.Center.X + 6, s.Center.Y), 1, TileFlags.Trees | TileFlags.StoneDeposit);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(12, 12), 7, out _);
            var systems = new List<ISimSystem> { new FakeTownSystem { GrowEveryTicks = 5 }, new SettlementGovernorSystem(), new InstantPlannerSystem() };
            TestWorlds.RunAll(systems, ctx, 100);
            return GovernorTestKit.Signature(ctx);
        }
    }

    internal static class GovernorTestExtensions
    {
        public static float Defense(this SettlementNeeds n) => n.Values[SettlementNeeds.Defense];
    }
}
