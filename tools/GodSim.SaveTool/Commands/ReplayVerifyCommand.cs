using System;
using System.Collections.Generic;
using System.IO;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>
    /// `replay-verify &lt;save&gt; &lt;replay&gt; --ticks N`: loads the save, re-casts every replay command whose
    /// recorded tick falls in [saveTick, saveTick + N) through GodSimulation.TryCastPower (validated like
    /// player input) just before that tick is simulated, runs N ticks and prints the resulting state hash.
    /// With --expect-hash the exit code is 0 on match and 6 on mismatch (deterministic-replay QA). The world
    /// config comes from the replay header (ReplayHeader.ToConfig), and recorded checkpoints that fall inside
    /// the window are compared as they are reached (any divergence -> exit 6).
    /// </summary>
    public static class ReplayVerifyCommand
    {
        public const int MaxTicks = 10000000;

        public static int Run(IReadOnlyList<string> args)
        {
            var p = new ArgParser("replay-verify", "save", "replay")
                .Option("--ticks").Option("--key-file").Option("--key-hex").Option("--expect-hash").Parse(args, 1);
            int ticks = p.Int("--ticks", -1, 0, MaxTicks);
            if (ticks < 0) throw new ToolException(ExitCodes.Usage, "'replay-verify' requires --ticks N (0.." + MaxTicks + ")");
            ulong expected = 0;
            bool hasExpected = p.HasValue("--expect-hash");
            if (hasExpected && !ArgParser.TryParseULong(NormalizeHash(p.Value("--expect-hash")), out expected))
                throw new ToolException(ExitCodes.Usage, "--expect-hash must be a 64-bit hex hash (e.g. 0x0123456789ABCDEF)");
            bool verbose = p.Has("--verbose");

            ReplayLog replay = LoadReplay(p.Positional(1));
            IReadOnlyList<ReplayEntry> entries = replay.Entries;
            SimConfig config = replay.Header != null ? replay.Header.ToConfig() : new SimConfig();

            byte[] key = KeyMaterial.FromArgs(p);
            GodSimulation sim;
            LoadedSave save;
            try
            {
                save = SaveAccess.ReadHeaderOnly(p.Positional(0));
                bool tagOk = SaveAccess.PreCheck(save, key, false);
                try
                {
                    // GodSimulation.Load requires a signature whenever a key is passed: only pass it for signed files.
                    sim = GodSimulation.Load(save.Bytes, config, verbose ? (ISimLogger)new StderrLogger() : NullLogger.Instance,
                                             save.Header.IsSigned ? key : null);
                }
                catch (SaveCorruptException e) { throw SaveAccess.Classify(save, e, tagOk); }
            }
            finally { KeyMaterial.Wipe(key); }

            // Stable order by tick (record order breaks ties) without trusting the file to be sorted.
            var order = new List<int>(entries.Count);
            for (int i = 0; i < entries.Count; i++) order.Add(i);
            order.Sort((x, y) => entries[x].Tick != entries[y].Tick ? entries[x].Tick.CompareTo(entries[y].Tick) : x.CompareTo(y));

            long start = sim.Tick, end = start + ticks;
            ulong startHash = sim.ComputeHash();
            int idx = 0, before = 0, accepted = 0, rejected = 0;
            while (idx < order.Count && entries[order[idx]].Tick < start) { before++; idx++; }

            // Recorded state-hash checkpoints inside [start, end] are compared as the run reaches them.
            var checkpoints = new List<ReplayCheckpoint>();
            if (replay.Checkpoints != null)
                for (int i = 0; i < replay.Checkpoints.Count; i++)
                    if (replay.Checkpoints[i].Tick >= start && replay.Checkpoints[i].Tick <= end) checkpoints.Add(replay.Checkpoints[i]);
            checkpoints.Sort((x, y) => x.Tick.CompareTo(y.Tick));
            int cpIdx = 0, cpMatched = 0, cpFailed = 0;
            long firstDivergence = -1;

            while (true)
            {
                while (cpIdx < checkpoints.Count && checkpoints[cpIdx].Tick == sim.Tick)
                {
                    ReplayCheckpoint cp = checkpoints[cpIdx++];
                    ulong now = sim.ComputeHash();
                    if (now == cp.Hash) cpMatched++;
                    else
                    {
                        cpFailed++;
                        if (firstDivergence < 0) firstDivergence = cp.Tick;
                        if (verbose) TextOut.Err.WriteLine("checkpoint @" + cp.Tick + ": expected " + TextOut.Hash(cp.Hash) + ", got " + TextOut.Hash(now));
                    }
                }
                if (sim.Tick >= end) break;
                while (idx < order.Count && entries[order[idx]].Tick == sim.Tick)
                {
                    ReplayEntry e = entries[order[idx++]];
                    if (sim.TryCastPower(e.Power, e.Target, e.Radius, e.Parameter, out string error)) accepted++;
                    else
                    {
                        rejected++;
                        if (verbose) TextOut.Err.WriteLine("rejected @" + e.Tick + " " + e.Power + " " + e.Target + ": " + TextOut.Safe(error, 200));
                    }
                }
                long next = idx < order.Count ? Math.Min(entries[order[idx]].Tick, end) : end;
                if (cpIdx < checkpoints.Count) next = Math.Min(next, checkpoints[cpIdx].Tick);
                long before_ = sim.Tick;
                sim.StepMany((int)Math.Max(1, next - sim.Tick));
                if (sim.Tick <= before_) throw new ToolException(ExitCodes.InternalError, "simulation did not advance past tick " + before_);
            }
            int after = order.Count - idx;
            ulong hash = sim.ComputeHash();

            TextOut.Line("Save:      " + TextOut.Safe(save.Path, 300) + (save.Header.IsSigned ? " [signed, verified]" : " [unsigned]"));
            TextOut.Line("Replay:    " + TextOut.Safe(p.Positional(1), 300) + " (" + entries.Count + " entries)");
            TextOut.Line("Start:     tick " + start + " (" + SimTime.Format(start) + "), hash " + TextOut.Hash(startHash));
            TextOut.Line("Ran:       " + ticks + " ticks -> tick " + sim.Tick + " (" + SimTime.Format(sim.Tick) + ")");
            TextOut.Line("Commands:  " + accepted + " accepted, " + rejected + " rejected, " + before + " before the save tick (skipped), " + after + " after the window (not applied)");
            TextOut.Line("People:    " + sim.State.LivingPopulation + " alive");
            TextOut.Line("Checks:    " + checkpoints.Count + " recorded checkpoint(s) in window: " + cpMatched + " matched, " + cpFailed + " diverged" +
                         (firstDivergence >= 0 ? " (first at tick " + firstDivergence + ")" : ""));
            TextOut.Line("Hash:      " + TextOut.Hash(hash));
            if (cpFailed > 0)
            {
                TextOut.Line("RESULT: DESYNC (replay checkpoints diverged)");
                return ExitCodes.Mismatch;
            }
            if (!hasExpected) return ExitCodes.Ok;
            if (hash == expected) { TextOut.Line("RESULT: MATCH"); return ExitCodes.Ok; }
            TextOut.Line("RESULT: MISMATCH (expected " + TextOut.Hash(expected) + ")");
            return ExitCodes.Mismatch;
        }

        private static ReplayLog LoadReplay(string path)
        {
            byte[] bytes = SafeFile.ReadBounded(path, SafeFile.MaxReplayBytes, "Replay file");
            string name = TextOut.Safe(path, 200);
            try
            {
                ReplayLog log = ReplayLog.FromBytes(bytes);
                if (log == null || log.Entries == null) throw new ToolException(ExitCodes.Corrupt, "Replay " + name + " has no entries table");
                return log;
            }
            catch (SaveCorruptException e) { throw new ToolException(ExitCodes.Corrupt, "Corrupt replay " + name + ": " + TextOut.Safe(e.Message, 300), e); }
            catch (InvalidDataException e) { throw new ToolException(ExitCodes.Corrupt, "Corrupt replay " + name + ": " + TextOut.Safe(e.Message, 300), e); }
            catch (EndOfStreamException e) { throw new ToolException(ExitCodes.Corrupt, "Truncated replay " + name + ": " + TextOut.Safe(e.Message, 300), e); }
            catch (FormatException e) { throw new ToolException(ExitCodes.Corrupt, "Corrupt replay " + name + ": " + TextOut.Safe(e.Message, 300), e); }
            catch (ArgumentException e) { throw new ToolException(ExitCodes.Corrupt, "Corrupt replay " + name + ": " + TextOut.Safe(e.Message, 300), e); }
        }

        private static string NormalizeHash(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s : "0x" + s;
        }
    }

    /// <summary>Simulation log sink for --verbose: stderr, sanitized, warnings and above.</summary>
    public sealed class StderrLogger : ISimLogger
    {
        public LogLevel MinLevel => LogLevel.Warning;
        public void Log(LogLevel level, string message)
        {
            if (level < MinLevel) return;
            TextOut.Err.WriteLine("[sim " + level + "] " + TextOut.Safe(message, 300));
        }
    }
}
