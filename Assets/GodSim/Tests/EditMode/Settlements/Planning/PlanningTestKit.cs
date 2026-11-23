using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Settlements
{
    /// <summary>Helpers for the town-planning tests: a small real-ish database and map painting/inspection utilities.</summary>
    internal static class PlanningTestKit
    {
        public static GameDatabase CreateDb(Action<GameDatabase> extra = null)
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5, Desirability = 0.1f });
            db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, Width = 3, Height = 3, Placement = PlacementRule.Fertile, Job = JobType.Farmer, JobSlots = 3 });
            db.Register(new BuildingDefinition { Type = BuildingType.FishingHut, Name = "Fishing Hut", Category = BuildingCategory.Food, Placement = PlacementRule.NearWater, Job = JobType.Fisher, JobSlots = 2 });
            db.Register(new BuildingDefinition { Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource, Width = 2, Height = 2, Placement = PlacementRule.NearOre, Desirability = -0.4f, Job = JobType.Miner, JobSlots = 4 });
            db.Register(new BuildingDefinition { Type = BuildingType.Temple, Name = "Temple", Category = BuildingCategory.Religion, Width = 2, Height = 2, Placement = PlacementRule.Central, MinAge = AgeId.Iron, Desirability = 0.4f });
            db.Register(new BuildingDefinition { Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure });
            extra?.Invoke(db);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        public static BuildOrder Enqueue(Settlement s, BuildingType type, float priority = 1f, long requestedTick = 0)
        {
            var o = new BuildOrder { Type = type, Priority = priority, RequestedTick = requestedTick, Reason = "test" };
            s.BuildQueue.Add(o);
            return o;
        }

        public static List<Building> BuildingsOf(SimContext ctx, Settlement s, BuildingType type)
        {
            var list = new List<Building>();
            foreach (int id in s.BuildingIds)
            {
                var b = ctx.State.Buildings.Get(id);
                if (b != null && !b.IsDestroyed && b.Type == type) list.Add(b);
            }
            return list;
        }

        public static void CompleteAll(SimContext ctx, Settlement s)
        {
            foreach (int id in s.BuildingIds.ToArray())
            {
                var b = ctx.State.Buildings.Get(id);
                if (b != null && !b.IsComplete) ctx.Ops.CompleteBuilding(b);
            }
        }

        public static void PaintBiome(WorldMap map, int x0, int y0, int w, int h, Biome biome)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    if (map.InBounds(x, y)) map.Biomes[map.Index(x, y)] = biome;
        }

        public static void PaintFlag(WorldMap map, int x0, int y0, int w, int h, TileFlags flag)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    if (map.InBounds(x, y)) map.Set(map.Index(x, y), flag);
        }

        /// <summary>Chebyshev distance from the footprint to the nearest tile matching the predicate (int.MaxValue if none within 8).</summary>
        public static int DistanceTo(WorldMap map, Building b, Func<int, bool> match)
        {
            int best = int.MaxValue;
            for (int y = b.Origin.Y - 8; y < b.Origin.Y + b.Height + 8; y++)
                for (int x = b.Origin.X - 8; x < b.Origin.X + b.Width + 8; x++)
                {
                    if (!map.InBounds(x, y) || !match(map.Index(x, y))) continue;
                    int dx = x < b.Origin.X ? b.Origin.X - x : (x >= b.Origin.X + b.Width ? x - (b.Origin.X + b.Width - 1) : 0);
                    int dy = y < b.Origin.Y ? b.Origin.Y - y : (y >= b.Origin.Y + b.Height ? y - (b.Origin.Y + b.Height - 1) : 0);
                    best = Math.Min(best, Math.Max(dx, dy));
                }
            return best;
        }

        public static bool HasAdjacentRoad(WorldMap map, Building b)
        {
            foreach (var p in OrthogonalRing(b))
                if (map.InBounds(p) && map.Has(p, TileFlags.Road)) return true;
            return false;
        }

        /// <summary>BFS over Road tiles: true if a road touching <paramref name="a"/> reaches a road touching <paramref name="b"/>.</summary>
        public static bool RoadConnects(WorldMap map, Building a, Building b)
        {
            var goals = new HashSet<int>();
            foreach (var p in OrthogonalRing(b)) if (map.InBounds(p) && map.Has(p, TileFlags.Road)) goals.Add(map.Index(p));
            if (goals.Count == 0) return false;
            var seen = new bool[map.TileCount];
            var open = new Queue<int>();
            foreach (var p in OrthogonalRing(a))
            {
                if (!map.InBounds(p) || !map.Has(p, TileFlags.Road)) continue;
                int i = map.Index(p);
                if (!seen[i]) { seen[i] = true; open.Enqueue(i); }
            }
            while (open.Count > 0)
            {
                int cur = open.Dequeue();
                if (goals.Contains(cur)) return true;
                var c = map.PosOf(cur);
                foreach (var d in Vec2i.Dirs4)
                {
                    var n = c + d;
                    if (!map.InBounds(n)) continue;
                    int ni = map.Index(n);
                    if (seen[ni] || !map.Has(ni, TileFlags.Road)) continue;
                    seen[ni] = true;
                    open.Enqueue(ni);
                }
            }
            return false;
        }

        public static IEnumerable<Vec2i> OrthogonalRing(Building b)
        {
            for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++) { yield return new Vec2i(x, b.Origin.Y - 1); yield return new Vec2i(x, b.Origin.Y + b.Height); }
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++) { yield return new Vec2i(b.Origin.X - 1, y); yield return new Vec2i(b.Origin.X + b.Width, y); }
        }

        public static int CountFlag(WorldMap map, TileFlags flag)
        {
            int n = 0;
            for (int i = 0; i < map.TileCount; i++) if (map.Has(i, flag)) n++;
            return n;
        }

        /// <summary>Number of 4-connected components formed by Road tiles.</summary>
        public static int CountRoadComponents(WorldMap map)
        {
            var seen = new bool[map.TileCount];
            var open = new Queue<int>();
            int components = 0;
            for (int i = 0; i < map.TileCount; i++)
            {
                if (seen[i] || !map.Has(i, TileFlags.Road)) continue;
                components++;
                seen[i] = true;
                open.Enqueue(i);
                while (open.Count > 0)
                {
                    var c = map.PosOf(open.Dequeue());
                    foreach (var d in Vec2i.Dirs4)
                    {
                        var n = c + d;
                        if (!map.InBounds(n)) continue;
                        int ni = map.Index(n);
                        if (seen[ni] || !map.Has(ni, TileFlags.Road)) continue;
                        seen[ni] = true;
                        open.Enqueue(ni);
                    }
                }
            }
            return components;
        }

        /// <summary>Asserts every non-Town-Center building of the settlement is linked by road to its Town Center.</summary>
        public static int AssertAllRoadConnected(SimContext ctx, Settlement s)
        {
            var map = ctx.State.Map;
            var tc = ctx.State.Buildings.Get(s.TownCenterId);
            NUnit.Framework.Assert.IsNotNull(tc, "town center");
            int checkedCount = 0;
            foreach (int id in s.BuildingIds)
            {
                var b = ctx.State.Buildings.Get(id);
                if (b == null || b.IsDestroyed || b.Id == tc.Id) continue;
                checkedCount++;
                NUnit.Framework.Assert.IsTrue(RoadConnects(map, b, tc), b.Type + " at " + b.Origin + " has no road link to the town center");
            }
            return checkedCount;
        }

        /// <summary>Paints a one-tile ring of the biome at Chebyshev radius r around c.</summary>
        public static void PaintRing(WorldMap map, Vec2i c, int r, Biome biome)
        {
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                    if (Math.Max(Math.Abs(x - c.X), Math.Abs(y - c.Y)) == r && map.InBounds(x, y)) map.Biomes[map.Index(x, y)] = biome;
        }
    }
}
