using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Renders the world map as chunked, vertex-colored low-poly terrain (with MeshColliders on <see cref="TerrainLayer"/>
    /// for raycasts), per-chunk water, GPU-instanced procedural trees and a minimap texture.
    /// <para>Lifecycle: hooks <see cref="GodSimRunner.SimulationCreated"/> (and builds at once if a simulation already
    /// exists). A new world destroys every old chunk and moves the EventBus subscriptions to the new bus.</para>
    /// <para>Updates: TerrainChanged / TerritoryChanged / Building placed+destroyed / Settlement / Disaster events mark
    /// chunks dirty; every <see cref="RefreshEveryTicks"/> ticks a light signature scan (a few chunks per frame) catches
    /// anything that changed without an event (roads, fields, fires, territory). At most
    /// <see cref="MaxRebuildsPerFrame"/> dirty chunks are rebuilt per frame, visible ones first.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainRenderer : MonoBehaviour
    {
        /// <summary>Instanced draw batch limit (matrices per call).</summary>
        public const int MaxInstancesPerBatch = 1023;
        public const int MinChunkSize = 8;
        public const int MaxChunkSize = 128;

        /// <summary>The active terrain renderer (null when none).</summary>
        public static TerrainRenderer Instance { get; private set; }

        [Header("Chunks")]
        [Tooltip("Tiles per chunk side (clamped to 8..128). Changing it at runtime rebuilds the terrain.")]
        public int ChunkSize = 32;
        [Tooltip("Maximum dirty chunks rebuilt per frame.")]
        public int MaxRebuildsPerFrame = 2;
        [Tooltip("Simulation ticks between light refresh scans (roads, fields, fires, territory).")]
        public int RefreshEveryTicks = 24;
        [Tooltip("Chunks whose content signature is checked per frame during a refresh scan.")]
        public int ScanChunksPerFrame = 12;

        [Header("Overlays")]
        [Tooltip("Blend civilization colors over owned land (also requires GodSimSettings.ShowTerritory).")]
        public bool ShowTerritory = true;
        public bool ShowTrees = true;
        [Tooltip("Layer of the chunk GameObjects / colliders (first set bit is used; Nothing = Default).")]
        public LayerMask TerrainLayer;

        [Header("Water")]
        [Tooltip("World height of the water surface.")]
        public float WaterHeight = 0f;

        [Header("Trees")]
        [Tooltip("Trees beyond this camera distance are not drawn (0 = unlimited).")]
        public float TreeDrawDistance = 350f;
        [Tooltip("Tile Resource value at which trees reach full size.")]
        public float TreeFullResource = 160f;
        [Range(0, 3)] public int MaxTreesPerTile = 2;
        public bool TreesCastShadows = true;

        [Header("Minimap")]
        [Tooltip("Seconds between minimap refreshes (only when something changed).")]
        public float MinimapRefreshSeconds = 2f;
        [Tooltip("Frames a minimap refresh is spread over.")]
        public int MinimapRefreshFrames = 12;

        /// <summary>Whole-map minimap texture for the HUD (point filtered; null before the first world). The object is stable across refreshes.</summary>
        public Texture2D Minimap => _minimap != null ? _minimap.Texture : null;

        // --- world
        private GodSimRunner _runner;
        private GodSimulation _sim;
        private WorldState _state;
        private WorldMap _map;
        private EventBus _bus;

        // --- build state
        private readonly TerrainBuildInput _input = new TerrainBuildInput();
        private readonly TerrainMeshData _data = new TerrainMeshData();
        private MinimapTexture _minimap;
        private GameObject _root;
        private TerrainChunk[] _chunks;
        private int _chunksX, _chunksY, _builtChunkSize;
        private int _dirtyCount, _rebuildCursor;
        private int _layer;
        private float _builtHeightScale;
        private float _builtWaterHeight, _builtTreeFull;
        private int _builtTreesPerTile;

        // --- refresh scheduling
        private int _ticksSinceRefresh;
        private bool _scanActive;
        private int _scanCursor;
        private bool _coloursDirty;
        private bool _minimapDirty;
        private float _minimapTimer;

        // --- drawing
        private readonly Plane[] _planes = new Plane[6];
        private bool _planesValid;
        private Vector3 _cameraPos;
        private Mesh[] _treeMeshes;
        private Material _treeMaterial;
        private Material _treeMaterialSource;

        // --- cached delegates (no allocation on (un)subscribe; identity-stable for Unsubscribe)
        private Action<GodSimulation> _onSimulationCreated;
        private Action _onTicked;
        private Action<TerrainChangedEvent> _onTerrainChanged;
        private Action<TerritoryChangedEvent> _onTerritoryChanged;
        private Action<BuildingPlacedEvent> _onBuildingPlaced;
        private Action<BuildingDestroyedEvent> _onBuildingDestroyed;
        private Action<SettlementFoundedEvent> _onSettlementFounded;
        private Action<SettlementCapturedEvent> _onSettlementCaptured;
        private Action<SettlementDestroyedEvent> _onSettlementDestroyed;
        private Action<DisasterEvent> _onDisaster;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        // =================================================================================================
        // Unity lifecycle
        // =================================================================================================

        private void Awake()
        {
            _onSimulationCreated = OnSimulationCreated;
            _onTicked = OnTicked;
            _onTerrainChanged = OnTerrainChanged;
            _onTerritoryChanged = OnTerritoryChanged;
            _onBuildingPlaced = OnBuildingPlaced;
            _onBuildingDestroyed = OnBuildingDestroyed;
            _onSettlementFounded = OnSettlementFounded;
            _onSettlementCaptured = OnSettlementCaptured;
            _onSettlementDestroyed = OnSettlementDestroyed;
            _onDisaster = OnDisaster;

            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GodSim] Duplicate TerrainRenderer disabled; only one may be active.");
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
            if (Instance != this) return;
            if (_root != null) _root.SetActive(true);
            HookRunner(GodSimRunner.Instance);
            if (_sim != null && _bus == null)
            {
                // Re-enabled with the same world: events were missed while disabled.
                SubscribeBus(_sim.Events);
                MarkAllDirty();
                _minimapDirty = true;
            }
            SyncSimulation();
        }

        private void OnDisable()
        {
            UnhookRunner();
            UnsubscribeBus();
            if (_root != null) _root.SetActive(false);
        }

        private void OnDestroy()
        {
            UnhookRunner();
            UnsubscribeBus();
            DestroyChunks();
            if (_treeMeshes != null)
            {
                for (int i = 0; i < _treeMeshes.Length; i++) if (_treeMeshes[i] != null) Destroy(_treeMeshes[i]);
                _treeMeshes = null;
            }
            if (_treeMaterial != null && _treeMaterial != _treeMaterialSource) Destroy(_treeMaterial);
            _treeMaterial = null;
            if (_minimap != null) { _minimap.Release(); _minimap = null; }
            _sim = null; _state = null; _map = null;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Instance != this) return;
            if (_runner == null) HookRunner(GodSimRunner.Instance); // Unity-null also covers a destroyed runner
            SyncSimulation();
            if (_sim == null) return;

            // The simulation may swap its state (e.g. load) without a SimulationCreated: treat as a new world.
            WorldState st = _sim.State;
            if (st == null) return;
            if (!ReferenceEquals(st, _state) || !ReferenceEquals(st.Map, _map))
            {
                _state = st;
                _map = st.Map;
                RebuildAll();
            }
            if (!ReferenceEquals(_sim.Events, _bus))
            {
                UnsubscribeBus();
                SubscribeBus(_sim.Events);
                MarkAllDirty();
            }
            if (_chunks == null) return;

            ApplySettingChanges();
            if (_chunks == null) return;
            if (_coloursDirty) RefreshSettlementColours();

            if (!_scanActive && _ticksSinceRefresh >= Math.Max(1, RefreshEveryTicks))
            {
                _ticksSinceRefresh = 0;
                _scanActive = true;
                _scanCursor = 0;
                RefreshSettlementColours();
            }
            if (_scanActive) ScanStep();
            if (_dirtyCount > 0) ProcessDirty();
            UpdateMinimap(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (Instance != this || _chunks == null) return;
            Camera cam = Camera.main;
            if (cam != null)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, _planes);
                _cameraPos = cam.transform.position;
                _planesValid = true;
            }
            else _planesValid = false;

            if (ShowTrees) DrawTrees();
        }

        // =================================================================================================
        // Public API
        // =================================================================================================

        /// <summary>Synchronously destroys and rebuilds every chunk (plus the minimap) for the current world.</summary>
        public void RebuildAll()
        {
            DestroyChunks();
            if (_state == null || _map == null) return;

            int cs = MathUtil.Clamp(ChunkSize, MinChunkSize, MaxChunkSize);
            _builtChunkSize = cs;
            _layer = LayerIndex(TerrainLayer);
            PrepareInput();
            EnsureTreeMeshes();

            _root = new GameObject("Terrain");
            _root.layer = _layer;
            if (!isActiveAndEnabled) _root.SetActive(false);

            int w = _map.Width, h = _map.Height;
            _chunksX = (w + cs - 1) / cs;
            _chunksY = (h + cs - 1) / cs;
            _chunks = new TerrainChunk[_chunksX * _chunksY];
            float ts = _input.TileSize;
            for (int cy = 0; cy < _chunksY; cy++)
            {
                for (int cx = 0; cx < _chunksX; cx++)
                {
                    int minX = cx * cs, minY = cy * cs;
                    var chunk = new TerrainChunk(minX, minY, Math.Min(w, minX + cs), Math.Min(h, minY + cs));
                    Vector3 pos = new Vector3(_input.Origin.x + minX * ts, 0f, _input.Origin.z + minY * ts);
                    chunk.Create(_root.transform, pos, _layer, "Chunk " + cx + "," + cy);
                    _chunks[cy * _chunksX + cx] = chunk;
                    BuildChunk(chunk, false);
                }
            }
            _dirtyCount = 0;
            _rebuildCursor = 0;
            _scanActive = false;
            _ticksSinceRefresh = 0;

            if (_minimap == null) _minimap = new MinimapTexture();
            _minimap.Rebuild(_state, _input);
            _minimapDirty = false;
            _minimapTimer = 0f;
        }

        /// <summary>Marks every chunk touching the inclusive tile rect (grown by one tile for skirts/borders) for rebuild.</summary>
        public void MarkDirty(int minX, int minY, int maxX, int maxY)
        {
            if (_chunks == null || _map == null) return;
            if (minX > maxX) { int t = minX; minX = maxX; maxX = t; }
            if (minY > maxY) { int t = minY; minY = maxY; maxY = t; }
            int w = _map.Width, h = _map.Height;
            if (maxX < -1 || maxY < -1 || minX > w || minY > h) return;
            minX = MathUtil.Clamp(minX, 0, w) - 1; maxX = MathUtil.Clamp(maxX, -1, w - 1) + 1;
            minY = MathUtil.Clamp(minY, 0, h) - 1; maxY = MathUtil.Clamp(maxY, -1, h - 1) + 1;
            minX = MathUtil.Clamp(minX, 0, w - 1); maxX = MathUtil.Clamp(maxX, 0, w - 1);
            minY = MathUtil.Clamp(minY, 0, h - 1); maxY = MathUtil.Clamp(maxY, 0, h - 1);

            int cs = _builtChunkSize;
            int cx0 = minX / cs, cx1 = maxX / cs, cy0 = minY / cs, cy1 = maxY / cs;
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                    SetDirty(_chunks[cy * _chunksX + cx]);
            _minimapDirty = true;
        }

        // =================================================================================================
        // Runner / event wiring
        // =================================================================================================

        private void HookRunner(GodSimRunner runner)
        {
            if (ReferenceEquals(runner, _runner) && runner != null) return;
            UnhookRunner();
            if (runner == null) return;
            _runner = runner;
            _runner.SimulationCreated += _onSimulationCreated;
            _runner.Ticked += _onTicked;
        }

        private void UnhookRunner()
        {
            if (!ReferenceEquals(_runner, null))
            {
                _runner.SimulationCreated -= _onSimulationCreated;
                _runner.Ticked -= _onTicked;
            }
            _runner = null;
        }

        /// <summary>Picks up a simulation that exists without us having seen SimulationCreated (late hook / ordering).</summary>
        private void SyncSimulation()
        {
            if (_runner == null) return;
            GodSimulation sim = _runner.Simulation;
            if (sim != null && !ReferenceEquals(sim, _sim)) OnSimulationCreated(sim);
        }

        private void SubscribeBus(EventBus bus)
        {
            if (bus == null || ReferenceEquals(bus, _bus)) return;
            UnsubscribeBus();
            _bus = bus;
            bus.Subscribe(_onTerrainChanged);
            bus.Subscribe(_onTerritoryChanged);
            bus.Subscribe(_onBuildingPlaced);
            bus.Subscribe(_onBuildingDestroyed);
            bus.Subscribe(_onSettlementFounded);
            bus.Subscribe(_onSettlementCaptured);
            bus.Subscribe(_onSettlementDestroyed);
            bus.Subscribe(_onDisaster);
        }

        private void UnsubscribeBus()
        {
            EventBus bus = _bus;
            if (bus == null) return;
            _bus = null;
            bus.Unsubscribe(_onTerrainChanged);
            bus.Unsubscribe(_onTerritoryChanged);
            bus.Unsubscribe(_onBuildingPlaced);
            bus.Unsubscribe(_onBuildingDestroyed);
            bus.Unsubscribe(_onSettlementFounded);
            bus.Unsubscribe(_onSettlementCaptured);
            bus.Unsubscribe(_onSettlementDestroyed);
            bus.Unsubscribe(_onDisaster);
        }

        private void OnSimulationCreated(GodSimulation sim)
        {
            if (sim == null) return;
            UnsubscribeBus();
            DestroyChunks();
            _sim = sim;
            _state = sim.State;
            _map = _state != null ? _state.Map : null;
            if (isActiveAndEnabled) SubscribeBus(sim.Events);
            RebuildAll();
        }

        private void OnTicked()
        {
            _ticksSinceRefresh++;
            _minimapDirty = true;
        }

        private void OnTerrainChanged(TerrainChangedEvent e) => MarkDirty(e.MinX, e.MinY, e.MaxX, e.MaxY);

        private void OnTerritoryChanged(TerritoryChangedEvent e)
        {
            _coloursDirty = true;
            MarkSettlementArea(e.SettlementId, 5);
        }

        private void OnBuildingPlaced(BuildingPlacedEvent e) => MarkBuildingArea(e.BuildingId, e.Origin);

        private void OnBuildingDestroyed(BuildingDestroyedEvent e) => MarkBuildingArea(e.BuildingId, e.Origin);

        private void OnSettlementFounded(SettlementFoundedEvent e)
        {
            _coloursDirty = true;
            MarkDirty(e.Position.X - 12, e.Position.Y - 12, e.Position.X + 12, e.Position.Y + 12);
        }

        private void OnSettlementCaptured(SettlementCapturedEvent e)
        {
            _coloursDirty = true;
            MarkSettlementArea(e.SettlementId, 5);
        }

        private void OnSettlementDestroyed(SettlementDestroyedEvent e)
        {
            _coloursDirty = true;
            MarkDirty(e.Position.X - 24, e.Position.Y - 24, e.Position.X + 24, e.Position.Y + 24);
        }

        private void OnDisaster(DisasterEvent e)
        {
            int r = MathUtil.Clamp(e.Radius, 0, 64) + 1;
            MarkDirty(e.Position.X - r, e.Position.Y - r, e.Position.X + r, e.Position.Y + r);
        }

        private void MarkSettlementArea(int settlementId, int margin)
        {
            if (_state == null) return;
            Settlement s = _state.Settlements.Get(settlementId);
            if (s == null) return;
            int r = MathUtil.Clamp(s.TerritoryRadius, 0, 64) + margin;
            MarkDirty(s.Center.X - r, s.Center.Y - r, s.Center.X + r, s.Center.Y + r);
        }

        private void MarkBuildingArea(int buildingId, Vec2i origin)
        {
            int bw = 3, bh = 3;
            if (_state != null)
            {
                Building b = _state.Buildings.Get(buildingId);
                if (b != null) { bw = MathUtil.Clamp(b.Width, 1, 16); bh = MathUtil.Clamp(b.Height, 1, 16); }
            }
            // margin covers fields/roads laid around the footprint
            MarkDirty(origin.X - 3, origin.Y - 3, origin.X + bw + 2, origin.Y + bh + 2);
        }

        // =================================================================================================
        // Building
        // =================================================================================================

        private void PrepareInput()
        {
            WorldMap map = _map;
            _input.Map = map;
            if (_input.Heights == null || _input.Heights.Length != map.TileCount) _input.Heights = new float[map.TileCount];
            _input.TileSize = GodSimCoords.TileSize > 0f ? GodSimCoords.TileSize : 1f;
            Vector3 o = GodSimCoords.TileToWorld(map, new Vec2i(0, 0));
            o.y = 0f;
            _input.Origin = o;
            _input.Seed = _state.Seed;
            _input.WaterHeight = _builtWaterHeight = WaterHeight;
            _input.ShowTerritory = EffectiveShowTerritory();
            _input.TreeFullResource = _builtTreeFull = TreeFullResource;
            _input.MaxTreesPerTile = _builtTreesPerTile = MaxTreesPerTile;
            for (int b = 0; b < _input.BiomeColors.Length; b++) _input.BiomeColors[b] = GodSimPalette.Biome((Biome)b);
            _builtHeightScale = GodSimCoords.HeightScale;
            RefreshHeights(0, 0, map.Width - 1, map.Height - 1);
            float min = float.MaxValue;
            float[] hs = _input.Heights;
            for (int i = 0; i < hs.Length; i++) if (hs[i] < min) min = hs[i];
            _input.EdgeBottom = (min == float.MaxValue ? 0f : Math.Min(min, _input.WaterHeight)) - _input.TileSize;
            RefreshSettlementColours();
        }

        /// <summary>Caches GodSimCoords.SurfaceHeight for the inclusive rect (NaN/Infinity-safe).</summary>
        private void RefreshHeights(int minX, int minY, int maxX, int maxY)
        {
            WorldMap map = _map;
            minX = Math.Max(0, minX); minY = Math.Max(0, minY);
            maxX = Math.Min(map.Width - 1, maxX); maxY = Math.Min(map.Height - 1, maxY);
            float[] hs = _input.Heights;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    hs[y * map.Width + x] = MathUtil.Sanitize(GodSimCoords.SurfaceHeight(map, x, y));
        }

        private void RefreshSettlementColours()
        {
            _coloursDirty = false;
            var dict = _input.SettlementColors;
            dict.Clear();
            if (_state == null) return;
            var items = _state.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Settlement s = items[i];
                if (s == null || !s.IsAlive) continue;
                Civilization civ = _state.Civilizations.Get(s.CivilizationId);
                dict[s.Id] = civ != null ? GodSimPalette.FromRgba(civ.Color) : new Color(0.8f, 0.8f, 0.8f, 1f);
            }
        }

        private void BuildChunk(TerrainChunk c, bool refreshHeights)
        {
            if (refreshHeights) RefreshHeights(c.MinX - 1, c.MinY - 1, c.MaxX, c.MaxY);
            TerrainMeshBuilder.BuildChunk(_input, c.MinX, c.MinY, c.MaxX, c.MaxY, _data);
            TerrainMeshBuilder.BuildTrees(_input, c.MinX, c.MinY, c.MaxX, c.MaxY, _data);
            c.Apply(_data, _layer);
            c.Signature = TerrainMeshBuilder.ChunkSignature(_input, c.MinX, c.MinY, c.MaxX, c.MaxY);
            if (c.Dirty) { c.Dirty = false; _dirtyCount--; }
        }

        private void SetDirty(TerrainChunk c)
        {
            if (c == null || c.Dirty) return;
            c.Dirty = true;
            _dirtyCount++;
        }

        private void MarkAllDirty()
        {
            if (_chunks == null) return;
            for (int i = 0; i < _chunks.Length; i++) SetDirty(_chunks[i]);
            _minimapDirty = true;
        }

        /// <summary>Rebuilds up to MaxRebuildsPerFrame dirty chunks, preferring the ones in the camera frustum.</summary>
        private void ProcessDirty()
        {
            int budget = Math.Max(1, MaxRebuildsPerFrame);
            int total = _chunks.Length;
            if (_planesValid)
            {
                for (int n = 0; n < total && budget > 0 && _dirtyCount > 0; n++)
                {
                    TerrainChunk c = _chunks[(_rebuildCursor + n) % total];
                    if (c.Dirty && GeometryUtility.TestPlanesAABB(_planes, c.Bounds)) { BuildChunk(c, true); budget--; }
                }
            }
            for (int n = 0; n < total && budget > 0 && _dirtyCount > 0; n++)
            {
                TerrainChunk c = _chunks[(_rebuildCursor + n) % total];
                if (c.Dirty) { BuildChunk(c, true); budget--; }
            }
            _rebuildCursor = (_rebuildCursor + 1) % total;
        }

        /// <summary>Light periodic refresh: re-hashes a few chunks per frame and marks the changed ones dirty.</summary>
        private void ScanStep()
        {
            int n = Math.Max(1, ScanChunksPerFrame);
            while (n-- > 0 && _scanCursor < _chunks.Length)
            {
                TerrainChunk c = _chunks[_scanCursor++];
                if (c.Dirty) continue;
                if (TerrainMeshBuilder.ChunkSignature(_input, c.MinX, c.MinY, c.MaxX, c.MaxY) != c.Signature) SetDirty(c);
            }
            if (_scanCursor >= _chunks.Length) _scanActive = false;
        }

        private void ApplySettingChanges()
        {
            if (MathUtil.Clamp(ChunkSize, MinChunkSize, MaxChunkSize) != _builtChunkSize || LayerIndex(TerrainLayer) != _layer)
            {
                RebuildAll();
                return;
            }
            bool territory = EffectiveShowTerritory();
            if (territory != _input.ShowTerritory)
            {
                _input.ShowTerritory = territory;
                MarkAllDirty();
            }
            float hs = GodSimCoords.HeightScale;
            if (hs != _builtHeightScale && !float.IsNaN(hs))
            {
                _builtHeightScale = hs;
                MarkAllDirty(); // heights are re-read per chunk on rebuild
            }
            if (WaterHeight != _builtWaterHeight || TreeFullResource != _builtTreeFull || MaxTreesPerTile != _builtTreesPerTile)
            {
                _input.WaterHeight = _builtWaterHeight = WaterHeight;
                _input.TreeFullResource = _builtTreeFull = TreeFullResource;
                _input.MaxTreesPerTile = _builtTreesPerTile = MaxTreesPerTile;
                MarkAllDirty();
            }
        }

        private bool EffectiveShowTerritory()
        {
            if (!ShowTerritory) return false;
            GodSimSettings settings = _runner != null ? _runner.Settings : null;
            return settings == null || settings.ShowTerritory;
        }

        private void DestroyChunks()
        {
            if (_chunks != null)
            {
                for (int i = 0; i < _chunks.Length; i++) if (_chunks[i] != null) _chunks[i].Destroy();
                _chunks = null;
            }
            if (_root != null) Destroy(_root);
            _root = null;
            _chunksX = _chunksY = 0;
            _dirtyCount = 0;
            _scanActive = false;
        }

        // =================================================================================================
        // Trees
        // =================================================================================================

        private void EnsureTreeMeshes()
        {
            if (_treeMeshes != null) return;
            _treeMeshes = new Mesh[TreeMeshFactory.KindCount];
            for (int k = 0; k < _treeMeshes.Length; k++) _treeMeshes[k] = TreeMeshFactory.Create((TreeKind)k);
        }

        /// <summary>Instancing-enabled tree material (a private copy if the library material has instancing off).</summary>
        private Material TreeMaterial()
        {
            Material src = MaterialLibrary.VertexColorInstanced;
            if (src == null) return null;
            if (!ReferenceEquals(src, _treeMaterialSource))
            {
                if (_treeMaterial != null && _treeMaterial != _treeMaterialSource) Destroy(_treeMaterial);
                _treeMaterialSource = src;
                if (src.enableInstancing) _treeMaterial = src;
                else
                {
                    _treeMaterial = new Material(src);
                    _treeMaterial.enableInstancing = true;
                }
            }
            return _treeMaterial;
        }

        private void DrawTrees()
        {
            if (_treeMeshes == null) return;
            Material mat = TreeMaterial();
            if (mat == null) return;

            var rp = new RenderParams(mat);
            rp.layer = _layer;
            rp.shadowCastingMode = TreesCastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            rp.receiveShadows = true;
            float maxD2 = TreeDrawDistance > 0f ? TreeDrawDistance * TreeDrawDistance : float.MaxValue;

            for (int i = 0; i < _chunks.Length; i++)
            {
                TerrainChunk c = _chunks[i];
                if (c.TreeTotal == 0) continue;
                if (_planesValid)
                {
                    if (c.TreeBounds.SqrDistance(_cameraPos) > maxD2) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, c.TreeBounds)) continue;
                }
                rp.worldBounds = c.TreeBounds;
                for (int k = 0; k < TreeMeshFactory.KindCount; k++)
                {
                    int count = c.TreeCounts[k];
                    Matrix4x4[] matrices = c.TreeMatrices[k];
                    Mesh mesh = _treeMeshes[k];
                    if (count == 0 || matrices == null || mesh == null) continue;
                    for (int start = 0; start < count; start += MaxInstancesPerBatch)
                        Graphics.RenderMeshInstanced(rp, mesh, 0, matrices, Math.Min(MaxInstancesPerBatch, count - start), start);
                }
            }
        }

        // =================================================================================================
        // Minimap
        // =================================================================================================

        private void UpdateMinimap(float dt)
        {
            if (_minimap == null) return;
            if (_minimap.IsRefreshing)
            {
                int rows = Math.Max(1, (_map.Height + Math.Max(1, MinimapRefreshFrames) - 1) / Math.Max(1, MinimapRefreshFrames));
                _minimap.Step(_state, _input, rows);
                return;
            }
            _minimapTimer += dt;
            if (!_minimapDirty || _minimapTimer < Math.Max(0.25f, MinimapRefreshSeconds)) return;
            _minimapTimer = 0f;
            _minimapDirty = false;
            _minimap.BeginRefresh(_state);
        }

        // =================================================================================================

        private static int LayerIndex(LayerMask mask)
        {
            int v = mask.value;
            if (v == 0) return 0;
            for (int i = 0; i < 32; i++) if ((v & (1 << i)) != 0) return i;
            return 0;
        }
    }
}
