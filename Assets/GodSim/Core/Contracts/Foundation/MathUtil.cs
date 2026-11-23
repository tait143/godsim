using System;

namespace GodSim.Core
{
    /// <summary>Engine-independent math helpers (no UnityEngine.Mathf in Core).</summary>
    public static class MathUtil
    {
        public const float Epsilon = 1e-5f;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < Epsilon ? 0f : Clamp01((v - a) / (b - a));
        public static float SmoothStep(float edge0, float edge1, float x) { float t = InverseLerp(edge0, edge1, x); return t * t * (3f - 2f * t); }
        public static bool Approximately(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;
        public static float MoveToward(float current, float target, float maxDelta) => Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;

        /// <summary>Logistic curve centered at mid with steepness k; returns 0..1. Handy for utility AI.</summary>
        public static float Logistic(float x, float mid, float k) => 1f / (1f + (float)Math.Exp(-k * (x - mid)));

        /// <summary>Guards against NaN/Infinity creeping into persistent state.</summary>
        public static float Sanitize(float v, float fallback = 0f) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;

        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        /// <summary>Packs RGBA bytes into 0xRRGGBBAA.</summary>
        public static uint Rgba(byte r, byte g, byte b, byte a = 255) => ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
        public static uint Hex(uint rrggbb) => (rrggbb << 8) | 0xFFu;
    }
}
