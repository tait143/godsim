using System;

namespace GodSim.Core.World
{
    /// <summary>
    /// Seeded 2D gradient (Perlin-style) noise with a quintic fade curve. The permutation table is
    /// shuffled with the supplied <see cref="Rng"/>, and the 16 gradient directions are constant
    /// literals, so the output is bit-identical across platforms for the same generator state.
    /// Output is approximately in [-1, 1] and the lattice repeats every 256 units.
    /// </summary>
    public sealed class GradientNoise
    {
        private const int TableSize = 256;
        private const int TableMask = TableSize - 1;
        private const float OutputScale = 1.41421356f;

        // 16 unit vectors at 22.5 degree steps (literal constants: no trig at runtime, deterministic).
        private static readonly float[] GradX =
        {
            1f, 0.9238795f, 0.7071068f, 0.3826834f, 0f, -0.3826834f, -0.7071068f, -0.9238795f,
            -1f, -0.9238795f, -0.7071068f, -0.3826834f, 0f, 0.3826834f, 0.7071068f, 0.9238795f
        };

        private static readonly float[] GradY =
        {
            0f, 0.3826834f, 0.7071068f, 0.9238795f, 1f, 0.9238795f, 0.7071068f, 0.3826834f,
            0f, -0.3826834f, -0.7071068f, -0.9238795f, -1f, -0.9238795f, -0.7071068f, -0.3826834f
        };

        private readonly int[] _perm = new int[TableSize * 2];

        /// <summary>Builds a noise field whose permutation is drawn from <paramref name="rng"/>.</summary>
        public GradientNoise(Rng rng)
        {
            Guard.NotNull(rng, nameof(rng));
            for (int i = 0; i < TableSize; i++) _perm[i] = i;
            for (int i = TableSize - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                int t = _perm[i];
                _perm[i] = _perm[j];
                _perm[j] = t;
            }
            for (int i = 0; i < TableSize; i++) _perm[i + TableSize] = _perm[i];
        }

        /// <summary>Samples the noise at (x, y). Returns a value in [-1, 1] (0 at lattice points).</summary>
        public float Sample(float x, float y)
        {
            int xi = FastFloor(x), yi = FastFloor(y);
            float xf = x - xi, yf = y - yi;
            int cx = xi & TableMask, cy = yi & TableMask;
            int p0 = _perm[cx], p1 = _perm[cx + 1];
            int g00 = _perm[p0 + cy] & 15;
            int g01 = _perm[p0 + cy + 1] & 15;
            int g10 = _perm[p1 + cy] & 15;
            int g11 = _perm[p1 + cy + 1] & 15;

            float xm = xf - 1f, ym = yf - 1f;
            float n00 = GradX[g00] * xf + GradY[g00] * yf;
            float n10 = GradX[g10] * xm + GradY[g10] * yf;
            float n01 = GradX[g01] * xf + GradY[g01] * ym;
            float n11 = GradX[g11] * xm + GradY[g11] * ym;

            float u = Fade(xf), v = Fade(yf);
            float nx0 = n00 + u * (n10 - n00);
            float nx1 = n01 + u * (n11 - n01);
            float r = (nx0 + v * (nx1 - nx0)) * OutputScale;
            return r < -1f ? -1f : (r > 1f ? 1f : r);
        }

        /// <summary>Floor that is exact for negative values and cheaper than Math.Floor.</summary>
        public static int FastFloor(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// <summary>
    /// Fractal sums of <see cref="GradientNoise"/>: fBm (fractional Brownian motion) and ridged
    /// multifractal. Each octave is rotated (3-4-5 rotation, exact constants) and offset by a seeded
    /// amount to hide lattice artifacts and the 256-unit period.
    /// </summary>
    public sealed class FractalNoise
    {
        private const float RotCos = 0.8f;
        private const float RotSin = 0.6f;

        private readonly GradientNoise _noise;
        private readonly float[] _offX;
        private readonly float[] _offY;
        private readonly float _norm;

        /// <summary>Number of octaves summed.</summary>
        public readonly int Octaves;
        /// <summary>Frequency multiplier between octaves.</summary>
        public readonly float Lacunarity;
        /// <summary>Amplitude multiplier between octaves.</summary>
        public readonly float Gain;

        /// <summary>Creates a fractal noise source; all randomness is drawn from <paramref name="rng"/>.</summary>
        public FractalNoise(Rng rng, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            Guard.NotNull(rng, nameof(rng));
            Octaves = MathUtil.Clamp(octaves, 1, 12);
            Lacunarity = MathUtil.Clamp(MathUtil.Sanitize(lacunarity, 2f), 1.1f, 4f);
            Gain = MathUtil.Clamp(MathUtil.Sanitize(gain, 0.5f), 0.05f, 0.95f);
            _noise = new GradientNoise(rng);
            _offX = new float[Octaves];
            _offY = new float[Octaves];
            float amp = 1f, sum = 0f;
            for (int k = 0; k < Octaves; k++)
            {
                _offX[k] = rng.NextFloat() * 256f;
                _offY[k] = rng.NextFloat() * 256f;
                sum += amp;
                amp *= Gain;
            }
            _norm = 1f / sum;
        }

        /// <summary>Normalized fBm in roughly [-1, 1] (typical magnitude ~0.5).</summary>
        public float Fbm(float x, float y)
        {
            float sum = 0f, amp = 1f, fx = x, fy = y;
            for (int k = 0; k < Octaves; k++)
            {
                sum += amp * _noise.Sample(fx + _offX[k], fy + _offY[k]);
                amp *= Gain;
                float nx = (RotCos * fx - RotSin * fy) * Lacunarity;
                float ny = (RotSin * fx + RotCos * fy) * Lacunarity;
                fx = nx;
                fy = ny;
            }
            return sum * _norm;
        }

        /// <summary>
        /// Ridged multifractal in [0, 1]: sharp crests (values near 1) along the zero lines of the
        /// underlying noise, with octave weighting so detail concentrates on the ridges.
        /// </summary>
        public float Ridged(float x, float y)
        {
            float sum = 0f, amp = 1f, weight = 1f, fx = x, fy = y;
            for (int k = 0; k < Octaves; k++)
            {
                float n = _noise.Sample(fx + _offX[k], fy + _offY[k]);
                n = 1f - (n < 0f ? -n : n);
                n *= n;
                n *= weight;
                weight = n * 2f;
                if (weight > 1f) weight = 1f;
                sum += n * amp;
                amp *= Gain;
                float nx = (RotCos * fx - RotSin * fy) * Lacunarity;
                float ny = (RotSin * fx + RotCos * fy) * Lacunarity;
                fx = nx;
                fy = ny;
            }
            float r = sum * _norm;
            return r < 0f ? 0f : (r > 1f ? 1f : r);
        }
    }
}
