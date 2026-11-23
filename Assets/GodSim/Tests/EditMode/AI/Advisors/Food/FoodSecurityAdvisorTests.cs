using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class FoodSecurityAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static BuildingDefinition Producer(BuildingType t, AgeId min, AgeId max, JobType job, int slots, float food, PlacementRule rule, int size, float work)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = BuildingCategory.Food, MinAge = min, MaxAge = max,
                Width = size, Height = size, BuildWork = work, Job = job, JobSlots = slots,
                OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, food) }, Placement = rule,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 10f) },
            };
        }

        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(Producer(BuildingType.GathererHut, AgeId.Dawn, AgeId.Iron, JobType.Gatherer, 3, 0.09f, PlacementRule.Outskirts, 1, 10f));
            db.Register(Producer(BuildingType.HuntingLodge, AgeId.Dawn, AgeId.Medieval, JobType.Hunter, 2, 0.12f, PlacementRule.NearForest | PlacementRule.Outskirts, 1, 12f));
            db.Register(Producer(BuildingType.FishingHut, AgeId.Dawn, AgeId.Ascension, JobType.Fisher, 2, 0.15f, PlacementRule.NearWater, 1, 12f));
            db.Register(Producer(BuildingType.Farm, AgeId.Bronze, AgeId.Ascension, JobType.Farmer, 4, 0.22f, PlacementRule.Fertile, 3, 25f));
            db.Register(Producer(BuildingType.IndustrialFarm, AgeId.Modern, AgeId.Ascension, JobType.Farmer, 4, 0.6f, PlacementRule.Fertile | PlacementRule.Outskirts, 3, 60f));
            var hydro = Producer(BuildingType.HydroponicsTower, AgeId.Dawn, AgeId.Ascension, JobType.Engineer, 3, 1.2f, PlacementRule.None, 2, 80f);
            hydro.AllowedRaces = RaceMask.Elf; // race-gated: never voted for humans
            db.Register(hydro);
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Granary, Name = "Granary", Category = BuildingCategory.Storage, MinAge = AgeId.Bronze,
                MinPopulation = 15, MaxPerSettlement = 3, Width = 2, Height = 2, BuildWork = 35f, StorageCapacity = 150f,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 25f), new ResourceAmount(ResourceType.Stone, 20f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage, MinAge = AgeId.Dawn,
                MinPopulation = 8, Width = 2, Height = 2, BuildWork = 15f, StorageCapacity = 120f,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 20f), new ResourceAmount(ResourceType.Stone, 6f) },
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement Setup(GameDatabase db, AgeId age, int adults, out SimContext ctx, out Civilization civ, bool withStaffedFarm)
        {
            ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), adults, out civ);
            civ.Age = age;
            if (withStaffedFarm)
            {
                var site = ctx.Ops.FindFreeSite(new Vec2i(36, 32), 3, 3, 10);
                var farm = ctx.Ops.CreateBuilding(s, BuildingType.Farm, site, 0, true);
                Assert.IsNotNull(farm);
                int assigned = 0;
                for (int i = 0; i < s.ResidentIds.Count && assigned < 4; i++)
                    if (ctx.Ops.AssignJob(ctx.State.People.Get(s.ResidentIds[i]), farm, JobType.Farmer)) assigned++;
                Assert.AreEqual(4, assigned);
            }
            return s;
        }

        private static void SetFood(Settlement s, float stock, float income, float expense)
        {
            s.Stockpile.SetRaw(ResourceType.Food, stock);
            s.Stockpile.AvgIncome[(int)ResourceType.Food] = income;
            s.Stockpile.AvgExpense[(int)ResourceType.Food] = expense;
        }

        /// <summary>
        /// Per-run board value of a per-council-cycle expectation. Votes are normalized per 24-tick council cycle
        /// (AdvisorSystemBase.CycleScale = TickInterval / 24), so one run of this 12-tick advisor contributes half of
        /// what it votes per cycle. Thresholds below are written per cycle and checked against a single run.
        /// </summary>
        private static float PerRun(float perCycle) => perCycle * System.Math.Min(1f, new FoodSecurityAdvisor().TickInterval / 24f);

        private static void RunOnce(FoodSecurityAdvisor adv, SimContext ctx, long tick)
        {
            ctx.State.Tick = tick;
            ctx.DeltaTicks = adv.TickInterval;
            adv.Tick(ctx);
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        private const long AutumnDay54 = SimTime.TicksPerYear + 54; // 18 days before winter

        // ------------------------------------------------------------------ forecast model
        [Test]
        public void Forecast_WinterNeedFollowsSeasonalCycle()
        {
            var f = new FoodSecurityForecast();
            FoodSecurityForecaster.Project(f, AutumnDay54, 8f, 150f, 1f, 1f, 1f, 0, 20f, 0.05f, 0f, 20f);
            Assert.IsFalse(f.InWinter);
            Assert.AreEqual(18, f.DaysUntilWinter);
            Assert.AreEqual(14.4f, f.WinterNeed, 0.6f, "winter yields 0.4x -> 0.6 food/day short for 24 days");
            Assert.AreEqual(8f, f.StockAtWinter, 0.6f);
            Assert.That(f.WinterCoverage, Is.InRange(0.45f, 0.65f));
            Assert.That(f.StarveDay, Is.GreaterThan(18));
            FoodSecurityForecaster.Classify(f, 1.25f, FoodSecuritySituation.None, false);
            Assert.AreEqual(FoodSecuritySituation.WinterShortfall, f.Situation);
            Assert.That(f.Urgency, Is.InRange(0.4f, 1f));
            Assert.IsFalse(float.IsNaN(f.AnnualNetRatio) || float.IsNaN(f.WinterCoverage));
        }

        [Test]
        public void Forecast_GarbageInputsNeverProduceNaN()
        {
            var f = new FoodSecurityForecast();
            FoodSecurityForecaster.Project(f, -5, float.NaN, 0f, float.PositiveInfinity, float.NaN, float.NaN, -3, 10f, float.NaN, float.NaN, float.NaN);
            FoodSecurityForecaster.Classify(f, float.NaN, FoodSecuritySituation.Surplus, false);
            Assert.IsFalse(float.IsNaN(f.Urgency) || float.IsNaN(f.WinterCoverage) || float.IsNaN(f.AnnualNetRatio) || float.IsNaN(f.DaysOfFood));
            Assert.That(f.Urgency, Is.InRange(0f, 1f));
        }

        // ------------------------------------------------------------------ advisor behaviour
        [Test]
        public void PreWinterShortfall_ProducesStrongFoodBuildAndJobVotes()
        {
            var db = CreateDb();
            var s = Setup(db, AgeId.Bronze, 20, out var ctx, out var civ, true);
            SetFood(s, 8f, 1f, 1f);
            var adv = new FoodSecurityAdvisor();
            RunOnce(adv, ctx, AutumnDay54);

            Assert.AreEqual((float)FoodSecuritySituation.WinterShortfall, s.Mem(FoodSecurityAdvisor.KeySituation));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Farm), Is.GreaterThanOrEqualTo(PerRun(0.5f)), "strong farm vote");
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Farm), Is.GreaterThan(AdvisorBoard.BuildBias(s, BuildingType.GathererHut)));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.GathererHut), Is.LessThan(0f), "gatherer huts are obsolete once farms exist");
            Assert.That(AdvisorBoard.JobBias(s, JobType.Farmer), Is.GreaterThanOrEqualTo(PerRun(0.4f)), "food jobs voted up");
            Assert.That(AdvisorBoard.Urgency(s, "food"), Is.GreaterThan(0.4f));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.FishingHut), "no water here");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.HuntingLodge), "no woods here");
            Assert.IsTrue(LogContains(s, "[food]") && LogContains(s, "winter in 18 days; stock covers"), "explained to the player");
            Assert.IsTrue(LogContains(s, "obsolete: Farm"));
        }

        [Test]
        public void LargeSurplus_FreesFoodLaborAndVotesGranary()
        {
            var db = CreateDb();
            var s = Setup(db, AgeId.Bronze, 20, out var ctx, out var civ, true);
            SetFood(s, 145f, 2.5f, 1f);
            var adv = new FoodSecurityAdvisor();
            RunOnce(adv, ctx, SimTime.TicksPerYear + 10);

            Assert.AreEqual((float)FoodSecuritySituation.Surplus, s.Mem(FoodSecurityAdvisor.KeySituation));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Farmer), Is.LessThan(0f), "surplus frees farm labor");
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Granary), Is.GreaterThanOrEqualTo(PerRun(0.5f)), "spoiling surplus wants a granary");
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Farm), Is.LessThanOrEqualTo(0f));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Storehouse), "only the best storage is voted");
            Assert.IsTrue(LogContains(s, "spoiling"));
            Assert.That(AdvisorBoard.Urgency(s, "food"), Is.LessThan(0.2f));
        }

        [Test]
        public void UnavailableBuildings_AreNeverVoted()
        {
            var db = CreateDb();
            var s = Setup(db, AgeId.Dawn, 20, out var ctx, out var civ, false);
            SetFood(s, 1f, 0.2f, 1f); // crisis: maximum pressure to vote anything
            var adv = new FoodSecurityAdvisor();
            RunOnce(adv, ctx, SimTime.TicksPerYear + 30);

            Assert.AreEqual((float)FoodSecuritySituation.Crisis, s.Mem(FoodSecurityAdvisor.KeySituation));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.GathererHut), Is.GreaterThanOrEqualTo(PerRun(0.5f)));
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var t = (BuildingType)i;
                if (!db.IsBuildingAvailable(civ, t))
                    Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, t), "voted unavailable " + t);
            }
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.HydroponicsTower), "race-gated");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Farm));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Granary));
            Assert.That(AdvisorBoard.Urgency(s, "food"), Is.GreaterThanOrEqualTo(0.85f));
        }

        [Test]
        public void PoorSoilNearWater_PrefersFishingOverFarms()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++)
            {
                map.Fertility[i] = 0.1f;
                if (map.PosOf(i).X >= 36) { map.Biomes[i] = Biome.ShallowWater; map.Elevation[i] = -0.3f; }
            }
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 32), 20, out var civ);
            civ.Age = AgeId.Bronze;
            SetFood(s, 1f, 0.2f, 1f);
            var adv = new FoodSecurityAdvisor();
            RunOnce(adv, ctx, SimTime.TicksPerYear + 30);

            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.FishingHut), Is.GreaterThanOrEqualTo(PerRun(0.5f)));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.FishingHut), Is.GreaterThan(AdvisorBoard.BuildBias(s, BuildingType.Farm)));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.GathererHut), Is.GreaterThanOrEqualTo(0f), "farms on barren soil do not obsolete gatherers");
        }

        [Test]
        public void DeadSettlementsAndCivs_GetNoVotes()
        {
            var db = CreateDb();
            var s = Setup(db, AgeId.Bronze, 20, out var ctx, out var civ, false);
            SetFood(s, 1f, 0.2f, 1f);
            s.IsAlive = false;
            var s2 = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(12, 12), 10, out var civ2);
            SetFood(s2, 1f, 0.2f, 1f);
            civ2.IsAlive = false;
            var adv = new FoodSecurityAdvisor();
            RunOnce(adv, ctx, SimTime.TicksPerYear + 30);

            foreach (var kv in s.Memory) Assert.IsFalse(kv.Key.StartsWith("ai."), "dead settlement got " + kv.Key);
            foreach (var kv in s2.Memory) Assert.IsFalse(kv.Key.StartsWith("ai."), "dead civ got " + kv.Key);
        }

        [Test]
        public void EmptySettlement_ReportsZeroUrgencyAndNoVotes()
        {
            var db = CreateDb();
            var s = Setup(db, AgeId.Bronze, 0, out var ctx, out var civ, false);
            SetFood(s, 0f, 0f, 0f);
            var adv = new FoodSecurityAdvisor();
            RunOnce(adv, ctx, SimTime.TicksPerYear + 30);
            Assert.AreEqual(0f, AdvisorBoard.Urgency(s, "food"));
            foreach (var kv in s.Memory) Assert.IsFalse(kv.Key.StartsWith("ai.bias."), "voted " + kv.Key);
        }

        [Test]
        public void SameInputs_ProduceIdenticalVotes()
        {
            var a = RunScenario();
            var b = RunScenario();
            Assert.AreEqual(a.Memory.Count, b.Memory.Count);
            foreach (var kv in a.Memory)
            {
                Assert.IsTrue(b.Memory.TryGetValue(kv.Key, out float v), "missing " + kv.Key);
                Assert.AreEqual(kv.Value, v, 0f, kv.Key);
            }
            Assert.AreEqual(a.AiLog.Count, b.AiLog.Count);
            for (int i = 0; i < a.AiLog.Count; i++) Assert.AreEqual(a.AiLog[i], b.AiLog[i]);
        }

        private static Settlement RunScenario()
        {
            var db = CreateDb();
            var s = Setup(db, AgeId.Bronze, 20, out var ctx, out var civ, true);
            var adv = new FoodSecurityAdvisor();
            for (int k = 0; k < 16; k++)
            {
                // drifting economy across a full year, all seasons
                SetFood(s, 10f + 9f * k, 0.6f + 0.15f * k, 1f);
                RunOnce(adv, ctx, SimTime.TicksPerYear + 12L * (k + 1));
                var all = ctx.State.Settlements.Items;
                for (int i = 0; i < all.Count; i++) AdvisorBoard.Decay(all[i], AdvisorBoard.DecayPerCouncil);
            }
            return s;
        }
    }
}
