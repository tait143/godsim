using System.Collections.Generic;
using System.Linq;
using GodSim.Core;
using NUnit.Framework;
using UnityEngine;

namespace GodSim.Unity
{
    internal static class Program
    {
        private static int Main(string[] args) => new NUnitLite.AutoRun().Execute(args);
    }

    [TestFixture]
    public sealed class BuildingArtTests
    {
        private readonly List<string> _requests = new List<string>();
        private ArtLibrary _library;

        [SetUp]
        public void SetUp()
        {
            _requests.Clear();
            var variants = new[] { "house-a", "house-b", "house-c" };
            var references = variants.Select(id => new ArtLibrary.Entry { id = id, baseAgeIndex = 3 }).ToArray();
            var bindings = new[] {
                new ArtLibrary.Binding { buildingType = "House", race = "human", age = "bronze", modelIds = variants },
                new ArtLibrary.Binding { buildingType = "House", race = "human", age = "industrial", modelIds = variants },
                new ArtLibrary.Binding { buildingType = "House", race = "elf", age = "industrial", modelIds = new[] { "missing-id" } }
            };
            _library = new ArtLibrary(new ArtLibrary.Index { schema = "godsim.buildings/2", assets = references, bindings = bindings },
                new Material(), path => {
                    _requests.Add(path);
                    return new BuildingArtModel { Lod0 = new Mesh(), Lod1 = new Mesh(), Lod2 = path.EndsWith("house-c") ? null : new Mesh() };
                });
        }

        [TestCase(0, "house-a")]
        [TestCase(4, "house-b")]
        [TestCase(int.MinValue, "house-b")]
        public void SelectsExactBindingAndCachesAcrossAges(int variant, string id)
        {
            Assert.That(_requests, Is.Empty);
            Assert.That(_library.TryGet(BuildingType.House, RaceId.Human, AgeId.Bronze, variant, out var earlier, out var earlyOffset));
            Assert.That(_library.TryGet(BuildingType.House, RaceId.Human, AgeId.Industrial, variant, out var later, out var lateOffset));
            Assert.That(later, Is.SameAs(earlier));
            Assert.That(earlyOffset, Is.EqualTo(2 * 96f / 1024));
            Assert.That(lateOffset, Is.EqualTo(-3 * 96f / 1024));
            Assert.That(_requests, Is.EqualTo(new[] { "GodSimBuildingModels/" + id }));
        }

        [TestCase(RaceId.Human, AgeId.Industrial, -1, 1)] // selected variant lacks its LOD2
        [TestCase(RaceId.Elf, AgeId.Industrial, 0, 0)] // binding points to absent id
        [TestCase(RaceId.Human, AgeId.Modern, 0, 0)] // neighbouring age cannot supply a missing binding
        [TestCase(RaceId.Orc, AgeId.Industrial, 0, 0)]
        public void MissingArtLeavesProceduralFallback(RaceId race, AgeId age, int variant, int loads)
        {
            Assert.That(_library.TryGet(BuildingType.House, race, age, variant, out var model, out _), Is.False);
            Assert.That(model, Is.Null);
            Assert.That(_requests.Count, Is.EqualTo(loads));
        }

        [TestCase(0, true, 100f, 0)]
        [TestCase(0, true, 122f, 1)]
        [TestCase(1, true, 100f, 1)]
        [TestCase(1, true, 80f, 0)]
        [TestCase(0, true, 485f, 2)]
        [TestCase(2, true, 325f, 2)]
        [TestCase(2, true, 323f, 1)]
        [TestCase(0, false, 999f, 1)]
        public void LodUsesHysteresisAtBothBoundaries(int current, bool art, float squared, int expected)
        {
            Assert.That(BuildingRenderer.SelectLod(current, art, squared, 81, 121), Is.EqualTo(expected));
        }

        [Test]
        public void MixedTownPacksFourBodyMeshesAndReleasesPages()
        {
            BuildingRenderer.CheckTownBatching();
        }

        [Test]
        public void HeadlessHierarchyRoundTripsPointsAndDirectionsAcrossScaledParents()
        {
            var parent = new GameObject("parent").transform;
            parent.SetPositionAndRotation(new Vector3(3, 0, 5), Quaternion.Euler(0, 90, 0));
            parent.localScale = new Vector3(2, 3, 4);
            var child = new GameObject("child").transform;
            child.SetParent(parent, false);
            child.localPosition = new Vector3(1, 2, -1);
            child.localRotation = Quaternion.Euler(0, 90, 0);
            child.localScale = new Vector3(3, .5f, .25f);
            var local = new Vector3(1, 2, 4);
            var worldPoint = new Vector3(-13, 9, 1);
            var worldVector = new Vector3(-12, 3, -2);
            Assert.That((child.localToWorldMatrix.MultiplyPoint3x4(local) - worldPoint).sqrMagnitude, Is.LessThan(1e-10f));
            Assert.That((child.localToWorldMatrix.MultiplyVector(local) - worldVector).sqrMagnitude, Is.LessThan(1e-10f));
            Assert.That((child.worldToLocalMatrix.MultiplyPoint3x4(worldPoint) - local).sqrMagnitude, Is.LessThan(1e-10f));
            child.SetParent(null, true);
            Assert.That((child.localToWorldMatrix.MultiplyPoint3x4(local) - worldPoint).sqrMagnitude, Is.LessThan(1e-10f));
        }

        [Test]
        public void PlacedTownCenterHandlesConstructionTierFireRuinAndReuse()
        {
            BuildingRenderer.CheckViewLifecycle();
        }
    }

    // Test-only access to private view/batch state. This fixture counts generated single-submesh bodies.
    public sealed partial class BuildingRenderer
    {
        internal static void CheckViewLifecycle()
        {
            var renderer = new GameObject("test").AddComponent<BuildingRenderer>();
            renderer.Awake();
            var state = new WorldState(1, new WorldMap(16, 16));
            var database = new GameDatabase();
            database.Register(new BuildingDefinition { Type = BuildingType.TownCenter, Category = BuildingCategory.Civic,
                TintColor = 0x2E2B28FFu });
            database.Register(new BuildingDefinition { Type = BuildingType.House, Category = BuildingCategory.Housing });
            MaterialLibrary.VertexColorOpaque = new Material();
            MaterialLibrary.VertexColorInstanced = new Material();
            renderer.OnSimulationCreated(new Core.Composition.GodSimulation { State = state, Db = database, Events = new EventBus { StrictMode = true } });
            var model = new BuildingArtModel { Lod0 = new Mesh { bounds = new Bounds { max = new Vector3(1, 2, 1) } },
                Lod1 = new Mesh(), Lod2 = new Mesh() };
            var index = new ArtLibrary.Index {
                schema = "godsim.buildings/2", assets = new[] { new ArtLibrary.Entry { id = "tc", baseAgeIndex = 0 } },
                bindings = new[] { new ArtLibrary.Binding { buildingType = "TownCenter", race = "elf", age = "iron", modelIds = new[] { "tc" } } }
            };
            renderer._art = new ArtLibrary(index, new Material(), _ => model);
            var center = new Building { Id = 1, Type = BuildingType.TownCenter, SettlementId = 2, StyleRace = RaceId.Elf,
                StyleAge = AgeId.Iron, Rotation = 1, Width = 2, Height = 3, ConstructionProgress = .5f };
            state.Buildings.Add(center);
            state.Settlements.Add(new Settlement { Id = 2, TownCenterId = 1, Tier = SettlementTier.Town });
            renderer._bus.Publish(new BuildingPlacedEvent { BuildingId = 1 });
            var v = renderer._views[1];
            Assert.That(v.BodyMf.sharedMesh, Is.SameAs(model.Lod0));
            Assert.That(v.BaseTint, Is.EqualTo(Color.white));
            Assert.That(v.DefinitionTint.r, Is.EqualTo(46f / 255f).Within(.00001));
            Assert.That(v.BodyT.localScale.x, Is.EqualTo(T).Within(.00001));
            Assert.That(v.BodyT.localScale.z, Is.EqualTo(T).Within(.00001));
            Assert.That(v.Collider.size.x, Is.EqualTo(3f * T * .95f).Within(.00001));
            Assert.That(v.MeshW, Is.EqualTo(3));
            Assert.That(v.MeshH, Is.EqualTo(2));
            Assert.That(v.Collider.size.y, Is.EqualTo(3.8f).Within(.00001));
            Assert.That(v.BodyT.localScale.y, Is.EqualTo(.95f).Within(.00001));
            Assert.That(v.BannerGo.activeSelf && v.ScaffoldGo.activeSelf && v.BodyMr.enabled);
            center.IsComplete = true;
            renderer._bus.Publish(new BuildingCompletedEvent { BuildingId = 1 });
            Assert.That(v.Batch, Is.Not.Null);
            Assert.That(v.BodyMr.enabled || v.ScaffoldGo.activeSelf, Is.False);
            renderer.SetLod(v, 2);
            Assert.That(v.BodyMf.sharedMesh, Is.SameAs(model.Lod2));
            Assert.That(v.BannerGo.activeSelf, Is.False);
            renderer.SetFire(v, true);
            renderer.UpdateFire();
            Assert.That(v.Batch, Is.Null);
            Assert.That(v.BodyMr.enabled);
            renderer.SetFire(v, false);
            renderer._bus.Publish(new BuildingDestroyedEvent { BuildingId = 1, Cause = DestroyCause.Fire });
            Assert.That(v.IsRuin && v.BodyMr.enabled);
            Assert.That(v.Collider.enabled || v.HasArt, Is.False);
            Assert.That(v.BodyMr.sharedMaterial, Is.SameAs(MaterialLibrary.VertexColorOpaque));
            renderer.ReleaseAll();
            state.Buildings.Add(new Building { Id = 3, Type = BuildingType.House, IsComplete = true });
            renderer._bus.Publish(new BuildingPlacedEvent { BuildingId = 3 });
            Assert.That(renderer._views[3], Is.SameAs(v));
            Assert.That(v.HasArt || v.IsRuin || v.OnFire || v.BannerGo.activeSelf || v.ScaffoldGo.activeSelf, Is.False);
            Assert.That(v.Collider.enabled && v.BodyMr.enabled);
            Assert.That(v.BodyMr.sharedMaterial, Is.SameAs(MaterialLibrary.VertexColorOpaque));
            Assert.That(renderer.GetView(1), Is.Null);
            Assert.That(renderer.GetView(3), Is.SameAs(v.Root));
        }

        internal static void CheckTownBatching()
        {
            var renderer = new GameObject("test").AddComponent<BuildingRenderer>();
            renderer._art = new ArtLibrary(null, new Material(), null);
            renderer._mpb = new MaterialPropertyBlock();
            var views = new List<View>();
            for (int i = 0; i < 200; i++)
            {
                var source = new Mesh();
                source.SetVertices(new List<Vector3> { Vector3.zero, new Vector3(0, 1, 1), Vector3.right });
                var normal = new Vector3(0, 1, -1).normalized;
                source.SetNormals(new List<Vector3> { normal, normal, normal });
                source.SetUVs(0, new List<Vector2> { new Vector2 { y = .75f }, new Vector2 { y = .75f }, new Vector2 { y = .75f } });
                source.SetTriangles(new List<int> { 0, 1, 2 }, 0);
                var view = new View {
                    Building = new Building { SettlementId = 12 }, BodyT = new GameObject("body").transform,
                    Full = source, AgeRowOffset = -(i % 3) * ArtLibrary.AgeRowHeight
                };
                views.Add(view);
                renderer.SetBatched(view, true);
            }
            var firstBody = views[0].BodyT;
            firstBody.SetPositionAndRotation(new Vector3(5, 3, 9), Quaternion.Euler(0, 90, 0));
            firstBody.localScale = new Vector3(2, 3, 4);
            renderer.FlushBatches();
            Assert.That(renderer._batches.Select(x => x.Views.Count), Is.EqualTo(new[] { 64, 64, 64, 8 }));
            Assert.That(renderer._batches.Sum(x => x.Mesh.vertexCount), Is.EqualTo(600));
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            renderer._batches[0].Mesh.GetVertices(positions);
            renderer._batches[0].Mesh.GetNormals(normals);
            Assert.That((positions[0] - new Vector3(5, 3, 9)).sqrMagnitude, Is.LessThan(1e-10f));
            Assert.That((positions[1] - new Vector3(9, 6, 9)).sqrMagnitude, Is.LessThan(1e-10f));
            Assert.That((positions[2] - new Vector3(5, 3, 7)).sqrMagnitude, Is.LessThan(1e-10f));
            Assert.That((normals[0] - new Vector3(-.6f, .8f, 0)).sqrMagnitude, Is.LessThan(1e-10f));
            var uvs = new List<Vector2>();
            renderer._batches[0].Mesh.GetUVs(0, uvs);
            Assert.That(uvs[3].y, Is.EqualTo(.75f - ArtLibrary.AgeRowHeight));
            var indices = new List<int>();
            renderer._batches[0].Mesh.GetTriangles(indices, 0);
            Assert.That(indices, Is.EqualTo(Enumerable.Range(0, 192)));
            renderer.SetBatched(views[0], false); // an individual renderer now owns this fire/construction body
            renderer.FlushBatches();
            Assert.That(renderer._batches.Sum(x => x.Mesh.vertexCount), Is.EqualTo(597));
            renderer.SetBatched(views[0], true);
            Assert.That(renderer._batches.Count, Is.EqualTo(4));
            foreach (var view in views) renderer.SetBatched(view, false);
            renderer.FlushBatches();
            Assert.That(renderer._batches, Is.Empty);
            Assert.That(renderer._townBatches, Is.Empty);
        }
    }
}
