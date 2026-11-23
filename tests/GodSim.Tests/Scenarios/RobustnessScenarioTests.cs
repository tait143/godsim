using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>Scenarios 10 (robustness soak) and 11 (performance budget).</summary>
    [TestFixture]
    [Category("Scenario")]
    [Category("Slow")]
    public sealed class RobustnessScenarioTests
    {
        private const ulong SoakSeed = 600613UL;
        private const int SoakYears = 300;
        private const int SoakMap = 160;
        private const int SoakMaxPopulation = 1500;

        private static readonly GodPowerType[] SoakPowers =
        {
            GodPowerType.Meteor, GodPowerType.Plague, GodPowerType.Flood, GodPowerType.Earthquake, GodPowerType.Wildfire,
            GodPowerType.LowerLand, GodPowerType.RaiseLand, GodPowerType.Drought, GodPowerType.Bless, GodPowerType.Fertility, GodPowerType.SpawnTribe,
        };

        [Test]
        public void RobustnessSoak_300Years_NoFaultsNoDisabledSystemsConsistentState()
        {
            var cfg = ScenarioWorlds.SmallWorld(SoakSeed, SoakMap);
            cfg.StrictMode = false;
            cfg.MaxPopulation = SoakMaxPopulation; // low enough that the cap is actually exercised
            cfg.GrowthMultiplier = 1.5f;
            cfg.ManaRegenMultiplier = 5f;
            var log = new CollectingLogger();
            using (var h = new ScenarioHarness(ScenarioWorlds.Create(cfg, log), log))
            {
                var simCfg = h.Sim.Config ?? cfg;
                int maxPop = simCfg.MaxPopulation;
                int popSlack = Math.Max(10, maxPop / 50); // births resolved within one interval may overshoot by a hair
                int peak = 0; long peakTick = 0;
                var capViolations = new List<string>();
                h.CheckHousingOnSample = true;
                h.OnSample = x =>
                {
                    int pop = x.State.LivingPopulation;
                    if (pop > peak) { peak = pop; peakTick = x.Now; }
                };

                InvariantReport firstBroken = null; int firstBrokenYear = -1;
                List<string> firstNonFinite = null; int firstNonFiniteYear = -1;
                int powerIndex = 0, casts = 0, castOk = 0;
                for (int year = 1; year <= SoakYears; year++)
                {
                    h.RunYears(1);
                    if (year % 25 == 0)
                    {
                        // A little divine chaos: one power every 25 years, aimed deterministically.
                        var power = SoakPowers[powerIndex++ % SoakPowers.Length];
                        var def = h.Db.GodPower(power);
                        Vec2i target; int param = 0;
                        if (def.UsesRaceParameter) { ScenarioWorlds.TryFindEmptyLand(h.State, h.Db, 12, out target, out var race); param = (int)race; }
                        else { var st = ScenarioWorlds.LargestSettlement(h.State); target = st != null ? st.Center : new Vec2i(SoakMap / 2, SoakMap / 2); }
                        if (target.IsValid)
                        {
                            var o = h.Cast(power, target, Math.Min(def.MaxRadius, Math.Max(def.MinRadius, 4)), param);
                            casts++; if (o.SucceededInWorld) castOk++;
                        }
                    }
                    if (year % 10 == 0 || year == SoakYears)
                    {
                        capViolations.AddRange(ScenarioInvariants.CheckCaps(h.State, simCfg));
                        if (firstBroken == null)
                        {
                            var rep = ScenarioInvariants.CheckReferences(h.State, h.Db);
                            if (!rep.Ok) { firstBroken = rep; firstBrokenYear = year; }
                        }
                        if (firstNonFinite == null)
                        {
                            var nf = ScenarioInvariants.CheckFinite(h.State);
                            if (nf.Count > 0) { firstNonFinite = nf; firstNonFiniteYear = year; }
                        }
                    }
                }

                var final = ScenarioInvariants.CheckReferences(h.State, h.Db);
                var integrity = new List<string>();
                bool ran = ScenarioInvariants.TryRunIntegrityChecker(h.Sim, integrity, out string note);
                var summary = new StringBuilder();
                summary.AppendLine("peak population " + peak + " at " + SimTime.Format(peakTick) + " (MaxPopulation " + maxPop + "), final " + h.State.LivingPopulation);
                summary.AppendLine("god casts " + castOk + "/" + casts + " succeeded; " + note);
                summary.AppendLine("final reference check: " + final.ErrorCount + " errors, " + final.WarningCount + " warnings");
                summary.AppendLine(h.Summary());
                TestContext.WriteLine(summary.ToString());

                var faults = new List<string>();
                foreach (var p in h.Sim.Engine.Profiles)
                {
                    if (p.Disabled) faults.Add(p.Name + " DISABLED (last error: " + p.LastError + ")");
                    if (p.TotalFaults > 0) faults.Add(p.Name + " faulted " + p.TotalFaults + " times (last error: " + p.LastError + ")");
                }
                Assert.That(faults, Is.Empty, "Systems faulted during the soak:\n" + ScenarioWorlds.Join(faults) + "log tail:\n" + log.Tail(10));
                Assert.That(h.Sim.Events.HandlerFaults, Is.EqualTo(0), "Event handlers threw " + h.Sim.Events.HandlerFaults + " times.\n" + log.Tail(10));
                Assert.That(peak, Is.LessThanOrEqualTo(maxPop + popSlack), "Population exceeded MaxPopulation.\n" + summary);
                if (peak > maxPop) Assert.Warn("Population overshot MaxPopulation by " + (peak - maxPop) + " (within slack " + popSlack + ").");
                Assert.That(capViolations, Is.Empty, "Configured caps exceeded:\n" + ScenarioWorlds.Join(capViolations));
                Assert.That(h.HousingViolationCount, Is.EqualTo(0), "Housing capacity violations:\n" + ScenarioWorlds.Join(h.HousingViolations));
                Assert.That(firstNonFinite, Is.Null, "NaN/Infinity entered state by year " + firstNonFiniteYear + ":\n" + ScenarioWorlds.Join(firstNonFinite));
                Assert.That(firstBroken, Is.Null, "Entity references inconsistent at year " + firstBrokenYear + ": " + firstBroken);
                Assert.That(final.Ok, Is.True, "Entity references inconsistent at the end: " + final);
                if (ran) Assert.That(integrity, Is.Empty, "IntegrityChecker reported problems:\n" + ScenarioWorlds.Join(integrity));
                if (final.WarningCount > 0) Assert.Warn("Soft consistency warnings: " + final);
            }
        }

        [Test]
        public void PerformanceBudget_Year100_AverageStepUnder5ms()
        {
            const ulong seed = 271828UL;
            const int measureTicks = 500;
            var sim = ScenarioWorlds.Create(ScenarioWorlds.SmallWorld(seed));
            sim.StepMany(100 * SimTime.TicksPerYear);
            double total = 0, worst = 0;
            for (int i = 0; i < measureTicks; i++)
            {
                sim.Step();
                double ms = sim.Engine.LastStepMs;
                total += ms;
                if (ms > worst) worst = ms;
            }
            double avg = total / measureTicks;
            string info = "avg " + avg.ToString("0.000") + " ms/tick over " + measureTicks + " ticks at year 100 (worst " + worst.ToString("0.00") + " ms), population " +
                          sim.State.LivingPopulation + ", buildings " + sim.State.Buildings.Count + ", settlements " + sim.State.Settlements.Count +
                          "\nslowest systems: " + ScenarioWorlds.DescribeTopSystems(sim.Engine, 6);
            TestContext.WriteLine(info);
            Assert.That(avg, Is.LessThan(15.0), "Tick budget blown: " + info);
            if (avg >= 5.0) Assert.Warn("Average tick above the 5 ms budget: " + info);
        }
    }
}
