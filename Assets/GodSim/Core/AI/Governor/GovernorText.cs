using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>Culture-independent number formatting for governor log lines (no locale decimal commas).</summary>
    internal static class GovernorText
    {
        /// <summary>Formats with a fixed number of decimals (0..3) using '.' as separator.</summary>
        public static string Fixed(float value, int decimals)
        {
            value = MathUtil.Sanitize(value);
            decimals = MathUtil.Clamp(decimals, 0, 3);
            int scale = decimals == 0 ? 1 : decimals == 1 ? 10 : decimals == 2 ? 100 : 1000;
            double scaled = Math.Round((double)value * scale, MidpointRounding.AwayFromZero);
            if (scaled > 1e15) scaled = 1e15;
            if (scaled < -1e15) scaled = -1e15;
            long n = (long)scaled;
            var sb = new StringBuilder(16);
            if (n < 0) { sb.Append('-'); n = -n; }
            long whole = n / scale;
            long frac = n % scale;
            sb.Append(whole);
            if (decimals > 0)
            {
                sb.Append('.');
                string f = frac.ToString();
                for (int i = f.Length; i < decimals; i++) sb.Append('0');
                sb.Append(f);
            }
            return sb.ToString();
        }

        /// <summary>Two decimals.</summary>
        public static string F2(float v) => Fixed(v, 2);

        /// <summary>One decimal.</summary>
        public static string F1(float v) => Fixed(v, 1);

        /// <summary>Rounded integer.</summary>
        public static string I(float v) => Fixed(v, 0);

        /// <summary>
        /// Top advisor contributions to a building's council bias, e.g. "food +1.4, seasons +0.6", recovered from the
        /// advisors' own "[id] +v Type: reason" log lines (the board stores only totals). Falls back to
        /// "council +1.4" when no advisor line is in the bounded log. Logging only: never used for decisions.
        /// </summary>
        public static string AdvisorSummary(List<string> log, BuildingType type, float totalBias, int max)
        {
            string typeName = type.ToString();
            string[] ids = null;
            float[] sums = null;
            int n = 0;
            if (log != null)
                for (int i = 0; i < log.Count; i++)
                {
                    string line = log[i];
                    if (!TryParseVote(line, typeName, out string id, out float v)) continue;
                    if (ids == null) { ids = new string[log.Count]; sums = new float[log.Count]; }
                    int k = 0;
                    while (k < n && ids[k] != id) k++;
                    if (k == n) { ids[n] = id; sums[n] = 0f; n++; }
                    sums[k] += v;
                }
            var sb = new StringBuilder(48);
            if (n == 0) return "council " + Signed(totalBias);
            max = Math.Max(1, max);
            for (int shown = 0; shown < max && shown < n; shown++)
            {
                int best = -1;
                for (int k = 0; k < n; k++)
                    if (ids[k] != null && (best < 0 || Math.Abs(sums[k]) > Math.Abs(sums[best]))) best = k;
                if (best < 0) break;
                if (shown > 0) sb.Append(", ");
                sb.Append(ids[best]).Append(' ').Append(Signed(sums[best]));
                ids[best] = null;
            }
            return sb.ToString();
        }

        /// <summary>Signed one-decimal number ("+1.4", "-0.6").</summary>
        public static string Signed(float v) => (v >= 0f ? "+" : "") + F1(v);

        // Parses "[id] +0.80 Granary: reason" (AdvisorBoard.VoteBuild format; either decimal separator).
        private static bool TryParseVote(string line, string typeName, out string id, out float value)
        {
            id = null;
            value = 0f;
            if (string.IsNullOrEmpty(line) || line[0] != '[') return false;
            int close = line.IndexOf(']');
            if (close <= 1 || close + 2 >= line.Length || line[close + 1] != ' ') return false;
            int i = close + 2;
            bool neg = false;
            if (line[i] == '+' || line[i] == '-') { neg = line[i] == '-'; i++; }
            double whole = 0, frac = 0, scale = 1;
            bool digits = false, dot = false;
            for (; i < line.Length; i++)
            {
                char c = line[i];
                if (c >= '0' && c <= '9')
                {
                    digits = true;
                    if (dot) { scale *= 0.1; frac += (c - '0') * scale; }
                    else whole = whole * 10 + (c - '0');
                }
                else if ((c == '.' || c == ',') && !dot) dot = true;
                else break;
            }
            if (!digits || i >= line.Length || line[i] != ' ') return false;
            i++;
            if (string.CompareOrdinal(line, i, typeName, 0, typeName.Length) != 0) return false;
            int end = i + typeName.Length;
            if (end >= line.Length || line[end] != ':') return false;
            id = line.Substring(1, close - 1);
            value = (float)((whole + frac) * (neg ? -1 : 1));
            return true;
        }

        /// <summary>Stable log name of a building type (its enum name, independent of catalog display names).</summary>
        public static string NameOf(BuildingDefinition def) => def == null ? "?" : def.Type.ToString();
    }
}
