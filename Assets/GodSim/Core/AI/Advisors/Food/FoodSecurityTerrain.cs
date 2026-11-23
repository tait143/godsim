using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// What the land around a settlement offers for food (reused instance; one bounded scan per advise). Only land the
    /// settlement owns or could still claim counts toward soil; water, rivers and woods within the radius count as reachable.
    /// </summary>
    public sealed class FoodSecurityTerrain
    {
        public const int MinRadius = 4;
        public const int MaxRadius = 14;

        public int LandTiles, FreeLand, FertileTiles, Water, River, Coastal, Trees, Hills, Mountain;
        public int Ore, Stone, Gold, Coal, Oil;
        public float FertilitySum;

        public float AverageFertility => LandTiles > 0 ? MathUtil.Clamp01(FertilitySum / LandTiles) : 0f;

        public void Clear()
        {
            LandTiles = FreeLand = FertileTiles = Water = River = Coastal = Trees = Hills = Mountain = 0;
            Ore = Stone = Gold = Coal = Oil = 0;
            FertilitySum = 0f;
        }

        public void Scan(WorldMap map, Settlement s)
        {
            Clear();
            if (map == null || s == null) return;
            int r = MathUtil.Clamp(s.TerritoryRadius + 2, MinRadius, MaxRadius), r2 = r * r;
            int cx = s.Center.X, cy = s.Center.Y;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    if (map.Biomes[i].IsWater()) { Water++; continue; }
                    TileFlags fl = map.Flags[i];
                    if ((fl & TileFlags.River) != 0) River++;
                    if ((fl & TileFlags.Coastal) != 0) Coastal++;
                    if ((fl & TileFlags.Trees) != 0) Trees++;
                    if ((fl & TileFlags.OreDeposit) != 0) Ore++;
                    if ((fl & TileFlags.StoneDeposit) != 0) Stone++;
                    if ((fl & TileFlags.GoldDeposit) != 0) Gold++;
                    if ((fl & TileFlags.CoalDeposit) != 0) Coal++;
                    if ((fl & TileFlags.OilDeposit) != 0) Oil++;
                    Biome b = map.Biomes[i];
                    if (b == Biome.Hills) Hills++;
                    else if (b == Biome.Mountain) Mountain++;
                    int owner = map.Owner[i];
                    if (owner != s.Id && owner >= 0) continue;
                    LandTiles++;
                    FertilitySum += MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i]));
                    if ((fl & (TileFlags.FertileSoil | TileFlags.Farmland)) != 0) FertileTiles++;
                    if (map.IsFreeBuildable(i)) FreeLand++;
                }
        }

        /// <summary>
        /// Expected terrain yield factor (0 = cannot work here, 1 = baseline, up to ~1.3) for a food building's placement
        /// rule. Hard "Near*" requirements gate to 0; Fertile buildings are judged by soil, fishing by water, hunting by woods.
        /// </summary>
        public float FitFor(PlacementRule rule, int footprintTiles)
        {
            const PlacementRule waterRules = PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.NearRiver;
            if ((rule & PlacementRule.NearWater) != 0 && Water + River == 0) return 0f;
            if ((rule & PlacementRule.Coastal) != 0 && Coastal == 0 && Water == 0) return 0f;
            if ((rule & PlacementRule.NearRiver) != 0 && River == 0) return 0f;
            if ((rule & PlacementRule.NearForest) != 0 && Trees == 0) return 0f;
            if ((rule & PlacementRule.OnHills) != 0 && Hills == 0) return 0f;
            if ((rule & PlacementRule.NearMountain) != 0 && Mountain == 0) return 0f;
            if ((rule & PlacementRule.NearOre) != 0 && Ore == 0) return 0f;
            if ((rule & PlacementRule.NearStone) != 0 && Stone == 0) return 0f;
            if ((rule & PlacementRule.NearGold) != 0 && Gold == 0) return 0f;
            if ((rule & PlacementRule.NearCoal) != 0 && Coal == 0) return 0f;
            if ((rule & PlacementRule.NearOil) != 0 && Oil == 0) return 0f;

            float fit;
            if ((rule & PlacementRule.Fertile) != 0)
            {
                float share = LandTiles > 0 ? FertileTiles / (float)LandTiles : 0f;
                fit = MathUtil.Clamp(0.3f + 1.1f * AverageFertility + 0.4f * share, 0f, 1.3f);
            }
            else if ((rule & waterRules) != 0) fit = MathUtil.Clamp(0.35f + (Water + River) / 16f, 0f, 1.2f);
            else if ((rule & PlacementRule.NearForest) != 0) fit = MathUtil.Clamp(0.3f + Trees / 30f, 0f, 1.15f);
            else fit = 1f;

            if (FreeLand < Math.Max(1, footprintTiles) * 2) fit *= 0.3f; // no room left to put it anywhere sensible
            return fit;
        }
    }
}
