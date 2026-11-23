using System;

namespace GodSim.Unity
{
    /// <summary>
    /// Pure, engine-free color ramps. Every function returns a packed sRGB color 0xRRGGBBAA (the same
    /// packing as <c>Civilization.Color</c> and <c>RaceDefinition.BannerColor</c>), so the class can be
    /// unit-tested without UnityEngine. Inputs are sanitized: NaN maps to the low end, t is clamped to [0, 1].
    /// </summary>
    public static class ColorRamps
    {
        /// <summary>Neutral grey used for "no category" (unaffiliated territory, folk faith).</summary>
        public const uint Neutral = 0x9E9E9EFFu;

        /// <summary>Default <c>Civilization.Color</c> (meaning "unset").</summary>
        public const uint UnsetCivColor = 0xFFFFFFFFu;

        // matplotlib viridis / magma sampled at 9 evenly spaced stops (perceptually uniform, CVD-safe).
        private static readonly uint[] ViridisStops =
        {
            0x440154FFu, 0x472C7AFFu, 0x3B518BFFu, 0x2C718EFFu, 0x21908DFFu, 0x27AD81FFu, 0x5CC863FFu, 0xAADC32FFu, 0xFDE725FFu,
        };

        private static readonly uint[] MagmaStops =
        {
            0x000004FFu, 0x1C1044FFu, 0x4F127BFFu, 0x812581FFu, 0xB5367AFFu, 0xE55964FFu, 0xFB8761FFu, 0xFEC287FFu, 0xFCFDBFFFu,
        };

        // ColorBrewer RdBu (9 classes), red at 0 and blue at 1. Safe for deuteranopia/protanopia.
        private static readonly uint[] RedWhiteBlueStops =
        {
            0xB2182BFFu, 0xD6604DFFu, 0xF4A582FFu, 0xFDDBC7FFu, 0xF7F7F7FFu, 0xD1E5F0FFu, 0x92C5DEFFu, 0x4393C3FFu, 0x2166ACFFu,
        };

        // Okabe-Ito qualitative palette (without black/grey), designed for color-vision deficiencies.
        private static readonly uint[] CategoricalStops =
        {
            0x56B4E9FFu, 0xE69F00FFu, 0x009E73FFu, 0xF0E442FFu, 0x0072B2FFu, 0xD55E00FFu, 0xCC79A7FFu,
        };

        /// <summary>Number of hand-picked categorical colors before generated (golden-angle) hues are used.</summary>
        public static int CategoricalBaseCount => CategoricalStops.Length;

        /// <summary>Viridis: dark purple (0) to yellow (1).</summary>
        public static uint Viridis(float t) => SampleStops(ViridisStops, t);

        /// <summary>Magma: black (0) to pale yellow (1).</summary>
        public static uint Magma(float t) => SampleStops(MagmaStops, t);

        /// <summary>Diverging red (0) - white (0.5) - blue (1).</summary>
        public static uint DivergingRedWhiteBlue(float t) => SampleStops(RedWhiteBlueStops, t);

        /// <summary>Diverging blue (0) - white (0.5) - red (1).</summary>
        public static uint DivergingBlueWhiteRed(float t) => SampleStops(RedWhiteBlueStops, 1f - Sanitize01(t));

        /// <summary>
        /// Evaluates a continuous ramp at t in [0, 1]. For <see cref="ColorRampId.Categorical"/>, t is
        /// interpreted as a category index (rounded).
        /// </summary>
        public static uint Evaluate(ColorRampId ramp, float t)
        {
            switch (ramp)
            {
                case ColorRampId.Magma: return Magma(t);
                case ColorRampId.DivergingRedWhiteBlue: return DivergingRedWhiteBlue(t);
                case ColorRampId.DivergingBlueWhiteRed: return DivergingBlueWhiteRed(t);
                case ColorRampId.Categorical:
                    if (float.IsNaN(t) || float.IsInfinity(t)) return Neutral;
                    return Categorical((int)Math.Round(Math.Max(-1f, Math.Min(t, 1e6f))));
                default: return Viridis(t);
            }
        }

        /// <summary>
        /// Distinct color for a category index. Negative indices are <see cref="Neutral"/>. The first
        /// <see cref="CategoricalBaseCount"/> are Okabe-Ito colors; later ones use golden-angle hues.
        /// </summary>
        public static uint Categorical(int index)
        {
            if (index < 0) return Neutral;
            if (index < CategoricalStops.Length) return CategoricalStops[index];
            // Golden-angle hue walk: well separated, deterministic, unbounded.
            double h = (index * 0.6180339887498949) % 1.0;
            float s = (index & 1) == 0 ? 0.70f : 0.55f;
            float v = (index % 3) == 0 ? 0.80f : 0.95f;
            return HsvToRgba((float)h, s, v);
        }

        /// <summary>
        /// Territory color of a civilization. Honours its banner color (alpha forced opaque) unless it is
        /// unset (white or fully transparent), in which case <see cref="Categorical"/>(civId) is used.
        /// <paramref name="duplicateIndex"/> &gt; 0 (number of earlier civs sharing the same banner color,
        /// e.g. two Human civs) alternately lightens/darkens the color so neighbours stay distinguishable.
        /// </summary>
        public static uint CivColor(uint civRgba, int civId, int duplicateIndex = 0)
        {
            uint baseColor = (civRgba == UnsetCivColor || (civRgba & 0xFFu) == 0u) ? Categorical(civId) : (civRgba | 0xFFu);
            if (duplicateIndex <= 0) return baseColor;
            int step = (duplicateIndex + 1) / 2;                    // 1,1,2,2,3,3...
            float amount = Math.Min(0.6f, 0.22f * step);
            return (duplicateIndex & 1) == 1 ? Shade(baseColor, -amount) : Shade(baseColor, amount);
        }

        /// <summary>Packs channels into 0xRRGGBBAA.</summary>
        public static uint Pack(byte r, byte g, byte b, byte a) => ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;

        public static byte R(uint rgba) => (byte)(rgba >> 24);
        public static byte G(uint rgba) => (byte)(rgba >> 16);
        public static byte B(uint rgba) => (byte)(rgba >> 8);
        public static byte A(uint rgba) => (byte)rgba;

        /// <summary>Replaces the alpha channel.</summary>
        public static uint WithAlpha(uint rgba, byte a) => (rgba & 0xFFFFFF00u) | a;

        /// <summary>Channel-wise linear interpolation (sRGB space) between two packed colors.</summary>
        public static uint Lerp(uint a, uint b, float t)
        {
            t = Sanitize01(t);
            return Pack(LerpByte(R(a), R(b), t), LerpByte(G(a), G(b), t), LerpByte(B(a), B(b), t), LerpByte(A(a), A(b), t));
        }

        /// <summary>Lightens (amount &gt; 0, towards white) or darkens (amount &lt; 0, towards black); alpha kept.</summary>
        public static uint Shade(uint rgba, float amount)
        {
            if (float.IsNaN(amount)) return rgba;
            amount = Math.Max(-1f, Math.Min(1f, amount));
            uint target = amount >= 0f ? 0xFFFFFF00u : 0x00000000u;
            uint shaded = Lerp(rgba & 0xFFFFFF00u, target, Math.Abs(amount));
            return (shaded & 0xFFFFFF00u) | (rgba & 0xFFu);
        }

        /// <summary>Relative luminance (Rec. 709 weights on sRGB bytes, 0..1). Useful for ordering/tests.</summary>
        public static float Luminance(uint rgba) => (0.2126f * R(rgba) + 0.7152f * G(rgba) + 0.0722f * B(rgba)) / 255f;

        /// <summary>HSV (all 0..1) to opaque packed color.</summary>
        public static uint HsvToRgba(float h, float s, float v)
        {
            h = Sanitize01(h - (float)Math.Floor(h));
            s = Sanitize01(s);
            v = Sanitize01(v);
            float h6 = h * 6f;
            int sector = (int)h6 % 6;
            float f = h6 - (int)h6;
            float p = v * (1f - s), q = v * (1f - s * f), u = v * (1f - s * (1f - f));
            float r, g, b;
            switch (sector)
            {
                case 0: r = v; g = u; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = u; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = u; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Pack(ToByte(r), ToByte(g), ToByte(b), 255);
        }

        /// <summary>Fills a lookup table with evenly spaced samples of a continuous ramp (lut[0] = t 0, lut[n-1] = t 1).</summary>
        public static void FillLut(ColorRampId ramp, uint[] lut)
        {
            if (lut == null || lut.Length == 0) return;
            int n = lut.Length;
            if (n == 1) { lut[0] = Evaluate(ramp, 0.5f); return; }
            for (int i = 0; i < n; i++) lut[i] = Evaluate(ramp, i / (float)(n - 1));
        }

        private static uint SampleStops(uint[] stops, float t)
        {
            t = Sanitize01(t);
            float f = t * (stops.Length - 1);
            int i = (int)f;
            if (i >= stops.Length - 1) return stops[stops.Length - 1];
            return Lerp(stops[i], stops[i + 1], f - i);
        }

        private static float Sanitize01(float t) => t > 0f ? (t < 1f ? t : 1f) : 0f; // NaN -> 0

        private static byte LerpByte(byte a, byte b, float t) => (byte)(a + (b - a) * t + 0.5f);

        private static byte ToByte(float v) => (byte)(Sanitize01(v) * 255f + 0.5f);
    }
}
