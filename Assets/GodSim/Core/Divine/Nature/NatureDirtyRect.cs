namespace GodSim.Core.Divine
{
    /// <summary>
    /// Accumulates the bounding box of tiles changed by a natural process so a single
    /// <see cref="TerrainChangedEvent"/> can be published for a whole run instead of one per tile.
    /// A default-initialized value is empty.
    /// </summary>
    public struct NatureDirtyRect
    {
        /// <summary>Inclusive bounds (valid only while <see cref="Any"/> is true).</summary>
        public int MinX, MinY, MaxX, MaxY;

        /// <summary>True once at least one tile has been added.</summary>
        public bool Any;

        /// <summary>Adds a single tile coordinate.</summary>
        public void Add(int x, int y)
        {
            if (!Any)
            {
                MinX = MaxX = x;
                MinY = MaxY = y;
                Any = true;
                return;
            }
            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
        }

        /// <summary>Adds the tile with the given map index.</summary>
        public void AddIndex(WorldMap map, int index)
        {
            if (map == null || index < 0 || index >= map.TileCount) return;
            Add(index % map.Width, index / map.Width);
        }

        /// <summary>Adds the square of Chebyshev radius r around c, clipped to the map.</summary>
        public void AddSquare(WorldMap map, Vec2i c, int r)
        {
            if (map == null) return;
            if (r < 0) r = 0;
            int x0 = MathUtil.Clamp(c.X - r, 0, map.Width - 1), x1 = MathUtil.Clamp(c.X + r, 0, map.Width - 1);
            int y0 = MathUtil.Clamp(c.Y - r, 0, map.Height - 1), y1 = MathUtil.Clamp(c.Y + r, 0, map.Height - 1);
            Add(x0, y0);
            Add(x1, y1);
        }

        /// <summary>Merges another rectangle into this one.</summary>
        public void Merge(NatureDirtyRect other)
        {
            if (!other.Any) return;
            Add(other.MinX, other.MinY);
            Add(other.MaxX, other.MaxY);
        }

        /// <summary>Converts the bounds to the shared event type.</summary>
        public TerrainChangedEvent ToEvent() => new TerrainChangedEvent { MinX = MinX, MinY = MinY, MaxX = MaxX, MaxY = MaxY };
    }
}
