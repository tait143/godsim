namespace GodSim.Core.Heroes
{
    /// <summary>Read-only snapshot of a living hero (for UI, headless exporters and other systems via Composition).</summary>
    public struct HeroInfo
    {
        public int PersonId;
        public string Name;
        public HeroRole Role;
        /// <summary>Accumulated deeds (floored).</summary>
        public int Deeds;
        /// <summary>Tick the hero rose (-1 if unknown).</summary>
        public long SinceTick;
        /// <summary>Settlement the hero rose in (-1 if unknown).</summary>
        public int HomeSettlementId;
        /// <summary>Current settlement / civilization of the person.</summary>
        public int SettlementId;
        public int CivilizationId;
        public bool IsLegendary;
    }
}
