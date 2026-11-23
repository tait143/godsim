using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Cheap, cached survey of which hard <see cref="PlacementRule"/> requirements a settlement's surroundings can
    /// satisfy (coast, hills, ore, forest...), so the age advisor never pushes a Harbor inland or a Coal Mine without
    /// coal. Results are cached per settlement and refreshed every <see cref="AgesTuning.SiteRefreshTicks"/> ticks or
    /// when terrain/territory changes. Lookups only (no dictionary iteration): deterministic.
    /// </summary>
    public sealed class AgesSiteSurvey
    {
        private readonly Dictionary<int, int> _mask = new Dictionary<int, int>();
        private readonly Dictionary<int, long> _stamp = new Dictionary<int, long>();

        /// <summary>Bitmask of satisfiable <see cref="PlacementRule"/> flags (soft preferences always set).</summary>
        public int Get(SimContext ctx, Settlement s)
        {
            if (ctx == null || ctx.State == null || ctx.State.Map == null || s == null) return 0;
            var map = ctx.State.Map;
            long stamp = ((long)map.TerrainVersion << 40) ^ ((long)s.TerritoryRadius << 32);
            long due = ctx.Tick / AgesTuning.SiteRefreshTicks;
            long key = stamp ^ due;
            if (_stamp.TryGetValue(s.Id, out long k) && k == key && _mask.TryGetValue(s.Id, out int cached)) return cached;
            int m = Compute(map, s);
            if (_mask.Count > 4096) { _mask.Clear(); _stamp.Clear(); }
            _mask[s.Id] = m;
            _stamp[s.Id] = key;
            return m;
        }

        /// <summary>True if every hard placement requirement of the definition is satisfiable for the surveyed mask.</summary>
        public static bool Feasible(BuildingDefinition d, int mask)
        {
            if (d == null) return false;
            int hard = (int)(d.Placement & AgesBuildingIndex.HardPlacement);
            return (hard & ~mask) == 0;
        }

        /// <summary>Scans the territory (+3 tiles of reach) once. Stride 2 on large territories.</summary>
        public static int Compute(WorldMap map, Settlement s)
        {
            int soft = (int)(PlacementRule.Central | PlacementRule.Outskirts | PlacementRule.Isolated);
            if (map == null || s == null) return soft;
            int radius = Math.Max(2, s.TerritoryRadius) + 3;
            int stride = radius > 14 ? 2 : 1;
            int r2 = radius * radius;
            int m = soft;
            int cx = s.Center.X, cy = s.Center.Y;
            for (int dy = -radius; dy <= radius; dy += stride)
            {
                int y = cy + dy;
                if (y < 0 || y >= map.Height) continue;
                for (int dx = -radius; dx <= radius; dx += stride)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int x = cx + dx;
                    if (x < 0 || x >= map.Width) continue;
                    int i = map.Index(x, y);
                    var f = map.Flags[i];
                    var b = map.Biomes[i];
                    bool water = b.IsWater();
                    if (water || (f & (TileFlags.River | TileFlags.Coastal)) != 0) m |= (int)PlacementRule.NearWater;
                    if ((f & TileFlags.Coastal) != 0) m |= (int)PlacementRule.Coastal;
                    if ((f & TileFlags.River) != 0) m |= (int)PlacementRule.NearRiver;
                    if (b == Biome.Hills) m |= (int)(PlacementRule.OnHills | PlacementRule.NearStone);
                    if (b == Biome.Mountain) m |= (int)(PlacementRule.NearMountain | PlacementRule.NearStone);
                    if ((f & TileFlags.Trees) != 0 || b == Biome.Forest || b == Biome.Jungle || b == Biome.Taiga) m |= (int)PlacementRule.NearForest;
                    if ((f & TileFlags.OreDeposit) != 0) m |= (int)PlacementRule.NearOre;
                    if ((f & TileFlags.StoneDeposit) != 0) m |= (int)PlacementRule.NearStone;
                    if ((f & TileFlags.GoldDeposit) != 0) m |= (int)PlacementRule.NearGold;
                    if ((f & TileFlags.CoalDeposit) != 0) m |= (int)PlacementRule.NearCoal;
                    if ((f & TileFlags.OilDeposit) != 0) m |= (int)PlacementRule.NearOil;
                    if (!water && ((f & TileFlags.FertileSoil) != 0 || map.Fertility[i] >= 0.45f)) m |= (int)PlacementRule.Fertile;
                }
            }
            return m;
        }
    }
}
