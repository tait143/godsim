using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Headless
{
    /// <summary>Process exit codes of the headless runner.</summary>
    public static class ExitCodes
    {
        public const int Ok = 0;
        public const int BadArguments = 1;
        public const int SimulationFault = 2;
        public const int IoError = 3;
    }

    /// <summary>Entry point: parse options, create/load a world, run it, report, export.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            HeadlessOptions options;
            try { options = HeadlessOptions.Parse(args); }
            catch (UsageException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                Console.Error.WriteLine();
                Console.Error.Write(HeadlessOptions.Usage);
                return ExitCodes.BadArguments;
            }
            if (options.ShowHelp)
            {
                Console.Write(HeadlessOptions.Usage);
                return ExitCodes.Ok;
            }
            return new HeadlessRunner(options).Run();
        }
    }

    /// <summary>One headless run. Separated from Main so tools/tests can drive it.</summary>
    public sealed class HeadlessRunner
    {
        private readonly HeadlessOptions _o;
        private readonly HeadlessLogger _log;
        private readonly Dictionary<int, char> _civLetters = new Dictionary<int, char>();
        private readonly HistoryRecorder _history = new HistoryRecorder();
        private List<double> _tickMs;

        public HeadlessRunner(HeadlessOptions options)
        {
            _o = options ?? throw new ArgumentNullException(nameof(options));
            _log = new HeadlessLogger(LogLevel.Warning);
        }

        public int Run()
        {
            var config = BuildConfig();
            GodSimulation sim;

            // ------------------------------------------------------------ create / load
            if (_o.LoadPath != null)
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(_o.LoadPath); }
                catch (Exception ex) when (IsIo(ex))
                {
                    Console.Error.WriteLine("error: cannot read save '" + _o.LoadPath + "': " + ex.Message);
                    return ExitCodes.IoError;
                }
                try { sim = GodSimulation.Load(bytes, config, _log); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("error: save '" + _o.LoadPath + "' could not be loaded: " + ex.GetType().Name + ": " + ex.Message);
                    return ExitCodes.IoError;
                }
                if (sim == null) { Console.Error.WriteLine("error: save '" + _o.LoadPath + "' could not be loaded."); return ExitCodes.IoError; }
            }
            else
            {
                try { sim = GodSimulation.CreateNew(config, _log); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("fatal: world creation failed: " + ex);
                    return ExitCodes.SimulationFault;
                }
            }

            PrintHeader(sim);
            long startTick = sim.Tick;
            long totalTicks = (long)_o.Years * SimTime.TicksPerYear;
            PowersDemo demo = null;
            if (_o.PowersDemo)
            {
                demo = new PowersDemo(startTick, totalTicks, !_o.Quiet);
                demo.Attach(sim.Events);
            }
            if (_o.Benchmark) _tickMs = new List<double>((int)Math.Min(totalTicks, 1_000_000));

            // ------------------------------------------------------------ run
            _history.Record(sim.State);
            if (!_o.Quiet) { PrintTableHeader(sim); PrintRow(sim, 0.0); }
            var total = Stopwatch.StartNew();
            Exception fault = null;
            int interval = _o.EffectiveInterval;
            double intervalMs = 0; long intervalTicks = 0;
            try
            {
                for (int year = 1; year <= _o.Years; year++)
                {
                    for (int t = 0; t < SimTime.TicksPerYear; t++)
                    {
                        demo?.BeforeStep(sim);
                        long t0 = Stopwatch.GetTimestamp();
                        sim.Step();
                        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        intervalMs += ms; intervalTicks++;
                        _tickMs?.Add(ms);
                    }
                    if (year % interval == 0 || year == _o.Years)
                    {
                        _history.Record(sim.State);
                        if (!_o.Quiet) PrintRow(sim, intervalTicks > 0 ? intervalMs / intervalTicks : 0.0);
                        intervalMs = 0; intervalTicks = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                fault = ex;
            }
            total.Stop();

            // ------------------------------------------------------------ report
            if (!_o.Quiet) PrintHighlights(sim);
            PrintStats(sim, total.Elapsed.TotalSeconds, sim.Tick - startTick, demo);
            PrintSystems(sim);
            bool anyFault = PrintFaults(sim);
            if (_o.Benchmark) PrintBenchmark(sim, total.Elapsed.TotalSeconds);
            if (_o.Ascii) { Console.WriteLine(); Console.Write(AsciiMap.Render(sim.State, _civLetters)); }

            if (fault != null)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("FATAL: simulation aborted at tick " + sim.Tick + " (" + SimTime.Format(sim.Tick) + "): " + fault);
                return ExitCodes.SimulationFault;
            }

            if (_o.PrintHash)
            {
                ulong hash;
                try { hash = sim.ComputeHash(); }
                catch (Exception ex) { Console.Error.WriteLine("error: state hash failed: " + ex.Message); return ExitCodes.SimulationFault; }
                Console.WriteLine("hash: 0x" + hash.ToString("X16"));
            }

            // ------------------------------------------------------------ outputs
            if (_o.SavePath != null)
            {
                byte[] data;
                try { data = sim.Save(); }
                catch (Exception ex) { Console.Error.WriteLine("error: save serialization failed: " + ex.GetType().Name + ": " + ex.Message); return ExitCodes.SimulationFault; }
                try
                {
                    WriteAtomic(_o.SavePath, data);
                    Console.WriteLine("Saved " + data.Length.ToString("N0") + " bytes to " + _o.SavePath);
                }
                catch (Exception ex) when (IsIo(ex))
                {
                    Console.Error.WriteLine("error: cannot write save '" + _o.SavePath + "': " + ex.Message);
                    return ExitCodes.IoError;
                }
            }
            if (_o.OutPath != null)
            {
                try
                {
                    SnapshotExporter.ExportToFile(sim, _history.Samples, _o.OutPath);
                    Console.WriteLine("Snapshot written to " + _o.OutPath + " (" + new FileInfo(_o.OutPath).Length.ToString("N0") + " bytes)");
                }
                catch (Exception ex) when (IsIo(ex))
                {
                    Console.Error.WriteLine("error: cannot write snapshot '" + _o.OutPath + "': " + ex.Message);
                    return ExitCodes.IoError;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("error: snapshot export failed: " + ex);
                    return ExitCodes.SimulationFault;
                }
            }

            if (_o.Strict && anyFault) return ExitCodes.SimulationFault;
            return ExitCodes.Ok;
        }

        // ================================================================ setup
        private SimConfig BuildConfig()
        {
            var c = new SimConfig
            {
                Seed = _o.Seed,
                MapWidth = _o.Width,
                MapHeight = _o.Height,
                Preset = _o.Preset,
                StrictMode = _o.Strict,
            };
            if (_o.Races != null) c.StartingRaces = (RaceId[])_o.Races.Clone();
            if (_o.PowersDemo && c.StartingMana < 400f) c.StartingMana = 400f;
            return c.Sanitize();
        }

        private static bool IsIo(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is System.Security.SecurityException || ex is ArgumentException;

        private static void WriteAtomic(string path, byte[] data)
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = full + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }
                File.Move(tmp, full, true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // ================================================================ table
        private char LetterOf(int civId)
        {
            if (_civLetters.TryGetValue(civId, out char c)) return c;
            int n = _civLetters.Count;
            c = n < 26 ? (char)('A' + n) : (n < 52 ? (char)('a' + n - 26) : '@');
            _civLetters[civId] = c;
            return c;
        }

        private List<Civilization> SortedCivs(WorldState s)
        {
            var list = new List<Civilization>();
            var items = s.Civilizations.Items;
            for (int i = 0; i < items.Count; i++) list.Add(items[i]);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var c in list) LetterOf(c.Id);
            return list;
        }

        private void PrintHeader(GodSimulation sim)
        {
            var s = sim.State;
            var cfg = sim.Config;
            Console.WriteLine("GodSim headless | seed " + s.Seed + (_o.SeedText != null && _o.SeedText != s.Seed.ToString() ? " (\"" + _o.SeedText + "\")" : "") +
                              " | " + s.Map.Width + "x" + s.Map.Height + " " + (cfg != null ? cfg.Preset.ToString() : "?") +
                              " | " + (_o.LoadPath != null ? "loaded " + _o.LoadPath + " at " + SimTime.Format(s.Tick) : "new world") +
                              " | " + _o.Years + " years" + (_o.Strict ? " | strict" : ""));
            var civs = SortedCivs(s);
            var tally = WorldQueries.TallyCivs(s);
            foreach (var c in civs)
            {
                tally.TryGetValue(c.Id, out var t);
                Console.WriteLine("  " + LetterOf(c.Id) + " = " + c.Name + " (" + c.Race + ", id " + c.Id + ", pop " + t.Population + (c.IsAlive ? "" : ", fallen") + ")");
            }
        }

        private void PrintTableHeader(GodSimulation sim)
        {
            Console.WriteLine();
            Console.WriteLine(" Year    Pop  Bldgs Wars    Mana  ms/tick | per civ: letter pop age settlements");
            Console.WriteLine("----- ------ ------ ---- ------- -------- +------------------------------------");
        }

        private void PrintRow(GodSimulation sim, double msPerTick)
        {
            var s = sim.State;
            var tally = WorldQueries.TallyCivs(s);
            var sb = new System.Text.StringBuilder(160);
            sb.Append(s.Year.ToString().PadLeft(5)).Append(' ')
              .Append(s.LivingPopulation.ToString().PadLeft(6)).Append(' ')
              .Append(WorldQueries.CompleteBuildings(s).ToString().PadLeft(6)).Append(' ')
              .Append(WorldQueries.ActiveWars(s).ToString().PadLeft(4)).Append(' ')
              .Append(s.God.Mana.ToString("0.0").PadLeft(7)).Append(' ')
              .Append(msPerTick.ToString("0.000").PadLeft(8)).Append(" |");
            foreach (var c in SortedCivs(s))
            {
                tally.TryGetValue(c.Id, out var t);
                if (!c.IsAlive && t.Population == 0) { sb.Append(' ').Append(LetterOf(c.Id)).Append(" --fallen--  "); continue; }
                sb.Append(' ').Append(LetterOf(c.Id)).Append(' ')
                  .Append(t.Population.ToString().PadLeft(5)).Append(' ')
                  .Append(WorldQueries.AgeAbbrev(c.Age)).Append(' ')
                  .Append(t.Settlements.ToString().PadLeft(2)).Append(" |");
            }
            Console.WriteLine(sb.ToString());
        }

        // ================================================================ end-of-run report
        private void PrintHighlights(GodSimulation sim)
        {
            const int MaxShown = 80;
            var entries = sim.State.Chronicle.Entries;
            var picked = new List<ChronicleEntry>();
            for (int i = 0; i < entries.Count; i++) if (entries[i].Importance >= 2) picked.Add(entries[i]);
            Console.WriteLine();
            Console.WriteLine("== Chronicle highlights (importance >= 2): " + picked.Count + " ==");
            int start = Math.Max(0, picked.Count - MaxShown);
            if (start > 0) Console.WriteLine("  ... " + start + " earlier entries omitted");
            for (int i = start; i < picked.Count; i++)
            {
                var e = picked[i];
                Console.WriteLine("  Y" + SimTime.Year(e.Tick).ToString().PadLeft(4) + " " + new string('*', MathUtil.Clamp(e.Importance, 0, 3)).PadRight(3) +
                                  " [" + e.Category + "] " + (e.Text ?? ""));
            }
        }

        private void PrintStats(GodSimulation sim, double seconds, long ticksRun, PowersDemo demo)
        {
            var s = sim.State;
            var st = s.Stats;
            Console.WriteLine();
            Console.WriteLine("== Summary at " + SimTime.Format(s.Tick) + " (tick " + s.Tick + ") ==");
            Console.WriteLine("  Population      " + s.LivingPopulation + " living, peak " + st.PeakPopulation + ", births " + st.TotalBirths + ", deaths " + st.TotalDeaths);
            var causes = (DeathCause[])Enum.GetValues(typeof(DeathCause));
            var parts = new List<string>();
            for (int i = 0; i < causes.Length && i < st.DeathsByCause.Length; i++) if (st.DeathsByCause[i] > 0) parts.Add(causes[i] + " " + st.DeathsByCause[i]);
            if (parts.Count > 0) Console.WriteLine("  Deaths by cause " + string.Join(", ", parts));
            Console.WriteLine("  Settlements     " + WorldQueries.LivingSettlements(s) + " living, " + st.SettlementsFounded + " founded in total");
            Console.WriteLine("  Buildings       " + WorldQueries.CompleteBuildings(s) + " complete / " + WorldQueries.StandingBuildings(s) + " standing; " +
                              st.BuildingsCompleted + " completed, " + st.BuildingsDestroyed + " destroyed in total");
            int houseCap = WorldQueries.HousingCapacity(s, BuildingType.House);
            int pop = s.LivingPopulation;
            Console.WriteLine("  Housing         " + houseCap + " beds in complete Houses (" + (pop > 0 ? (100.0 * houseCap / pop).ToString("0") + "% of population" : "no population") + ")");
            Console.WriteLine("  Warfare         " + WorldQueries.ActiveWars(s) + " active wars, " + st.WarsDeclared + " declared, " + st.BattlesFought + " battles, " + s.Diplomacy.Armies.Count + " armies");
            Console.WriteLine("  God             mana " + s.God.Mana.ToString("0.0") + "/" + s.God.MaxMana.ToString("0") + ", alignment " + s.God.Alignment.ToString("0.00") +
                              ", powers cast " + s.God.PowersCast);
            if (demo != null)
                Console.WriteLine("  Powers demo     " + demo.Attempted + " attempted, " + demo.Accepted + " accepted, " + demo.Executed + " executed, " + demo.ExecutionFailures + " failed at execution");
            Console.WriteLine("  World age       " + s.WorldAge);
            var tally = WorldQueries.TallyCivs(s);
            foreach (var c in SortedCivs(s))
            {
                tally.TryGetValue(c.Id, out var t);
                Console.WriteLine("    " + LetterOf(c.Id) + " " + (c.Name ?? "").PadRight(28) + " " + c.Race.ToString().PadRight(8) + " " + c.Age.ToString().PadRight(12) +
                                  (c.IsAlive ? "" : "(fallen) ") + "pop " + t.Population + ", settlements " + t.Settlements + ", buildings " + t.Buildings +
                                  ", techs " + c.KnownTechs.Count + ", research " + c.TotalResearch.ToString("0"));
            }
            double tps = seconds > 0 ? ticksRun / seconds : 0;
            Console.WriteLine("  Run time        " + seconds.ToString("0.00") + " s for " + ticksRun + " ticks (" + tps.ToString("0") + " ticks/s, " +
                              (ticksRun > 0 ? (seconds * 1000.0 / ticksRun).ToString("0.000") : "0") + " ms/tick)");
            if (sim.Events != null && sim.Events.HandlerFaults > 0) Console.WriteLine("  Event handler faults: " + sim.Events.HandlerFaults);
            if (_log.ErrorCount > 0) Console.WriteLine("  Logged errors: " + _log.ErrorCount + ", warnings: " + _log.WarningCount);
        }

        private void PrintSystems(GodSimulation sim)
        {
            var profiles = Profiles(sim);
            if (profiles.Count == 0) return;
            profiles.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            int n = _o.Benchmark ? profiles.Count : Math.Min(8, profiles.Count);
            Console.WriteLine();
            Console.WriteLine("== Slowest systems (avg ms per run) ==");
            for (int i = 0; i < n; i++)
            {
                var p = profiles[i];
                Console.WriteLine("  " + (p.Name ?? "?").PadRight(32) + p.AvgMs.ToString("0.0000").PadLeft(10) + " ms  runs " + p.Runs);
            }
        }

        /// <returns>true if any system faulted or was disabled.</returns>
        private bool PrintFaults(GodSimulation sim)
        {
            var bad = new List<SystemProfile>();
            foreach (var p in Profiles(sim)) if (p.TotalFaults > 0 || p.Disabled) bad.Add(p);
            if (bad.Count == 0) { Console.WriteLine("  No system faults."); return false; }
            Console.WriteLine();
            Console.WriteLine("== FAULTED SYSTEMS: " + bad.Count + " ==");
            foreach (var p in bad)
                Console.WriteLine("  " + (p.Name ?? "?").PadRight(32) + " faults " + p.TotalFaults + (p.Disabled ? " DISABLED" : "") + " last: " + (p.LastError ?? "-"));
            return true;
        }

        private void PrintBenchmark(GodSimulation sim, double seconds)
        {
            Console.WriteLine();
            Console.WriteLine("== Benchmark ==");
            if (_tickMs == null || _tickMs.Count == 0) { Console.WriteLine("  no ticks measured"); return; }
            var sorted = new List<double>(_tickMs);
            sorted.Sort();
            double sum = 0; foreach (var v in sorted) sum += v;
            Console.WriteLine("  ticks " + sorted.Count + ", wall " + seconds.ToString("0.00") + " s, " + (sorted.Count / Math.Max(1e-9, seconds)).ToString("0") + " ticks/s, " +
                              (sorted.Count / (double)SimTime.TicksPerYear / Math.Max(1e-9, seconds)).ToString("0.0") + " years/s");
            Console.WriteLine("  ms/tick avg " + (sum / sorted.Count).ToString("0.000") + ", p50 " + Pct(sorted, 0.50).ToString("0.000") + ", p95 " + Pct(sorted, 0.95).ToString("0.000") +
                              ", p99 " + Pct(sorted, 0.99).ToString("0.000") + ", max " + sorted[sorted.Count - 1].ToString("0.000"));
            int last = Math.Min(sorted.Count, SimTime.TicksPerYear * 10);
            double lastSum = 0; for (int i = _tickMs.Count - last; i < _tickMs.Count; i++) lastSum += _tickMs[i];
            Console.WriteLine("  last " + last + " ticks avg " + (lastSum / last).ToString("0.000") + " ms at population " + sim.State.LivingPopulation +
                              " (budget: < 3 ms/tick at 5,000 people)");
            Console.WriteLine("  GC collections gen0/1/2: " + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2) +
                              ", managed heap " + (GC.GetTotalMemory(false) / (1024 * 1024)) + " MB, working set " + (Environment.WorkingSet / (1024 * 1024)) + " MB");
        }

        private static double Pct(List<double> sorted, double q)
        {
            int i = (int)Math.Ceiling(q * sorted.Count) - 1;
            return sorted[MathUtil.Clamp(i, 0, sorted.Count - 1)];
        }

        private static List<SystemProfile> Profiles(GodSimulation sim)
        {
            var list = new List<SystemProfile>();
            var profiles = sim.Engine != null ? sim.Engine.Profiles : null;
            if (profiles != null) for (int i = 0; i < profiles.Count; i++) if (profiles[i] != null) list.Add(profiles[i]);
            return list;
        }
    }
}
