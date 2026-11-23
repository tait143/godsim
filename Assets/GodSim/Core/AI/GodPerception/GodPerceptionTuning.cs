namespace GodSim.Core.AI
{
    /// <summary>Balancing constants for mortal perception of the god. Rates are per reference run (24 ticks).</summary>
    public static class GodPerceptionTuning
    {
        /// <summary>Tiles beyond the cast radius within which a settlement still feels the act.</summary>
        public const int AwarenessMargin = 5;
        /// <summary>Impact on a settlement just outside the radius (falls to ~0.1 at the margin).</summary>
        public const float NearImpact = 0.6f;

        public const float GratitudeGain = 0.22f;
        public const float FearGain = 0.28f;
        public const float ResentGain = 0.30f;
        public const float UncertaintyGain = 0.30f;
        /// <summary>Each consecutive blessing adds this much gain (up to <see cref="MaxStreakBonus"/> steps).</summary>
        public const float StreakBonus = 0.15f;
        public const int MaxStreakBonus = 4;

        /// <summary>Gift and punishment closer together than this make the god look erratic.</summary>
        public const int ErraticWindowTicks = SimTime.TicksPerYear * 2;

        // Per-run retention at the reference run length (memory scales by race temperament).
        public const float GratitudeRetention = 0.97f;
        public const float FearRetention = 0.94f;
        public const float ResentRetention = 0.975f;
        public const float UncertaintyRetention = 0.95f;
        public const int ReferenceRunTicks = 24;

        /// <summary>Hard cap on the Devotion/GodFavor nudge this system applies per run (the Divine FaithSystem does the drift).</summary>
        public const float MaxNudgePerRun = 0.02f;
        /// <summary>Bound on banked, not-yet-applied nudges.</summary>
        public const float MaxPendingNudge = 0.06f;

        public const float BlessDevotion = 0.006f;
        public const float BlessFavor = 0.004f;
        public const float PiousWrathDevotion = 0.010f;
        public const float ImpiousWrathDevotion = -0.007f;
        public const float ImpiousWrathFavor = -0.012f;
        public const float FlipDevotion = -0.008f;
        public const float UncertaintyDrag = -0.006f;
        public const float TurnedDrag = -0.004f;

        // Interpretation thresholds.
        public const float ImpiousPiety = 0.35f;
        public const float PiousPiety = 0.75f;
        public const float BlameMinPiety = 0.5f;
        public const float BlameFullPiety = 0.9f;

        public const float TurnBackResentment = 0.55f;
        public const float ForgiveResentment = 0.3f;
        public const float AdoreGratitude = 0.75f;
        public const float GreatWrathFear = 0.45f;
        public const float ErraticUncertainty = 0.5f;

        // Chronicle rate limits.
        public const int CivChronicleGapTicks = SimTime.TicksPerYear * 2;
        public const int WorldChronicleGapTicks = SimTime.TicksPerSeason * 2;
        public const int ChronicleQueueTtlTicks = SimTime.TicksPerYear;
        public const int GreatWrathGapTicks = SimTime.TicksPerYear * 4;

        /// <summary>Per-run retention of settlement response pressure (thanks/appeasement).</summary>
        public const float ResponseRetention = 0.55f;
    }
}
