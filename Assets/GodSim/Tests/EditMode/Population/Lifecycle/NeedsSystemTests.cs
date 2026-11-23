using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Population;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Population.Lifecycle
{
    public class NeedsSystemTests
    {
        [Test]
        public void FedSettlement_KeepsSatiationNearOne_AndEatsTheRightAmount()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 10, out _);
            s.Stockpile.Capacity = 5000f;
            s.Stockpile.SetRaw(ResourceType.Food, 2000f);
            foreach (int id in s.ResidentIds) ctx.State.People.Get(id).Satiation = 0.5f;

            TestWorlds.Run(new NeedsSystem(), ctx, 200);

            foreach (int id in s.ResidentIds)
                Assert.IsTrue(ctx.State.People.Get(id).Satiation > 0.99f, "fed people should be (nearly) fully satiated");
            Assert.AreEqual(2000f - 10 * 0.05f * 200, s.Stockpile.Get(ResourceType.Food), 0.05f);
            Assert.AreEqual(1f, s.Mem(LifecycleMemoryKeys.FoodRatio), 1e-4f);
            Assert.AreEqual(0.5f, s.Mem(LifecycleMemoryKeys.FoodNeedPerTick), 1e-4f);
            Assert.AreEqual(10, ctx.State.LivingPopulation);
        }

        [Test]
        public void ChildrenAndEldersEatLess()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 5f);   // child
            LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 30f);    // adult
            LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 60f);  // elder

            TestWorlds.Run(new NeedsSystem(), ctx, 1);

            float expected = 0.05f * (0.6f + 1f + 0.8f);
            Assert.AreEqual(expected, s.Mem(LifecycleMemoryKeys.FoodNeedPerTick), 1e-5f);
            Assert.AreEqual(100f - expected, s.Stockpile.Get(ResourceType.Food), 1e-4f);
        }

        [Test]
        public void NoFood_CausesStarvationDeathsOverTime_WithThrottledFamine()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out _);
            s.Stockpile.SetRaw(ResourceType.Food, 0f);

            var famineTicks = new List<long>();
            int starvationDeaths = 0;
            ctx.Events.Subscribe<FamineEvent>(e => { if (e.SettlementId == s.Id) { famineTicks.Add(ctx.Tick); Assert.IsTrue(e.Shortfall > 0f); } });
            ctx.Events.Subscribe<PersonDiedEvent>(e => { if (e.Cause == DeathCause.Starvation) starvationDeaths++; });

            var needs = new NeedsSystem();
            TestWorlds.Run(needs, ctx, 10);
            Assert.AreEqual(0, starvationDeaths, "hunger takes time to kill");
            Assert.AreEqual(12, ctx.State.LivingPopulation);

            TestWorlds.Run(needs, ctx, 3 * SimTime.TicksPerYear - 10, false);

            Assert.AreEqual(12, starvationDeaths, "with no food at all everybody eventually starves");
            Assert.AreEqual(0, ctx.State.LivingPopulation);
            Assert.AreEqual(12, ctx.State.Stats.DeathsByCause[(int)DeathCause.Starvation]);
            Assert.IsTrue(famineTicks.Count >= 2, "famine should be reported repeatedly");
            for (int i = 1; i < famineTicks.Count; i++)
                Assert.IsTrue(famineTicks[i] - famineTicks[i - 1] >= 24, "famine events are throttled to one per 24 ticks");

            int chronicles = 0;
            foreach (var e in ctx.State.Chronicle.Entries)
                if (e.Category == ChronicleCategory.Disaster && e.SettlementId == s.Id) { chronicles++; Assert.AreEqual(1, e.Importance); }
            Assert.IsTrue(chronicles >= 1 && chronicles <= 3, "famine chronicled at most once per year, got " + chronicles);
        }

        [Test]
        public void PartialFood_SatiationSettlesAtFedRatio_WithoutDeathsOrFamine()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 10, out _);
            s.Stockpile.SetRaw(ResourceType.Food, 0f);
            int famines = 0, deaths = 0;
            ctx.Events.Subscribe<FamineEvent>(e => famines++);
            ctx.Events.Subscribe<PersonDiedEvent>(e => deaths++);

            // Need is 10 * 0.05 = 0.5 per tick; supply 0.4 -> ratio 0.8.
            var systems = new List<ISimSystem> { new TestFoodSource { FlatPerTick = 0.4f }, new NeedsSystem() };
            TestWorlds.RunAll(systems, ctx, 300);

            foreach (int id in s.ResidentIds)
                Assert.AreEqual(0.8f, ctx.State.People.Get(id).Satiation, 0.01f);
            Assert.AreEqual(0, famines);
            Assert.AreEqual(0, deaths);
            Assert.AreEqual(0.8f, s.Mem(LifecycleMemoryKeys.FoodRatio), 0.01f);
        }

        [Test]
        public void DeltaTicksScaling_MatchesSingleTicks()
        {
            var db = TestDb.CreateStub();
            var a = TestWorlds.CreateContext(db, seed: 7);
            var b = TestWorlds.CreateContext(db, seed: 7);
            var sa = TestWorlds.AddSettlement(a, RaceId.Human, new Vec2i(32, 32), 10, out _);
            var sb = TestWorlds.AddSettlement(b, RaceId.Human, new Vec2i(32, 32), 10, out _);
            sa.Stockpile.SetRaw(ResourceType.Food, 1f); // enough for 2 of the 4 ticks
            sb.Stockpile.SetRaw(ResourceType.Food, 1f);

            var na = new NeedsSystem();
            a.State.Tick = 4;
            a.DeltaTicks = 4;
            na.Tick(a);

            var nb = new NeedsSystem();
            for (int t = 1; t <= 4; t++) { b.State.Tick = t; b.DeltaTicks = 1; nb.Tick(b); }

            Assert.AreEqual(0f, sa.Stockpile.Get(ResourceType.Food), 1e-5f);
            Assert.AreEqual(0f, sb.Stockpile.Get(ResourceType.Food), 1e-5f);
            // Interval run: ratio 0.5 for 4 ticks -> 1 - 0.5*0.05*4 = 0.9. Single ticks: two fed ticks, two hungry ones -> 0.9.
            var pa = a.State.People.Get(sa.ResidentIds[0]);
            var pb = b.State.People.Get(sb.ResidentIds[0]);
            Assert.AreEqual(0.9f, pa.Satiation, 1e-4f);
            Assert.AreEqual(0.9f, pb.Satiation, 1e-4f);
        }

        [Test]
        public void Tick_IgnoresDeadAndMissingResidents()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var alive = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 30f);
            var dead = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 30f);
            ctx.Ops.KillPerson(dead, DeathCause.Accident);
            s.ResidentIds.Add(dead.Id);   // stale entry (dead, not yet flushed)
            s.ResidentIds.Add(987654);    // dangling id
            s.ResidentIds.Add(-1);

            var needs = new NeedsSystem();
            ctx.State.Tick = 1;
            ctx.DeltaTicks = 1;
            Assert.DoesNotThrow(() => needs.Tick(ctx));
            Assert.AreEqual(100f - 0.05f, s.Stockpile.Get(ResourceType.Food), 1e-4f);
            Assert.IsTrue(alive.IsAlive);
        }

        [Test]
        public void Tick_SanitizesCorruptValues()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 0f, out _);
            var p = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 30f);
            p.Satiation = float.NaN;
            p.Health = float.PositiveInfinity;

            TestWorlds.Run(new NeedsSystem(), ctx, 30); // long enough to starve, which touches health

            Assert.IsTrue(p.Health >= 0f && p.Health <= 1f);
            Assert.IsFalse(float.IsNaN(p.Satiation) || float.IsInfinity(p.Satiation));
            Assert.IsFalse(float.IsNaN(p.Health) || float.IsInfinity(p.Health));
            Assert.IsTrue(p.Satiation >= 0f && p.Satiation <= 1f);
        }
    }
}
