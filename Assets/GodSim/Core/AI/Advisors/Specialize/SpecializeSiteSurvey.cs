using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Allocation-free terrain survey of a settlement's hinterland (tiles it owns or nobody owns, within a radius of its
    /// center). Produces per-role terrain richness in [0, 1] and the set of hard placement flags the area can satisfy.
    /// </summary>
    public sealed class SpecializeSiteSurvey
    {
        public int Land, Water, Coastal, River, Trees, Ore, Stone, Gold, Coal, Oil, Hills, Mountain, Sacred, FertileSoil, FertileTiles;
        public float FertilitySum;

        /// <summary>Hard placement flags satisfiable somewhere in the surveyed area.</summary>
        public PlacementRule Satisfiable;

        /// <summary>Terrain richness per role (index = (int)SpecializeRole), 0..1.</summary>
        public readonly float[] Richness = new float[SpecializeRoles.Count];

        public void Scan(WorldMap map, Settlement s)
        {
            Land = Water = Coastal = River = Trees = Ore = Stone = Gold = Coal = Oil = Hills = Mountain = Sacred = FertileSoil = FertileTiles = 0;
            FertilitySum = 0f;
            Satisfiable = PlacementRule.Central | PlacementRule.Outskirts | PlacementRule.Isolated;
            for (int i = 0; i < Richness.Length; i++) Richness[i] = 0f;
            if (map == null || s == null || !map.InBounds(s.Center)) return;

            int r = MathUtil.Clamp(s.TerritoryRadius, 4, 14) + 1;
            int cx = s.Center.X, cy = s.Center.Y, r2 = r * r;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= map.Height) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= map.Width) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    int owner = map.Owner[i];
                    if (owner >= 0 && owner != s.Id) continue; // a neighbour's land is not ours to exploit
                    var b = map.Biomes[i];
                    if (b.IsWater()) { Water++; continue; }
                    Land++;
                    var f = map.Flags[i];
                    float fert = MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i]));
                    FertilitySum += fert;
                    if (fert >= 0.5f) FertileTiles++;
                    if ((f & TileFlags.FertileSoil) != 0) FertileSoil++;
                    if ((f & TileFlags.Coastal) != 0) Coastal++;
                    if ((f & TileFlags.River) != 0) River++;
                    if ((f & TileFlags.Trees) != 0) Trees++;
                    if ((f & TileFlags.OreDeposit) != 0) Ore++;
                    if ((f & TileFlags.StoneDeposit) != 0) Stone++;
                    if ((f & TileFlags.GoldDeposit) != 0) Gold++;
                    if ((f & TileFlags.CoalDeposit) != 0) Coal++;
                    if ((f & TileFlags.OilDeposit) != 0) Oil++;
                    if ((f & TileFlags.Sacred) != 0) Sacred++;
                    if (b == Biome.Hills) Hills++;
                    else if (b == Biome.Mountain) Mountain++;
                }
            }

            if (Water + River + Coastal > 0) Satisfiable |= PlacementRule.NearWater;
            if (Coastal > 0 || Water > 0) Satisfiable |= PlacementRule.Coastal;
            if (Hills > 0) Satisfiable |= PlacementRule.OnHills;
            if (Mountain > 0) Satisfiable |= PlacementRule.NearMountain;
            if (Trees >= 3) Satisfiable |= PlacementRule.NearForest;
            if (Ore > 0) Satisfiable |= PlacementRule.NearOre;
            if (Stone > 0) Satisfiable |= PlacementRule.NearStone;
            if (Gold > 0) Satisfiable |= PlacementRule.NearGold;
            if (Coal > 0) Satisfiable |= PlacementRule.NearCoal;
            if (Oil > 0) Satisfiable |= PlacementRule.NearOil;
            if (River > 0) Satisfiable |= PlacementRule.NearRiver;
            if (FertileSoil > 0 || FertileTiles >= 9) Satisfiable |= PlacementRule.Fertile;

            if (Land <= 0) return;
            float land = Land, total = Land + Water;
            float avgFert = FertilitySum / land;
            float fertileFrac = FertileSoil / land;
            float waterFrac = Water / total;
            float riverFrac = River / land;
            float hillsFrac = Hills / land, mountainFrac = Mountain / land;
            float treeFrac = Trees / land;
            float depFrac = (Ore + Stone * 0.7f + Gold * 1.2f + Coal * 0.8f + Oil * 0.8f) / land;

            float food = 0.75f * avgFert + 0.6f * fertileFrac + 0.25f * Math.Min(1f, waterFrac * 4f) + 0.2f * Math.Min(1f, riverFrac * 5f)
                         - 0.3f * mountainFrac - 0.15f * hillsFrac;
            float wood = treeFrac * 2.5f;
            float mine = depFrac * 5f + hillsFrac * 0.35f + mountainFrac * 0.5f;
            Richness[(int)SpecializeRole.Breadbasket] = MathUtil.Clamp01(food);
            Richness[(int)SpecializeRole.TimberTown] = MathUtil.Clamp01(wood);
            Richness[(int)SpecializeRole.MiningTown] = MathUtil.Clamp01(mine);
            Richness[(int)SpecializeRole.ForgeTown] = MathUtil.Clamp01(0.2f + 0.5f * MathUtil.Clamp01(mine) + 0.3f * MathUtil.Clamp01(wood));
            Richness[(int)SpecializeRole.MarketTown] = MathUtil.Clamp01((Coastal > 0 || Water > 0 ? 0.6f : 0f) + (River > 0 ? 0.4f : 0f));
            Richness[(int)SpecializeRole.TempleCity] = MathUtil.Clamp01(Sacred * 0.2f + mountainFrac * 0.5f + 0.1f);
            Richness[(int)SpecializeRole.ScholarCity] = 0.2f;
        }
    }
}
