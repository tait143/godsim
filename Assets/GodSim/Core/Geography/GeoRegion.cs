namespace GodSim.Core.Geography
{
    /// <summary>Inclusive tile-space bounding box of a region.</summary>
    public readonly struct GeoBounds
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MaxX;
        public readonly int MaxY;

        public GeoBounds(int minX, int minY, int maxX, int maxY) { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
        public bool Contains(Vec2i p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
        public override string ToString() => "[" + MinX + "," + MinY + " .. " + MaxX + "," + MaxY + "]";
    }

    /// <summary>
    /// One named region of the atlas. Instances are owned by a <see cref="GeographyIndex"/> and are read-only
    /// for consumers (the only mutation is <see cref="PlaceNames.Rename"/>).
    /// </summary>
    public sealed class GeoRegion
    {
        /// <summary>Index into <see cref="GeographyIndex.Regions"/>.</summary>
        public int Id { get; internal set; }
        public RegionKind Kind { get; internal set; }
        public RegionLayer Layer { get; internal set; }
        /// <summary>Label without a leading article, unique within the world (e.g. "Ashen Peaks", "Lake Orneth").</summary>
        public string Name { get; internal set; }
        /// <summary>Name as used in prose, with the article English expects (e.g. "the Ashen Peaks", "Lake Orneth").</summary>
        public string Phrase { get; internal set; }
        /// <summary>The generated (atlas) name, kept when a race renames the region.</summary>
        public string BaseName { get; internal set; }
        /// <summary>Race that renamed the region via <see cref="PlaceNames.Rename"/>, or -1.</summary>
        public int NativeRace { get; internal set; } = -1;
        /// <summary>True once <see cref="PlaceNames.Rename"/> replaced the generated name.</summary>
        public bool IsRenamed => Renamed;
        internal bool Renamed;
        public int TileCount { get; internal set; }
        public GeoBounds Bounds { get; internal set; }
        /// <summary>Mean tile position (rounded). May lie outside a concave region; see <see cref="Anchor"/>.</summary>
        public Vec2i Centroid { get; internal set; }
        /// <summary>The region tile closest to <see cref="Centroid"/>; always inside the region (good for map labels).</summary>
        public Vec2i Anchor { get; internal set; }
        /// <summary>Containing region id (feature inside a continent, bay inside an ocean), or -1 for top-level bodies.</summary>
        public int ParentId { get; internal set; } = -1;
        public Biome DominantBiome { get; internal set; }
        /// <summary>0 at the equator (map middle row) .. 1 at the poles (top/bottom rows).</summary>
        public float Latitude { get; internal set; }
        /// <summary>True when any tile lies on the map border (used to tell oceans from lakes).</summary>
        public bool TouchesMapEdge { get; internal set; }
        /// <summary>Deterministic per-region seed = Combine(Combine(worldSeed, centroid), kind). Stable across rebuilds.</summary>
        public ulong NameSeed { get; internal set; }

        public bool IsWater => Kind.IsWaterKind();
        public bool IsLandmass => Kind.IsLandmassKind();

        public override string ToString() => Kind + " #" + Id + " '" + Name + "' (" + TileCount + " tiles)";
    }
}
