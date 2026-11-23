using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GodSim.Core;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>
    /// `fuzz &lt;file&gt; --iterations N --seed S`: deterministic mutation fuzzing of the save loader. Every
    /// mutated input goes through SaveSerializer.ReadHeader and SaveSerializer.Deserialize; the only
    /// acceptable failure is SaveCorruptException. Anything else (IndexOutOfRange, OutOfMemory,
    /// InvalidCast, ...) is a loader bug: it is reported and the exit code is 7.
    /// <para>--deep first re-encodes the save uncompressed + unsigned and re-seals the SHA-256 checksum
    /// after each mutation, so mutations reach the payload parser instead of stopping at the checksum.</para>
    /// <para>The PRNG is the Core PCG32 <see cref="Rng"/>: the same file, seed and options always produce
    /// the same mutations, so a CI failure reproduces locally with the printed iteration number.</para>
    /// </summary>
    public static class FuzzCommand
    {
        public const int MaxIterations = 1000000;
        private const int MaxFailuresShown = 20;
        private const int MaxFailuresSaved = 50;

        private sealed class Failure
        {
            public int Iteration;
            public string Mutation;
            public string Exception;
        }

        public static int Run(IReadOnlyList<string> args)
        {
            var p = new ArgParser("fuzz", "file")
                .Option("--iterations").Option("--seed").Option("--key-file").Option("--key-hex")
                .Option("--max-mutations").Option("--save-failures").Flag("--deep").Parse(args, 1);
            if (!p.HasValue("--iterations")) throw new ToolException(ExitCodes.Usage, "'fuzz' requires --iterations N");
            if (!p.HasValue("--seed")) throw new ToolException(ExitCodes.Usage, "'fuzz' requires --seed S");
            int iterations = p.Int("--iterations", 0, 1, MaxIterations);
            ulong seed = p.ULong("--seed", 0);
            int maxMutations = p.Int("--max-mutations", 8, 1, 4096);
            bool deep = p.Has("--deep");
            bool verbose = p.Has("--verbose");
            string failDir = p.Value("--save-failures");
            if (failDir != null && !Directory.Exists(failDir))
                throw new ToolException(ExitCodes.IoError, "--save-failures directory does not exist: " + TextOut.Safe(failDir, 300));

            byte[] key = KeyMaterial.FromArgs(p);
            try
            {
                // The base input must itself be valid: fuzzing a broken file proves nothing.
                LoadedSave baseSave = SaveAccess.Load(p.Positional(0), key, false);
                byte[] seedBytes = baseSave.Bytes;
                byte[] fuzzKey = key;
                if (deep)
                {
                    seedBytes = SaveAccess.Reserialize(baseSave.State, baseSave.Header, null, false);
                    fuzzKey = null;
                    if (!SaveAccess.ResealChecksum((byte[])seedBytes.Clone()))
                        throw new ToolException(ExitCodes.InternalError, "--deep: cannot locate the checksum in the re-encoded save (unknown container layout)");
                }
                baseSave.State = null; // let the GC reclaim the base world during the run

                return Fuzz(seedBytes, fuzzKey, iterations, seed, maxMutations, deep, verbose, failDir, p.Positional(0));
            }
            finally
            {
                KeyMaterial.Wipe(key);
            }
        }

        private static int Fuzz(byte[] original, byte[] key, int iterations, ulong seed, int maxMutations, bool deep, bool verbose, string failDir, string path)
        {
            GameDatabase db = SaveAccess.Database;
            var rng = new Rng(seed, 0x5AFE5AFE5AFE5AFFUL);
            var failures = new List<Failure>();
            var rejectReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int rejected = 0, accepted = 0, headerRejected = 0, unchanged = 0, saved = 0;
            long slowestMs = 0;
            int slowestIter = -1;
            var total = Stopwatch.StartNew();
            var options = new SaveOptions { HmacKey = key, MaxCompressedBytes = (int)SafeFile.MaxSaveBytes };

            for (int it = 0; it < iterations; it++)
            {
                string desc;
                // Deep mode: 75% in-place mutations, so most inputs keep a consistent length and reach the payload parser.
                bool inPlace = deep && rng.Chance(0.75f);
                byte[] data = Mutate(original, rng, maxMutations, inPlace, out desc);
                if (deep) SaveAccess.ResealChecksum(data);
                if (BytesEqual(data, original)) unchanged++;

                var sw = Stopwatch.StartNew();
                string unexpected = null;
                try
                {
                    try { SaveSerializer.ReadHeader(data); }
                    catch (SaveCorruptException) { headerRejected++; }
                    WorldState s = SaveSerializer.Deserialize(data, db, options);
                    if (s == null) unexpected = "Deserialize returned null instead of throwing SaveCorruptException";
                    else accepted++;
                }
                catch (SaveCorruptException e)
                {
                    rejected++;
                    string reason = Normalize(e.Message);
                    rejectReasons.TryGetValue(reason, out int n);
                    rejectReasons[reason] = n + 1;
                }
                catch (Exception e)
                {
                    unexpected = e.GetType().FullName + ": " + TextOut.Safe(e.Message, 200) + (verbose ? Environment.NewLine + e.StackTrace : "");
                }
                sw.Stop();
                if (sw.ElapsedMilliseconds > slowestMs) { slowestMs = sw.ElapsedMilliseconds; slowestIter = it; }

                if (unexpected != null)
                {
                    failures.Add(new Failure { Iteration = it, Mutation = desc, Exception = unexpected });
                    if (failDir != null && saved < MaxFailuresSaved)
                    {
                        string name = Path.Combine(failDir, "fuzz-" + seed.ToString("X16") + "-" + it.ToString("D7") + ".gsav");
                        SafeFile.WriteAtomic(name, data, true, "Failure file");
                        saved++;
                    }
                }
            }
            total.Stop();

            TextOut.Line("Fuzz summary: " + TextOut.Safe(path, 300));
            TextOut.Field("Mode", deep ? "deep (uncompressed, unsigned, checksum re-sealed)" : "container (as stored" + (key != null ? ", keyed" : "") + ")");
            TextOut.Field("Seed", "0x" + seed.ToString("X16"));
            TextOut.Field("Iterations", iterations + " (max " + maxMutations + " mutations each)");
            TextOut.Field("Input size", original.Length + " bytes");
            TextOut.Field("Rejected", rejected + " (SaveCorruptException; header-level " + headerRejected + ")");
            TextOut.Field("Accepted", accepted + " (" + unchanged + " mutations were no-ops)");
            TextOut.Field("Unexpected", failures.Count.ToString());
            TextOut.Field("Elapsed", total.ElapsedMilliseconds + " ms (slowest iteration #" + slowestIter + ": " + slowestMs + " ms)");
            if (rejectReasons.Count > 0)
            {
                var reasons = new List<KeyValuePair<string, int>>(rejectReasons);
                reasons.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
                TextOut.Line("  Top rejection reasons:");
                for (int i = 0; i < reasons.Count && i < 10; i++) TextOut.Line("    " + reasons[i].Value.ToString().PadLeft(7) + "  " + reasons[i].Key);
            }
            if (failures.Count == 0) { TextOut.Line("RESULT: PASS (loader only threw SaveCorruptException)"); return ExitCodes.Ok; }

            TextOut.Line("RESULT: FAIL (" + failures.Count + " unexpected exception" + (failures.Count == 1 ? "" : "s") + ")");
            for (int i = 0; i < failures.Count && i < MaxFailuresShown; i++)
                TextOut.Line("  #" + failures[i].Iteration + " [" + failures[i].Mutation + "] " + failures[i].Exception);
            if (failures.Count > MaxFailuresShown) TextOut.Line("  ... " + (failures.Count - MaxFailuresShown) + " more");
            if (saved > 0) TextOut.Line("  " + saved + " failing input(s) written to " + TextOut.Safe(failDir, 300));
            return ExitCodes.FuzzFailure;
        }

        private static readonly int[] InPlaceKinds = { 0, 1, 5, 7, 8 };

        /// <summary>
        /// Applies 1..max mutations. 25% of byte-level edits target the first 128 bytes (header).
        /// <paramref name="inPlaceOnly"/> restricts to length-preserving edits (flip, set, u32, reverse, fill).
        /// </summary>
        public static byte[] Mutate(byte[] src, Rng rng, int maxMutations, bool inPlaceOnly, out string description)
        {
            var data = new List<byte>(src);
            int count = rng.RangeInclusive(1, maxMutations);
            var desc = new System.Text.StringBuilder();
            for (int m = 0; m < count; m++)
            {
                if (desc.Length > 0) desc.Append(',');
                if (data.Count == 0) { data.Add((byte)rng.Range(0, 256)); desc.Append("seed-byte"); continue; }
                int pos = Pos(data.Count, rng);
                int kind = inPlaceOnly ? InPlaceKinds[rng.Range(0, InPlaceKinds.Length)] : rng.Range(0, 9);
                switch (kind)
                {
                    case 0:
                    {
                        int bit = rng.Range(0, 8);
                        data[pos] = (byte)(data[pos] ^ (1 << bit));
                        desc.Append("flip@").Append(pos).Append('.').Append(bit);
                        break;
                    }
                    case 1:
                        data[pos] = (byte)rng.Range(0, 256);
                        desc.Append("set@").Append(pos);
                        break;
                    case 2:
                    {
                        int len = rng.Range(0, data.Count);
                        data.RemoveRange(len, data.Count - len);
                        desc.Append("truncate@").Append(len);
                        break;
                    }
                    case 3:
                    {
                        int n = rng.RangeInclusive(1, 64);
                        var ins = new byte[n];
                        for (int i = 0; i < n; i++) ins[i] = (byte)rng.Range(0, 256);
                        data.InsertRange(pos, ins);
                        desc.Append("insert@").Append(pos).Append('+').Append(n);
                        break;
                    }
                    case 4:
                    {
                        int n = Math.Min(data.Count - pos, rng.RangeInclusive(1, 256));
                        data.RemoveRange(pos, n);
                        desc.Append("delete@").Append(pos).Append('-').Append(n);
                        break;
                    }
                    case 5:
                    {
                        // Interesting 32-bit values (length/count fields).
                        uint[] magic = { 0u, 1u, 0x7Fu, 0x80u, 0xFFu, 0x7FFFu, 0xFFFFu, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFFFFu, 0xFFFFFFFEu };
                        uint v = magic[rng.Range(0, magic.Length)];
                        for (int i = 0; i < 4 && pos + i < data.Count; i++) data[pos + i] = (byte)(v >> (8 * i));
                        desc.Append("u32@").Append(pos).Append('=').Append(v.ToString("X"));
                        break;
                    }
                    case 6:
                    {
                        int n = Math.Min(data.Count - pos, rng.RangeInclusive(1, 128));
                        int dst = rng.Range(0, data.Count);
                        var chunk = data.GetRange(pos, n);
                        data.InsertRange(dst, chunk);
                        desc.Append("dup@").Append(pos).Append('-').Append(n).Append('>').Append(dst);
                        break;
                    }
                    case 7:
                    {
                        int n = Math.Min(data.Count - pos, rng.RangeInclusive(2, 32));
                        data.Reverse(pos, n);
                        desc.Append("reverse@").Append(pos).Append('-').Append(n);
                        break;
                    }
                    default:
                    {
                        int n = Math.Min(data.Count - pos, rng.RangeInclusive(1, 16));
                        byte fill = rng.Chance(0.5f) ? (byte)0x00 : (byte)0xFF;
                        for (int i = 0; i < n; i++) data[pos + i] = fill;
                        desc.Append("fill@").Append(pos).Append('-').Append(n).Append('=').Append(fill.ToString("X2"));
                        break;
                    }
                }
            }
            description = desc.ToString();
            return data.ToArray();
        }

        private static int Pos(int length, Rng rng)
        {
            if (rng.Chance(0.25f)) return rng.Range(0, Math.Min(length, 128));
            return rng.Range(0, length);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            return new ReadOnlySpan<byte>(a).SequenceEqual(new ReadOnlySpan<byte>(b));
        }

        /// <summary>Groups rejection messages by replacing digit runs with '#'.</summary>
        private static string Normalize(string message)
        {
            string s = TextOut.Safe(message, 120);
            var sb = new System.Text.StringBuilder(s.Length);
            bool inDigits = false;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) { if (!inDigits) sb.Append('#'); inDigits = true; }
                else { sb.Append(c); inDigits = false; }
            }
            return sb.ToString();
        }
    }
}
