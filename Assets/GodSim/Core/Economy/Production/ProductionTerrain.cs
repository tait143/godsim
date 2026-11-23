using System;

namespace GodSim.Core.Economy
{
    /// <summary>
    /// Bounding box accumulator for tiles whose flags changed during one system run
    /// (published once per run as a <see cref="TerrainChangedEvent"/>). Allocation-free.
    /// </summary>
    internal struct TileBounds
    {
        public int MinX, MinY, MaxX, MaxY;
        public bool Any;

        public void Reset() { Any = false; MinX = MinY = MaxX = MaxY = 0; }

        public void Include(int x, int y)
        {
            if (!Any) { MinX = MaxX = x; MinY = MaxY = y; Any = true; return; }
            if (x < MinX) MinX = x; else if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y; else if (y > MaxY) MaxY = y;
        }

        public TerrainChangedEvent ToEvent() => new TerrainChangedEvent { MinX = MinX, MinY = MinY, MaxX = MaxX, MaxY = MaxY };
    }

    /// <summary>
    /// Terrain-dependent yield helpers for <see cref="ProductionSystem"/>: footprint-relative tile scans
    /// (bounded by footprint size and a small radius), yield factors and nearest-first extraction.
    /// Distances are Chebyshev distances from the building footprint rectangle.
    /// </summary>
    internal static class ProductionTerrain
    {
        /// <summary>All harvestable deposit flags.</summary>
        internal const TileFlags DepositFlags = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit | TileFlags.CoalDeposit | TileFlags.OilDeposit;

        /// <summary>Wood assumed on a Trees tile whose Resource was never initialized (written by another module without an amount).</summary>
        internal const float DefaultTreeWood = 60f;
        /// <summary>Amount assumed on a deposit tile whose Resource was never initialized.</summary>
        internal const float DefaultDepositAmount = 250f;
        /// <summary>A tile at or below this amount is considered exhausted.</summary>
        internal const float ExhaustedEpsilon = 1e-3f;

        internal const int FertileFarmlandRadius = 2;
        internal const int WoodRadius = 4;
        internal const int DepositRadius = 3;
        internal const int FishingRadius = 3;
        internal const int HuntingRadius = 5;

        /// <summary>
        /// Fertile placement: 0.4 + average footprint fertility + 0.025 per Farmland tile within 2 (max +0.25),
        /// clamped to 0.4..1.4. Fertility 0.6 with no farmland gives exactly 1.0.
        /// </summary>
        internal static float FertilityFactor(WorldMap map, int ox, int oy, int w, int h)
        {
            float sum = 0f;
            int n = 0;
            int x0 = Math.Max(0, ox), y0 = Math.Max(0, oy);
            int x1 = Math.Min(map.Width - 1, ox + w - 1), y1 = Math.Min(map.Height - 1, oy + h - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    sum += MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[y * map.Width + x]));
                    n++;
                }
            float avg = n > 0 ? sum / n : 0f;
            int farmland = CountFlag(map, ox, oy, w, h, FertileFarmlandRadius, TileFlags.Farmland);
            return MathUtil.Clamp(0.4f + avg + Math.Min(0.25f, farmland * 0.025f), 0.4f, 1.4f);
        }

        /// <summary>Woodcutting: 0 without trees, else 0.3 + 0.7 * trees/16 (max 1.2).</summary>
        internal static float WoodFactor(int trees) => trees <= 0 ? 0f : Math.Min(1.2f, 0.3f + 0.7f * trees / 16f);

        /// <summary>Mining/quarrying: 0 without deposits, else 0.7 + 0.1 per deposit tile (max 1.2).</summary>
        internal static float DepositFactor(int deposits) => deposits <= 0 ? 0f : Math.Min(1.2f, 0.7f + 0.1f * deposits);

        /// <summary>Fishing: 0 without water, else 0.35 + 0.65 * waterTiles/12 (max 1.2).</summary>
        internal static float FishingFactor(int water) => water <= 0 ? 0f : Math.Min(1.2f, 0.35f + 0.65f * water / 12f);

        /// <summary>Hunting: 0 without woodland, else 0.3 + 0.7 * trees/30 (max 1.2).</summary>
        internal static float HuntingFactor(int trees) => trees <= 0 ? 0f : Math.Min(1.2f, 0.3f + 0.7f * trees / 30f);

        /// <summary>Deposit flags whose extracted resource is exactly <paramref name="res"/> and that the placement rule requires.</summary>
        internal static TileFlags DepositFlagsFor(PlacementRule rule, ResourceType res)
        {
            TileFlags m = TileFlags.None;
            if ((rule & PlacementRule.NearOre) != 0 && res == ResourceType.Ore) m |= TileFlags.OreDeposit;
            if ((rule & PlacementRule.NearStone) != 0 && res == ResourceType.Stone) m |= TileFlags.StoneDeposit;
            if ((rule & PlacementRule.NearGold) != 0 && res == ResourceType.Gold) m |= TileFlags.GoldDeposit;
            if ((rule & PlacementRule.NearCoal) != 0 && res == ResourceType.Fuel) m |= TileFlags.CoalDeposit;
            if ((rule & PlacementRule.NearOil) != 0 && res == ResourceType.Fuel) m |= TileFlags.OilDeposit;
            return m;
        }

        /// <summary>Every deposit flag the placement rule requires (used for by-products of a deposit).</summary>
        internal static TileFlags DepositFlagsOf(PlacementRule rule)
        {
            TileFlags m = TileFlags.None;
            if ((rule & PlacementRule.NearOre) != 0) m |= TileFlags.OreDeposit;
            if ((rule & PlacementRule.NearStone) != 0) m |= TileFlags.StoneDeposit;
            if ((rule & PlacementRule.NearGold) != 0) m |= TileFlags.GoldDeposit;
            if ((rule & PlacementRule.NearCoal) != 0) m |= TileFlags.CoalDeposit;
            if ((rule & PlacementRule.NearOil) != 0) m |= TileFlags.OilDeposit;
            return m;
        }

        /// <summary>Counts tiles carrying any of <paramref name="mask"/> within radius r of the footprint.</summary>
        internal static int CountFlag(WorldMap map, int ox, int oy, int w, int h, int r, TileFlags mask)
        {
            int c = 0;
            int x0 = Math.Max(0, ox - r), y0 = Math.Max(0, oy - r);
            int x1 = Math.Min(map.Width - 1, ox + w - 1 + r), y1 = Math.Min(map.Height - 1, oy + h - 1 + r);
            var flags = map.Flags;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * map.Width;
                for (int x = x0; x <= x1; x++) if ((flags[row + x] & mask) != 0) c++;
            }
            return c;
        }

        /// <summary>Counts water tiles (ocean/lake biomes or River-flagged land) within radius r of the footprint.</summary>
        internal static int CountWater(WorldMap map, int ox, int oy, int w, int h, int r)
        {
            int c = 0;
            int x0 = Math.Max(0, ox - r), y0 = Math.Max(0, oy - r);
            int x1 = Math.Min(map.Width - 1, ox + w - 1 + r), y1 = Math.Min(map.Height - 1, oy + h - 1 + r);
            for (int y = y0; y <= y1; y++)
            {
                int row = y * map.Width;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    if (map.Biomes[i].IsWater() || (map.Flags[i] & TileFlags.River) != 0) c++;
                }
            }
            return c;
        }

        /// <summary>
        /// Counts harvestable tiles (any of <paramref name="mask"/>) within radius r and sums their remaining amount.
        /// A flagged tile with no recorded amount counts with the default amount (see <see cref="UsableAmount"/>).
        /// </summary>
        internal static int CountHarvestable(WorldMap map, int ox, int oy, int w, int h, int r, TileFlags mask, out float total)
        {
            int c = 0;
            total = 0f;
            int x0 = Math.Max(0, ox - r), y0 = Math.Max(0, oy - r);
            int x1 = Math.Min(map.Width - 1, ox + w - 1 + r), y1 = Math.Min(map.Height - 1, oy + h - 1 + r);
            var flags = map.Flags;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * map.Width;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    TileFlags f = flags[i] & mask;
                    if (f == 0) continue;
                    c++;
                    total += UsableAmount(map.Resource[i], f);
                }
            }
            return c;
        }

        /// <summary>
        /// Remaining amount on a flagged tile. Our own extraction clears the flag when a tile reaches 0, so a flagged
        /// tile holding 0/NaN was written by another module without an amount and is treated as a fresh default stock.
        /// </summary>
        internal static float UsableAmount(float resource, TileFlags present)
        {
            if (resource > 0f && !float.IsInfinity(resource)) return resource;
            return (present & TileFlags.Trees) != 0 ? DefaultTreeWood : DefaultDepositAmount;
        }

        /// <summary>
        /// Removes up to <paramref name="amount"/> from matching tiles, nearest ring first (deterministic order:
        /// ring by ring, rows bottom-up, columns left-right). Tiles reaching zero lose the matching flags.
        /// Returns the amount actually taken; <paramref name="cleared"/> counts exhausted tiles.
        /// </summary>
        internal static float Deplete(WorldMap map, int ox, int oy, int w, int h, int radius, TileFlags mask, float amount, ref TileBounds changed, out int cleared)
        {
            cleared = 0;
            if (!(amount > 0f) || float.IsInfinity(amount)) return 0f;
            float taken = 0f;
            for (int d = 0; d <= radius && amount > 0f; d++)
            {
                int x0 = ox - d, x1 = ox + w - 1 + d, y0 = oy - d, y1 = oy + h - 1 + d;
                for (int y = y0; y <= y1 && amount > 0f; y++)
                {
                    if (y < 0 || y >= map.Height) continue;
                    bool fullRow = d == 0 || y == y0 || y == y1;
                    int step = fullRow ? 1 : x1 - x0; // middle rows of a ring: only the two edge columns
                    for (int x = x0; x <= x1 && amount > 0f; x += step)
                    {
                        if (x < 0 || x >= map.Width) continue;
                        int i = y * map.Width + x;
                        TileFlags present = map.Flags[i] & mask;
                        if (present == 0) continue;
                        float r = UsableAmount(map.Resource[i], present);
                        float take = Math.Min(r, amount);
                        r -= take;
                        amount -= take;
                        taken += take;
                        if (r <= ExhaustedEpsilon)
                        {
                            r = 0f;
                            map.Flags[i] &= ~mask;
                            cleared++;
                            changed.Include(x, y);
                        }
                        map.Resource[i] = r;
                    }
                }
            }
            return taken;
        }
    }
}
