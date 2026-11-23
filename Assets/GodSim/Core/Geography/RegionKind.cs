namespace GodSim.Core.Geography
{
    /// <summary>
    /// Kind of a named geographic region. Values are stable (append only) so tools can store them.
    /// Layers: Continent/Island/Ocean/Sea/Lake are top-level "bodies" (every tile has exactly one);
    /// Bay and the biome features sit inside a body; River systems are the most specific layer.
    /// </summary>
    public enum RegionKind : byte
    {
        Continent = 0,
        Island = 1,
        Ocean = 2,
        Sea = 3,
        Bay = 4,
        Lake = 5,
        MountainRange = 6,
        Hills = 7,
        Forest = 8,
        Jungle = 9,
        Taiga = 10,
        Desert = 11,
        Swamp = 12,
        Marsh = 13,
        Tundra = 14,
        River = 15,
        Plains = 16,
        Volcano = 17
    }

    /// <summary>Which tile-to-region layer a region lives in.</summary>
    public enum RegionLayer : byte
    {
        /// <summary>Landmass or water body; covers every tile.</summary>
        Body = 0,
        /// <summary>Biome feature (forest, peaks, desert...) or a bay; optional per tile.</summary>
        Feature = 1,
        /// <summary>River system (connected River-flagged tiles); optional per tile.</summary>
        River = 2
    }

    /// <summary>Helpers for <see cref="RegionKind"/>.</summary>
    public static class RegionKindUtil
    {
        /// <summary>Number of <see cref="RegionKind"/> values.</summary>
        public const int Count = 18;

        public static bool IsWaterKind(this RegionKind k) =>
            k == RegionKind.Ocean || k == RegionKind.Sea || k == RegionKind.Bay || k == RegionKind.Lake;

        public static bool IsLandmassKind(this RegionKind k) => k == RegionKind.Continent || k == RegionKind.Island;

        /// <summary>True for regions small/specific enough to be a chronicle landmark ("near the Whispering Fen").</summary>
        public static bool IsLandmark(this RegionKind k) => k != RegionKind.Continent && k != RegionKind.Ocean;
    }
}
