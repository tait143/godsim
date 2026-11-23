using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class SeasonalAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static ResourceAmount[] Res(ResourceType t, float a) => new[] { new ResourceAmount(t, a) };

        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, Shape = BuildingShape.Field, MinAge = AgeId.Bronze,
                Job = JobType.Farmer, JobSlots = 4, OutputsPerWorker = Res(ResourceType.Food, 0.4f), Placement = PlacementRule.Fertile,
                Cost = Res(ResourceType.Wood, 15f), BuildWork = 20f, Width = 2, Height = 2,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.HuntingLodge, Name = "Hunting Lodge", Category = BuildingCategory.Food, Shape = BuildingShape.Hut,
                Job = JobType.Hunter, JobSlots = 2, OutputsPerWorker = Res(ResourceType.Food, 0.3f), Cost = Res(ResourceType.Wood, 10f), BuildWork = 14f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.WoodcutterCamp, Name = "Woodcutter Camp", Category = BuildingCategory.Resource, Shape = BuildingShape.Hut,
                Job = JobType.Woodcutter, JobSlots = 3, OutputsPerWorker = Res(ResourceType.Wood, 0.5f), Placement = PlacementRule.NearForest,
                Cost = Res(ResourceType.Wood, 8f), BuildWork = 12f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage, Shape = BuildingShape.Hall,
                StorageCapacity = 120f, Cost = new[] { new ResourceAmount(ResourceType.Wood, 20f), new ResourceAmount(ResourceType.Stone, 6f) },
                BuildWork = 24f, Width = 2, Height = 1,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Granary, Name = "Granary", Category = BuildingCategory.Storage, Shape = BuildingShape.Dome, MinAge = AgeId.Bronze,
                StorageCapacity = 150f, MaxPerSettlement = 3, Cost = new[] { new ResourceAmount(ResourceType.Wood, 25f), new ResourceAmount(ResourceType.Stone, 20f) },
                BuildWork = 35f, Width = 2, Height = 2,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure, Shape = BuildingShape.Monument,
                Services = new[] { new ServiceAmount(ServiceType.Water, 3f) }, Cost = Res(ResourceType.Stone, 8f), BuildWork = 12f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Workshop, Name = "Workshop", Category = BuildingCategory.Industry, Shape = BuildingShape.Workshop,
                Job = JobType.Craftsman, JobSlots = 2, OutputsPerWorker = Res(ResourceType.Goods, 0.2f), BuildWork = 20f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Scriptorium, Name = "Scriptorium", Category = BuildingCategory.Education, Shape = BuildingShape.Hall,
                Job = JobType.Scholar, JobSlots = 2, Services = new[] { new ServiceAmount(ServiceType.Research, 1f) }, BuildWork = 20f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Shrine, Name = "Shrine", Category = BuildingCategory.Religion, Shape = BuildingShape.Monument,
                Job = JobType.Priest, JobSlots = 1, Services = new[] { new ServiceAmount(ServiceType.Faith, 1f) }, BuildWork = 15f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Temple, Name = "Temple", Category = BuildingCategory.Religion, Shape = BuildingShape.Hall, MinAge = AgeId.Bronze,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 80f), new ResourceAmount(ResourceType.Stone, 60f) },
                BuildWork = 120f, Width = 3, Height = 3,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Harbor, Name = "Harbor", Category = BuildingCategory.Commerce, Shape = BuildingShape.Dock,
                Placement = PlacementRule.Coastal, Cost = Res(ResourceType.Wood, 50f), BuildWork = 60f, Width = 2, Height = 2,
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement Setup(GameDatabase db, Biome biome, Season season, int adults, AgeId age, out SimContext ctx, out Civilization civ)
        {
            ctx = TestWorlds.CreateContext(db, 64, 7, biome);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), adults, out civ);
            civ.Age = age;
            ctx.State.Weather.Season = season;
            return s;
        }

        private static Building Place(SimContext ctx, Settlement s, BuildingType t)
        {
            var def = ctx.Db.Building(t);
            var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 20);
            Assert.IsTrue(site.IsValid, "no site for " + t);
            var b = ctx.Ops.CreateBuilding(s, t, site, 0, true);
            Assert.IsNotNull(b);
            return b;
        }

        private static void FlagRing(SimContext ctx, Vec2i c, int radius, TileFlags flag)
        {
            var map = ctx.State.Map;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != radius) continue;
                    int x = c.X + dx, y = c.Y + dy;
                    if (map.InBounds(x, y)) map.Set(map.Index(x, y), flag);
                }
        }

        /// <summary>Runs the advisor for one full council cycle (24 ticks = 4 runs; votes are cycle-scaled by the base class).</summary>
        private static SeasonalAdvisor RunOnce(SimContext ctx)
        {
            var adv = new SeasonalAdvisor();
            TestWorlds.Run(adv, ctx, 24);
            return adv;
        }

        private static float Storage(Settlement s) =>
            AdvisorBoard.BuildBias(s, BuildingType.Granary) + AdvisorBoard.BuildBias(s, BuildingType.Storehouse);

        // ------------------------------------------------------------------ identity
        [Test]
        public void Identity_IdAndInterval()
        {
            var adv = new SeasonalAdvisor();
            Assert.AreEqual("seasons", adv.AdvisorId);
            Assert.AreEqual(6, adv.TickInterval);
            Assert.AreEqual("AI.Advisor.seasons", adv.Name);
        }

        // ------------------------------------------------------------------ spring
        [Test]
        public void Spring_PlantsFieldsAndUsesFreeLaborForConstruction()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Grassland, Season.Spring, 30, AgeId.Bronze, out var ctx, out _);
            Place(ctx, s, BuildingType.Farm);
            RunOnce(ctx);
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Farmer), 0.5f, "spring sowing staffs farms");
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Farm), 0f, "spring votes new fields");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Builder), 0f, "spring labor goes to construction");
        }

        // ------------------------------------------------------------------ summer
        [Test]
        public void Summer_IsBuildingSeasonAndRaisesStorageBeforeAutumn()
        {
            var db = MakeDb();
            var spring = Setup(db, Biome.Grassland, Season.Spring, 30, AgeId.Bronze, out var c1, out _);
            RunOnce(c1);
            var summer = Setup(db, Biome.Grassland, Season.Summer, 30, AgeId.Bronze, out var c2, out _);
            summer.Stockpile.Capacity = 40f;
            RunOnce(c2);
            Assert.Greater(AdvisorBoard.JobBias(summer, JobType.Builder), AdvisorBoard.JobBias(spring, JobType.Builder), "summer is the building season");
            Assert.Greater(Storage(summer), 0f, "storage is raised before the autumn harvest when capacity is short");
        }

        // ------------------------------------------------------------------ autumn
        [Test]
        public void Autumn_MaxesFoodJobsAndVotesStorageOnlyWhenCapacityIsShort()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Grassland, Season.Autumn, 30, AgeId.Bronze, out var ctx, out _);
            Place(ctx, s, BuildingType.Farm);
            Place(ctx, s, BuildingType.HuntingLodge);
            s.Stockpile.Capacity = 40f;
            RunOnce(ctx);
            Assert.GreaterOrEqual(AdvisorBoard.JobBias(s, JobType.Farmer), 0.99f, "harvest: farmers maxed");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Hunter), 0.5f, "harvest: all food jobs up");
            Assert.Greater(Storage(s), 0f, "capacity < projected winter need -> storage");

            var roomy = Setup(db, Biome.Grassland, Season.Autumn, 30, AgeId.Bronze, out var ctx2, out _);
            roomy.Stockpile.Capacity = 1000f;
            RunOnce(ctx2);
            Assert.AreEqual(0f, Storage(roomy), "ample storage -> no storage votes");
        }

        [Test]
        public void ColdAutumn_StockpilesFirewoodAndStorage_WarmAutumnDoesNot()
        {
            var db = MakeDb();
            var cold = Setup(db, Biome.Tundra, Season.Autumn, 30, AgeId.Bronze, out var ctx, out _);
            Place(ctx, cold, BuildingType.WoodcutterCamp);
            FlagRing(ctx, cold.Center, 5, TileFlags.Trees);
            cold.Stockpile.SetRaw(ResourceType.Wood, 5f);
            cold.Stockpile.Capacity = 40f;
            var adv = RunOnce(ctx);
            Assert.GreaterOrEqual(cold.Mem(SeasonalAdvisor.ColdKey), SeasonalAdvisor.ColdThreshold, "tundra is a cold climate");
            Assert.Greater(AdvisorBoard.BuildBias(cold, BuildingType.WoodcutterCamp), 0f, "cold autumn: woodcutter camp");
            Assert.Greater(AdvisorBoard.JobBias(cold, JobType.Woodcutter), 0.3f, "cold autumn: woodcutters staffed");
            Assert.Greater(Storage(cold), 0f, "cold autumn: storage");
            Assert.Greater(AdvisorBoard.Urgency(cold, SeasonalAdvisor.Id), 0.2f, "firewood gap is urgent");
            Assert.Greater(adv.LastOutlook.WoodGap, 0f);

            var warm = Setup(db, Biome.Grassland, Season.Autumn, 30, AgeId.Bronze, out var ctx2, out _);
            Place(ctx2, warm, BuildingType.WoodcutterCamp);
            FlagRing(ctx2, warm.Center, 5, TileFlags.Trees);
            warm.Stockpile.SetRaw(ResourceType.Wood, 5f);
            RunOnce(ctx2);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(warm, BuildingType.WoodcutterCamp), "temperate towns do not hoard firewood");
            Assert.AreEqual(0f, AdvisorBoard.JobBias(warm, JobType.Woodcutter));
        }

        [Test]
        public void ColdAutumn_WithoutForest_DoesNotVoteUnplaceableWoodcutter()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Tundra, Season.Autumn, 30, AgeId.Bronze, out var ctx, out _);
            s.Stockpile.SetRaw(ResourceType.Wood, 0f);
            RunOnce(ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.WoodcutterCamp), "NearForest requirement unmet in territory");
        }

        // ------------------------------------------------------------------ winter
        [Test]
        public void Winter_ShiftsToIndoorWorkAndHoldsLargeProjects()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Grassland, Season.Winter, 30, AgeId.Bronze, out var ctx, out _);
            Place(ctx, s, BuildingType.Farm);
            Place(ctx, s, BuildingType.Workshop);
            Place(ctx, s, BuildingType.Scriptorium);
            Place(ctx, s, BuildingType.Shrine);
            s.Stockpile.SetRaw(ResourceType.Wood, 5f);
            s.Stockpile.SetRaw(ResourceType.Stone, 5f);
            RunOnce(ctx);
            Assert.Less(AdvisorBoard.JobBias(s, JobType.Farmer), 0f, "frozen fields");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Craftsman), 0f, "winter crafts");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Scholar), 0f, "winter study");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Priest), 0f, "winter faith");
            Assert.Less(AdvisorBoard.JobBias(s, JobType.Builder), 0f);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Temple), 0f, "no big project without materials in winter");
            Assert.GreaterOrEqual(AdvisorBoard.BuildBias(s, BuildingType.Storehouse), 0f, "essential storage never discouraged");
            Assert.GreaterOrEqual(AdvisorBoard.BuildBias(s, BuildingType.House), 0f, "housing never discouraged");
        }

        [Test]
        public void Winter_AllowsLargeProjectWhenMaterialsAreInStock()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Grassland, Season.Winter, 30, AgeId.Bronze, out var ctx, out _);
            s.Stockpile.Capacity = 1000f;
            s.Stockpile.SetRaw(ResourceType.Wood, 500f);
            s.Stockpile.SetRaw(ResourceType.Stone, 500f);
            RunOnce(ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Temple));
        }

        [Test]
        public void Winter_IsHarsherInColdClimates()
        {
            var db = MakeDb();
            var tundra = Setup(db, Biome.Tundra, Season.Winter, 30, AgeId.Bronze, out var c1, out _);
            Place(c1, tundra, BuildingType.Farm);
            RunOnce(c1);
            var grass = Setup(db, Biome.Grassland, Season.Winter, 30, AgeId.Bronze, out var c2, out _);
            Place(c2, grass, BuildingType.Farm);
            RunOnce(c2);
            Assert.Less(AdvisorBoard.JobBias(tundra, JobType.Farmer), AdvisorBoard.JobBias(grass, JobType.Farmer));
            Assert.Less(AdvisorBoard.BuildBias(tundra, BuildingType.Temple), AdvisorBoard.BuildBias(grass, BuildingType.Temple));
        }

        // ------------------------------------------------------------------ weather
        [Test]
        public void Drought_VotesWaterAndDroughtProofFood()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Grassland, Season.Summer, 30, AgeId.Bronze, out var ctx, out _);
            Place(ctx, s, BuildingType.HuntingLodge);
            Place(ctx, s, BuildingType.Farm);
            ctx.State.Weather.DroughtTicksRemaining = 30;
            RunOnce(ctx);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Well), 0.5f, "drought: water");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Hunter), 0f, "drought: food jobs");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Farmer), 0f, "drought: tend surviving crops");
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.HuntingLodge), 0f, "drought-proof food");
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Farm), 0f, "no new fields in a drought");
            bool explained = false;
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains("drought")) explained = true;
            Assert.IsTrue(explained, "strong drought votes are explained in the AI log");
        }

        [Test]
        public void Storm_DiscouragesExposedCoastalConstruction()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Grassland, Season.Autumn, 30, AgeId.Bronze, out var ctx, out _);
            FlagRing(ctx, s.Center, 4, TileFlags.Coastal);
            ctx.State.Weather.StormIntensity = 0.8f;
            RunOnce(ctx);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Harbor), 0f);

            var inland = Setup(db, Biome.Grassland, Season.Autumn, 30, AgeId.Bronze, out var ctx2, out _);
            ctx2.State.Weather.StormIntensity = 0.8f;
            RunOnce(ctx2);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(inland, BuildingType.Harbor), "inland towns have no shore to protect");
        }

        // ------------------------------------------------------------------ gating & robustness
        [Test]
        public void NeverVotesForUnavailableBuildings()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Grassland, Season.Autumn, 30, AgeId.Dawn, out var ctx, out _);
            s.Stockpile.Capacity = 40f;
            RunOnce(ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Granary), "Granary is Bronze age");
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Storehouse), 0f);

            var sp = Setup(db, Biome.Grassland, Season.Spring, 30, AgeId.Dawn, out var ctx2, out _);
            RunOnce(ctx2);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(sp, BuildingType.Farm), "Farm is Bronze age");
        }

        [Test]
        public void SurvivesInsaneWeatherWithoutNaN()
        {
            var db = MakeDb();
            var s = Setup(db, Biome.Snow, Season.Winter, 12, AgeId.Bronze, out var ctx, out _);
            var w = ctx.State.Weather;
            w.FoodYieldMultiplier = float.NaN;
            w.StormIntensity = float.PositiveInfinity;
            w.Rainfall = float.NaN;
            w.TemperatureOffset = float.NegativeInfinity;
            w.DroughtTicksRemaining = int.MaxValue;
            w.RainTicksRemaining = -5;
            w.Season = (Season)42;
            Assert.DoesNotThrow(() => TestWorlds.Run(new SeasonalAdvisor(), ctx, 60));
            foreach (var kv in s.Memory)
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        [Test]
        public void Deterministic_AcrossAFullYearCycle()
        {
            var db = MakeDb();
            var a = Snapshot(db);
            var b = Snapshot(db);
            Assert.AreEqual(a.Count, b.Count);
            Assert.Greater(a.Count, 4);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
        }

        private static List<string> Snapshot(GameDatabase db)
        {
            var s = Setup(db, Biome.Taiga, Season.Spring, 25, AgeId.Bronze, out var ctx, out _);
            Place(ctx, s, BuildingType.Farm);
            Place(ctx, s, BuildingType.WoodcutterCamp);
            Place(ctx, s, BuildingType.Workshop);
            FlagRing(ctx, s.Center, 5, TileFlags.Trees);
            var adv = new SeasonalAdvisor();
            var council = new AdvisorCouncilSystem();
            adv.Initialize(ctx);
            var w = ctx.State.Weather;
            for (int t = 1; t <= SimTime.TicksPerYear * 2; t++)
            {
                ctx.State.Tick = t;
                w.Season = SimTime.SeasonOf(t);
                w.DroughtTicksRemaining = t > 30 && t < 60 ? 60 - t : 0;
                w.StormIntensity = t > 140 && t < 160 ? 0.7f : 0f;
                w.FoodYieldMultiplier = w.Season == Season.Winter ? 0.4f : 1.1f;
                if (t % 6 != 0) continue;
                ctx.DeltaTicks = 24;
                if (t % 24 == 0) council.Tick(ctx);
                ctx.DeltaTicks = 6;
                adv.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var lines = new List<string>();
            for (int i = 0; i < keys.Count; i++)
            {
                float v = s.Memory[keys[i]];
                Assert.IsFalse(float.IsNaN(v));
                lines.Add(keys[i] + "=" + v.ToString("R"));
            }
            for (int i = 0; i < s.AiLog.Count; i++) lines.Add(s.AiLog[i]);
            float u = AdvisorBoard.Urgency(s, SeasonalAdvisor.Id);
            Assert.IsTrue(u >= 0f && u <= 1f);
            return lines;
        }
    }
}
