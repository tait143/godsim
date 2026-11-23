namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Neighborhood measurements around a candidate building footprint, gathered in a single scan by
    /// <see cref="PlacementRules.Probe"/>. Distances are Chebyshev distances to the footprint rectangle
    /// (0 = inside the footprint). Used both for hard placement requirements and for soft scoring.
    /// </summary>
    public struct SiteProbe
    {
        /// <summary>Footprint area in tiles.</summary>
        public int Area;
        /// <summary>Water-biome or River-flag tiles within 3.</summary>
        public int WaterOrRiverNear3;
        /// <summary>Water-biome tiles (ocean, shallow, lake) within 3.</summary>
        public int WaterNear3;
        /// <summary>River-flag tiles within 3.</summary>
        public int RiverNear3;
        /// <summary>Mountain-biome tiles within 3.</summary>
        public int MountainNear3;
        /// <summary>Hills-biome tiles within 3 (footprint included).</summary>
        public int HillsNear3;
        /// <summary>Trees-flag tiles within 4.</summary>
        public int TreesNear4;
        /// <summary>OreDeposit tiles within 3.</summary>
        public int OreNear3;
        /// <summary>StoneDeposit tiles within 3.</summary>
        public int StoneNear3;
        /// <summary>GoldDeposit tiles within 3.</summary>
        public int GoldNear3;
        /// <summary>CoalDeposit tiles within 3.</summary>
        public int CoalNear3;
        /// <summary>OilDeposit tiles within 3.</summary>
        public int OilNear3;
        /// <summary>Water tiles orthogonally adjacent to the footprint.</summary>
        public int AdjacentWater;
        /// <summary>Footprint tiles carrying the Coastal flag.</summary>
        public int CoastalInFootprint;
        /// <summary>Footprint tiles with the Hills biome.</summary>
        public int HillsInFootprint;
        /// <summary>Sum of Fertility over the footprint.</summary>
        public float FertilitySum;
        /// <summary>Footprint tiles carrying the FertileSoil flag.</summary>
        public int FertileSoilInFootprint;
        /// <summary>Footprint tiles carrying the Farmland flag.</summary>
        public int FarmlandInFootprint;
        /// <summary>Road tiles orthogonally adjacent to the footprint.</summary>
        public int RoadAdjacent;
        /// <summary>Road tiles at distance 1..2.</summary>
        public int RoadNear2;
        /// <summary>Building-occupied tiles at distance 1 (diagonals included).</summary>
        public int OccupiedAdjacent;
        /// <summary>Building-occupied tiles at distance 1..2.</summary>
        public int OccupiedNear2;

        /// <summary>Average fertility over the footprint (0 when the area is 0).</summary>
        public float AverageFertility => Area > 0 ? FertilitySum / Area : 0f;
    }
}
