using System;

namespace GodSim.Core
{
    /// <summary>
    /// Tile grid stored as struct-of-arrays for cache efficiency and cheap serialization.
    /// Index = y * Width + x. Elevation is normalized to [-1, 1] with sea level at <see cref="SeaLevel"/>.
    /// </summary>
    public sealed class WorldMap
    {
        public const int MinSize = 16;
        public const int MaxSize = 1024;

        public readonly int Width;
        public readonly int Height;

        public readonly float[] Elevation;       // -1..1
        public readonly float[] Moisture;        // 0..1
        public readonly float[] Temperature;     // 0..1 (cold..hot)
        public readonly float[] Fertility;       // 0..1
        public readonly Biome[] Biomes;
        public readonly TileFlags[] Flags;
        /// <summary>Remaining harvestable amount on the tile (wood for Trees, ore/stone/gold/coal/oil for deposits).</summary>
        public readonly float[] Resource;
        /// <summary>Owning settlement id, -1 = unclaimed.</summary>
        public readonly int[] Owner;
        /// <summary>Building occupying the tile, -1 = none.</summary>
        public readonly int[] BuildingAt;

        public float SeaLevel;
        /// <summary>Incremented whenever terrain (elevation/biome/flags) changes; renderers compare to refresh.</summary>
        public int TerrainVersion;

        public WorldMap(int width, int height)
        {
            if (width < MinSize || height < MinSize || width > MaxSize || height > MaxSize)
                throw new ArgumentOutOfRangeException(nameof(width), "Map size must be within [" + MinSize + ", " + MaxSize + "].");
            Width = width;
            Height = height;
            int n = width * height;
            Elevation = new float[n];
            Moisture = new float[n];
            Temperature = new float[n];
            Fertility = new float[n];
            Biomes = new Biome[n];
            Flags = new TileFlags[n];
            Resource = new float[n];
            Owner = new int[n];
            BuildingAt = new int[n];
            for (int i = 0; i < n; i++) { Owner[i] = -1; BuildingAt[i] = -1; }
        }

        public int TileCount => Width * Height;

        public int Index(int x, int y) => y * Width + x;
        public int Index(Vec2i p) => p.Y * Width + p.X;
        public Vec2i PosOf(int index) => new Vec2i(index % Width, index / Width);
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool InBounds(Vec2i p) => InBounds(p.X, p.Y);
        public Vec2i ClampToBounds(Vec2i p) => new Vec2i(MathUtil.Clamp(p.X, 0, Width - 1), MathUtil.Clamp(p.Y, 0, Height - 1));

        public Biome BiomeAt(Vec2i p) => Biomes[Index(p)];
        public bool IsWater(int i) => Biomes[i].IsWater();
        public bool IsWater(Vec2i p) => InBounds(p) && Biomes[Index(p)].IsWater();
        public bool Has(int i, TileFlags f) => (Flags[i] & f) != 0;
        public bool Has(Vec2i p, TileFlags f) => InBounds(p) && (Flags[Index(p)] & f) != 0;
        public void Set(int i, TileFlags f) => Flags[i] |= f;
        public void Clear(int i, TileFlags f) => Flags[i] &= ~f;

        /// <summary>Land, generically buildable biome, no building, no road, not burning.</summary>
        public bool IsFreeBuildable(int i) =>
            Biomes[i].IsBuildable() && BuildingAt[i] < 0 && (Flags[i] & (TileFlags.Road | TileFlags.Burning | TileFlags.Wall)) == 0;

        /// <summary>Counts tiles within Chebyshev radius r of p matching the flag.</summary>
        public int CountFlagInRadius(Vec2i p, int r, TileFlags f)
        {
            int c = 0;
            for (int y = p.Y - r; y <= p.Y + r; y++)
                for (int x = p.X - r; x <= p.X + r; x++)
                    if (InBounds(x, y) && (Flags[Index(x, y)] & f) != 0) c++;
            return c;
        }

        public int CountBiomeInRadius(Vec2i p, int r, Biome b)
        {
            int c = 0;
            for (int y = p.Y - r; y <= p.Y + r; y++)
                for (int x = p.X - r; x <= p.X + r; x++)
                    if (InBounds(x, y) && Biomes[Index(x, y)] == b) c++;
            return c;
        }

        public void MarkTerrainChanged() { unchecked { TerrainVersion++; } }
    }
}
