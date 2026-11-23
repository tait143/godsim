using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Scans a settlement's territory for the terrain features that hard placement rules need (water,
    /// coast, river, deposits, trees, fertile land, hills, mountains) and caches the result in the
    /// settlement blackboard as <c>ai.terr.&lt;flag&gt;</c> = 0/1 (plus <c>ai.terr.free</c>, <c>ai.terr.land</c>
    /// and bookkeeping keys). The cache is refreshed every <c>refreshTicks</c> or immediately when the
    /// territory radius or center changes. All cached state lives in <see cref="Settlement.Memory"/>, so
    /// decisions are identical after a save/load.
    /// <para>
    /// Reach: "near" features (water, river, deposits, mountains) count within TerritoryRadius + 3 tiles of
    /// the center, trees within TerritoryRadius + 4 (matching the NearForest rule); "inside" features
    /// (coast, fertile land, hills, free tiles) count on tiles the settlement owns or could claim (unowned
    /// land within its radius).
    /// </para>
    /// </summary>
    public static class TerritoryScanner
    {
        /// <summary>Margin (tiles) for NearWater/NearOre/NearStone/... style rules.</summary>
        public const int NearMargin = 3;
        /// <summary>Margin (tiles) for the NearForest rule.</summary>
        public const int ForestMargin = 4;
        /// <summary>Fertility at or above which land counts as fertile (in addition to the FertileSoil flag).</summary>
        public const float FertileThreshold = 0.5f;

        /// <summary>Feature flags in key order.</summary>
        public static readonly TerritoryFeature[] AllFeatures =
        {
            TerritoryFeature.Water, TerritoryFeature.Coast, TerritoryFeature.River, TerritoryFeature.Ore,
            TerritoryFeature.Stone, TerritoryFeature.Gold, TerritoryFeature.Coal, TerritoryFeature.Oil,
            TerritoryFeature.Trees, TerritoryFeature.Fertile, TerritoryFeature.Hills, TerritoryFeature.Mountain
        };

        /// <summary>Blackboard keys matching <see cref="AllFeatures"/> (values 0 or 1).</summary>
        public static readonly string[] FeatureKeys =
        {
            "ai.terr.water", "ai.terr.coast", "ai.terr.river", "ai.terr.ore",
            "ai.terr.stone", "ai.terr.gold", "ai.terr.coal", "ai.terr.oil",
            "ai.terr.trees", "ai.terr.fertile", "ai.terr.hills", "ai.terr.mountain"
        };

        /// <summary>Key: free buildable tiles in the territory.</summary>
        public const string KeyFree = "ai.terr.free";
        /// <summary>Key: land tiles in the territory.</summary>
        public const string KeyLand = "ai.terr.land";
        /// <summary>Key: tick of the last scan.</summary>
        public const string KeyTick = "ai.terr.tick";
        /// <summary>Key: territory radius at the last scan.</summary>
        public const string KeyRadius = "ai.terr.radius";
        /// <summary>Key: packed center at the last scan.</summary>
        public const string KeyCenter = "ai.terr.center";

        /// <summary>Scans the map around the settlement (no caching). Null-safe: returns an empty result.</summary>
        public static TerritoryFeatures Scan(WorldMap map, Settlement s)
        {
            if (map == null || s == null) return default;
            int radius = MathUtil.Clamp(s.TerritoryRadius, 1, 64);
            var c = map.ClampToBounds(s.Center.IsValid ? s.Center : Vec2i.Zero);
            int reach = radius + ForestMargin;
            int r2 = radius * radius;
            int near2 = (radius + NearMargin) * (radius + NearMargin);
            int forest2 = reach * reach;
            TerritoryFeature f = TerritoryFeature.None;
            int free = 0, land = 0;
            const TileFlags deposits = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit | TileFlags.CoalDeposit | TileFlags.OilDeposit;

            int y0 = Math.Max(0, c.Y - reach), y1 = Math.Min(map.Height - 1, c.Y + reach);
            int x0 = Math.Max(0, c.X - reach), x1 = Math.Min(map.Width - 1, c.X + reach);
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - c.Y;
                for (int x = x0; x <= x1; x++)
                {
                    int dx = x - c.X;
                    int d2 = dx * dx + dy * dy;
                    int i = map.Index(x, y);
                    var flags = map.Flags[i];
                    var biome = map.Biomes[i];
                    bool water = biome.IsWater();

                    if (d2 <= forest2 && (flags & TileFlags.Trees) != 0) f |= TerritoryFeature.Trees;
                    if (d2 <= near2)
                    {
                        if (water) f |= TerritoryFeature.Water;
                        if ((flags & TileFlags.River) != 0) f |= TerritoryFeature.River | TerritoryFeature.Water;
                        if (biome == Biome.Mountain) f |= TerritoryFeature.Mountain;
                        if ((flags & deposits) != 0)
                        {
                            if ((flags & TileFlags.OreDeposit) != 0) f |= TerritoryFeature.Ore;
                            if ((flags & TileFlags.StoneDeposit) != 0) f |= TerritoryFeature.Stone;
                            if ((flags & TileFlags.GoldDeposit) != 0) f |= TerritoryFeature.Gold;
                            if ((flags & TileFlags.CoalDeposit) != 0) f |= TerritoryFeature.Coal;
                            if ((flags & TileFlags.OilDeposit) != 0) f |= TerritoryFeature.Oil;
                        }
                    }

                    if (water) continue;
                    int owner = map.Owner[i];
                    bool inside = owner == s.Id || (owner < 0 && d2 <= r2);
                    if (!inside) continue;
                    land++;
                    if (biome == Biome.Hills) f |= TerritoryFeature.Hills;
                    bool buildable = biome.IsBuildable();
                    if (buildable && ((flags & TileFlags.FertileSoil) != 0 || map.Fertility[i] >= FertileThreshold)) f |= TerritoryFeature.Fertile;
                    if ((flags & TileFlags.Coastal) != 0 || TouchesWater(map, x, y)) f |= TerritoryFeature.Coast;
                    if (map.IsFreeBuildable(i)) free++;
                }
            }
            return new TerritoryFeatures(f, free, land);
        }

        /// <summary>
        /// Returns the cached features, rescanning when the cache is missing, older than
        /// <paramref name="refreshTicks"/>, from the future (loaded save), or when the territory radius or
        /// center changed. A rescan writes all <c>ai.terr.*</c> keys.
        /// </summary>
        public static TerritoryFeatures GetOrScan(WorldMap map, Settlement s, long tick, int refreshTicks = SimTime.TicksPerYear)
        {
            if (s == null) return default;
            if (map == null) return Load(s);
            if (NeedsRefresh(s, tick, refreshTicks))
            {
                var f = Scan(map, s);
                Store(s, f, tick);
                return f;
            }
            return Load(s);
        }

        /// <summary>True if the cached scan is missing or stale.</summary>
        public static bool NeedsRefresh(Settlement s, long tick, int refreshTicks)
        {
            if (s == null) return false;
            if (!s.Memory.TryGetValue(KeyTick, out float last)) return true;
            if (last > tick || tick - (long)last >= Math.Max(1, refreshTicks)) return true;
            if ((int)s.Mem(KeyRadius, -1f) != s.TerritoryRadius) return true;
            if (s.Mem(KeyCenter, -1f) != PackCenter(s.Center)) return true;
            return false;
        }

        /// <summary>Writes a scan result into the settlement blackboard.</summary>
        public static void Store(Settlement s, TerritoryFeatures f, long tick)
        {
            if (s == null) return;
            for (int i = 0; i < AllFeatures.Length; i++) s.Memory[FeatureKeys[i]] = f.Has(AllFeatures[i]) ? 1f : 0f;
            s.Memory[KeyFree] = f.FreeTiles;
            s.Memory[KeyLand] = f.LandTiles;
            s.Memory[KeyTick] = tick;
            s.Memory[KeyRadius] = s.TerritoryRadius;
            s.Memory[KeyCenter] = PackCenter(s.Center);
        }

        /// <summary>Reads the cached features from the blackboard (missing keys count as absent).</summary>
        public static TerritoryFeatures Load(Settlement s)
        {
            if (s == null) return default;
            TerritoryFeature f = TerritoryFeature.None;
            for (int i = 0; i < AllFeatures.Length; i++) if (s.Mem(FeatureKeys[i]) > 0.5f) f |= AllFeatures[i];
            return new TerritoryFeatures(f, (int)MathUtil.Sanitize(s.Mem(KeyFree)), (int)MathUtil.Sanitize(s.Mem(KeyLand)));
        }

        /// <summary>Features required (all of them) by the hard parts of a placement rule. NearWater is handled by <see cref="IsPlacementFeasible"/>.</summary>
        public static TerritoryFeature RequiredFeatures(PlacementRule rule)
        {
            TerritoryFeature f = TerritoryFeature.None;
            if ((rule & PlacementRule.Coastal) != 0) f |= TerritoryFeature.Coast;
            if ((rule & PlacementRule.OnHills) != 0) f |= TerritoryFeature.Hills;
            if ((rule & PlacementRule.NearMountain) != 0) f |= TerritoryFeature.Mountain;
            if ((rule & PlacementRule.NearForest) != 0) f |= TerritoryFeature.Trees;
            if ((rule & PlacementRule.NearOre) != 0) f |= TerritoryFeature.Ore;
            if ((rule & PlacementRule.NearStone) != 0) f |= TerritoryFeature.Stone;
            if ((rule & PlacementRule.NearGold) != 0) f |= TerritoryFeature.Gold;
            if ((rule & PlacementRule.NearCoal) != 0) f |= TerritoryFeature.Coal;
            if ((rule & PlacementRule.NearOil) != 0) f |= TerritoryFeature.Oil;
            if ((rule & PlacementRule.Fertile) != 0) f |= TerritoryFeature.Fertile;
            if ((rule & PlacementRule.NearRiver) != 0) f |= TerritoryFeature.River;
            return f;
        }

        /// <summary>
        /// True if every hard requirement of <paramref name="rule"/> is satisfiable in the territory.
        /// NearWater is satisfied by water, a river or a coast. Soft preferences (Central, Outskirts,
        /// Isolated) never block.
        /// </summary>
        public static bool IsPlacementFeasible(PlacementRule rule, TerritoryFeatures features)
        {
            var required = RequiredFeatures(rule);
            if (required != TerritoryFeature.None && !features.Has(required)) return false;
            if ((rule & PlacementRule.NearWater) != 0 && !features.HasAny(TerritoryFeature.Water | TerritoryFeature.River | TerritoryFeature.Coast)) return false;
            return true;
        }

        /// <summary>Feature name used in the blackboard key (e.g. "stone").</summary>
        public static string FeatureName(TerritoryFeature f)
        {
            for (int i = 0; i < AllFeatures.Length; i++) if (AllFeatures[i] == f) return FeatureKeys[i].Substring("ai.terr.".Length);
            return f.ToString().ToLowerInvariant();
        }

        private static float PackCenter(Vec2i c) => c.IsValid ? (float)(MathUtil.Clamp(c.X, 0, 4095) * 4096 + MathUtil.Clamp(c.Y, 0, 4095)) : -2f;

        private static bool TouchesWater(WorldMap map, int x, int y)
        {
            var dirs = Vec2i.Dirs4;
            for (int d = 0; d < dirs.Length; d++)
            {
                int nx = x + dirs[d].X, ny = y + dirs[d].Y;
                if (map.InBounds(nx, ny) && map.Biomes[map.Index(nx, ny)].IsWater()) return true;
            }
            return false;
        }
    }
}
