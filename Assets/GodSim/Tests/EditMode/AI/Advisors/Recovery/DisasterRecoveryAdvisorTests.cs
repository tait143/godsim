using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class DisasterRecoveryAdvisorTests
    {
        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.TownCenter, Name = "Town Center", Category = BuildingCategory.Civic, MaxPerSettlement = 1, Width = 2, Height = 2, Cost = new[] { new ResourceAmount(ResourceType.Wood, 40f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5, Cost = new[] { new ResourceAmount(ResourceType.Wood, 12f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure, Services = new[] { new ServiceAmount(ServiceType.Water, 2f) }, Cost = new[] { new ResourceAmount(ResourceType.Stone, 5f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.Palisade, Name = "Palisade", Category = BuildingCategory.Military, Shape = BuildingShape.Wall, MinAge = AgeId.Bronze, Services = new[] { new ServiceAmount(ServiceType.Defense, 2f) }, Cost = new[] { new ResourceAmount(ResourceType.Wood, 30f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.StoneWall, Name = "Stone Wall", Category = BuildingCategory.Military, Shape = BuildingShape.Wall, MinAge = AgeId.Classical, MaxAge = AgeId.Industrial, Services = new[] { new ServiceAmount(ServiceType.Defense, 4f) }, Cost = new[] { new ResourceAmount(ResourceType.Stone, 30f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.Granary, Name = "Granary", Category = BuildingCategory.Storage, StorageCapacity = 150f, MaxPerSettlement = 3, Width = 2, Height = 2 });
            db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, Job = JobType.Farmer, JobSlots = 2, OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, 0.3f) }, Placement = PlacementRule.Fertile });
            db.Register(new BuildingDefinition { Type = BuildingType.FishingHut, Name = "Fishing Hut", Category = BuildingCategory.Food, Job = JobType.Fisher, JobSlots = 2, OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, 0.3f) }, Placement = PlacementRule.NearWater });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Building Place(SimContext ctx, Settlement s, BuildingType t)
        {
            var def = ctx.Db.Building(t);
            var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 20);
            Assert.IsTrue(site.IsValid, "no site for " + t);
            var b = ctx.Ops.CreateBuilding(s, t, site, 0, true);
            Assert.IsNotNull(b);
            return b;
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].IndexOf(text, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        [Test]
        public void FireDestroyingHouses_VotesHousesAndWells_ThatDecay()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            var houses = new List<Building>();
            for (int i = 0; i < 4; i++) houses.Add(Place(ctx, s, BuildingType.House));
            var systems = new List<ISimSystem> { new AdvisorCouncilSystem(), new DisasterRecoveryAdvisor() };
            TestWorlds.RunAll(systems, ctx, 0);

            for (int i = 0; i < houses.Count; i++) ctx.Ops.DestroyBuilding(houses[i], DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            s.Homeless = 6;
            Assert.AreEqual(4f, RecoveryLedger.Lost(s, BuildingType.House), 1e-4f);

            TestWorlds.RunAll(systems, ctx, 48);
            float house0 = AdvisorBoard.BuildBias(s, BuildingType.House);
            float well0 = AdvisorBoard.BuildBias(s, BuildingType.Well);
            Assert.Greater(house0, 0.5f, "strong rebuild vote for houses");
            Assert.Greater(well0, 0.3f, "fire teaches wells");
            Assert.IsTrue(LogContains(s, "Rebuilding after the Great Fire: 4 houses lost"), string.Join("\n", s.AiLog));
            Assert.IsTrue(LogContains(s, "wells"), string.Join("\n", s.AiLog));
            Assert.Greater(AdvisorBoard.Urgency(s, DisasterRecoveryAdvisor.Id), 0.5f);
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Builder), 0f, "builders are prioritized while rebuilding");

            TestWorlds.RunAll(systems, ctx, SimTime.TicksPerYear * 2);
            float house1 = AdvisorBoard.BuildBias(s, BuildingType.House);
            float well1 = AdvisorBoard.BuildBias(s, BuildingType.Well);
            Assert.Less(house1, house0 * 0.6f, "rebuild votes fade over the years");
            Assert.Greater(well1, 0.1f, "lessons outlast the rebuilding");

            TestWorlds.RunAll(systems, ctx, SimTime.TicksPerYear * 2);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Well), well0, "the lesson fades too");
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.House), 0.05f, "plan over after ~3 years");
            Assert.IsFalse(s.Memory.ContainsKey(RecoveryKeys.Lost[(int)BuildingType.House]));
        }

        [Test]
        public void RebuiltHouses_AreCreditedAgainstTheLedger()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            var adv = new DisasterRecoveryAdvisor();
            var a = Place(ctx, s, BuildingType.House);
            var b = Place(ctx, s, BuildingType.House);
            TestWorlds.Run(adv, ctx, 0);
            ctx.Ops.DestroyBuilding(a, DestroyCause.Fire);
            ctx.Ops.DestroyBuilding(b, DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(2f, RecoveryLedger.Lost(s, BuildingType.House), 1e-4f);
            Place(ctx, s, BuildingType.House);
            Assert.AreEqual(1f, RecoveryLedger.Lost(s, BuildingType.House), 1e-4f);
        }

        [Test]
        public void DestroyedTownCenter_GetsTopPriority()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            var h1 = Place(ctx, s, BuildingType.House);
            var h2 = Place(ctx, s, BuildingType.House);
            var adv = new DisasterRecoveryAdvisor();
            TestWorlds.Run(adv, ctx, 0);
            var tc = ctx.State.Buildings.Get(s.TownCenterId);
            Assert.IsNotNull(tc);
            ctx.Ops.DestroyBuilding(h1, DestroyCause.War);
            ctx.Ops.DestroyBuilding(tc, DestroyCause.War);
            ctx.Ops.DestroyBuilding(h2, DestroyCause.War);
            ctx.Ops.FlushRemovals();
            s.Homeless = 4;

            TestWorlds.Run(adv, ctx, 24, false);
            float tcBias = AdvisorBoard.BuildBias(s, BuildingType.TownCenter);
            float houseBias = AdvisorBoard.BuildBias(s, BuildingType.House);
            Assert.GreaterOrEqual(tcBias, 0.9f);
            Assert.Greater(tcBias, houseBias);
            Assert.Greater(houseBias, 0.3f);
            Assert.GreaterOrEqual(AdvisorBoard.Urgency(s, DisasterRecoveryAdvisor.Id), 0.95f);
            Assert.IsTrue(LogContains(s, "town center lost (top priority)"), string.Join("\n", s.AiLog));
            Assert.IsTrue(LogContains(s, "after the war"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void WarDamage_VotesWalls_GatedByAvailability()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(32, 32), 12, out var civ);
            civ.Age = AgeId.Iron;
            var hs = new List<Building>();
            for (int i = 0; i < 3; i++) hs.Add(Place(ctx, s, BuildingType.House));
            var adv = new DisasterRecoveryAdvisor();
            TestWorlds.Run(adv, ctx, 0);
            for (int i = 0; i < hs.Count; i++) ctx.Ops.DestroyBuilding(hs[i], DestroyCause.War);
            ctx.Ops.FlushRemovals();

            TestWorlds.Run(adv, ctx, 24, false);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Palisade), 0.3f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.StoneWall), "Stone walls are not available in the Iron age");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Soldier), 0f);

            civ.Age = AgeId.Classical;
            TestWorlds.Run(adv, ctx, 24, false);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.StoneWall), 0.3f, "the best available wall wins once unlocked");
        }

        [Test]
        public void Famine_VotesGranary_AndViableFoodDiversity()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            var adv = new DisasterRecoveryAdvisor();
            TestWorlds.Run(adv, ctx, 0);
            ctx.Events.Publish(new FamineEvent { SettlementId = s.Id, Shortfall = 5f });

            TestWorlds.Run(adv, ctx, 24, false);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Granary), 0.3f);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Farm), 0f, "diversify food on fertile land");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.FishingHut), "no water nearby: no fishing hut");
            Assert.Greater(RecoveryLedger.CivScar(civ, RecoveryScar.Famine), 0f, "the civilization remembers");
        }

        [Test]
        public void CulturalMemory_MakesOtherTownsPreventive()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(16, 16), 12, out var civ);
            var other = ctx.Ops.FoundSettlement(civ, new Vec2i(48, 48));
            for (int i = 0; i < 8; i++) ctx.Ops.CreatePerson(other, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 25f);
            var hs = new List<Building>();
            for (int i = 0; i < 4; i++) hs.Add(Place(ctx, s, BuildingType.House));
            var adv = new DisasterRecoveryAdvisor();
            TestWorlds.Run(adv, ctx, 0);
            for (int i = 0; i < hs.Count; i++) ctx.Ops.DestroyBuilding(hs[i], DestroyCause.Fire);
            ctx.Ops.FlushRemovals();

            TestWorlds.Run(adv, ctx, 24, false);
            Assert.Greater(AdvisorBoard.BuildBias(other, BuildingType.Well), 0f, "untouched towns of the civ also dig wells");
            Assert.Less(AdvisorBoard.BuildBias(other, BuildingType.Well), AdvisorBoard.BuildBias(s, BuildingType.Well));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(other, BuildingType.House), "but they rebuild nothing");
        }

        [Test]
        public void DestroyedSettlement_GetsNoRecoveryPlan()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(16, 16), 6, out var civ);
            var refuge = ctx.Ops.FoundSettlement(civ, new Vec2i(48, 48));
            Place(ctx, s, BuildingType.House);
            var adv = new DisasterRecoveryAdvisor();
            TestWorlds.Run(adv, ctx, 0);
            ctx.Ops.DestroySettlement(s, DestroyCause.War);
            ctx.Ops.FlushRemovals();
            TestWorlds.Run(adv, ctx, 12, false);
            Assert.IsFalse(s.Memory.ContainsKey(RecoveryKeys.LastTick));
            Assert.IsFalse(refuge.Memory.ContainsKey(RecoveryKeys.LastTick));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.House));
        }

        [Test]
        public void SmallSettlement_SpreadsRebuildingOverRuns()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var small = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(16, 16), 2, out var c1);
            var big = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(48, 48), 30, out var c2);
            var types = new[] { BuildingType.Campfire, BuildingType.SpiritTotem, BuildingType.Shrine, BuildingType.Weaver, BuildingType.Herbalist, BuildingType.Market };
            var doomed = new List<Building>();
            for (int k = 0; k < 2; k++)
                for (int i = 0; i < types.Length; i++) { doomed.Add(Place(ctx, small, types[i])); doomed.Add(Place(ctx, big, types[i])); }
            var adv = new DisasterRecoveryAdvisor();
            TestWorlds.Run(adv, ctx, 0);
            for (int i = 0; i < doomed.Count; i++) ctx.Ops.DestroyBuilding(doomed[i], DestroyCause.Disaster);
            ctx.Ops.FlushRemovals();

            TestWorlds.Run(adv, ctx, 24, false);
            float sumSmall = 0f, sumBig = 0f;
            for (int i = 0; i < types.Length; i++) { sumSmall += AdvisorBoard.BuildBias(small, types[i]); sumBig += AdvisorBoard.BuildBias(big, types[i]); }
            Assert.Greater(sumSmall, 0f);
            Assert.Less(sumSmall, sumBig * 0.8f, "a hamlet cannot raise everything at once");
        }

        [Test]
        public void Flood_PrefersStorageAndHighGroundHint()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(32, 32), 12, out var civ);
            var adv = new DisasterRecoveryAdvisor();
            TestWorlds.Run(adv, ctx, 0);
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Flood, Position = new Vec2i(33, 33), Radius = 4, Severity = 1f });
            TestWorlds.Run(adv, ctx, 24, false);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Granary), 0f);
            Assert.Greater(s.Mem(RecoveryKeys.HintHighGround), 0.3f);
            Assert.AreEqual(RecoveryCatastrophe.Flood, RecoveryLedger.Kind(s));
        }

        [Test]
        public void Deterministic_AndFinite()
        {
            var a = Scenario();
            var b = Scenario();
            Assert.AreEqual(a.Memory.Count, b.Memory.Count);
            foreach (var kv in a.Memory)
            {
                Assert.IsTrue(b.Memory.TryGetValue(kv.Key, out float v), kv.Key);
                Assert.AreEqual(kv.Value, v, kv.Key);
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            }
        }

        private static Settlement Scenario()
        {
            var ctx = TestWorlds.CreateContext(CreateDb(), 64, 7);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 15, out var civ);
            civ.Age = AgeId.Bronze;
            var hs = new List<Building>();
            for (int i = 0; i < 5; i++) hs.Add(Place(ctx, s, BuildingType.House));
            var systems = new List<ISimSystem> { new AdvisorCouncilSystem(), new DisasterRecoveryAdvisor() };
            TestWorlds.RunAll(systems, ctx, 0);
            ctx.Ops.DestroyBuilding(hs[0], DestroyCause.Fire);
            ctx.Ops.DestroyBuilding(hs[1], DestroyCause.War);
            ctx.Ops.DestroyBuilding(hs[2], DestroyCause.DivinePower);
            ctx.Ops.FlushRemovals();
            ctx.Events.Publish(new FamineEvent { SettlementId = s.Id, Shortfall = 3f });
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Plague, Position = s.Center, Radius = 3, Severity = float.NaN });
            s.Homeless = 3;
            TestWorlds.RunAll(systems, ctx, 150);
            return s;
        }
    }
}
