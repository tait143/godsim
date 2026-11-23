using System;
using System.IO;
using System.Text;

namespace GodSim.ContentReport
{
    /// <summary>
    /// Entry point. Exit codes: 0 = ok, 1 = content errors (with --check) or stale doc (with --verify),
    /// 2 = bad arguments or I/O failure.
    /// </summary>
    internal static class Program
    {
        private const string Usage =
@"GodSim.ContentReport - generates docs/CONTENT_REFERENCE.md from the content catalogs.

Usage: dotnet run --project tools/GodSim.ContentReport -c Release -- [options]

  --out <file>        Markdown output (default: <root>/docs/CONTENT_REFERENCE.md).
  --json <file>       Also dump the whole database (+ derived availability and findings) as JSON.
  --check             CI gate: print findings, exit 1 if there are content errors.
                      Markdown is only written with --check when --out is given explicitly.
  --fail-on-warnings  With --check: warnings also fail.
  --verify            Exit 1 if the markdown on disk differs from what would be generated (stale doc).
  --no-md             Do not write markdown.
  --root <dir>        Repository root (default: found by walking up to docs/ENGINEERING_CONTRACT.md).
  --quiet             Only print errors.
  --help              This text.";

        private static int Main(string[] args)
        {
            string outPath = null, jsonPath = null, root = null;
            bool check = false, failOnWarnings = false, verify = false, noMd = false, quiet = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--out": if (!Next(args, ref i, out outPath)) return BadArgs(a); break;
                    case "--json": if (!Next(args, ref i, out jsonPath)) return BadArgs(a); break;
                    case "--root": if (!Next(args, ref i, out root)) return BadArgs(a); break;
                    case "--check": check = true; break;
                    case "--fail-on-warnings": failOnWarnings = true; break;
                    case "--verify": verify = true; break;
                    case "--no-md": noMd = true; break;
                    case "--quiet": quiet = true; break;
                    case "-h": case "--help": case "/?": Console.WriteLine(Usage); return 0;
                    default: Console.Error.WriteLine("Unknown argument: " + a); Console.Error.WriteLine(Usage); return 2;
                }
            }

            root = root != null ? Path.GetFullPath(root) : FindRoot();
            bool writeMd = !noMd && !verify && (outPath != null || !check);
            if (outPath == null) outPath = Path.Combine(root ?? Directory.GetCurrentDirectory(), "docs", "CONTENT_REFERENCE.md");

            var load = ContentLoader.Load();
            var model = new ContentModel(load);
            string fingerprint = "fnv1a64:" + Fnv1a64(JsonExport.Definitions(model)).ToString("x16");
            string md = new MarkdownReport(model, fingerprint).Generate();

            if (!quiet)
                Console.WriteLine("Content: " + model.Races.Count + " races, " + model.Ages.Count + " ages, " + model.Techs.Count + " techs, " +
                                  model.Buildings.Count + " buildings, " + model.Powers.Count + " god powers; " +
                                  model.ErrorCount + " errors, " + model.WarningCount + " warnings, " + model.Count(Severity.Info) + " notes (" + fingerprint + ").");
            try
            {
                if (writeMd)
                {
                    WriteText(outPath, md);
                    if (!quiet) Console.WriteLine("Wrote " + outPath);
                }
                if (jsonPath != null)
                {
                    string full = Path.GetFullPath(jsonPath);
                    string dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(full, JsonExport.Full(model));
                    if (!quiet) Console.WriteLine("Wrote " + full);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Write failed: " + ex.Message);
                return 2;
            }

            int exit = 0;
            if (check)
            {
                foreach (var f in model.Findings)
                {
                    if (f.Severity == Severity.Error) Console.Error.WriteLine(f.ToString());
                    else if (!quiet && f.Severity == Severity.Warning) Console.WriteLine(f.ToString());
                }
                if (model.ErrorCount > 0 || (failOnWarnings && model.WarningCount > 0))
                {
                    Console.Error.WriteLine("Content check FAILED: " + model.ErrorCount + " errors, " + model.WarningCount + " warnings.");
                    exit = 1;
                }
                else if (!quiet) Console.WriteLine("Content check passed (" + model.WarningCount + " warnings).");
            }
            if (verify)
            {
                string existing = File.Exists(outPath) ? File.ReadAllText(outPath).Replace("\r\n", "\n") : null;
                if (existing != md)
                {
                    Console.Error.WriteLine(outPath + " is out of date. Regenerate: " + MarkdownReport.RegenerateCommand);
                    exit = 1;
                }
                else if (!quiet) Console.WriteLine(outPath + " is up to date.");
            }
            return exit;
        }

        private static bool Next(string[] args, ref int i, out string value)
        {
            value = null;
            if (i + 1 >= args.Length) return false;
            value = args[++i];
            return true;
        }

        private static int BadArgs(string a) { Console.Error.WriteLine(a + " needs a value."); return 2; }

        private static void WriteText(string path, string text)
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = full + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, full, true);
        }

        /// <summary>Walks up from the working directory, then from the executable, to the folder holding docs/ENGINEERING_CONTRACT.md.</summary>
        private static string FindRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    if (File.Exists(Path.Combine(d.FullName, "docs", "ENGINEERING_CONTRACT.md")) || Directory.Exists(Path.Combine(d.FullName, "Assets", "GodSim")))
                        return d.FullName;
                    d = d.Parent;
                }
            }
            return null;
        }

        private static ulong Fnv1a64(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in data) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
    }
}
