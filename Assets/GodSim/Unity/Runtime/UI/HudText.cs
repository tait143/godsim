using System;
using System.Globalization;
using System.Text;
using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>
    /// String helpers for HUD refreshes. These allocate (strings), so they are only called from the
    /// throttled refresh (~4 Hz) or on user interaction, never per frame.
    /// </summary>
    internal static class HudText
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly StringBuilder Sb = new StringBuilder(256);
        private static readonly string[] SmallInts = BuildSmallInts(1024);

        private static string[] BuildSmallInts(int n)
        {
            var a = new string[n];
            for (int i = 0; i < n; i++) a[i] = i.ToString(Inv);
            return a;
        }

        /// <summary>Cached for 0..1023 (no allocation), otherwise formatted.</summary>
        public static string Int(int v) => v >= 0 && v < SmallInts.Length ? SmallInts[v] : v.ToString("#,0", Inv);

        /// <summary>1234 -> "1.2k", 2500000 -> "2.5M".</summary>
        public static string Compact(float v)
        {
            v = MathUtil.Sanitize(v);
            float a = Math.Abs(v);
            if (a < 1000f) return a < 10f && a != (float)Math.Floor(a) ? v.ToString("0.#", Inv) : ((int)Math.Round(v)).ToString(Inv);
            if (a < 1000000f) return (v / 1000f).ToString(a < 10000f ? "0.#" : "0", Inv) + "k";
            return (v / 1000000f).ToString("0.#", Inv) + "M";
        }

        public static string F0(float v) => MathUtil.Sanitize(v).ToString("0", Inv);
        public static string F1(float v) => MathUtil.Sanitize(v).ToString("0.0", Inv);
        public static string F2(float v) => MathUtil.Sanitize(v).ToString("0.00", Inv);
        public static string Ms(double v) => (double.IsNaN(v) || double.IsInfinity(v) ? 0.0 : v).ToString("0.00", Inv);
        public static string Pct(float t) => ((int)Math.Round(MathUtil.Sanitize(t) * 100f)).ToString(Inv) + "%";

        /// <summary>"+1.25" / "-0.40" with a sign; "0" when tiny.</summary>
        public static string Signed(float v, string format = "0.00")
        {
            v = MathUtil.Sanitize(v);
            if (Math.Abs(v) < 0.005f) return "0";
            return (v > 0f ? "+" : "") + v.ToString(format, Inv);
        }

        public static string Bytes(long n)
        {
            if (n < 1024) return n.ToString(Inv) + " B";
            if (n < 1024 * 1024) return (n / 1024.0).ToString("0.#", Inv) + " KB";
            return (n / (1024.0 * 1024.0)).ToString("0.##", Inv) + " MB";
        }

        public static string LocalDate(DateTime utc)
        {
            try { return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv); }
            catch (ArgumentException) { return utc.ToString("yyyy-MM-dd HH:mm", Inv); }
        }

        /// <summary>Splits "BountifulHarvest" into "Bountiful Harvest" (fallback names when defs are missing).</summary>
        public static string Spaced(string pascal)
        {
            if (string.IsNullOrEmpty(pascal)) return "";
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascal[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Shared builder for multi-part strings (single-threaded UI only).</summary>
        public static StringBuilder Begin() { Sb.Length = 0; return Sb; }
        public static string End() => Sb.ToString();

        public static string Year(long tick) => "Y" + Int(SimTime.Year(tick));
    }
}
