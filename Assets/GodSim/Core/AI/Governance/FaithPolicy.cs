namespace GodSim.Core.AI
{
    /// <summary>State stance toward religion. Stored as a float index in civ memory key "gov.faithPolicy".</summary>
    public enum FaithPolicy : byte
    {
        /// <summary>The state keeps temples at arm's length (slightly discourages faith buildings).</summary>
        Secular = 0,
        /// <summary>Faiths are tolerated and mildly supported.</summary>
        Tolerant = 1,
        /// <summary>An official state faith: temples are funded.</summary>
        StateFaith = 2,
        /// <summary>Zealotry: the faith is the law; temples and priests above all.</summary>
        Zealotry = 3
    }
}
