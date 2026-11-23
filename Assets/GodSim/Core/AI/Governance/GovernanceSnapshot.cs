namespace GodSim.Core.AI
{
    /// <summary>Civ-wide political conditions gathered once per governance run (value type; no allocations).</summary>
    public struct GovernanceSnapshot
    {
        public int Population;
        public int Settlements;
        /// <summary>Population-weighted settlement happiness (0..1).</summary>
        public float AvgHappiness;
        /// <summary>Population-weighted settlement unrest (0..1).</summary>
        public float AvgUnrest;
        /// <summary>Share of the population (0..1) that starved during the last interval.</summary>
        public float FamineShare;
        public float Gold;
        /// <summary>Sum of settlement net gold flow per tick.</summary>
        public float GoldNet;
        public bool AtWar;
        /// <summary>An active war is going badly (score against the civ).</summary>
        public bool LosingWar;
        /// <summary>A war ended in defeat during the last interval.</summary>
        public bool LostWar;
        /// <summary>A war ended in victory during the last interval.</summary>
        public bool WonWar;
        /// <summary>The civ advanced at least one age since the last run.</summary>
        public bool AgeAdvanced;

        /// <summary>0..1 crisis intensity (famine, defeat, unrest).</summary>
        public float Crisis
        {
            get
            {
                float c = FamineShare * 0.6f + AvgUnrest * 0.6f + (LostWar ? 0.5f : 0f) + (LosingWar ? 0.2f : 0f);
                return MathUtil.Clamp01(MathUtil.Sanitize(c));
            }
        }
    }
}
