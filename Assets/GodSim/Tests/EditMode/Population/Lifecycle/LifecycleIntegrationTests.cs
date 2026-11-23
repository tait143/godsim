using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Population;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Population.Lifecycle
{
    public class LifecycleIntegrationTests
    {
        /// <summary>A small human tribe on bounded farmland: workers produce a bit more than they eat, up to a cap.</summary>
        private static SimContext Tribe(GameDatabase db, ulong seed, out Settlement s, out TestFoodSource food)
        {
            var ctx = TestWorlds.CreateContext(db, seed: seed);
            s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 20, out _);
            s.Stockpile.Capacity = 600f;
            food = new TestFoodSource { PerAdultPerTick = 0.075f, MaxPerTick = 6f };
            return ctx;
        }

        [Test]
        public void LifecycleModule_CreatesTheFourSystems_WithExpectedIntervals()
        {
            var systems = LifecycleModule.CreateSystems();
            Assert.AreEqual(4, systems.Count);
            Assert.IsTrue(systems[0] is NeedsSystem && systems[0].TickInterval == 1);
            Assert.IsTrue(systems[1] is AgingSystem && systems[1].TickInterval == 8);
            Assert.IsTrue(systems[2] is FamilySystem && systems[2].TickInterval == 12);
            Assert.IsTrue(systems[3] is BirthSystem && systems[3].TickInterval == 8);
            var names = new HashSet<string>();
            foreach (var sys in systems) Assert.IsTrue(names.Add(sys.Name), "system names must be unique");
        }

        [Test]
        public void SameSeed_SamePopulation_After50Years()
        {
            var db = TestDb.CreateStub();
            var results = new List<ulong>();
            var counts = new List<int>();
            for (int run = 0; run < 2; run++)
            {
                var ctx = Tribe(db, 2024, out var s, out var food);
                TestWorlds.RunAll(LifecycleTestSupport.AllSystems(food), ctx, 50 * SimTime.TicksPerYear);
                results.Add(LifecycleTestSupport.Fingerprint(ctx.State));
                counts.Add(ctx.State.LivingPopulation);
                Assert.IsTrue(ctx.State.Stats.TotalBirths > 0);
                Assert.IsTrue(ctx.State.Stats.TotalDeaths > 0);
            }
            Assert.AreEqual(counts[0], counts[1]);
            Assert.AreEqual(results[0], results[1]);

            var other = Tribe(db, 9999, out _, out var food2);
            TestWorlds.RunAll(LifecycleTestSupport.AllSystems(food2), other, 50 * SimTime.TicksPerYear);
            Assert.AreNotEqual(results[0], LifecycleTestSupport.Fingerprint(other.State), "a different seed should give a different history");
        }

        [Test]
        public void TwoHundredYears_PopulationNeitherExplodesNorDiesOut()
        {
            var db = TestDb.CreateStub();
            var ctx = Tribe(db, 77, out var s, out var food);
            var systems = LifecycleTestSupport.AllSystems(food);
            int min = int.MaxValue, max = 0;
            float maxAgeSeen = 0f;
            ctx.Events.Subscribe<PersonDiedEvent>(e => { if (e.AgeYears > maxAgeSeen) maxAgeSeen = e.AgeYears; });

            TestWorlds.RunAll(systems, ctx, 0);
            for (int decade = 0; decade < 20; decade++)
            {
                for (int y = 0; y < 10; y++)
                {
                    RunMore(systems, ctx, SimTime.TicksPerYear);
                    int pop = ctx.State.LivingPopulation;
                    if (pop < min) min = pop;
                    if (pop > max) max = pop;
                }
                TestContext.WriteLine("year " + ctx.State.Year + ": pop " + ctx.State.LivingPopulation + ", food " + s.Stockpile.Get(ResourceType.Food).ToString("0")
                                      + ", fedRatio " + s.Mem(LifecycleMemoryKeys.FoodRatio).ToString("0.00"));
            }

            int final = ctx.State.LivingPopulation;
            TestContext.WriteLine("min " + min + " max " + max + " final " + final + " births " + ctx.State.Stats.TotalBirths + " deaths " + ctx.State.Stats.TotalDeaths
                                  + " starvation " + ctx.State.Stats.DeathsByCause[(int)DeathCause.Starvation]);
            Assert.IsTrue(min >= 10, "the tribe should never come close to dying out (min " + min + ")");
            Assert.IsTrue(max <= 250, "the population should stay near what the land feeds (max " + max + ")");
            Assert.IsTrue(final >= 40, "the tribe should have grown into its farmland (final " + final + ")");
            Assert.IsTrue(maxAgeSeen <= 70f * LifecycleBalance.MaxAgeFactor + 1e-3f);
            Assert.IsTrue(ctx.State.Stats.TotalBirths > 100, "generations should turn over");
        }

        [Test]
        public void FourHundredYears_FullLifecycle_NobodyExceedsMaxAge()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, seed: 404);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out _);
            s.Stockpile.Capacity = 300f;
            var food = new TestFoodSource { PerAdultPerTick = 0.075f, MaxPerTick = 2f };
            var systems = LifecycleTestSupport.AllSystems(food);
            float limit = 70f * LifecycleBalance.MaxAgeFactor + 1e-3f;
            int elderDeaths = 0;
            ctx.Events.Subscribe<PersonDiedEvent>(e =>
            {
                Assert.IsTrue(e.AgeYears <= limit, "died at " + e.AgeYears);
                if (e.AgeYears >= 55f) elderDeaths++;
            });

            TestWorlds.RunAll(systems, ctx, 0);
            for (int year = 0; year < 400; year++)
            {
                RunMore(systems, ctx, SimTime.TicksPerYear);
                foreach (var p in ctx.State.People.Items) Assert.IsTrue(p.AgeYears <= limit);
            }
            Assert.IsTrue(elderDeaths > 0, "elders should eventually die");
            Assert.IsTrue(ctx.State.LivingPopulation > 0, "a fed tiny settlement should survive 400 years");
        }

        private static void RunMore(IList<ISimSystem> systems, SimContext ctx, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                ctx.State.Tick++;
                foreach (var sys in systems)
                {
                    if (sys.TickInterval > 1 && ctx.State.Tick % sys.TickInterval != 0) continue;
                    ctx.DeltaTicks = sys.TickInterval;
                    sys.Tick(ctx);
                    ctx.Ops.FlushRemovals();
                }
            }
        }
    }
}
