using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity.Editor
{
    /// <summary>Each small Resource points to one reviewed GLB. Only its three meshes enter the player.</summary>
    [ScriptedImporter(2, "buildingart", 10)]
    public sealed class BuildingArtImporter : ScriptedImporter
    {
        private static readonly string[] LodNames = { "LOD0", "LOD1", "LOD2" };
        public override void OnImportAsset(AssetImportContext context)
        {
            string[] binding = File.ReadAllLines(context.assetPath);
            var model = ScriptableObject.CreateInstance<BuildingArtModel>();
            model.name = Path.GetFileNameWithoutExtension(context.assetPath);
            context.AddObjectToAsset("building", model);
            context.SetMainObject(model);
            if (binding.Length != 2 || binding[1].Length != 64)
            {
                context.LogImportError("The building reference needs a reviewed GLB path and its SHA-256 hash.");
                return;
            }
            string path = binding[0];
            if (!path.StartsWith("Assets/GodSim/Art/Models/Buildings/", StringComparison.Ordinal)
                || !path.EndsWith(".glb", StringComparison.Ordinal) || path.Contains(".."))
            {
                context.LogImportError("Invalid approved building GLB path: " + path);
                return;
            }
            context.DependsOnArtifact(path);
            if (!File.Exists(path) || !MatchesReviewedModel(path, binding[1]))
            {
                context.LogImportWarning("The building GLB changed since review or is missing at " + path +
                    ". The procedural building will be used until the approved reference is regenerated.");
                return;
            }
            var lods = new Mesh[3];
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is Mesh mesh)) continue;
                int level = Array.IndexOf(LodNames, mesh.name);
                if (level >= 0) lods[level] = mesh;
            }
            if (Array.Exists(lods, mesh => mesh == null))
            {
                context.LogImportWarning("Missing glTFast LOD meshes at " + path + ". Buildings using this model will use procedural art.");
                return;
            }
            for (int level = 0; level < lods.Length; level++)
            {
                // Player batching requires readable geometry. No glTF scene, imported material or per-model texture is retained.
                var source = lods[level];
                var mesh = new Mesh {
                    name = source.name, indexFormat = source.indexFormat,
                    vertices = source.vertices, normals = source.normals, uv = source.uv, triangles = source.triangles,
                    bounds = source.bounds
                };
                if (source.HasVertexAttribute(VertexAttribute.Color)) mesh.colors = source.colors;
                context.AddObjectToAsset(mesh.name, mesh);
                lods[level] = mesh;
            }
            model.Lod0 = lods[0];
            model.Lod1 = lods[1];
            model.Lod2 = lods[2];
        }

        private static bool MatchesReviewedModel(string path, string expected)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                string digest = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                return string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
