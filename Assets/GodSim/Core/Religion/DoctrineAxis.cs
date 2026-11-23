namespace GodSim.Core.Religion
{
    /// <summary>
    /// The four axes of a mortal faith. Every founded faith holds exactly one doctrine per axis.
    /// Values are persisted indirectly (doctrine ids live in <c>Civilization.Memory</c>); do not reorder.
    /// </summary>
    public enum DoctrineAxis : byte
    {
        /// <summary>What the faithful venerate (the sun, ancestors, nature, the stars).</summary>
        Worship = 0,
        /// <summary>How the faithful ought to live.</summary>
        Ethos = 1,
        /// <summary>The ritual life of the faith.</summary>
        Practice = 2,
        /// <summary>How mortals interpret the player-god's character.</summary>
        View = 3
    }
}
