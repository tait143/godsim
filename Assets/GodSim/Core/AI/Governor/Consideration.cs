using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// One input of a utility decision: a named response curve with a weight. A consideration turns a raw
    /// world measurement (days of food, service coverage, housing deficit...) into a weighted score.
    /// Immutable and allocation-free to evaluate, so instances are shared as static readonly fields.
    /// </summary>
    public sealed class Consideration
    {
        /// <summary>Debug name (used in logs/inspectors).</summary>
        public readonly string Name;
        /// <summary>Response curve applied to the raw input.</summary>
        public readonly ResponseCurve Curve;
        /// <summary>Non-negative weight multiplied into the curve output.</summary>
        public readonly float Weight;

        /// <summary>Creates a consideration. A negative or non-finite weight is treated as 0.</summary>
        public Consideration(string name, ResponseCurve curve, float weight = 1f)
        {
            Name = name ?? "";
            Curve = curve;
            float w = MathUtil.Sanitize(weight);
            Weight = w > 0f ? w : 0f;
        }

        /// <summary>Weighted score for a raw input: Curve(input) * Weight (finite, &gt;= 0).</summary>
        public float Score(float input) => Curve.Evaluate(input) * Weight;

        /// <summary>Unweighted curve output in [0, 1].</summary>
        public float Evaluate(float input) => Curve.Evaluate(input);

        /// <summary>
        /// Multiplies normalized scores and applies the standard "compensation factor" so that adding more
        /// considerations does not systematically drive the product toward zero (Infinite Axis Utility System).
        /// Returns 0 for an empty set. Scores are clamped to [0, 1].
        /// </summary>
        public static float CombineMultiplicative(float[] scores, int count)
        {
            if (scores == null || count <= 0) return 0f;
            count = Math.Min(count, scores.Length);
            float product = 1f;
            for (int i = 0; i < count; i++) product *= MathUtil.Clamp01(MathUtil.Sanitize(scores[i]));
            return Compensate(product, count);
        }

        /// <summary>Compensates a product of <paramref name="count"/> normalized scores (see <see cref="CombineMultiplicative"/>).</summary>
        public static float Compensate(float product, int count)
        {
            product = MathUtil.Clamp01(MathUtil.Sanitize(product));
            if (count <= 1 || product <= 0f) return product;
            float modification = 1f - 1f / count;
            float makeUp = (1f - product) * modification;
            return MathUtil.Clamp01(product + makeUp * product);
        }

        /// <inheritdoc />
        public override string ToString() => Name + " " + Curve + " x" + Weight;
    }
}
