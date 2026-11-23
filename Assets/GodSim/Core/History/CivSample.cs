namespace GodSim.Core.History
{
    /// <summary>One per-civilization snapshot taken by <see cref="HistoryRecorder"/>. All fields are gauges.</summary>
    public struct CivSample : IHistorySample<CivSample>
    {
        public long Tick;
        public int Population;
        public AgeId Age;
        public int Settlements;
        public int Buildings;
        public float MilitaryPower;
        public float TotalResearch;
        public float Devotion;
        public float GodFavor;
        public float Happiness;

        public int Year => SimTime.Year(Tick);

        /// <summary>Metric value as a float for charting.</summary>
        public float Get(CivMetric metric)
        {
            switch (metric)
            {
                case CivMetric.Population: return Population;
                case CivMetric.Age: return (int)Age;
                case CivMetric.Settlements: return Settlements;
                case CivMetric.Buildings: return Buildings;
                case CivMetric.MilitaryPower: return MilitaryPower;
                case CivMetric.TotalResearch: return TotalResearch;
                case CivMetric.Devotion: return Devotion;
                case CivMetric.GodFavor: return GodFavor;
                case CivMetric.Happiness: return Happiness;
                default: return 0f;
            }
        }

        /// <summary>Gauges only: the later sample wins unchanged.</summary>
        public CivSample AbsorbEarlier(CivSample earlier) => this;
    }
}
