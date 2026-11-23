using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GodSim.Core.Modding
{
    /// <summary>
    /// Strict, dependency-free RFC 8259 JSON parser for untrusted mod data.
    /// <list type="bullet">
    /// <item>Hard limits (<see cref="JsonLimits"/>) on input size, depth, string length, array/object size and total nodes.</item>
    /// <item>Rejects duplicate keys, trailing commas, comments, NaN/Infinity, leading zeros, invalid escapes,
    /// unescaped control characters, lone surrogates (raw or escaped) and non-finite numbers.</item>
    /// <item>A single leading byte-order mark is ignored; a BOM anywhere else is an error.</item>
    /// <item>Single pass, linear time (duplicate detection is O(k log k) per object); recursion is bounded by MaxDepth.</item>
    /// <item>Never throws anything but <see cref="ModLoadException"/> (with line:column).</item>
    /// </list>
    /// </summary>
    public static class SafeJson
    {
        /// <summary>Parses UTF-16 text.</summary>
        public static JsonValue Parse(string text, JsonLimits limits = null, string sourceName = null)
        {
            if (text == null) throw new ModLoadException("JSON input is null.", sourceName, 0, 0);
            var lim = (limits ?? JsonLimits.Default).Normalized();
            if (text.Length > lim.MaxInputBytes)
                throw new ModLoadException("Input too large (" + text.Length + " chars; limit " + lim.MaxInputBytes + ").", sourceName, 0, 0);
            try
            {
                return new Parser(text, lim, sourceName).ParseDocument();
            }
            catch (ModLoadException) { throw; }
            catch (Exception e)
            {
                // Defense in depth: no other exception type may escape the parser.
                throw new ModLoadException("Internal JSON parser error (" + e.GetType().Name + ").", sourceName, 0, 0, e);
            }
        }

        /// <summary>Parses strict UTF-8 bytes (invalid sequences are rejected; a leading UTF-8 BOM is ignored).</summary>
        public static JsonValue Parse(byte[] utf8, JsonLimits limits = null, string sourceName = null)
        {
            if (utf8 == null) throw new ModLoadException("JSON input is null.", sourceName, 0, 0);
            var lim = (limits ?? JsonLimits.Default).Normalized();
            if (utf8.Length > lim.MaxInputBytes)
                throw new ModLoadException("Input too large (" + utf8.Length + " bytes; limit " + lim.MaxInputBytes + ").", sourceName, 0, 0);
            return Parse(DecodeUtf8(utf8, sourceName), lim, sourceName);
        }

        /// <summary>Non-throwing variant.</summary>
        public static bool TryParse(string text, JsonLimits limits, out JsonValue value, out ModLoadException error)
        {
            try { value = Parse(text, limits); error = null; return true; }
            catch (ModLoadException e) { value = null; error = e; return false; }
        }

        /// <summary>Strict UTF-8 decoding; throws <see cref="ModLoadException"/> with the line of the first invalid byte.</summary>
        public static string DecodeUtf8(byte[] utf8, string sourceName = null)
        {
            if (utf8 == null) throw new ModLoadException("Input is null.", sourceName, 0, 0);
            try
            {
                return new UTF8Encoding(false, true).GetString(utf8, 0, utf8.Length);
            }
            catch (DecoderFallbackException e)
            {
                int index = e.Index < 0 ? 0 : Math.Min(e.Index, utf8.Length);
                int line = 1, lineStart = 0;
                for (int i = 0; i < index; i++) if (utf8[i] == (byte)'\n') { line++; lineStart = i + 1; }
                throw new ModLoadException("Invalid UTF-8 byte sequence.", sourceName, line, index - lineStart + 1);
            }
            catch (Exception e)
            {
                throw new ModLoadException("Invalid UTF-8 input (" + e.GetType().Name + ").", sourceName, 0, 0, e);
            }
        }

        // =====================================================================================
        private sealed class Parser
        {
            private readonly string _s;
            private readonly int _len;
            private readonly JsonLimits _lim;
            private readonly string _source;
            private readonly StringBuilder _sb = new StringBuilder(64);
            private int _pos;
            private int _line = 1;
            private int _lineStart;
            private int _nodes;

            public Parser(string s, JsonLimits lim, string source)
            {
                _s = s; _len = s.Length; _lim = lim; _source = source;
            }

            public JsonValue ParseDocument()
            {
                if (_len > 0 && _s[0] == '\uFEFF') { _pos = 1; _lineStart = 1; } // ignore one leading BOM
                SkipWhitespace();
                if (_pos >= _len) throw Fail("Empty document.", _pos);
                var root = ParseValue(0);
                SkipWhitespace();
                if (_pos < _len) throw Fail("Unexpected " + Describe(_s[_pos]) + " after the document's root value.", _pos);
                return root;
            }

            private JsonValue ParseValue(int depth)
            {
                if (_pos >= _len) throw Fail("Unexpected end of input; expected a value.", _pos);
                if (++_nodes > _lim.MaxTotalNodes) throw Fail("Document has more than " + _lim.MaxTotalNodes + " values.", _pos);
                int line = _line, col = _pos - _lineStart + 1;
                char c = _s[_pos];
                switch (c)
                {
                    case '{': return ParseObject(depth + 1, line, col);
                    case '[': return ParseArray(depth + 1, line, col);
                    case '"': return JsonValue.NewString(ParseString(), line, col);
                    case 't': ExpectLiteral("true"); return JsonValue.NewBool(true, line, col);
                    case 'f': ExpectLiteral("false"); return JsonValue.NewBool(false, line, col);
                    case 'n': ExpectLiteral("null"); return JsonValue.NewNull(line, col);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return JsonValue.NewNumber(ParseNumber(), line, col);
                        if (c == 'N' || c == 'I') throw Fail("NaN and Infinity are not valid JSON numbers.", _pos);
                        throw Fail("Unexpected " + Describe(c) + "; expected a value.", _pos);
                }
            }

            private JsonValue ParseObject(int depth, int line, int col)
            {
                if (depth > _lim.MaxDepth) throw Fail("Nesting deeper than " + _lim.MaxDepth + " levels.", _pos);
                _pos++; // '{'
                var keys = new List<string>();
                var keyOffsets = new List<int>();
                var values = new List<JsonValue>();
                SkipWhitespace();
                if (_pos < _len && _s[_pos] == '}') { _pos++; return JsonValue.NewObject(new string[0], new JsonValue[0], line, col); }
                while (true)
                {
                    if (_pos >= _len) throw Fail("Unterminated object.", _pos);
                    if (_s[_pos] != '"') throw Fail("Expected a string key but found " + Describe(_s[_pos]) + ".", _pos);
                    if (keys.Count >= _lim.MaxObjectKeys) throw Fail("Object has more than " + _lim.MaxObjectKeys + " members.", _pos);
                    keyOffsets.Add(_pos);
                    keys.Add(ParseString());
                    SkipWhitespace();
                    if (_pos >= _len || _s[_pos] != ':') throw Fail(_pos >= _len ? "Unterminated object; expected ':'." : "Expected ':' after key but found " + Describe(_s[_pos]) + ".", _pos);
                    _pos++;
                    SkipWhitespace();
                    values.Add(ParseValue(depth));
                    SkipWhitespace();
                    if (_pos >= _len) throw Fail("Unterminated object; expected ',' or '}'.", _pos);
                    char c = _s[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        SkipWhitespace();
                        if (_pos < _len && _s[_pos] == '}') throw Fail("Trailing comma in object.", _pos);
                        continue;
                    }
                    if (c == '}') { _pos++; break; }
                    throw Fail("Expected ',' or '}' but found " + Describe(c) + ".", _pos);
                }
                var keyArr = keys.ToArray();
                CheckDuplicateKeys(keyArr, keyOffsets);
                return JsonValue.NewObject(keyArr, values.ToArray(), line, col);
            }

            private void CheckDuplicateKeys(string[] keys, List<int> offsets)
            {
                int n = keys.Length;
                if (n < 2) return;
                if (n <= 8)
                {
                    for (int i = 1; i < n; i++)
                        for (int j = 0; j < i; j++)
                            if (string.Equals(keys[i], keys[j], StringComparison.Ordinal))
                                throw Fail("Duplicate key \"" + Printable(keys[i]) + "\".", offsets[i]);
                    return;
                }
                // O(k log k) and immune to hash flooding (no hashing of attacker-chosen keys).
                var idx = new int[n];
                for (int i = 0; i < n; i++) idx[i] = i;
                Array.Sort(idx, (a, b) =>
                {
                    int c = string.CompareOrdinal(keys[a], keys[b]);
                    return c != 0 ? c : a.CompareTo(b);
                });
                for (int i = 1; i < n; i++)
                    if (string.Equals(keys[idx[i]], keys[idx[i - 1]], StringComparison.Ordinal))
                        throw Fail("Duplicate key \"" + Printable(keys[idx[i]]) + "\".", offsets[idx[i]]);
            }

            private JsonValue ParseArray(int depth, int line, int col)
            {
                if (depth > _lim.MaxDepth) throw Fail("Nesting deeper than " + _lim.MaxDepth + " levels.", _pos);
                _pos++; // '['
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (_pos < _len && _s[_pos] == ']') { _pos++; return JsonValue.NewArray(new JsonValue[0], line, col); }
                while (true)
                {
                    if (items.Count >= _lim.MaxArrayItems) throw Fail("Array has more than " + _lim.MaxArrayItems + " items.", _pos);
                    items.Add(ParseValue(depth));
                    SkipWhitespace();
                    if (_pos >= _len) throw Fail("Unterminated array; expected ',' or ']'.", _pos);
                    char c = _s[_pos];
                    if (c == ',')
                    {
                        _pos++;
                        SkipWhitespace();
                        if (_pos < _len && _s[_pos] == ']') throw Fail("Trailing comma in array.", _pos);
                        continue;
                    }
                    if (c == ']') { _pos++; break; }
                    throw Fail("Expected ',' or ']' but found " + Describe(c) + ".", _pos);
                }
                return JsonValue.NewArray(items.ToArray(), line, col);
            }

            private string ParseString()
            {
                int start = _pos;
                _pos++; // opening quote
                _sb.Length = 0;
                int max = _lim.MaxStringLength;
                while (true)
                {
                    if (_pos >= _len) throw Fail("Unterminated string.", start);
                    char c = _s[_pos];
                    if (c == '"') { _pos++; return _sb.ToString(); }
                    if (c == '\\')
                    {
                        int escPos = _pos;
                        _pos++;
                        if (_pos >= _len) throw Fail("Unterminated string (dangling escape).", start);
                        char e = _s[_pos];
                        switch (e)
                        {
                            case '"': _sb.Append('"'); _pos++; break;
                            case '\\': _sb.Append('\\'); _pos++; break;
                            case '/': _sb.Append('/'); _pos++; break;
                            case 'b': _sb.Append('\b'); _pos++; break;
                            case 'f': _sb.Append('\f'); _pos++; break;
                            case 'n': _sb.Append('\n'); _pos++; break;
                            case 'r': _sb.Append('\r'); _pos++; break;
                            case 't': _sb.Append('\t'); _pos++; break;
                            case 'u':
                            {
                                _pos++;
                                int cu = ReadHex4(escPos);
                                if (cu >= 0xD800 && cu <= 0xDBFF)
                                {
                                    // must be immediately followed by an escaped low surrogate
                                    if (_pos + 1 < _len && _s[_pos] == '\\' && _s[_pos + 1] == 'u')
                                    {
                                        int lowPos = _pos;
                                        _pos += 2;
                                        int lo = ReadHex4(lowPos);
                                        if (lo < 0xDC00 || lo > 0xDFFF) throw Fail("Invalid UTF-16 escape: high surrogate not followed by a low surrogate.", escPos);
                                        _sb.Append((char)cu).Append((char)lo);
                                    }
                                    else throw Fail("Invalid UTF-16 escape: lone high surrogate.", escPos);
                                }
                                else if (cu >= 0xDC00 && cu <= 0xDFFF) throw Fail("Invalid UTF-16 escape: lone low surrogate.", escPos);
                                else _sb.Append((char)cu);
                                break;
                            }
                            default:
                                throw Fail("Invalid escape sequence '\\" + Printable(e.ToString()) + "'.", escPos);
                        }
                    }
                    else if (c < 0x20)
                    {
                        throw Fail("Unescaped control character " + Describe(c) + " in string.", _pos);
                    }
                    else if (char.IsHighSurrogate(c))
                    {
                        if (_pos + 1 >= _len || !char.IsLowSurrogate(_s[_pos + 1])) throw Fail("Invalid UTF-16 in string: lone high surrogate.", _pos);
                        _sb.Append(c).Append(_s[_pos + 1]);
                        _pos += 2;
                    }
                    else if (char.IsLowSurrogate(c))
                    {
                        throw Fail("Invalid UTF-16 in string: lone low surrogate.", _pos);
                    }
                    else
                    {
                        _sb.Append(c);
                        _pos++;
                    }
                    if (_sb.Length > max) throw Fail("String longer than " + max + " characters.", start);
                }
            }

            private int ReadHex4(int escPos)
            {
                if (_pos + 4 > _len) throw Fail("Unterminated \\u escape.", escPos);
                int v = 0;
                for (int i = 0; i < 4; i++)
                {
                    char h = _s[_pos + i];
                    int d;
                    if (h >= '0' && h <= '9') d = h - '0';
                    else if (h >= 'a' && h <= 'f') d = h - 'a' + 10;
                    else if (h >= 'A' && h <= 'F') d = h - 'A' + 10;
                    else throw Fail("Invalid \\u escape: '" + Printable(h.ToString()) + "' is not a hex digit.", escPos);
                    v = (v << 4) | d;
                }
                _pos += 4;
                return v;
            }

            private double ParseNumber()
            {
                int start = _pos;
                if (_s[_pos] == '-') _pos++;
                if (_pos >= _len) throw Fail("Invalid number: '-' must be followed by a digit.", start);
                char c = _s[_pos];
                if (c == '0')
                {
                    _pos++;
                    if (_pos < _len && _s[_pos] >= '0' && _s[_pos] <= '9') throw Fail("Invalid number: leading zeros are not allowed.", start);
                }
                else if (c >= '1' && c <= '9')
                {
                    while (_pos < _len && _s[_pos] >= '0' && _s[_pos] <= '9') { _pos++; CheckNumberLength(start); }
                }
                else if (c == 'I') throw Fail("NaN and Infinity are not valid JSON numbers.", start);
                else throw Fail("Invalid number: expected a digit but found " + Describe(c) + ".", _pos);

                if (_pos < _len && _s[_pos] == '.')
                {
                    _pos++;
                    if (_pos >= _len || _s[_pos] < '0' || _s[_pos] > '9') throw Fail("Invalid number: expected a digit after '.'.", _pos);
                    while (_pos < _len && _s[_pos] >= '0' && _s[_pos] <= '9') { _pos++; CheckNumberLength(start); }
                }
                if (_pos < _len && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    _pos++;
                    if (_pos < _len && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
                    if (_pos >= _len || _s[_pos] < '0' || _s[_pos] > '9') throw Fail("Invalid number: expected a digit in the exponent.", _pos);
                    while (_pos < _len && _s[_pos] >= '0' && _s[_pos] <= '9') { _pos++; CheckNumberLength(start); }
                }
                CheckNumberLength(start);
                double d;
                try
                {
                    d = double.Parse(_s.Substring(start, _pos - start), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
                }
                catch (OverflowException) { throw Fail("Number out of range.", start); }
                catch (FormatException) { throw Fail("Invalid number.", start); }
                if (double.IsNaN(d) || double.IsInfinity(d)) throw Fail("Number out of range.", start);
                return d;
            }

            private void CheckNumberLength(int start)
            {
                if (_pos - start > _lim.MaxNumberLength) throw Fail("Number literal longer than " + _lim.MaxNumberLength + " characters.", start);
            }

            private void ExpectLiteral(string lit)
            {
                if (_pos + lit.Length > _len || string.CompareOrdinal(_s, _pos, lit, 0, lit.Length) != 0)
                    throw Fail("Invalid literal; expected '" + lit + "'.", _pos);
                _pos += lit.Length;
            }

            private void SkipWhitespace()
            {
                while (_pos < _len)
                {
                    char c = _s[_pos];
                    if (c == ' ' || c == '\t') { _pos++; continue; }
                    if (c == '\n') { _pos++; _line++; _lineStart = _pos; continue; }
                    if (c == '\r')
                    {
                        _pos++;
                        if (_pos < _len && _s[_pos] == '\n') _pos++;
                        _line++; _lineStart = _pos;
                        continue;
                    }
                    break;
                }
            }

            private ModLoadException Fail(string message, int offset)
            {
                // Recompute line/col for an arbitrary offset (only runs once, on error).
                if (offset < 0) offset = 0;
                if (offset > _len) offset = _len;
                int line = 1, lineStart = 0;
                for (int i = 0; i < offset; i++)
                {
                    char c = _s[i];
                    if (c == '\n') { line++; lineStart = i + 1; }
                    else if (c == '\r') { if (i + 1 < offset && _s[i + 1] == '\n') continue; line++; lineStart = i + 1; }
                }
                return new ModLoadException(message, _source, line, offset - lineStart + 1);
            }

            private static string Describe(char c)
            {
                if (c < 0x20 || c == 0x7F || (c >= 0x80 && c < 0xA0) || char.IsSurrogate(c) || ModText.IsInvisibleOrBidi(c) || c == '\u2028' || c == '\u2029')
                    return "character U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
                return "'" + c + "'";
            }
        }

        /// <summary>Renders untrusted text safely for error messages (escapes controls, truncates).</summary>
        internal static string Printable(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            int n = Math.Min(s.Length, 64);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (c < 0x20 || c == 0x7F || (c >= 0x80 && c < 0xA0) || char.IsSurrogate(c) || ModText.IsInvisibleOrBidi(c) || c == '\u2028' || c == '\u2029')
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            if (s.Length > n) sb.Append("...");
            return sb.ToString();
        }
    }
}
