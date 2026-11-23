using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GodSim.Core;

namespace GodSim.AIArena
{
    public sealed class ArenaOptions
    {
        public int Seeds = 8;
        public int Years = 150;
        public int Size = 160;
        public WorldPreset Preset = WorldPreset.Continents;
        public ulong SeedBase = 1001;
        public List<ulong> SeedList;
        public string Out = "ai_quality_report.md";
        public string Json;
        public int Parallel = 1;
        public bool Threads;              // --threads: Parallel.For in-process instead of worker processes
        public int TimeoutMinutes = 60;   // per worker process
        public bool Worker;               // internal: run one seed, write JSON to WorkerOut
        public string WorkerOut;
        public string Rescore;            // --rescore results.json: re-analyze saved results without simulating
        public double MinScore = -1;      // --min-score: exit code 4 when overall rating is below
        public bool Help;

        public IEnumerable<ulong> AllSeeds() => SeedList ?? Enumerable.Range(0, Seeds).Select(i => SeedBase + (ulong)i);

        public const string Usage =
@"GodSim AI Arena - scores the civilization AI across many seeded worlds.

  --seeds N         number of seeds (default 8), seeds are seed-base .. seed-base+N-1
  --seed-base S     first seed (default 1001)
  --seed-list a,b   explicit seeds (overrides --seeds/--seed-base)
  --years Y         years per world (default 150)
  --size W          map width = height (default 160)
  --preset P        Continents | Pangaea | Archipelago | Highlands (default Continents)
  --quick           2 seeds x 40 years x 128 map
  --parallel K      run K worlds at once in separate worker processes (crash/hang isolated)
  --threads         with --parallel: use Parallel.For in-process instead of processes
                    (each sim is independent, but some Core modules keep static caches; processes are safer)
  --timeout-min M   per-worker timeout in minutes (default 60)
  --out FILE        markdown report (default ai_quality_report.md)
  --json FILE       raw results + scores as JSON
  --rescore FILE    re-score a results JSON written by --json (no simulation)
  --min-score X     exit with code 4 if the overall rating is below X (CI gate)
";

        public static ArenaOptions Parse(string[] args)
        {
            var o = new ArenaOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() { if (i + 1 >= args.Length) throw new ArgumentException("missing value for " + a); return args[++i]; }
                switch (a)
                {
                    case "--seeds": o.Seeds = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                    case "--seed-base": o.SeedBase = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--seed-list": case "--seed": o.SeedList = Next().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => ulong.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToList(); break;
                    case "--years": o.Years = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                    case "--size": o.Size = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--preset":
                        if (!Enum.TryParse(Next(), true, out o.Preset)) throw new ArgumentException("unknown preset " + args[i]);
                        break;
                    case "--quick": o.Seeds = 2; o.Years = 40; o.Size = 128; break;
                    case "--parallel": o.Parallel = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                    case "--threads": o.Threads = true; break;
                    case "--timeout-min": o.TimeoutMinutes = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                    case "--out": o.Out = Next(); break;
                    case "--json": o.Json = Next(); break;
                    case "--rescore": o.Rescore = Next(); break;
                    case "--min-score": o.MinScore = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--worker": o.Worker = true; break;
                    case "--worker-out": o.WorkerOut = Next(); break;
                    case "-h": case "--help": o.Help = true; break;
                    default: throw new ArgumentException("unknown argument " + a);
                }
            }
            return o;
        }
    }

    public static class Program
    {
        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        public static int Main(string[] args)
        {
            ArenaOptions o;
            try { o = ArenaOptions.Parse(args); }
            catch (Exception ex) { Console.Error.WriteLine("error: " + ex.Message); Console.Error.Write(ArenaOptions.Usage); return 1; }
            if (o.Help) { Console.Write(ArenaOptions.Usage); return 0; }
            if (o.Worker) return RunWorker(o);

            if (o.Rescore != null) return Finish(o, args, LoadResults(o), Stopwatch.StartNew());
            var seeds = o.AllSeeds().ToList();
            Console.Error.WriteLine($"AI Arena: {seeds.Count} seeds x {o.Years} years, {o.Size}x{o.Size} {o.Preset}, parallel {o.Parallel}{(o.Parallel > 1 ? (o.Threads ? " (threads)" : " (processes)") : "")}");
            var wall = Stopwatch.StartNew();
            var results = new SimResult[seeds.Count];
            int done = 0;
            void Report(int i)
            {
                int d = Interlocked.Increment(ref done);
                var r = results[i];
                int alive = r.Civs.Count(c => c.Initial && c.Alive), civs = r.Civs.Count(c => c.Initial);
                int pop = r.WorldPopByYear.Count > 0 ? r.WorldPopByYear[r.WorldPopByYear.Count - 1] : 0;
                Console.Error.WriteLine($"  [{d}/{seeds.Count}] seed {r.Seed}: {(r.Crashed ? "CRASH " + r.Exception : "ok")} | {r.YearsCompleted} y, civs {alive}/{civs}, pop {pop}, wars {r.WarsDeclared}, {r.AvgTickMs:0.###} ms/tick, {r.WallSeconds:0.#} s");
            }

            if (o.Parallel <= 1)
            {
                for (int i = 0; i < seeds.Count; i++) { results[i] = RunSafe(seeds[i], o); Report(i); }
            }
            else if (o.Threads)
            {
                System.Threading.Tasks.Parallel.For(0, seeds.Count, new ParallelOptions { MaxDegreeOfParallelism = o.Parallel }, i => { results[i] = RunSafe(seeds[i], o); Report(i); });
            }
            else
            {
                System.Threading.Tasks.Parallel.For(0, seeds.Count, new ParallelOptions { MaxDegreeOfParallelism = o.Parallel }, i => { results[i] = RunProcess(seeds[i], o); Report(i); });
            }

            return Finish(o, args, results.ToList(), wall);
        }

        private static List<SimResult> LoadResults(ArenaOptions o)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(o.Rescore));
            var root = doc.RootElement;
            var sims = root.TryGetProperty("sims", out var arr) ? arr : root;
            var list = JsonSerializer.Deserialize<List<SimResult>>(sims.GetRawText(), Json) ?? new List<SimResult>();
            if (list.Count > 0)
            {
                o.Years = list[0].Years; o.Size = list[0].Size;
                if (Enum.TryParse(list[0].Preset, true, out WorldPreset pr)) o.Preset = pr;
            }
            return list;
        }

        private static int Finish(ArenaOptions o, string[] args, List<SimResult> list, Stopwatch wall)
        {
            ArenaAnalysis an;
            try { an = Scoring.Analyze(list, o.Years); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: scoring failed: " + ex);
                WriteJson(o, list, null);
                return 2;
            }
            string cmd = "GodSim.AIArena " + string.Join(" ", args);
            string md = ReportWriter.Render(an, o, cmd);
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(o.Out));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(o.Out, md);
            }
            catch (Exception ex) { Console.Error.WriteLine("error: cannot write report: " + ex.Message); }
            WriteJson(o, list, an);

            Console.WriteLine($"Overall AI rating: {an.Overall:0.0}/10 ({an.Grade})  [{wall.Elapsed.TotalSeconds:0.#} s]");
            foreach (var s in an.Scores) Console.WriteLine($"  {s.Name,-26} {s.Score,5:0.0}  x{s.Weight:0.00}  {s.Inputs}");
            Console.WriteLine("Top problems:");
            foreach (var p in an.Problems.Take(5)) Console.WriteLine($"  - {p.Title} ({p.SeedsAffected}/{p.SeedsTotal} seeds, worst seed {p.Seed}): {p.Detail}");
            Console.WriteLine("Report: " + Path.GetFullPath(o.Out) + (o.Json != null ? "  JSON: " + Path.GetFullPath(o.Json) : ""));
            if (o.MinScore >= 0 && an.Overall < o.MinScore) return 4;
            return 0;
        }

        private static void WriteJson(ArenaOptions o, List<SimResult> list, ArenaAnalysis an)
        {
            if (string.IsNullOrEmpty(o.Json)) return;
            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["config"] = new { o.Years, o.Size, Preset = o.Preset.ToString(), Seeds = list.Select(r => r.Seed).ToArray() },
                    ["overall"] = an?.Overall ?? -1,
                    ["grade"] = an?.Grade ?? "",
                    ["scores"] = an?.Scores ?? new List<SubScore>(),
                    ["problems"] = an?.Problems ?? new List<Problem>(),
                    ["races"] = an != null ? an.Races.Concat(new[] { an.All }).ToList() : new List<GroupStats>(),
                    ["raceDistanceMean"] = an?.MeanRaceDistance ?? 0,
                    ["sims"] = list,
                };
                File.WriteAllText(o.Json, JsonSerializer.Serialize(payload, Json));
            }
            catch (Exception ex) { Console.Error.WriteLine("error: cannot write json: " + ex.Message); }
        }

        /// <summary>Runs one world in-process; never throws.</summary>
        private static SimResult RunSafe(ulong seed, ArenaOptions o)
        {
            try { return new SimRunner(seed, o.Years, o.Size, o.Preset).Run(); }
            catch (Exception ex) { return new SimResult { Seed = seed, Years = o.Years, Size = o.Size, Preset = o.Preset.ToString(), Crashed = true, Exception = ex.GetType().Name + ": " + ex.Message }; }
        }

        private static int RunWorker(ArenaOptions o)
        {
            var seed = o.AllSeeds().First();
            var r = RunSafe(seed, o);
            try { File.WriteAllText(o.WorkerOut, JsonSerializer.Serialize(r, Json)); }
            catch (Exception ex) { Console.Error.WriteLine("worker: cannot write result: " + ex.Message); return 3; }
            return 0;
        }

        /// <summary>Runs one world in a child process (isolates crashes, hangs and static state); never throws.</summary>
        private static SimResult RunProcess(ulong seed, ArenaOptions o)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "godsim-arena-" + Environment.ProcessId + "-" + seed + ".json");
            var fail = new SimResult { Seed = seed, Years = o.Years, Size = o.Size, Preset = o.Preset.ToString(), Crashed = true };
            try
            {
                var psi = new ProcessStartInfo { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                string host = Environment.ProcessPath ?? "dotnet";
                if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = host;
                    psi.ArgumentList.Add(typeof(Program).Assembly.Location);
                }
                else psi.FileName = host;
                foreach (var a in new[] { "--worker", "--seed-list", seed.ToString(CultureInfo.InvariantCulture), "--years", o.Years.ToString(CultureInfo.InvariantCulture),
                                          "--size", o.Size.ToString(CultureInfo.InvariantCulture), "--preset", o.Preset.ToString(), "--worker-out", tmp })
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var errTask = p.StandardError.ReadToEndAsync();
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(o.TimeoutMinutes * 60_000))
                {
                    try { p.Kill(true); } catch (Exception) { }
                    fail.Exception = "worker timed out after " + o.TimeoutMinutes + " min";
                    return fail;
                }
                p.WaitForExit();
                string err = errTask.Result;
                if (p.ExitCode != 0 || !File.Exists(tmp))
                {
                    fail.Exception = "worker exit code " + p.ExitCode + ": " + Tail(err, 400);
                    return fail;
                }
                var r = JsonSerializer.Deserialize<SimResult>(File.ReadAllText(tmp), Json);
                return r ?? fail;
            }
            catch (Exception ex) { fail.Exception = "worker launch failed: " + ex.Message; return fail; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } }
        }

        private static string Tail(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(s.Length - n) : s).Replace('\n', ' ');
    }
}
