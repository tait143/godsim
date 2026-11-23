using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Draws the selected <see cref="OverlayMode"/> as a transparent, terrain-hugging mesh (one 64x64-tile
    /// chunk mesh per terrain chunk, heights sampled per vertex) textured with a one-pixel-per-tile
    /// <see cref="Texture2D"/> (RGBA32 sRGB, bilinear, clamped, no mips, kept readable for updates).
    /// <para>
    /// Updates are incremental: data is recomputed at most every <see cref="UpdateIntervalSeconds"/> and only
    /// after <see cref="GodSimRunner.Ticked"/> fired (or the mode/world changed). Computation and texture fill
    /// are time-sliced over frames (<see cref="TilesPerFrame"/>) into reused buffers, then uploaded once.
    /// Steady-state updates are allocation-free. Keep this GameObject at the world origin (chunk vertices are
    /// in world-space tile coordinates, like the terrain).
    /// </para>
    /// Keys (via <see cref="InputAdapter"/>): F5 Territory, F6 Population, F7 Happiness, F8 Health, F9 Faith,
    /// F10 Fertility, F11 Danger (press again to hide), F12 cycles through every mode.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapOverlayRenderer : MonoBehaviour
    {
        /// <summary>Tiles per chunk edge (65x65 vertices per chunk: fits 16-bit indices).</summary>
        public const int ChunkSize = 64;

        /// <summary>Overlay opacity at weight 1.</summary>
        public const float BaseAlpha = 0.55f;

        /// <summary>Render queue (after opaque terrain and the transparent water at 3000).</summary>
        public const int RenderQueue = 3100;

        /// <summary>Direct-select modes for F5..F11 (in that order).</summary>
        public static readonly OverlayMode[] FunctionKeyModes =
        {
            OverlayMode.Territory, OverlayMode.PopulationDensity, OverlayMode.Happiness, OverlayMode.Health,
            OverlayMode.Faith, OverlayMode.Fertility, OverlayMode.Danger,
        };

        private static readonly KeyCode[] FunctionKeys =
        {
            KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11,
        };

        /// <summary>Key hint text shown by <see cref="OverlayLegend"/>.</summary>
        public const string KeyHints = "F5 Territory  F6 Population  F7 Happiness  F8 Health\nF9 Faith  F10 Fertility  F11 Danger  F12 Cycle (same key = off)";

        /// <summary>The active overlay renderer (last enabled instance).</summary>
        public static MapOverlayRenderer Instance { get; private set; }

        [SerializeField, Tooltip("Overlay shown at start.")]
        private OverlayMode _initialMode = OverlayMode.None;

        [SerializeField, Tooltip("Minimum seconds between recomputations (only after a simulation tick).")]
        private float _updateInterval = 0.5f;

        [SerializeField, Tooltip("Tiles computed / colorized per frame (time slicing for large maps).")]
        private int _tilesPerFrame = 65536;

        [SerializeField, Tooltip("Chunk meshes rebuilt per frame after a terrain change.")]
        private int _chunksPerFrame = 8;

        [SerializeField, Tooltip("World units above the terrain surface.")]
        private float _heightOffset = 0.06f;

        [SerializeField, Tooltip("Key cycling through every overlay mode.")]
        private KeyCode _cycleKey = KeyCode.F12;

        [SerializeField, Tooltip("Handle F5-F12 here (disable if another input layer drives Mode).")]
        private bool _handleInput = true;

        /// <summary>Raised after <see cref="Mode"/> changed (argument = new mode).</summary>
        public event Action<OverlayMode> ModeChanged;

        // ---- binding ----
        private GodSimRunner _runner;
        private WorldState _state;
        private WorldMap _map;
        private int _terrainVersion;
        private bool _simDirty = true;
        private bool _forceRecompute = true;

        // ---- data / texture ----
        private OverlayMode _mode = OverlayMode.None;
        private readonly OverlayField _field = new OverlayField();
        private Color32[] _pixels = new Color32[0];
        private Texture2D _texture;
        private Material _material;
        private bool _hasTexture;
        private readonly Color32[] _lut = new Color32[256];
        private readonly uint[] _lutPacked = new uint[256];
        private Color32[] _categoryColors = new Color32[0];
        private int _categoryLimit;

        // ---- pipeline ----
        private enum Phase { Idle, Computing, Colorizing }
        private Phase _phase = Phase.Idle;
        private int _row;
        private float _lastBeginTime = -1000f;
        private OverlayMode _pipelineMode;

        // ---- chunks ----
        private sealed class Chunk
        {
            public GameObject Go;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public int X0, Y0, W, H;
            public bool Dirty = true;
            public bool Built;
        }

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private readonly List<Vector3> _verts = new List<Vector3>((ChunkSize + 1) * (ChunkSize + 1));
        private readonly List<Vector2> _uvs = new List<Vector2>((ChunkSize + 1) * (ChunkSize + 1));
        private readonly List<Color32> _vertColors = new List<Color32>((ChunkSize + 1) * (ChunkSize + 1));
        private readonly List<int> _tris = new List<int>(ChunkSize * ChunkSize * 6);
        private bool _anyChunkDirty;
        private bool _visible;
        private int _chunkMapWidth, _chunkMapHeight;

        // ---- legend ----
        private readonly Gradient _legendGradient = new Gradient();
        private string _legendMinLabel = "";
        private string _legendMaxLabel = "";
        private float _labelMin = float.NaN, _labelMax = float.NaN;
        private OverlayMode _labelMode = OverlayMode.None;
        private const int MaxLegendCategories = 8;
        private readonly string[] _categoryNames = new string[MaxLegendCategories];
        private readonly Color[] _categoryLegendColors = new Color[MaxLegendCategories];
        private int _categoryCount;
        private int _categorySignature;
        private readonly Dictionary<int, string> _faithNames = new Dictionary<int, string>();

        // ================================================================== public API

        /// <summary>Selected overlay. Setting it restarts the computation and raises <see cref="ModeChanged"/>.</summary>
        public OverlayMode Mode
        {
            get => _mode;
            set
            {
                if (!Enum.IsDefined(typeof(OverlayMode), value)) value = OverlayMode.None;
                if (value == _mode) return;
                _mode = value;
                _phase = Phase.Idle;
                _hasTexture = false;
                _forceRecompute = true;
                _categoryCount = 0;
                _categorySignature = 0;
                RebuildLut();
                RebuildContinuousGradient();
                _labelMode = OverlayMode.None;
                _legendMinLabel = "";
                _legendMaxLabel = "";
                ApplyVisibility();
                var handler = ModeChanged;
                if (handler != null)
                {
                    try { handler(value); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        /// <summary>Legend title of the current mode ("" for None).</summary>
        public string LegendTitle => OverlayData.Title(_mode);

        /// <summary>Gradient of the current mode (8 keys sampled from the ramp; civ colors with fixed blending for categorical modes).</summary>
        public Gradient LegendGradient => _legendGradient;

        /// <summary>Label of the low end of the current range (descriptive word or formatted value).</summary>
        public string LegendMinLabel => _legendMinLabel;

        /// <summary>Label of the high end of the current range.</summary>
        public string LegendMaxLabel => _legendMaxLabel;

        /// <summary>Color ramp of the current mode.</summary>
        public ColorRampId Ramp => OverlayData.RampFor(_mode);

        /// <summary>True for Territory/Religion (legend shows swatches instead of a bar).</summary>
        public bool IsCategorical => OverlayData.IsCategorical(_mode);

        /// <summary>Number of legend swatches for categorical modes (at most 8).</summary>
        public int LegendCategoryCount => _categoryCount;

        /// <summary>Legend swatch (name, color) of a categorical mode.</summary>
        public bool TryGetLegendCategory(int index, out string label, out Color color)
        {
            if (index < 0 || index >= _categoryCount) { label = ""; color = Color.white; return false; }
            label = _categoryNames[index] ?? "";
            color = _categoryLegendColors[index];
            return true;
        }

        /// <summary>The latest completed data field (read-only use; values are NaN where there is no data).</summary>
        public OverlayField Field => _field;

        /// <summary>Minimum seconds between recomputations.</summary>
        public float UpdateIntervalSeconds
        {
            get => _updateInterval;
            set => _updateInterval = float.IsNaN(value) ? 0.5f : Mathf.Clamp(value, 0.05f, 10f);
        }

        /// <summary>Tiles processed per frame (time slicing).</summary>
        public int TilesPerFrame
        {
            get => _tilesPerFrame;
            set => _tilesPerFrame = Mathf.Clamp(value, 1024, 1 << 22);
        }

        /// <summary>Selects the next mode in enum order (wrapping through None).</summary>
        public void CycleMode(int direction = 1)
        {
            int count = Enum.GetValues(typeof(OverlayMode)).Length;
            int next = ((int)_mode + (direction >= 0 ? 1 : -1) + count) % count;
            Mode = (OverlayMode)next;
        }

        /// <summary>Forces a recomputation on the next frame (e.g. after external data edits).</summary>
        public void Invalidate() { _forceRecompute = true; }

        // ================================================================== lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("MapOverlayRenderer: another instance is active; disabling duplicate.");
                enabled = false;
                return;
            }
            Instance = this;
            _updateInterval = float.IsNaN(_updateInterval) ? 0.5f : Mathf.Clamp(_updateInterval, 0.05f, 10f);
            _tilesPerFrame = Mathf.Clamp(_tilesPerFrame, 1024, 1 << 22);
            _chunksPerFrame = Mathf.Clamp(_chunksPerFrame, 1, 1024);
            RebuildLut();
            RebuildContinuousGradient();
            Mode = _initialMode;
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
            BindRunner(GodSimRunner.Instance);
            ApplyVisibility();
        }

        private void OnDisable()
        {
            UnbindRunner();
            _phase = Phase.Idle;
            SetChunksVisible(false);
        }

        private void OnDestroy()
        {
            UnbindRunner();
            DestroyChunks();
            if (_texture != null) Destroy(_texture);
            if (_material != null) Destroy(_material);
            _texture = null;
            _material = null;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Instance != this) return;
            var runner = GodSimRunner.Instance;
            if (runner != _runner) BindRunner(runner);
            if (_handleInput) HandleInput();

            var sim = _runner != null ? _runner.Simulation : null;
            var state = sim != null ? sim.State : null;
            var map = state != null ? state.Map : null;
            if (state != _state || map != _map) OnWorldChanged(state, map);
            if (map == null || _mode == OverlayMode.None) { ApplyVisibility(); return; }

            if (map.TerrainVersion != _terrainVersion)
            {
                _terrainVersion = map.TerrainVersion;
                for (int i = 0; i < _chunks.Count; i++) _chunks[i].Dirty = true;
                _anyChunkDirty = _chunks.Count > 0;
                if (OverlayData.IsTerrainStatic(_mode)) _forceRecompute = true;
            }
            if (_anyChunkDirty) RebuildDirtyChunks(map);

            if (_phase == Phase.Idle)
            {
                float now = Time.unscaledTime;
                bool due = _forceRecompute ||
                           (_simDirty && !OverlayData.IsTerrainStatic(_mode) && now - _lastBeginTime >= _updateInterval);
                if (due) BeginPipeline(state, now);
            }
            if (_phase != Phase.Idle) StepPipeline(state);
            ApplyVisibility();
        }

        // ================================================================== runner binding

        private void BindRunner(GodSimRunner runner)
        {
            if (runner == _runner && runner != null) return;
            UnbindRunner();
            if (runner == null) return;
            _runner = runner;
            _runner.SimulationCreated += OnSimulationCreated;
            _runner.Ticked += OnTicked;
            _simDirty = true;
        }

        private void UnbindRunner()
        {
            // Plain C# reference check: unsubscribe even if the runner object was already destroyed.
            if (ReferenceEquals(_runner, null)) return;
            _runner.SimulationCreated -= OnSimulationCreated;
            _runner.Ticked -= OnTicked;
            _runner = null;
        }

        private void OnSimulationCreated(GodSimulation sim)
        {
            // New or loaded world: drop everything tied to the old map; Update rebinds to sim.State.
            _state = null;
            _map = null;
            _simDirty = true;
            _forceRecompute = true;
            _phase = Phase.Idle;
            _hasTexture = false;
        }

        private void OnTicked() { _simDirty = true; }

        private void OnWorldChanged(WorldState state, WorldMap map)
        {
            _state = state;
            _map = map;
            _phase = Phase.Idle;
            _hasTexture = false;
            _forceRecompute = true;
            _simDirty = true;
            if (map == null) { SetChunksVisible(false); return; }
            _terrainVersion = map.TerrainVersion;
            if (_chunks.Count == 0 || map.Width != _chunkMapWidth || map.Height != _chunkMapHeight) CreateChunks(map);
            else { for (int i = 0; i < _chunks.Count; i++) _chunks[i].Dirty = true; _anyChunkDirty = true; }
            EnsureTexture(map.Width, map.Height);
        }

        // ================================================================== input

        private void HandleInput()
        {
            for (int k = 0; k < FunctionKeys.Length; k++)
            {
                if (!InputAdapter.GetKeyDown(FunctionKeys[k])) continue;
                OverlayMode m = FunctionKeyModes[k];
                Mode = _mode == m ? OverlayMode.None : m;
                return;
            }
            if (_cycleKey != KeyCode.None && InputAdapter.GetKeyDown(_cycleKey)) CycleMode(1);
        }

        // ================================================================== pipeline

        private void BeginPipeline(WorldState state, float now)
        {
            _forceRecompute = false;
            _simDirty = false;
            _lastBeginTime = now;
            _pipelineMode = _mode;
            if (!OverlayData.Begin(state, _mode, _field)) { _phase = Phase.Idle; return; }
            _row = 0;
            _phase = Phase.Computing;
        }

        private void StepPipeline(WorldState state)
        {
            if (_pipelineMode != _mode || state == null || state.Map == null || state.Map.Width != _field.Width || state.Map.Height != _field.Height)
            {
                _phase = Phase.Idle;
                _forceRecompute = true;
                return;
            }
            int w = Math.Max(1, _field.Width), h = _field.Height;
            int budget = _tilesPerFrame;
            while (budget > 0 && _phase != Phase.Idle)
            {
                int rows = Math.Max(1, Math.Min(h - _row, budget / w));
                int y1 = _row + rows;
                if (_phase == Phase.Computing)
                {
                    OverlayData.ComputeRows(state, _field, _row, y1);
                    _row = y1;
                    if (_row >= h)
                    {
                        OverlayData.End(state, _field);
                        PrepareColorize(state);
                        _row = 0;
                        _phase = Phase.Colorizing;
                    }
                }
                else
                {
                    ColorizeRows(_row, y1);
                    _row = y1;
                    if (_row >= h)
                    {
                        Upload();
                        _phase = Phase.Idle;
                    }
                }
                budget -= rows * w;
            }
        }

        private void PrepareColorize(WorldState state)
        {
            if (!_field.IsCategorical) { UpdateRangeLabels(); return; }

            // Category id (civ id) -> color, honouring civ banner colors (Territory) or a faith palette (Religion).
            var civs = state.Civilizations.Items;
            int limit = Math.Max(1, state.Ids.NextCivilizationId);
            for (int k = 0; k < civs.Count; k++) if (civs[k] != null && civs[k].Id >= limit) limit = civs[k].Id + 1;
            if (limit > OverlayField.MaxTrackedIds) limit = OverlayField.MaxTrackedIds;
            if (_categoryColors.Length < limit) _categoryColors = new Color32[Math.Max(limit, _categoryColors.Length * 2)];
            _categoryLimit = limit;
            bool religion = _mode == OverlayMode.Religion;
            for (int id = 0; id < limit; id++) _categoryColors[id] = ToColor32(ColorRamps.Categorical(id));

            int count = 0;
            int signature = 17;
            for (int k = 0; k < civs.Count; k++)
            {
                var civ = civs[k];
                if (civ == null || civ.Id < 0 || civ.Id >= limit) continue;
                uint packed;
                if (religion)
                {
                    if (!civ.Memory.ContainsKey(OverlayData.ReligionFoundedKey)) continue;
                    packed = ColorRamps.Categorical(civ.Id);
                }
                else
                {
                    int duplicates = 0;
                    for (int j = 0; j < k; j++)
                        if (civs[j] != null && civs[j].Color == civ.Color) duplicates++;
                    packed = ColorRamps.CivColor(civ.Color, civ.Id, duplicates);
                }
                _categoryColors[civ.Id] = ToColor32(packed);
                if (!civ.IsAlive || count >= MaxLegendCategories) continue;
                _categoryNames[count] = religion ? FaithName(civ) : civ.Name;
                _categoryLegendColors[count] = ToColor(packed);
                unchecked { signature = signature * 31 + civ.Id * 7919 + (int)packed; }
                count++;
            }
            _categoryCount = count;
            if (signature != _categorySignature)
            {
                _categorySignature = signature;
                RebuildCategoricalGradient();
            }
        }

        private void ColorizeRows(int y0, int y1)
        {
            float[] values = _field.Values, weights = _field.Weights;
            int w = _field.Width;
            bool categorical = _field.IsCategorical;
            float min = _field.Min, range = _field.Max - _field.Min;
            float inv = range > 1e-6f ? 255f / range : 0f;
            Color32 neutral = ToColor32(ColorRamps.Neutral);
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                Color32 last = neutral;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    float v = values[i];
                    if (float.IsNaN(v))
                    {
                        // Transparent, but keep the neighbour's RGB so bilinear filtering does not darken edges.
                        last.a = 0;
                        _pixels[i] = last;
                        continue;
                    }
                    Color32 c;
                    if (categorical)
                    {
                        int cat = (int)v;
                        c = cat < 0 ? neutral : (cat < _categoryLimit ? _categoryColors[cat] : ToColor32(ColorRamps.Categorical(cat)));
                    }
                    else
                    {
                        float t = inv > 0f ? (v - min) * inv : 127.5f;
                        int idx = t <= 0f ? 0 : (t >= 255f ? 255 : (int)(t + 0.5f));
                        c = _lut[idx];
                    }
                    float a = weights[i];
                    if (!(a > 0f)) a = 0f; else if (a > 1f) a = 1f;
                    c.a = (byte)(a * (BaseAlpha * 255f) + 0.5f);
                    _pixels[i] = c;
                    last = c;
                }
            }
        }

        private void Upload()
        {
            if (_texture == null || _pixels.Length != _texture.width * _texture.height) return;
            if (_material == null) EnsureMaterial();
            _texture.SetPixels32(_pixels);
            _texture.Apply(false, false); // no mips; stay CPU-readable for the next incremental update
            _hasTexture = true;
        }

        private void UpdateRangeLabels()
        {
            if (_labelMode == _mode && _labelMin == _field.Min && _labelMax == _field.Max) return;
            _labelMode = _mode;
            _labelMin = _field.Min;
            _labelMax = _field.Max;
            _legendMinLabel = OverlayData.RangeLabel(_mode, _field.Min, false);
            _legendMaxLabel = OverlayData.RangeLabel(_mode, _field.Max, true);
        }

        private string FaithName(Civilization civ)
        {
            string name;
            if (_faithNames.TryGetValue(civ.Id, out name)) return name;
            name = "Faith of " + (civ.Name ?? "?");
            if (_faithNames.Count > 4096) _faithNames.Clear();
            _faithNames[civ.Id] = name;
            return name;
        }

        // ================================================================== texture / material

        private void EnsureTexture(int width, int height)
        {
            if (_texture == null || _texture.width != width || _texture.height != height)
            {
                if (_texture != null) Destroy(_texture);
                _texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    name = "GodSim Overlay",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                    hideFlags = HideFlags.DontSave,
                };
            }
            if (_pixels.Length != width * height) _pixels = new Color32[width * height];
            EnsureMaterial();
            if (_material != null) _material.mainTexture = _texture;
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;
            // Copy the shared transparent unlit material (never mutate the cached library instance).
            Material source = MaterialLibrary.Unlit(new Color(1f, 1f, 1f, 0.5f));
            if (source == null) return;
            _material = new Material(source)
            {
                name = "GodSim Overlay",
                hideFlags = HideFlags.DontSave,
                renderQueue = RenderQueue,
            };
            _material.color = Color.white; // opacity comes from the texture alpha
            if (_texture != null) _material.mainTexture = _texture;
            for (int i = 0; i < _chunks.Count; i++) _chunks[i].Renderer.sharedMaterial = _material;
        }

        // ================================================================== chunks

        private void CreateChunks(WorldMap map)
        {
            DestroyChunks();
            EnsureMaterial();
            for (int y0 = 0; y0 < map.Height; y0 += ChunkSize)
                for (int x0 = 0; x0 < map.Width; x0 += ChunkSize)
                {
                    var go = new GameObject("Overlay Chunk " + x0 + "," + y0);
                    go.layer = gameObject.layer;
                    go.transform.SetParent(transform, false);
                    var mesh = new Mesh { name = "GodSim Overlay Chunk", hideFlags = HideFlags.DontSave };
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _material;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.enabled = false;
                    _chunks.Add(new Chunk
                    {
                        Go = go, Mesh = mesh, Renderer = mr, X0 = x0, Y0 = y0,
                        W = Math.Min(ChunkSize, map.Width - x0), H = Math.Min(ChunkSize, map.Height - y0),
                    });
                }
            _chunkMapWidth = map.Width;
            _chunkMapHeight = map.Height;
            _anyChunkDirty = _chunks.Count > 0;
            _visible = false;
        }

        private void DestroyChunks()
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                var c = _chunks[i];
                if (c.Mesh != null) Destroy(c.Mesh);
                if (c.Go != null) Destroy(c.Go);
            }
            _chunks.Clear();
            _chunkMapWidth = _chunkMapHeight = 0;
            _anyChunkDirty = false;
        }

        private void RebuildDirtyChunks(WorldMap map)
        {
            int budget = _chunksPerFrame;
            bool remaining = false;
            for (int i = 0; i < _chunks.Count; i++)
            {
                var c = _chunks[i];
                if (!c.Dirty) continue;
                if (budget <= 0) { remaining = true; break; }
                BuildChunkMesh(map, c);
                c.Dirty = false;
                c.Built = true;
                if (c.Renderer != null) c.Renderer.enabled = _visible;
                budget--;
            }
            _anyChunkDirty = remaining;
        }

        private void BuildChunkMesh(WorldMap map, Chunk c)
        {
            if (c.Mesh == null) return;
            _verts.Clear();
            _uvs.Clear();
            _vertColors.Clear();
            _tris.Clear();
            float invW = 1f / map.Width, invH = 1f / map.Height;
            float ts = GodSimCoords.TileSize;
            Color32 white = new Color32(255, 255, 255, 255);
            for (int vy = 0; vy <= c.H; vy++)
            {
                int y = c.Y0 + vy;
                for (int vx = 0; vx <= c.W; vx++)
                {
                    int x = c.X0 + vx;
                    // Corner (x, y) touches tiles (x-1..x, y-1..y): take the highest so the overlay never dips into terrain.
                    float h = Math.Max(
                        Math.Max(GodSimCoords.SurfaceHeight(map, x - 1, y - 1), GodSimCoords.SurfaceHeight(map, x, y - 1)),
                        Math.Max(GodSimCoords.SurfaceHeight(map, x - 1, y), GodSimCoords.SurfaceHeight(map, x, y)));
                    _verts.Add(new Vector3(x * ts, h + _heightOffset, y * ts));
                    _uvs.Add(new Vector2(x * invW, y * invH));
                    _vertColors.Add(white);
                }
            }
            int stride = c.W + 1;
            for (int ty = 0; ty < c.H; ty++)
                for (int tx = 0; tx < c.W; tx++)
                {
                    int i0 = ty * stride + tx, i1 = i0 + 1, i2 = i0 + stride, i3 = i2 + 1;
                    // Clockwise seen from above (+Y), Unity's front face.
                    _tris.Add(i0); _tris.Add(i2); _tris.Add(i1);
                    _tris.Add(i1); _tris.Add(i2); _tris.Add(i3);
                }
            c.Mesh.Clear();
            c.Mesh.SetVertices(_verts);
            c.Mesh.SetUVs(0, _uvs);
            c.Mesh.SetColors(_vertColors);
            c.Mesh.SetTriangles(_tris, 0);
            c.Mesh.RecalculateBounds();
        }

        private void ApplyVisibility()
        {
            bool show = isActiveAndEnabled && _mode != OverlayMode.None && _hasTexture && _map != null && _material != null;
            if (show == _visible) return;
            SetChunksVisible(show);
        }

        private void SetChunksVisible(bool show)
        {
            _visible = show;
            for (int i = 0; i < _chunks.Count; i++)
            {
                var c = _chunks[i];
                if (c.Renderer != null) c.Renderer.enabled = show && c.Built;
            }
        }

        // ================================================================== colors / legend

        private void RebuildLut()
        {
            ColorRamps.FillLut(OverlayData.RampFor(_mode), _lutPacked);
            for (int i = 0; i < _lut.Length; i++) _lut[i] = ToColor32(_lutPacked[i]);
        }

        private void RebuildContinuousGradient()
        {
            ColorRampId ramp = OverlayData.RampFor(_mode);
            if (ramp == ColorRampId.Categorical) { RebuildCategoricalGradient(); return; }
            var colorKeys = new GradientColorKey[8];
            for (int k = 0; k < colorKeys.Length; k++)
            {
                float t = k / (float)(colorKeys.Length - 1);
                colorKeys[k] = new GradientColorKey(ToColor(ColorRamps.Evaluate(ramp, t)), t);
            }
            _legendGradient.mode = GradientMode.Blend;
            _legendGradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(BaseAlpha, 0f), new GradientAlphaKey(BaseAlpha, 1f) });
        }

        private void RebuildCategoricalGradient()
        {
            int n = Math.Max(1, _categoryCount);
            var colorKeys = new GradientColorKey[n];
            for (int k = 0; k < n; k++)
            {
                Color c = _categoryCount > 0 ? _categoryLegendColors[k] : ToColor(ColorRamps.Neutral);
                colorKeys[k] = new GradientColorKey(c, n == 1 ? 1f : (k + 1) / (float)n);
            }
            _legendGradient.mode = GradientMode.Fixed;
            _legendGradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(BaseAlpha, 0f), new GradientAlphaKey(BaseAlpha, 1f) });
        }

        internal static Color32 ToColor32(uint rgba) =>
            new Color32(ColorRamps.R(rgba), ColorRamps.G(rgba), ColorRamps.B(rgba), ColorRamps.A(rgba));

        internal static Color ToColor(uint rgba) =>
            new Color(ColorRamps.R(rgba) / 255f, ColorRamps.G(rgba) / 255f, ColorRamps.B(rgba) / 255f, ColorRamps.A(rgba) / 255f);
    }
}
