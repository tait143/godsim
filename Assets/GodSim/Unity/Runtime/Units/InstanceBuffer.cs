using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity.Units
{
    /// <summary>
    /// Reusable GPU-instancing buffer: matrices + per-instance colors split into chunks of at most
    /// <see cref="MaxPerBatch"/> (1023, Unity's DrawMeshInstanced limit). Each chunk owns its own
    /// Matrix4x4[] / Vector4[] / MaterialPropertyBlock, so nothing is allocated per frame (chunks are created
    /// only when the instance count grows) and no buffer is reused within one frame's draw calls.
    /// Writes the color to both "_Color" (GodSim/VertexColorLit, Standard) and "_BaseColor" (URP).
    /// </summary>
    public sealed class InstanceBuffer
    {
        public const int MaxPerBatch = 1023;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private sealed class Chunk
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[MaxPerBatch];
            public readonly Vector4[] Colors = new Vector4[MaxPerBatch];
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public int Count;
        }

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private int _active;
        private int _total;
        private int _capacity;

        public InstanceBuffer(int capacity) { Capacity = capacity; }

        /// <summary>Hard cap on instances per frame (extra Add calls return false).</summary>
        public int Capacity
        {
            get => _capacity;
            set => _capacity = value < 0 ? 0 : value;
        }

        public int Count => _total;

        public void Clear()
        {
            for (int i = 0; i < _chunks.Count && i <= _active; i++) _chunks[i].Count = 0;
            _active = 0;
            _total = 0;
        }

        public bool Add(in Matrix4x4 m, Color c)
        {
            if (_total >= _capacity) return false;
            if (_chunks.Count == 0) _chunks.Add(new Chunk());
            Chunk ch = _chunks[_active];
            if (ch.Count >= MaxPerBatch)
            {
                _active++;
                if (_active >= _chunks.Count) _chunks.Add(new Chunk());
                ch = _chunks[_active];
                ch.Count = 0;
            }
            ch.Matrices[ch.Count] = m;
            ch.Colors[ch.Count] = new Vector4(c.r, c.g, c.b, c.a);
            ch.Count++;
            _total++;
            return true;
        }

        public void Draw(Mesh mesh, Material material, ShadowCastingMode shadows, bool receiveShadows, int layer)
        {
            if (mesh == null || material == null || _total == 0) return;
            for (int i = 0; i < _chunks.Count && i <= _active; i++)
            {
                Chunk ch = _chunks[i];
                if (ch.Count <= 0) continue;
                ch.Block.SetVectorArray(ColorId, ch.Colors);
                ch.Block.SetVectorArray(BaseColorId, ch.Colors);
                Graphics.DrawMeshInstanced(mesh, 0, material, ch.Matrices, ch.Count, ch.Block, shadows, receiveShadows, layer);
            }
        }
    }
}
