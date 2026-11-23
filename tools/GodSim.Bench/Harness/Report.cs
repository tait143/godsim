using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GodSim.Bench.Harness
{
    /// <summary>Console table + machine-readable JSON (hand-written, no System.Text.Json dependency).</summary>
    public static class Report
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FormatNs(double ns)
        {
            if (double.IsNaN(ns)) return "-";
            if (ns < 1e3) return ns.ToString("0.0", Inv) + " ns";
            if (ns < 1e6) return (ns / 1e3).ToString("0.00", Inv) + " us";
            if (ns < 1e9) return (ns / 1e6).ToString("0.000", Inv) + " ms";
            return (ns / 1e9).ToString("0.00", Inv) + " s";
        }

        public static string FormatBytes(double b)
        {
            if (double.IsNaN(b)) return "-";
            if (b < 0.05) return "0 B";
            if (b < 1024) return b.ToString(b < 10 ? "0.0" : "0", Inv) + " B";
            if (b < 1048576) return (b / 1024).ToString("0.0", Inv) + " KB";
            return (b / 1048576).ToString("0.0", Inv) + " MB";
        }

        public static void PrintTable(IReadOnlyList<BenchResult> results)
        {
            var rows = new List<string[]>();
            rows.Add(new[] { "Benchmark", "op", "n", "median", "p95", "mean", "alloc/op", "gc0/1/2" });
            foreach (var r in results)
            {
                if (r.Error != null) { rows.Add(new[] { r.FullName, "", "", "ERROR", "", "", "", "" }); continue; }
                if (r.Skipped) { rows.Add(new[] { r.FullName, "", "", "SKIPPED", "", "", "", "" }); continue; }
                rows.Add(new[]
                {
                    r.FullName, r.OpName, r.TotalOps.ToString(Inv), FormatNs(r.MedianNs), FormatNs(r.P95Ns), FormatNs(r.MeanNs),
                    FormatBytes(r.AllocBytesPerOp), r.Gen0 + "/" + r.Gen1 + "/" + r.Gen2,
                });
            }
            WriteTable(rows, new[] { false, false, true, true, true, true, true, true });

            foreach (var r in results)
            {
                if (r.Error != null) Console.WriteLine("  ! " + r.FullName + ": " + r.Error);
                else if (r.Note != null) Console.WriteLine("  * " + r.FullName + ": " + r.Note);
            }
        }

        public static void PrintDetails(IReadOnlyList<BenchResult> results)
        {
            foreach (var r in results)
            {
                if (r.Metrics.Count == 0 && r.Systems.Count == 0) continue;
                if (r.Systems.Count == 0 && r.Category == "contracts" && r.Metrics.Count <= 1) continue;
                Console.WriteLine();
                Console.WriteLine("-- " + r.FullName);
                if (r.Metrics.Count > 0)
                {
                    var sb = new StringBuilder("   ");
                    int col = 3;
                    foreach (var kv in r.Metrics)
                    {
                        string item = kv.Key + "=" + Num(kv.Value) + "  ";
                        if (col + item.Length > 118) { sb.Append('\n').Append("   "); col = 3; }
                        sb.Append(item);
                        col += item.Length;
                    }
                    Console.WriteLine(sb.ToString().TrimEnd());
                }
                if (r.Systems.Count > 0)
                {
                    var rows = new List<string[]> { new[] { "system", "interval", "runs", "ms/tick", "ms/run", "max ms", "engine AvgMs", "% of tick", "faults" } };
                    var order = new List<SystemRow>(r.Systems);
                    order.Sort((a, b) => b.MsPerTick.CompareTo(a.MsPerTick));
                    double tickMs = r.MeanMs;
                    double sumSys = 0;
                    foreach (var s in order) sumSys += s.MsPerTick;
                    foreach (var s in order)
                        rows.Add(new[]
                        {
                            s.Name + (s.Disabled ? " (DISABLED)" : ""), s.TickInterval.ToString(Inv), s.Runs.ToString(Inv),
                            s.MsPerTick.ToString("0.0000", Inv), s.MsPerRun.ToString("0.0000", Inv), s.MaxRunMs.ToString("0.000", Inv),
                            s.EngineAvgMs.ToString("0.0000", Inv), tickMs > 0 ? (100 * s.MsPerTick / tickMs).ToString("0.0", Inv) + "%" : "-",
                            s.Faults.ToString(Inv),
                        });
                    rows.Add(new[] { "(sum of systems)", "", "", sumSys.ToString("0.0000", Inv), "", "", "", tickMs > 0 ? (100 * sumSys / tickMs).ToString("0.0", Inv) + "%" : "-", "" });
                    WriteTable(rows, new[] { false, true, true, true, true, true, true, true, true }, "   ");
                    foreach (var s in order) if (s.LastError != null) Console.WriteLine("   ! " + s.Name + " last error: " + s.LastError);
                }
            }
        }

        public static void PrintBudgets(IReadOnlyList<BudgetCheck> budgets)
        {
            var rows = new List<string[]> { new[] { "Budget", "limit", "actual", "status", "detail" } };
            foreach (var b in budgets)
                rows.Add(new[] { b.Name, Num(b.Limit) + " " + b.Unit, double.IsNaN(b.Actual) ? "-" : Num(b.Actual) + " " + b.Unit, b.Status, b.Detail });
            WriteTable(rows, new[] { false, true, true, false, false });
        }

        private static void WriteTable(List<string[]> rows, bool[] rightAlign, string indent = "")
        {
            int cols = rows[0].Length;
            var w = new int[cols];
            foreach (var row in rows) for (int c = 0; c < cols; c++) w[c] = Math.Max(w[c], (row[c] ?? "").Length);
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var sb = new StringBuilder(indent);
                for (int c = 0; c < cols; c++)
                {
                    string cell = rows[ri][c] ?? "";
                    sb.Append(rightAlign[c] ? cell.PadLeft(w[c]) : cell.PadRight(w[c]));
                    if (c < cols - 1) sb.Append("  ");
                }
                Console.WriteLine(sb.ToString().TrimEnd());
                if (ri == 0)
                {
                    var line = new StringBuilder(indent);
                    for (int c = 0; c < cols; c++) { line.Append(new string('-', w[c])); if (c < cols - 1) line.Append("  "); }
                    Console.WriteLine(line.ToString());
                }
            }
        }

        private static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
            if (v != 0 && Math.Abs(v) < 1e-3) return v.ToString("0.###E+0", Inv);
            if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15) return ((long)Math.Round(v)).ToString(Inv);
            return Math.Abs(v) >= 100 ? v.ToString("0.0", Inv) : v.ToString("0.####", Inv);
        }

        // =========================================================================== JSON
        public static void WriteJson(string path, BenchOptions o, IReadOnlyList<BenchResult> results, IReadOnlyList<BudgetCheck> budgets, EnvInfo env)
        {
            var j = new JsonOut();
            j.BeginObject();
            j.Prop("schema", "godsim.bench/1");
            j.Prop("timestampUtc", DateTime.UtcNow.ToString("o", Inv));
            j.Key("options"); j.BeginObject();
            j.Prop("quick", o.Quick); j.Prop("filter", o.Filter); j.Prop("seed", o.Seed.ToString(Inv)); j.Prop("budget", o.Budget);
            j.EndObject();
            j.Key("environment"); j.BeginObject();
            j.Prop("runtime", env.Runtime); j.Prop("os", env.Os); j.Prop("cpuCount", env.CpuCount); j.Prop("serverGC", env.ServerGc);
            j.Prop("tieredPGO", env.TieredPgo); j.Prop("benchOptimized", env.BenchOptimized); j.Prop("coreOptimized", env.CoreOptimized);
            j.Prop("stopwatchHz", env.StopwatchFrequency);
            j.EndObject();

            j.Key("results"); j.BeginArray();
            foreach (var r in results)
            {
                j.BeginObject();
                j.Prop("name", r.FullName); j.Prop("category", r.Category); j.Prop("op", r.OpName);
                j.Prop("status", r.Error != null ? "error" : r.Skipped ? "skipped" : "ok");
                if (r.Error != null) j.Prop("error", r.Error);
                if (r.Note != null) j.Prop("note", r.Note);
                if (r.HasTiming)
                {
                    j.Prop("iterations", r.Iterations); j.Prop("opsPerIteration", r.OpsPerIteration); j.Prop("totalOps", r.TotalOps);
                    j.Prop("medianNs", r.MedianNs); j.Prop("p95Ns", r.P95Ns); j.Prop("meanNs", r.MeanNs); j.Prop("minNs", r.MinNs); j.Prop("maxNs", r.MaxNs);
                    j.Prop("allocBytesPerOp", r.AllocBytesPerOp); j.Prop("allocBytesTotal", r.AllocBytesTotal);
                    j.Prop("gen0", r.Gen0); j.Prop("gen1", r.Gen1); j.Prop("gen2", r.Gen2);
                }
                if (r.Metrics.Count > 0)
                {
                    j.Key("metrics"); j.BeginObject();
                    foreach (var kv in r.Metrics) j.Prop(kv.Key, kv.Value);
                    j.EndObject();
                }
                if (r.Systems.Count > 0)
                {
                    j.Key("systems"); j.BeginArray();
                    foreach (var s in r.Systems)
                    {
                        j.BeginObject();
                        j.Prop("name", s.Name); j.Prop("tickInterval", s.TickInterval); j.Prop("runs", s.Runs);
                        j.Prop("totalMs", s.TotalMs); j.Prop("msPerTick", s.MsPerTick); j.Prop("msPerRun", s.MsPerRun);
                        j.Prop("maxRunMs", s.MaxRunMs); j.Prop("engineAvgMs", s.EngineAvgMs); j.Prop("faults", s.Faults);
                        j.Prop("disabled", s.Disabled);
                        if (s.LastError != null) j.Prop("lastError", s.LastError);
                        j.EndObject();
                    }
                    j.EndArray();
                }
                j.EndObject();
            }
            j.EndArray();

            j.Key("budgets"); j.BeginArray();
            foreach (var b in budgets)
            {
                j.BeginObject();
                j.Prop("name", b.Name); j.Prop("benchmark", b.Benchmark); j.Prop("unit", b.Unit); j.Prop("limit", b.Limit);
                j.Prop("actual", b.Actual); j.Prop("status", b.Status); j.Prop("detail", b.Detail);
                j.EndObject();
            }
            j.EndArray();
            bool anyFail = false;
            foreach (var b in budgets) anyFail |= b.Failed;
            j.Prop("budgetsPassed", !anyFail);
            j.EndObject();

            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(full, j.ToString(), new UTF8Encoding(false));
        }

        private sealed class JsonOut
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly Stack<bool> _first = new Stack<bool>();
            private bool _afterKey;
            private int Depth => _first.Count;

            private void Sep()
            {
                if (_afterKey) { _afterKey = false; return; }
                if (_first.Count > 0)
                {
                    if (!_first.Peek()) _sb.Append(',');
                    _first.Pop(); _first.Push(false);
                    _sb.Append('\n').Append(' ', Depth * 2);
                }
            }

            public void BeginObject() { Sep(); _sb.Append('{'); _first.Push(true); }
            public void EndObject() { bool empty = _first.Pop(); if (!empty) _sb.Append('\n').Append(' ', Depth * 2); _sb.Append('}'); }
            public void BeginArray() { Sep(); _sb.Append('['); _first.Push(true); }
            public void EndArray() { bool empty = _first.Pop(); if (!empty) _sb.Append('\n').Append(' ', Depth * 2); _sb.Append(']'); }
            public void Key(string k) { Sep(); Str(k); _sb.Append(": "); _afterKey = true; }
            public void Prop(string k, string v) { Key(k); Sep(); if (v == null) _sb.Append("null"); else Str(v); }
            public void Prop(string k, bool v) { Key(k); Sep(); _sb.Append(v ? "true" : "false"); }
            public void Prop(string k, long v) { Key(k); Sep(); _sb.Append(v.ToString(Inv)); }
            public void Prop(string k, double v)
            {
                Key(k); Sep();
                if (double.IsNaN(v) || double.IsInfinity(v)) _sb.Append("null");
                else _sb.Append(v.ToString("R", Inv));
            }

            private void Str(string s)
            {
                _sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                            else _sb.Append(c);
                            break;
                    }
                }
                _sb.Append('"');
            }

            public override string ToString() => _sb.ToString() + "\n";
        }
    }

    public sealed class EnvInfo
    {
        public string Runtime;
        public string Os;
        public int CpuCount;
        public bool ServerGc;
        public string TieredPgo;
        public bool BenchOptimized;
        public bool CoreOptimized;
        public long StopwatchFrequency;
    }
}
