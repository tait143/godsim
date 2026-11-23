using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Renders every simulation building as a pooled view (root with a selection BoxCollider + body mesh,
    /// construction scaffold and civ banner children). Views are created/updated incrementally from
    /// EventBus events; per-tick state (construction progress, fire, style age, owner, tier) is synced
    /// round-robin with fixed per-frame budgets. Destroyed buildings leave a ruin that sinks away after
    /// <see cref="RuinLifetime"/> seconds of real time. Far views swap to a box LOD mesh.
    /// Handles a new world (SimulationCreated fired again) by releasing all views and rebinding.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingRenderer : MonoBehaviour
    {
        /// <summary>The active renderer (null when none is enabled).</summary>
        public static BuildingRenderer Instance { get; private set; }

        [Tooltip("Show scaffolds and grow incomplete buildings by construction progress. Off = draw them finished.")]
        public bool ShowConstruction = true;
        [Tooltip("Camera distance (world units) beyond which views swap to a simple box mesh. <= 0 disables LOD.")]
        public float LodDistance = 90f;
        [Tooltip("Seconds (real time) a ruin stays before it sinks away and returns to the pool.")]
        public float RuinLifetime = 10f;
        [Tooltip("Optional camera for LOD distance; defaults to Camera.main.")]
        public Camera LodCamera;
        public bool CastShadows = true;
        public int MaxProgressUpdatesPerFrame = 200;
        public int MaxStateSyncsPerFrame = 200;
        public int MaxLodChecksPerFrame = 400;
        public int MaxPooledViews = 512;

        private const float T = GodSimCoords.TileSize;
        private const int MeshCacheSoftLimit = 4096;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color FireColor = new Color(1.35f, 0.55f, 0.14f, 1f);
        private static readonly Color CharredTint = new Color(0.5f, 0.45f, 0.42f, 1f);
        private static readonly Color NoCivColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        private enum ListKind { Active, Incomplete, Burning, Ruins }

        private sealed class View
        {
            public int Id;
            public Building Building;
            public GameObject Root;
            public Transform RootT;
            public BoxCollider Collider;
            public BuildingViewTag Tag;
            public Transform BodyT;
            public MeshFilter BodyMf;
            public MeshRenderer BodyMr;
            public GameObject ScaffoldGo;
            public MeshFilter ScaffoldMf;
            public MeshRenderer ScaffoldMr;
            public GameObject BannerGo;
            public Transform BannerT;
            public MeshRenderer BannerMr;

            public Mesh Full, Lod;
            public BuildingType Type;
            public BuildingShape Shape;
            public AgeId StyleAge;
            public int Level;
            public byte Rotation;
            public SettlementTier Tier;
            public int CivId;
            public int MeshW, MeshH;
            public float TopHeight, ScaleXZ = 1f, Progress;
            public bool Complete, OnFire, UsingLod, HasBanner, HasTint, IsRuin;
            public Color BaseTint = Color.white;
            public Vector3 Position;
            public float RuinStart;
            public int ActiveIndex = -1, IncompleteIndex = -1, BurningIndex = -1, RuinIndex = -1;
        }

        private readonly Dictionary<int, View> _views = new Dictionary<int, View>(1024);
        private readonly List<View> _active = new List<View>(1024);
        private readonly List<View> _incomplete = new List<View>(256);
        private readonly List<View> _burning = new List<View>(32);
        private readonly List<View> _ruins = new List<View>(64);
        private readonly Stack<View> _pool = new Stack<View>(256);

        private GodSimRunner _runner;
        private GodSimulation _sim;
        private EventBus _bus;
        private MaterialPropertyBlock _mpb;
        private Camera _mainCamera;
        private float _nextCameraLookup;
        private int _layer = -1;
        private bool _lastShowConstruction;
        private int _progressCursor, _progressRemaining, _syncCursor, _syncRemaining, _lodCursor;

        private Action<GodSimulation> _onSimulationCreated;
        private Action _onTicked;
        private Action<BuildingPlacedEvent> _onPlaced;
        private Action<BuildingCompletedEvent> _onCompleted;
        private Action<BuildingDestroyedEvent> _onDestroyed;
        private Action<BuildingUpgradedEvent> _onUpgraded;
        private Action<SettlementCapturedEvent> _onCaptured;
        private Action<SettlementTierChangedEvent> _onTierChanged;
        private Action<TerrainChangedEvent> _onTerrainChanged;

        /// <summary>Number of live (non-ruin) building views.</summary>
        public int ViewCount => _active.Count;

        /// <summary>The view root for a building (has the BoxCollider and <see cref="BuildingViewTag"/>), or null.</summary>
        public GameObject GetView(int buildingId)
        {
            return _views.TryGetValue(buildingId, out var v) ? v.Root : null;
        }

        // =============================================================================== lifecycle

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _layer = LayerMask.NameToLayer("Buildings");
            _lastShowConstruction = ShowConstruction;
            _onSimulationCreated = OnSimulationCreated;
            _onTicked = OnTicked;
            _onPlaced = OnBuildingPlaced;
            _onCompleted = OnBuildingCompleted;
            _onDestroyed = OnBuildingDestroyed;
            _onUpgraded = OnBuildingUpgraded;
            _onCaptured = OnSettlementCaptured;
            _onTierChanged = OnSettlementTierChanged;
            _onTerrainChanged = OnTerrainChanged;
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Another BuildingRenderer is already active; disabling this one.");
                enabled = false;
                return;
            }
            Instance = this;
            // Binding is deferred to LateUpdate: creating/releasing (activating) child views from inside
            // OnEnable/OnDisable can hit "GameObject is already being activated or deactivated".
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            UnbindRunner();
            UnbindSimulation();
            // Views are kept (hidden with the hierarchy or frozen); they are released and rebuilt when
            // LateUpdate rebinds to the runner after re-enabling.
        }

        private void OnDestroy()
        {
            UnbindRunner();
            UnbindSimulation();
            if (Instance == this) Instance = null;
            if (Instance == null) ProceduralBuildingMeshes.ClearCache();
        }

        private void TryBindRunner()
        {
            var runner = GodSimRunner.Instance;
            if (runner == null || ReferenceEquals(runner, _runner)) return;
            UnbindRunner();
            _runner = runner;
            _runner.SimulationCreated += _onSimulationCreated;
            _runner.Ticked += _onTicked;
            if (_runner.Simulation != null) OnSimulationCreated(_runner.Simulation);
        }

        private void UnbindRunner()
        {
            if (!ReferenceEquals(_runner, null))
            {
                _runner.SimulationCreated -= _onSimulationCreated;
                _runner.Ticked -= _onTicked;
            }
            _runner = null;
        }

        private void OnSimulationCreated(GodSimulation sim)
        {
            UnbindSimulation();
            ReleaseAll();
            if (ProceduralBuildingMeshes.CachedMeshCount > MeshCacheSoftLimit) ProceduralBuildingMeshes.ClearCache();
            if (sim == null || sim.State == null || sim.Db == null || sim.Events == null) return;
            _sim = sim;
            _bus = sim.Events;
            _bus.Subscribe(_onPlaced);
            _bus.Subscribe(_onCompleted);
            _bus.Subscribe(_onDestroyed);
            _bus.Subscribe(_onUpgraded);
            _bus.Subscribe(_onCaptured);
            _bus.Subscribe(_onTierChanged);
            _bus.Subscribe(_onTerrainChanged);
            var items = sim.State.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b != null && !b.IsDestroyed) CreateView(b);
            }
            _progressRemaining = _incomplete.Count;
        }

        private void UnbindSimulation()
        {
            if (_bus != null)
            {
                _bus.Unsubscribe(_onPlaced);
                _bus.Unsubscribe(_onCompleted);
                _bus.Unsubscribe(_onDestroyed);
                _bus.Unsubscribe(_onUpgraded);
                _bus.Unsubscribe(_onCaptured);
                _bus.Unsubscribe(_onTierChanged);
                _bus.Unsubscribe(_onTerrainChanged);
            }
            _bus = null;
            _sim = null;
        }

        private void OnTicked()
        {
            _progressRemaining = _incomplete.Count;
            _syncRemaining = _active.Count;
        }

        // =============================================================================== per frame

        private void LateUpdate()
        {
            if (Instance != this) return;
            if (!ReferenceEquals(_runner, null) && _runner == null)
            {
                // runner destroyed (scene change): drop everything until a new one appears
                UnbindRunner();
                UnbindSimulation();
                ReleaseAll();
            }
            if (ReferenceEquals(_runner, null)) TryBindRunner();

            if (ShowConstruction != _lastShowConstruction)
            {
                _lastShowConstruction = ShowConstruction;
                for (int i = 0; i < _incomplete.Count; i++) ApplyConstruction(_incomplete[i]);
            }

            if (_sim != null)
            {
                UpdateProgress();
                SyncState();
            }
            UpdateFire();
            UpdateLod();
            UpdateRuins();
        }

        private void UpdateProgress()
        {
            int budget = Mathf.Min(MaxProgressUpdatesPerFrame, _progressRemaining);
            for (; budget > 0 && _incomplete.Count > 0; budget--)
            {
                if (_progressCursor >= _incomplete.Count) _progressCursor = 0;
                var v = _incomplete[_progressCursor++];
                _progressRemaining--;
                var b = v.Building;
                if (b == null || b.IsDestroyed) continue; // destroyed event / state sync turns it into a ruin
                if (b.IsComplete)
                {
                    SetComplete(v);
                    _progressCursor--; // the swapped-in view now occupies this slot
                    continue;
                }
                float p = MathUtil.Clamp01(MathUtil.Sanitize(b.ConstructionProgress));
                if (Mathf.Abs(p - v.Progress) > 0.002f)
                {
                    v.Progress = p;
                    ApplyConstruction(v);
                }
            }
            if (_incomplete.Count == 0) _progressRemaining = 0;
        }

        private void SyncState()
        {
            int budget = Mathf.Min(MaxStateSyncsPerFrame, _syncRemaining);
            var store = _sim.State.Buildings;
            for (; budget > 0 && _active.Count > 0; budget--)
            {
                if (_syncCursor >= _active.Count) _syncCursor = 0;
                var v = _active[_syncCursor++];
                _syncRemaining--;
                var b = v.Building;
                if (b == null || b.IsDestroyed || !ReferenceEquals(store.Get(v.Id), b))
                {
                    MakeRuin(v, DestroyCause.Decay);
                    _syncCursor--;
                    continue;
                }
                if (b.IsOnFire != v.OnFire) SetFire(v, b.IsOnFire);
                if (b.StyleAge != v.StyleAge || b.Rotation != v.Rotation || (v.Type == BuildingType.TownCenter && TierOf(b) != v.Tier))
                    RebuildAppearance(v, b);
                if (b.CivilizationId != v.CivId)
                {
                    v.CivId = b.CivilizationId;
                    ApplyBanner(v);
                }
                if (b.IsComplete != v.Complete)
                {
                    if (b.IsComplete) SetComplete(v);
                    else
                    {
                        v.Complete = false;
                        ListAdd(_incomplete, v, ListKind.Incomplete);
                        ApplyConstruction(v);
                    }
                }
            }
            if (_active.Count == 0) _syncRemaining = 0;
        }

        private void UpdateFire()
        {
            if (_burning.Count == 0) return;
            float t = Time.unscaledTime;
            for (int i = 0; i < _burning.Count; i++)
            {
                var v = _burning[i];
                float f = 0.5f + 0.28f * Mathf.Sin(t * 13f + v.Id * 1.7f) + 0.22f * Mathf.Sin(t * 7.3f + v.Id * 0.9f);
                SetBodyColor(v, Color.Lerp(v.BaseTint, FireColor, 0.3f + 0.5f * f));
            }
        }

        private Camera ResolveCamera()
        {
            if (LodCamera != null) return LodCamera;
            if (_mainCamera == null && Time.unscaledTime >= _nextCameraLookup)
            {
                _mainCamera = Camera.main;
                _nextCameraLookup = Time.unscaledTime + 1f;
            }
            return _mainCamera;
        }

        private void UpdateLod()
        {
            if (_active.Count == 0) return;
            if (LodDistance <= 0f)
            {
                // LOD disabled at runtime: bring any LOD views back gradually.
                int m = Mathf.Min(MaxLodChecksPerFrame, _active.Count);
                for (int k = 0; k < m; k++)
                {
                    if (_lodCursor >= _active.Count) _lodCursor = 0;
                    var v = _active[_lodCursor++];
                    if (v.UsingLod) SetLod(v, false);
                }
                return;
            }
            var cam = ResolveCamera();
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            float far = LodDistance * 1.1f, near = LodDistance * 0.9f;
            float far2 = far * far, near2 = near * near;
            int n = Mathf.Min(MaxLodChecksPerFrame, _active.Count);
            for (int k = 0; k < n; k++)
            {
                if (_lodCursor >= _active.Count) _lodCursor = 0;
                var v = _active[_lodCursor++];
                float d2 = (v.Position - cp).sqrMagnitude;
                bool want = v.UsingLod ? d2 > near2 : d2 > far2;
                if (want != v.UsingLod) SetLod(v, want);
            }
        }

        private void UpdateRuins()
        {
            if (_ruins.Count == 0) return;
            float now = Time.unscaledTime;
            float life = Mathf.Max(0.5f, RuinLifetime);
            float fadeStart = Mathf.Max(0f, life - 3f);
            for (int i = _ruins.Count - 1; i >= 0; i--)
            {
                var v = _ruins[i];
                float age = now - v.RuinStart;
                if (age >= life)
                {
                    ListRemove(_ruins, v, ListKind.Ruins);
                    Release(v);
                    continue;
                }
                if (age > fadeStart)
                {
                    float k = (age - fadeStart) / Mathf.Max(0.01f, life - fadeStart);
                    v.BodyT.localPosition = new Vector3(0f, -0.35f * T * k, 0f);
                    float s = v.ScaleXZ * (1f - 0.3f * k);
                    v.BodyT.localScale = new Vector3(s, Mathf.Max(0.05f, 1f - 0.7f * k), s);
                }
            }
        }

        // =============================================================================== events

        private Building GetBuilding(int id) => _sim != null ? _sim.State.Buildings.Get(id) : null;

        private void OnBuildingPlaced(BuildingPlacedEvent e)
        {
            var b = GetBuilding(e.BuildingId);
            if (b == null || b.IsDestroyed) return;
            if (_views.TryGetValue(b.Id, out var v)) RebuildAppearance(v, b);
            else CreateView(b);
        }

        private void OnBuildingCompleted(BuildingCompletedEvent e)
        {
            var b = GetBuilding(e.BuildingId);
            if (b == null || b.IsDestroyed) return;
            if (_views.TryGetValue(b.Id, out var v)) SetComplete(v);
            else CreateView(b);
        }

        private void OnBuildingDestroyed(BuildingDestroyedEvent e)
        {
            if (_views.TryGetValue(e.BuildingId, out var v)) MakeRuin(v, e.Cause);
        }

        private void OnBuildingUpgraded(BuildingUpgradedEvent e)
        {
            if (!_views.TryGetValue(e.BuildingId, out var v)) return;
            var b = GetBuilding(e.BuildingId);
            if (b == null || b.IsDestroyed) return;
            if (b.StyleAge != v.StyleAge || b.Rotation != v.Rotation) RebuildAppearance(v, b);
            v.Level = b.Level;
        }

        private void OnSettlementCaptured(SettlementCapturedEvent e)
        {
            if (_sim == null) return;
            var s = _sim.State.Settlements.Get(e.SettlementId);
            if (s == null) return;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!_views.TryGetValue(ids[i], out var v)) continue;
                v.CivId = v.Building != null ? v.Building.CivilizationId : e.NewCivilizationId;
                ApplyBanner(v);
            }
        }

        private void OnSettlementTierChanged(SettlementTierChangedEvent e)
        {
            if (_sim == null) return;
            var s = _sim.State.Settlements.Get(e.SettlementId);
            if (s == null || s.TownCenterId < 0) return;
            if (!_views.TryGetValue(s.TownCenterId, out var v)) return;
            var b = GetBuilding(s.TownCenterId);
            if (b != null && !b.IsDestroyed) RebuildAppearance(v, b);
        }

        private void OnTerrainChanged(TerrainChangedEvent e)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var v = _active[i];
                var b = v.Building;
                if (b == null) continue;
                if (b.Origin.X > e.MaxX || b.Origin.Y > e.MaxY || b.Origin.X + b.Width - 1 < e.MinX || b.Origin.Y + b.Height - 1 < e.MinY) continue;
                Place(v, b);
            }
        }

        // =============================================================================== views

        private void CreateView(Building b)
        {
            if (_sim == null || !_sim.Db.TryGetBuilding(b.Type, out var def)) return;
            var v = Rent();
            v.Id = b.Id;
            v.Building = b;
            v.Type = b.Type;
            v.Shape = def.Shape;
            v.HasBanner = def.Shape != BuildingShape.Wall && def.Shape != BuildingShape.Field &&
                (def.IsWonder || def.Category == BuildingCategory.Civic || def.Category == BuildingCategory.Military || def.Category == BuildingCategory.Wonder);
            v.Tag.BuildingId = b.Id;
            v.CivId = b.CivilizationId;
            v.Complete = b.IsComplete;
            v.Progress = MathUtil.Clamp01(MathUtil.Sanitize(b.ConstructionProgress));
            v.UsingLod = false;
#if UNITY_EDITOR
            v.Root.name = "Building " + b.Id + " (" + b.Type + ")";
#endif
            _views[b.Id] = v;
            ListAdd(_active, v, ListKind.Active);
            if (!v.Complete) ListAdd(_incomplete, v, ListKind.Incomplete);
            v.Root.SetActive(true);
            ApplyAppearance(v, b, def);
            Place(v, b);
            ApplyConstruction(v);
            ApplyBanner(v);
            if (b.IsOnFire) SetFire(v, true);
        }

        private void RebuildAppearance(View v, Building b)
        {
            if (_sim == null || !_sim.Db.TryGetBuilding(b.Type, out var def)) return;
            ApplyAppearance(v, b, def);
            Place(v, b);
            ApplyConstruction(v);
            ApplyBanner(v);
        }

        private SettlementTier TierOf(Building b)
        {
            var s = _sim != null ? _sim.State.Settlements.Get(b.SettlementId) : null;
            return s != null ? s.Tier : SettlementTier.Camp;
        }

        private ArchitectureStyle StyleOf(RaceId race)
        {
            var races = _sim.Db.Races;
            int i = (int)race;
            var def = i >= 0 && i < races.Count ? races[i] : null;
            return def != null ? def.Architecture : null;
        }

        private void ApplyAppearance(View v, Building b, BuildingDefinition def)
        {
            ArchitectureStyle style = StyleOf(b.StyleRace);
            AgeId age = b.StyleAge;
            RoofStyle roof = ProceduralBuildingMeshes.ResolveRoof(def.Shape, style, age);
            bool odd = (b.Rotation & 1) != 0;
            int mw = MathUtil.Clamp(odd ? b.Height : b.Width, 1, 8);
            int mh = MathUtil.Clamp(odd ? b.Width : b.Height, 1, 8);
            float vh = def.VisualHeight;
            float sxz = 1f;
            SettlementTier tier = SettlementTier.Camp;
            if (b.Type == BuildingType.TownCenter)
            {
                tier = TierOf(b);
                int t = (int)tier;
                vh *= 1f + 0.3f * t;
                sxz = 1f + 0.05f * t;
            }
            var set = ProceduralBuildingMeshes.GetSet(def.Shape, roof, style, age, mw, mh, vh, b.Variant);
            v.Full = set.Full;
            v.Lod = set.Lod;
            v.TopHeight = set.TopHeight;
            v.ScaleXZ = sxz;
            v.Tier = tier;
            v.StyleAge = age;
            v.Level = b.Level;
            v.Rotation = b.Rotation;
            v.MeshW = mw;
            v.MeshH = mh;
            v.BodyMf.sharedMesh = v.UsingLod ? v.Lod : v.Full;
            v.ScaffoldMf.sharedMesh = null; // fetched lazily for the new size

            float top = Mathf.Max(0.1f * T, set.TopHeight);
            v.Collider.size = new Vector3(mw * T * 0.95f * sxz, top, mh * T * 0.95f * sxz);
            v.Collider.center = new Vector3(0f, top * 0.5f, 0f);
            v.Collider.enabled = true;

            if (v.HasBanner)
            {
                float bh = Mathf.Clamp(top * 0.6f, 0.6f * T, 2.2f * T);
                v.BannerT.localPosition = new Vector3(mw * T * 0.46f * sxz, 0f, -mh * T * 0.46f * sxz);
                v.BannerT.localScale = new Vector3(bh, bh, bh);
            }

            v.HasTint = def.TintColor != 0xFFFFFFFFu;
            v.BaseTint = v.HasTint ? GodSimPalette.FromRgba(def.TintColor) : Color.white;
            ApplyBaseTint(v);
        }

        private void Place(View v, Building b)
        {
            var map = _sim.State.Map;
            float cx = b.Origin.X + (b.Width - 1) * 0.5f, cy = b.Origin.Y + (b.Height - 1) * 0.5f;
            Vector3 p = GodSimCoords.TileToWorld(map, cx, cy);
            float sum = 0f, max = float.MinValue;
            int n = 0;
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    float h = GodSimCoords.SurfaceHeight(map, x, y);
                    sum += h;
                    if (h > max) max = h;
                    n++;
                }
            if (n > 0) p.y = v.Shape == BuildingShape.Dock ? max : sum / n;
            v.Position = p;
            v.RootT.SetPositionAndRotation(p, Quaternion.Euler(0f, (b.Rotation & 3) * 90f, 0f));
        }

        private void ApplyConstruction(View v)
        {
            if (v.IsRuin) return;
            bool site = ShowConstruction && !v.Complete;
            float prog = site ? v.Progress : 1f;
            v.BodyT.localPosition = Vector3.zero;
            v.BodyT.localScale = new Vector3(v.ScaleXZ, Mathf.Max(0.02f, prog), v.ScaleXZ);
            bool bodyVisible = !site || prog > 0.02f;
            if (v.BodyMr.enabled != bodyVisible) v.BodyMr.enabled = bodyVisible;
            bool scaffold = site && !v.UsingLod && v.Shape != BuildingShape.Field;
            if (scaffold && v.ScaffoldMf.sharedMesh == null)
                v.ScaffoldMf.sharedMesh = ProceduralBuildingMeshes.GetScaffold(v.MeshW, v.MeshH, v.TopHeight);
            if (scaffold) v.ScaffoldGo.transform.localScale = new Vector3(v.ScaleXZ, 1f, v.ScaleXZ);
            if (v.ScaffoldGo.activeSelf != scaffold) v.ScaffoldGo.SetActive(scaffold);
        }

        private void SetComplete(View v)
        {
            v.Complete = true;
            v.Progress = 1f;
            ListRemove(_incomplete, v, ListKind.Incomplete);
            ApplyConstruction(v);
        }

        private void SetLod(View v, bool lod)
        {
            v.UsingLod = lod;
            v.BodyMf.sharedMesh = lod ? v.Lod : v.Full;
            ApplyConstruction(v);
            ApplyBanner(v);
        }

        private void ApplyBanner(View v)
        {
            bool show = v.HasBanner && !v.UsingLod && !v.IsRuin;
            if (v.BannerGo.activeSelf != show) v.BannerGo.SetActive(show);
            if (!show) return;
            Color col = NoCivColor;
            var civ = _sim != null ? _sim.State.Civilizations.Get(v.CivId) : null;
            if (civ != null) col = GodSimPalette.FromRgba(civ.Color);
            _mpb.Clear();
            _mpb.SetColor(BaseColorId, col);
            _mpb.SetColor(ColorId, col);
            v.BannerMr.SetPropertyBlock(_mpb);
        }

        private void SetFire(View v, bool on)
        {
            if (v.OnFire == on) return;
            v.OnFire = on;
            if (on) ListAdd(_burning, v, ListKind.Burning);
            else
            {
                ListRemove(_burning, v, ListKind.Burning);
                ApplyBaseTint(v);
            }
        }

        private void ApplyBaseTint(View v)
        {
            if (v.OnFire) return; // the flicker owns the block while burning
            if (v.HasTint) SetBodyColor(v, v.BaseTint);
            else v.BodyMr.SetPropertyBlock(null); // no block: keeps the body SRP-batcher friendly
        }

        private void SetBodyColor(View v, Color c)
        {
            _mpb.Clear();
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            v.BodyMr.SetPropertyBlock(_mpb);
        }

        private void MakeRuin(View v, DestroyCause cause)
        {
            if (v.IsRuin) return;
            _views.Remove(v.Id);
            ListRemove(_active, v, ListKind.Active);
            ListRemove(_incomplete, v, ListKind.Incomplete);
            ListRemove(_burning, v, ListKind.Burning);
            v.OnFire = false;
            v.IsRuin = true;
            v.Building = null;
            v.Tag.BuildingId = -1;
            v.Collider.enabled = false;
            if (v.ScaffoldGo.activeSelf) v.ScaffoldGo.SetActive(false);
            if (v.BannerGo.activeSelf) v.BannerGo.SetActive(false);
            v.BodyMf.sharedMesh = ProceduralBuildingMeshes.GetRuin(v.MeshW, v.MeshH, v.StyleAge, v.Id);
            v.BodyT.localPosition = Vector3.zero;
            v.BodyT.localScale = new Vector3(v.ScaleXZ, 1f, v.ScaleXZ);
            v.BodyMr.enabled = true;
            bool burnt = cause == DestroyCause.Fire || cause == DestroyCause.Disaster || cause == DestroyCause.DivinePower || cause == DestroyCause.War;
            if (burnt) SetBodyColor(v, CharredTint);
            else if (v.HasTint) SetBodyColor(v, v.BaseTint);
            else v.BodyMr.SetPropertyBlock(null);
            v.RuinStart = Time.unscaledTime;
            ListAdd(_ruins, v, ListKind.Ruins);
        }

        // =============================================================================== pooling

        private View Rent()
        {
            return _pool.Count > 0 ? _pool.Pop() : CreateViewObjects();
        }

        private View CreateViewObjects()
        {
            var v = new View();
            var root = new GameObject("Building");
            v.Root = root;
            v.RootT = root.transform;
            v.RootT.SetParent(transform, false);
            v.Collider = root.AddComponent<BoxCollider>();
            v.Tag = root.AddComponent<BuildingViewTag>();

            var body = new GameObject("Body");
            v.BodyT = body.transform;
            v.BodyT.SetParent(v.RootT, false);
            v.BodyMf = body.AddComponent<MeshFilter>();
            v.BodyMr = body.AddComponent<MeshRenderer>();
            v.BodyMr.sharedMaterial = MaterialLibrary.VertexColorOpaque;
            v.BodyMr.shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

            v.ScaffoldGo = new GameObject("Scaffold");
            v.ScaffoldGo.transform.SetParent(v.RootT, false);
            v.ScaffoldMf = v.ScaffoldGo.AddComponent<MeshFilter>();
            v.ScaffoldMr = v.ScaffoldGo.AddComponent<MeshRenderer>();
            v.ScaffoldMr.sharedMaterial = MaterialLibrary.VertexColorOpaque;
            v.ScaffoldMr.shadowCastingMode = ShadowCastingMode.Off;

            v.BannerGo = new GameObject("Banner");
            v.BannerT = v.BannerGo.transform;
            v.BannerT.SetParent(v.RootT, false);
            v.BannerGo.AddComponent<MeshFilter>().sharedMesh = ProceduralBuildingMeshes.GetBanner();
            v.BannerMr = v.BannerGo.AddComponent<MeshRenderer>();
            v.BannerMr.sharedMaterial = MaterialLibrary.VertexColorInstanced;
            v.BannerMr.shadowCastingMode = ShadowCastingMode.Off;

            if (_layer >= 0)
            {
                root.layer = _layer;
                body.layer = _layer;
                v.ScaffoldGo.layer = _layer;
                v.BannerGo.layer = _layer;
            }
            v.ScaffoldGo.SetActive(false);
            v.BannerGo.SetActive(false);
            root.SetActive(false);
            return v;
        }

        private void Release(View v)
        {
            if (v.Root == null) return; // destroyed with the scene
            v.Root.SetActive(false);
            v.BodyMf.sharedMesh = null;
            v.ScaffoldMf.sharedMesh = null;
            v.BodyMr.SetPropertyBlock(null);
            v.BodyMr.enabled = true;
            v.BodyT.localPosition = Vector3.zero;
            v.BodyT.localScale = Vector3.one;
            v.Collider.enabled = true;
            v.Tag.BuildingId = -1;
            if (v.ScaffoldGo.activeSelf) v.ScaffoldGo.SetActive(false);
            if (v.BannerGo.activeSelf) v.BannerGo.SetActive(false);
            v.Building = null;
            v.Full = v.Lod = null;
            v.Id = 0;
            v.IsRuin = v.OnFire = v.UsingLod = v.Complete = v.HasTint = false;
            v.BaseTint = Color.white;
            v.ActiveIndex = v.IncompleteIndex = v.BurningIndex = v.RuinIndex = -1;
            if (_pool.Count < MaxPooledViews) _pool.Push(v);
            else Destroy(v.Root);
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < _active.Count; i++) Release(_active[i]);
            for (int i = 0; i < _ruins.Count; i++) Release(_ruins[i]);
            _active.Clear();
            _incomplete.Clear();
            _burning.Clear();
            _ruins.Clear();
            _views.Clear();
            _progressCursor = _progressRemaining = _syncCursor = _syncRemaining = _lodCursor = 0;
        }

        // Swap-remove lists with per-view index slots: O(1) add/remove, no allocations.
        private static ref int Slot(View v, ListKind k)
        {
            switch (k)
            {
                case ListKind.Active: return ref v.ActiveIndex;
                case ListKind.Incomplete: return ref v.IncompleteIndex;
                case ListKind.Burning: return ref v.BurningIndex;
                default: return ref v.RuinIndex;
            }
        }

        private static void ListAdd(List<View> list, View v, ListKind k)
        {
            ref int s = ref Slot(v, k);
            if (s >= 0) return;
            s = list.Count;
            list.Add(v);
        }

        private static void ListRemove(List<View> list, View v, ListKind k)
        {
            ref int s = ref Slot(v, k);
            int i = s;
            if (i < 0) return;
            int last = list.Count - 1;
            if (i != last)
            {
                View moved = list[last];
                list[i] = moved;
                Slot(moved, k) = i;
            }
            list.RemoveAt(last);
            s = -1;
        }
    }
}
