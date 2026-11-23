using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.AI.Governor
{
    /// <summary>Realistic minimal building definitions and tiny world helpers for governor tests.</summary>
    internal static class GovernorTestKit
    {
        public static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);
        public static ServiceAmount S(ServiceType t, float a) => new ServiceAmount(t, a);

        /// <summary>Creates a database with the governor test buildings (plus stubs for everything else).</summary>
        public static GameDatabase CreateDb(System.Action<GameDatabase> extra = null, bool freeze = true)
        {
            var db = new GameDatabase();
            foreach (var d in CreateDefinitions()) db.Register(d);
            extra?.Invoke(db);
            TestDb.FillMissing(db);
            if (freeze) db.Freeze();
            return db;
        }

        public static List<BuildingDefinition> CreateDefinitions()
        {
            return new List<BuildingDefinition>
            {
                new BuildingDefinition
                {
                    Type = BuildingType.TownCenter, Name = "Town Center", Category = BuildingCategory.Civic, MaxPerSettlement = 1,
                    Width = 2, Height = 2, Cost = new[] { R(ResourceType.Wood, 40f), R(ResourceType.Stone, 20f) }, BuildWork = 60f,
                    Services = new[] { S(ServiceType.Administration, 2f), S(ServiceType.Defense, 1f) }, StorageCapacity = 100f,
                    Placement = PlacementRule.Central,
                },
                new BuildingDefinition
                {
                    Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5,
                    Cost = new[] { R(ResourceType.Wood, 10f) }, BuildWork = 18f,
                },
                new BuildingDefinition
                {
                    Type = BuildingType.GathererHut, Name = "Gatherer's Hut", Category = BuildingCategory.Food, MaxAge = AgeId.Iron,
                    Job = JobType.Gatherer, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Food, 0.09f) },
                    Cost = new[] { R(ResourceType.Wood, 8f) }, Placement = PlacementRule.Outskirts,
                },
                new BuildingDefinition
                {
                    Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, Width = 2, Height = 2,
                    Job = JobType.Farmer, JobSlots = 4, OutputsPerWorker = new[] { R(ResourceType.Food, 0.2f) },
                    Cost = new[] { R(ResourceType.Wood, 15f) }, Placement = PlacementRule.Fertile | PlacementRule.Outskirts,
                },
                new BuildingDefinition
                {
                    Type = BuildingType.WoodcutterCamp, Name = "Woodcutter's Camp", Category = BuildingCategory.Resource,
                    Job = JobType.Woodcutter, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Wood, 0.15f) },
                    Cost = new[] { R(ResourceType.Wood, 6f) }, Placement = PlacementRule.NearForest,
                },
                new BuildingDefinition
                {
                    Type = BuildingType.StoneQuarry, Name = "Stone Quarry", Category = BuildingCategory.Resource,
                    Job = JobType.Quarrier, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Stone, 0.12f) },
                    Cost = new[] { R(ResourceType.Wood, 10f) }, Placement = PlacementRule.NearStone | PlacementRule.Outskirts,
                },
                new BuildingDefinition
                {
                    Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage, Width = 2,
                    Cost = new[] { R(ResourceType.Wood, 15f), R(ResourceType.Stone, 4f) }, StorageCapacity = 120f,
                },
                new BuildingDefinition
                {
                    Type = BuildingType.Shrine, Name = "Shrine", Category = BuildingCategory.Religion,
                    Job = JobType.Priest, JobSlots = 1, Services = new[] { S(ServiceType.Faith, 2f) },
                    Cost = new[] { R(ResourceType.Wood, 10f), R(ResourceType.Stone, 6f) },
                },
                new BuildingDefinition
                {
                    Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure, MaxPerSettlement = 1,
                    Services = new[] { S(ServiceType.Water, 3f) }, Cost = new[] { R(ResourceType.Stone, 6f), R(ResourceType.Wood, 4f) },
                },
                new BuildingDefinition
                {
                    Type = BuildingType.Barracks, Name = "Barracks", Category = BuildingCategory.Military, Width = 2,
                    Job = JobType.Soldier, JobSlots = 4, Services = new[] { S(ServiceType.Defense, 2f) },
                    Cost = new[] { R(ResourceType.Wood, 20f), R(ResourceType.Stone, 5f) },
                },
                // age-gated: Iron age temple (civ starts in Dawn)
                new BuildingDefinition
                {
                    Type = BuildingType.Temple, Name = "Temple", Category = BuildingCategory.Religion, MinAge = AgeId.Iron,
                    Services = new[] { S(ServiceType.Faith, 8f) }, Cost = new[] { R(ResourceType.Wood, 5f) },
                },
                // race-restricted: elves only
                new BuildingDefinition
                {
                    Type = BuildingType.Herbalist, Name = "Herbalist", Category = BuildingCategory.Health, AllowedRaces = RaceMask.Elf,
                    Services = new[] { S(ServiceType.Health, 4f) }, Cost = new[] { R(ResourceType.Wood, 5f) },
                },
                // wonder (cheap and very useful: would win if the governor were allowed to order it)
                new BuildingDefinition
                {
                    Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, IsWonder = true,
                    UniquePerCivilization = true, Services = new[] { S(ServiceType.Research, 20f), S(ServiceType.Health, 5f) },
                    Cost = new[] { R(ResourceType.Wood, 2f) },
                },
            };
        }

        /// <summary>Ore -> Metal consumer and an ore mine (for production-chain tests).</summary>
        public static void AddChainBuildings(GameDatabase db)
        {
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Smelter, Name = "Smelter", Category = BuildingCategory.Industry,
                Job = JobType.Smith, JobSlots = 2, InputsPerWorker = new[] { R(ResourceType.Ore, 0.1f) },
                OutputsPerWorker = new[] { R(ResourceType.Metal, 0.05f) }, Cost = new[] { R(ResourceType.Wood, 10f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource,
                Job = JobType.Miner, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Ore, 0.1f) },
                Cost = new[] { R(ResourceType.Wood, 10f) }, Placement = PlacementRule.NearOre,
            });
        }

        public static readonly Vec2i Center = new Vec2i(32, 32);

        /// <summary>World + one Human settlement with N adults.</summary>
        public static SimContext CreateWorld(GameDatabase db, int adults, out Settlement s, out Civilization civ, ulong seed = 7, RaceId race = RaceId.Human)
        {
            var ctx = TestWorlds.CreateContext(db, 64, seed);
            s = TestWorlds.AddSettlement(ctx, race, Center, adults, out civ);
            return ctx;
        }

        /// <summary>Places a complete building near the center; dwellings get the given capacity.</summary>
        public static Building Place(SimContext ctx, Settlement s, BuildingType type, int housing = -1)
        {
            var def = ctx.Db.Building(type);
            var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 20);
            var b = ctx.Ops.CreateBuilding(s, type, site, 0, true);
            if (b != null && housing >= 0) b.HousingCapacity = housing;
            return b;
        }

        /// <summary>Marks tiles in a square around a point with a flag.</summary>
        public static void Flag(SimContext ctx, Vec2i at, int radius, TileFlags flag)
        {
            var map = ctx.State.Map;
            for (int y = at.Y - radius; y <= at.Y + radius; y++)
                for (int x = at.X - radius; x <= at.X + radius; x++)
                    if (map.InBounds(x, y)) map.Set(map.Index(x, y), flag);
        }

        /// <summary>Sets all service coverages to a value.</summary>
        public static void SetCoverage(Settlement s, float v)
        {
            for (int i = 0; i < s.ServiceCoverage.Length; i++) s.ServiceCoverage[i] = v;
        }

        public static bool QueueHas(Settlement s, BuildingType t)
        {
            foreach (var o in s.BuildQueue) if (o.Type == t) return true;
            return false;
        }

        public static int QueueCount(Settlement s, BuildingType t)
        {
            int n = 0;
            foreach (var o in s.BuildQueue) if (o.Type == t) n++;
            return n;
        }

        public static string Signature(SimContext ctx)
        {
            var sb = new StringBuilder();
            foreach (var s in ctx.State.Settlements.Items)
            {
                sb.Append(s.Id).Append(':');
                foreach (var o in s.BuildQueue) sb.Append(o.Type).Append('/').Append(o.Priority.ToString("R")).Append('/').Append(o.RequestedTick).Append(';');
                foreach (var l in s.AiLog) sb.Append(l).Append('|');
                foreach (var j in s.JobPriority) sb.Append(j.ToString("R")).Append(',');
            }
            foreach (var b in ctx.State.Buildings.Items) sb.Append(b.Id).Append(b.Type).Append(b.Origin).Append(b.HousingCapacity).Append(';');
            return sb.ToString();
        }
    }

    /// <summary>Test stand-in for the town planner + construction: places the highest-priority order instantly.</summary>
    internal sealed class InstantPlannerSystem : ISimSystem
    {
        public readonly List<BuildingType> Built = new List<BuildingType>();
        public string Name => "Test.InstantPlanner";
        public int TickInterval => 1;
        public void Initialize(SimContext ctx) { }

        public void Tick(SimContext ctx)
        {
            foreach (var s in ctx.State.Settlements.Items)
            {
                if (!s.IsAlive || s.BuildQueue.Count == 0) continue;
                int best = 0;
                for (int i = 1; i < s.BuildQueue.Count; i++) if (s.BuildQueue[i].Priority > s.BuildQueue[best].Priority) best = i;
                var o = s.BuildQueue[best];
                var def = ctx.Db.Building(o.Type);
                var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 24);
                if (!site.IsValid) { o.Attempts++; if (o.Attempts > 3) s.BuildQueue.RemoveAt(best); continue; }
                var b = ctx.Ops.CreateBuilding(s, o.Type, site, 0, true);
                if (b != null && o.Type == BuildingType.TownCenter) s.TownCenterId = b.Id;
                s.BuildQueue.RemoveAt(best);
                Built.Add(o.Type);
            }
        }
    }

    /// <summary>Test stand-in for population + economy: grows the town, computes homelessness and service coverage, keeps stocks steady.</summary>
    internal sealed class FakeTownSystem : ISimSystem
    {
        public int GrowEveryTicks = 8;
        public int MaxPopulation = 1000;
        public float FoodDaysKept = 60f;
        public string Name => "Test.FakeTown";
        public int TickInterval => 1;
        public void Initialize(SimContext ctx) { }

        public void Tick(SimContext ctx)
        {
            foreach (var s in ctx.State.Settlements.Items)
            {
                if (!s.IsAlive) continue;
                if (GrowEveryTicks > 0 && ctx.Tick % GrowEveryTicks == 0 && s.Population < MaxPopulation)
                    ctx.Ops.CreatePerson(s, s.Race, (ctx.Tick / GrowEveryTicks) % 2 == 0 ? Sex.Female : Sex.Male, 18f);
                int cap = 0;
                var points = new float[Counts.Services];
                foreach (int id in s.BuildingIds)
                {
                    var b = ctx.State.Buildings.Get(id);
                    if (b == null || b.IsDestroyed || !b.IsComplete) continue;
                    cap += b.HousingCapacity;
                    var def = ctx.Db.Building(b.Type);
                    foreach (var sv in def.Services) points[(int)sv.Type] += sv.Amount;
                }
                int pop = s.Population;
                s.Homeless = System.Math.Max(0, pop - cap);
                for (int i = 0; i < Counts.Services; i++) s.ServiceCoverage[i] = pop > 0 ? points[i] * 8f / pop : 0f;
                s.Stockpile.Capacity = 1000f;
                s.Stockpile.SetRaw(ResourceType.Food, System.Math.Min(900f, pop * 0.05f * FoodDaysKept));
                s.Stockpile.SetRaw(ResourceType.Wood, 400f);
                s.Stockpile.SetRaw(ResourceType.Stone, 300f);
            }
        }
    }
}
