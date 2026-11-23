using System;
using System.Collections.Generic;
using System.IO;

namespace GodSim.Core.Modding
{
    /// <summary>
    /// Discovers data packs on disk. All file-system access of the modding module lives in this class.
    /// Layout: &lt;root&gt;/&lt;folder&gt;/mod.json and &lt;root&gt;/&lt;folder&gt;/data/*.json.
    /// <list type="bullet">
    /// <item>Only direct child folders of the root are considered; folder and file names are validated
    /// (no separators, no "..", no hidden names) and every resolved path must stay inside the root.</item>
    /// <item>Symbolic links / junctions / reparse points are refused (fail-closed when attributes are unreadable);
    /// empty or special files (FIFOs, devices report length 0) are refused before being opened, so reads never block.</item>
    /// <item>Every read is size-capped (per file, per pack, total). Code files are never loaded (a warning is emitted).</item>
    /// <item>Deterministic order: dependencies first, then (loadOrder, id). Missing dependencies, version
    /// mismatches, duplicate ids and dependency cycles reject the affected packs (and their dependents).</item>
    /// </list>
    /// </summary>
    public sealed class ModLoader
    {
        private static readonly string[] CodeExtensions =
            { ".dll", ".exe", ".so", ".dylib", ".cs", ".js", ".lua", ".py", ".bat", ".cmd", ".sh", ".ps1", ".jar", ".class", ".wasm", ".bundle", ".assetbundle" };

        private readonly ModLoaderOptions _opt;

        public ModLoader(ModLoaderOptions options = null) { _opt = options ?? new ModLoaderOptions(); }

        // ----------------------------------------------------------------- discovery
        /// <summary>Loads every pack folder under <paramref name="rootDirectory"/>. Never throws for bad mod content.</summary>
        public ModLoadResult LoadAll(string rootDirectory)
        {
            var result = new ModLoadResult();
            if (!TryResolveRoot(rootDirectory, result, out string root)) return result;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch (Exception e) when (IsIoException(e)) { result.Error("", "", "Cannot list the mods directory (" + e.GetType().Name + ")."); return result; }
            if (dirs.Length > _opt.MaxDirectoryEntries) { result.Error("", "", "The mods directory has more than " + _opt.MaxDirectoryEntries + " folders; refusing to scan it."); return result; }
            var names = new List<string>();
            foreach (var d in dirs) names.Add(Path.GetFileName(d));
            names.Sort(StringComparer.Ordinal);
            return LoadFolders(root, names, result);
        }

        /// <summary>Loads only the named pack folders (e.g. the player's enabled-mods list). Names are validated, so "../x" is refused.</summary>
        public ModLoadResult LoadSelected(string rootDirectory, IEnumerable<string> folderNames)
        {
            var result = new ModLoadResult();
            if (!TryResolveRoot(rootDirectory, result, out string root)) return result;
            var names = new List<string>();
            if (folderNames != null) foreach (var n in folderNames) if (n != null && !names.Contains(n)) names.Add(n);
            names.Sort(StringComparer.Ordinal);
            return LoadFolders(root, names, result);
        }

        private ModLoadResult LoadFolders(string root, List<string> names, ModLoadResult result)
        {
            var candidates = new List<ModPack>();
            long total = 0;
            foreach (var name in names)
            {
                if (name.Length > 0 && name[0] == '.') continue; // hidden folders (".git", ".DS_Store" dirs) are ignored
                if (candidates.Count >= _opt.MaxPacks)
                {
                    result.Error("", SafeJson.Printable(name), "More than " + _opt.MaxPacks + " mod packs; the rest were not loaded.");
                    result.Rejected.Add(SafeJson.Printable(name));
                    continue;
                }
                try
                {
                    var pack = LoadPackFolder(root, name, result, out long bytes);
                    if (total + bytes > _opt.MaxTotalBytes) throw new ModLoadException("Total size of all mods exceeds " + _opt.MaxTotalBytes + " bytes.", name, 0, 0);
                    total += bytes;
                    candidates.Add(pack);
                }
                catch (ModLoadException e)
                {
                    result.Error(SafeJson.Printable(name), e);
                    result.Rejected.Add(SafeJson.Printable(name));
                }
                catch (Exception e) when (IsIoException(e))
                {
                    result.Error(SafeJson.Printable(name), SafeJson.Printable(name), "Cannot load pack (" + e.GetType().Name + ").");
                    result.Rejected.Add(SafeJson.Printable(name));
                }
            }
            Resolve(candidates, _opt, result);
            return result;
        }

        /// <summary>Loads and validates a single pack folder (throws <see cref="ModLoadException"/> on any problem).</summary>
        public ModPack LoadPack(string rootDirectory, string folderName)
        {
            var result = new ModLoadResult();
            if (!TryResolveRoot(rootDirectory, result, out string root))
                throw new ModLoadException(result.Errors.Count > 0 ? result.Errors[0].Message : "Mods directory not found.");
            return LoadPackFolder(root, folderName, result, out _);
        }

        private bool TryResolveRoot(string rootDirectory, ModLoadResult result, out string root)
        {
            root = null;
            if (string.IsNullOrEmpty(rootDirectory)) { result.Error("", "", "No mods directory given."); return false; }
            try { root = Path.GetFullPath(rootDirectory); }
            catch (Exception e) when (IsIoException(e)) { result.Error("", "", "Invalid mods directory path (" + e.GetType().Name + ")."); return false; }
            root = TrimSeparators(root);
            if (!Directory.Exists(root)) { result.Warn("", "", "Mods directory does not exist; no mods loaded."); return false; }
            if (IsLinkOrUnreadable(root)) { result.Error("", "", "The mods directory must not be a symbolic link or junction."); return false; }
            return true;
        }

        private ModPack LoadPackFolder(string root, string folderName, ModLoadResult result, out long bytes)
        {
            bytes = 0;
            if (!IsValidFolderName(folderName))
                throw new ModLoadException("Invalid pack folder name '" + SafeJson.Printable(folderName) + "' (plain name of [A-Za-z0-9_.-], no path separators or '..').", SafeJson.Printable(folderName), 0, 0);
            string packDir = Contained(root, Path.Combine(root, folderName), folderName);
            if (!Directory.Exists(packDir)) throw new ModLoadException("Pack folder not found.", folderName, 0, 0);
            if (IsLinkOrUnreadable(packDir)) throw new ModLoadException("Pack folder must not be a symbolic link or junction.", folderName, 0, 0);

            // mod.json
            string manifestPath = Contained(packDir, Path.Combine(packDir, "mod.json"), folderName + "/mod.json");
            if (!File.Exists(manifestPath)) throw new ModLoadException("Missing mod.json.", folderName, 0, 0);
            var manifestBytes = ReadCapped(manifestPath, Math.Min(_opt.MaxManifestBytes, MaxFile()), folderName + "/mod.json");
            bytes += manifestBytes.Length;
            var manifest = ModManifest.FromJson(SafeJson.Parse(manifestBytes, _opt.Limits, folderName + "/mod.json"), folderName + "/mod.json");
            foreach (var w in manifest.Warnings) result.Warn(manifest.Id, folderName + "/mod.json", w);
            if (!string.Equals(manifest.Id, folderName, StringComparison.Ordinal))
                result.Warn(manifest.Id, folderName, "Folder name '" + folderName + "' differs from mod id '" + manifest.Id + "' (allowed, but confusing).");

            WarnAboutCode(packDir, folderName, manifest.Id, result);

            // data/*.json
            var docs = new List<ModPatchDocument>();
            string dataDir = Contained(packDir, Path.Combine(packDir, "data"), folderName + "/data");
            var fileNames = new List<string>();
            if (Directory.Exists(dataDir))
            {
                if (IsLinkOrUnreadable(dataDir)) throw new ModLoadException("data/ must not be a symbolic link or junction.", folderName, 0, 0);
                if (manifest.DataFiles.Count > 0) fileNames.AddRange(manifest.DataFiles); // explicit order (names pre-validated)
                else
                {
                    string[] files = ListFiles(dataDir, folderName + "/data");
                    foreach (var f in files)
                    {
                        string n = Path.GetFileName(f);
                        if (!n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsCodeFile(n)) result.Warn(manifest.Id, folderName + "/data/" + SafeJson.Printable(n), "Code file ignored: mods are data-only and code is never loaded.");
                            continue;
                        }
                        if (!ManifestNameOk(n)) { result.Warn(manifest.Id, folderName + "/data/" + SafeJson.Printable(n), "Ignored: data file names must match [A-Za-z0-9_.-]+.json."); continue; }
                        fileNames.Add(n);
                    }
                    fileNames.Sort(StringComparer.Ordinal);
                }
            }
            else if (manifest.DataFiles.Count > 0) throw new ModLoadException("mod.json lists dataFiles but the pack has no data/ folder.", folderName, 0, 0);

            if (fileNames.Count > _opt.MaxDataFilesPerPack) throw new ModLoadException("More than " + _opt.MaxDataFilesPerPack + " data files.", folderName, 0, 0);
            foreach (var n in fileNames)
            {
                string logical = folderName + "/data/" + n;
                if (!ManifestNameOk(n)) throw new ModLoadException("Invalid data file name '" + SafeJson.Printable(n) + "'.", folderName, 0, 0);
                string path = Contained(dataDir, Path.Combine(dataDir, n), logical);
                if (!File.Exists(path)) throw new ModLoadException("Data file listed in mod.json does not exist.", logical, 0, 0);
                var data = ReadCapped(path, MaxFile(), logical);
                bytes += data.Length;
                if (bytes > _opt.MaxPackBytes) throw new ModLoadException("Pack is larger than " + _opt.MaxPackBytes + " bytes.", folderName, 0, 0);
                var root2 = SafeJson.Parse(data, _opt.Limits, logical);
                if (!root2.IsObject) throw new ModLoadException("A patch document must be a JSON object.", logical, root2.Line, root2.Column);
                docs.Add(new ModPatchDocument(logical, root2));
            }
            if (docs.Count == 0) result.Warn(manifest.Id, folderName, "Pack has no data files; it changes nothing.");
            return new ModPack(manifest, docs, packDir);
        }

        private static bool ManifestNameOk(string n) => ModManifest.IsValidDataFileName(n);

        private long MaxFile()
        {
            long lim = (_opt.Limits ?? JsonLimits.Default).Normalized().MaxInputBytes;
            return Math.Min(_opt.MaxFileBytes, lim);
        }

        private void WarnAboutCode(string packDir, string folderName, string id, ModLoadResult result)
        {
            string[] files;
            try { files = ListFiles(packDir, folderName); }
            catch (ModLoadException) { return; }
            foreach (var f in files)
            {
                string n = Path.GetFileName(f);
                if (IsCodeFile(n)) result.Warn(id, folderName + "/" + SafeJson.Printable(n), "Code file ignored: mods are data-only and code is never loaded.");
            }
        }

        private string[] ListFiles(string dir, string logical)
        {
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception e) when (IsIoException(e)) { throw new ModLoadException("Cannot list directory (" + e.GetType().Name + ").", logical, 0, 0); }
            if (files.Length > _opt.MaxDirectoryEntries) throw new ModLoadException("Directory has more than " + _opt.MaxDirectoryEntries + " files.", logical, 0, 0);
            return files;
        }

        private static bool IsCodeFile(string name)
        {
            foreach (var ext in CodeExtensions) if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ----------------------------------------------------------------- file-system safety
        /// <summary>A plain, non-hidden folder name of [A-Za-z0-9_.-]{1,64} (so no separators, drive letters or "..").</summary>
        public static bool IsValidFolderName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64 || name[0] == '.' || name[name.Length - 1] == '.') return false;
            if (name.IndexOf("..", StringComparison.Ordinal) >= 0 || ModText.IsReservedDeviceName(name)) return false;
            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Returns the full path of <paramref name="candidate"/> if it lies strictly inside <paramref name="parentFull"/>; throws otherwise.</summary>
        public static string Contained(string parentFull, string candidate, string logical)
        {
            string full;
            try { full = TrimSeparators(Path.GetFullPath(candidate)); }
            catch (Exception e) when (IsIoException(e)) { throw new ModLoadException("Invalid path (" + e.GetType().Name + ").", logical, 0, 0); }
            string parent = TrimSeparators(parentFull) + Path.DirectorySeparatorChar;
            var cmp = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(parent, cmp) || full.Length <= parent.Length)
                throw new ModLoadException("Path escapes the mods directory.", logical, 0, 0);
            return full;
        }

        private static string TrimSeparators(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            int end = p.Length;
            while (end > 1 && (p[end - 1] == Path.DirectorySeparatorChar || p[end - 1] == Path.AltDirectorySeparatorChar))
            {
                if (end == 3 && p[1] == ':') break; // keep "C:\"
                end--;
            }
            return p.Substring(0, end);
        }

        /// <summary>True for symbolic links, junctions and other reparse points; fails closed (true) when attributes cannot be read.</summary>
        private static bool IsLinkOrUnreadable(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch (Exception) { return true; }
        }

        private byte[] ReadCapped(string path, long maxBytes, string logical)
        {
            if (IsLinkOrUnreadable(path)) throw new ModLoadException("Symbolic links are not allowed in mod packs.", logical, 0, 0);
            try
            {
                var info = new FileInfo(path);
                long len = info.Length;
                // Length 0 also covers FIFOs and devices: refuse before opening so a read can never block.
                if (len == 0) throw new ModLoadException("File is empty (or not a regular file).", logical, 0, 0);
                if (len > maxBytes) throw new ModLoadException("File is larger than " + maxBytes + " bytes.", logical, 0, 0);
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    var buf = new byte[len];
                    int read = 0;
                    while (read < len)
                    {
                        int n = fs.Read(buf, read, (int)(len - read));
                        if (n <= 0) break;
                        read += n;
                    }
                    if (fs.ReadByte() >= 0) throw new ModLoadException("File changed size while being read.", logical, 0, 0);
                    if (read != len) { var shrunk = new byte[read]; Array.Copy(buf, shrunk, read); buf = shrunk; }
                    return buf;
                }
            }
            catch (ModLoadException) { throw; }
            catch (Exception e) when (IsIoException(e)) { throw new ModLoadException("Cannot read file (" + e.GetType().Name + ").", logical, 0, 0); }
        }

        private static bool IsIoException(Exception e) =>
            e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException || e is System.Security.SecurityException;

        // ----------------------------------------------------------------- resolution (pure, testable in memory)
        /// <summary>
        /// Validates and orders candidate packs (no IO). Rejects duplicate ids, game-version mismatches, missing
        /// or mismatched required dependencies (transitively) and dependency cycles; orders the rest
        /// dependencies-first, ties broken by (loadOrder, id); records field conflicts (later pack wins).
        /// </summary>
        public static void Resolve(IList<ModPack> candidates, ModLoaderOptions options, ModLoadResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            options = options ?? new ModLoaderOptions();
            var sorted = new List<ModPack>();
            if (candidates != null) foreach (var p in candidates) if (p != null) sorted.Add(p);
            sorted.Sort(CompareOrderSafe);

            // 1. duplicate ids & game version
            var byId = new Dictionary<string, ModPack>(StringComparer.Ordinal);
            var alive = new List<ModPack>();
            foreach (var p in sorted)
            {
                string id = p.Manifest.Id;
                if (!IsSane(p.Manifest)) { Reject(result, p, "Invalid manifest (id, version, gameVersionRange or dependencies missing/invalid)."); continue; }
                if (byId.ContainsKey(id)) { Reject(result, p, "Duplicate mod id '" + id + "' (another pack with this id was already loaded)."); continue; }
                byId[id] = p;
                if (options.GameVersion != null && !p.Manifest.GameVersionRange.Contains(options.GameVersion))
                {
                    Reject(result, p, "Requires game version " + p.Manifest.GameVersionRange + " but the game is " + options.GameVersion + ".");
                    continue;
                }
                alive.Add(p);
            }

            // 2. required dependencies (iterate to a fixed point so rejections cascade)
            var aliveIds = new Dictionary<string, ModPack>(StringComparer.Ordinal);
            foreach (var p in alive) aliveIds[p.Manifest.Id] = p;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < alive.Count; i++)
                {
                    var p = alive[i];
                    string problem = null;
                    foreach (var dep in p.Manifest.Dependencies)
                    {
                        if (aliveIds.TryGetValue(dep.Id, out var target))
                        {
                            if (!dep.Versions.Contains(target.Manifest.Version))
                            {
                                if (dep.Optional) continue;
                                problem = "Requires '" + dep.Id + "' " + dep.Versions + " but version " + target.Manifest.Version + " is installed.";
                                break;
                            }
                        }
                        else if (!dep.Optional) { problem = "Missing required dependency '" + dep.Id + "' " + dep.Versions + "."; break; }
                    }
                    if (problem == null) continue;
                    Reject(result, p, problem);
                    aliveIds.Remove(p.Manifest.Id);
                    alive.RemoveAt(i);
                    i--;
                    changed = true;
                }
            }

            // 3. deterministic topological order (Kahn; ready set ordered by loadOrder then id)
            var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
            var dependents = new Dictionary<string, List<ModPack>>(StringComparer.Ordinal);
            foreach (var p in alive) { indegree[p.Manifest.Id] = 0; dependents[p.Manifest.Id] = new List<ModPack>(); }
            foreach (var p in alive)
                foreach (var dep in p.Manifest.Dependencies)
                    if (aliveIds.ContainsKey(dep.Id) && dep.Versions.Contains(aliveIds[dep.Id].Manifest.Version))
                    {
                        indegree[p.Manifest.Id]++;
                        dependents[dep.Id].Add(p);
                    }
            var ready = new List<ModPack>();
            foreach (var p in alive) if (indegree[p.Manifest.Id] == 0) ready.Add(p);
            var ordered = new List<ModPack>();
            while (ready.Count > 0)
            {
                ready.Sort(CompareOrder);
                var next = ready[0];
                ready.RemoveAt(0);
                ordered.Add(next);
                foreach (var d in dependents[next.Manifest.Id])
                    if (--indegree[d.Manifest.Id] == 0) ready.Add(d);
            }

            // 4. whatever is left is in, or depends on, a dependency cycle
            if (ordered.Count < alive.Count)
            {
                var placed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in ordered) placed.Add(p.Manifest.Id);
                var left = new List<ModPack>();
                foreach (var p in alive) if (!placed.Contains(p.Manifest.Id)) left.Add(p);
                var leftIds = new Dictionary<string, ModPack>(StringComparer.Ordinal);
                foreach (var p in left) leftIds[p.Manifest.Id] = p;
                foreach (var p in left)
                {
                    string cycle = FindCycle(p, leftIds);
                    Reject(result, p, cycle != null
                        ? "Dependency cycle detected: " + cycle + "."
                        : "Depends on a mod that is part of a dependency cycle.");
                }
            }

            // 5. conflicts: two packs patch the same field -> the later wins (warning)
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in ordered)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in ModPatchApplier.CollectTargetPaths(p))
                {
                    if (!seen.Add(path)) continue;
                    if (owners.TryGetValue(path, out var prev) && !string.Equals(prev, p.Manifest.Id, StringComparison.Ordinal))
                    {
                        result.Conflicts.Add(new ModConflict { Path = path, OverriddenModId = prev, WinningModId = p.Manifest.Id });
                        result.Warn(p.Manifest.Id, "", "Conflict on '" + path + "': also patched by '" + prev + "'; '" + p.Manifest.Id + "' loads later and wins.");
                    }
                    owners[path] = p.Manifest.Id;
                }
            }
            result.Packs.AddRange(ordered);
        }

        private static bool IsSane(ModManifest m)
        {
            if (m == null || !ModManifest.IsValidId(m.Id) || m.Version == null || m.GameVersionRange == null || m.Dependencies == null) return false;
            foreach (var d in m.Dependencies) if (d == null || d.Versions == null || !ModManifest.IsValidId(d.Id) || d.Id == m.Id) return false;
            return true;
        }

        /// <summary>Order key: loadOrder ascending, then id (ordinal).</summary>
        public static int CompareOrder(ModPack a, ModPack b)
        {
            int c = a.Manifest.LoadOrder.CompareTo(b.Manifest.LoadOrder);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Manifest.Id, b.Manifest.Id);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Directory, b.Directory);
        }

        private static int CompareOrderSafe(ModPack a, ModPack b)
        {
            int c = a.Manifest.LoadOrder.CompareTo(b.Manifest.LoadOrder);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Manifest.Id ?? "", b.Manifest.Id ?? "");
            return c != 0 ? c : string.CompareOrdinal(a.Directory, b.Directory);
        }

        /// <summary>Follows dependencies from <paramref name="start"/> inside the leftover set; returns "a -> b -> a" if start is on a cycle.</summary>
        private static string FindCycle(ModPack start, Dictionary<string, ModPack> left)
        {
            // iterative DFS with an explicit path; bounded by the leftover count
            var path = new List<string>();
            var onPath = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            return Dfs(start.Manifest.Id, start.Manifest.Id, left, path, onPath, visited, 0);
        }

        private static string Dfs(string node, string target, Dictionary<string, ModPack> left, List<string> path, HashSet<string> onPath, HashSet<string> visited, int depth)
        {
            if (depth > left.Count + 1) return null;
            path.Add(node);
            onPath.Add(node);
            visited.Add(node);
            var deps = new List<string>();
            foreach (var d in left[node].Manifest.Dependencies) if (left.ContainsKey(d.Id)) deps.Add(d.Id);
            deps.Sort(StringComparer.Ordinal);
            foreach (var d in deps)
            {
                if (d == target) return string.Join(" -> ", path) + " -> " + target;
                if (visited.Contains(d)) continue;
                var r = Dfs(d, target, left, path, onPath, visited, depth + 1);
                if (r != null) return r;
            }
            path.RemoveAt(path.Count - 1);
            onPath.Remove(node);
            return null;
        }

        private static void Reject(ModLoadResult result, ModPack p, string message)
        {
            result.Error(p.Manifest.Id, "", message);
            result.Rejected.Add(p.Manifest.Id);
        }
    }
}
