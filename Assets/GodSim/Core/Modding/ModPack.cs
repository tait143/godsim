using System;
using System.Collections.Generic;

namespace GodSim.Core.Modding
{
    /// <summary>One parsed data/*.json patch document.</summary>
    public sealed class ModPatchDocument
    {
        /// <summary>Logical source name used in diagnostics, e.g. "my_mod/data/buildings.json".</summary>
        public string SourceName { get; }
        /// <summary>Parsed root (must be an object to be applied).</summary>
        public JsonValue Root { get; }

        public ModPatchDocument(string sourceName, JsonValue root)
        {
            SourceName = sourceName ?? "";
            Root = root;
        }

        /// <summary>Parses a patch document from text (throws <see cref="ModLoadException"/>).</summary>
        public static ModPatchDocument Parse(string sourceName, string json, JsonLimits limits = null)
        {
            var root = SafeJson.Parse(json, limits, sourceName);
            if (!root.IsObject) throw new ModLoadException("A patch document must be a JSON object.", sourceName, root.Line, root.Column);
            return new ModPatchDocument(sourceName, root);
        }
    }

    /// <summary>A data pack: validated manifest plus its JSON patch documents in application order.</summary>
    public sealed class ModPack
    {
        public ModManifest Manifest { get; }
        public IReadOnlyList<ModPatchDocument> Documents => _documents;
        /// <summary>Folder the pack was loaded from ("" for in-memory packs).</summary>
        public string Directory { get; }
        private readonly List<ModPatchDocument> _documents;

        public ModPack(ModManifest manifest, IEnumerable<ModPatchDocument> documents, string directory = "")
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _documents = new List<ModPatchDocument>();
            if (documents != null) foreach (var d in documents) if (d != null) _documents.Add(d);
            Directory = directory ?? "";
        }

        /// <summary>In-memory construction from JSON text (manifest + named documents, applied in the given order).</summary>
        public static ModPack FromJson(string manifestJson, params KeyValuePair<string, string>[] documents)
        {
            var manifest = ModManifest.Parse(manifestJson);
            var docs = new List<ModPatchDocument>();
            if (documents != null)
                foreach (var kv in documents)
                    docs.Add(ModPatchDocument.Parse(manifest.Id + "/data/" + kv.Key, kv.Value));
            return new ModPack(manifest, docs);
        }

        public string Id => Manifest.Id;
        public override string ToString() => Manifest.ToString();
    }
}
