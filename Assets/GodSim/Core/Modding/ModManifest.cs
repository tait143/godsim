using System;
using System.Collections.Generic;

namespace GodSim.Core.Modding
{
    /// <summary>A dependency declared in mod.json.</summary>
    public sealed class ModDependency
    {
        public string Id = "";
        public SemVersionRange Versions = SemVersionRange.Any;
        /// <summary>Optional dependencies only affect ordering; a missing optional dependency is not an error.</summary>
        public bool Optional;
        public override string ToString() => Id + " " + Versions + (Optional ? " (optional)" : "");
    }

    /// <summary>
    /// Parsed and validated mod.json. Every string is sanitized; the id matches ^[a-z0-9_.-]{3,48}$.
    /// </summary>
    public sealed class ModManifest
    {
        public const int MaxDependencies = 32;
        public const int MaxDataFiles = 64;
        public const int MinLoadOrder = -10000;
        public const int MaxLoadOrder = 10000;

        public string Id = "";
        public string Name = "";
        public SemVersion Version = new SemVersion(0, 0, 0);
        public string Author = "";
        public string Description = "";
        public SemVersionRange GameVersionRange = SemVersionRange.Any;
        public int LoadOrder;
        public List<ModDependency> Dependencies = new List<ModDependency>();
        /// <summary>Optional explicit list of data file names (inside data/) in application order; empty = all data/*.json sorted.</summary>
        public List<string> DataFiles = new List<string>();
        /// <summary>Non-fatal notes produced while parsing (e.g. unknown keys).</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>True when the id matches ^[a-z0-9_.-]{3,48}$ (checked by hand, no regex engine).</summary>
        public static bool IsValidId(string id)
        {
            if (id == null || id.Length < 3 || id.Length > 48) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return false;
            }
            // "." / ".." style ids would be confusing as folder names; require at least one alphanumeric.
            for (int i = 0; i < id.Length; i++) if ((id[i] >= 'a' && id[i] <= 'z') || (id[i] >= '0' && id[i] <= '9')) return true;
            return false;
        }

        /// <summary>True for a plain data file name: [A-Za-z0-9_.-]{1,64} ending in ".json", no path separators, no "..".</summary>
        public static bool IsValidDataFileName(string name)
        {
            if (name == null || name.Length < 6 || name.Length > 64) return false;
            if (!name.EndsWith(".json", StringComparison.Ordinal)) return false;
            if (name.IndexOf("..", StringComparison.Ordinal) >= 0 || name[0] == '.') return false;
            if (ModText.IsReservedDeviceName(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Parses mod.json text.</summary>
        public static ModManifest Parse(string json, JsonLimits limits = null, string sourceName = "mod.json")
        {
            return FromJson(SafeJson.Parse(json, limits, sourceName), sourceName);
        }

        /// <summary>Validates a parsed mod.json. Throws <see cref="ModLoadException"/> on any error.</summary>
        public static ModManifest FromJson(JsonValue root, string sourceName = "mod.json")
        {
            if (root == null || !root.IsObject) throw Err("mod.json must be a JSON object.", root, sourceName);
            var m = new ModManifest();
            bool hasId = false, hasName = false, hasVersion = false;
            for (int i = 0; i < root.Count; i++)
            {
                string key = root.KeyAt(i);
                var v = root.Item(i);
                switch (key)
                {
                    case "id":
                        if (!v.IsString || !IsValidId(v.StringValue)) throw Err("'id' must match ^[a-z0-9_.-]{3,48}$.", v, sourceName);
                        m.Id = v.StringValue; hasId = true; break;
                    case "name":
                        m.Name = RequireText(v, "name", 64, false, sourceName);
                        if (m.Name.Length == 0) throw Err("'name' is empty after sanitization.", v, sourceName);
                        hasName = true; break;
                    case "version":
                        if (!v.IsString || !SemVersion.TryParse(v.StringValue, out var ver)) throw Err("'version' must be a semantic version such as \"1.0.0\".", v, sourceName);
                        m.Version = ver; hasVersion = true; break;
                    case "author": m.Author = RequireText(v, "author", 64, false, sourceName); break;
                    case "description": m.Description = RequireText(v, "description", 1024, true, sourceName); break;
                    case "gameVersionRange":
                        if (!v.IsString || !SemVersionRange.TryParse(v.StringValue, out var range)) throw Err("'gameVersionRange' must be a version range such as \">=0.1.0 <1.0.0\".", v, sourceName);
                        m.GameVersionRange = range; break;
                    case "loadOrder":
                        if (!v.IsNumber || v.NumberValue != Math.Floor(v.NumberValue) || v.NumberValue < MinLoadOrder || v.NumberValue > MaxLoadOrder)
                            throw Err("'loadOrder' must be an integer in [" + MinLoadOrder + ", " + MaxLoadOrder + "].", v, sourceName);
                        m.LoadOrder = (int)v.NumberValue; break;
                    case "dependencies": ParseDependencies(m, v, sourceName); break;
                    case "dataFiles": ParseDataFiles(m, v, sourceName); break;
                    default:
                        m.Warnings.Add(ModLoadException.Format("Unknown manifest key '" + SafeJson.Printable(key) + "' ignored.", sourceName, v.Line, v.Column));
                        break;
                }
            }
            if (!hasId) throw Err("Missing required key 'id'.", root, sourceName);
            if (!hasName) throw Err("Missing required key 'name'.", root, sourceName);
            if (!hasVersion) throw Err("Missing required key 'version'.", root, sourceName);
            foreach (var d in m.Dependencies)
                if (string.Equals(d.Id, m.Id, StringComparison.Ordinal)) throw Err("A mod cannot depend on itself.", root, sourceName);
            return m;
        }

        private static void ParseDependencies(ModManifest m, JsonValue v, string src)
        {
            if (!v.IsArray) throw Err("'dependencies' must be an array.", v, src);
            if (v.Count > MaxDependencies) throw Err("At most " + MaxDependencies + " dependencies are allowed.", v, src);
            foreach (var d in v.Items)
            {
                var dep = new ModDependency();
                if (d.IsString)
                {
                    if (!IsValidId(d.StringValue)) throw Err("Dependency id must match ^[a-z0-9_.-]{3,48}$.", d, src);
                    dep.Id = d.StringValue;
                }
                else if (d.IsObject)
                {
                    for (int i = 0; i < d.Count; i++)
                    {
                        var dv = d.Item(i);
                        switch (d.KeyAt(i))
                        {
                            case "id":
                                if (!dv.IsString || !IsValidId(dv.StringValue)) throw Err("Dependency id must match ^[a-z0-9_.-]{3,48}$.", dv, src);
                                dep.Id = dv.StringValue; break;
                            case "version":
                                if (!dv.IsString || !SemVersionRange.TryParse(dv.StringValue, out var r)) throw Err("Dependency 'version' must be a version range.", dv, src);
                                dep.Versions = r; break;
                            case "optional":
                                if (!dv.IsBool) throw Err("Dependency 'optional' must be true or false.", dv, src);
                                dep.Optional = dv.BoolValue; break;
                            default:
                                m.Warnings.Add(ModLoadException.Format("Unknown dependency key '" + SafeJson.Printable(d.KeyAt(i)) + "' ignored.", src, dv.Line, dv.Column));
                                break;
                        }
                    }
                    if (dep.Id.Length == 0) throw Err("Dependency is missing 'id'.", d, src);
                }
                else throw Err("Each dependency must be an id string or an object {\"id\", \"version\", \"optional\"}.", d, src);
                foreach (var existing in m.Dependencies)
                    if (string.Equals(existing.Id, dep.Id, StringComparison.Ordinal)) throw Err("Duplicate dependency '" + dep.Id + "'.", d, src);
                m.Dependencies.Add(dep);
            }
        }

        private static void ParseDataFiles(ModManifest m, JsonValue v, string src)
        {
            if (!v.IsArray) throw Err("'dataFiles' must be an array of file names.", v, src);
            if (v.Count > MaxDataFiles) throw Err("At most " + MaxDataFiles + " data files are allowed.", v, src);
            foreach (var f in v.Items)
            {
                if (!f.IsString || !IsValidDataFileName(f.StringValue))
                    throw Err("Data file '" + SafeJson.Printable(f.StringValue) + "' must be a plain file name inside data/ ([A-Za-z0-9_.-]+.json; no paths, no '..').", f, src);
                foreach (var e in m.DataFiles)
                    if (string.Equals(e, f.StringValue, StringComparison.OrdinalIgnoreCase)) throw Err("Duplicate data file '" + f.StringValue + "'.", f, src);
                m.DataFiles.Add(f.StringValue);
            }
        }

        private static string RequireText(JsonValue v, string key, int max, bool newlines, string src)
        {
            if (!v.IsString) throw Err("'" + key + "' must be a string.", v, src);
            return ModText.Sanitize(v.StringValue, max, newlines);
        }

        private static ModLoadException Err(string msg, JsonValue at, string src) =>
            new ModLoadException(msg, src, at != null ? at.Line : 0, at != null ? at.Column : 0);

        public override string ToString() => Id + "@" + Version;
    }
}
