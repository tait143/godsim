using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// One ChunkSize x ChunkSize block of terrain: a ground GameObject (MeshFilter + MeshRenderer + MeshCollider on the
    /// terrain layer), an optional child water GameObject, and cached world-space tree matrices per <see cref="TreeKind"/>.
    /// Internal to <see cref="TerrainRenderer"/>.
    /// </summary>
    internal sealed class TerrainChunk
    {
        public readonly int MinX, MinY, MaxX, MaxY; // tile rect, max exclusive

        public GameObject Go;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public MeshCollider Collider;
        public Mesh Mesh;

        public GameObject WaterGo;
        public MeshFilter WaterFilter;
        public MeshRenderer WaterRenderer;
        public Mesh WaterMesh;

        /// <summary>World bounds of ground + water (used for rebuild prioritization).</summary>
        public Bounds Bounds;
        /// <summary>World bounds of the tree instances (used for frustum culling).</summary>
        public Bounds TreeBounds;
        public readonly Matrix4x4[][] TreeMatrices = new Matrix4x4[TreeMeshFactory.KindCount][];
        public readonly int[] TreeCounts = new int[TreeMeshFactory.KindCount];
        public int TreeTotal;

        public bool Dirty;
        public ulong Signature;

        private Vector3 _position;

        public TerrainChunk(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        public void Create(Transform parent, Vector3 position, int layer, string name)
        {
            _position = position;
            Go = new GameObject(name);
            Go.layer = layer;
            Go.transform.SetParent(parent, false);
            Go.transform.localPosition = position;
            Filter = Go.AddComponent<MeshFilter>();
            Renderer = Go.AddComponent<MeshRenderer>();
            Collider = Go.AddComponent<MeshCollider>();
            Mesh = new Mesh();
            Mesh.name = name;
            Mesh.MarkDynamic();
            Filter.sharedMesh = Mesh;
            Renderer.sharedMaterial = MaterialLibrary.VertexColorOpaque;
            Renderer.shadowCastingMode = ShadowCastingMode.On;
            Renderer.receiveShadows = true;
        }

        /// <summary>Uploads the builder output (ground, water, trees). Allocates only when the tree arrays must grow.</summary>
        public void Apply(TerrainMeshData data, int layer)
        {
            Upload(Mesh, data.Vertices, data.Normals, data.Colors, data.Triangles);
            // Re-assign so the MeshCollider re-cooks the modified mesh.
            Collider.sharedMesh = null;
            if (data.Triangles.Count > 0) Collider.sharedMesh = Mesh;

            Bounds b = Mesh.bounds;
            b.center = b.center + _position;

            if (data.HasWater)
            {
                if (WaterGo == null) CreateWater(layer);
                if (!WaterGo.activeSelf) WaterGo.SetActive(true);
                Upload(WaterMesh, data.WaterVertices, data.WaterNormals, data.WaterColors, data.WaterTriangles);
                Bounds wb = WaterMesh.bounds;
                wb.center = wb.center + _position;
                b.Encapsulate(wb);
            }
            else if (WaterGo != null && WaterGo.activeSelf)
            {
                WaterGo.SetActive(false);
            }
            Bounds = b;

            TreeTotal = 0;
            for (int k = 0; k < TreeMatrices.Length; k++)
            {
                List<Matrix4x4> src = data.Trees[k];
                int count = src.Count;
                if (count > 0 && (TreeMatrices[k] == null || TreeMatrices[k].Length < count))
                    TreeMatrices[k] = new Matrix4x4[count + (count >> 2) + 8]; // headroom for regrowth
                if (count > 0) src.CopyTo(TreeMatrices[k]);
                TreeCounts[k] = count;
                TreeTotal += count;
            }
            TreeBounds = data.TreeBounds;
        }

        private void CreateWater(int layer)
        {
            WaterGo = new GameObject("Water");
            WaterGo.layer = layer;
            WaterGo.transform.SetParent(Go.transform, false);
            WaterFilter = WaterGo.AddComponent<MeshFilter>();
            WaterRenderer = WaterGo.AddComponent<MeshRenderer>();
            WaterMesh = new Mesh();
            WaterMesh.name = "Water";
            WaterFilter.sharedMesh = WaterMesh;
            WaterRenderer.sharedMaterial = MaterialLibrary.Water;
            WaterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            WaterRenderer.receiveShadows = true;
        }

        private static void Upload(Mesh mesh, List<Vector3> v, List<Vector3> n, List<Color32> c, List<int> t)
        {
            mesh.Clear();
            mesh.indexFormat = v.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0, true);
        }

        /// <summary>Destroys the meshes (not owned by the GameObjects) and the chunk GameObject.</summary>
        public void Destroy()
        {
            if (Mesh != null) Object.Destroy(Mesh);
            if (WaterMesh != null) Object.Destroy(WaterMesh);
            if (Go != null) Object.Destroy(Go);
            Mesh = null; WaterMesh = null; Go = null; WaterGo = null;
            Filter = null; Renderer = null; Collider = null; WaterFilter = null; WaterRenderer = null;
            for (int k = 0; k < TreeMatrices.Length; k++) { TreeMatrices[k] = null; TreeCounts[k] = 0; }
            TreeTotal = 0;
        }
    }
}
