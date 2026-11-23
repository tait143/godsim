using System.Collections.Generic;

namespace GodSim.Bench.Harness
{
    /// <summary>
    /// CI performance budgets (docs/ENGINEERING_CONTRACT.md section 3.6 + bench contract). With --budget the
    /// process exits 1 if any budget FAILs or its benchmark ERRORs. Budgets whose benchmark was filtered out or
    /// skipped are reported as SKIPPED and do not fail the run.
    /// </summary>
    public static class Budgets
    {
        public static List<BudgetCheck> Evaluate(IReadOnlyList<BenchResult> results, IReadOnlyCollection<string> errorCases)
        {
            var list = new List<BudgetCheck>
            {
                MeanMs(results, errorCases, "5,000 people < 3 ms/tick (mean)", "stress/people5000", 3.0),
                ZeroAlloc(results, errorCases, "EventBus publish, 0 subscribers: 0 B", "contracts/eventbus.publish(0 subscribers)"),
                ZeroAlloc(results, errorCases, "EventBus publish, 5 subscribers: 0 B", "contracts/eventbus.publish(5 subscribers)"),
                MedianMs(results, errorCases, "Pathfinder 128-tile path < 0.5 ms (open, median)", "contracts/pathfinder.path128.open.straight", 0.5),
                MedianMs(results, errorCases, "Pathfinder 128-tile path < 0.5 ms (22% obstacles, median)", "contracts/pathfinder.path128.obstacles22pct", 0.5),
            };
            return list;
        }

        private static BenchResult Find(IReadOnlyList<BenchResult> results, string fullName)
        {
            for (int i = 0; i < results.Count; i++) if (results[i].FullName == fullName) return results[i];
            return null;
        }

        private static BudgetCheck Start(IReadOnlyList<BenchResult> results, IReadOnlyCollection<string> errorCases, string name, string bench, string unit, double limit, out BenchResult r)
        {
            var c = new BudgetCheck { Name = name, Benchmark = bench, Unit = unit, Limit = limit };
            r = Find(results, bench);
            if (r == null)
            {
                // errorCases = full names of BenchCases that threw; a budget belongs to the case its name starts with.
                foreach (var e in errorCases)
                    if (bench.StartsWith(e, System.StringComparison.OrdinalIgnoreCase))
                    { c.Status = "ERROR"; c.Detail = "benchmark case '" + e + "' failed"; return c; }
                c.Status = "SKIPPED"; c.Detail = "not run (filtered out)";
                return c;
            }
            if (r.Error != null) { c.Status = "ERROR"; c.Detail = r.Error; r = null; return c; }
            if (r.Skipped) { c.Status = "SKIPPED"; c.Detail = r.Note ?? "skipped"; r = null; return c; }
            return c;
        }

        private static BudgetCheck Judge(BudgetCheck c, double actual)
        {
            c.Actual = actual;
            c.Status = actual <= c.Limit ? "PASS" : "FAIL";
            return c;
        }

        private static BudgetCheck MeanMs(IReadOnlyList<BenchResult> rs, IReadOnlyCollection<string> err, string name, string bench, double limit)
        {
            var c = Start(rs, err, name, bench, "ms/tick", limit, out var r);
            if (r == null) return c;
            Judge(c, r.MeanMs);
            if (r.TryGetMetric("people_at_window_start", out double pop)) c.Detail = "people=" + pop.ToString("0") + ", p95=" + r.P95Ms.ToString("0.000") + " ms";
            if (r.TryGetMetric("system_faults_in_window", out double f) && f > 0) c.Detail += ", WARNING " + f + " system faults (faulting systems are cheap)";
            return c;
        }

        private static BudgetCheck MedianMs(IReadOnlyList<BenchResult> rs, IReadOnlyCollection<string> err, string name, string bench, double limit)
        {
            var c = Start(rs, err, name, bench, "ms", limit, out var r);
            if (r == null) return c;
            Judge(c, r.MedianMs);
            c.Detail = "p95=" + r.P95Ms.ToString("0.000") + " ms";
            if (r.TryGetMetric("path_tiles", out double t)) c.Detail += ", tiles=" + t;
            return c;
        }

        private static BudgetCheck ZeroAlloc(IReadOnlyList<BenchResult> rs, IReadOnlyCollection<string> err, string name, string bench)
        {
            var c = Start(rs, err, name, bench, "bytes", 0, out var r);
            if (r == null) return c;
            Judge(c, r.AllocBytesTotal);
            c.Detail = r.TotalOps + " publishes, " + r.AllocBytesTotal + " B total";
            return c;
        }
    }
}
