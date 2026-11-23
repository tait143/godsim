using System;

namespace GodSim.Core.AI
{
    /// <summary>Shape of a <see cref="ResponseCurve"/>.</summary>
    public enum ResponseCurveKind : byte
    {
        /// <summary>y = t.</summary>
        Linear = 0,
        /// <summary>y = t^exponent (exponent 2 by default; values below 1 give a concave curve).</summary>
        Quadratic = 1,
        /// <summary>S-shaped curve centered at <see cref="ResponseCurve.Midpoint"/>, rescaled so y(0)=0 and y(1)=1.</summary>
        Logistic = 2,
        /// <summary>Hyperbolic falloff y = (1 - t) / (1 + k t): 1 at the input minimum, 0 at the maximum.</summary>
        Inverse = 3
    }

    /// <summary>
    /// A normalized utility response curve: maps a raw input (days of food, coverage, deficit...) to a
    /// score in [0, 1]. The input is first normalized to t in [0, 1] between <see cref="InputMin"/> and
    /// <see cref="InputMax"/> (InputMin may be greater than InputMax for a descending mapping), then shaped
    /// by <see cref="Kind"/>, and finally optionally inverted (y = 1 - y). NaN/Infinity inputs never
    /// produce NaN output: they evaluate as the curve's value at t = 0.
    /// </summary>
    [Serializable]
    public readonly struct ResponseCurve
    {
        /// <summary>Curve shape.</summary>
        public readonly ResponseCurveKind Kind;
        /// <summary>Raw input that maps to t = 0.</summary>
        public readonly float InputMin;
        /// <summary>Raw input that maps to t = 1.</summary>
        public readonly float InputMax;
        /// <summary>Shape parameter: exponent (Quadratic), steepness (Logistic, Inverse). Ignored by Linear.</summary>
        public readonly float Shape;
        /// <summary>Center of the logistic curve in normalized space (0..1).</summary>
        public readonly float Midpoint;
        /// <summary>When true the output is mirrored (y = 1 - y).</summary>
        public readonly bool Invert;

        /// <summary>Creates a curve. Out-of-range shape parameters are replaced by safe defaults.</summary>
        public ResponseCurve(ResponseCurveKind kind, float inputMin, float inputMax, float shape = 0f, float midpoint = 0.5f, bool invert = false)
        {
            Kind = kind;
            InputMin = MathUtil.Sanitize(inputMin);
            InputMax = MathUtil.Sanitize(inputMax, 1f);
            Shape = SanitizeShape(kind, shape);
            Midpoint = MathUtil.Clamp01(MathUtil.Sanitize(midpoint, 0.5f));
            Invert = invert;
        }

        /// <summary>Linear ramp from 0 at <paramref name="min"/> to 1 at <paramref name="max"/>.</summary>
        public static ResponseCurve Linear(float min, float max, bool invert = false) =>
            new ResponseCurve(ResponseCurveKind.Linear, min, max, 0f, 0.5f, invert);

        /// <summary>Power curve t^exponent between <paramref name="min"/> and <paramref name="max"/>.</summary>
        public static ResponseCurve Quadratic(float min, float max, float exponent = 2f, bool invert = false) =>
            new ResponseCurve(ResponseCurveKind.Quadratic, min, max, exponent, 0.5f, invert);

        /// <summary>Normalized logistic S-curve with the given steepness and midpoint (0..1 in normalized space).</summary>
        public static ResponseCurve Logistic(float min, float max, float steepness = 10f, float midpoint = 0.5f, bool invert = false) =>
            new ResponseCurve(ResponseCurveKind.Logistic, min, max, steepness, midpoint, invert);

        /// <summary>Hyperbolic falloff: 1 at <paramref name="min"/>, 0 at <paramref name="max"/>; larger steepness drops faster.</summary>
        public static ResponseCurve Inverse(float min, float max, float steepness = 4f, bool invert = false) =>
            new ResponseCurve(ResponseCurveKind.Inverse, min, max, steepness, 0.5f, invert);

        /// <summary>Maps a raw input to t in [0, 1] (0 for NaN/Infinity or a degenerate range).</summary>
        public float Normalize(float input)
        {
            if (float.IsNaN(input) || float.IsInfinity(input))
            {
                if (float.IsPositiveInfinity(input)) return InputMax >= InputMin ? 1f : 0f;
                if (float.IsNegativeInfinity(input)) return InputMax >= InputMin ? 0f : 1f;
                return 0f;
            }
            float range = InputMax - InputMin;
            if (Math.Abs(range) < MathUtil.Epsilon) return input >= InputMax ? 1f : 0f;
            return MathUtil.Clamp01((input - InputMin) / range);
        }

        /// <summary>Evaluates the curve for a raw input. Always returns a finite value in [0, 1].</summary>
        public float Evaluate(float input)
        {
            float t = Normalize(input);
            float y;
            switch (Kind)
            {
                case ResponseCurveKind.Quadratic:
                    y = (float)Math.Pow(t, Shape);
                    break;
                case ResponseCurveKind.Logistic:
                {
                    float lo = MathUtil.Logistic(0f, Midpoint, Shape);
                    float hi = MathUtil.Logistic(1f, Midpoint, Shape);
                    float span = hi - lo;
                    y = span > MathUtil.Epsilon ? (MathUtil.Logistic(t, Midpoint, Shape) - lo) / span : t;
                    break;
                }
                case ResponseCurveKind.Inverse:
                    y = (1f - t) / (1f + Shape * t);
                    break;
                default:
                    y = t;
                    break;
            }
            y = MathUtil.Clamp01(MathUtil.Sanitize(y));
            return Invert ? 1f - y : y;
        }

        private static float SanitizeShape(ResponseCurveKind kind, float shape)
        {
            shape = MathUtil.Sanitize(shape);
            switch (kind)
            {
                case ResponseCurveKind.Quadratic: return shape > 0f ? MathUtil.Clamp(shape, 0.05f, 16f) : 2f;
                case ResponseCurveKind.Logistic: return shape > 0f ? MathUtil.Clamp(shape, 0.1f, 60f) : 10f;
                case ResponseCurveKind.Inverse: return shape >= 0f ? MathUtil.Clamp(shape, 0f, 100f) : 4f;
                default: return 0f;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            Kind + "[" + InputMin + ".." + InputMax + (Invert ? ", inverted" : "") + "]";
    }
}
