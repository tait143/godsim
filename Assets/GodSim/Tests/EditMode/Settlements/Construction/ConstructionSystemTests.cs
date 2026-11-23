using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;
using Data = GodSim.Tests.Settlements.Construction.ConstructionTestData;

namespace GodSim.Tests.Settlements.Construction
{
    public class ConstructionSystemTests
    {
        [Test]
        public void SystemContract_NameAndInterval()
        {
            var sys = new ConstructionSystem();
            Assert.AreEqual(1, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
        }

        [Test]
        public void UnaffordableSite_WaitsAndTracksWaitTime()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            Data.ClearStock(s);
            var b = Data.Place(ctx, s, BuildingType.Storehouse);
            TestWorlds.Run(new ConstructionSystem(), ctx, 10);
            Assert.IsFalse(b.CostPaid);
            Assert.AreEqual(0f, b.ConstructionProgress);
            Assert.AreEqual(10f, s.Mem(ConstructionSystem.WaitKeyFor(b.Id)), 1e-4f);
            Assert.AreEqual(1f, s.Mem(ConstructionSystem.WaitingSitesKey), 1e-4f);
            Assert.AreEqual(0, s.DesiredBuilders);
        }

        [Test]
        public void Construction_PaysWhenAffordable_ProgressesOnlyWithLabor_AndCompletes()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            Data.ClearStock(s);
            var b = Data.Place(ctx, s, BuildingType.Storehouse);
            var sys = new ConstructionSystem();
            TestWorlds.Run(sys, ctx, 5);
            Assert.IsFalse(b.CostPaid);

            s.Stockpile.SetRaw(ResourceType.Wood, 100f);
            s.Stockpile.SetRaw(ResourceType.Stone, 50f);
            TestWorlds.Run(sys, ctx, 3, false);
            Assert.IsTrue(b.CostPaid);
            Assert.AreEqual(70f, s.Stockpile.Get(ResourceType.Wood), 1e-3f, "cost paid exactly once");
            Assert.AreEqual(40f, s.Stockpile.Get(ResourceType.Stone), 1e-3f);
            Assert.AreEqual(0f, b.ConstructionProgress, "no residents -> no labor -> no progress");
            Assert.IsFalse(s.Memory.ContainsKey(ConstructionSystem.WaitKeyFor(b.Id)));

            int completed = 0;
            ctx.Events.Subscribe<BuildingCompletedEvent>(e => { if (e.BuildingId == b.Id) completed++; });
            Data.AddWorker(ctx, s, JobType.Builder);
            TestWorlds.Run(sys, ctx, 1, false);
            Assert.AreEqual(1f / 20f, b.ConstructionProgress, 1e-4f, "1 builder (skill 1) on BuildWork 20");
            TestWorlds.Run(sys, ctx, 25, false);
            Assert.IsTrue(b.IsComplete);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(0, s.DesiredBuilders, "nothing left under construction");
            Assert.AreEqual(70f, s.Stockpile.Get(ResourceType.Wood), 1e-3f);
        }

        [Test]
        public void Labor_ScalesWithDeltaTicks_BuildSpeedAndSkill()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out var civ);
            Data.Stock(s, 500f, 500f);
            var a = Data.Place(ctx, s, BuildingType.Storehouse);
            var sys = new ConstructionSystem();
            sys.Initialize(ctx);
            Data.AddWorker(ctx, s, JobType.Builder, null, 1.2f);
            ctx.DeltaTicks = 4;
            sys.Tick(ctx);
            Assert.AreEqual(1.2f * 4f / 20f, a.ConstructionProgress, 1e-4f);

            civ.Modifiers.BuildSpeed = 2f;
            ctx.DeltaTicks = 1;
            sys.Tick(ctx);
            Assert.AreEqual(0.24f + 1.2f * 2f / 20f, a.ConstructionProgress, 1e-4f);

            civ.Modifiers.BuildSpeed = float.NaN; // bad data must not poison state
            sys.Tick(ctx);
            Assert.IsFalse(float.IsNaN(a.ConstructionProgress));
        }

        [Test]
        public void IdleAdults_ContributeCappedLabor()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 10, out _); // 10 idle adults, no builders
            Data.Stock(s, 500f, 500f);
            var a = Data.Place(ctx, s, BuildingType.Storehouse);
            Assert.AreEqual(0.9f, ConstructionSystem.ComputeLabor(ctx.State, s, out int adults), 1e-4f, "0.3 x min(10, 0*2+3)");
            Assert.AreEqual(10, adults);
            TestWorlds.Run(new ConstructionSystem(), ctx, 1);
            Assert.AreEqual(0.9f / 20f, a.ConstructionProgress, 1e-4f);

            Data.AddWorker(ctx, s, JobType.Builder);
            Assert.AreEqual(1f + 0.3f * 5f, ConstructionSystem.ComputeLabor(ctx.State, s, out adults), 1e-4f, "cap 2*1+3 helpers");
            Assert.AreEqual(11, adults);
        }

        [Test]
        public void Labor_IgnoresDeadChildrenAndSoldiersAway()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            var dead = Data.AddWorker(ctx, s, JobType.Builder);
            ctx.Ops.KillPerson(dead, DeathCause.Accident);
            var child = ctx.Ops.CreatePerson(s, s.Race, Sex.Female, 5f);
            Assert.AreEqual(0f, ConstructionSystem.ComputeLabor(ctx.State, s, out int adults), 1e-5f);
            Assert.AreEqual(0, adults);
            Assert.IsNotNull(child);
        }

        [Test]
        public void UnpaidSite_AbandonedAfterThreeYears()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            Data.ClearStock(s);
            var b = Data.Place(ctx, s, BuildingType.Storehouse);
            int id = b.Id;
            var causes = new List<DestroyCause>();
            ctx.Events.Subscribe<BuildingDestroyedEvent>(e => { if (e.BuildingId == id) causes.Add(e.Cause); });
            var sys = new ConstructionSystem();
            TestWorlds.Run(sys, ctx, ConstructionSystem.MaxWaitTicks);
            Assert.IsFalse(b.IsDestroyed, "exactly 3 years is still waiting");
            TestWorlds.Run(sys, ctx, 1, false);
            Assert.IsTrue(b.IsDestroyed);
            Assert.AreEqual(1, causes.Count);
            Assert.AreEqual(DestroyCause.Abandoned, causes[0]);
            Assert.IsFalse(s.Memory.ContainsKey(ConstructionSystem.WaitKeyFor(id)), "memory key cleaned up");
            Assert.IsFalse(s.BuildingIds.Contains(id));
            Assert.IsNull(ctx.State.Buildings.Get(id));
            Assert.IsTrue(s.AiLog.Count > 0);
        }

        [Test]
        public void TownCenter_HasPriority_AndBecomesSeatOfPower()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            ctx.Ops.DestroyBuilding(Data.TownCenter(ctx, s), DestroyCause.War);
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(-1, s.TownCenterId);
            Data.Stock(s, 500f, 500f);
            var store = Data.Place(ctx, s, BuildingType.Storehouse);
            var tc = Data.Place(ctx, s, BuildingType.TownCenter);
            Data.AddWorker(ctx, s, JobType.Builder);
            var sys = new ConstructionSystem();
            TestWorlds.Run(sys, ctx, 1);
            Assert.AreEqual(1f / 40f, tc.ConstructionProgress, 1e-4f);
            Assert.AreEqual(0f, store.ConstructionProgress, "older site waits for the town center");
            TestWorlds.Run(sys, ctx, 45, false);
            Assert.IsTrue(tc.IsComplete);
            Assert.AreEqual(tc.Id, s.TownCenterId);
            Assert.Greater(store.ConstructionProgress, 0f);
        }

        [Test]
        public void Houses_HavePriority_OnlyWhileHomeless()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            Data.Stock(s, 500f, 500f);
            var store = Data.Place(ctx, s, BuildingType.Storehouse);
            var house = Data.Place(ctx, s, BuildingType.House);
            Data.AddWorker(ctx, s, JobType.Builder);
            var sys = new ConstructionSystem();
            TestWorlds.Run(sys, ctx, 1);
            Assert.AreEqual(0.05f, store.ConstructionProgress, 1e-4f, "placement order without homeless");
            Assert.AreEqual(0f, house.ConstructionProgress);

            s.Homeless = 4;
            TestWorlds.Run(sys, ctx, 1, false);
            Assert.AreEqual(0.05f, store.ConstructionProgress, 1e-4f);
            Assert.AreEqual(0.1f, house.ConstructionProgress, 1e-4f, "house first while people are homeless");
        }

        [Test]
        public void Labor_CappedAtFourBuildersPerTile()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            Data.Stock(s, 500f, 500f);
            var h1 = Data.Place(ctx, s, BuildingType.House);
            var h2 = Data.Place(ctx, s, BuildingType.House);
            var h3 = Data.Place(ctx, s, BuildingType.House);
            for (int i = 0; i < 10; i++) Data.AddWorker(ctx, s, JobType.Builder);
            TestWorlds.Run(new ConstructionSystem(), ctx, 1);
            Assert.AreEqual(0.4f, h1.ConstructionProgress, 1e-4f);
            Assert.AreEqual(0.4f, h2.ConstructionProgress, 1e-4f);
            Assert.AreEqual(0.2f, h3.ConstructionProgress, 1e-4f);
        }

        [Test]
        public void DesiredBuilders_FollowsActiveSites()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 30, out _);
            var sys = new ConstructionSystem();
            TestWorlds.Run(sys, ctx, 1);
            Assert.AreEqual(0, s.DesiredBuilders, "nothing under construction");

            Data.Stock(s, 500f, 500f);
            Data.Place(ctx, s, BuildingType.Storehouse);
            Data.Place(ctx, s, BuildingType.Storehouse);
            TestWorlds.Run(sys, ctx, 1, false);
            Assert.AreEqual(5, s.DesiredBuilders, "2*2+1");
            Assert.AreEqual(2f, s.Mem(ConstructionSystem.ActiveSitesKey), 1e-4f);

            var small = Data.CreateContext();
            var t = Data.AddTown(small, 6, out _);
            Data.Stock(t, 500f, 500f);
            Data.Place(small, t, BuildingType.Storehouse);
            Data.Place(small, t, BuildingType.Storehouse);
            TestWorlds.Run(new ConstructionSystem(), small, 1);
            Assert.AreEqual(2, t.DesiredBuilders, "clamped to adults/3");

            TestWorlds.Run(sys, ctx, 400, false);
            Assert.AreEqual(0, s.DesiredBuilders, "all done");
            Assert.IsFalse(s.Memory.ContainsKey(ConstructionSystem.ActiveSitesKey));
        }

        [Test]
        public void Wonder_ChronicledWhenConstructionBegins()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 3, out _);
            Data.Stock(s, 0f, 100f);
            var w = Data.Place(ctx, s, BuildingType.GreatLibrary);
            int before = ctx.State.Chronicle.Entries.Count;
            TestWorlds.Run(new ConstructionSystem(), ctx, 2);
            Assert.IsTrue(w.CostPaid);
            Assert.AreEqual(before + 1, ctx.State.Chronicle.Entries.Count, "one entry, not one per tick");
            var e = ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1];
            StringAssert.Contains("Great Library", e.Text);
            Assert.AreEqual(ChronicleCategory.Building, e.Category);
        }

        [Test]
        public void DestroyedAndDanglingSites_AreSkippedSafely()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 5, out _);
            Data.Stock(s, 500f, 500f);
            var a = Data.Place(ctx, s, BuildingType.House);
            var b = Data.Place(ctx, s, BuildingType.House);
            var sys = new ConstructionSystem();
            sys.Initialize(ctx);
            ctx.Ops.DestroyBuilding(a, DestroyCause.Fire); // not flushed yet
            s.BuildingIds.Add(987654);                     // dangling id
            s.BuildingIds.Add(-1);
            ctx.DeltaTicks = 1;
            sys.Tick(ctx);
            Assert.AreEqual(0f, a.ConstructionProgress);
            Assert.Greater(b.ConstructionProgress, 0f);
            ctx.Ops.FlushRemovals();
            sys.Tick(ctx);
        }

        [Test]
        public void StaleWaitKeys_AreSwept()
        {
            var ctx = Data.CreateContext();
            var s = Data.AddTown(ctx, 0, out _);
            Data.ClearStock(s);
            var b = Data.Place(ctx, s, BuildingType.Storehouse);
            var sys = new ConstructionSystem();
            TestWorlds.Run(sys, ctx, 5);
            string key = ConstructionSystem.WaitKeyFor(b.Id);
            Assert.IsTrue(s.Memory.ContainsKey(key));
            ctx.Ops.DestroyBuilding(b, DestroyCause.Disaster); // destroyed by someone else
            ctx.Ops.FlushRemovals();
            s.Memory[ConstructionSystem.WaitKeyPrefix + "garbage"] = 3f;
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear + 1, false);
            Assert.IsFalse(s.Memory.ContainsKey(key));
            Assert.IsFalse(s.Memory.ContainsKey(ConstructionSystem.WaitKeyPrefix + "garbage"));
        }
    }
}
