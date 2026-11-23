using System;
using System.Text;

namespace GodSim.Core.World
{
    /// <summary>Summary of a generated map (biome histogram, water ratios, rivers, deposits) for tests and tools.</summary>
    public sealed class MapStatistics
    {
        /// <summary>Map width in tiles.</summary>
        public int Width;
        /// <summary>Map height in tiles.</summary>
        public int Height;
        /// <summary>Total tile count.</summary>
        public int TileCount;
        /// <summary>Tile count per biome, indexed by (int)Biome.</summary>
        public readonly int[] BiomeCounts = new int[Counts.Biomes];
        /// <summary>DeepOcean + ShallowWater tiles.</summary>
        public int OceanTiles;
        /// <summary>Lake tiles.</summary>
        public int LakeTiles;
        /// <summary>Non-water tiles.</summary>
        public int LandTiles;
        /// <summary>Land tiles whose biome is generically buildable.</summary>
        public int BuildableTiles;
        /// <summary>OceanTiles / TileCount.</summary>
        public float OceanRatio;
        /// <summary>(OceanTiles + LakeTiles) / TileCount.</summary>
        public float WaterRatio;
        /// <summary>Tiles flagged River.</summary>
        public int RiverTiles;
        /// <summary>Tiles flagged Trees.</summary>
        public int TreeTiles;
        /// <summary>Tiles flagged Coastal.</summary>
        public int CoastalTiles;
        /// <summary>Tiles flagged FertileSoil.</summary>
        public int FertileTiles;
        /// <summary>Tiles flagged Sacred.</summary>
        public int SacredTiles;
        /// <summary>Tiles flagged Ruins.</summary>
        public int RuinTiles;
        /// <summary>Tiles flagged OreDeposit.</summary>
        public int OreDeposits;
        /// <summary>Tiles flagged StoneDeposit.</summary>
        public int StoneDeposits;
        /// <summary>Tiles flagged GoldDeposit.</summary>
        public int GoldDeposits;
        /// <summary>Tiles flagged CoalDeposit.</summary>
        public int CoalDeposits;
        /// <summary>Tiles flagged OilDeposit.</summary>
        public int OilDeposits;
        /// <summary>Mean elevation of land tiles.</summary>
        public float MeanLandElevation;
        /// <summary>Mean fertility of land tiles.</summary>
        public float MeanLandFertility;

        /// <summary>Number of biomes with at least one tile.</summary>
        public int DistinctBiomes
        {
            get { int c = 0; for (int i = 0; i < BiomeCounts.Length; i++) if (BiomeCounts[i] > 0) c++; return c; }
        }

        /// <summary>Tile count of a biome.</summary>
        public int Count(Biome b) => BiomeCounts[(int)b];

        /// <summary>Fraction of all tiles with the biome.</summary>
        public float Fraction(Biome b) => TileCount > 0 ? BiomeCounts[(int)b] / (float)TileCount : 0f;

        /// <summary>Total deposit tiles of every kind.</summary>
        public int TotalDeposits => OreDeposits + StoneDeposits + GoldDeposits + CoalDeposits + OilDeposits;

        /// <summary>Computes statistics for a map (O(tiles), no per-tile allocation).</summary>
        public static MapStatistics Compute(WorldMap map)
        {
            Guard.NotNull(map, nameof(map));
            var s = new MapStatistics { Width = map.Width, Height = map.Height, TileCount = map.TileCount };
            double elev = 0, fert = 0;
            for (int i = 0; i < map.TileCount; i++)
            {
                Biome b = map.Biomes[i];
                int bi = (int)b;
                if (bi >= 0 && bi < s.BiomeCounts.Length) s.BiomeCounts[bi]++;
                if (b == Biome.DeepOcean || b == Biome.ShallowWater) s.OceanTiles++;
                else if (b == Biome.Lake) s.LakeTiles++;
                else
                {
                    s.LandTiles++;
                    elev += map.Elevation[i];
                    fert += map.Fertility[i];
                    if (b.IsBuildable()) s.BuildableTiles++;
                }
                TileFlags f = map.Flags[i];
                if ((f & TileFlags.River) != 0) s.RiverTiles++;
                if ((f & TileFlags.Trees) != 0) s.TreeTiles++;
                if ((f & TileFlags.Coastal) != 0) s.CoastalTiles++;
                if ((f & TileFlags.FertileSoil) != 0) s.FertileTiles++;
                if ((f & TileFlags.Sacred) != 0) s.SacredTiles++;
                if ((f & TileFlags.Ruins) != 0) s.RuinTiles++;
                if ((f & TileFlags.OreDeposit) != 0) s.OreDeposits++;
                if ((f & TileFlags.StoneDeposit) != 0) s.StoneDeposits++;
                if ((f & TileFlags.GoldDeposit) != 0) s.GoldDeposits++;
                if ((f & TileFlags.CoalDeposit) != 0) s.CoalDeposits++;
                if ((f & TileFlags.OilDeposit) != 0) s.OilDeposits++;
            }
            if (s.TileCount > 0)
            {
                s.OceanRatio = s.OceanTiles / (float)s.TileCount;
                s.WaterRatio = (s.OceanTiles + s.LakeTiles) / (float)s.TileCount;
            }
            if (s.LandTiles > 0)
            {
                s.MeanLandElevation = (float)(elev / s.LandTiles);
                s.MeanLandFertility = (float)(fert / s.LandTiles);
            }
            return s;
        }

        /// <summary>Multi-line human-readable report.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Map ").Append(Width).Append('x').Append(Height)
              .Append(" ocean=").Append(OceanRatio.ToString("0.000"))
              .Append(" water=").Append(WaterRatio.ToString("0.000"))
              .Append(" land=").Append(LandTiles)
              .Append(" buildable=").Append(BuildableTiles)
              .Append(" distinctBiomes=").Append(DistinctBiomes).Append('\n');
            for (int i = 0; i < BiomeCounts.Length; i++)
                sb.Append("  ").Append(((Biome)i).ToString().PadRight(13)).Append(BiomeCounts[i].ToString().PadLeft(7))
                  .Append("  ").Append((TileCount > 0 ? BiomeCounts[i] * 100f / TileCount : 0f).ToString("0.0")).Append("%\n");
            sb.Append("  rivers=").Append(RiverTiles).Append(" trees=").Append(TreeTiles).Append(" coastal=").Append(CoastalTiles)
              .Append(" fertile=").Append(FertileTiles).Append(" sacred=").Append(SacredTiles).Append(" ruins=").Append(RuinTiles).Append('\n');
            sb.Append("  ore=").Append(OreDeposits).Append(" stone=").Append(StoneDeposits).Append(" gold=").Append(GoldDeposits)
              .Append(" coal=").Append(CoalDeposits).Append(" oil=").Append(OilDeposits)
              .Append(" meanLandElev=").Append(MeanLandElevation.ToString("0.000"))
              .Append(" meanLandFert=").Append(MeanLandFertility.ToString("0.000"));
            return sb.ToString();
        }
    }
}
