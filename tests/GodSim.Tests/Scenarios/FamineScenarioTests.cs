using System;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>
    /// Scenario 5 (FamineRecovery). At year 10 the largest settlement's food is drained to 0. Because the economy
    /// produces food and the population eats within the same tick (Economy runs before Population), a single
    /// drain can be refilled before anyone notices; the drain is therefore held (food forced to 0 before every
    /// tick, i.e. a blight) until the famine has visibly struck (FamineEvent + a starvation death), for at most
    /// two years. Warfare and natural disasters are off so the famine is the only shock.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    [Category("Slow")]
    public sealed class FamineScenarioTests
    {
        private const ulong Seed = 9001UL;

        [Test]
        public void FamineRecovery_SettlementStarvesThenRecoversWithin15Years()
        {
            var cfg = ScenarioWorlds.SmallWorld(Seed);
            cfg.EnableWarfare = false;
            cfg.EnableDisasters = false;
            var log = new CollectingLogger();
            using (var h = new ScenarioHarness(ScenarioWorlds.Create(cfg, log), log))
            {
                h.RunYears(10);
                var target = ScenarioWorlds.LargestSettlement(h.State);
                Assert.That(target, Is.Not.Null, "No settlement alive at year 10.\n" + h.Summary());
                Assert.That(target.Population, Is.GreaterThanOrEqualTo(8), "Largest settlement has only " + target.Population + " people at year 10.\n" + h.Summary());

                int targetId = target.Id, civId = target.CivilizationId, popBefore = target.Population;
                var before = h.SampleSettlement(target);
                float preFood = before.Food;
                float foodPriorityBefore = MaxFoodJobPriority(target);
                int famines0 = h.Famines.Count, deaths0 = h.Deaths.Count, completions0 = h.Completions.Count;

                // ---- drain phase: hold food at 0 until the famine bites (FamineEvent + starvation death) or 2 years pass
                bool draining = true;
                float foodPriorityDuring = foodPriorityBefore;
                h.BeforeStep = x =>
                {
                    var st = x.State.Settlements.Get(targetId);
                    if (draining && st != null) st.Stockpile.SetRaw(ResourceType.Food, 0f);
                };
                h.AfterStep = x =>
                {
                    var st = x.State.Settlements.Get(targetId);
                    if (st != null) foodPriorityDuring = Math.Max(foodPriorityDuring, MaxFoodJobPriority(st));
                };
                int drainTicks = 0;
                bool famineSeen = false;
                int starvation = 0;
                for (; drainTicks < 2 * SimTime.TicksPerYear; drainTicks++)
                {
                    h.Step();
                    famineSeen = CountFamines(h, famines0, targetId) > 0;
                    starvation = CountStarvation(h, deaths0, targetId, civId);
                    var st = h.State.Settlements.Get(targetId);
                    if (st == null || !st.IsAlive) break;
                    if (famineSeen && starvation > 0) break;
                    if (st.Population <= Math.Max(3, popBefore * 4 / 10)) break; // keep the town alive enough to recover
                }
                draining = false;
                long drainEnd = h.Now;
                string drainInfo = "drain " + drainTicks + " ticks from food " + preFood.ToString("0.0") + ": famineEvents=" + CountFamines(h, famines0, targetId) +
                                   " starvationDeaths=" + starvation + " popBefore=" + popBefore + " popNow=" + (h.State.Settlements.Get(targetId)?.Population ?? 0) +
                                   " foodProducersBefore=" + before.FoodProducers + " foodWorkersBefore=" + before.FoodWorkers;

                Assert.That(famineSeen, Is.True, "No FamineEvent for the drained settlement. " + drainInfo);
                Assert.That(starvation, Is.GreaterThan(0), "Famine caused no starvation deaths. " + drainInfo);

                // ---- recovery phase: 15 years
                float maxLateFood = 0f, lastFood = 0f;
                int lateFamines = 0, foodBuildingsCompleted = 0;
                long recoveryEnd = drainEnd + SimTime.YearsToTicks(15);
                long lateFrom = recoveryEnd - SimTime.YearsToTicks(3);
                h.AfterStep = x =>
                {
                    var st = x.State.Settlements.Get(targetId);
                    if (st == null) return;
                    foodPriorityDuring = Math.Max(foodPriorityDuring, MaxFoodJobPriority(st));
                    lastFood = st.Stockpile.Get(ResourceType.Food);
                    if (x.Now >= lateFrom) maxLateFood = Math.Max(maxLateFood, lastFood);
                };
                int faminesAtDrainEnd = h.Famines.Count;
                h.RunTicks((int)(recoveryEnd - drainEnd));
                for (int i = faminesAtDrainEnd; i < h.Famines.Count; i++)
                    if (h.Famines[i].Event.SettlementId == targetId && h.Famines[i].Tick >= lateFrom) lateFamines++;
                for (int i = completions0; i < h.Completions.Count; i++)
                    if (h.Completions[i].Event.SettlementId == targetId && ScenarioWorlds.IsFoodProducer(h.Db, h.Completions[i].Event.Type)) foodBuildingsCompleted++;

                var end = h.State.Settlements.Get(targetId);
                Assert.That(end != null && end.IsAlive, Is.True, "The famine-struck settlement did not survive 15 years. " + drainInfo + "\n" + h.Summary());
                var after = h.SampleSettlement(end);
                float recoveryTarget = Math.Max(10f, Math.Min(40f, preFood * 0.5f));
                string info = drainInfo + "\nafter 15y: pop=" + after.Population + " food=" + after.Food.ToString("0.0") + " maxFoodLast3y=" + maxLateFood.ToString("0.0") +
                              " (target " + recoveryTarget.ToString("0.0") + ") faminesLast3y=" + lateFamines + " foodBuildingsCompleted=" + foodBuildingsCompleted +
                              " foodWorkers " + before.FoodWorkers + "->" + after.FoodWorkers + " foodPriority " + foodPriorityBefore.ToString("0.00") + "->" + foodPriorityDuring.ToString("0.00");
                TestContext.WriteLine(info);
                Assert.That(after.Population, Is.GreaterThan(0), info);
                Assert.That(maxLateFood, Is.GreaterThanOrEqualTo(recoveryTarget), "Food never recovered. " + info);
                Assert.That(lateFamines, Is.EqualTo(0), "Settlement is still in famine 12-15 years later. " + info);
                bool reacted = foodBuildingsCompleted > 0 || after.FoodWorkers > before.FoodWorkers || foodPriorityDuring > foodPriorityBefore + 1e-4f;
                Assert.That(reacted, Is.True, "AI did not react to the famine with food buildings, food workers or food job priority. " + info);
            }
        }

        private static float MaxFoodJobPriority(Settlement st)
        {
            float m = 0f;
            for (int j = 0; j < st.JobPriority.Length; j++) if (ScenarioWorlds.IsFoodJob((JobType)j)) m = Math.Max(m, st.JobPriority[j]);
            return m;
        }

        private static int CountFamines(ScenarioHarness h, int from, int settlementId)
        {
            int n = 0;
            for (int i = from; i < h.Famines.Count; i++) if (h.Famines[i].Event.SettlementId == settlementId) n++;
            return n;
        }

        private static int CountStarvation(ScenarioHarness h, int from, int settlementId, int civId)
        {
            int n = 0;
            for (int i = from; i < h.Deaths.Count; i++)
            {
                var d = h.Deaths[i].Event;
                if (d.Cause == DeathCause.Starvation && (d.SettlementId == settlementId || d.CivilizationId == civId)) n++;
            }
            return n;
        }
    }
}
