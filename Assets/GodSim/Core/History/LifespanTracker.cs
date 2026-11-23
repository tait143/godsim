using System;

namespace GodSim.Core.History
{
    /// <summary>
    /// Running tally of ages at death, fed by <see cref="PersonDiedEvent"/> (dead people leave WorldState, so
    /// this cannot be computed from state later). Allocation-free after construction. In-memory only.
    /// </summary>
    public sealed class LifespanTracker
    {
        private static readonly int CauseCount = Enum.GetValues(typeof(DeathCause)).Length;

        private readonly long[] _countByRace = new long[Counts.Races];
        private readonly double[] _sumByRace = new double[Counts.Races];
        private readonly long[] _countByCause = new long[CauseCount];
        private readonly double[] _sumByCause = new double[CauseCount];
        private long _count;
        private double _sum;

        /// <summary>Total deaths recorded.</summary>
        public long Count => _count;

        /// <summary>Mean age (years) at death over every recorded death; 0 when none.</summary>
        public float Average => _count > 0 ? (float)(_sum / _count) : 0f;

        /// <summary>Records one death. Invalid ages are ignored; unknown races/causes only count in the total.</summary>
        public void Record(RaceId race, DeathCause cause, float ageYears)
        {
            if (float.IsNaN(ageYears) || float.IsInfinity(ageYears)) return;
            double age = ageYears < 0f ? 0.0 : (ageYears > 100000f ? 100000.0 : ageYears);
            _count++;
            _sum += age;
            int r = (int)race;
            if (r >= 0 && r < _countByRace.Length) { _countByRace[r]++; _sumByRace[r] += age; }
            int c = (int)cause;
            if (c >= 0 && c < _countByCause.Length) { _countByCause[c]++; _sumByCause[c] += age; }
        }

        public long CountFor(RaceId race) { int r = (int)race; return r >= 0 && r < _countByRace.Length ? _countByRace[r] : 0; }

        public float AverageFor(RaceId race)
        {
            int r = (int)race;
            if (r < 0 || r >= _countByRace.Length || _countByRace[r] == 0) return 0f;
            return (float)(_sumByRace[r] / _countByRace[r]);
        }

        public long CountFor(DeathCause cause) { int c = (int)cause; return c >= 0 && c < _countByCause.Length ? _countByCause[c] : 0; }

        public float AverageFor(DeathCause cause)
        {
            int c = (int)cause;
            if (c < 0 || c >= _countByCause.Length || _countByCause[c] == 0) return 0f;
            return (float)(_sumByCause[c] / _countByCause[c]);
        }

        public void Clear()
        {
            Array.Clear(_countByRace, 0, _countByRace.Length);
            Array.Clear(_sumByRace, 0, _sumByRace.Length);
            Array.Clear(_countByCause, 0, _countByCause.Length);
            Array.Clear(_sumByCause, 0, _sumByCause.Length);
            _count = 0;
            _sum = 0;
        }
    }
}
