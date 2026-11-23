namespace GodSim.Core.AI
{
    /// <summary>How a civilization feels about the player-god, as summarized for the UI.</summary>
    public enum GodMood : byte
    {
        /// <summary>Barely notices the heavens.</summary>
        Indifferent = 0,
        /// <summary>Remembers gifts with thanks.</summary>
        Grateful = 1,
        /// <summary>Overwhelming love; the god can do no wrong.</summary>
        Adoring = 2,
        /// <summary>Love and fear in balance: awe.</summary>
        Reverent = 3,
        /// <summary>Cannot read the god's will (erratic god or lingering fear).</summary>
        Uneasy = 4,
        /// <summary>Terrified, appeasing.</summary>
        Fearful = 5,
        /// <summary>Sees the god as a tyrant.</summary>
        Resentful = 6,
    }
}
