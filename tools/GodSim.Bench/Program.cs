using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using GodSim.Bench.Benchmarks;
using GodSim.Bench.Harness;
using GodSim.Core;

namespace GodSim.Bench
{
    /// <summary>
    /// GodSim performance harness.
    ///   dotnet run -c Release --project tools/GodSim.Bench -- [--quick] [--filter a,b] [--json out.json] [--budget] [--seed N] [--verbose] [--list]
    /// Exit codes: 0 ok, 1 budget exceeded (only with --budget), 2 bad arguments, 3 a benchmark case crashed (only with --budget).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            BenchOptions o;
            try { o = Parse(args); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                PrintUsage();
                return 2;
            }
            if (o == null) { PrintUsage(); return 0; }

            var cases = new List<BenchCase>();
            cases.AddRange(ContractsBenchmarks.Cases());
            cases.AddRange(WorldGenBenchmarks.Cases());
            cases.AddRange(SimBenchmarks.Cases());
            cases.AddRange(PersistenceBenchmarks.Cases());

            if (o.ListOnly)
            {
                foreach (var c in cases) Console.WriteLine(c.FullName.PadRight(28) + c.Description);
                return 0;
            }

            Timing.MinWarmupMs = o.Quick ? 200 : 500;
            var env = Environment(o);
            var results = new List<BenchResult>();
            var crashed = new List<string>();
            var total = Stopwatch.StartNew();
            int ran = 0;
            foreach (var c in cases)
            {
                if (!c.Matches(o.Filter)) continue;
                ran++;
                Console.Write("> " + c.FullName + " ... ");
                var sw = Stopwatch.StartNew();
                try
                {
                    // Materialize inside the try: cases are lazy iterators.
                    var produced = new List<BenchResult>(c.Run(o));
                    results.AddRange(produced);
                    Console.WriteLine(produced.Count + " result(s) in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
                }
                catch (Exception ex)
                {
                    // Missing modules surface as TypeLoadException/MissingMethodException when GodSim.Core lacks a type
                    // (e.g. Composition not written yet): report and continue with the other cases.
                    var root = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    crashed.Add(c.FullName);
                    results.Add(BenchResult.Fail(c.Category, c.Name, root.GetType().Name + ": " + root.Message));
                    Console.WriteLine("FAILED (" + root.GetType().Name + ": " + root.Message + ")");
                    if (o.Verbose) Console.WriteLine(root);
                }
                Timing.Settle();
            }
            if (ran == 0) { Console.Error.WriteLine("No benchmark matches filter '" + o.Filter + "'. Use --list."); return 2; }

            Console.WriteLine();
            Report.PrintTable(results);
            Report.PrintDetails(results);

            var budgets = Budgets.Evaluate(results, crashed);
            Console.WriteLine();
            Report.PrintBudgets(budgets);
            Console.WriteLine();
            Console.WriteLine("Total " + total.Elapsed.TotalSeconds.ToString("0.0") + " s, " + results.Count + " results, " + crashed.Count + " crashed case(s).");

            if (o.JsonPath != null)
            {
                Report.WriteJson(o.JsonPath, o, results, budgets, env);
                Console.WriteLine("JSON written to " + System.IO.Path.GetFullPath(o.JsonPath));
            }

            if (!o.Budget) return 0;
            bool fail = false;
            foreach (var b in budgets) fail |= b.Failed;
            if (fail) { Console.WriteLine("BUDGET CHECK FAILED"); return 1; }
            if (crashed.Count > 0) { Console.WriteLine("BUDGET CHECK: " + crashed.Count + " benchmark case(s) crashed"); return 3; }
            Console.WriteLine("BUDGET CHECK PASSED");
            return 0;
        }

        private static BenchOptions Parse(string[] args)
        {
            var o = new BenchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next()
                {
                    if (i + 1 >= args.Length) throw new ArgumentException(a + " needs a value");
                    return args[++i];
                }
                switch (a)
                {
                    case "--quick": case "-q": o.Quick = true; break;
                    case "--filter": case "-f": o.Filter = Next(); break;
                    case "--json": o.JsonPath = Next(); break;
                    case "--budget": o.Budget = true; break;
                    case "--seed":
                        string v = Next();
                        if (!ulong.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out o.Seed)) throw new ArgumentException("--seed must be an unsigned integer: " + v);
                        break;
                    case "--verbose": case "-v": o.Verbose = true; break;
                    case "--list": o.ListOnly = true; break;
                    case "--help": case "-h": case "-?": return null;
                    default: throw new ArgumentException("unknown argument: " + a);
                }
            }
            return o;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("GodSim.Bench - Stopwatch-based performance harness");
            Console.WriteLine("  --quick            fewer iterations / shorter windows (CI)");
            Console.WriteLine("  --filter <a,b>     run cases whose 'category/name' contains any of the substrings (see --list)");
            Console.WriteLine("  --json <file>      write machine-readable results");
            Console.WriteLine("  --budget           exit 1 if a performance budget is exceeded (3 if a case crashed)");
            Console.WriteLine("  --seed <n>         world/RNG seed (default 20261119)");
            Console.WriteLine("  --verbose          echo simulation warnings/errors and stack traces");
            Console.WriteLine("  --list             list benchmark cases");
        }

        private static EnvInfo Environment(BenchOptions o)
        {
            var env = new EnvInfo
            {
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                CpuCount = System.Environment.ProcessorCount,
                ServerGc = GCSettings.IsServerGC,
                TieredPgo = AppContext.GetData("System.Runtime.TieredPGO")?.ToString() ?? "default",
                BenchOptimized = IsOptimized(typeof(Program).Assembly),
                CoreOptimized = IsOptimized(typeof(WorldState).Assembly),
                StopwatchFrequency = Stopwatch.Frequency,
            };
            Console.WriteLine("GodSim.Bench  " + env.Runtime + "  " + env.Os + "  cpus=" + env.CpuCount
                + "  serverGC=" + env.ServerGc + "  tieredPGO=" + env.TieredPgo + "  seed=" + o.Seed + (o.Quick ? "  [quick]" : "")
                + (o.Filter != null ? "  filter=" + o.Filter : ""));
            if (!env.BenchOptimized || !env.CoreOptimized)
                Console.WriteLine("WARNING: " + (!env.CoreOptimized ? "GodSim.Core" : "GodSim.Bench") + " was built without optimizations (Debug). Use -c Release; numbers are not representative.");
            if (Debugger.IsAttached) Console.WriteLine("WARNING: debugger attached; numbers are not representative.");
            return env;
        }

        private static bool IsOptimized(Assembly asm)
        {
            var d = asm.GetCustomAttribute<DebuggableAttribute>();
            return d == null || !d.IsJITOptimizerDisabled;
        }
    }
}
