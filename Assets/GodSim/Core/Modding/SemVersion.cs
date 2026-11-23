using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.Modding
{
    /// <summary>
    /// Semantic version (semver 2.0.0): MAJOR.MINOR.PATCH[-prerelease][+build]. Components are capped at
    /// 999,999,999 (no overflow). Build metadata is kept but ignored for precedence.
    /// </summary>
    public sealed class SemVersion : IComparable<SemVersion>
    {
        public const int MaxLength = 64;
        public const int MaxComponent = 999999999;

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        /// <summary>Pre-release identifiers ("" when none).</summary>
        public string Prerelease { get; }
        /// <summary>Build metadata ("" when none).</summary>
        public string Build { get; }

        public SemVersion(int major, int minor, int patch, string prerelease = "", string build = "")
        {
            Major = major < 0 ? 0 : major; Minor = minor < 0 ? 0 : minor; Patch = patch < 0 ? 0 : patch;
            Prerelease = prerelease ?? ""; Build = build ?? "";
        }

        /// <summary>Parses strictly; returns false on any deviation from semver 2.0.0.</summary>
        public static bool TryParse(string s, out SemVersion version)
        {
            version = null;
            if (string.IsNullOrEmpty(s) || s.Length > MaxLength) return false;
            int pos = 0;
            if (!ReadNumber(s, ref pos, out int major) || !Expect(s, ref pos, '.')) return false;
            if (!ReadNumber(s, ref pos, out int minor) || !Expect(s, ref pos, '.')) return false;
            if (!ReadNumber(s, ref pos, out int patch)) return false;
            string pre = "", build = "";
            if (pos < s.Length && s[pos] == '-')
            {
                pos++;
                int start = pos;
                while (pos < s.Length && s[pos] != '+') pos++;
                pre = s.Substring(start, pos - start);
                if (!ValidIdentifiers(pre, true)) return false;
            }
            if (pos < s.Length && s[pos] == '+')
            {
                pos++;
                build = s.Substring(pos);
                pos = s.Length;
                if (!ValidIdentifiers(build, false)) return false;
            }
            if (pos != s.Length) return false;
            version = new SemVersion(major, minor, patch, pre, build);
            return true;
        }

        /// <summary>Parses or throws <see cref="ModLoadException"/>.</summary>
        public static SemVersion Parse(string s)
        {
            if (TryParse(s, out var v)) return v;
            throw new ModLoadException("Invalid semantic version '" + SafeJson.Printable(s) + "' (expected MAJOR.MINOR.PATCH).");
        }

        private static bool Expect(string s, ref int pos, char c)
        {
            if (pos < s.Length && s[pos] == c) { pos++; return true; }
            return false;
        }

        private static bool ReadNumber(string s, ref int pos, out int value)
        {
            value = 0;
            int start = pos;
            long v = 0;
            while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
            {
                v = v * 10 + (s[pos] - '0');
                if (v > MaxComponent) return false;
                pos++;
            }
            int len = pos - start;
            if (len == 0) return false;
            if (len > 1 && s[start] == '0') return false; // no leading zeros
            value = (int)v;
            return true;
        }

        private static bool ValidIdentifiers(string ids, bool prerelease)
        {
            if (ids.Length == 0) return false;
            var parts = ids.Split('.');
            foreach (var p in parts)
            {
                if (p.Length == 0) return false;
                bool numeric = true;
                foreach (char c in p)
                {
                    bool alnum = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '-';
                    if (!alnum) return false;
                    if (c < '0' || c > '9') numeric = false;
                }
                if (prerelease && numeric && p.Length > 1 && p[0] == '0') return false;
            }
            return true;
        }

        /// <summary>Semver precedence (build metadata ignored).</summary>
        public int CompareTo(SemVersion other)
        {
            if (other == null) return 1;
            int c = Major.CompareTo(other.Major); if (c != 0) return c;
            c = Minor.CompareTo(other.Minor); if (c != 0) return c;
            c = Patch.CompareTo(other.Patch); if (c != 0) return c;
            if (Prerelease.Length == 0) return other.Prerelease.Length == 0 ? 0 : 1;
            if (other.Prerelease.Length == 0) return -1;
            var a = Prerelease.Split('.');
            var b = other.Prerelease.Split('.');
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                bool an = IsNumeric(a[i]), bn = IsNumeric(b[i]);
                if (an && bn)
                {
                    c = a[i].Length != b[i].Length ? a[i].Length.CompareTo(b[i].Length) : string.CompareOrdinal(a[i], b[i]);
                }
                else if (an) c = -1;
                else if (bn) c = 1;
                else c = string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return c < 0 ? -1 : 1;
            }
            return a.Length.CompareTo(b.Length);
        }

        private static bool IsNumeric(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return s.Length > 0;
        }

        public override bool Equals(object obj) => obj is SemVersion v && CompareTo(v) == 0;
        public override int GetHashCode() => unchecked((Major * 397 ^ Minor) * 397 ^ Patch);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Major).Append('.').Append(Minor).Append('.').Append(Patch);
            if (Prerelease.Length > 0) sb.Append('-').Append(Prerelease);
            if (Build.Length > 0) sb.Append('+').Append(Build);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Version range: "*" (any), or comparator sets joined by "||"; each set is space-separated comparators
    /// that must all match. Comparators: "1.2.3" or "=1.2.3" (exact), "&gt;", "&gt;=", "&lt;", "&lt;=",
    /// "^1.2.3" (compatible: &lt;2.0.0, or &lt;0.3.0 for 0.2.x, &lt;0.0.4 for 0.0.3) and "~1.2.3" (&lt;1.3.0).
    /// Pre-releases compare by plain semver precedence.
    /// </summary>
    public sealed class SemVersionRange
    {
        public const int MaxLength = 128;
        private enum Op : byte { Eq, Gt, Ge, Lt, Le }
        private struct Comparator { public Op Op; public SemVersion Version; }

        private readonly List<List<Comparator>> _sets;
        private readonly string _text;

        /// <summary>The range that accepts every version.</summary>
        public static readonly SemVersionRange Any = new SemVersionRange(new List<List<Comparator>>(), "*");

        private SemVersionRange(List<List<Comparator>> sets, string text) { _sets = sets; _text = text; }

        public bool IsAny => _sets.Count == 0;

        public static bool TryParse(string s, out SemVersionRange range)
        {
            range = null;
            if (s == null || s.Length > MaxLength) return false;
            string t = s.Trim();
            if (t.Length == 0 || t == "*") { range = Any; return true; }
            var sets = new List<List<Comparator>>();
            var alternatives = t.Split(new[] { "||" }, StringSplitOptions.None);
            if (alternatives.Length > 8) return false;
            foreach (var alt in alternatives)
            {
                var tokens = alt.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0 || tokens.Length > 8) return false;
                var set = new List<Comparator>();
                foreach (var tok in tokens)
                {
                    if (tok == "*") continue;
                    if (!ParseComparator(tok, set)) return false;
                }
                if (set.Count == 0) { range = Any; return true; } // "*" alternative
                sets.Add(set);
            }
            range = new SemVersionRange(sets, t);
            return true;
        }

        public static SemVersionRange Parse(string s)
        {
            if (TryParse(s, out var r)) return r;
            throw new ModLoadException("Invalid version range '" + SafeJson.Printable(s) + "'.");
        }

        private static bool ParseComparator(string tok, List<Comparator> set)
        {
            string ops = "";
            int i = 0;
            while (i < tok.Length && (tok[i] == '>' || tok[i] == '<' || tok[i] == '=' || tok[i] == '^' || tok[i] == '~')) i++;
            ops = tok.Substring(0, i);
            if (!SemVersion.TryParse(tok.Substring(i), out var v)) return false;
            switch (ops)
            {
                case "": case "=": set.Add(new Comparator { Op = Op.Eq, Version = v }); return true;
                case ">": set.Add(new Comparator { Op = Op.Gt, Version = v }); return true;
                case ">=": set.Add(new Comparator { Op = Op.Ge, Version = v }); return true;
                case "<": set.Add(new Comparator { Op = Op.Lt, Version = v }); return true;
                case "<=": set.Add(new Comparator { Op = Op.Le, Version = v }); return true;
                case "^":
                {
                    SemVersion upper;
                    if (v.Major > 0) upper = new SemVersion(Inc(v.Major), 0, 0);
                    else if (v.Minor > 0) upper = new SemVersion(0, Inc(v.Minor), 0);
                    else upper = new SemVersion(0, 0, Inc(v.Patch));
                    set.Add(new Comparator { Op = Op.Ge, Version = v });
                    set.Add(new Comparator { Op = Op.Lt, Version = upper });
                    return true;
                }
                case "~":
                    set.Add(new Comparator { Op = Op.Ge, Version = v });
                    set.Add(new Comparator { Op = Op.Lt, Version = new SemVersion(v.Major, Inc(v.Minor), 0) });
                    return true;
                default: return false;
            }
        }

        private static int Inc(int v) => v >= SemVersion.MaxComponent ? SemVersion.MaxComponent : v + 1;

        /// <summary>True when the version satisfies the range.</summary>
        public bool Contains(SemVersion v)
        {
            if (v == null) return false;
            if (_sets.Count == 0) return true;
            foreach (var set in _sets)
            {
                bool ok = true;
                foreach (var c in set)
                {
                    int cmp = v.CompareTo(c.Version);
                    switch (c.Op)
                    {
                        case Op.Eq: ok = cmp == 0; break;
                        case Op.Gt: ok = cmp > 0; break;
                        case Op.Ge: ok = cmp >= 0; break;
                        case Op.Lt: ok = cmp < 0; break;
                        case Op.Le: ok = cmp <= 0; break;
                    }
                    if (!ok) break;
                }
                if (ok) return true;
            }
            return false;
        }

        public override string ToString() => _text;
    }
}
