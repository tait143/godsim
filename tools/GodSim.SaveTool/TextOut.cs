using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GodSim.SaveTool
{
    /// <summary>
    /// Output helpers. Every string that comes from a save (civ names, chronicle text, versions) or from the
    /// command line is passed through <see cref="Safe"/> before printing, so a crafted save cannot inject
    /// terminal escape sequences or forge extra output lines.
    /// </summary>
    public static class TextOut
    {
        public static TextWriter Out = Console.Out;
        public static TextWriter Err = Console.Error;

        /// <summary>Replaces control/format characters with '?', and truncates to <paramref name="max"/> chars.</summary>
        public static string Safe(string s, int max = 120)
        {
            if (s == null) return "";
            if (max < 4) max = 4;
            var sb = new StringBuilder(Math.Min(s.Length, max));
            for (int i = 0; i < s.Length && sb.Length < max; i++)
            {
                char c = s[i];
                UnicodeCategory cat = char.GetUnicodeCategory(c);
                bool bad = char.IsControl(c) || cat == UnicodeCategory.Format || cat == UnicodeCategory.LineSeparator ||
                           cat == UnicodeCategory.ParagraphSeparator || cat == UnicodeCategory.Surrogate && !IsValidPair(s, i);
                sb.Append(bad ? '?' : c);
            }
            if (s.Length > max) { sb.Length = Math.Max(0, max - 3); sb.Append("..."); }
            return sb.ToString();
        }

        private static bool IsValidPair(string s, int i)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c)) return i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
            return i > 0 && char.IsHighSurrogate(s[i - 1]);
        }

        public static string Hash(ulong h) => "0x" + h.ToString("X16", CultureInfo.InvariantCulture);
        public static string Num(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);
        public static string Signed(long v) => v > 0 ? "+" + v.ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);
        public static string Signed(double v, string fmt = "0.##") => (v > 0 ? "+" : "") + v.ToString(fmt, CultureInfo.InvariantCulture);

        public static void Line(string s = "") => Out.WriteLine(s);
        public static void Field(string name, string value) => Out.WriteLine("  " + (name + ":").PadRight(20) + value);
        public static void Section(string title) { Out.WriteLine(); Out.WriteLine("== " + title + " =="); }
    }

    /// <summary>Minimal fixed-width text table (left-aligned text, right-aligned numbers).</summary>
    public sealed class TextTable
    {
        private readonly string[] _headers;
        private readonly bool[] _rightAlign;
        private readonly List<string[]> _rows = new List<string[]>();

        public TextTable(params string[] headers)
        {
            _headers = headers;
            _rightAlign = new bool[headers.Length];
        }

        public TextTable Right(params int[] columns) { foreach (int c in columns) _rightAlign[c] = true; return this; }

        public int RowCount => _rows.Count;

        public void Add(params object[] cells)
        {
            var row = new string[_headers.Length];
            for (int i = 0; i < row.Length; i++)
            {
                object c = i < cells.Length ? cells[i] : null;
                row[i] = c is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : TextOut.Safe(c?.ToString() ?? "", 48);
            }
            _rows.Add(row);
        }

        public void Write(TextWriter w)
        {
            var width = new int[_headers.Length];
            for (int i = 0; i < width.Length; i++) width[i] = _headers[i].Length;
            foreach (var r in _rows) for (int i = 0; i < width.Length; i++) width[i] = Math.Max(width[i], r[i].Length);
            WriteRow(w, _headers, width);
            var sep = new string[_headers.Length];
            for (int i = 0; i < sep.Length; i++) sep[i] = new string('-', width[i]);
            WriteRow(w, sep, width);
            foreach (var r in _rows) WriteRow(w, r, width);
        }

        private void WriteRow(TextWriter w, string[] cells, int[] width)
        {
            var sb = new StringBuilder("  ");
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(_rightAlign[i] ? cells[i].PadLeft(width[i]) : cells[i].PadRight(width[i]));
            }
            w.WriteLine(sb.ToString().TrimEnd());
        }
    }
}
