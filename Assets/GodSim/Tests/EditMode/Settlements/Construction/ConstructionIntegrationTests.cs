using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;
using Data = GodSim.Tests.Settlements.Construction.ConstructionTestData;

namespace GodSim.Tests.Settlements.Construction
{
    public class ConstructionIntegrationTests
    {
        private static List<ISimSystem> Systems() => new List<ISimSystem> { new SettlementGrowthSystem(), new ConstructionSystem(), new MaintenanceSystem() };

        private static string Simulate(ulong seed)
        {
            var ctx = Data.CreateContext(seed);
            var s = Data.AddTown(ctx, 40, out var civ);
            for (int i = 0; i < 4; i++) Data.AddWorker(ctx, s, JobType.Builder, null, 0.8f + 0.1f * i);
            Data.Place(ctx, s, BuildingType.House, true);
            Data.Place(ctx, s, BuildingType.House, true);
            Data.Place(ctx, s, BuildingType.Workshop, true).Condition = 0.5f;
            for (int i = 0; i < 6; i++) Data.Place(ctx, s, i % 2 == 0 ? BuildingType.House : BuildingType.Storehouse);
            Data.Stock(s, 60f, 15f);
            var systems = Systems();
            TestWorlds.RunAll(systems, ctx, SimTime.TicksPerYear);
            civ.Age = AgeId.Bronze;
            Data.ClearStock(s); // dwellings renovate for free via ctx.Rng
            Data.Step(systems, ctx, SimTime.TicksPerYear * 2);
            Data.Stock(s, 300f, 300f);
            Data.Step(systems, ctx, SimTime.TicksPerYear);
            return Data.Fingerprint(ctx);
        }

        [Test]
        public void SameSeed_SameResult()
        {
            string a = Simulate(99);
            string b = Simulate(99);
            Assert.AreEqual(a, b);
            StringAssert.Contains("True", a);
        }

        [Test]
        public void DestroyedBuildings_DanglingIds_AndMissingCiv_NeverThrow()
        {
            var ctx = Data.CreateContext(5);
            var s = Data.AddTown(ctx, 20, out _);
            Data.Stock(s, 500f, 500f);
            var site = Data.Place(ctx, s, BuildingType.Storehouse);
            var done = Data.Place(ctx, s, BuildingType.Workshop, true);
            Data.Place(ctx, s, BuildingType.House);
            Data.AddWorker(ctx, s, JobType.Builder);
            var systems = Systems();
            foreach (var sys in systems) { ctx.DeltaTicks = sys.TickInterval; sys.Initialize(ctx); }

            ctx.Ops.DestroyBuilding(site, DestroyCause.Fire);  // destroyed, not yet flushed
            ctx.Ops.DestroyBuilding(done, DestroyCause.War);
            s.BuildingIds.Add(55555);
            s.ResidentIds.Add(77777);
            foreach (var sys in systems) { ctx.DeltaTicks = sys.TickInterval; sys.Tick(ctx); }
            ctx.Ops.FlushRemovals();

            s.CivilizationId = 9999; // dangling civilization
            Data.Step(systems, ctx, SimTime.TicksPerYear * 2);
            Assert.IsTrue(s.IsAlive);

            s.CivilizationId = -1;
            foreach (int pid in s.ResidentIds.ToArray())
            {
                var p = ctx.State.People.Get(pid);
                if (p != null) ctx.Ops.KillPerson(p, DeathCause.Disease);
            }
            s.ResidentIds.Clear();
            ctx.Ops.FlushRemovals();
            Data.Step(systems, ctx, SimTime.TicksPerYear * 3);
            Assert.IsFalse(s.IsAlive, "empty settlement abandoned even without a civilization");
            Data.Step(systems, ctx, 48);
        }

        [Test]
        public void NullishContext_IsIgnored()
        {
            foreach (var sys in Systems())
            {
                sys.Tick(null);
                sys.Tick(new SimContext());
            }
        }

        [Test]
        public void TwoHundredYears_TownsEvolveThroughTheAges()
        {
            var ctx = Data.CreateContext(2024);
            var s = Data.AddTown(ctx, 60, out var civ);
            for (int i = 0; i < 5; i++) Data.AddWorker(ctx, s, JobType.Builder);
            var systems = Systems();
            foreach (var sys in systems) { ctx.DeltaTicks = sys.TickInterval; sys.Initialize(ctx); }
            var upgraded = new HashSet<int>();
            ctx.Events.Subscribe<BuildingUpgradedEvent>(e => upgraded.Add(e.BuildingId));
            int completed = 0;
            ctx.Events.Subscribe<BuildingCompletedEvent>(e => completed++);

            for (int year = 0; year < 200; year++)
            {
                if (year > 0 && year % 20 == 0 && civ.Age < AgeId.Ascension) civ.Age = civ.Age + 1;
                Data.Stock(s, 400f, 400f);
                Data.Place(ctx, s, BuildingType.House);
                if (year % 10 == 0) Data.Place(ctx, s, BuildingType.Storehouse);
                Data.Step(systems, ctx, SimTime.TicksPerYear);
            }

            Assert.IsTrue(s.IsAlive);
            Assert.AreEqual(AgeId.Ascension, civ.Age);
            Assert.Greater(completed, 200);
            Assert.Greater(upgraded.Count, 50);
            int complete = 0, current = 0;
            foreach (int id in s.BuildingIds)
            {
                var b = ctx.State.Buildings.Get(id);
                Assert.IsNotNull(b);
                Assert.IsFalse(float.IsNaN(b.Condition) || float.IsInfinity(b.Condition));
                Assert.IsTrue(b.Condition >= 0f && b.Condition <= 1f);
                Assert.IsTrue(b.ConstructionProgress >= 0f && b.ConstructionProgress <= 1f);
                if (!b.IsComplete) continue;
                complete++;
                if (b.StyleAge == civ.Age) current++;
            }
            Assert.Greater(complete, 200);
            Assert.Greater(current, complete / 2, "most of the town has been renovated into the current age");
            foreach (var kv in s.Memory)
                if (kv.Key.StartsWith(ConstructionSystem.WaitKeyPrefix, StringComparison.Ordinal))
                {
                    int id = int.Parse(kv.Key.Substring(ConstructionSystem.WaitKeyPrefix.Length));
                    var b = ctx.State.Buildings.Get(id);
                    Assert.IsTrue(b != null && !b.CostPaid, "no stale wait keys");
                }
            Assert.AreEqual(SettlementTier.Village, s.Tier);
            Assert.GreaterOrEqual(s.TerritoryRadius, 10);
        }
    }
}
