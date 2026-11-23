using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GodSim.Core;
using GodSim.Core.Modding;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Modding
{
    public class ModLoaderTests
    {
        private static int _counter;
        private string _base;   // temp sandbox
        private string _root;   // <sandbox>/mods

        [SetUp]
        public void SetUp()
        {
            _base = Path.Combine(Path.GetTempPath(), "godsim-modding-" + DateTime.UtcNow.Ticks + "-" + (++_counter));
            _root = Path.Combine(_base, "mods");
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_base)) Directory.Delete(_base, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static bool Has(string hay, string needle) => hay != null && hay.IndexOf(needle, StringComparison.Ordinal) >= 0;

        private static bool AnyError(ModLoadResult r, string needle)
        {
            foreach (var e in r.Errors) if (Has(e.ToString(), needle)) return true;
            return false;
        }

        private static bool AnyWarning(ModLoadResult r, string needle)
        {
            foreach (var e in r.Warnings) if (Has(e.ToString(), needle)) return true;
            return false;
        }

        private static string Dump(ModLoadResult r)
        {
            var sb = new StringBuilder();
            foreach (var e in r.Errors) sb.Append("E: ").Append(e).Append('\n');
            foreach (var w in r.Warnings) sb.Append("W: ").Append(w).Append('\n');
            return sb.ToString();
        }

        private string WritePack(string folder, string manifest, params string[] files)
        {
            string dir = Path.Combine(_root, folder);
            Directory.CreateDirectory(Path.Combine(dir, "data"));
            File.WriteAllText(Path.Combine(dir, "mod.json"), manifest);
            for (int i = 0; i + 1 < files.Length; i += 2) File.WriteAllText(Path.Combine(dir, "data", files[i]), files[i + 1]);
            return dir;
        }

        private static string M(string id, int loadOrder = 0, string deps = "[]") =>
            "{\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"version\":\"1.0.0\",\"loadOrder\":" + loadOrder + ",\"dependencies\":" + deps + "}";

        // ------------------------------------------------------------------ discovery
        [Test]
        public void LoadsPacksInDeterministicOrderAndAppliesThem()
        {
            WritePack("zeta", M("zeta", 0), "b.json", "{\"buildings\":{\"House\":{\"buildWork\":30}}}", "a.json", "{\"techs\":{\"stub.tech\":{\"cost\":70}}}");
            WritePack("alpha", M("alpha", 0), "x.json", "{\"buildings\":{\"Well\":{\"buildWork\":12}}}");
            WritePack("early", M("early", -10), "x.json", "{\"buildings\":{\"House\":{\"buildWork\":20}}}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, result.Errors.Count, Dump(result));
            Assert.AreEqual(3, result.Packs.Count);
            Assert.AreEqual("early", result.Packs[0].Id);
            Assert.AreEqual("alpha", result.Packs[1].Id);
            Assert.AreEqual("zeta", result.Packs[2].Id);
            Assert.IsTrue(Has(result.Packs[2].Documents[0].SourceName, "a.json")); // data files sorted by name
            Assert.AreEqual(1, result.Conflicts.Count);
            Assert.AreEqual("buildings.House.buildWork", result.Conflicts[0].Path);
            Assert.AreEqual("zeta", result.Conflicts[0].WinningModId);

            var db = TestDb.CreateStub(freeze: false);
            var reports = ModPatchApplier.ApplyAll(db, result.Packs, null);
            foreach (var r in reports) Assert.IsTrue(r.Applied, r.ToString());
            Assert.AreEqual(30f, db.Building(BuildingType.House).BuildWork);
            Assert.AreEqual(70f, db.TechByKey("stub.tech").Cost);
            db.Freeze();
        }

        [Test]
        public void DependenciesLoadFirstRegardlessOfLoadOrder()
        {
            WritePack("base_pack", M("base_pack", 100), "a.json", "{}");
            WritePack("addon", M("addon", -100, "[\"base_pack\"]"), "a.json", "{}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, result.Errors.Count, Dump(result));
            Assert.AreEqual("base_pack", result.Packs[0].Id);
            Assert.AreEqual("addon", result.Packs[1].Id);
        }

        [Test]
        public void MissingDependencyRejectsPackAndItsDependents()
        {
            WritePack("aaa", M("aaa", 0, "[\"ghost\"]"), "a.json", "{}");
            WritePack("bbb", M("bbb", 0, "[\"aaa\"]"), "a.json", "{}");
            WritePack("ccc", M("ccc", 0, "[{\"id\":\"ghost\",\"optional\":true}]"), "a.json", "{}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(1, result.Packs.Count);
            Assert.AreEqual("ccc", result.Packs[0].Id);
            Assert.IsTrue(AnyError(result, "Missing required dependency 'ghost'"));
            Assert.IsTrue(AnyError(result, "Missing required dependency 'aaa'"));
        }

        [Test]
        public void DependencyVersionMismatchRejected()
        {
            WritePack("lib", "{\"id\":\"lib\",\"name\":\"Lib\",\"version\":\"1.5.0\"}", "a.json", "{}");
            WritePack("app", M("app", 0, "[{\"id\":\"lib\",\"version\":\"^2.0.0\"}]"), "a.json", "{}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(1, result.Packs.Count);
            Assert.IsTrue(AnyError(result, "version 1.5.0 is installed"));
        }

        [Test]
        public void DependencyCycleDetectedInMemory()
        {
            var a = ModPack.FromJson(M("mod_a", 0, "[\"mod_b\"]"));
            var b = ModPack.FromJson(M("mod_b", 0, "[\"mod_c\"]"));
            var c = ModPack.FromJson(M("mod_c", 0, "[\"mod_a\"]"));
            var d = ModPack.FromJson(M("mod_d", 0, "[\"mod_a\"]"));
            var e = ModPack.FromJson(M("mod_e"));
            var result = new ModLoadResult();
            ModLoader.Resolve(new List<ModPack> { d, c, b, a, e }, null, result);
            Assert.AreEqual(1, result.Packs.Count);
            Assert.AreEqual("mod_e", result.Packs[0].Id);
            Assert.IsTrue(AnyError(result, "Dependency cycle detected: mod_a -> mod_b -> mod_c -> mod_a"), Dump(result));
            Assert.IsTrue(AnyError(result, "[mod_d] Depends on a mod that is part of a dependency cycle"), Dump(result));
            Assert.AreEqual(4, result.Rejected.Count);
        }

        [Test]
        public void DuplicateIdsRejected()
        {
            WritePack("one", M("same_id"), "a.json", "{}");
            WritePack("two", M("same_id"), "a.json", "{}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(1, result.Packs.Count);
            Assert.IsTrue(AnyError(result, "Duplicate mod id"));
        }

        [Test]
        public void GameVersionRangeChecked()
        {
            WritePack("future", "{\"id\":\"future\",\"name\":\"F\",\"version\":\"1.0.0\",\"gameVersionRange\":\">=9.0.0\"}", "a.json", "{}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, result.Packs.Count);
            Assert.IsTrue(AnyError(result, "game version"));
        }

        // ------------------------------------------------------------------ security
        [Test]
        public void PathTraversalViaFolderNameRejected()
        {
            Directory.CreateDirectory(Path.Combine(_base, "outside"));
            File.WriteAllText(Path.Combine(_base, "outside", "mod.json"), M("outside"));
            var loader = new ModLoader();
            foreach (var evil in new[] { "../outside", "..", ".", "a/../../outside", "..\\outside", "/etc", "C:\\Windows", "" })
            {
                var r = loader.LoadSelected(_root, new[] { evil });
                Assert.AreEqual(0, r.Packs.Count, evil);
                Assert.Throws<ModLoadException>(() => loader.LoadPack(_root, evil), evil);
            }
        }

        [Test]
        public void PathTraversalViaDataFilesRejected()
        {
            File.WriteAllText(Path.Combine(_base, "secret.json"), "{\"buildings\":{\"House\":{\"buildWork\":99}}}");
            WritePack("sneaky", "{\"id\":\"sneaky\",\"name\":\"S\",\"version\":\"1.0.0\",\"dataFiles\":[\"../../secret.json\"]}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, result.Packs.Count);
            Assert.IsTrue(AnyError(result, "plain file name"), Dump(result));
        }

        [Test]
        public void ContainmentCheckRejectsEscapingPaths()
        {
            string full = Path.GetFullPath(_root);
            Assert.Throws<ModLoadException>(() => ModLoader.Contained(full, Path.Combine(full, "..", "x"), "x"));
            Assert.Throws<ModLoadException>(() => ModLoader.Contained(full, full, "x"));
            Assert.Throws<ModLoadException>(() => ModLoader.Contained(full, full + "-evil", "x")); // prefix trick
            Assert.DoesNotThrow(() => ModLoader.Contained(full, Path.Combine(full, "ok"), "x"));
        }

        [Test]
        public void ExplicitDataFilesOrderAndMissingFile()
        {
            WritePack("ordered", "{\"id\":\"ordered\",\"name\":\"O\",\"version\":\"1.0.0\",\"dataFiles\":[\"z.json\",\"a.json\"]}",
                "a.json", "{\"buildings\":{\"House\":{\"buildWork\":11}}}", "z.json", "{\"buildings\":{\"House\":{\"buildWork\":22}}}");
            WritePack("broken", "{\"id\":\"broken\",\"name\":\"B\",\"version\":\"1.0.0\",\"dataFiles\":[\"nope.json\"]}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(1, result.Packs.Count);
            Assert.IsTrue(Has(result.Packs[0].Documents[0].SourceName, "z.json"));
            Assert.IsTrue(AnyError(result, "does not exist"));
        }

        [Test]
        public void OversizedAndEmptyFilesRejected()
        {
            WritePack("big", M("big"), "a.json", "{\"x\":\"" + new string('a', 3000) + "\"}");
            WritePack("empty", M("empty"), "a.json", "");
            var loader = new ModLoader(new ModLoaderOptions { MaxFileBytes = 1024 });
            var result = loader.LoadAll(_root);
            Assert.AreEqual(0, result.Packs.Count);
            Assert.IsTrue(AnyError(result, "larger than 1024"), Dump(result));
            Assert.IsTrue(AnyError(result, "empty"), Dump(result));
        }

        [Test]
        public void MalformedJsonReportedWithPosition()
        {
            WritePack("bad_json", M("bad_json"), "a.json", "{\n  \"buildings\": {,}\n}");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, result.Packs.Count);
            Assert.IsTrue(AnyError(result, "bad_json/data/a.json:2:17"), Dump(result));
        }

        [Test]
        public void CodeFilesAreIgnoredWithWarning()
        {
            string dir = WritePack("codey", M("codey"), "a.json", "{}");
            File.WriteAllText(Path.Combine(dir, "Evil.dll"), "MZ");
            File.WriteAllText(Path.Combine(dir, "data", "script.lua"), "os.exit()");
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(1, result.Packs.Count);
            Assert.AreEqual(1, result.Packs[0].Documents.Count);
            Assert.IsTrue(AnyWarning(result, "Evil.dll"));
            Assert.IsTrue(AnyWarning(result, "script.lua"));
        }

        [Test]
        public void MissingManifestAndMissingRootHandled()
        {
            Directory.CreateDirectory(Path.Combine(_root, "no_manifest"));
            var result = new ModLoader().LoadAll(_root);
            Assert.IsTrue(AnyError(result, "Missing mod.json"));
            var none = new ModLoader().LoadAll(Path.Combine(_base, "does-not-exist"));
            Assert.AreEqual(0, none.Packs.Count);
            Assert.AreEqual(0, none.Errors.Count);
            Assert.AreEqual(1, none.Warnings.Count);
            Assert.AreEqual(0, new ModLoader().LoadAll(null).Packs.Count);
        }

        [Test]
        public void SymlinkedPackFolderRejectedWhenPlatformSupportsLinks()
        {
            if (Path.DirectorySeparatorChar != '/') return; // POSIX only; Windows junctions are covered by the same attribute check
            string target = Path.Combine(_base, "real_pack");
            Directory.CreateDirectory(Path.Combine(target, "data"));
            File.WriteAllText(Path.Combine(target, "mod.json"), M("linked"));
            File.WriteAllText(Path.Combine(target, "data", "a.json"), "{}");
            if (!TryLink(target, Path.Combine(_root, "linked"))) return;
            var result = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, result.Packs.Count, Dump(result));
            Assert.IsTrue(AnyError(result, "symbolic link"), Dump(result));
        }

        [Test]
        public void SymlinkedDataFileAndFifoRejectedWithoutBlocking()
        {
            if (Path.DirectorySeparatorChar != '/') return;
            File.WriteAllText(Path.Combine(_base, "outside.json"), "{}");
            string dir = WritePack("links", M("links"));
            if (!TryRun("ln", "-s \"" + Path.Combine(_base, "outside.json") + "\" \"" + Path.Combine(dir, "data", "a.json") + "\"")) return;
            var r1 = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, r1.Packs.Count, Dump(r1));
            Assert.IsTrue(AnyError(r1, "Symbolic links"), Dump(r1));

            File.Delete(Path.Combine(dir, "data", "a.json"));
            if (!TryRun("mkfifo", "\"" + Path.Combine(dir, "data", "b.json") + "\"")) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r2 = new ModLoader().LoadAll(_root);
            Assert.AreEqual(0, r2.Packs.Count, Dump(r2));
            Assert.IsTrue(sw.ElapsedMilliseconds < 5000);
        }

        private static bool TryLink(string target, string link) => TryRun("ln", "-s \"" + target + "\" \"" + link + "\"") && Directory.Exists(link);

        private static bool TryRun(string exe, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p == null || !p.WaitForExit(5000)) return false;
                    return p.ExitCode == 0;
                }
            }
            catch (Exception) { return false; }
        }
    }
}
