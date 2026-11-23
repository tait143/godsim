namespace GodSim.Core.Settlements
{
    /// <summary>Accumulates the bounding box of tiles whose flags were changed (roads, fields) so one
    /// <see cref="TerrainChangedEvent"/> can be published per batch.</summary>
    public struct TerrainDirtyRect
    {
        /// <summary>True once at least one tile was included.</summary>
        public bool Any;
        /// <summary>Inclusive bounds.</summary>
        public int MinX, MinY, MaxX, MaxY;

        /// <summary>Grows the rectangle to contain (x, y).</summary>
        public void Include(int x, int y)
        {
            if (!Any)
            {
                Any = true;
                MinX = MaxX = x;
                MinY = MaxY = y;
                return;
            }
            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
        }
    }
}
