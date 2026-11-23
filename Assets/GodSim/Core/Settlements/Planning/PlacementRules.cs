using System;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Hard placement requirements (<see cref="PlacementRule"/> Near*/OnHills/Coastal/Fertile), footprint
    /// claimability, access-tile search and the deterministic ring enumeration used by the town planner.
    /// All methods are allocation-free.
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>Scan margin around the footprint (largest requirement distance).</summary>
        public const int ProbeMargin = 4;

        /// <summary>Minimum average fertility that satisfies <see cref="PlacementRule.Fertile"/>.</summary>
        public const float FertileThreshold = 0.45f;

        /// <summary>Minimum Trees tiles within 4 that satisfy <see cref="PlacementRule.NearForest"/>.</summary>
        public const int ForestTreeCount = 3;

        /// <summary>Dwarves may build these on Mountain tiles (CanPlace allowRugged).</summary>
        public static bool UsesRugged(RaceId race, BuildingType type)
        {
            if (race != RaceId.Dwarf) return false;
            switch (type)
            {
                case BuildingType.Mine:
                case BuildingType.DeepForge:
                case BuildingType.House:
                case BuildingType.Smelter:
                case BuildingType.Blacksmith:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Fills <paramref name="p"/> with neighborhood counts for the footprint at origin (w x h) in one scan.</summary>
        public static void Probe(WorldMap map, Vec2i origin, int w, int h, ref SiteProbe p)
        {
            p = default(SiteProbe);
            if (map == null || w <= 0 || h <= 0) return;
            p.Area = w * h;
            int x0 = origin.X, y0 = origin.Y, x1 = origin.X + w - 1, y1 = origin.Y + h - 1;
            for (int y = y0 - ProbeMargin; y <= y1 + ProbeMargin; y++)
            {
                if (y < 0 || y >= map.Height) continue;
                int dy = y < y0 ? y0 - y : (y > y1 ? y - y1 : 0);
                for (int x = x0 - ProbeMargin; x <= x1 + ProbeMargin; x++)
                {
                    if (x < 0 || x >= map.Width) continue;
                    int dx = x < x0 ? x0 - x : (x > x1 ? x - x1 : 0);
                    int d = dx > dy ? dx : dy;
                    int i = y * map.Width + x;
                    Biome b = map.Biomes[i];
                    TileFlags f = map.Flags[i];
                    bool water = b.IsWater();
                    bool occupied = map.BuildingAt[i] >= 0;

                    if (d == 0)
                    {
                        if (b == Biome.Hills) p.HillsInFootprint++;
                        p.FertilitySum += MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i]));
                        if ((f & TileFlags.FertileSoil) != 0) p.FertileSoilInFootprint++;
                        if ((f & TileFlags.Farmland) != 0) p.FarmlandInFootprint++;
                        if ((f & TileFlags.Coastal) != 0) p.CoastalInFootprint++;
                    }
                    else
                    {
                        if (d == 1)
                        {
                            if (occupied) p.OccupiedAdjacent++;
                            if (dx == 0 || dy == 0)
                            {
                                if (water) p.AdjacentWater++;
                                if ((f & TileFlags.Road) != 0) p.RoadAdjacent++;
                            }
                        }
                        if (d <= 2)
                        {
                            if (occupied) p.OccupiedNear2++;
                            if ((f & TileFlags.Road) != 0) p.RoadNear2++;
                        }
                    }

                    if (d <= 3)
                    {
                        bool river = (f & TileFlags.River) != 0;
                        if (water) p.WaterNear3++;
                        if (river) p.RiverNear3++;
                        if (water || river) p.WaterOrRiverNear3++;
                        if (b == Biome.Mountain) p.MountainNear3++;
                        if (b == Biome.Hills) p.HillsNear3++;
                        if ((f & TileFlags.OreDeposit) != 0) p.OreNear3++;
                        if ((f & TileFlags.StoneDeposit) != 0) p.StoneNear3++;
                        if ((f & TileFlags.GoldDeposit) != 0) p.GoldNear3++;
                        if ((f & TileFlags.CoalDeposit) != 0) p.CoalNear3++;
                        if ((f & TileFlags.OilDeposit) != 0) p.OilNear3++;
                    }
                    if ((f & TileFlags.Trees) != 0) p.TreesNear4++;
                }
            }
        }

        /// <summary>True if every HARD requirement in <paramref name="rule"/> is satisfied by the probe.
        /// Soft preferences (Central/Outskirts/Isolated) never fail.</summary>
        public static bool MeetsRequirements(PlacementRule rule, in SiteProbe p)
        {
            if (rule == PlacementRule.None) return true;
            if ((rule & PlacementRule.NearWater) != 0 && p.WaterOrRiverNear3 == 0) return false;
            if ((rule & PlacementRule.Coastal) != 0 && p.AdjacentWater == 0 && p.CoastalInFootprint == 0) return false;
            if ((rule & PlacementRule.OnHills) != 0 && p.HillsInFootprint * 2 <= p.Area) return false;
            if ((rule & PlacementRule.NearMountain) != 0 && p.MountainNear3 == 0) return false;
            if ((rule & PlacementRule.NearForest) != 0 && p.TreesNear4 < ForestTreeCount) return false;
            if ((rule & PlacementRule.NearOre) != 0 && p.OreNear3 == 0) return false;
            if ((rule & PlacementRule.NearStone) != 0 && p.StoneNear3 == 0) return false;
            if ((rule & PlacementRule.NearGold) != 0 && p.GoldNear3 == 0) return false;
            if ((rule & PlacementRule.NearCoal) != 0 && p.CoalNear3 == 0) return false;
            if ((rule & PlacementRule.NearOil) != 0 && p.OilNear3 == 0) return false;
            if ((rule & PlacementRule.Fertile) != 0 && !(p.AverageFertility >= FertileThreshold || p.FertileSoilInFootprint > 0)) return false;
            if ((rule & PlacementRule.NearRiver) != 0 && p.RiverNear3 == 0) return false;
            return true;
        }

        /// <summary>Convenience: probes the footprint and checks the hard requirements.</summary>
        public static bool MeetsRequirements(WorldMap map, PlacementRule rule, Vec2i origin, int w, int h)
        {
            var p = default(SiteProbe);
            Probe(map, origin, w, h, ref p);
            return MeetsRequirements(rule, in p);
        }

        /// <summary>True if no footprint tile is owned by another settlement and, unless
        /// <paramref name="allowFarmland"/> (farms may reuse fields), none is tilled Farmland
        /// (CanPlace allows Farmland, the planner keeps fields free for non-farm buildings).</summary>
        public static bool FootprintClaimable(WorldMap map, int settlementId, Vec2i origin, int w, int h, bool allowFarmland = false)
        {
            for (int y = origin.Y; y < origin.Y + h; y++)
                for (int x = origin.X; x < origin.X + w; x++)
                {
                    if (!map.InBounds(x, y)) return false;
                    int i = map.Index(x, y);
                    int owner = map.Owner[i];
                    if (owner >= 0 && owner != settlementId) return false;
                    if (!allowFarmland && (map.Flags[i] & TileFlags.Farmland) != 0) return false;
                }
            return true;
        }

        /// <summary>True if the tile can serve as a building entrance: an unoccupied road, or free buildable non-farmland land.</summary>
        public static bool IsAccessTile(WorldMap map, int i)
        {
            if (map.BuildingAt[i] >= 0 || map.Biomes[i].IsWater()) return false;
            TileFlags f = map.Flags[i];
            if ((f & TileFlags.Road) != 0) return (f & TileFlags.Wall) == 0;
            return map.IsFreeBuildable(i) && (f & TileFlags.Farmland) == 0;
        }

        /// <summary>
        /// Finds the best orthogonally-adjacent access tile of a footprint: road tiles first, then the tile
        /// closest (Manhattan) to <paramref name="toward"/>. <paramref name="rotation"/> is the facing
        /// (0 = N/+Y, 1 = E/+X, 2 = S/-Y, 3 = W/-X, matching <see cref="Vec2i.Dirs4"/>). Returns Invalid if none.
        /// </summary>
        public static Vec2i FindAccessTile(WorldMap map, Vec2i origin, int w, int h, Vec2i toward, out byte rotation)
        {
            rotation = 0;
            Vec2i best = Vec2i.Invalid;
            int bestRank = int.MaxValue;
            for (int x = origin.X; x < origin.X + w; x++) Consider(map, x, origin.Y + h, 0, toward, ref best, ref bestRank, ref rotation);
            for (int y = origin.Y + h - 1; y >= origin.Y; y--) Consider(map, origin.X + w, y, 1, toward, ref best, ref bestRank, ref rotation);
            for (int x = origin.X + w - 1; x >= origin.X; x--) Consider(map, x, origin.Y - 1, 2, toward, ref best, ref bestRank, ref rotation);
            for (int y = origin.Y; y < origin.Y + h; y++) Consider(map, origin.X - 1, y, 3, toward, ref best, ref bestRank, ref rotation);
            return best;
        }

        private static void Consider(WorldMap map, int x, int y, byte rot, Vec2i toward, ref Vec2i best, ref int bestRank, ref byte bestRot)
        {
            if (!map.InBounds(x, y)) return;
            int i = map.Index(x, y);
            if (!IsAccessTile(map, i)) return;
            int rank = Math.Abs(x - toward.X) + Math.Abs(y - toward.Y);
            if ((map.Flags[i] & TileFlags.Road) == 0) rank += 1000000;
            if (rank >= bestRank) return;
            bestRank = rank;
            best = new Vec2i(x, y);
            bestRot = rot;
        }

        /// <summary>Most access tiles a footprint can have (an 8 x 8 footprint has 32).</summary>
        public const int MaxAccessTiles = 32;

        /// <summary>
        /// Best entrance of a footprint that can reach the settlement's road network according to
        /// <paramref name="field"/>: network tiles first, then the fewest steps to the network, then the
        /// Manhattan distance to <paramref name="toward"/>. Unlinked road stubs get no preference. Returns
        /// Invalid when no entrance is reachable (the site would be cut off from the town).
        /// </summary>
        public static Vec2i FindAccessTile(WorldMap map, Vec2i origin, int w, int h, AccessField field, Vec2i toward, out byte rotation)
        {
            rotation = 0;
            if (map == null || field == null) return Vec2i.Invalid;
            Vec2i best = Vec2i.Invalid;
            int bestRank = int.MaxValue;
            for (int s = 0; s < 4; s++)
            {
                int len = SideLength(w, h, s);
                for (int t = 0; t < len; t++)
                {
                    SideTile(origin, w, h, s, t, out int x, out int y);
                    int rank = ReachableRank(map, field, x, y, toward);
                    if (rank < 0 || rank >= bestRank) continue;
                    bestRank = rank;
                    best = new Vec2i(x, y);
                    rotation = (byte)s;
                }
            }
            return best;
        }

        /// <summary>
        /// Fills <paramref name="buffer"/> with every reachable entrance of the footprint, best first (same
        /// ranking as <see cref="FindAccessTile(WorldMap, Vec2i, int, int, AccessField, Vec2i, out byte)"/>;
        /// ties keep the N, E, S, W side order). Returns the number written (at most the buffer length).
        /// </summary>
        public static int CollectAccessTiles(WorldMap map, Vec2i origin, int w, int h, AccessField field, Vec2i toward, AccessCandidate[] buffer)
        {
            if (map == null || field == null || buffer == null || buffer.Length == 0) return 0;
            int count = 0;
            for (int s = 0; s < 4; s++)
            {
                int len = SideLength(w, h, s);
                for (int t = 0; t < len; t++)
                {
                    SideTile(origin, w, h, s, t, out int x, out int y);
                    int rank = ReachableRank(map, field, x, y, toward);
                    if (rank < 0) continue;
                    if (count == buffer.Length && rank >= buffer[count - 1].Rank) continue;
                    int j = count < buffer.Length ? count++ : count - 1;
                    while (j > 0 && buffer[j - 1].Rank > rank) { buffer[j] = buffer[j - 1]; j--; }
                    buffer[j] = new AccessCandidate { Tile = new Vec2i(x, y), Rotation = (byte)s, Rank = rank };
                }
            }
            return count;
        }

        private static int SideLength(int w, int h, int side) => side == 0 || side == 2 ? w : h;

        // Side 0 = N (row above), 1 = E (column right), 2 = S (row below), 3 = W (column left): the facings of Dirs4.
        private static void SideTile(Vec2i origin, int w, int h, int side, int t, out int x, out int y)
        {
            switch (side)
            {
                case 0: x = origin.X + t; y = origin.Y + h; break;
                case 1: x = origin.X + w; y = origin.Y + h - 1 - t; break;
                case 2: x = origin.X + w - 1 - t; y = origin.Y - 1; break;
                default: x = origin.X - 1; y = origin.Y + t; break;
            }
        }

        private static int ReachableRank(WorldMap map, AccessField field, int x, int y, Vec2i toward)
        {
            if (!map.InBounds(x, y)) return -1;
            if (!IsAccessTile(map, map.Index(x, y))) return -1;
            int dist = field.DistanceAt(x, y);
            if (dist < 0) return -1;
            int manhattan = Math.Min(65535, Math.Abs(x - toward.X) + Math.Abs(y - toward.Y));
            return Math.Min(dist, 30000) * 65536 + manhattan;
        }

        /// <summary>
        /// Offset of the t-th tile (0 &lt;= t &lt; 8r, or t = 0 for r = 0) on the square ring of Chebyshev
        /// radius r, enumerated clockwise from the top-left corner. Deterministic; covers each ring tile once.
        /// </summary>
        public static void RingOffset(int r, int t, out int dx, out int dy)
        {
            if (r <= 0) { dx = 0; dy = 0; return; }
            int side = 2 * r;
            if (t < side) { dx = -r + t; dy = r; }
            else if (t < 2 * side) { dx = r; dy = r - (t - side); }
            else if (t < 3 * side) { dx = r - (t - 2 * side); dy = -r; }
            else { dx = -r; dy = -r + (t - 3 * side); }
        }

        /// <summary>Number of tiles on the ring of Chebyshev radius r.</summary>
        public static int RingCount(int r) => r <= 0 ? 1 : 8 * r;
    }
}
