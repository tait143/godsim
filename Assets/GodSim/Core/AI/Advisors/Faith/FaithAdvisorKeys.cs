namespace GodSim.Core.AI
{
    /// <summary>
    /// Settlement blackboard keys owned by <see cref="FaithAdvisor"/> (all floats, persisted with the save).
    /// Ticks are stored as floats (exact below 16.7M ticks); <see cref="Never"/> marks "no such event yet".
    /// </summary>
    public static class FaithAdvisorKeys
    {
        /// <summary>Sentinel tick for "never happened".</summary>
        public const float Never = -1000000f;

        // --- divine acts perceived by this settlement
        public const string BlessTick = "adv.faith.blessTick";   // tick of the most recent blessing nearby
        public const string BlessKind = "adv.faith.blessKind";   // GodPowerType of that blessing
        public const string BlessMag = "adv.faith.blessMag";     // 0..1 perceived strength (stacks a little)
        public const string BlessSeen = "adv.faith.blessSeen";   // last blessing tick already digested
        public const string WrathTick = "adv.faith.wrathTick";   // tick of the most recent wrath nearby
        public const string WrathKind = "adv.faith.wrathKind";   // GodPowerType, or 100 + DisasterType
        public const string WrathMag = "adv.faith.wrathMag";     // 0..1 perceived strength
        public const string WrathSeen = "adv.faith.wrathSeen";   // last wrath tick already digested
        public const string WrathCount = "adv.faith.wrathCount"; // recent wrath events (decays ~2 years)

        // --- feelings & follow-up
        public const string Resentment = "adv.faith.resent";     // 0..1 grudge of an impious people
        public const string Offered = "adv.faith.offered";       // tick a house of worship was last raised (fulfils thanks/appeasement)
        public const string LostTick = "adv.faith.lostTick";     // tick a house of worship was destroyed
        public const string LostType = "adv.faith.lostType";     // its BuildingType
        public const string Sacred = "adv.faith.sacred";         // cached count of Sacred tiles in territory
        public const string SacredScan = "adv.faith.sacredScan"; // tick of the last territory scan
        public const string LastPop = "adv.faith.pop";           // population at the last run (growth anticipation)
        public const string Motive = "adv.faith.motive";         // dominant FaithAdvisorMotive (UI/debug)

        /// <summary>Offset added to a DisasterType when stored as a wrath kind.</summary>
        public const int DisasterKindOffset = 100;
    }

    /// <summary>Dominant reason behind the faith advisor's latest advice (stored in <see cref="FaithAdvisorKeys.Motive"/>).</summary>
    public enum FaithAdvisorMotive : byte
    {
        None = 0, Coverage = 1, Thanks = 2, Appease = 3, Resentment = 4, HolySite = 5, Rebuild = 6, Wonder = 7,
        Revival = 8, Placate = 9, Praise = 10, Growth = 11
    }
}
