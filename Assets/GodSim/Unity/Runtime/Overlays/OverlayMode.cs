namespace GodSim.Unity
{
    /// <summary>
    /// Data overlay drawn over the terrain by <see cref="MapOverlayRenderer"/>.
    /// Values are explicit so serialized scene/prefab references stay stable if modes are appended.
    /// </summary>
    public enum OverlayMode
    {
        /// <summary>No overlay (renderer and legend hidden).</summary>
        None = 0,
        /// <summary>Owning civilization per tile (categorical, honours civ banner colors).</summary>
        Territory = 1,
        /// <summary>Settlement population spread over its territory (people per tile).</summary>
        PopulationDensity = 2,
        /// <summary>Settlement happiness (diverging: red = unhappy, blue = happy).</summary>
        Happiness = 3,
        /// <summary>Settlement health.</summary>
        Health = 4,
        /// <summary>Settlement faith.</summary>
        Faith = 5,
        /// <summary>Tile fertility (land only).</summary>
        Fertility = 6,
        /// <summary>Remaining harvestable amount on the tile (wood, ore, stone, gold...).</summary>
        Resources = 7,
        /// <summary>Normalized elevation (-1..1).</summary>
        Elevation = 8,
        /// <summary>Tile moisture (land only).</summary>
        Moisture = 9,
        /// <summary>Tile temperature (diverging: blue = cold, red = hot).</summary>
        Temperature = 10,
        /// <summary>Armies, fires and war fronts, spread with distance decay.</summary>
        Danger = 11,
        /// <summary>Gold stockpiled per settlement.</summary>
        Wealth = 12,
        /// <summary>Age of the owning civilization.</summary>
        Age = 13,
        /// <summary>Founded religion of the owning civilization (civ.Memory "rel.founded").</summary>
        Religion = 14,
    }

    /// <summary>Color ramps available to overlays (see <see cref="ColorRamps"/>).</summary>
    public enum ColorRampId
    {
        /// <summary>Perceptually uniform, colorblind-friendly sequential ramp (dark purple to yellow).</summary>
        Viridis = 0,
        /// <summary>Perceptually uniform sequential ramp (black to pale yellow) for intensity/danger.</summary>
        Magma = 1,
        /// <summary>Diverging red (0) - white (0.5) - blue (1).</summary>
        DivergingRedWhiteBlue = 2,
        /// <summary>Diverging blue (0) - white (0.5) - red (1), e.g. cold to hot.</summary>
        DivergingBlueWhiteRed = 3,
        /// <summary>Discrete categories (civilizations, religions).</summary>
        Categorical = 4,
    }
}
