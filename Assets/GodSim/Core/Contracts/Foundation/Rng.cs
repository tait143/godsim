using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    /// <summary>
    /// Deterministic PCG32 random number generator. All simulation randomness MUST go through this
    /// type (never System.Random or UnityEngine.Random) so that worlds are reproducible from a seed
    /// and replays/save-games stay in sync. State is fully serializable via <see cref="State"/> and
    /// <see cref="Increment"/>.
    /// </summary>
    public sealed class Rng
    {
        private ulong _state;
        private ulong _inc;

        public Rng(ulong seed, ulong stream = 0xDA3E39CB94B95BDBUL)
        {
            _state = 0UL;
            _inc = (stream << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        private Rng() { }

        public ulong State => _state;
        public ulong Increment => _inc;

        /// <summary>Restores a generator from serialized state (used by persistence).</summary>
        public static Rng FromState(ulong state, ulong increment)
        {
            if ((increment & 1UL) == 0) throw new ArgumentException("PCG increment must be odd.", nameof(increment));
            return new Rng { _state = state, _inc = increment };
        }

        /// <summary>Creates an independent child stream; deterministic given this generator's state.</summary>
        public Rng Fork(ulong salt)
        {
            ulong seed = NextULong() ^ Hashing.Mix64(salt);
            return new Rng(seed, Hashing.Mix64(salt + 0x9E3779B97F4A7C15UL));
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * 6364136223846793005UL + _inc);
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        public ulong NextULong() => ((ulong)NextUInt() << 32) | NextUInt();

        /// <summary>Uniform int in [minInclusive, maxExclusive). Returns min if range is empty.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            uint threshold = unchecked(0u - range) % range;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return minInclusive + (int)(r % range);
            }
        }

        /// <summary>Uniform int in [minInclusive, maxInclusive].</summary>
        public int RangeInclusive(int minInclusive, int maxInclusive) => Range(minInclusive, maxInclusive + 1);

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>True with probability p (clamped to [0,1]).</summary>
        public bool Chance(float p)
        {
            if (p <= 0f) return false;
            if (p >= 1f) return true;
            return NextFloat() < p;
        }

        /// <summary>Standard normal sample (Box-Muller).</summary>
        public float Gaussian(float mean = 0f, float stdDev = 1f)
        {
            double u1 = 1.0 - NextDouble();
            double u2 = NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return (float)(mean + stdDev * z);
        }

        public T Pick<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(list));
            return list[Range(0, list.Count)];
        }

        public T PickOrDefault<T>(IReadOnlyList<T> list, T fallback)
        {
            if (list == null || list.Count == 0) return fallback;
            return list[Range(0, list.Count)];
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>Picks an index with probability proportional to weights. Negative weights count as 0. Returns -1 if all zero.</summary>
        public int WeightedIndex(IReadOnlyList<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) if (weights[i] > 0f) total += weights[i];
            if (total <= 0f) return -1;
            float roll = NextFloat() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i];
                if (w <= 0f) continue;
                if (roll < w) return i;
                roll -= w;
            }
            for (int i = weights.Count - 1; i >= 0; i--) if (weights[i] > 0f) return i;
            return -1;
        }

        /// <summary>Converts a per-year probability into the probability for a span of ticks.</summary>
        public static float PerYearToPerTicks(float perYear, int ticks)
        {
            perYear = MathUtil.Clamp01(perYear);
            if (ticks <= 0) return 0f;
            return 1f - (float)Math.Pow(1.0 - perYear, ticks / (double)SimTime.TicksPerYear);
        }
    }

    /// <summary>Stable, platform-independent hashing (string.GetHashCode is randomized per process in .NET Core).</summary>
    public static class Hashing
    {
        public static ulong Mix64(ulong z)
        {
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        public static ulong Fnv1a64(string s)
        {
            ulong hash = 14695981039346656037UL;
            if (s == null) return hash;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash = unchecked(hash * 1099511628211UL);
            }
            return hash;
        }

        public static ulong Combine(ulong a, ulong b) => Mix64(a ^ unchecked(b * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL));

        public static int Coord(int x, int y) => unchecked((x * 73856093) ^ (y * 19349663));
    }
}
