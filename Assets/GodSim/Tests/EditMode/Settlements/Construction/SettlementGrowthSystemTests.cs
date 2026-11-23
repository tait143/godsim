using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;
using Data = GodSim.Tests.Settlements.Construction.ConstructionTestData;

namespace GodSim.Tests.Settlements.Construction
{
    public class SettlementGrowthSystemTests
    {
        [TestCase(0, SettlementTier.Camp)]
        [TestCase(9, SettlementTier.Camp)]
        [TestCase(10, SettlementTier.Hamlet)]
        [TestCase(30, SettlementTier.Village)]
        [TestCase(79, SettlementTier.Village)]
        [TestCase(80, SettlementTier.Town)]
        [TestCase(200, SettlementTier.City)]
        [TestCase(500, SettlementTier.Metropolis)]
        [TestCase(1200, SettlementTier.Megalopolis)]
        [TestCase(99999, SettlementTier.Megalopolis)]
        public void EvaluateTier_Upgrades(int pop, SettlementTier expected)
        {
            Assert.AreEqual(expected, SettlementGrowthSystem.EvaluateTier(SettlementTier.Camp, pop));
        }

        [TestCase(SettlementTier.Village, 24, SettlementTier.Village)]
        [TestCase(SettlementTier.Village, 23, SettlementTier.Hamlet)]
        [TestCase(SettlementTier.Town, 64, SettlementTier.Town)]
        [TestCase(SettlementTier.Town, 63, SettlementTier.Village)]
        [TestCase(SettlementTier.City, 5, SettlementTier.Camp)]
        [TestCase(SettlementTier.Hamlet, 8, SettlementTier.Hamlet)]
        [TestCase(SettlementTier.Hamlet, 7, SettlementTier.Camp)]
        [TestCase(SettlementTier.Camp, -5, SettlementTier.Camp)]
        public void EvaluateTier_DowngradesWithHysteresis(SettlementTier current, int pop, SettlementTier expected)
        {
            Assert.AreEqual(expected, SettlementGrowthSystem.EvaluateTier(current, pop));
        }

        [Test]
        public void TerritoryRadius_GrowsWithTier()
        {
            int[] expected = { 6, 8, 10, 13, 16, 20, 24 };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], SettlementGrowthSystem.TerritoryRadiusFor((SettlementTier)i));
        }

        [Test]
        public void Tier_UpgradesAndDowngrades_WithEventsAndTerritory()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 35, out _);
            var events = new List<SettlementTierChangedEvent>();
            ctx.Events.Subscribe<SettlementTierChangedEvent>(e => events.Add(e));
            int territory = 0;
            ctx.Events.Subscribe<TerritoryChangedEvent>(e => { if (e.SettlementId == s.Id) territory++; });
            int ownedBefore = CountOwned(ctx, s);
            var sys = new SettlementGrowthSystem();

            TestWorlds.Run(sys, ctx, 24);
            Assert.AreEqual(SettlementTier.Village, s.Tier);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(SettlementTier.Camp, events[0].OldTier);
            Assert.AreEqual(SettlementTier.Village, events[0].NewTier);
            Assert.AreEqual(s.Id, events[0].SettlementId);
            Assert.AreEqual(10, s.TerritoryRadius);
            Assert.Greater(CountOwned(ctx, s), ownedBefore);
            Assert.GreaterOrEqual(territory, 1);

            KillResidents(ctx, s, 10); // 25 >= 24 (80% of 30): stays
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.AreEqual(SettlementTier.Village, s.Tier);
            Assert.AreEqual(1, events.Count);

            KillResidents(ctx, s, 2); // 23 < 24: downgrade
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.AreEqual(SettlementTier.Hamlet, s.Tier);
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(SettlementTier.Village, events[1].OldTier);
            Assert.AreEqual(SettlementTier.Hamlet, events[1].NewTier);
            Assert.AreEqual(10, s.TerritoryRadius, "territory never shrinks");
        }

        [Test]
        public void UpgradeToTown_IsChronicled_SmallerTiersAreNot()
        {
            var ctx = Data.CreateContext();
            var small = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(12, 12), 12, out _);
            var big = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(48, 48), 85, out _);
            TestWorlds.Run(new SettlementGrowthSystem(), ctx, 24);
            Assert.AreEqual(SettlementTier.Hamlet, small.Tier);
            Assert.AreEqual(SettlementTier.Town, big.Tier);
            int grown = 0;
            foreach (var e in ctx.State.Chronicle.Entries)
                if (e.Text.Contains("has grown into a"))
                {
                    grown++;
                    Assert.AreEqual(big.Name + " has grown into a Town.", e.Text);
                    Assert.AreEqual(big.Id, e.SettlementId);
                    Assert.IsTrue(e.Importance >= 1 && e.Importance <= 2);
                }
            Assert.AreEqual(1, grown);
        }

        [Test]
        public void EmptySettlement_AbandonedAfterTwoYears()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out var civ);
            int destroyed = 0;
            ctx.Events.Subscribe<SettlementDestroyedEvent>(e => { if (e.SettlementId == s.Id) destroyed++; });
            var sys = new SettlementGrowthSystem();
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear);
            Assert.IsTrue(s.IsAlive);
            Assert.AreEqual(SimTime.TicksPerYear, s.Mem(SettlementGrowthSystem.EmptyTicksKey), 1e-3f);
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear - 24, false);
            Assert.IsTrue(s.IsAlive, "not yet two years");
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.IsFalse(s.IsAlive);
            Assert.AreEqual(1, destroyed);
            Assert.IsNull(ctx.State.Settlements.Get(s.Id));
            Assert.IsFalse(civ.IsAlive, "its only settlement is gone");
        }

        [Test]
        public void Repopulated_ResetsAbandonmentClock()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            var sys = new SettlementGrowthSystem();
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear + 48);
            ctx.Ops.CreatePerson(s, s.Race, Sex.Female, 20f);
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.IsFalse(s.Memory.ContainsKey(SettlementGrowthSystem.EmptyTicksKey));
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 3, false);
            Assert.IsTrue(s.IsAlive);
        }

        [Test]
        public void MissingTownCenter_QueuesSingleRebuildOrder_ThenAdoptsCompletedOne()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 5, out _);
            var sys = new SettlementGrowthSystem();
            TestWorlds.Run(sys, ctx, 24);
            Assert.AreEqual(0, s.BuildQueue.Count, "has a town center");

            ctx.Ops.DestroyBuilding(Data.TownCenter(ctx, s), DestroyCause.Disaster);
            ctx.Ops.FlushRemovals();
            TestWorlds.Run(sys, ctx, 72, false);
            Assert.AreEqual(1, s.BuildQueue.Count);
            Assert.AreEqual(BuildingType.TownCenter, s.BuildQueue[0].Type);
            Assert.AreEqual(10f, s.BuildQueue[0].Priority, 1e-5f);
            Assert.AreEqual("rebuild seat of power", s.BuildQueue[0].Reason);

            s.BuildQueue.Clear(); // planner took the order and placed a site
            var site = Data.Place(ctx, s, BuildingType.TownCenter);
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.AreEqual(0, s.BuildQueue.Count, "site under construction: no duplicate order");

            ctx.Ops.CompleteBuilding(site);
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.AreEqual(site.Id, s.TownCenterId);
        }

        private static int CountOwned(SimContext ctx, Settlement s)
        {
            int n = 0;
            var owner = ctx.State.Map.Owner;
            for (int i = 0; i < owner.Length; i++) if (owner[i] == s.Id) n++;
            return n;
        }

        private static void KillResidents(SimContext ctx, Settlement s, int count)
        {
            for (int i = 0; i < count && s.ResidentIds.Count > 0; i++)
                ctx.Ops.KillPerson(ctx.State.People.Get(s.ResidentIds[s.ResidentIds.Count - 1]), DeathCause.Disease);
            ctx.Ops.FlushRemovals();
        }
    }
}
