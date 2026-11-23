using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GodSim.Core.Modding
{
    /// <summary>JSON value kinds produced by <see cref="SafeJson"/>.</summary>
    public enum JsonKind : byte { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Immutable JSON DOM node. Objects keep their members in document order (duplicate keys are rejected by
    /// the parser). Accessors never throw: wrong-kind or out-of-range access returns a neutral value or null.
    /// </summary>
    public sealed class JsonValue
    {
        private static readonly JsonValue[] NoValues = new JsonValue[0];
        private static readonly string[] NoKeys = new string[0];

        private readonly bool _bool;
        private readonly double _number;
        private readonly string _string;
        private readonly JsonValue[] _values;   // array items or object member values
        private readonly string[] _keys;        // object member keys (parallel to _values)

        /// <summary>Kind of this node.</summary>
        public JsonKind Kind { get; }
        /// <summary>1-based line of the first character of this value (0 if constructed in code).</summary>
        public int Line { get; }
        /// <summary>1-based column of the first character of this value.</summary>
        public int Column { get; }

        private JsonValue(JsonKind kind, int line, int column, bool b, double n, string s, JsonValue[] values, string[] keys)
        {
            Kind = kind; Line = line; Column = column;
            _bool = b; _number = n; _string = s;
            _values = values ?? NoValues;
            _keys = keys ?? NoKeys;
        }

        internal static JsonValue NewNull(int line, int col) => new JsonValue(JsonKind.Null, line, col, false, 0, null, null, null);
        internal static JsonValue NewBool(bool v, int line, int col) => new JsonValue(JsonKind.Bool, line, col, v, 0, null, null, null);
        internal static JsonValue NewNumber(double v, int line, int col) => new JsonValue(JsonKind.Number, line, col, false, v, null, null, null);
        internal static JsonValue NewString(string v, int line, int col) => new JsonValue(JsonKind.String, line, col, false, 0, v ?? "", null, null);
        internal static JsonValue NewArray(JsonValue[] items, int line, int col) => new JsonValue(JsonKind.Array, line, col, false, 0, null, items, null);
        internal static JsonValue NewObject(string[] keys, JsonValue[] values, int line, int col) => new JsonValue(JsonKind.Object, line, col, false, 0, null, values, keys);

        public bool IsNull => Kind == JsonKind.Null;
        public bool IsBool => Kind == JsonKind.Bool;
        public bool IsNumber => Kind == JsonKind.Number;
        public bool IsString => Kind == JsonKind.String;
        public bool IsArray => Kind == JsonKind.Array;
        public bool IsObject => Kind == JsonKind.Object;

        /// <summary>Boolean value (false for other kinds).</summary>
        public bool BoolValue => _bool;
        /// <summary>Number value (always finite; 0 for other kinds).</summary>
        public double NumberValue => _number;
        /// <summary>String value (null for other kinds).</summary>
        public string StringValue => _string;

        /// <summary>Item count for arrays, member count for objects, 0 otherwise.</summary>
        public int Count => Kind == JsonKind.Array || Kind == JsonKind.Object ? _values.Length : 0;

        /// <summary>Array item (or object member value) at index, or null when out of range.</summary>
        public JsonValue Item(int index) => index >= 0 && index < _values.Length ? _values[index] : null;

        /// <summary>Object key at index, or null.</summary>
        public string KeyAt(int index) => Kind == JsonKind.Object && index >= 0 && index < _keys.Length ? _keys[index] : null;

        /// <summary>Array items (empty for non-arrays).</summary>
        public IReadOnlyList<JsonValue> Items => Kind == JsonKind.Array ? _values : NoValues;

        /// <summary>Object keys in document order (empty for non-objects).</summary>
        public IReadOnlyList<string> Keys => Kind == JsonKind.Object ? _keys : NoKeys;

        /// <summary>Object member values in document order (parallel to <see cref="Keys"/>).</summary>
        public IReadOnlyList<JsonValue> Values => Kind == JsonKind.Object ? _values : NoValues;

        /// <summary>Member value by exact (ordinal) key, or null when absent or not an object.</summary>
        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object || key == null) return null;
            for (int i = 0; i < _keys.Length; i++)
                if (string.Equals(_keys[i], key, StringComparison.Ordinal)) return _values[i];
            return null;
        }

        public bool TryGet(string key, out JsonValue value) { value = Get(key); return value != null; }

        /// <summary>Compact, valid JSON text for this value (round-trips through <see cref="SafeJson"/>).</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            Write(sb, this, 0);
            return sb.ToString();
        }

        public override string ToString() => ToJson();

        private static void Write(StringBuilder sb, JsonValue v, int depth)
        {
            if (depth > JsonLimits.DepthCeiling) { sb.Append("null"); return; }
            switch (v.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(v._bool ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(FormatNumber(v._number)); break;
                case JsonKind.String: WriteString(sb, v._string); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < v._values.Length; i++) { if (i > 0) sb.Append(','); Write(sb, v._values[i], depth + 1); }
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    sb.Append('{');
                    for (int i = 0; i < v._values.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteString(sb, v._keys[i]);
                        sb.Append(':');
                        Write(sb, v._values[i], depth + 1);
                    }
                    sb.Append('}');
                    break;
            }
        }

        /// <summary>Invariant, round-trippable number formatting.</summary>
        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
