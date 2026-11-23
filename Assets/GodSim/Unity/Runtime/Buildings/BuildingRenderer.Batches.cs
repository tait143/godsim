using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    public sealed partial class BuildingRenderer
    {
        // Geometry within one settlement shares a draw even when its source meshes, ages and colours differ.
        internal const int BodiesPerBatch = 64;
        private sealed class BodyBatch
        {
            public int SettlementId;
            public readonly List<View> Views = new List<View>(BodiesPerBatch);
            public GameObject Root;
            public Mesh Mesh;
            public bool Dirty = true;
        }

        private readonly List<BodyBatch> _batches = new List<BodyBatch>();
        private readonly Dictionary<int, List<BodyBatch>> _townBatches = new Dictionary<int, List<BodyBatch>>();
        private readonly List<Vector3> _batchPositions = new List<Vector3>(), _batchNormals = new List<Vector3>();
        private readonly List<Vector2> _batchUvs = new List<Vector2>();
        private readonly List<Color32> _batchColors = new List<Color32>();
        private readonly List<int> _batchIndices = new List<int>();
        private readonly List<Vector3> _positions = new List<Vector3>(), _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _indices = new List<int>();

        private void SetBatched(View v, bool batch)
        {
            if (v.Batch != null)
            {
                v.Batch.Dirty = true;
                if (batch && v.Batch.SettlementId == v.Building.SettlementId) return;
                v.Batch.Views.Remove(v);
                v.Batch = null;
            }
            if (!batch) return;
            int town = v.Building.SettlementId;
            if (!_townBatches.TryGetValue(town, out var pages))
                _townBatches.Add(town, pages = new List<BodyBatch>());
            foreach (var page in pages)
            {
                if (page.Views.Count >= BodiesPerBatch) continue;
                v.Batch = page;
                break;
            }
            if (v.Batch == null)
            {
                var go = new GameObject("Building bodies");
                go.transform.SetParent(transform, false);
                if (_layer >= 0) go.layer = _layer;
                var mesh = new Mesh { name = "Building bodies" };
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _art.Material;
                renderer.shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                _mpb.Clear();
                _mpb.SetFloat(VertexTintId, 1f);
                renderer.SetPropertyBlock(_mpb);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                v.Batch = new BodyBatch { SettlementId = town, Root = go, Mesh = mesh };
                _batches.Add(v.Batch);
                pages.Add(v.Batch);
            }
            v.Batch.Views.Add(v);
            v.Batch.Dirty = true;
        }

        private void FlushBatches()
        {
            for (int i = _batches.Count - 1; i >= 0; i--)
            {
                var page = _batches[i];
                if (!page.Dirty) continue;
                if (page.Views.Count == 0)
                {
                    var pages = _townBatches[page.SettlementId];
                    pages.Remove(page);
                    if (pages.Count == 0) _townBatches.Remove(page.SettlementId);
                    _batches.RemoveAt(i);
                    Destroy(page.Mesh);
                    Destroy(page.Root);
                    continue;
                }
                _batchPositions.Clear();
                _batchNormals.Clear();
                _batchUvs.Clear();
                _batchColors.Clear();
                _batchIndices.Clear();
                var worldToPage = page.Root.transform.worldToLocalMatrix;
                foreach (var view in page.Views)
                    AppendBody(view, worldToPage * view.BodyT.localToWorldMatrix);
                var mesh = page.Mesh;
                mesh.Clear();
                mesh.indexFormat = _batchPositions.Count <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
                mesh.SetVertices(_batchPositions);
                mesh.SetNormals(_batchNormals);
                mesh.SetUVs(0, _batchUvs);
                mesh.SetColors(_batchColors);
                mesh.SetTriangles(_batchIndices, 0);
                page.Dirty = false;
            }
        }

        private void AppendBody(View v, Matrix4x4 localToPage)
        {
            var source = BodyMesh(v);
            source.GetVertices(_positions);
            source.GetNormals(_normals);
            source.GetUVs(0, _uvs);
            source.GetColors(_colors);
            source.GetTriangles(_indices, 0);
            var normalMatrix = localToPage.inverse.transpose;
            int first = _batchPositions.Count;
            // SetPropertyBlock.SetColor applies this same conversion to the individual renderer's tint.
            Color tint = QualitySettings.activeColorSpace == ColorSpace.Linear ? v.BaseTint.linear : v.BaseTint;
            for (int i = 0; i < _positions.Count; i++)
            {
                _batchPositions.Add(localToPage.MultiplyPoint3x4(_positions[i]));
                _batchNormals.Add(normalMatrix.MultiplyVector(_normals[i]).normalized);
                Vector2 uv = _uvs[i];
                uv.y += v.AgeRowOffset;
                _batchUvs.Add(uv);
                _batchColors.Add(_colors.Count == 0 ? tint : _colors[i] * tint);
            }
            for (int i = 0; i < _indices.Count; i++) _batchIndices.Add(first + _indices[i]);
        }

        private void ClearBatches()
        {
            foreach (var page in _batches)
            {
                Destroy(page.Mesh);
                Destroy(page.Root);
            }
            _batches.Clear();
            _townBatches.Clear();
        }
    }
}
