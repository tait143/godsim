using System;
using System.Globalization;
using System.Text;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Sanitizers for untrusted text and configuration coming from players, UIs, config files and mods.
    /// Every method is total (never throws on bad input), bounded in time and memory, and deterministic for a given
    /// runtime's Unicode tables. Sanitize text at the input boundary (UI/config load) and store the sanitized result,
    /// so replays and saves never depend on the runtime's Unicode version.
    /// </summary>
    public static class InputSanitizer
    {
        /// <summary>Hard cap for the <c>maxLength</c> argument of <see cref="SanitizeName"/>.</summary>
        public const int MaxNameLengthLimit = 256;
        /// <summary>Maximum combining marks kept per base character (defeats "Zalgo" stacking).</summary>
        public const int MaxCombiningMarksPerBase = 2;
        /// <summary>Maximum length of a save-slot name.</summary>
        public const int MaxSlotNameLength = 32;
        /// <summary>Free-text seeds longer than this are truncated before hashing.</summary>
        public const int MaxSeedTextLength = 256;
        /// <summary>
        /// Memory budget enforced by <see cref="ClampConfig"/>: MapWidth * MapHeight * StartingRaces.Length.
        /// Allows 1024x1024 with one race, 512x512 with five races, ~286x286 with sixteen.
        /// </summary>
        public const long MaxTileRaceBudget = 512L * 512L * 5L;

        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // ================================================================== names

        /// <summary>
        /// Produces a safe display name: NFC-normalized; control, format, bidi-override/isolate (U+202A-202E,
        /// U+2066-2069), zero-width and invisible "filler" characters removed; only letters, decimal digits, space,
        /// '-' and apostrophe kept (letters/digits may carry up to <see cref="MaxCombiningMarksPerBase"/> combining
        /// marks, so accents survive); whitespace runs collapsed to one space and trimmed. The result is at most
        /// <paramref name="maxLength"/> UTF-16 code units and is truncated only between text elements (a base character
        /// plus its marks), so surrogate pairs and accented letters are never split. Returns <paramref name="fallback"/>
        /// when nothing printable (no letter or digit) remains.
        /// </summary>
        /// <param name="input">Untrusted text (may be null or arbitrarily large).</param>
        /// <param name="maxLength">Maximum result length in UTF-16 code units, clamped to [1, <see cref="MaxNameLengthLimit"/>].</param>
        /// <param name="fallback">Returned (unchanged) when the input yields no usable name; null means "Unnamed".</param>
        public static string SanitizeName(string input, int maxLength = 32, string fallback = "Unnamed")
        {
            string fb = fallback ?? "Unnamed";
            if (string.IsNullOrEmpty(input)) return fb;
            maxLength = MathUtil.Clamp(maxLength, 1, MaxNameLengthLimit);

            // 1) Bound the work: never process more than a small multiple of the output size.
            int rawLimit = maxLength * 8 + 64;
            string raw = input.Length > rawLimit ? SafeTruncate(input, rawLimit) : input;

            // 2) Remove invisible/dangerous characters and lone surrogates (so normalization cannot throw and stripped
            //    joiners cannot block composition, e.g. "e\u200D\u0301").
            string pre = StripInvisible(raw);
            if (pre.Length == 0) return fb;

            // 3) Canonical composition so "e\u0301" and "\u00E9" become the same name.
            string norm = NormalizeC(pre);

            // 4) Allow-list, whitespace collapse and text-element-safe truncation.
            string result = FilterName(norm, maxLength);
            return result.Length == 0 ? fb : result;
        }

        /// <summary>
        /// True if <paramref name="name"/> is a safe save-slot name: 1-<see cref="MaxSlotNameLength"/> ASCII characters
        /// from [A-Za-z0-9_- ], starting with a letter, digit or underscore, not ending with a space, and not a Windows
        /// reserved device name (CON, NUL, COM1, ...). Dots, slashes, colons and backslashes are never allowed, which rules
        /// out path traversal ("..", "/etc", "C:\") and alternate data streams.
        /// </summary>
        public static bool IsValidSlotName(string name)
        {
            if (name == null || name.Length == 0 || name.Length > MaxSlotNameLength) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == ' ';
                if (!ok) return false;
            }
            char first = name[0], last = name[name.Length - 1];
            if (!(IsAsciiLetterOrDigit(first) || first == '_')) return false;
            if (last == ' ') return false;
            return !IsReservedDeviceName(name);
        }

        /// <summary>
        /// Converts arbitrary text into a name that passes <see cref="IsValidSlotName"/> (disallowed characters become
        /// '_', runs collapse, length is capped). Returns <paramref name="fallback"/> (or "save" if that is invalid too)
        /// when nothing usable remains.
        /// </summary>
        public static string SanitizeSlotName(string input, string fallback = "save")
        {
            string fb = IsValidSlotName(fallback) ? fallback : "save";
            if (string.IsNullOrEmpty(input)) return fb;
            var sb = new StringBuilder(MaxSlotNameLength);
            int limit = Math.Min(input.Length, MaxSlotNameLength * 8);
            for (int i = 0; i < limit && sb.Length < MaxSlotNameLength; i++)
            {
                char c = input[i];
                char o = IsAsciiLetterOrDigit(c) || c == '-' ? c : '_';
                if (o == '_' && sb.Length > 0 && sb[sb.Length - 1] == '_') continue;
                sb.Append(o);
            }
            while (sb.Length > 0 && sb[sb.Length - 1] == '_') sb.Length--;
            while (sb.Length > 0 && sb[0] == '-') sb.Remove(0, 1);
            string s = sb.ToString();
            if (s.Length == 0 || s == "_") return fb;
            if (IsReservedDeviceName(s)) s = s.Length < MaxSlotNameLength ? "_" + s : fb;
            return IsValidSlotName(s) ? s : fb;
        }

        // ================================================================== seeds

        /// <summary>
        /// Turns free-text seed input into a world seed. Plain decimal numbers (after trimming, up to
        /// <see cref="ulong.MaxValue"/>) are used as-is so "12345" means seed 12345; any other text is cleaned
        /// (controls/format/bidi removed, NFC, whitespace collapsed, at most <see cref="MaxSeedTextLength"/> chars) and
        /// hashed with <see cref="Hashing.Fnv1a64"/>. Null or blank input hashes the empty string.
        /// </summary>
        public static ulong SanitizeSeedText(string text)
        {
            if (text == null) return Hashing.Fnv1a64(string.Empty);
            string t = text.Length > MaxSeedTextLength ? SafeTruncate(text, MaxSeedTextLength) : text;
            t = StripInvisible(t);
            t = NormalizeC(t);
            t = CollapseWhitespace(t);
            if (TryParseDecimal(t, out ulong numeric)) return numeric;
            return Hashing.Fnv1a64(t);
        }

        // ================================================================== config

        /// <summary>
        /// Clamps an untrusted configuration in place and returns it: calls <see cref="SimConfig.Sanitize"/>, then caps
        /// MapWidth * MapHeight * StartingRaces.Length to <see cref="MaxTileRaceBudget"/> by shrinking the map
        /// (preserving aspect ratio, never below the minimum map size), and lowers InitialPopulationPerRace so the
        /// founding population fits MaxPopulation. A null config yields a sanitized default config.
        /// </summary>
        public static SimConfig ClampConfig(SimConfig config)
        {
            var c = config ?? new SimConfig();
            c.Sanitize();
            int minDim = WorldMap.MinSize * 4;
            int races = Math.Max(1, c.StartingRaces.Length);
            long product = (long)c.MapWidth * c.MapHeight * races;
            if (product > MaxTileRaceBudget)
            {
                double scale = Math.Sqrt((double)MaxTileRaceBudget / product);
                int w = Math.Max(minDim, (int)(c.MapWidth * scale));
                int h = Math.Max(minDim, (int)(c.MapHeight * scale));
                while ((long)w * h * races > MaxTileRaceBudget && (w > minDim || h > minDim))
                {
                    if (w >= h && w > minDim) w--;
                    else if (h > minDim) h--;
                    else w--;
                }
                c.MapWidth = w;
                c.MapHeight = h;
            }
            long founders = (long)c.InitialPopulationPerRace * races;
            if (founders > c.MaxPopulation) c.InitialPopulationPerRace = Math.Max(2, c.MaxPopulation / races);
            return c;
        }

        // ================================================================== shared text helpers

        /// <summary>
        /// True if the text contains no control characters (optionally allowing \n, \r and \t), no format/bidi
        /// characters (U+202A-202E, U+2066-2069, U+200B-200F, U+FEFF, ...), no lone surrogates and no invisible
        /// fillers. Used by content validation for names and descriptions shown in the UI and chronicle.
        /// </summary>
        public static bool IsSafeDisplayText(string text, bool allowLineBreaks)
        {
            if (text == null) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                    if (IsInvisibleCodePoint(char.ConvertToUtf32(c, text[i + 1]))) return false;
                    if (CharUnicodeInfo.GetUnicodeCategory(text, i) == UnicodeCategory.Format) return false; // e.g. U+1D173
                    i++;
                    continue;
                }
                if (char.IsLowSurrogate(c)) return false;
                if (char.IsControl(c))
                {
                    if (allowLineBreaks && (c == '\n' || c == '\r' || c == '\t')) continue;
                    return false;
                }
                if (IsInvisibleCodePoint(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format) return false;
            }
            return true;
        }

        // ================================================================== internals

        /// <summary>Truncates without leaving a dangling high surrogate at the end.</summary>
        internal static string SafeTruncate(string s, int length)
        {
            if (s == null || s.Length <= length) return s;
            if (length <= 0) return string.Empty;
            if (char.IsHighSurrogate(s[length - 1])) length--;
            return s.Substring(0, length);
        }

        private static string NormalizeC(string s)
        {
            if (s.Length == 0) return s;
            // Invalid code points throw ArgumentException; invariant-globalization runtimes may throw
            // PlatformNotSupportedException. Either way keep the (already stripped) input: sanitizers never throw.
            try { return s.Normalize(NormalizationForm.FormC); }
            catch (Exception) { return s; }
        }

        /// <summary>
        /// Removes lone surrogates, non-whitespace controls, format characters (Cf: bidi embeddings/overrides/isolates,
        /// zero-width joiners/spaces, soft hyphen, BOM, tag characters), variation selectors, the combining grapheme
        /// joiner and Hangul filler letters. Whitespace (including controls such as TAB/LF and U+2028/2029) becomes ' '.
        /// </summary>
        private static string StripInvisible(string s)
        {
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int width = 1;
                bool drop;
                char replacement = '\0';
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        width = 2;
                        int cp = char.ConvertToUtf32(c, s[i + 1]);
                        drop = IsInvisibleCodePoint(cp) || CharUnicodeInfo.GetUnicodeCategory(s, i) == UnicodeCategory.Format;
                    }
                    else drop = true; // lone high surrogate
                }
                else if (char.IsLowSurrogate(c)) drop = true; // lone low surrogate
                else if (char.IsWhiteSpace(c))
                {
                    drop = c != ' ';
                    replacement = ' ';
                }
                else if (char.IsControl(c)) drop = true;
                else drop = IsInvisibleCodePoint(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;

                if (!drop)
                {
                    if (sb != null) { sb.Append(c); if (width == 2) sb.Append(s[i + 1]); }
                }
                else
                {
                    if (sb == null) { sb = new StringBuilder(s.Length); sb.Append(s, 0, i); }
                    if (replacement != '\0') sb.Append(replacement);
                }
                i += width - 1;
            }
            return sb == null ? s : sb.ToString();
        }

        /// <summary>Characters that render as nothing (or blank) but are not categorized as format characters everywhere.</summary>
        private static bool IsInvisibleCodePoint(int cp)
        {
            if (cp >= 0x202A && cp <= 0x202E) return true;   // bidi embeddings / overrides
            if (cp >= 0x2066 && cp <= 0x2069) return true;   // bidi isolates
            if (cp >= 0x200B && cp <= 0x200F) return true;   // zero-width space/joiners, LRM/RLM
            if (cp >= 0x2060 && cp <= 0x2064) return true;   // word joiner, invisible operators
            if (cp == 0x061C || cp == 0x180E || cp == 0xFEFF || cp == 0x00AD) return true; // ALM, MVS, BOM, soft hyphen
            if (cp == 0x034F) return true;                    // combining grapheme joiner
            if (cp >= 0xFE00 && cp <= 0xFE0F) return true;   // variation selectors
            if (cp >= 0xFFF9 && cp <= 0xFFFB) return true;   // interlinear annotation
            if (cp == 0x115F || cp == 0x1160 || cp == 0x3164 || cp == 0xFFA0) return true; // Hangul fillers (blank "letters")
            if (cp >= 0xE0000 && cp <= 0xE007F) return true; // tag characters
            if (cp >= 0xE0100 && cp <= 0xE01EF) return true; // variation selectors supplement
            return false;
        }

        private static bool IsLetterCategory(UnicodeCategory cat) =>
            cat == UnicodeCategory.UppercaseLetter || cat == UnicodeCategory.LowercaseLetter ||
            cat == UnicodeCategory.TitlecaseLetter || cat == UnicodeCategory.ModifierLetter ||
            cat == UnicodeCategory.OtherLetter;

        private static bool IsKeptMark(UnicodeCategory cat) =>
            cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark;

        private static bool IsMark(UnicodeCategory cat) => IsKeptMark(cat) || cat == UnicodeCategory.EnclosingMark;

        /// <summary>Width (1 or 2) of the code point at i, or 0 for a lone surrogate.</summary>
        private static int CodePointWidth(string s, int i)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c)) return i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 0;
            return char.IsLowSurrogate(c) ? 0 : 1;
        }

        private static string FilterName(string s, int maxLength)
        {
            var sb = new StringBuilder(Math.Min(maxLength, s.Length));
            var elem = new StringBuilder(8);
            bool pendingSpace = false, hasAlnum = false;
            int i = 0, n = s.Length;
            while (i < n)
            {
                int w = CodePointWidth(s, i);
                if (w == 0) { i++; continue; }
                char c = s[i];
                if (w == 1 && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0) pendingSpace = true;
                    i++;
                    continue;
                }
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(s, i);
                bool isBase = IsLetterCategory(cat) || cat == UnicodeCategory.DecimalDigitNumber;
                if (isBase && (w == 2 ? IsInvisibleCodePoint(char.ConvertToUtf32(c, s[i + 1])) : IsInvisibleCodePoint(c))) isBase = false;
                bool isPunct = w == 1 && (c == '-' || c == '\'');
                if (!isBase && !isPunct) { i += w; continue; } // symbols, emoji, stray marks, surrogate-category on old runtimes

                elem.Length = 0;
                elem.Append(c);
                if (w == 2) elem.Append(s[i + 1]);
                int j = i + w;
                if (isBase)
                {
                    int marks = 0;
                    while (j < n)
                    {
                        int mw = CodePointWidth(s, j);
                        if (mw == 0) break;
                        UnicodeCategory mc = CharUnicodeInfo.GetUnicodeCategory(s, j);
                        if (!IsMark(mc)) break;
                        if (IsKeptMark(mc) && marks < MaxCombiningMarksPerBase)
                        {
                            elem.Append(s[j]);
                            if (mw == 2) elem.Append(s[j + 1]);
                            marks++;
                        }
                        j += mw;
                    }
                }

                int need = elem.Length + (pendingSpace ? 1 : 0);
                if (sb.Length + need > maxLength) break; // truncate between text elements only
                if (pendingSpace) sb.Append(' ');
                pendingSpace = false;
                sb.Append(elem);
                if (isBase) hasAlnum = true;
                i = j;
            }
            return hasAlnum ? sb.ToString() : string.Empty;
        }

        private static string CollapseWhitespace(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool pending = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0) pending = true; continue; }
                if (pending) { sb.Append(' '); pending = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool TryParseDecimal(string s, out ulong value)
        {
            value = 0;
            if (s.Length == 0 || s.Length > 20) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') { value = 0; return false; }
                ulong d = (ulong)(c - '0');
                if (value > (ulong.MaxValue - d) / 10UL) { value = 0; return false; }
                value = value * 10UL + d;
            }
            return true;
        }

        private static bool IsAsciiLetterOrDigit(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        private static bool IsReservedDeviceName(string name)
        {
            string upper = name.Trim().ToUpperInvariant();
            for (int i = 0; i < ReservedDeviceNames.Length; i++)
                if (string.Equals(upper, ReservedDeviceNames[i], StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
