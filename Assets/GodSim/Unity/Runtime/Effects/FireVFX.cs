using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Unity.Effects;
using GodSim.Unity.Units;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Flames + smoke at tiles flagged <see cref="TileFlags.Burning"/> and at buildings with
    /// <see cref="Building.IsOnFire"/>. The map and building list are scanned incrementally (fixed budget per
    /// frame); at most <see cref="MaxActive"/> (64) sites burn visibly, preferring those nearest the camera
    /// (a farther site is replaced only by a clearly nearer one, to avoid churn). Emitters come from the
    /// shared <see cref="VfxPool"/> and stop as soon as the fire is out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FireVFX : SimulationBoundBehaviour
    {
        public static FireVFX Instance { get; private set; }

        [Range(1, 64)] public int MaxActive = 64;
        [Tooltip("Map tiles checked for the Burning flag per frame.")]
        public int TilesPerFrame = 16384;
        [Tooltip("Buildings checked for IsOnFire per frame.")]
        public int BuildingsPerFrame = 512;
        [Tooltip("Fires farther than this from the camera (world units) are not shown.")]
        public float MaxDistance = 180f;
        public float TileFireSize = 0.75f;
        public bool EmitSmoke = true;

        private struct Site
        {
            public long Key;
            public bool IsBuilding;
            public int Ref;          // tile index or building id
            public Vector3 Pos;
            public float Dist2;
            public int Flame, Smoke;
        }

        private readonly Site[] _sites = new Site[64];
        private int _count;
        private readonly Dictionary<long, int> _index = new Dictionary<long, int>();
        private int _tileCursor;
        private int _buildingCursor;
        private MapFrame _frame;
        private Vector3 _camPos;

        /// <summary>Fire sites currently shown.</summary>
        public int ActiveCount => _count;

        protected override void OnEnable()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("Multiple FireVFX instances; the last enabled one is FireVFX.Instance.", this);
            Instance = this;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopAllSites();
            if (Instance == this) Instance = null;
        }

        protected override void OnSimulationBound(GodSimulation sim)
        {
            _frame = MapFrame.From(sim.State.Map);
            StopAllSites();
            _tileCursor = 0;
            _buildingCursor = 0;
        }

        protected override void OnSimulationUnbound(GodSimulation old)
        {
            StopAllSites();
        }

        private void StopAllSites()
        {
            VfxPool pool = VfxPool.Instance;
            for (int i = 0; i < _count; i++)
            {
                if (pool != null) { pool.Stop(_sites[i].Flame); pool.Stop(_sites[i].Smoke); }
            }
            _count = 0;
            _index.Clear();
        }

        private static long TileKey(int index) => index;
        private static long BuildingKey(int id) => (1L << 40) | (uint)id;

        private void LateUpdate()
        {
            if (!EnsureBound()) return;
            WorldState state = Sim.State;
            WorldMap map = state.Map;
            if (!ReferenceEquals(map, _frame.Map)) { _frame = MapFrame.From(map); StopAllSites(); _tileCursor = 0; }
            Camera cam = Camera.main;
            if (cam == null) return;
            VfxPool pool = VfxPool.GetOrCreate();
            if (pool == null) return;
            _camPos = cam.transform.position;

            ValidateSites(state, pool);
            ScanTiles(map, pool);
            ScanBuildings(state, pool);
        }

        private void ValidateSites(WorldState state, VfxPool pool)
        {
            WorldMap map = state.Map;
            for (int i = _count - 1; i >= 0; i--)
            {
                bool alive;
                if (_sites[i].IsBuilding)
                {
                    Building b = state.Buildings.Get(_sites[i].Ref);
                    alive = b != null && !b.IsDestroyed && b.IsOnFire;
                }
                else
                {
                    int idx = _sites[i].Ref;
                    alive = idx >= 0 && idx < map.TileCount && (map.Flags[idx] & TileFlags.Burning) != 0;
                }
                if (!alive) { RemoveAt(i, pool); continue; }
                Vector3 d = _sites[i].Pos - _camPos;
                _sites[i].Dist2 = d.sqrMagnitude;
                // Re-light emitters that the pool could not provide earlier.
                if (_sites[i].Flame < 0 || !pool.IsPlaying(_sites[i].Flame)) _sites[i].Flame = pool.Play(VfxKind.Flame, _sites[i].Pos, SizeOf(ref _sites[i], state), 0f);
            }
        }

        private float SizeOf(ref Site s, WorldState state)
        {
            if (!s.IsBuilding) return TileFireSize * _frame.TileSize;
            Building b = state.Buildings.Get(s.Ref);
            int ext = b != null ? Mathf.Max(b.Width, b.Height) : 1;
            return Mathf.Clamp(ext * 0.8f, 0.8f, 3f) * _frame.TileSize;
        }

        private void ScanTiles(WorldMap map, VfxPool pool)
        {
            TileFlags[] flags = map.Flags;
            int n = flags.Length;
            if (n == 0) return;
            int budget = Mathf.Clamp(TilesPerFrame, 256, n);
            if (_tileCursor >= n) _tileCursor = 0;
            for (int k = 0; k < budget; k++)
            {
                int i = _tileCursor;
                if (++_tileCursor >= n) _tileCursor = 0;
                if ((flags[i] & TileFlags.Burning) == 0) continue;
                if (_index.ContainsKey(TileKey(i))) continue;
                int x = i % map.Width, y = i / map.Width;
                Vector3 pos = GodSimCoords.TileToWorld(map, x, y);
                Consider(TileKey(i), false, i, pos, TileFireSize * _frame.TileSize, pool);
            }
        }

        private void ScanBuildings(WorldState state, VfxPool pool)
        {
            IReadOnlyList<Building> items = state.Buildings.Items;
            int n = items.Count;
            if (n == 0) return;
            int budget = Mathf.Min(Mathf.Max(16, BuildingsPerFrame), n);
            if (_buildingCursor >= n) _buildingCursor = 0;
            for (int k = 0; k < budget; k++)
            {
                Building b = items[_buildingCursor];
                if (++_buildingCursor >= n) _buildingCursor = 0;
                if (b == null || b.IsDestroyed || !b.IsOnFire) continue;
                long key = BuildingKey(b.Id);
                if (_index.ContainsKey(key)) continue;
                float cx = b.Origin.X + (b.Width - 1) * 0.5f, cy = b.Origin.Y + (b.Height - 1) * 0.5f;
                Vector3 pos = GodSimCoords.TileToWorld(state.Map, cx, cy);
                pos.y += 0.35f * _frame.TileSize;
                float size = Mathf.Clamp(Mathf.Max(b.Width, b.Height) * 0.8f, 0.8f, 3f) * _frame.TileSize;
                Consider(key, true, b.Id, pos, size, pool);
            }
        }

        private void Consider(long key, bool isBuilding, int reference, Vector3 pos, float size, VfxPool pool)
        {
            float d2 = (pos - _camPos).sqrMagnitude;
            if (d2 > MaxDistance * MaxDistance) return;
            int cap = Mathf.Clamp(MaxActive, 1, _sites.Length);
            if (_count >= cap)
            {
                int far = -1;
                float farD2 = -1f;
                for (int i = 0; i < _count; i++) if (_sites[i].Dist2 > farD2) { farD2 = _sites[i].Dist2; far = i; }
                if (far < 0 || d2 >= farD2 * 0.7f) return; // hysteresis: only clearly nearer fires replace
                RemoveAt(far, pool);
            }
            int flame = pool.Play(VfxKind.Flame, pos, size, 0f);
            if (flame < 0) return;
            int smoke = EmitSmoke ? pool.Play(VfxKind.Smoke, pos + new Vector3(0f, 0.4f * size, 0f), size, 0f) : -1;
            pool.Burst(VfxKind.Embers, pos, size);
            _sites[_count] = new Site { Key = key, IsBuilding = isBuilding, Ref = reference, Pos = pos, Dist2 = d2, Flame = flame, Smoke = smoke };
            _index[key] = _count;
            _count++;
        }

        private void RemoveAt(int i, VfxPool pool)
        {
            pool.Stop(_sites[i].Flame);
            pool.Stop(_sites[i].Smoke);
            _index.Remove(_sites[i].Key);
            int last = _count - 1;
            if (i != last)
            {
                _sites[i] = _sites[last];
                _index[_sites[i].Key] = i;
            }
            _sites[last] = default;
            _count--;
        }
    }
}
