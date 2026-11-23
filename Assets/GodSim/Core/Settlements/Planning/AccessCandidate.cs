namespace GodSim.Core.Settlements
{
    /// <summary>One possible entrance of a building footprint, ranked by <see cref="PlacementRules.CollectAccessTiles"/>.</summary>
    public struct AccessCandidate
    {
        /// <summary>The entrance tile (orthogonally adjacent to the footprint).</summary>
        public Vec2i Tile;
        /// <summary>Building facing toward this tile (0 = N/+Y, 1 = E/+X, 2 = S/-Y, 3 = W/-X).</summary>
        public byte Rotation;
        /// <summary>Lower is better: road-network distance first, then Manhattan distance to the target.</summary>
        public int Rank;
    }
}
