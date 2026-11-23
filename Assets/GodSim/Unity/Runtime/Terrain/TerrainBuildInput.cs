using System.Collections.Generic;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>The three procedural tree archetypes drawn on <see cref="TileFlags.Trees"/> tiles.</summary>
    public enum TreeKind : byte { Conifer = 0, Deciduous = 1, Palm = 2 }

    /// <summary>
    /// Everything <see cref="TerrainMeshBuilder"/> reads besides the <see cref="WorldMap"/> itself.
    /// Owned and refreshed by <see cref="TerrainRenderer"/>; plain data so the builder stays a pure function
    /// (no Unity scene access, no GodSim.Unity statics) and can be unit-tested.
    /// </summary>
    public sealed class TerrainBuildInput
    {
        /// <summary>Map being rendered.</summary>
        public WorldMap Map;
        /// <summary>World-space surface height per tile (index = y * Width + x), normally from GodSimCoords.SurfaceHeight.</summary>
        public float[] Heights;
        /// <summary>Tile edge length in world units.</summary>
        public float TileSize = 1f;
        /// <summary>World position of the CENTER of tile (0,0), with y = 0.</summary>
        public Vector3 Origin;
        /// <summary>World height of the water surface.</summary>
        public float WaterHeight;
        /// <summary>Bottom of the skirts that close the map border.</summary>
        public float EdgeBottom = -2f;
        /// <summary>Base color per <see cref="Biome"/> (indexed by the enum value).</summary>
        public readonly Color32[] BiomeColors = new Color32[Counts.Biomes];
        /// <summary>Blend civilization colors over owned tiles.</summary>
        public bool ShowTerritory = true;
        /// <summary>Owner settlement id -> civilization color. Settlements missing here draw no territory.</summary>
        public readonly Dictionary<int, Color32> SettlementColors = new Dictionary<int, Color32>(64);
        /// <summary>World seed; drives the deterministic per-tile color/tree variation.</summary>
        public ulong Seed;
        /// <summary>Tile Resource value at which trees reach full size.</summary>
        public float TreeFullResource = 160f;
        /// <summary>Upper bound of tree instances per wooded tile (0 disables trees).</summary>
        public int MaxTreesPerTile = 2;

        /// <summary>True when the input can be used to build meshes.</summary>
        public bool IsValid => Map != null && Heights != null && Heights.Length >= Map.TileCount;
    }

    /// <summary>
    /// Reusable output buffers of <see cref="TerrainMeshBuilder"/>. One instance is reused for every chunk
    /// (Unity's List-based Mesh setters copy the data), so rebuilding allocates nothing after warm-up.
    /// Ground/water vertices are local to the chunk: (0,0,0) is the center of the chunk's first tile at y = 0.
    /// Tree matrices are in world space.
    /// </summary>
    public sealed class TerrainMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>(8192);
        public readonly List<Vector3> Normals = new List<Vector3>(8192);
        public readonly List<Color32> Colors = new List<Color32>(8192);
        public readonly List<int> Triangles = new List<int>(12288);

        public readonly List<Vector3> WaterVertices = new List<Vector3>(1200);
        public readonly List<Vector3> WaterNormals = new List<Vector3>(1200);
        public readonly List<Color32> WaterColors = new List<Color32>(1200);
        public readonly List<int> WaterTriangles = new List<int>(6144);

        /// <summary>World-space tree matrices, indexed by (int)<see cref="TreeKind"/>.</summary>
        public readonly List<Matrix4x4>[] Trees =
        {
            new List<Matrix4x4>(256), new List<Matrix4x4>(256), new List<Matrix4x4>(256)
        };

        /// <summary>World-space bounds enclosing every tree instance (valid when <see cref="TreeCount"/> &gt; 0).</summary>
        public Bounds TreeBounds;

        internal bool[] WaterMask = new bool[32 * 32];

        public bool HasWater => WaterTriangles.Count > 0;

        public int TreeCount => Trees[0].Count + Trees[1].Count + Trees[2].Count;

        public void ClearGround()
        {
            Vertices.Clear(); Normals.Clear(); Colors.Clear(); Triangles.Clear();
            WaterVertices.Clear(); WaterNormals.Clear(); WaterColors.Clear(); WaterTriangles.Clear();
        }

        public void ClearTrees()
        {
            for (int i = 0; i < Trees.Length; i++) Trees[i].Clear();
            TreeBounds = default(Bounds);
        }
    }
}
