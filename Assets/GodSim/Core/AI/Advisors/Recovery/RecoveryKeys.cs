namespace GodSim.Core.AI
{
    /// <summary>Kinds of lasting "scars" a catastrophe leaves; each drives a different mitigation lesson.</summary>
    public enum RecoveryScar : byte { Fire = 0, Flood = 1, War = 2, Famine = 3, Plague = 4, Drought = 5, Divine = 6, Quake = 7 }

    /// <summary>The catastrophe a settlement is recovering from (names the recovery in reason strings).</summary>
    public enum RecoveryCatastrophe : byte
    {
        None = 0, Fire = 1, Flood = 2, War = 3, DivineWrath = 4, Earthquake = 5, Famine = 6,
        Plague = 7, Storm = 8, Meteor = 9, Volcano = 10, Drought = 11, Disaster = 12
    }

    /// <summary>Priority tier of a lost building in the rebuild plan (lower = rebuilt first).</summary>
    public enum RecoveryTier : byte { TownCenter = 0, Dwelling = 1, Food = 2, Storage = 3, Other = 4 }

    /// <summary>
    /// Blackboard keys owned by the disaster-recovery advisor. Everything lives under <c>adv.rec.</c> in
    /// <see cref="Settlement.Memory"/> (or <see cref="Civilization.Memory"/> for cultural memory), so it is
    /// persisted with the save and never clashes with other advisors.
    /// </summary>
    public static class RecoveryKeys
    {
        /// <summary>Settlement: tick of the latest catastrophe that touched the settlement (presence = recovery active).</summary>
        public const string LastTick = "adv.rec.lastTick";
        /// <summary>Settlement: <see cref="RecoveryCatastrophe"/> code naming the latest catastrophe.</summary>
        public const string Kind = "adv.rec.kind";
        /// <summary>Settlement: tick the kind was last set (a specific disaster name wins over a generic one for a few ticks).</summary>
        public const string KindTick = "adv.rec.kindTick";
        /// <summary>Settlement: tick the advisor last wrote reasons to the AI log (rate-limits log spam and string allocation).</summary>
        public const string LogTick = "adv.rec.logTick";
        /// <summary>Settlement: decaying count of fire losses that stood at the forest edge.</summary>
        public const string ForestEdge = "adv.rec.forestEdge";
        /// <summary>Settlement: tick of the last famine that added a full scar (famines are reported repeatedly).</summary>
        public const string FamineTick = "adv.rec.famineTick";
        /// <summary>Settlement: cached PlacementRule bitmask of hard placement features available in the territory.</summary>
        public const string Features = "adv.rec.feat";
        /// <summary>Settlement: tick the feature mask was computed.</summary>
        public const string FeaturesTick = "adv.rec.featTick";

        /// <summary>Planner hint 0..1: avoid placing new buildings next to Trees tiles (learned from fires).</summary>
        public const string HintAvoidForest = "adv.rec.hint.avoidForest";
        /// <summary>Planner hint 0..1: prefer higher elevation tiles (learned from floods).</summary>
        public const string HintHighGround = "adv.rec.hint.highGround";

        /// <summary>Civilization: 1 while any cultural scar is remembered (cheap fast-path check).</summary>
        public const string CivAny = "adv.rec.civ.any";

        /// <summary>Settlement, per BuildingType: decaying count of buildings lost and not yet rebuilt ("adv.rec.lost.House").</summary>
        public static readonly string[] Lost = TypeTable("adv.rec.lost.");

        /// <summary>Settlement scars indexed by <see cref="RecoveryScar"/>.</summary>
        public static readonly string[] Scar =
        {
            "adv.rec.scar.fire", "adv.rec.scar.flood", "adv.rec.scar.war", "adv.rec.scar.famine",
            "adv.rec.scar.plague", "adv.rec.scar.drought", "adv.rec.scar.divine", "adv.rec.scar.quake"
        };

        /// <summary>Civilization cultural memory indexed by <see cref="RecoveryScar"/> (slow decay; makes new towns preventive).</summary>
        public static readonly string[] CivScar =
        {
            "adv.rec.civ.fire", "adv.rec.civ.flood", "adv.rec.civ.war", "adv.rec.civ.famine",
            "adv.rec.civ.plague", "adv.rec.civ.drought", "adv.rec.civ.divine", "adv.rec.civ.quake"
        };

        public const int ScarCount = 8;

        private static string[] TypeTable(string prefix)
        {
            var a = new string[Counts.Buildings];
            for (int i = 0; i < a.Length; i++) a[i] = prefix + ((BuildingType)i).ToString();
            return a;
        }
    }
}
