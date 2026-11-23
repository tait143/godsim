using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GodSim.Core;

namespace GodSim.Headless
{
    /// <summary>Thrown for any invalid command line. Mapped to exit code 1 with usage.</summary>
    public sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    /// <summary>Validated command-line options of the headless runner.</summary>
    public sealed class HeadlessOptions
    {
        public const int MinYears = 1, MaxYears = 3000;
        public const int MinSize = 64, MaxSize = 1024;
        public const int MaxHistoryInterval = 3000;
        public const int MaxRaces = 16;

        public ulong Seed = new SimConfig().Seed;
        public string SeedText;          // original --seed argument (for display)
        public bool SeedGiven;
        public int Years = 50;
        public int Width = 192;
        public int Height = 192;
        public bool SizeGiven;
        public WorldPreset Preset = WorldPreset.Continents;
        public bool PresetGiven;
        public RaceId[] Races;           // null = config default (all races)
        public string OutPath;
        public int HistoryInterval;      // 0 = automatic
        public bool Ascii;
        public bool Quiet;
        public bool Strict;
        public bool Benchmark;
        public string SavePath;
        public string LoadPath;
        public bool PrintHash;
        public bool PowersDemo;
        public bool ShowHelp;

        /// <summary>Effective table/history interval in years.</summary>
        public int EffectiveInterval => HistoryInterval > 0 ? HistoryInterval : (Years <= 100 ? 1 : (Years + 99) / 100);

        public static string Usage
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("GodSim.Headless - run the god simulation without Unity.");
                sb.AppendLine();
                sb.AppendLine("Usage: GodSim.Headless [options]");
                sb.AppendLine();
                sb.AppendLine("World:");
                sb.AppendLine("  --seed <ulong|text>        world seed; non-numeric text is hashed (FNV-1a 64). Default 20261119");
                sb.AppendLine("  --width <64..1024>         map width in tiles (default 192)");
                sb.AppendLine("  --height <64..1024>        map height in tiles (default 192)");
                sb.AppendLine("  --preset <name>            Continents | Pangaea | Archipelago | Highlands (default Continents)");
                sb.AppendLine("  --races <list>             comma list of Human,Elf,Dwarf,Orc,Saurian (default all; max 16)");
                sb.AppendLine("Run:");
                sb.AppendLine("  --years <1..3000>          years to simulate (default 50; 1 year = " + SimTime.TicksPerYear + " ticks)");
                sb.AppendLine("  --history-interval <years> table/history sample interval, 1..3000 (default: 1, or years/100 for long runs)");
                sb.AppendLine("  --strict                   Config.StrictMode: system exceptions abort the run (exit 2)");
                sb.AppendLine("  --powers-demo              cast a scripted series of god powers during the run");
                sb.AppendLine("  --benchmark                print detailed per-tick and per-system timings");
                sb.AppendLine("Files:");
                sb.AppendLine("  --load <file>              resume from a save (cannot be combined with --seed/--width/--height/--preset/--races)");
                sb.AppendLine("  --save <file>              write a save at the end of the run");
                sb.AppendLine("  --out <snapshot.json>      write a godsim.snapshot/1 JSON snapshot for the web viewer");
                sb.AppendLine("Output:");
                sb.AppendLine("  --ascii                    print an ASCII map at the end");
                sb.AppendLine("  --hash                     print the final state hash");
                sb.AppendLine("  --quiet                    suppress the periodic table and chronicle highlights");
                sb.AppendLine("  -h, --help                 show this help");
                sb.AppendLine();
                sb.AppendLine("Exit codes: 0 ok, 1 bad arguments, 2 simulation fault, 3 IO error.");
                return sb.ToString();
            }
        }

        /// <summary>Strictly parses arguments. Throws <see cref="UsageException"/> on any problem.</summary>
        public static HeadlessOptions Parse(string[] args)
        {
            var o = new HeadlessOptions();
            if (args == null) return o;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i] ?? "";
                string name = raw, inlineValue = null;
                if (raw.StartsWith("--", StringComparison.Ordinal))
                {
                    int eq = raw.IndexOf('=');
                    if (eq > 2) { name = raw.Substring(0, eq); inlineValue = raw.Substring(eq + 1); }
                }
                else if (raw != "-h")
                {
                    throw new UsageException("unexpected argument '" + Printable(raw) + "' (options start with --)");
                }

                if (name == "-h") name = "--help";
                if (!seen.Add(name)) throw new UsageException("option " + name + " given more than once");

                switch (name)
                {
                    case "--help": NoValue(name, inlineValue); o.ShowHelp = true; break;
                    case "--ascii": NoValue(name, inlineValue); o.Ascii = true; break;
                    case "--quiet": NoValue(name, inlineValue); o.Quiet = true; break;
                    case "--strict": NoValue(name, inlineValue); o.Strict = true; break;
                    case "--benchmark": NoValue(name, inlineValue); o.Benchmark = true; break;
                    case "--hash": NoValue(name, inlineValue); o.PrintHash = true; break;
                    case "--powers-demo": NoValue(name, inlineValue); o.PowersDemo = true; break;

                    case "--seed":
                        o.SeedText = Value(args, ref i, name, inlineValue);
                        o.Seed = ParseSeed(o.SeedText);
                        o.SeedGiven = true;
                        break;
                    case "--years": o.Years = ParseInt(Value(args, ref i, name, inlineValue), name, MinYears, MaxYears); break;
                    case "--width": o.Width = ParseInt(Value(args, ref i, name, inlineValue), name, MinSize, MaxSize); o.SizeGiven = true; break;
                    case "--height": o.Height = ParseInt(Value(args, ref i, name, inlineValue), name, MinSize, MaxSize); o.SizeGiven = true; break;
                    case "--history-interval": o.HistoryInterval = ParseInt(Value(args, ref i, name, inlineValue), name, 1, MaxHistoryInterval); break;
                    case "--preset": o.Preset = ParseEnumName<WorldPreset>(Value(args, ref i, name, inlineValue), name); o.PresetGiven = true; break;
                    case "--races": o.Races = ParseRaces(Value(args, ref i, name, inlineValue)); break;
                    case "--out": o.OutPath = ParsePath(Value(args, ref i, name, inlineValue), name); break;
                    case "--save": o.SavePath = ParsePath(Value(args, ref i, name, inlineValue), name); break;
                    case "--load": o.LoadPath = ParsePath(Value(args, ref i, name, inlineValue), name); break;
                    default:
                        throw new UsageException("unknown option '" + Printable(name) + "'");
                }
            }

            if (o.LoadPath != null && (o.SeedGiven || o.SizeGiven || o.PresetGiven || o.Races != null))
                throw new UsageException("--load restores the world from the save; --seed/--width/--height/--preset/--races cannot be combined with it");
            if (o.OutPath != null && o.SavePath != null && SamePath(o.OutPath, o.SavePath))
                throw new UsageException("--out and --save must be different files");
            if (o.OutPath != null && o.LoadPath != null && SamePath(o.OutPath, o.LoadPath))
                throw new UsageException("--out would overwrite the --load save file");
            return o;
        }

        private static void NoValue(string name, string inlineValue)
        {
            if (inlineValue != null) throw new UsageException(name + " does not take a value");
        }

        private static string Value(string[] args, ref int i, string name, string inlineValue)
        {
            if (inlineValue != null)
            {
                if (inlineValue.Length == 0) throw new UsageException(name + " requires a value");
                return inlineValue;
            }
            if (i + 1 >= args.Length) throw new UsageException(name + " requires a value");
            string v = args[++i] ?? "";
            if (v.Length == 0) throw new UsageException(name + " requires a non-empty value");
            if (v.StartsWith("--", StringComparison.Ordinal)) throw new UsageException(name + " requires a value (got option '" + Printable(v) + "')");
            return v;
        }

        public static int ParseInt(string s, string name, int min, int max)
        {
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v))
                throw new UsageException(name + " expects an integer, got '" + Printable(s) + "'");
            if (v < min || v > max) throw new UsageException(name + " must be in " + min + ".." + max + " (got " + v + ")");
            return v;
        }

        /// <summary>Decimal or 0x-hex ulong; any other text (1..256 printable chars) is hashed with FNV-1a 64.</summary>
        public static ulong ParseSeed(string s)
        {
            if (string.IsNullOrEmpty(s)) throw new UsageException("--seed requires a value");
            if (s.Length > 256) throw new UsageException("--seed text is too long (max 256 characters)");
            for (int i = 0; i < s.Length; i++)
                if (char.IsControl(s[i])) throw new UsageException("--seed contains control characters");
            if (ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong dec)) return dec;
            if (s.Length > 2 && (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                && ulong.TryParse(s.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong hex)) return hex;
            bool allDigits = true;
            for (int i = 0; i < s.Length; i++) if (!(s[i] >= '0' && s[i] <= '9')) { allDigits = false; break; }
            if (allDigits) throw new UsageException("--seed number does not fit in 64 bits: " + s);
            if (s[0] == '-' && s.Length > 1 && char.IsDigit(s[1])) throw new UsageException("--seed must be a non-negative number or text");
            return Hashing.Fnv1a64(s);
        }

        /// <summary>Case-insensitive match against declared enum NAMES only (numbers are rejected).</summary>
        public static T ParseEnumName<T>(string s, string name) where T : struct, Enum
        {
            string[] names = Enum.GetNames(typeof(T));
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], s.Trim(), StringComparison.OrdinalIgnoreCase))
                    return (T)Enum.Parse(typeof(T), names[i]);
            throw new UsageException(name + " must be one of " + string.Join(", ", names) + " (got '" + Printable(s) + "')");
        }

        public static RaceId[] ParseRaces(string s)
        {
            var parts = s.Split(',');
            if (parts.Length > MaxRaces) throw new UsageException("--races accepts at most " + MaxRaces + " entries");
            var list = new List<RaceId>();
            foreach (var p in parts)
            {
                if (p.Trim().Length == 0) throw new UsageException("--races contains an empty entry");
                list.Add(ParseEnumName<RaceId>(p, "--races"));
            }
            return list.ToArray();
        }

        private static string ParsePath(string s, string name)
        {
            if (s.IndexOf('\0') >= 0) throw new UsageException(name + " path contains a NUL character");
            if (s.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new UsageException(name + " path contains invalid characters");
            try
            {
                string full = Path.GetFullPath(s);
                if (Directory.Exists(full)) throw new UsageException(name + " must be a file, not a directory: " + s);
            }
            catch (UsageException) { throw; }
            catch (Exception ex) { throw new UsageException(name + " has an invalid path '" + Printable(s) + "': " + ex.Message); }
            return s;
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal); }
            catch (Exception) { return false; }
        }

        private static string Printable(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s) { if (sb.Length >= 64) { sb.Append("..."); break; } sb.Append(char.IsControl(c) ? '?' : c); }
            return sb.ToString();
        }
    }
}
