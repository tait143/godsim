using System;

namespace GodSim.Core.AI
{
    /// <summary>Terrain features present in (or near) a settlement's territory.</summary>
    [Flags]
    public enum TerritoryFeature : ushort
    {
        /// <summary>Nothing.</summary>
        None = 0,
        /// <summary>Water (ocean, lake or river) within reach of the territory.</summary>
        Water = 1 << 0,
        /// <summary>A coastal land tile inside the territory.</summary>
        Coast = 1 << 1,
        /// <summary>A river tile within reach.</summary>
        River = 1 << 2,
        /// <summary>Ore deposit within reach.</summary>
        Ore = 1 << 3,
        /// <summary>Stone deposit within reach.</summary>
        Stone = 1 << 4,
        /// <summary>Gold deposit within reach.</summary>
        Gold = 1 << 5,
        /// <summary>Coal deposit within reach.</summary>
        Coal = 1 << 6,
        /// <summary>Oil deposit within reach.</summary>
        Oil = 1 << 7,
        /// <summary>Harvestable trees within reach.</summary>
        Trees = 1 << 8,
        /// <summary>Fertile buildable land inside the territory.</summary>
        Fertile = 1 << 9,
        /// <summary>Hills tiles inside the territory.</summary>
        Hills = 1 << 10,
        /// <summary>Mountain tiles within reach.</summary>
        Mountain = 1 << 11
    }

    /// <summary>Result of a territory scan: which features exist and how many free buildable tiles remain.</summary>
    [Serializable]
    public readonly struct TerritoryFeatures
    {
        /// <summary>Features present.</summary>
        public readonly TerritoryFeature Flags;
        /// <summary>Free buildable tiles inside the territory.</summary>
        public readonly int FreeTiles;
        /// <summary>Land tiles inside the territory.</summary>
        public readonly int LandTiles;

        /// <summary>Creates a result.</summary>
        public TerritoryFeatures(TerritoryFeature flags, int freeTiles, int landTiles)
        {
            Flags = flags;
            FreeTiles = Math.Max(0, freeTiles);
            LandTiles = Math.Max(0, landTiles);
        }

        /// <summary>True if every feature in <paramref name="f"/> is present.</summary>
        public bool Has(TerritoryFeature f) => (Flags & f) == f;

        /// <summary>True if any feature in <paramref name="f"/> is present.</summary>
        public bool HasAny(TerritoryFeature f) => (Flags & f) != 0;

        /// <inheritdoc />
        public override string ToString() => Flags + " (free " + FreeTiles + "/" + LandTiles + ")";
    }
}
