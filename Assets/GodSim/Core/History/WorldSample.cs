namespace GodSim.Core.History
{
    /// <summary>
    /// One world-wide snapshot taken by <see cref="HistoryRecorder"/>. Gauge fields (population, mana, ...) are
    /// the values at <see cref="Tick"/>; additive fields (births, deaths, deaths by cause) count what happened
    /// since the previous stored sample (downsampling folds dropped samples in, so the sums stay exact).
    /// </summary>
    public struct WorldSample : IHistorySample<WorldSample>
    {
        public long Tick;
        public int Population;
        public int Settlements;
        public int Buildings;
        public int BuildingsUnderConstruction;
        public int Armies;
        public int ActiveWars;
        public int LivingCivilizations;
        public AgeId WorldAge;
        public float Mana;
        public float MaxMana;
        public float Alignment;
        public float Happiness;

        // Additive (since previous stored sample)
        public int Births;
        public int Deaths;
        public int DeathsOldAge;
        public int DeathsStarvation;
        public int DeathsDisease;
        public int DeathsViolence;
        public int DeathsDisaster;
        public int DeathsDivineWrath;
        public int DeathsAccident;
        public int DeathsExposure;

        // Cumulative (WorldState.Stats at sample time)
        public long TotalBirths;
        public long TotalDeaths;

        public int Year => SimTime.Year(Tick);

        /// <summary>Deaths of the given cause since the previous stored sample (0 for unknown causes).</summary>
        public int DeathsBy(DeathCause cause)
        {
            switch (cause)
            {
                case DeathCause.OldAge: return DeathsOldAge;
                case DeathCause.Starvation: return DeathsStarvation;
                case DeathCause.Disease: return DeathsDisease;
                case DeathCause.Violence: return DeathsViolence;
                case DeathCause.Disaster: return DeathsDisaster;
                case DeathCause.DivineWrath: return DeathsDivineWrath;
                case DeathCause.Accident: return DeathsAccident;
                case DeathCause.Exposure: return DeathsExposure;
                default: return 0;
            }
        }

        internal void AddDeaths(DeathCause cause, int n)
        {
            switch (cause)
            {
                case DeathCause.OldAge: DeathsOldAge += n; break;
                case DeathCause.Starvation: DeathsStarvation += n; break;
                case DeathCause.Disease: DeathsDisease += n; break;
                case DeathCause.Violence: DeathsViolence += n; break;
                case DeathCause.Disaster: DeathsDisaster += n; break;
                case DeathCause.DivineWrath: DeathsDivineWrath += n; break;
                case DeathCause.Accident: DeathsAccident += n; break;
                case DeathCause.Exposure: DeathsExposure += n; break;
            }
        }

        /// <summary>Metric value as a float for charting.</summary>
        public float Get(WorldMetric metric)
        {
            switch (metric)
            {
                case WorldMetric.Population: return Population;
                case WorldMetric.Settlements: return Settlements;
                case WorldMetric.Buildings: return Buildings;
                case WorldMetric.Armies: return Armies;
                case WorldMetric.ActiveWars: return ActiveWars;
                case WorldMetric.Mana: return Mana;
                case WorldMetric.Alignment: return Alignment;
                case WorldMetric.Births: return Births;
                case WorldMetric.Deaths: return Deaths;
                case WorldMetric.LivingCivilizations: return LivingCivilizations;
                case WorldMetric.Happiness: return Happiness;
                case WorldMetric.WorldAge: return (int)WorldAge;
                case WorldMetric.BuildingsUnderConstruction: return BuildingsUnderConstruction;
                default: return 0f;
            }
        }

        public WorldSample AbsorbEarlier(WorldSample earlier)
        {
            var r = this;
            r.Births = Sat(r.Births, earlier.Births);
            r.Deaths = Sat(r.Deaths, earlier.Deaths);
            r.DeathsOldAge = Sat(r.DeathsOldAge, earlier.DeathsOldAge);
            r.DeathsStarvation = Sat(r.DeathsStarvation, earlier.DeathsStarvation);
            r.DeathsDisease = Sat(r.DeathsDisease, earlier.DeathsDisease);
            r.DeathsViolence = Sat(r.DeathsViolence, earlier.DeathsViolence);
            r.DeathsDisaster = Sat(r.DeathsDisaster, earlier.DeathsDisaster);
            r.DeathsDivineWrath = Sat(r.DeathsDivineWrath, earlier.DeathsDivineWrath);
            r.DeathsAccident = Sat(r.DeathsAccident, earlier.DeathsAccident);
            r.DeathsExposure = Sat(r.DeathsExposure, earlier.DeathsExposure);
            return r;
        }

        private static int Sat(int a, int b)
        {
            long v = (long)a + b;
            return v > int.MaxValue ? int.MaxValue : (int)v;
        }
    }
}
