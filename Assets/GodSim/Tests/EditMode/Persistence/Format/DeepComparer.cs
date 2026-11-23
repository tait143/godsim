using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace GodSim.Tests.Persistence.Format
{
    /// <summary>
    /// Reflection-based structural comparer used to prove exact round trips: walks every instance field
    /// (public and private, including auto-property backing fields) of the whole WorldState graph, compares
    /// floats bit-for-bit, sequences element-wise and dictionaries as maps. Because it is reflective, a field
    /// added to the contracts later and not persisted makes the round-trip test fail.
    /// </summary>
    public static class DeepComparer
    {
        /// <summary>Fields documented as transient (not persisted), as "DeclaringType.Field".</summary>
        public static readonly HashSet<string> Transient = new HashSet<string>(StringComparer.Ordinal) { "Settlement.AiLog" };

        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static List<string> Compare(object expected, object actual, string path = "state", ICollection<string> excluded = null, int maxDiffs = 40)
        {
            var diffs = new List<string>();
            Walk(expected, actual, path, excluded ?? Transient, diffs, maxDiffs, 0);
            return diffs;
        }

        public static bool AreEqual(object a, object b) => Compare(a, b, "x", Transient, 1).Count == 0;

        public static IEnumerable<FieldInfo> Fields(Type t)
        {
            for (var cur = t; cur != null && cur != typeof(object) && cur != typeof(ValueType); cur = cur.BaseType)
                foreach (var f in cur.GetFields(AllInstance | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        private static void Walk(object a, object b, string path, ICollection<string> ex, List<string> d, int max, int depth)
        {
            if (d.Count >= max) return;
            if (depth > 32) { d.Add(path + ": graph too deep"); return; }
            if (a == null && b == null) return;
            if (a == null || b == null) { d.Add(path + ": expected " + Show(a) + " but was " + Show(b)); return; }
            var t = a.GetType();
            if (t != b.GetType()) { d.Add(path + ": type " + t.Name + " vs " + b.GetType().Name); return; }

            if (t == typeof(float))
            {
                if (BitConverter.SingleToInt32Bits((float)a) != BitConverter.SingleToInt32Bits((float)b)) d.Add(path + ": expected " + a + " but was " + b);
                return;
            }
            if (t == typeof(double))
            {
                if (BitConverter.DoubleToInt64Bits((double)a) != BitConverter.DoubleToInt64Bits((double)b)) d.Add(path + ": expected " + a + " but was " + b);
                return;
            }
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
            {
                if (!a.Equals(b)) d.Add(path + ": expected " + Show(a) + " but was " + Show(b));
                return;
            }
            if (typeof(Delegate).IsAssignableFrom(t) || typeof(IEqualityComparer).IsAssignableFrom(t)) return;

            if (a is Array aa)
            {
                var ba = (Array)b;
                if (aa.Length != ba.Length) { d.Add(path + ": length " + aa.Length + " vs " + ba.Length); return; }
                for (int i = 0; i < aa.Length && d.Count < max; i++) Walk(aa.GetValue(i), ba.GetValue(i), path + "[" + i + "]", ex, d, max, depth + 1);
                return;
            }
            if (a is IDictionary da)
            {
                var dbb = (IDictionary)b;
                if (da.Count != dbb.Count) { d.Add(path + ": count " + da.Count + " vs " + dbb.Count); return; }
                foreach (DictionaryEntry e in da)
                {
                    if (!dbb.Contains(e.Key)) { d.Add(path + ": missing key " + e.Key); continue; }
                    Walk(e.Value, dbb[e.Key], path + "[" + e.Key + "]", ex, d, max, depth + 1);
                }
                return;
            }
            if (a is IEnumerable ea)
            {
                var la = new List<object>(); foreach (var o in ea) la.Add(o);
                var lb = new List<object>(); foreach (var o in (IEnumerable)b) lb.Add(o);
                if (la.Count != lb.Count) { d.Add(path + ": count " + la.Count + " vs " + lb.Count); return; }
                for (int i = 0; i < la.Count && d.Count < max; i++) Walk(la[i], lb[i], path + "[" + i + "]", ex, d, max, depth + 1);
                return;
            }
            foreach (var f in Fields(t))
            {
                if (ex.Contains(f.DeclaringType.Name + "." + f.Name)) continue;
                Walk(f.GetValue(a), f.GetValue(b), path + "." + f.Name, ex, d, max, depth + 1);
            }
        }

        private static string Show(object o) => o == null ? "null" : (o is string s ? "\"" + s + "\"" : o.ToString());
    }
}
