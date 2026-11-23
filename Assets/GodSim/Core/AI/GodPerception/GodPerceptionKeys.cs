namespace GodSim.Core.AI
{
    /// <summary>
    /// Memory keys written by <see cref="GodPerceptionSystem"/> (all floats, persisted with the save).
    /// Civ keys: feelings (gratitude/fear live on <see cref="AdvisorBoard"/>), the short "divine history"
    /// counters and pending nudges. Settlement keys: per-town response pressure consumed by the votes.
    /// </summary>
    public static class GodPerceptionKeys
    {
        // ---- civ: feelings (0..1)
        /// <summary>Same key as <see cref="AdvisorBoard.GodGratitudeKey"/>.</summary>
        public const string Gratitude = AdvisorBoard.GodGratitudeKey;
        /// <summary>Same key as <see cref="AdvisorBoard.GodFearKey"/>.</summary>
        public const string Fear = AdvisorBoard.GodFearKey;
        /// <summary>Bitterness at a god seen as a tyrant (0..1).</summary>
        public const string Resentment = "godp.resent";
        /// <summary>Confusion caused by an erratic god (0..1).</summary>
        public const string Uncertainty = "godp.uncert";

        // ---- civ: divine history
        public const string BlessCount = "godp.bless.n";
        public const string WrathCount = "godp.wrath.n";
        public const string ShapeCount = "godp.shape.n";
        public const string BlameCount = "godp.blame.n";
        /// <summary>(float)<see cref="DivineActKind"/> of the last remembered act.</summary>
        public const string LastKind = "godp.lastKind";
        public const string LastTick = "godp.lastTick";
        /// <summary>Epithet index (see <see cref="GodPerceptionLore"/>) of the last act: how the civ currently names its god.</summary>
        public const string LastEpithet = "godp.lastEp";
        /// <summary>Epithet of the last punishment (used for appeasement reasons and fearful moods).</summary>
        public const string WrathEpithet = "godp.wrathEp";
        public const string LastPolarity = "godp.lastPol";
        public const string LastPolarityTick = "godp.lastPolT";
        /// <summary>Signed streak: +n consecutive gifts, -n consecutive punishments.</summary>
        public const string Streak = "godp.streak";
        public const string Flips = "godp.flips";

        // ---- civ: pending nudges & state flags
        public const string PendingDevotion = "godp.pDev";
        public const string PendingFavor = "godp.pFav";
        public const string Turned = "godp.turned";
        public const string AdoreFlag = "godp.adore";
        public const string ErraticFlag = "godp.erratic";
        public const string GreatWrathTick = "godp.greatT";
        public const string ChronicleQueued = "godp.chq";
        public const string ChronicleQueuedTick = "godp.chqT";
        public const string ChronicleQueuedEpithet = "godp.chqE";
        public const string ChronicleTick = "godp.chT";

        // ---- settlement: response pressure
        public const string SettlementThanks = "godp.s.thx";
        public const string SettlementAppease = "godp.s.app";
        public const string SettlementEpithet = "godp.s.ep";

        // ---- world
        /// <summary>World-level: tick of the last god-perception chronicle entry (global rate limit).</summary>
        public const string WorldChronicleTick = "godp.chT";
    }
}
