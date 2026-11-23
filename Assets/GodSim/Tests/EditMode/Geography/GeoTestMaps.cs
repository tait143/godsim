using GodSim.Core;

namespace GodSim.Tests.Geography
{
    /// <summary>Hand-built maps for geography tests.</summary>
    public static class GeoTestMaps
    {
        public static WorldMap Filled(int size, Biome fill)
        {
            var map = new WorldMap(size, size);
            for (int i = 0; i < map.TileCount; i++)
            {
                map.Biomes[i] = fill;
                map.Elevation[i] = fill.IsWater() ? -0.3f : 0.2f;
            }
            return map;
        }

        /// <summary>Paints an inclusive rectangle.</summary>
        public static void Rect(WorldMap map, int x0, int y0, int x1, int y1, Biome b)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = map.Index(x, y);
                    map.Biomes[i] = b;
                    map.Elevation[i] = b.IsWater() ? -0.3f : 0.2f;
                }
        }

        public static void FlagLine(WorldMap map, int x0, int x1, int y, TileFlags f)
        {
            for (int x = x0; x <= x1; x++) map.Set(map.Index(x, y), f);
        }

        // Layout of the standard test world (64x64, ocean everywhere else):
        public static readonly Vec2i LakeTile = new Vec2i(12, 12);
        public static readonly Vec2i MountainTile = new Vec2i(21, 21);
        public static readonly Vec2i ForestTile = new Vec2i(29, 9);
        public static readonly Vec2i NextToForest = new Vec2i(24, 9);
        public static readonly Vec2i RiverTileA = new Vec2i(8, 30);
        public static readonly Vec2i RiverTileB = new Vec2i(30, 30);
        public static readonly Vec2i PlainsTile = new Vec2i(8, 20);
        public static readonly Vec2i SmallMountainTile = new Vec2i(6, 6);
        public static readonly Vec2i IslandTile = new Vec2i(48, 48);
        public static readonly Vec2i OceanTile = new Vec2i(60, 2);

        /// <summary>
        /// Two islands in an ocean: a 32x32 grassland continent (x,y 4..35) holding a 5x5 lake, a 12-tile mountain
        /// cluster, a 49-tile forest, a 5-tile (too small) mountain blob and a 23-tile river; plus a 6x6 beach island.
        /// </summary>
        public static WorldMap Standard()
        {
            var map = Filled(64, Biome.DeepOcean);
            Rect(map, 4, 4, 35, 35, Biome.Grassland);
            Rect(map, 10, 10, 14, 14, Biome.Lake);
            Rect(map, 20, 20, 23, 22, Biome.Mountain);
            Rect(map, 26, 6, 32, 12, Biome.Forest);
            Rect(map, 5, 5, 6, 6, Biome.Mountain);   // 4 tiles
            Rect(map, 7, 5, 7, 5, Biome.Mountain);   // + 1 = 5 tiles: below the range threshold
            FlagLine(map, 8, 30, 30, TileFlags.River);
            Rect(map, 46, 46, 51, 51, Biome.Beach);
            return map;
        }
    }
}
