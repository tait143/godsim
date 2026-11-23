using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>
    /// Scenarios 3 (AgesProgress) and 4 (RenovationThroughAges): a 5-race world with ResearchMultiplier 15
    /// simulated for 80 years.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    [Category("Slow")]
    public sealed class ProgressionScenarioTests
    {
        private const ulong Seed = 777UL;
        private const int Years = 80;
        private const int RenovationWindowYears = 30;

        private ScenarioHarness _h;

        [OneTimeSetUp]
        public void RunWorld()
        {
            var cfg = ScenarioWorlds.SmallWorld(Seed);
            cfg.ResearchMultiplier = 15f;
            var log = new CollectingLogger();
            _h = new ScenarioHarness(ScenarioWorlds.Create(cfg, log), log);
            _h.RunYears(Years);
            TestContext.Progress.WriteLine("[Progression] seed " + Seed + "\n" + _h.Summary() + "\nAges: " + DescribeAges());
        }

        [OneTimeTearDown]
        public void TearDown() { _h?.Dispose(); }

        [Test]
        public void AgesProgress_AtLeastTwoCivsReachIronWithin80Years()
        {
            int reached = 0;
            foreach (var kv in _h.MaxAgeReached) if (kv.Value >= AgeId.Iron) reached++;
            Assert.That(reached, Is.GreaterThanOrEqualTo(2),
                "Only " + reached + " civs reached Iron or later in " + Years + " years at 15x research.\nAges: " + DescribeAges() + "\n" + _h.Summary());
        }

        [Test]
        public void AgesProgress_NoCivSkipsAnAgeAndRulesAreMetOnAdvance()
        {
            Assert.That(_h.AgeViolations, Is.Empty, "Age advancement rule violations:\n" + ScenarioWorlds.Join(_h.AgeViolations));
            // The civ's final age must equal its starting age plus the number of advancements observed.
            var failures = new List<string>();
            var counts = new Dictionary<int, int>();
            foreach (var e in _h.AgeAdvances) { counts.TryGetValue(e.Event.CivilizationId, out int n); counts[e.Event.CivilizationId] = n + 1; }
            foreach (int civId in _h.InitialCivIds)
            {
                var civ = _h.State.Civilizations.Get(civId);
                if (civ == null) continue;
                counts.TryGetValue(civId, out int n);
                _h.InitialAges.TryGetValue(civId, out var start);
                if ((int)civ.Age - (int)start != n)
                    failures.Add("civ " + civId + " went " + start + " -> " + civ.Age + " but " + n + " AgeAdvancedEvents were published");
            }
            Assert.That(failures, Is.Empty, ScenarioWorlds.Join(failures));
        }

        [Test]
        public void RenovationThroughAges_BuildingsAdoptTheNewAgeStyleWithin30Years()
        {
            Assert.That(_h.UpgradeViolations, Is.Empty, "BuildingUpgradedEvent inconsistencies:\n" + ScenarioWorlds.Join(_h.UpgradeViolations));
            long window = SimTime.YearsToTicks(RenovationWindowYears);
            long end = _h.Now;
            var checkedPairs = new HashSet<long>();
            var failures = new List<string>();
            var report = new StringBuilder();
            foreach (var adv in _h.AgeAdvances)
            {
                var e = adv.Event;
                if (adv.Tick + window > end) continue; // not enough time left to renovate
                long key = ((long)e.CivilizationId << 8) | (byte)e.NewAge;
                if (!checkedPairs.Add(key)) continue;
                var civ = _h.State.Civilizations.Get(e.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;

                bool upgradeSeen = false;
                foreach (var u in _h.Upgrades)
                    if (u.CivilizationId == e.CivilizationId && u.StyleAge >= e.NewAge && u.Tick >= adv.Tick && u.Tick <= adv.Tick + window) { upgradeSeen = true; break; }

                int styled = 0, total = 0;
                var b = _h.State.Buildings.Items;
                for (int i = 0; i < b.Count; i++)
                {
                    if (b[i].IsDestroyed || b[i].CivilizationId != civ.Id) continue;
                    total++;
                    if (b[i].StyleAge >= e.NewAge) styled++;
                }
                report.AppendLine("  civ " + civ.Id + " entered " + e.NewAge + " at " + SimTime.Format(adv.Tick) + ": upgradeEvent=" + upgradeSeen +
                                  ", buildings styled >= " + e.NewAge + ": " + styled + "/" + total);
                if (!upgradeSeen) failures.Add("civ " + civ.Id + " entered " + e.NewAge + " at " + SimTime.Format(adv.Tick) + " but no BuildingUpgradedEvent renovated any of its buildings to that age within " + RenovationWindowYears + " years");
                if (styled == 0) failures.Add("civ " + civ.Id + " has no building with StyleAge >= " + e.NewAge);
            }
            TestContext.WriteLine("Renovation report:\n" + report);
            Assert.That(checkedPairs.Count, Is.GreaterThan(0),
                "No civ advanced an age early enough (before year " + (Years - RenovationWindowYears) + ") to check renovation.\nAges: " + DescribeAges());
            Assert.That(failures, Is.Empty, ScenarioWorlds.Join(failures) + report);
        }

        [Test]
        public void RenovationThroughAges_RenovatedBuildingsPredateTheAdvance()
        {
            // At least one renovation must touch a building placed before its owner reached the renovated style:
            // new construction automatically gets the current style, so this is what distinguishes renovation.
            int renovatedOld = 0;
            foreach (var u in _h.Upgrades)
            {
                long enteredTick = -1;
                foreach (var adv in _h.AgeAdvances)
                    if (adv.Event.CivilizationId == u.CivilizationId && adv.Event.NewAge == u.StyleAge) { enteredTick = adv.Tick; break; }
                if (enteredTick >= 0 && u.PlacedTick >= 0 && u.PlacedTick < enteredTick) renovatedOld++;
            }
            if (_h.AgeAdvances.Count == 0) Assert.Fail("No age advancement happened; cannot evaluate renovation.\n" + _h.Summary());
            Assert.That(renovatedOld, Is.GreaterThan(0), "No BuildingUpgradedEvent renovated a building that predates its civ's age advancement (" + _h.Upgrades.Count + " upgrade events total).");
        }

        private string DescribeAges()
        {
            var sb = new StringBuilder();
            foreach (var kv in _h.MaxAgeReached)
            {
                var civ = _h.State.Civilizations.Get(kv.Key);
                sb.Append("civ ").Append(kv.Key);
                if (civ != null) sb.Append(" (").Append(civ.Race).Append(civ.IsAlive ? "" : ", collapsed").Append(", research ").Append(civ.TotalResearch.ToString("0")).Append(')');
                sb.Append(": max ").Append(kv.Value).Append("; ");
            }
            return sb.ToString();
        }
    }
}
