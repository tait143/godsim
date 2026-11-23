using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy.Production
{
    /// <summary>Hand-made building definitions and tiny worlds for production tests (independent of the real catalog).</summary>
    internal static class ProductionTestKit
    {
        public static readonly Vec2i Center = new Vec2i(32, 32);

        public static BuildingDefinition Farm() => new BuildingDefinition
        {
            Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, Job = JobType.Farmer, JobSlots = 4,
            OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, 0.22f) }, Placement = PlacementRule.Fertile,
        };

        public static BuildingDefinition Smelter() => new BuildingDefinition
        {
            Type = BuildingType.Smelter, Name = "Smelter", Category = BuildingCategory.Industry, Job = JobType.Smith, JobSlots = 2,
            InputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.06f) },
            OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.05f) }, Placement = PlacementRule.Isolated,
        };

        public static BuildingDefinition Mine() => new BuildingDefinition
        {
            Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource, Job = JobType.Miner, JobSlots = 3,
            OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.07f) }, Placement = PlacementRule.NearOre,
        };

        public static BuildingDefinition Woodcutter() => new BuildingDefinition
        {
            Type = BuildingType.WoodcutterCamp, Name = "Woodcutter", Category = BuildingCategory.Resource, Job = JobType.Woodcutter, JobSlots = 2,
            OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Wood, 0.12f) }, Placement = PlacementRule.NearForest | PlacementRule.Outskirts,
        };

        public static BuildingDefinition FishingHut() => new BuildingDefinition
        {
            Type = BuildingType.FishingHut, Name = "Fishing Hut", Category = BuildingCategory.Food, Job = JobType.Fisher, JobSlots = 2,
            OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, 0.15f) }, Placement = PlacementRule.NearWater,
        };

        public static BuildingDefinition HuntingLodge() => new BuildingDefinition
        {
            Type = BuildingType.HuntingLodge, Name = "Hunting Lodge", Category = BuildingCategory.Food, Job = JobType.Hunter, JobSlots = 2,
            OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, 0.12f) }, Placement = PlacementRule.NearForest,
        };

        public static BuildingDefinition PowerPlant() => new BuildingDefinition
        {
            Type = BuildingType.PowerPlant, Name = "Power Plant", Category = BuildingCategory.Infrastructure, Job = JobType.Engineer, JobSlots = 2,
            InputsPerWorker = new[] { new ResourceAmount(ResourceType.Fuel, 0.05f) },
            Services = new[] { new ServiceAmount(ServiceType.Power, 5f) },
        };

        public static GameDatabase CreateDb(bool freeze = true, params BuildingDefinition[] overrides)
        {
            var db = new GameDatabase();
            db.Register(Farm());
            db.Register(Smelter());
            db.Register(Mine());
            db.Register(Woodcutter());
            db.Register(FishingHut());
            db.Register(HuntingLodge());
            db.Register(PowerPlant());
            if (overrides != null) foreach (var d in overrides) db.Register(d);
            TestDb.FillMissing(db);
            if (freeze) db.Freeze();
            return db;
        }

        /// <summary>64x64 world with one settlement of N adults at the center and an empty stockpile.</summary>
        public static SimContext CreateWorld(GameDatabase db, int adults, out Settlement s, out Civilization civ, Biome fill = Biome.Grassland, ulong seed = 1)
        {
            var ctx = TestWorlds.CreateContext(db, 64, seed, fill);
            s = TestWorlds.AddSettlement(ctx, RaceId.Human, Center, adults, out civ);
            for (int i = 0; i < Counts.Resources; i++) s.Stockpile.SetRaw((ResourceType)i, 0f);
            return ctx;
        }

        public static Building Place(SimContext ctx, Settlement s, BuildingType type, Vec2i at, bool complete = true)
        {
            var b = ctx.Ops.CreateBuilding(s, type, at, 0, complete);
            Assert.IsNotNull(b, "could not place " + type + " at " + at);
            return b;
        }

        /// <summary>Assigns up to count unemployed adults (normalized to Skill 1, Experience 0) to the building.</summary>
        public static List<Person> Staff(SimContext ctx, Settlement s, Building b, JobType job, int count)
        {
            var staff = new List<Person>();
            foreach (int pid in s.ResidentIds)
            {
                if (staff.Count >= count) break;
                var p = ctx.State.People.Get(pid);
                if (p == null || !p.IsAlive || !p.IsAdult || p.Job != JobType.None) continue;
                p.Skill = 1f;
                p.Experience = 0f;
                Assert.IsTrue(ctx.Ops.AssignJob(p, b, job), "AssignJob failed");
                staff.Add(p);
            }
            Assert.AreEqual(count, staff.Count, "not enough adults to staff");
            return staff;
        }

        /// <summary>Runs the system exactly <paramref name="runs"/> times, advancing the clock by its interval each time.</summary>
        public static void Step(ISimSystem sys, SimContext ctx, int runs = 1)
        {
            for (int r = 0; r < runs; r++)
            {
                ctx.State.Tick += sys.TickInterval;
                ctx.DeltaTicks = sys.TickInterval;
                sys.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
        }

        public static void SetFlag(SimContext ctx, int x, int y, TileFlags flag, float resource)
        {
            var map = ctx.State.Map;
            int i = map.Index(x, y);
            map.Set(i, flag);
            map.Resource[i] = resource;
        }

        public static float SumResource(WorldMap map, TileFlags flag)
        {
            float sum = 0f;
            for (int i = 0; i < map.TileCount; i++) if (map.Has(i, flag)) sum += map.Resource[i];
            return sum;
        }

        public static int CountFlag(WorldMap map, TileFlags flag)
        {
            int c = 0;
            for (int i = 0; i < map.TileCount; i++) if (map.Has(i, flag)) c++;
            return c;
        }
    }
}
