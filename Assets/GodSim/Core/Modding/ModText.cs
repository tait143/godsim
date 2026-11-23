using System.Text;

namespace GodSim.Core.Modding
{
    /// <summary>
    /// Sanitization of every string that a mod can put into the game database. Strips C0/C1 control
    /// characters, Unicode bidi overrides/isolates (Trojan-Source style spoofing), zero-width and other
    /// invisible characters, noncharacters, lone surrogates and the rich-text angle brackets '&lt;' '&gt;'
    /// (so a mod cannot inject UI markup such as &lt;size=999&gt;), collapses whitespace, trims and caps length.
    /// </summary>
    public static class ModText
    {
        /// <summary>True for bidi controls, zero-width/invisible formatting characters and BOM.</summary>
        public static bool IsInvisibleOrBidi(char c)
        {
            switch (c)
            {
                case '\u00AD': // soft hyphen
                case '\u034F': // combining grapheme joiner
                case '\u061C': // arabic letter mark
                case '\u115F': case '\u1160': // hangul fillers
                case '\u17B4': case '\u17B5':
                case '\u180E': // mongolian vowel separator
                case '\u200B': case '\u200C': case '\u200D': case '\u200E': case '\u200F':
                case '\u202A': case '\u202B': case '\u202C': case '\u202D': case '\u202E':
                case '\u2060': case '\u2061': case '\u2062': case '\u2063': case '\u2064':
                case '\u2066': case '\u2067': case '\u2068': case '\u2069':
                case '\u206A': case '\u206B': case '\u206C': case '\u206D': case '\u206E': case '\u206F':
                case '\u3164': // hangul filler
                case '\uFEFF': // BOM / zero-width no-break space
                case '\uFFA0':
                case '\uFFF9': case '\uFFFA': case '\uFFFB': // interlinear annotation
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>At most this many combining marks are kept after one base character (defeats "Zalgo" stacks).</summary>
        public const int MaxMarksPerBase = 2;

        private static bool IsMark(char c)
        {
            var cat = char.GetUnicodeCategory(c);
            return cat == System.Globalization.UnicodeCategory.NonSpacingMark || cat == System.Globalization.UnicodeCategory.EnclosingMark
                || cat == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }

        private static bool IsControl(char c) => c < 0x20 || (c >= 0x7F && c <= 0x9F);

        /// <summary>
        /// General-purpose sanitizer for names and descriptions. When <paramref name="allowNewlines"/> is true,
        /// '\n' is kept (at most one blank line in a row); otherwise all whitespace collapses to single spaces.
        /// The result is trimmed and capped at <paramref name="maxLength"/> UTF-16 units without splitting a
        /// surrogate pair. Never returns null.
        /// </summary>
        public static string Sanitize(string s, int maxLength, bool allowNewlines = false)
        {
            if (string.IsNullOrEmpty(s) || maxLength <= 0) return "";
            var sb = new StringBuilder(s.Length < maxLength ? s.Length : maxLength);
            bool pendingSpace = false;
            int pendingNewlines = 0;
            int marks = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        // drop supplementary-plane tag characters (U+E0000..U+E007F) used for invisible text
                        if (c == '\uDB40') { i++; continue; }
                        if (sb.Length + Pending(pendingSpace, pendingNewlines) + 2 > maxLength) break;
                        Flush(sb, ref pendingSpace, ref pendingNewlines);
                        sb.Append(c).Append(s[i + 1]);
                        i++;
                    }
                    continue; // lone high surrogate: drop
                }
                if (char.IsLowSurrogate(c)) continue; // lone low surrogate: drop
                if (c == '\n' || c == '\u2028' || c == '\u2029' || c == '\r')
                {
                    if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') continue;
                    marks = MaxMarksPerBase; // a mark cannot attach to a line break
                    if (allowNewlines) { if (sb.Length > 0 && pendingNewlines < 2) pendingNewlines++; pendingSpace = false; }
                    else if (sb.Length > 0) pendingSpace = true;
                    continue;
                }
                if (c == '\t' || c == ' ' || c == '\u00A0' || (c >= '\u2000' && c <= '\u200A') || c == '\u3000' || c == '\u205F' || c == '\u202F' || c == '\u1680')
                {
                    if (sb.Length > 0 && pendingNewlines == 0) pendingSpace = true;
                    marks = MaxMarksPerBase;
                    continue;
                }
                if (IsControl(c) || IsInvisibleOrBidi(c) || c == '\uFFFE' || c == '\uFFFF' || c == '<' || c == '>') continue;
                if (c >= '\uFDD0' && c <= '\uFDEF') continue; // noncharacters
                if (IsMark(c)) { if (++marks > MaxMarksPerBase || sb.Length == 0) continue; } // anti-Zalgo
                else marks = 0;
                if (sb.Length + Pending(pendingSpace, pendingNewlines) + 1 > maxLength) break;
                Flush(sb, ref pendingSpace, ref pendingNewlines);
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static int Pending(bool pendingSpace, int pendingNewlines) => pendingNewlines > 0 ? pendingNewlines : (pendingSpace ? 1 : 0);

        /// <summary>
        /// True for Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9, CONIN$, CONOUT$), with or
        /// without an extension. Opening e.g. "CON.json" on Windows reads the console and would block forever.
        /// </summary>
        public static bool IsReservedDeviceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int dot = name.IndexOf('.');
            string stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ').ToUpperInvariant();
            switch (stem)
            {
                case "CON": case "PRN": case "AUX": case "NUL": case "CONIN$": case "CONOUT$": case "CLOCK$": return true;
            }
            if (stem.Length == 4 && (stem.StartsWith("COM", System.StringComparison.Ordinal) || stem.StartsWith("LPT", System.StringComparison.Ordinal)))
                return stem[3] >= '0' && stem[3] <= '9';
            return false;
        }

        private static void Flush(StringBuilder sb, ref bool pendingSpace, ref int pendingNewlines)
        {
            if (pendingNewlines > 0) { sb.Append('\n', pendingNewlines); pendingNewlines = 0; pendingSpace = false; }
            else if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
        }

        /// <summary>
        /// Strict sanitizer for name syllables and family names: keeps only letters, combining marks,
        /// apostrophes, hyphens and single inner spaces (BMP only). Result is trimmed and length-capped.
        /// </summary>
        public static string SanitizeSyllable(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || maxLength <= 0) return "";
            var sb = new StringBuilder();
            bool pendingSpace = false;
            int marks = 0;
            for (int i = 0; i < s.Length && sb.Length < maxLength; i++)
            {
                char c = s[i];
                if (c == ' ') { if (sb.Length > 0) pendingSpace = true; marks = MaxMarksPerBase; continue; }
                bool mark = IsMark(c);
                bool ok = char.IsLetter(c) || c == '\'' || c == '-' || mark;
                if (!ok || char.IsSurrogate(c) || IsInvisibleOrBidi(c)) continue;
                if (mark) { if (++marks > MaxMarksPerBase || sb.Length == 0) continue; }
                else marks = 0;
                if (pendingSpace) { if (sb.Length + 2 > maxLength) break; sb.Append(' '); pendingSpace = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// A civilization name pattern is used with string.Format(pattern, root): it must contain exactly one
        /// "{0}" and no other brace, so it can never throw or pull extra arguments.
        /// </summary>
        public static bool IsValidCivilizationPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            int placeholders = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '{')
                {
                    if (i + 2 < pattern.Length && pattern[i + 1] == '0' && pattern[i + 2] == '}') { placeholders++; i += 2; continue; }
                    return false;
                }
                if (c == '}') return false;
            }
            return placeholders == 1;
        }
    }
}
