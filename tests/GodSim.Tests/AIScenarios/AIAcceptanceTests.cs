using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.AIScenarios
{
    /// <summary>
    /// Behavioural acceptance tests for the civilization AI (Advisor Council + SettlementGovernor), run against
    /// the full composed simulation. These define "10/10": a failing test is a real AI gap. Thresholds come
    /// from the AI acceptance spec and must not be relaxed to make a test pass; gaps are tagged KnownGap.
    /// Every assertion message carries the measured numbers.
    /// </summary>
    [TestFixture]
    public class AIGrowthAcceptanceTests
    {
        /// <summary>(1) After 60 years every surviving capital: Houses in [pop/5, pop/3], >= 1 food producer per 15 people, TownCenter + Well/Storehouse-class.</summary>
        // KnownGap (2026-11-19, live Core): Saurian capitals outgrow housing/food in all 3 seeds (pop 2522: 184 houses, 32 food; pop 782: 96 houses, 39 food).
        [Test, Category("AI"), Category("Slow"), Category("KnownGap")]
        public void TownsGrowSensibly_After60Years()
        {
            var fails = new List<string>();
            var report = new StringBuilder();
            int capitals = 0;
            foreach (ulong seed in AIScenarioRuns.BaselineSeeds)
            {
            var h = AIScenarioRuns.Baseline(seed);
            if (!h.Snapshots.TryGetValue(AIScenarioRuns.GrowthYear, out var snaps)) { fails.Add("seed " + seed + ": no surviving capital at year 60"); continue; }
            report.Append("\n seed ").Append(seed).Append(": ").Append(snaps.Count).Append(" capitals");
            foreach (var c in snaps)
            {
                capitals++;
                int pop = c.Population;
                int minHouses = (int)Math.Floor(pop / 5f) - 1;   // ±1 house rounding slack only
                int maxHouses = (int)Math.Ceiling(pop / 3f) + 1;
                int minFood = Math.Max(1, pop / 15);
                report.Append("\n  ").Append(c.Describe()).Append(" houseRange=[").Append(minHouses).Append(',').Append(maxHouses).Append("] minFood=").Append(minFood);
                if (pop < 6) fails.Add(c.Race + ": capital barely alive after 60 years (pop " + pop + ")");
                if (c.Houses < minHouses || c.Houses > maxHouses) fails.Add(c.Race + ": houses " + c.Houses + " for pop " + pop + " (want " + minHouses + ".." + maxHouses + ")");
                if (c.FoodBuildings < minFood) fails.Add(c.Race + ": food producers " + c.FoodBuildings + " < " + minFood + " for pop " + pop);
                if (!c.HasTownCenter) fails.Add(c.Race + ": no TownCenter");
                if (!c.HasWaterOrStorage) fails.Add(c.Race + ": no Well/Storehouse-class building");
            }
            }
            TestContext.WriteLine("Year-60 capitals (" + capitals + "):" + report + "\nGaps: " + fails.Count + "\n  " + string.Join("\n  ", fails));
            Assert.IsTrue(fails.Count == 0, "Town growth gaps:\n  " + string.Join("\n  ", fails) + "\nCapitals:" + report);
        }

        /// <summary>(2) Houses are built ahead of growth: capital homeless share &lt; 20% in >= 80% of yearly samples (years 1..60).</summary>
        [Test, Category("AI"), Category("Slow")]
        public void Anticipation_HomelessShareStaysLow()
        {
            int total = 0, good = 0;
            var perRace = new Dictionary<RaceId, int[]>();
            float worst = 0f; string worstWhere = "";
            foreach (ulong seed in AIScenarioRuns.BaselineSeeds)
            foreach (var smp in AIScenarioRuns.Baseline(seed).YearSamples)
            {
                if (!smp.IsCapital || smp.Year > AIScenarioRuns.GrowthYear || smp.Population <= 0) continue;
                total++;
                bool ok = smp.HomelessShare < 0.2f;
                if (ok) good++;
                if (!perRace.TryGetValue(smp.Race, out var pr)) { pr = new int[2]; perRace[smp.Race] = pr; }
                pr[0]++; if (ok) pr[1]++;
                if (smp.HomelessShare > worst) { worst = smp.HomelessShare; worstWhere = smp.Race + " #" + smp.SettlementId + " y" + smp.Year + " pop=" + smp.Population + " homeless=" + smp.Homeless + " cap=" + smp.HousingCapacity; }
            }
            var sb = new StringBuilder();
            foreach (var kv in perRace) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value[1]).Append('/').Append(kv.Value[0]);
            float share = total == 0 ? 0f : good / (float)total;
            string msg = "capital-years with homeless<20%: " + good + "/" + total + " = " + share.ToString("P0") + " (want >= 80%); per race:" + sb + "; worst " + worst.ToString("P0") + " at " + worstWhere;
            TestContext.WriteLine(msg);
            Assert.IsTrue(total >= 150, "too few samples: " + total);
            Assert.IsTrue(share >= 0.8f, msg);
        }

        /// <summary>(7) Different races build differently: cosine distance of non-housing category-share vectors > 0.05 for >= 60% (6 of 10) of race pairs at year 80.</summary>
        // Flaky across Core revisions on 2026-11-19: failed 5/10 pairs (Elf-Saurian 0.017), then passed 6/6 (Dwarf had no year-80 capital in any seed).
        [Test, Category("AI"), Category("Slow")]
        public void RaceDistinctness_BuildingMixDiffers()
        {
            const float threshold = 0.05f;
            // Mean non-housing category-share vector per race over every baseline seed in which that race still has a capital at year 80.
            var byRace = new Dictionary<RaceId, float[]>();
            var samples = new Dictionary<RaceId, int>();
            var sb = new StringBuilder();
            foreach (ulong seed in AIScenarioRuns.BaselineSeeds)
            {
                if (!AIScenarioRuns.Baseline(seed).Snapshots.TryGetValue(AIScenarioRuns.MixYear, out var snaps)) continue;
                foreach (var c in snaps)
                {
                    var v = MixVector(c);
                    if (!byRace.TryGetValue(c.Race, out var acc)) { acc = new float[v.Length]; byRace[c.Race] = acc; samples[c.Race] = 0; }
                    for (int k = 0; k < v.Length; k++) acc[k] += v[k];
                    samples[c.Race]++;
                    sb.Append("\n  seed ").Append(seed).Append(' ').Append(c.Describe());
                }
            }
            var races = new List<RaceId>(byRace.Keys);
            races.Sort();
            int pairs = 0, distinct = 0;
            for (int i = 0; i < races.Count; i++)
                for (int j = i + 1; j < races.Count; j++)
                {
                    float d = CosineDistance(byRace[races[i]], byRace[races[j]]);
                    pairs++;
                    if (d > threshold) distinct++;
                    sb.Append("\n  ").Append(races[i]).Append('-').Append(races[j]).Append(": ").Append(d.ToString("0.000"));
                }
            const int required = 6; // of the 10 pairs of 5 races
            string msg = "races with a year-80 capital in some seed: " + races.Count + "/5; distinct pairs " + distinct + "/" + pairs + " (cosine distance > " + threshold + ", need " + required + " of 10)" + sb;
            TestContext.WriteLine(msg);
            Assert.IsTrue(distinct >= required, msg);
        }

        /// <summary>(8) Every capital's AiLog explains itself: >= 5 lines with reasons (year 60 and year 80).</summary>
        [Test, Category("AI"), Category("Slow")]
        public void Explainability_CapitalAiLogsHaveReasons()
        {
            var fails = new List<string>();
            int checkedCaps = 0;
            foreach (ulong seed in AIScenarioRuns.BaselineSeeds)
            foreach (int year in new[] { AIScenarioRuns.GrowthYear, AIScenarioRuns.MixYear })
            {
                var h = AIScenarioRuns.Baseline(seed);
                if (!h.Snapshots.TryGetValue(year, out var snaps)) continue;
                foreach (var c in snaps)
                {
                    checkedCaps++;
                    int withReason = 0;
                    foreach (var l in c.AiLog) if (AIScenarioHarness.HasReason(l)) withReason++;
                    TestContext.WriteLine("seed " + seed + " y" + year + " " + c.Race + " AiLog " + c.AiLog.Count + " lines, " + withReason + " with reasons" + AIScenarioHarness.Tail(c.AiLog, 4));
                    if (withReason < 5) fails.Add("seed " + seed + " y" + year + " " + c.Race + " '" + c.Name + "': " + withReason + "/" + c.AiLog.Count + " lines with reasons" + AIScenarioHarness.Tail(c.AiLog, 6));
                }
            }
            Assert.IsTrue(checkedCaps > 0, "no capitals to check");
            Assert.IsTrue(fails.Count == 0, "Unexplained capitals:\n  " + string.Join("\n  ", fails));
        }

        /// <summary>(9) No churn over 80 years: BuildQueue never above 8, never more than 2 queued orders of the same non-House type.</summary>
        [Test, Category("AI"), Category("Slow")]
        public void NoChurn_BuildQueueBounded()
        {
            var sb = new StringBuilder();
            bool ok = true;
            foreach (ulong seed in AIScenarioRuns.BaselineSeeds)
            {
                var h = AIScenarioRuns.Baseline(seed);
                sb.Append("\n seed ").Append(seed).Append(": max queue ").Append(h.MaxQueueSeen).Append(" (").Append(h.MaxQueueWhere).Append("); max same non-House type queued ")
                  .Append(h.MaxSameTypeSeen).Append(" (").Append(h.MaxSameTypeWhere).Append(')');
                for (int i = 0; i < Math.Min(5, h.ChurnViolations.Count); i++) sb.Append("\n    ").Append(h.ChurnViolations[i]);
                if (h.MaxQueueSeen > AIScenarioHarness.MaxQueueAllowed || h.MaxSameTypeSeen > AIScenarioHarness.MaxSameTypeQueued) ok = false;
            }
            TestContext.WriteLine(sb.ToString());
            Assert.IsTrue(ok, sb.ToString());
        }

        internal static float[] MixVector(CapitalSnapshot c)
        {
            var v = new float[c.CompleteByCategory.Length];
            float sum = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                if (i == (int)BuildingCategory.Housing) continue; // houses dominate every town; compare what else they build
                v[i] = c.CompleteByCategory[i];
                sum += v[i];
            }
            if (sum > 0f) for (int i = 0; i < v.Length; i++) v[i] /= sum;
            return v;
        }

        internal static float CosineDistance(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            if (na <= 0 || nb <= 0) return 1f;
            return (float)(1.0 - dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
        }
    }

    [TestFixture]
    public class AIResponseAcceptanceTests
    {
        private static readonly string[] s_foodWords = { "food", "farm", "fish", "hunt", "gather", "granary", "pasture", "mill", "famine", "starv", "hunger", "harvest" };

        internal static bool IsFoodLine(string line)
        {
            if (line == null) return false;
            string l = line.ToLowerInvariant();
            for (int i = 0; i < s_foodWords.Length; i++) if (l.Contains(s_foodWords[i])) return true;
            return false;
        }

        private static bool IsFoodJob(JobType j) => j == JobType.Gatherer || j == JobType.Hunter || j == JobType.Fisher || j == JobType.Farmer || j == JobType.Herder;

        private static int FoodWorkers(AIScenarioHarness h, Settlement s)
        {
            int n = 0;
            for (int i = 0; i < s.ResidentIds.Count; i++)
            {
                var p = h.State.People.Get(s.ResidentIds[i]);
                if (p != null && p.IsAlive && IsFoodJob(p.Job)) n++;
            }
            return n;
        }

        private static bool QueueHasCategory(AIScenarioHarness h, Settlement s, BuildingCategory cat)
        {
            for (int i = 0; i < s.BuildQueue.Count; i++)
                if (h.Db.TryGetBuilding(s.BuildQueue[i].Type, out var d) && d.Category == cat) return true;
            return false;
        }

        /// <summary>(3) Famine: drain the capital's food at year 15. Within 2 seasons the AiLog shows food decisions and food buildings/jobs rise; food recovers within 8 years.</summary>
        // Flaky across Core revisions on 2026-11-19: one revision logged 0 food-related AiLog lines in the window (1 line total); the latest logs 2/5.
        [Test, Category("AI"), Category("Slow")]
        public void FamineResponse_DrainedCapitalRecovers()
        {
            using (var h = new AIScenarioHarness(AIScenarioHarness.Config(7101)))
            {
                h.RunUntilYear(15);
                var cap = h.LargestCapital();
                Assert.IsNotNull(cap, "no capital alive at year 15");
                float before = cap.Stockpile.Get(ResourceType.Food);
                int foodWorkersBefore = FoodWorkers(h, cap);
                int foodBuildingsBefore = h.SampleOf(cap, h.Year).FoodBuildings;
                cap.Stockpile.SetRaw(ResourceType.Food, 0f);
                long drainTick = h.Now;
                int placedBefore = h.Placed.Count;

                int maxFoodWorkers = foodWorkersBefore;
                bool foodQueued = false;
                float maxFoodJobPriority = 0f;
                h.AfterStep = x =>
                {
                    if (!cap.IsAlive || x.Now > drainTick + 2 * SimTime.TicksPerSeason) return;
                    maxFoodWorkers = Math.Max(maxFoodWorkers, FoodWorkers(x, cap));
                    if (QueueHasCategory(x, cap, BuildingCategory.Food)) foodQueued = true;
                    maxFoodJobPriority = Math.Max(maxFoodJobPriority, Math.Max(cap.JobPriority[(int)JobType.Farmer], cap.JobPriority[(int)JobType.Gatherer]));
                };
                h.RunTicks(2 * SimTime.TicksPerSeason);

                int foodLines = 0;
                var window = new List<string>();
                foreach (var l in h.LinesOf(cap.Id, drainTick, h.Now)) { window.Add(l.Text); if (IsFoodLine(l.Text)) foodLines++; }
                bool foodPlaced = false;
                for (int i = placedBefore; i < h.Placed.Count; i++)
                    if (h.Placed[i].Event.SettlementId == cap.Id && h.Db.TryGetBuilding(h.Placed[i].Event.Type, out var d) && d.Category == BuildingCategory.Food) foodPlaced = true;
                bool newFoodCapacity = foodPlaced || foodQueued || maxFoodWorkers > foodWorkersBefore;

                // Recovery: within 8 years the stock is back to >= 50% of the pre-drain level (at least 20 food).
                float target = Math.Max(20f, 0.5f * before);
                int recoveredAfterYears = -1;
                float maxFood = 0f;
                for (int y = 1; y <= 8 && cap.IsAlive; y++)
                {
                    for (int t = 0; t < SimTime.TicksPerYear; t++)
                    {
                        h.Step();
                        float f = cap.Stockpile.Get(ResourceType.Food);
                        if (f > maxFood) maxFood = f;
                        if (recoveredAfterYears < 0 && f >= target) recoveredAfterYears = y;
                    }
                }
                int starved = 0;
                foreach (var d in h.StarvationDeaths) if (d.Tick >= drainTick && d.Event.SettlementId == cap.Id) starved++;

                string msg = cap.Race + " capital '" + cap.Name + "' food before=" + before.ToString("0") + " (" + foodBuildingsBefore + " food bldgs, " + foodWorkersBefore + " food workers)"
                    + "; 2-season window: AiLog lines=" + window.Count + " food-related=" + foodLines + ", foodPlaced=" + foodPlaced + " foodQueued=" + foodQueued
                    + " maxFoodWorkers=" + maxFoodWorkers + " maxFoodJobPriority=" + maxFoodJobPriority.ToString("0.00")
                    + "; recovery target " + target.ToString("0") + " reached after " + (recoveredAfterYears < 0 ? "NEVER" : recoveredAfterYears + "y") + " (max " + maxFood.ToString("0") + "), starvation deaths=" + starved
                    + AIScenarioHarness.Tail(window, 8);
                TestContext.WriteLine(msg);
                Assert.IsTrue(foodLines >= 1, "no food-related AiLog decision within 2 seasons: " + msg);
                Assert.IsTrue(newFoodCapacity, "no new food buildings/jobs within 2 seasons: " + msg);
                Assert.IsTrue(recoveredAfterYears > 0, "food did not recover within 8 years: " + msg);
            }
        }

        /// <summary>(4) Threat: declare war and put an enemy army 6 tiles from a capital. Within a year the Soldier priority or defensive builds rise.</summary>
        [Test, Category("AI"), Category("Slow")]
        public void ThreatResponse_EnemyArmyRaisesDefense()
        {
            using (var h = new AIScenarioHarness(AIScenarioHarness.Config(7202)))
            {
                h.RunUntilYear(20);
                var caps = h.Capitals();
                Assert.IsTrue(caps.Count >= 2, "need two living civs, have " + caps.Count);
                // Defender: largest capital not already at war; attacker: any other civ (largest capital).
                Settlement def = null, att = null;
                foreach (var c in caps) if (!AtWarWithAnyone(h, c.CivilizationId) && (def == null || c.Population > def.Population)) def = c;
                if (def == null) def = h.LargestCapital();
                foreach (var c in caps) if (c.CivilizationId != def.CivilizationId && (att == null || c.Population > att.Population)) att = c;
                Assert.IsNotNull(att, "no attacker");

                float soldierBefore = def.JobPriority[(int)JobType.Soldier];
                int militaryBefore = CountCategory(h, def, BuildingCategory.Military);
                int soldiersBefore = def.Soldiers;
                bool wasAtWar = AtWarWithAnyone(h, def.CivilizationId);

                var war = DeclareWar(h, h.State.Civilizations.Get(att.CivilizationId), h.State.Civilizations.Get(def.CivilizationId));
                var army = PlaceArmy(h, att, def, war, 6, 8);
                Assert.IsNotNull(army, "could not raise a test army");
                int armySize = army.SoldierIds.Count;
                Vec2i armyPos = army.Position;
                int placedBefore = h.Placed.Count;
                long t0 = h.Now;

                float soldierMax = soldierBefore;
                bool defenseQueued = false;
                int soldiersMax = soldiersBefore;
                h.AfterStep = x =>
                {
                    if (!def.IsAlive) return;
                    soldierMax = Math.Max(soldierMax, def.JobPriority[(int)JobType.Soldier]);
                    soldiersMax = Math.Max(soldiersMax, def.Soldiers);
                    if (QueueHasCategory(x, def, BuildingCategory.Military)) defenseQueued = true;
                };
                h.RunTicks(SimTime.TicksPerYear);

                bool defensePlaced = false;
                for (int i = placedBefore; i < h.Placed.Count; i++)
                    if (h.Placed[i].Event.SettlementId == def.Id && h.Db.TryGetBuilding(h.Placed[i].Event.Type, out var d) && d.Category == BuildingCategory.Military) defensePlaced = true;
                int threatLines = 0;
                var window = new List<string>();
                foreach (var l in h.LinesOf(def.Id, t0, h.Now))
                {
                    window.Add(l.Text);
                    string s = l.Text.ToLowerInvariant();
                    if (s.Contains("war") || s.Contains("threat") || s.Contains("defen") || s.Contains("soldier") || s.Contains("army") || s.Contains("enemy")) threatLines++;
                }
                string msg = def.Race + " capital '" + def.Name + "' (alive=" + def.IsAlive + ", wasAtWar=" + wasAtWar + ") vs " + att.Race + " army of " + armySize + " placed at " + armyPos + " (capital " + def.Center + ", army alive after 1y=" + army.IsAlive + ")"
                    + ": Soldier priority " + soldierBefore.ToString("0.00") + " -> max " + soldierMax.ToString("0.00")
                    + ", soldiers " + soldiersBefore + " -> max " + soldiersMax + ", military bldgs before=" + militaryBefore
                    + ", defense queued=" + defenseQueued + " placed=" + defensePlaced + ", threat AiLog lines=" + threatLines + AIScenarioHarness.Tail(window, 8);
                TestContext.WriteLine(msg);
                Assert.IsTrue(soldierMax > soldierBefore + 0.01f || defenseQueued || defensePlaced, "no defensive reaction within a year: " + msg);
            }
        }

        /// <summary>(5) Disaster recovery: burn half the capital's houses at year 25; within 10 years housing capacity is back to >= 90%.</summary>
        [Test, Category("AI"), Category("Slow")]
        public void DisasterRecovery_HousingRebuilt()
        {
            using (var h = new AIScenarioHarness(AIScenarioHarness.Config(7303)))
            {
                h.RunUntilYear(25);
                Settlement town = null; int bestHouses = -1;
                foreach (var c in h.Capitals()) { int n = h.SampleOf(c, h.Year).Houses; if (n > bestHouses) { bestHouses = n; town = c; } }
                Assert.IsNotNull(town, "no capital at year 25");
                Assert.IsTrue(bestHouses >= 4, "largest capital has only " + bestHouses + " houses at year 25");
                int capBefore = h.HousingCapacity(town);
                int popBefore = town.Population;
                var houses = new List<Building>();
                h.ForEachStanding(town, (b, d) => { if (b.Type == BuildingType.House && b.IsComplete) houses.Add(b); });
                houses.Sort((a, b) => a.Id.CompareTo(b.Id));
                int destroyed = 0;
                for (int i = 0; i < houses.Count; i += 2) { h.Sim.Ops.DestroyBuilding(houses[i], DestroyCause.Fire); destroyed++; }
                h.Sim.Ops.FlushRemovals();
                int capAfter = h.HousingCapacity(town);

                int recoveredYear = -1;
                var curve = new StringBuilder();
                for (int y = 1; y <= 10 && town.IsAlive; y++)
                {
                    h.RunYears(1);
                    int cap = h.HousingCapacity(town);
                    curve.Append(' ').Append(cap).Append('/').Append(town.Population);
                    if (recoveredYear < 0 && cap >= 0.9f * capBefore) recoveredYear = y;
                }
                string msg = town.Race + " capital '" + town.Name + "': capacity " + capBefore + " (pop " + popBefore + ") -> " + capAfter + " after burning " + destroyed + "/" + houses.Count
                    + " houses; yearly cap/pop:" + curve + "; recovered(>=90%) after " + (recoveredYear < 0 ? "NEVER" : recoveredYear + "y");
                TestContext.WriteLine(msg);
                Assert.IsTrue(recoveredYear > 0, msg);
            }
        }

        /// <summary>(6) Modernization (ResearchMultiplier 25): after each age advance in the first 15 years, the civ places/renovates a building styled for the new age within 25 years.</summary>
        [Test, Category("AI"), Category("Slow")]
        public void Modernization_NewAgeStylesAppear()
        {
            using (var h = new AIScenarioHarness(AIScenarioHarness.Config(7404, 25f)))
            {
                h.RunUntilYear(40);
                var advances = new List<Stamped<AgeAdvancedEvent>>();
                foreach (var a in h.AgeAdvances) if (a.Year <= 15) advances.Add(a);
                Assert.IsTrue(advances.Count > 0, "no age advance in 15 years even with ResearchMultiplier 25 (total advances " + h.AgeAdvances.Count + ")");

                // Style ages of every placed building, looked up at placement (buildings are created with StyleAge = civ.Age).
                var fails = new List<string>();
                var sb = new StringBuilder();
                foreach (var a in advances)
                {
                    long end = a.Tick + 25L * SimTime.TicksPerYear;
                    int newStyled = 0, exactStyled = 0, placedInCiv = 0;
                    for (int i = 0; i < h.Placed.Count; i++)
                    {
                        var p = h.Placed[i];
                        if (p.Tick < a.Tick || p.Tick > end) continue;
                        var s = h.State.Settlements.Get(p.Event.SettlementId);
                        int civ = s != null ? s.CivilizationId : -1;
                        if (civ != a.Event.CivilizationId) continue;
                        placedInCiv++;
                        if (h.PlacedStyle.TryGetValue(p.Event.BuildingId, out var style) && style >= a.Event.NewAge) { newStyled++; if (style == a.Event.NewAge) exactStyled++; }
                    }
                    for (int i = 0; i < h.Upgraded.Count; i++)
                    {
                        var u = h.Upgraded[i];
                        if (u.Tick < a.Tick || u.Tick > end || u.Event.StyleAge < a.Event.NewAge) continue;
                        var b = h.State.Buildings.Get(u.Event.BuildingId);
                        if (b != null && b.CivilizationId == a.Event.CivilizationId) { newStyled++; if (u.Event.StyleAge == a.Event.NewAge) exactStyled++; }
                    }
                    var civObj = h.State.Civilizations.Get(a.Event.CivilizationId);
                    string line = (civObj != null ? civObj.Race.ToString() : "civ" + a.Event.CivilizationId) + " " + a.Event.OldAge + "->" + a.Event.NewAge + " @y" + a.Year
                        + ": placed " + placedInCiv + ", styled >= new age " + newStyled + ", styled == new age " + exactStyled;
                    sb.Append("\n  ").Append(line);
                    if (exactStyled == 0) fails.Add(line);
                }
                TestContext.WriteLine("Age advances (first 15y):" + sb);
                Assert.IsTrue(fails.Count == 0, "age advances without new-age buildings within 25 years:\n  " + string.Join("\n  ", fails));
            }
        }

        // ------------------------------------------------------------------ helpers
        private static bool AtWarWithAnyone(AIScenarioHarness h, int civId)
        {
            foreach (var w in h.State.Diplomacy.Wars) if (w.IsActive && (w.AttackerId == civId || w.DefenderId == civId)) return true;
            return false;
        }

        private static int CountCategory(AIScenarioHarness h, Settlement s, BuildingCategory cat)
        {
            int n = 0;
            h.ForEachStanding(s, (b, d) => { if (d.Category == cat) n++; });
            return n;
        }

        /// <summary>Declares a war the way the diplomacy module does (state + event), so every system sees it.</summary>
        internal static War DeclareWar(AIScenarioHarness h, Civilization attacker, Civilization defender)
        {
            var state = h.State;
            var rel = state.Diplomacy.Get(attacker.Id, defender.Id);
            if (rel.AtWar) return state.Diplomacy.GetWar(rel.WarId);
            var war = new War { Id = state.Ids.War(), AttackerId = attacker.Id, DefenderId = defender.Id, StartTick = state.Tick, CasusBelli = "test" };
            state.Diplomacy.Wars.Add(war);
            rel.HasContact = true;
            rel.LastContactTick = state.Tick;
            rel.WarId = war.Id;
            rel.TradeAgreement = false;
            var old = rel.Stance;
            rel.Stance = DiplomaticStance.War;
            state.Stats.WarsDeclared++;
            h.Sim.Events.Publish(new WarDeclaredEvent { WarId = war.Id, AttackerId = attacker.Id, DefenderId = defender.Id });
            h.Sim.Events.Publish(new StanceChangedEvent { CivA = rel.CivA, CivB = rel.CivB, OldStance = old, NewStance = DiplomaticStance.War });
            return war;
        }

        /// <summary>Raises an army (WorldOps.CreateArmy) of up to maxSoldiers adults from home and drops it ~distance tiles from target.</summary>
        internal static Army PlaceArmy(AIScenarioHarness h, Settlement home, Settlement target, War war, int distance, int maxSoldiers)
        {
            var civ = h.State.Civilizations.Get(home.CivilizationId);
            var soldiers = new List<Person>();
            for (int i = 0; i < home.ResidentIds.Count && soldiers.Count < maxSoldiers; i++)
            {
                var p = h.State.People.Get(home.ResidentIds[i]);
                if (p != null && p.IsAlive && p.IsAdult && p.ArmyId < 0) soldiers.Add(p);
            }
            if (soldiers.Count == 0) return null;
            var a = h.Sim.Ops.CreateArmy(civ, home, soldiers);
            a.WarId = war.Id;
            a.TargetSettlementId = target.Id;
            a.State = ArmyState.Marching;
            a.Strength = soldiers.Count;
            a.Position = NearbyLand(h.State.Map, target.Center, distance);
            a.Destination = target.Center;
            return a;
        }

        private static Vec2i NearbyLand(WorldMap map, Vec2i c, int distance)
        {
            for (int r = distance; r <= distance + 10; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int x = c.X + dx, y = c.Y + dy;
                        if (!map.InBounds(x, y)) continue;
                        if (map.Biomes[map.Index(x, y)].IsLand()) return new Vec2i(x, y);
                    }
            return c;
        }
    }

    [TestFixture]
    public class AIDeterminismAcceptanceTests
    {
        /// <summary>(10) Same seed, all AI modules active: identical ComputeHash at every 5-year checkpoint up to year 30.</summary>
        [Test, Category("AI"), Category("Slow")]
        public void Determinism_SameSeedSameHashAfter30Years()
        {
            var a = new List<ulong>();
            var b = new List<ulong>();
            foreach (var list in new[] { a, b })
            {
                using (var h = new AIScenarioHarness(AIScenarioHarness.Config(4242)))
                {
                    for (int y = 5; y <= 30; y += 5) { h.RunUntilYear(y); list.Add(h.Sim.ComputeHash()); }
                }
            }
            int firstDiff = -1;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) { firstDiff = (i + 1) * 5; break; }
            string msg = "hashes y30: " + a[a.Count - 1].ToString("X16") + " vs " + b[b.Count - 1].ToString("X16") + (firstDiff > 0 ? "; first divergence by year " + firstDiff : "");
            TestContext.WriteLine(msg);
            Assert.AreEqual(a[a.Count - 1], b[b.Count - 1], msg);
            Assert.AreEqual(-1, firstDiff, msg);
        }
    }
}
