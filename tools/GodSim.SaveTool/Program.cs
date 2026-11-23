using System;
using System.Collections.Generic;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>Entry point: dispatches subcommands and turns failures into exit codes + one-line messages.</summary>
    public static class Program
    {
        public const string Usage =
@"GodSim SaveTool - inspect, verify, compare, re-sign and fuzz GodSim save files (.gsav).

USAGE
  savetool <command> [arguments] [options]

COMMANDS
  info <file>                                 Header only (no payload parse): versions, flags, seed, tick/date, population, sizes.
  verify <file> [KEY] [--require-signature]   Integrity (checksum/HMAC) + full parse + StateValidator.
  stats <file> [KEY] [--top N]                Civilizations, settlements, buildings by category, wars, chronicle, state hash.
  diff <a> <b> [KEY]                          Compare two saves (seed, tick, per-civ deltas, entity counts, section hashes).
  replay-verify <save> <replay> --ticks N [KEY] [--expect-hash HEX]
                                              Load the save, re-cast the replay's commands at their ticks, run N ticks, print the hash.
  sign <in> <out> --key-file <path> [--in-key-file <path>|--in-key-hex <hex>] [--force]
                                              Re-serialize signed with HMAC-SHA256 (input verified with --in-key-*, else the same key).
  strip-signature <in> <out> [KEY] [--force]  Re-serialize unsigned (signed input must be verified with KEY first).
  fuzz <file> --iterations N --seed S [KEY] [--max-mutations K] [--deep] [--save-failures <dir>]
                                              Deterministic byte mutations; asserts the loader only throws SaveCorruptException.
  help                                        Show this text.

KEY (HMAC key; never printed)
  --key-file <path>    Raw key bytes, hex text (optionally 'hex:' prefixed), or 'base64:<text>'. 16..1024 bytes.
  --key-hex <hex>      Key as hex on the command line (visible in process listings: prefer --key-file).

GLOBAL OPTIONS
  --verbose            Show stack traces and extra detail on failure.
  --help, -h           Show this text.

EXIT CODES
  0  OK (verify: valid; diff: identical; replay-verify: hash matched or no --expect-hash; fuzz: no unexpected exception)
  1  Internal error (unexpected exception)
  2  Usage error (bad command line)
  3  I/O error (missing/unreadable/too-large file, output exists)
  4  Corrupt save or replay (truncated, tampered checksum, unsupported version, failed state validation)
  5  Signature failure (signed file without key, wrong key, tampered signed file, unsigned under --require-signature)
  6  Mismatch (diff: saves differ; replay-verify: hash differs from --expect-hash)
  7  Fuzz failure (the loader threw something other than SaveCorruptException)

LIMITS
  Saves <= 64 MiB, replays <= 32 MiB, key files <= 4 KiB. Paths are used exactly as given (no wildcards).
";

        public static int Main(string[] args)
        {
            bool verbose = Array.IndexOf(args, "--verbose") >= 0;
            try
            {
                return Run(args);
            }
            catch (ToolException e)
            {
                TextOut.Err.WriteLine("error: " + e.Message);
                if (verbose && e.InnerException != null) TextOut.Err.WriteLine(e.InnerException.ToString());
                return e.ExitCode;
            }
            catch (SaveCorruptException e)
            {
                TextOut.Err.WriteLine("error: corrupt save: " + TextOut.Safe(e.Message, 300));
                if (verbose) TextOut.Err.WriteLine(e.ToString());
                return ExitCodes.Corrupt;
            }
            catch (OutOfMemoryException)
            {
                TextOut.Err.WriteLine("error: out of memory");
                return ExitCodes.InternalError;
            }
            catch (Exception e)
            {
                TextOut.Err.WriteLine("error: internal error (" + e.GetType().Name + "): " + TextOut.Safe(e.Message, 300));
                if (verbose) TextOut.Err.WriteLine(e.ToString());
                else TextOut.Err.WriteLine("       re-run with --verbose for details");
                return ExitCodes.InternalError;
            }
            finally
            {
                TextOut.Out.Flush();
            }
        }

        public static int Run(IReadOnlyList<string> args)
        {
            if (args.Count == 0)
            {
                TextOut.Err.Write(Usage);
                return ExitCodes.Usage;
            }
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "--") break;
                if (args[i] == "--help" || args[i] == "-h") { TextOut.Out.Write(Usage); return ExitCodes.Ok; }
            }

            string command = args[0];
            switch (command)
            {
                case "help":
                    if (args.Count > 1) throw new ToolException(ExitCodes.Usage, "'help' takes no arguments");
                    TextOut.Out.Write(Usage);
                    return ExitCodes.Ok;
                case "info": return InfoCommand.Run(args);
                case "verify": return VerifyCommand.Run(args);
                case "stats": return StatsCommand.Run(args);
                case "diff": return DiffCommand.Run(args);
                case "replay-verify": return ReplayVerifyCommand.Run(args);
                case "sign": return SignCommand.RunSign(args);
                case "strip-signature": return SignCommand.RunStrip(args);
                case "fuzz": return FuzzCommand.Run(args);
                default:
                    throw new ToolException(ExitCodes.Usage, "unknown command '" + TextOut.Safe(command, 64) + "' (see 'savetool help')");
            }
        }
    }
}
