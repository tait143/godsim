using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Which hard placement requirements (<see cref="PlacementRule"/> Near*/OnHills/Coastal/Fertile) a settlement's
    /// territory can satisfy, so the logistics advisor never votes for a quarry without stone or a mine without ore.
    /// Scans the territory circle (plus a probe margin), skipping tiles owned by other settlements. Results are
    /// cached per settlement id and refreshed yearly, on terrain edits or when the territory grows.
    /// </summary>
    public sealed class LogisticsTerritory
    {
        public const int RefreshTicks = SimTime.TicksPerYear;
        public const int ProbeMargin = 4;
        public const float FertileThreshold = 0.45f;
        private const int MaxCache = 4096;

        /// <summary>All placement bits that are hard terrain requirements (the rest are soft preferences).</summary>
        public const PlacementRule HardRules = PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills
            | PlacementRule.NearMountain | PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone
            | PlacementRule.NearGold | PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver;

        private struct Entry
        {
            public PlacementRule Features;
            public long Tick;
            public int Radius;
            public int TerrainVersion;
        }

        private readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>();

        public void Clear() => _cache.Clear();

        /// <summary>True when every hard requirement of the rule is present in the features.</summary>
        public static bool Satisfies(PlacementRule rule, PlacementRule features)
        {
            var need = rule & HardRules;
            return (need & features) == need;
        }

        /// <summary>Hard placement features available in the settlement's territory (cached).</summary>
        public PlacementRule Features(SimContext ctx, Settlement s)
        {
            var map = ctx?.State?.Map;
            if (map == null || s == null) return HardRules;
            long tick = ctx.State.Tick;
            if (_cache.TryGetValue(s.Id, out var e) && e.Radius == s.TerritoryRadius && e.TerrainVersion == map.TerrainVersion
                && tick >= e.Tick && tick - e.Tick < RefreshTicks)
                return e.Features;
            var f = Scan(map, s);
            if (_cache.Count >= MaxCache) _cache.Clear();
            _cache[s.Id] = new Entry { Features = f, Tick = tick, Radius = s.TerritoryRadius, TerrainVersion = map.TerrainVersion };
            return f;
        }

        /// <summary>Uncached scan of the territory circle (radius = TerritoryRadius + probe margin).</summary>
        public static PlacementRule Scan(WorldMap map, Settlement s)
        {
            int r = MathUtil.Clamp(s.TerritoryRadius + ProbeMargin, 2, 48);
            int r2 = r * r;
            var c = s.Center;
            var f = PlacementRule.None;
            for (int y = c.Y - r; y <= c.Y + r; y++)
            {
                if (y < 0 || y >= map.Height) continue;
                int dy = y - c.Y;
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (x < 0 || x >= map.Width) continue;
                    int dx = x - c.X;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    int owner = map.Owner[i];
                    if (owner >= 0 && owner != s.Id) continue;
                    var b = map.Biomes[i];
                    var fl = map.Flags[i];
                    if (b.IsWater()) f |= PlacementRule.NearWater;
                    if (b == Biome.Hills) f |= PlacementRule.OnHills;
                    if (b == Biome.Mountain) f |= PlacementRule.NearMountain;
                    if ((fl & TileFlags.River) != 0) f |= PlacementRule.NearWater | PlacementRule.NearRiver;
                    if ((fl & TileFlags.Coastal) != 0) f |= PlacementRule.Coastal | PlacementRule.NearWater;
                    if ((fl & TileFlags.Trees) != 0) f |= PlacementRule.NearForest;
                    if ((fl & TileFlags.OreDeposit) != 0) f |= PlacementRule.NearOre;
                    if ((fl & TileFlags.StoneDeposit) != 0) f |= PlacementRule.NearStone;
                    if ((fl & TileFlags.GoldDeposit) != 0) f |= PlacementRule.NearGold;
                    if ((fl & TileFlags.CoalDeposit) != 0) f |= PlacementRule.NearCoal;
                    if ((fl & TileFlags.OilDeposit) != 0) f |= PlacementRule.NearOil;
                    if (!b.IsWater() && ((fl & TileFlags.FertileSoil) != 0 || map.Fertility[i] >= FertileThreshold)) f |= PlacementRule.Fertile;
                    if (f == HardRules) return f;
                }
            }
            return f;
        }
    }
}
