using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GodSim.Core.Persistence.Format
{
    /// <summary>
    /// Hardened little-endian reader for untrusted payloads. Every count is checked against a hard limit
    /// before anything is allocated, every enum against its definition table, every float for finiteness,
    /// every bool for 0/1, and every string for byte length, strict UTF-8 and character count. Violations
    /// throw <see cref="SaveCorruptException"/>; I/O failures (truncation) surface as EndOfStreamException,
    /// which <see cref="SaveSerializer.Deserialize"/> wraps.
    /// </summary>
    internal sealed class BoundedReader : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly Stream _stream;
        private readonly BinaryReader _r;

        public BoundedReader(Stream stream)
        {
            _stream = stream;
            _r = new BinaryReader(stream, StrictUtf8, true);
        }

        public static SaveCorruptException Corrupt(string message) => new SaveCorruptException("Corrupt save payload: " + message);

        public byte U8() => _r.ReadByte();
        public ushort U16() => _r.ReadUInt16();
        public int I32() => _r.ReadInt32();
        public uint U32() => _r.ReadUInt32();
        public long I64() => _r.ReadInt64();
        public ulong U64() => _r.ReadUInt64();

        public bool Bool(string what)
        {
            byte b = _r.ReadByte();
            if (b > 1) throw Corrupt(what + ": invalid boolean value " + b);
            return b == 1;
        }

        public float F32(string what)
        {
            float f = _r.ReadSingle();
            if (float.IsNaN(f) || float.IsInfinity(f)) throw Corrupt(what + ": non-finite float");
            return f;
        }

        public Vec2i Vec() { int x = _r.ReadInt32(); int y = _r.ReadInt32(); return new Vec2i(x, y); }

        /// <summary>Reads an i32 count and rejects negative values or values above <paramref name="max"/>.</summary>
        public int Count(int max, string what)
        {
            int n = _r.ReadInt32();
            if (n < 0 || n > max) throw Corrupt(what + ": count " + n + " outside [0, " + max + "]");
            return n;
        }

        public T Enum8<T>(string what) where T : struct, Enum
        {
            byte b = _r.ReadByte();
            if (!EnumTable<T>.Defined[b]) throw Corrupt(what + ": undefined " + typeof(T).Name + " value " + b);
            return EnumTable<T>.Values[b];
        }

        public ushort Flags16(ushort mask, string what)
        {
            ushort v = _r.ReadUInt16();
            if ((v & ~mask) != 0) throw Corrupt(what + ": undefined flag bits 0x" + v.ToString("X4"));
            return v;
        }

        /// <summary>i32 byte length (-1 = null), then strict UTF-8 bytes; at most 512 UTF-16 chars.</summary>
        public string Str(string what, bool allowNull = true)
        {
            int n = _r.ReadInt32();
            if (n == -1 && allowNull) return null;
            if (n < 0 || n > SaveFormatLimits.MaxStringBytes) throw Corrupt(what + ": string byte length " + n + " out of range");
            if (n == 0) return "";
            byte[] bytes = _r.ReadBytes(n);
            if (bytes.Length != n) throw new EndOfStreamException();
            string s = StrictUtf8.GetString(bytes);
            if (s.Length > SaveFormatLimits.MaxStringChars) throw Corrupt(what + ": string longer than " + SaveFormatLimits.MaxStringChars + " chars");
            return s;
        }

        public void ExpectSection(string tag)
        {
            for (int i = 0; i < 4; i++)
                if (_r.ReadByte() != (byte)tag[i]) throw Corrupt("expected section '" + tag + "'");
        }

        public void IntList(List<int> target, int max, string what)
        {
            int n = Count(max, what);
            for (int i = 0; i < n; i++) target.Add(_r.ReadInt32());
        }

        /// <summary>Reads a length-prefixed float array into a fixed-size target; shorter files keep the target's defaults.</summary>
        public void FloatArray(float[] target, string what)
        {
            int n = Count(target.Length, what);
            for (int i = 0; i < n; i++) target[i] = F32(what);
        }

        public void LongArray(long[] target, string what)
        {
            int n = Count(target.Length, what);
            for (int i = 0; i < n; i++) target[i] = _r.ReadInt64();
        }

        public void BoolArray(bool[] target, string what)
        {
            int n = Count(target.Length, what);
            for (int i = 0; i < n; i++) target[i] = Bool(what);
        }

        /// <summary>Count, then (key, value) pairs whose keys must be strictly increasing in ordinal order (canonical; no duplicates).</summary>
        public void Memory(Dictionary<string, float> target, string what)
        {
            int n = Count(SaveFormatLimits.MaxDictionaryEntries, what);
            string prev = null;
            for (int i = 0; i < n; i++)
            {
                string key = Str(what + " key", false);
                if (prev != null && string.CompareOrdinal(prev, key) >= 0) throw Corrupt(what + ": keys not strictly ordered");
                target.Add(key, F32(what + " value"));
                prev = key;
            }
        }

        /// <summary>True when the stream has no more bytes (consumes one byte otherwise).</summary>
        public bool AtEnd() => _stream.ReadByte() < 0;

        public void Dispose() => _r.Dispose();
    }

    /// <summary>
    /// Little-endian payload writer mirroring <see cref="BoundedReader"/>. Enforces the same limits so a file
    /// written by this build is always accepted by it; non-finite floats are written as 0 (contract rule 5
    /// forbids them in state, and a save must never become unloadable because of one).
    /// </summary>
    internal sealed class PayloadWriter : IDisposable
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);
        private readonly BinaryWriter _w;

        public PayloadWriter(Stream stream) { _w = new BinaryWriter(stream, Utf8, true); }

        public static InvalidOperationException TooBig(string message) => new InvalidOperationException("State cannot be saved: " + message);

        public void U8(byte v) => _w.Write(v);
        public void U16(ushort v) => _w.Write(v);
        public void I32(int v) => _w.Write(v);
        public void U32(uint v) => _w.Write(v);
        public void I64(long v) => _w.Write(v);
        public void U64(ulong v) => _w.Write(v);
        public void Bool(bool v) => _w.Write(v ? (byte)1 : (byte)0);
        public void F32(float v) => _w.Write(float.IsNaN(v) || float.IsInfinity(v) ? 0f : v);
        public void Vec(Vec2i v) { _w.Write(v.X); _w.Write(v.Y); }

        public void Count(int n, int max, string what)
        {
            if (n > max) throw TooBig(what + " has " + n + " entries (limit " + max + ").");
            _w.Write(n);
        }

        public void Str(string s, string what)
        {
            if (s == null) { _w.Write(-1); return; }
            if (s.Length > SaveFormatLimits.MaxStringChars) throw TooBig(what + " is longer than " + SaveFormatLimits.MaxStringChars + " chars.");
            byte[] bytes = Utf8.GetBytes(s);
            _w.Write(bytes.Length);
            _w.Write(bytes);
        }

        public void Section(string tag) { for (int i = 0; i < 4; i++) _w.Write((byte)tag[i]); }

        public void IntList(List<int> list, int max, string what)
        {
            Count(list.Count, max, what);
            for (int i = 0; i < list.Count; i++) _w.Write(list[i]);
        }

        public void FloatArray(float[] a) { _w.Write(a.Length); for (int i = 0; i < a.Length; i++) F32(a[i]); }
        public void LongArray(long[] a) { _w.Write(a.Length); for (int i = 0; i < a.Length; i++) _w.Write(a[i]); }
        public void BoolArray(bool[] a) { _w.Write(a.Length); for (int i = 0; i < a.Length; i++) Bool(a[i]); }

        public void Memory(Dictionary<string, float> dict, string what)
        {
            Count(dict.Count, SaveFormatLimits.MaxDictionaryEntries, what);
            var keys = new List<string>(dict.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                Str(keys[i], what + " key");
                F32(dict[keys[i]]);
            }
        }

        public void Flush() => _w.Flush();
        public void Dispose() => _w.Dispose();
    }
}
