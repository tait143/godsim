using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// Terrain sculpting shared by Raise/Lower Land and Meteor: smooth radial elevation changes, biome
    /// reclassification by simple elevation rules, coastal flag refresh, and the consequences for whatever
    /// stood on land that became water (buildings destroyed, people moved, drowned settlements abandoned).
    /// All work is bounded by the radius (+1 ring for coastal flags).
    /// </summary>
    internal static class GodPowerTerrain
    {
        // Upward thresholds (spec) and downward thresholds (hysteresis so world-gen biomes do not flicker).
        public const float HillsUp = 0.45f, MountainUp = 0.62f, SnowUp = 0.8f;
        public const float HillsDown = 0.35f, MountainDown = 0.55f, SnowDown = 0.75f;
        /// <summary>Water deeper than this below sea level is DeepOcean.</summary>
        public const float DeepOffset = 0.25f;

        public const TileFlags DepositFlags = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit | TileFlags.CoalDeposit | TileFlags.OilDeposit;

        /// <summary>Summary of one sculpting pass.</summary>
        public struct ShapeResult
        {
            public int TilesChanged;
            public int NewLand;
            public int NewWater;
            public int BuildingsDestroyed;
            public int SettlementsLost;
        }

        /// <summary>
        /// Adds <paramref name="amount"/> to elevation within <paramref name="radius"/> of c with smooth falloff
        /// (full at the center, ~0 at the rim), clamped to [-1, 1], then reclassifies every changed tile.
        /// Land that sinks below sea level becomes <paramref name="newWaterBiome"/> (or DeepOcean when deep).
        /// Does NOT publish TerrainChangedEvent; the caller does (it may change a wider area).
        /// </summary>
        public static ShapeResult Shape(SimContext ctx, Vec2i c, int radius, float amount, Biome newWaterBiome)
        {
            var result = new ShapeResult();
            var state = ctx.State;
            var map = state.Map;
            radius = GodPowerArea.ClampRadius(radius);
            amount = MathUtil.Clamp(MathUtil.Sanitize(amount), -1f, 1f);
            if (amount == 0f || !map.InBounds(c)) return result;

            GodPowerArea.Bounds(map, c, radius, out int x0, out int y0, out int x1, out int y1);
            var changed = new List<int>();
            var previous = new List<float>();
            float reach = radius + 0.5f;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(c, x, y, radius)) continue;
                    int dx = x - c.X, dy = y - c.Y;
                    float t = MathUtil.Clamp01((float)Math.Sqrt(dx * dx + dy * dy) / reach);
                    float falloff = 1f - t * t * (3f - 2f * t);
                    if (falloff <= 0f) continue;
                    int i = map.Index(x, y);
                    float prev = MathUtil.Clamp(MathUtil.Sanitize(map.Elevation[i]), -1f, 1f);
                    float e = MathUtil.Clamp(prev + amount * falloff, -1f, 1f);
                    if (e == prev) continue;
                    map.Elevation[i] = e;
                    changed.Add(i);
                    previous.Add(prev);
                }
            if (changed.Count == 0) return result;
            result.TilesChanged = changed.Count;

            Reclassify(ctx, changed, previous, newWaterBiome, ref result);
            RefreshCoastal(map, x0 - 1, y0 - 1, x1 + 1, y1 + 1);
            HandleDisplaced(ctx, c, radius, ref result);
            return result;
        }

        private static void Reclassify(SimContext ctx, List<int> changed, List<float> previous, Biome newWaterBiome, ref ShapeResult result)
        {
            var map = ctx.State.Map;
            float sea = MathUtil.Sanitize(map.SeaLevel);
            if (!newWaterBiome.IsWater()) newWaterBiome = Biome.ShallowWater;
            var newLand = new List<int>();
            var newLandPrev = new List<float>();
            var newWater = new List<int>();

            for (int k = 0; k < changed.Count; k++)
            {
                int i = changed[k];
                float prev = previous[k], e = map.Elevation[i];
                Biome b = map.Biomes[i];
                bool wasWater = b.IsWater();
                bool nowWater = e < sea;
                if (nowWater)
                {
                    if (wasWater)
                    {
                        if (b != Biome.Lake) map.Biomes[i] = e < sea - DeepOffset ? Biome.DeepOcean : Biome.ShallowWater;
                    }
                    else
                    {
                        map.Biomes[i] = newWaterBiome == Biome.Lake ? Biome.Lake : (e < sea - DeepOffset ? Biome.DeepOcean : newWaterBiome);
                        newWater.Add(i);
                    }
                }
                else if (wasWater)
                {
                    map.Biomes[i] = Biome.Grassland; // provisional, so neighbors see final land/water layout
                    newLand.Add(i);
                    newLandPrev.Add(prev);
                }
                else
                {
                    map.Biomes[i] = Reband(b, prev, e, map.Temperature[i]);
                }
            }

            for (int k = 0; k < newLand.Count; k++)
            {
                int i = newLand[k];
                var p = map.PosOf(i);
                Biome baseBiome = GodPowerArea.HasWaterNeighbor4(map, p.X, p.Y) ? Biome.Beach : Biome.Grassland;
                map.Biomes[i] = Reband(baseBiome, newLandPrev[k], map.Elevation[i], map.Temperature[i]);
                map.Resource[i] = 0f;
            }
            result.NewLand = newLand.Count;

            // Anything standing on new water is lost to the depths.
            for (int k = 0; k < newWater.Count; k++)
            {
                int i = newWater[k];
                int bid = map.BuildingAt[i];
                if (bid < 0) continue;
                var b = ctx.State.Buildings.Get(bid);
                if (b != null && !b.IsDestroyed) { ctx.Ops.DestroyBuilding(b, DestroyCause.DivinePower); result.BuildingsDestroyed++; }
                if (map.BuildingAt[i] == bid) map.BuildingAt[i] = -1;
            }
            for (int k = 0; k < newWater.Count; k++)
            {
                int i = newWater[k];
                map.Flags[i] = TileFlags.None; // trees, deposits, farmland, roads, walls, ruins and fire are all submerged
                map.Resource[i] = 0f;
                map.Moisture[i] = 1f;
            }
            result.NewWater = newWater.Count;
        }

        /// <summary>Elevation-band reclassification of a land tile whose elevation moved from prev to e.</summary>
        public static Biome Reband(Biome b, float prev, float e, float temperature)
        {
            if (b == Biome.Volcanic || b.IsWater()) return b;
            if (e > prev)
            {
                if (prev <= SnowUp && e > SnowUp) return Biome.Snow;
                if (b == Biome.Snow) return b;
                if (prev <= MountainUp && e > MountainUp) return Biome.Mountain;
                if (b == Biome.Mountain) return b;
                if (prev <= HillsUp && e > HillsUp) return Biome.Hills;
                return b;
            }
            if (e < prev)
            {
                if (b == Biome.Snow && prev >= SnowDown && e < SnowDown) return Lowered(e, temperature);
                if (b == Biome.Mountain && prev >= MountainDown && e < MountainDown) return Lowered(e, temperature);
                if (b == Biome.Hills && prev >= HillsDown && e < HillsDown) return Lowered(e, temperature);
            }
            return b;
        }

        private static Biome Lowered(float e, float temperature)
        {
            if (e >= MountainDown) return Biome.Mountain;
            if (e >= HillsDown) return Biome.Hills;
            return MathUtil.Sanitize(temperature, 0.5f) < 0.2f ? Biome.Tundra : Biome.Grassland;
        }

        /// <summary>Recomputes the Coastal flag (land with a 4-neighbor water tile) in a clipped rectangle.</summary>
        public static void RefreshCoastal(WorldMap map, int x0, int y0, int x1, int y1)
        {
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
            x1 = Math.Min(map.Width - 1, x1); y1 = Math.Min(map.Height - 1, y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = map.Index(x, y);
                    if (!map.IsWater(i) && GodPowerArea.HasWaterNeighbor4(map, x, y)) map.Set(i, TileFlags.Coastal);
                    else map.Clear(i, TileFlags.Coastal);
                }
        }

        /// <summary>People standing on new water move to their settlement center; settlements left with a drowned center and no buildings are abandoned.</summary>
        private static void HandleDisplaced(SimContext ctx, Vec2i c, int radius, ref ShapeResult result)
        {
            var state = ctx.State;
            var map = state.Map;
            var people = new List<Person>();
            GodPowerArea.PeopleInRange(state, c, radius + 1, people);
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!map.IsWater(p.Position)) continue;
                var home = state.Settlements.Get(p.SettlementId);
                if (home != null && map.InBounds(home.Center) && !map.IsWater(home.Center)) p.Position = home.Center;
            }

            var settlements = new List<Settlement>();
            GodPowerArea.SettlementsInRange(state, c, radius, 1, settlements);
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (!s.IsAlive || !map.InBounds(s.Center) || !map.IsWater(s.Center)) continue;
                bool hasBuilding = false;
                for (int k = 0; k < s.BuildingIds.Count && !hasBuilding; k++)
                {
                    var b = state.Buildings.Get(s.BuildingIds[k]);
                    if (b != null && !b.IsDestroyed) hasBuilding = true;
                }
                if (hasBuilding) continue;
                ctx.Ops.DestroySettlement(s, DestroyCause.DivinePower);
                result.SettlementsLost++;
            }
        }

        /// <summary>Bumps the map's terrain version and publishes a TerrainChangedEvent for the clipped square around c.</summary>
        public static void PublishChanged(SimContext ctx, Vec2i c, int radius)
        {
            var map = ctx.State.Map;
            GodPowerArea.Bounds(map, c, Math.Max(0, radius), out int x0, out int y0, out int x1, out int y1);
            map.MarkTerrainChanged();
            ctx.Events?.Publish(new TerrainChangedEvent { MinX = x0, MinY = y0, MaxX = x1, MaxY = y1 });
        }
    }
}
