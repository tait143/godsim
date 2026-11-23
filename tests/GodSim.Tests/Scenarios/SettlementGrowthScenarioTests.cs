using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>
    /// Scenarios 1 (HousingRule) and 2 (TownsGrow). One default-settings world (5 races, 128x128, warfare and
    /// disasters on) is simulated for 60 years; capitals are snapshotted at year 50.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    [Category("Slow")]
    public sealed class SettlementGrowthScenarioTests
    {
        private const ulong Seed = 4242UL;
        private const int SnapshotYear = 50;
        private const int TotalYears = 60;

        private ScenarioHarness _h;
        private readonly List<CapitalSnapshot> _capitals = new List<CapitalSnapshot>();

        [OneTimeSetUp]
        public void RunWorld()
        {
            var log = new CollectingLogger();
            var sim = ScenarioWorlds.Create(ScenarioWorlds.SmallWorld(Seed), log);
            _h = new ScenarioHarness(sim, log);
            _h.RunYears(SnapshotYear);
            foreach (int civId in _h.InitialCivIds) _capitals.Add(CapitalSnapshot.Take(_h, civId));
            _h.RunYears(TotalYears - SnapshotYear);
            TestContext.Progress.WriteLine("[SettlementGrowth] seed " + Seed + "\n" + _h.Summary());
        }

        [OneTimeTearDown]
        public void TearDown() { _h?.Dispose(); }

        [Test]
        public void HousingRule_NoDwellingEverExceedsCapacity()
        {
            Assert.That(_h.HousingViolationCount, Is.EqualTo(0),
                "Dwelling capacity rule broken " + _h.HousingViolationCount + " times:\n" + ScenarioWorlds.Join(_h.HousingViolations));
        }

        [Test]
        public void HousingRule_OccupancyAndHomelessnessStayWithinDesignBands()
        {
            const int minPop = 10, minSamples = 8; // 8 samples = 2 years at one sample per season
            var bySettlement = new SortedDictionary<int, List<SettlementSample>>();
            foreach (var smp in _h.Samples)
            {
                if (smp.Population < minPop) continue;
                if (!bySettlement.TryGetValue(smp.SettlementId, out var list)) { list = new List<SettlementSample>(); bySettlement[smp.SettlementId] = list; }
                list.Add(smp);
            }
            Assert.That(bySettlement.Count, Is.GreaterThan(0), "No settlement ever reached " + minPop + " people in " + TotalYears + " years.\n" + _h.Summary());

            var failures = new List<string>();
            var report = new StringBuilder();
            double ratioSum = 0; int ratioN = 0, homelessOk = 0, homelessN = 0;
            foreach (var kv in bySettlement)
            {
                var list = kv.Value;
                double sRatio = 0; int sRatioN = 0, noHouse = 0, sHomelessOk = 0;
                foreach (var smp in list)
                {
                    if (smp.CompleteHouses > 0) { sRatio += smp.HouseOccupancy; sRatioN++; }
                    else noHouse++;
                    if (smp.HomelessFraction < 0.25f) sHomelessOk++;
                }
                ratioSum += sRatio; ratioN += sRatioN; homelessOk += sHomelessOk; homelessN += list.Count;
                double avg = sRatioN > 0 ? sRatio / sRatioN : 0;
                double okFrac = sHomelessOk / (double)list.Count;
                report.AppendLine("  settlement " + kv.Key + ": samples=" + list.Count + " avgResidentsPerHouse=" + avg.ToString("0.00") +
                                  " homeless<25% in " + (okFrac * 100).ToString("0") + "% samples, noHouse samples=" + noHouse +
                                  ", last pop=" + list[list.Count - 1].Population + " houses=" + list[list.Count - 1].CompleteHouses);
                if (list.Count < minSamples) continue;
                if (noHouse > list.Count / 4) failures.Add("settlement " + kv.Key + " had >= " + minPop + " people but no complete House in " + noHouse + "/" + list.Count + " samples");
                if (sRatioN >= minSamples && (avg < 2.5 || avg > 5.0)) failures.Add("settlement " + kv.Key + " averages " + avg.ToString("0.00") + " residents per House (expected 2.5..5)");
                if (okFrac < 0.6) failures.Add("settlement " + kv.Key + " had >= 25% homeless in " + ((1 - okFrac) * 100).ToString("0") + "% of samples");
            }
            double worldAvg = ratioN > 0 ? ratioSum / ratioN : 0;
            double worldOk = homelessN > 0 ? homelessOk / (double)homelessN : 0;
            TestContext.WriteLine("World avg residents/House " + worldAvg.ToString("0.00") + ", homeless<25% in " + (worldOk * 100).ToString("0") + "% of samples\n" + report);
            Assert.That(worldAvg, Is.InRange(2.5, 5.0), "World-wide residents per complete House out of band.\n" + report);
            Assert.That(worldOk, Is.GreaterThanOrEqualTo(0.75), "Homelessness >= 25% too often world-wide.\n" + report);
            Assert.That(failures, Is.Empty, "Housing bands violated:\n" + ScenarioWorlds.Join(failures) + report);
        }

        [Test]
        public void TownsGrow_CapitalsHaveCoreBuildingsAfter50Years()
        {
            var failures = new List<string>();
            int surviving = 0;
            var report = new StringBuilder();
            foreach (var c in _capitals)
            {
                report.AppendLine("  " + c);
                if (!c.CivAlive || c.CapitalId < 0) continue;
                surviving++;
                if (c.CompleteBuildings < 8) failures.Add(c.Tag + " capital has only " + c.CompleteBuildings + " complete buildings (expected >= 8)");
                if (!c.HasTownCenter) failures.Add(c.Tag + " capital has no complete TownCenter");
                if (c.FoodProducers < 1) failures.Add(c.Tag + " capital has no complete food producer");
                int needHouses = Math.Max(1, (int)Math.Floor(c.Population / 5.0 * 0.8));
                if (c.CompleteHouses < needHouses) failures.Add(c.Tag + " capital has " + c.CompleteHouses + " Houses for " + c.Population + " people (expected >= " + needHouses + ", ~1 per 5)");
            }
            TestContext.WriteLine("Capitals at year " + SnapshotYear + ":\n" + report);
            Assert.That(surviving, Is.GreaterThan(0), "No initial civilization survived " + SnapshotYear + " years.\n" + report + _h.Summary());
            Assert.That(failures, Is.Empty, "Town growth expectations failed:\n" + ScenarioWorlds.Join(failures) + report);
        }

        [Test]
        public void TownsGrow_WorldIsAliveAndGrowing()
        {
            int initialPop = _h.WorldSamples.Count > 0 ? _h.WorldSamples[0].Population : 0;
            int finalPop = _h.State.LivingPopulation;
            int births = 0, completed = 0;
            foreach (var y in _h.Years) { births += y.Births; completed += y.BuildingsCompleted; }
            Assert.That(births, Is.GreaterThan(0), "Nobody was born in " + TotalYears + " years.");
            Assert.That(completed, Is.GreaterThanOrEqualTo(_h.InitialCivIds.Count * 8), "Too few buildings completed world-wide.\n" + _h.Summary());
            Assert.That(finalPop, Is.GreaterThan(initialPop), "Population did not grow over " + TotalYears + " years.\n" + _h.Summary());
        }

        /// <summary>What an initial civ's capital looked like at the snapshot year.</summary>
        private sealed class CapitalSnapshot
        {
            public int CivId;
            public string Tag;
            public bool CivAlive;
            public int CapitalId = -1;
            public int Population;
            public int CompleteBuildings;
            public int CompleteHouses;
            public int FoodProducers;
            public bool HasTownCenter;
            public string Buildings = "";

            public static CapitalSnapshot Take(ScenarioHarness h, int civId)
            {
                var s = h.State;
                var civ = s.Civilizations.Get(civId);
                var snap = new CapitalSnapshot { CivId = civId, Tag = "civ " + civId };
                if (civ == null) return snap;
                snap.Tag = "civ " + civId + " (" + civ.Race + " '" + civ.Name + "', " + civ.Age + ")";
                snap.CivAlive = civ.IsAlive;
                var cap = s.Settlements.Get(civ.CapitalId);
                if (cap == null || !cap.IsAlive) return snap;
                snap.CapitalId = cap.Id;
                var smp = h.SampleSettlement(cap);
                snap.Population = smp.Population;
                snap.CompleteBuildings = smp.CompleteBuildings;
                snap.CompleteHouses = smp.CompleteHouses;
                snap.FoodProducers = smp.FoodProducers;
                var counts = new int[Counts.Buildings];
                foreach (int bid in cap.BuildingIds)
                {
                    var b = s.Buildings.Get(bid);
                    if (b == null || b.IsDestroyed || !b.IsComplete) continue;
                    counts[(int)b.Type]++;
                    if (b.Type == BuildingType.TownCenter) snap.HasTownCenter = true;
                }
                var sb = new StringBuilder();
                for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) sb.Append((BuildingType)i).Append('x').Append(counts[i]).Append(' ');
                snap.Buildings = sb.ToString();
                return snap;
            }

            public override string ToString() =>
                Tag + ": alive=" + CivAlive + " capital=" + CapitalId + " pop=" + Population + " complete=" + CompleteBuildings +
                " houses=" + CompleteHouses + " food=" + FoodProducers + " townCenter=" + HasTownCenter + " [" + Buildings + "]";
        }
    }
}
