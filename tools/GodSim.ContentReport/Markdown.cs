using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GodSim.ContentReport
{
    /// <summary>
    /// A GitHub-flavored markdown table whose source is padded so columns line up in a plain-text diff.
    /// A header that starts with '&gt;' is right-aligned, '^' centered; the marker is not printed.
    /// </summary>
    internal sealed class MdTable
    {
        private readonly string[] _headers;
        private readonly char[] _align;
        private readonly List<string[]> _rows = new List<string[]>();

        public MdTable(params string[] headers)
        {
            _headers = new string[headers.Length];
            _align = new char[headers.Length];
            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i] ?? "";
                char a = 'l';
                if (h.StartsWith(">", StringComparison.Ordinal)) { a = 'r'; h = h.Substring(1); }
                else if (h.StartsWith("^", StringComparison.Ordinal)) { a = 'c'; h = h.Substring(1); }
                _headers[i] = Md.Cell(h);
                _align[i] = a;
            }
        }

        public int RowCount => _rows.Count;

        public MdTable Row(params object[] cells)
        {
            var r = new string[_headers.Length];
            for (int i = 0; i < r.Length; i++)
                r[i] = i < cells.Length ? Md.Cell(cells[i] == null ? "" : Convert.ToString(cells[i], CultureInfo.InvariantCulture)) : "";
            _rows.Add(r);
            return this;
        }

        public void WriteTo(StringBuilder sb)
        {
            int n = _headers.Length;
            var w = new int[n];
            for (int i = 0; i < n; i++) w[i] = Math.Max(3, Md.Width(_headers[i]));
            foreach (var r in _rows) for (int i = 0; i < n; i++) w[i] = Math.Max(w[i], Md.Width(r[i]));

            WriteLine(sb, _headers, w);
            sb.Append('|');
            for (int i = 0; i < n; i++)
            {
                sb.Append(' ');
                if (_align[i] == 'c') sb.Append(':').Append('-', w[i] - 2).Append(':');
                else if (_align[i] == 'r') sb.Append('-', w[i] - 1).Append(':');
                else sb.Append('-', w[i]);
                sb.Append(" |");
            }
            sb.Append('\n');
            foreach (var r in _rows) WriteLine(sb, r, w);
            sb.Append('\n');
        }

        private void WriteLine(StringBuilder sb, string[] cells, int[] w)
        {
            sb.Append('|');
            for (int i = 0; i < cells.Length; i++)
            {
                int pad = w[i] - Md.Width(cells[i]);
                sb.Append(' ');
                if (_align[i] == 'r') sb.Append(' ', pad).Append(cells[i]);
                else if (_align[i] == 'c') sb.Append(' ', pad / 2).Append(cells[i]).Append(' ', pad - pad / 2);
                else sb.Append(cells[i]).Append(' ', pad);
                sb.Append(" |");
            }
            sb.Append('\n');
        }
    }

    /// <summary>Markdown/mermaid escaping and invariant number formatting.</summary>
    internal static class Md
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Makes text safe inside a table cell (pipes escaped, no line breaks).</summary>
        public static string Cell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", "").Replace("\n", " ").Replace("|", "\\|");
        }

        /// <summary>Display width for padding (UTF-16 code units, surrogate pairs count once).</summary>
        public static int Width(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++) if (!char.IsLowSurrogate(s[i])) n++;
            return n;
        }

        /// <summary>Number with up to <paramref name="decimals"/> decimals, invariant culture.</summary>
        public static string N(double v, int decimals = 2)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "n/a";
            string fmt = decimals <= 0 ? "#,0" : "#,0." + new string('#', decimals);
            string s = v.ToString(fmt, Inv);
            return s == "-0" ? "0" : s;
        }

        /// <summary>Fixed decimals (for aligned columns).</summary>
        public static string F(double v, int decimals = 2)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "n/a";
            return v.ToString("0." + new string('0', Math.Max(1, decimals)), Inv);
        }

        /// <summary>A fraction (0.1) as a signed percentage ("+10%").</summary>
        public static string Pct(double fraction)
        {
            double p = fraction * 100.0;
            string s = N(Math.Abs(p), 1) + "%";
            return (p < 0 ? "−" : "+") + s;
        }

        /// <summary>Signed number ("+0.05").</summary>
        public static string Signed(double v, int decimals = 2)
        {
            string mag = N(Math.Abs(v), decimals);
            if (mag == "0") return "0";
            return (v < 0 ? "−" : "+") + mag;
        }

        /// <summary>Text shading of a 0..1 value with <paramref name="cells"/> dots, e.g. ●●●○○.</summary>
        public static string Dots(double v, int cells = 5)
        {
            if (double.IsNaN(v)) v = 0;
            v = Math.Max(0, Math.Min(1, v));
            int filled = (int)Math.Round(v * cells, MidpointRounding.AwayFromZero);
            if (filled == 0 && v > 0) filled = 0;
            return new string('●', filled) + new string('○', cells - filled);
        }

        /// <summary>Converts 0xRRGGBBAA to "#RRGGBB" (alpha dropped when opaque).</summary>
        public static string Color(uint rgba)
        {
            uint a = rgba & 0xFF;
            string rgb = "#" + (rgba >> 8).ToString("X6", Inv);
            return a == 0xFF ? rgb : rgb + (a).ToString("X2", Inv);
        }

        /// <summary>Mermaid node id from an arbitrary key.</summary>
        public static string Id(string prefix, string key)
        {
            var sb = new StringBuilder(prefix);
            foreach (char c in key ?? "") sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            return sb.ToString();
        }

        /// <summary>Mermaid quoted-label text.</summary>
        public static string Label(string s) => (s ?? "").Replace("\"", "#quot;").Replace("\n", " ");

        /// <summary>GitHub heading anchor (lowercase, spaces to dashes, punctuation dropped).</summary>
        public static string Anchor(string heading)
        {
            var sb = new StringBuilder();
            foreach (char c in heading.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ') sb.Append('-');
            }
            return sb.ToString();
        }

        public static string Join(IEnumerable<string> parts, string sep = ", ", string empty = "—")
        {
            var s = string.Join(sep, parts);
            return s.Length == 0 ? empty : s;
        }
    }
}
