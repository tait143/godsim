using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using GodSim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    [TestFixture]
    public sealed class CatalogTownTests
    {
        [Test]
        public void TwoHundredReviewedModelsBatchWithTheirExportedGeometry()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            const string manifest = "Assets/GodSim/Art/Models/manifest.json";
            while (root != null && !File.Exists(Path.Combine(root.FullName, manifest)))
                root = root.Parent;
            if (root == null) Assert.Ignore("No generated building catalog in this checkout.");
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName, manifest)));
            var entries = doc.RootElement.GetProperty("assets");
            int count = entries.GetArrayLength();
            if (count < 200) Assert.Ignore($"A town of 200 distinct assets requires more than the {count} currently published.");
            var meshes = new List<Mesh>();
            int triangleCount = 0;
            for (int i = 0; i < 200; i++)
            {
                // Sample the complete catalog, including its last race and rarest authored models.
                var entry = entries[(int)((long)i * (count - 1) / 199)];
                string relative = entry.GetProperty("path").GetString();
                string path = Path.Combine(root.FullName, relative);
                string pointer = Path.Combine(root.FullName, "Assets/GodSim/Art/Resources/GodSimBuildingModels",
                    entry.GetProperty("id").GetString() + ".buildingart");
                string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                Assert.That(File.ReadAllLines(pointer), Is.EqualTo(new[] { relative, digest }), pointer);
                Assert.That(entry.GetProperty("sha256").GetString(), Is.EqualTo(digest), relative);
                var mesh = ReadMesh(path);
                var triangles = new List<int>();
                mesh.GetTriangles(triangles, 0);
                Assert.That(triangles.Count / 3, Is.EqualTo(entry.GetProperty("triangles").GetInt32()), path);
                triangleCount += triangles.Count / 3;
                meshes.Add(mesh);
            }
            BuildingRenderer.CheckExportedTown(meshes, triangleCount);
        }

        // AssetForge's three indexed GLB meshes use tightly packed little-endian accessors.
        // Decode LOD0 directly so this headless check can exercise real vertex counts without an Editor.
        private static Mesh ReadMesh(string path)
        {
            using var input = new BinaryReader(File.OpenRead(path));
            Assert.That(input.ReadUInt32(), Is.EqualTo(0x46546C67u));
            Assert.That(input.ReadUInt32(), Is.EqualTo(2u));
            input.ReadUInt32();
            int length = input.ReadInt32();
            Assert.That(input.ReadUInt32(), Is.EqualTo(0x4E4F534Au));
            using var doc = JsonDocument.Parse(input.ReadBytes(length));
            input.ReadUInt32();
            Assert.That(input.ReadUInt32(), Is.EqualTo(0x004E4942u));
            long binaryStart = input.BaseStream.Position;
            var gltf = doc.RootElement;
            var primitive = gltf.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var attributes = primitive.GetProperty("attributes");

            int SeekAccessor(JsonElement reference, int component, string shape)
            {
                var accessor = gltf.GetProperty("accessors")[reference.GetInt32()];
                Assert.That(accessor.GetProperty("componentType").GetInt32(), Is.EqualTo(component));
                Assert.That(accessor.GetProperty("type").GetString(), Is.EqualTo(shape));
                var view = gltf.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
                input.BaseStream.Position = binaryStart + view.GetProperty("byteOffset").GetInt32()
                    + (accessor.TryGetProperty("byteOffset", out var start) ? start.GetInt32() : 0);
                return accessor.GetProperty("count").GetInt32();
            }

            List<Vector3> ReadVectors(string channel)
            {
                int count = SeekAccessor(attributes.GetProperty(channel), 5126, "VEC3");
                var values = new List<Vector3>(count);
                for (int i = 0; i < count; i++)
                    values.Add(new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle()));
                return values;
            }

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.SetVertices(ReadVectors("POSITION"));
            mesh.SetNormals(ReadVectors("NORMAL"));
            int uvs = SeekAccessor(attributes.GetProperty("TEXCOORD_0"), 5126, "VEC2");
            var coordinates = new List<Vector2>(uvs);
            for (int i = 0; i < uvs; i++)
                coordinates.Add(new Vector2 { x = input.ReadSingle(), y = 1 - input.ReadSingle() });
            mesh.SetUVs(0, coordinates);
            int count = SeekAccessor(primitive.GetProperty("indices"), 5123, "SCALAR");
            var indices = new List<int>(count);
            for (int i = 0; i < count; i++) indices.Add(input.ReadUInt16());
            mesh.SetTriangles(indices, 0);
            return mesh;
        }
    }

    public sealed partial class BuildingRenderer
    {
        private static Vector3 PlotPosition(int i) => new Vector3((i % 20) * 12, (i % 3) * .25f, (i / 20) * 12);
        private static Vector3 ModelScale(int i) => new Vector3(1f + (i % 3) * .2f, .65f + (i % 5) * .08f, 1.35f + (i % 4) * .1f);

        // Independent oracle for the four headings used by placed buildings.
        private static Vector3 Turn(Vector3 v, int quarterTurns) => (quarterTurns % 4) switch
        {
            0 => v, 1 => new Vector3(v.z, v.y, -v.x), 2 => new Vector3(-v.x, v.y, -v.z), _ => new Vector3(-v.z, v.y, v.x)
        };

        internal static void CheckExportedTown(List<Mesh> sources, int triangles)
        {
            var renderer = new GameObject("exported town").AddComponent<BuildingRenderer>();
            renderer._art = new ArtLibrary(null, new Material(), null);
            renderer._mpb = new MaterialPropertyBlock();
            var townOrigin = new Vector3(35, -4, 18);
            renderer.transform.SetPositionAndRotation(townOrigin, Quaternion.Euler(0, 90, 0));
            var bodyOffset = new Vector3(.25f, .125f, -.375f);
            for (int i = 0; i < sources.Count; i++)
            {
                var root = new GameObject("plot").transform;
                root.SetParent(renderer.transform, false);
                root.SetPositionAndRotation(townOrigin + Turn(PlotPosition(i), 1), Quaternion.Euler(0, (i % 4 + 1) * 90, 0));
                var body = new GameObject("body").transform;
                body.SetParent(root, false);
                body.localPosition = bodyOffset;
                body.localScale = ModelScale(i);
                var view = new View {
                    Building = new Building { SettlementId = 1 }, BodyT = body, Full = sources[i], HasArt = true
                };
                renderer.SetBatched(view, true);
            }
            renderer.FlushBatches();
            Assert.That(renderer._batches.Select(p => p.Views.Count), Is.EqualTo(new[] { 64, 64, 64, 8 }));
            int total = 0;
            int vertices = 0;
            int wide = 0;
            int modelIndex = 0;
            var indices = new List<int>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var sourcePositions = new List<Vector3>();
            var sourceNormals = new List<Vector3>();
            foreach (var page in renderer._batches)
            {
                page.Mesh.GetVertices(positions);
                page.Mesh.GetNormals(normals);
                Assert.That(normals.Count, Is.EqualTo(positions.Count));
                int start = 0;
                foreach (var view in page.Views)
                {
                    var source = sources[modelIndex];
                    Assert.That(view.Full, Is.SameAs(source));
                    source.GetVertices(sourcePositions);
                    source.GetNormals(sourceNormals);
                    Assert.That(sourceNormals.Count, Is.EqualTo(sourcePositions.Count), source.name);
                    var plot = PlotPosition(modelIndex);
                    var scale = ModelScale(modelIndex);
                    float pointError = 0, normalError = 0, lengthError = 0;
                    for (int j = 0; j < sourcePositions.Count; j++)
                    {
                        var p = sourcePositions[j];
                        var n = sourceNormals[j];
                        var scaled = new Vector3(p.x * scale.x, p.y * scale.y, p.z * scale.z);
                        var expectedPoint = plot + Turn(bodyOffset + scaled, modelIndex);
                        var expectedNormal = Turn(new Vector3(n.x / scale.x, n.y / scale.y, n.z / scale.z), modelIndex).normalized;
                        var normal = normals[start + j];
                        pointError = Math.Max(pointError, (positions[start + j] - expectedPoint).sqrMagnitude);
                        normalError = Math.Max(normalError, (normal - expectedNormal).sqrMagnitude);
                        lengthError = Math.Max(lengthError, Math.Abs(normal.sqrMagnitude - 1));
                    }
                    Assert.That(Math.Sqrt(pointError), Is.LessThan(.0002), $"{source.name} page-space position");
                    Assert.That(Math.Sqrt(normalError), Is.LessThan(.00001), $"{source.name} inverse-transpose normal");
                    Assert.That(lengthError, Is.LessThan(.00001), $"{source.name} unit normal");
                    start += source.vertexCount;
                    modelIndex++;
                }
                Assert.That(start, Is.EqualTo(positions.Count));
                var size = page.Mesh.bounds.max - page.Mesh.bounds.min;
                Assert.That(size.x, Is.GreaterThan(12f));
                Assert.That(size.y, Is.GreaterThan(.25f));
                Assert.That(size.z, Is.GreaterThan(.25f));
                TestContext.Progress.WriteLine($"{page.Views.Count} bodies, page bounds {size.x:F2} x {size.y:F2} x {size.z:F2}");
                page.Mesh.GetTriangles(indices, 0);
                Assert.That(indices.Max(), Is.LessThan(page.Mesh.vertexCount));
                Assert.That(indices.Min(), Is.GreaterThanOrEqualTo(0));
                bool needsWide = page.Mesh.vertexCount > ushort.MaxValue;
                Assert.That(page.Mesh.indexFormat, Is.EqualTo(needsWide ? IndexFormat.UInt32 : IndexFormat.UInt16));
                if (needsWide) wide++;
                total += indices.Count / 3;
                vertices += page.Mesh.vertexCount;
            }
            Assert.That(total, Is.EqualTo(triangles));
            Assert.That(vertices, Is.EqualTo(sources.Sum(m => m.vertexCount)));
            TestContext.Progress.WriteLine($"200 exported LOD0s, {vertices} vertices, {total} triangles, "
                + $"{renderer._batches.Count} single-material body meshes, {wide} with 32-bit indices.");
            renderer.ClearBatches();
        }
    }
}
