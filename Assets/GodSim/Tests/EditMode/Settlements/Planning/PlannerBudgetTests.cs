using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Settlements
{
    /// <summary>Per-run work cap of the town planner (TickInterval 2 system) and fairness across many towns.</summary>
    public class PlannerBudgetTests
    {
        [TestCase(8, 300)]
        [TestCase(12, 200)]
        [TestCase(26, 96)]
        public void CandidateCap_ShrinksWithRadius(int radius, int expected)
        {
            Assert.AreEqual(expected, TownPlannerSystem.CandidateCapFor(radius));
        }

        [Test]
        public void ThirtyBusyTowns_StayWithinTheWorkBudget_AndAllGetServed()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 256, 61);
            var towns = new List<Settlement>();
            for (int k = 0; k < 30; k++)
            {
                var at = new Vec2i(20 + (k % 6) * 40, 20 + (k / 6) * 44);
                towns.Add(TestWorlds.AddSettlement(ctx, (RaceId)(k % 5), at, 60, out _));
            }
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 0);
            int maxWork = 0, runs = 0;
            for (int run = 0; run < 40; run++)
            {
                foreach (var s in towns)
                    while (s.BuildQueue.Count < 3) PlanningTestKit.Enqueue(s, BuildingType.House, 1f, ctx.State.Tick);
                TestWorlds.Run(planner, ctx, 2, false);
                runs++;
                maxWork = System.Math.Max(maxWork, planner.LastRunWork);
                Assert.IsTrue(planner.LastRunWork <= TownPlannerSystem.WorkBudgetPerRun, "run " + run + " spent " + planner.LastRunWork);
                Assert.IsTrue(planner.LastRunCandidates <= TownPlannerSystem.WorkBudgetPerRun);
                foreach (var s in towns) PlanningTestKit.CompleteAll(ctx, s);
            }
            Assert.IsTrue(maxWork > TownPlannerSystem.WorkBudgetPerRun / 2, "the budget is actually the limiting factor (" + maxWork + ")");
            foreach (var s in towns)
            {
                int houses = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count;
                Assert.IsTrue(houses >= 4, s.Name + " was starved: " + houses + " houses after " + runs + " runs");
                PlanningTestKit.AssertAllRoadConnected(ctx, s);
            }
        }
    }
}
