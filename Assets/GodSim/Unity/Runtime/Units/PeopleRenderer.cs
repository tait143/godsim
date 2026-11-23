using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Unity.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Draws up to <see cref="GodSimSettings.MaxVisiblePeople"/> living people as GPU-instanced procedural
    /// meeples tinted by their race's SkinColor (children at 0.6 scale).
    /// <para>
    /// The visible set (nearest the camera first) is rebuilt incrementally: one pass scans at most
    /// <see cref="ScanBudgetPerFrame"/> people per frame, bucketing them into distance bins, and a new pass
    /// starts every <see cref="RebuildEveryFrames"/> frames. Selection is O(n) and allocation-free.
    /// </para>
    /// <para>
    /// Animation is a pure function of person id, the shared day phase (<see cref="DayNightCycle.DayPhase"/>)
    /// and real time: workers walk home -> workplace -> home over the day, builders gather around an
    /// incomplete building of their settlement, homeless people wander near the town center, children play
    /// around home, and most people go indoors late at night. Soldiers in armies are hidden
    /// (<see cref="ArmyRenderer"/> draws them).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PeopleRenderer : SimulationBoundBehaviour
    {
        public static PeopleRenderer Instance { get; private set; }

        [Header("Look")]
        [Tooltip("Height of an adult meeple, in tiles.")]
        public float PersonHeight = 0.3f;
        [Tooltip("Walking speed multiplier for wander/bob animation.")]
        public float AnimationSpeed = 1f;
        public bool CastShadows = true;
        [Tooltip("Most people are hidden inside their home late at night.")]
        public bool HideIndoorsAtNight = true;

        [Header("Visible set")]
        [Tooltip("Used when the runner has no settings asset.")]
        public int FallbackMaxVisible = 4000;
        [Tooltip("Start a new visible-set pass every N frames.")]
        [Range(1, 120)] public int RebuildEveryFrames = 12;
        [Tooltip("People examined per frame while a pass is running (caps per-frame work).")]
        public int ScanBudgetPerFrame = 6000;
        [Tooltip("People farther than this many tiles from the camera focus are never drawn.")]
        public float MaxViewDistanceTiles = 160f;

        private const int Bins = 64;
        private const float LeaveHome = 0.27f, ArriveWork = 0.35f, LeaveWork = 0.68f, ArriveHome = 0.76f;

        private enum Mode : byte { Idle, Commuter, Builder, Homebody, Homeless }

        private struct Visible
        {
            public Person P;
            public Mode Mode;
            public float Ax, Ay, Bx, By;
            public float Scale;
            public Color Color;
            public float PhaseOffset;
            public float Lane;
            public float S1, S2, S3, S4;
            public bool GoesIndoors;
            public bool DayTrip;
        }

        private Visible[] _front = new Visible[0];
        private Visible[] _back = new Visible[0];
        private int _frontCount;

        private int[] _candIds = new int[1024];
        private byte[] _candBins = new byte[1024];
        private int _candCount;
        private readonly int[] _binCounts = new int[Bins];
        private bool _scanning;
        private int _scanCursor;
        private int _framesSincePass = int.MaxValue / 2;
        private float _scanFx, _scanFy, _scanMaxDist;
        private int _passStamp;

        private readonly Dictionary<int, List<int>> _incomplete = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int> _incompleteStamp = new Dictionary<int, int>();

        private readonly Color[] _raceColors = new Color[8];
        private InstanceBuffer _buffer;
        private Mesh _mesh;
        private MapFrame _frame;
        private float _ownPhase = 0.35f;

        /// <summary>People drawn in the last frame.</summary>
        public int DrawnCount { get; private set; }

        /// <summary>Size of the current visible set.</summary>
        public int VisibleSetCount => _frontCount;

        private void Awake()
        {
            _buffer = new InstanceBuffer(FallbackMaxVisible);
        }

        protected override void OnEnable()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("Multiple PeopleRenderer instances; the last enabled one is PeopleRenderer.Instance.", this);
            Instance = this;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }

        protected override void OnSimulationBound(GodSimulation sim)
        {
            _frame = MapFrame.From(sim.State.Map);
            for (int i = 0; i < _raceColors.Length; i++) _raceColors[i] = new Color(0.88f, 0.69f, 0.56f, 1f);
            RaceId[] races = EnumUtil.AllRaces;
            for (int i = 0; i < races.Length; i++)
            {
                int r = (int)races[i];
                if (r < 0 || r >= _raceColors.Length) continue;
                try
                {
                    RaceDefinition def = sim.Db != null ? sim.Db.Race(races[i]) : null;
                    if (def != null) { Color c = GodSimPalette.FromRgba(def.SkinColor); c.a = 1f; _raceColors[r] = c; }
                }
                catch (InvalidOperationException) { /* race not registered: keep fallback */ }
            }
            ResetVisibleSet();
        }

        protected override void OnSimulationUnbound(GodSimulation old)
        {
            ResetVisibleSet();
        }

        private void ResetVisibleSet()
        {
            for (int i = 0; i < _frontCount; i++) _front[i].P = null;
            for (int i = 0; i < _back.Length; i++) _back[i].P = null;
            _frontCount = 0;
            _candCount = 0;
            _scanning = false;
            _scanCursor = 0;
            _framesSincePass = int.MaxValue / 2;
            foreach (KeyValuePair<int, List<int>> kv in _incomplete) kv.Value.Clear();
            _incompleteStamp.Clear();
            DrawnCount = 0;
        }

        private int MaxVisible()
        {
            GodSimRunner r = Runner;
            int n = FallbackMaxVisible;
            if (r != null && r.Settings != null) n = r.Settings.MaxVisiblePeople;
            return Mathf.Clamp(n, 0, 100000);
        }

        private void LateUpdate()
        {
            if (!EnsureBound()) { DrawnCount = 0; return; }
            WorldState state = Sim.State;
            if (!ReferenceEquals(state.Map, _frame.Map)) { _frame = MapFrame.From(state.Map); ResetVisibleSet(); }

            Camera cam = Camera.main;
            if (cam == null) { DrawnCount = 0; return; }
            if (_mesh == null) _mesh = UnitMeshes.CreateMeeple();

            StepScan(state, cam);
            Draw(state);
        }

        // ------------------------------------------------------------------ visible set

        private void StepScan(WorldState state, Camera cam)
        {
            if (!_scanning)
            {
                _framesSincePass++;
                if (_framesSincePass < RebuildEveryFrames) return;
                _framesSincePass = 0;
                _scanning = true;
                _scanCursor = 0;
                _candCount = 0;
                Array.Clear(_binCounts, 0, Bins);
                Vector2 focus = _frame.ClampTile(_frame.WorldToTile(MapFrame.CameraFocus(cam, _frame.Origin.y)));
                _scanFx = focus.x;
                _scanFy = focus.y;
                _scanMaxDist = Mathf.Max(4f, MaxViewDistanceTiles);
                _passStamp++;
            }

            IReadOnlyList<Person> items = state.People.Items;
            int count = items.Count;
            int end = Math.Min(count, _scanCursor + Math.Max(256, ScanBudgetPerFrame));
            float maxD2 = _scanMaxDist * _scanMaxDist;
            float binScale = Bins / _scanMaxDist;
            for (int i = _scanCursor; i < end; i++)
            {
                Person p = items[i];
                if (p == null || !p.IsAlive || p.ArmyId >= 0) continue;
                float dx = p.Position.X - _scanFx, dy = p.Position.Y - _scanFy;
                float d2 = dx * dx + dy * dy;
                if (d2 > maxD2) continue;
                int bin = (int)(Mathf.Sqrt(d2) * binScale);
                if (bin >= Bins) bin = Bins - 1;
                if (_candCount >= _candIds.Length)
                {
                    Array.Resize(ref _candIds, _candIds.Length * 2);
                    Array.Resize(ref _candBins, _candBins.Length * 2);
                }
                _candIds[_candCount] = p.Id;
                _candBins[_candCount] = (byte)bin;
                _candCount++;
                _binCounts[bin]++;
            }
            _scanCursor = end;
            if (end >= count) FinishPass(state);
        }

        private void FinishPass(WorldState state)
        {
            _scanning = false;
            int cap = MaxVisible();
            if (_buffer.Capacity != cap) _buffer.Capacity = cap;

            int cutoffBin = Bins, remainAtCutoff = 0, cum = 0;
            for (int b = 0; b < Bins; b++)
            {
                if (cum + _binCounts[b] >= cap) { cutoffBin = b; remainAtCutoff = cap - cum; break; }
                cum += _binCounts[b];
            }

            int want = Math.Min(cap, _candCount);
            if (_back.Length < want) _back = new Visible[Math.Max(want, _back.Length * 2)];
            int n = 0;
            for (int k = 0; k < _candCount && n < want; k++)
            {
                int bin = _candBins[k];
                if (bin > cutoffBin) continue;
                if (bin == cutoffBin) { if (remainAtCutoff <= 0) continue; remainAtCutoff--; }
                if (BuildVisible(state, _candIds[k], ref _back[n])) n++;
            }
            for (int i = n; i < _frontCount && i < _back.Length; i++) _back[i].P = null;

            Visible[] tmp = _front;
            _front = _back;
            _back = tmp;
            for (int i = 0; i < _back.Length; i++) _back[i].P = null;
            _frontCount = n;
        }

        private bool BuildVisible(WorldState state, int id, ref Visible v)
        {
            Person p = state.People.Get(id);
            if (p == null || !p.IsAlive) return false;

            uint h = UnitMath.Hash(id);
            v.P = p;
            v.PhaseOffset = (UnitMath.Unit(h) - 0.5f) * 0.08f;
            v.Lane = UnitMath.Signed(UnitMath.Hash(id, 7)) * 0.3f;
            v.S1 = UnitMath.Unit(UnitMath.Hash(id, 11)) * UnitMath.TwoPi;
            v.S2 = UnitMath.Unit(UnitMath.Hash(id, 12)) * UnitMath.TwoPi;
            v.S3 = UnitMath.Unit(UnitMath.Hash(id, 13)) * UnitMath.TwoPi;
            v.S4 = UnitMath.Unit(UnitMath.Hash(id, 14)) * UnitMath.TwoPi;
            v.GoesIndoors = (h % 10u) < 7u;
            v.DayTrip = false;
            v.Scale = p.Stage == LifeStage.Child ? 0.6f : (p.Stage == LifeStage.Elder ? 0.95f : 1f);

            int race = (int)p.Race;
            Color skin = race >= 0 && race < _raceColors.Length ? _raceColors[race] : _raceColors[0];
            float shade = 0.9f + 0.18f * UnitMath.Unit(UnitMath.Hash(id, 3));
            v.Color = new Color(Mathf.Clamp01(skin.r * shade), Mathf.Clamp01(skin.g * shade), Mathf.Clamp01(skin.b * shade), 1f);

            Building home = p.HomeId >= 0 ? state.Buildings.Get(p.HomeId) : null;
            if (home != null && home.IsDestroyed) home = null;
            Settlement s = p.SettlementId >= 0 ? state.Settlements.Get(p.SettlementId) : null;
            if (s != null && !s.IsAlive) s = null;

            float hx = p.Position.X, hy = p.Position.Y;
            if (home != null) DoorPoint(home, h, out hx, out hy);
            float cx = hx, cy = hy;
            if (s != null) { cx = s.Center.X + UnitMath.Signed(UnitMath.Hash(id, 21)) * 1.5f; cy = s.Center.Y + UnitMath.Signed(UnitMath.Hash(id, 22)) * 1.5f; }

            v.Ax = hx; v.Ay = hy; v.Bx = hx; v.By = hy;

            if (p.IsAdult && p.Job == JobType.Builder && p.WorkplaceId < 0 && s != null)
            {
                Building site = PickConstructionSite(state, s, id);
                if (site != null)
                {
                    v.Mode = Mode.Builder;
                    if (home == null) { v.Ax = cx; v.Ay = cy; }
                    PerimeterPoint(site, UnitMath.Unit(UnitMath.Hash(id, 5)), 0.3f, out v.Bx, out v.By);
                    return true;
                }
            }

            if (p.IsAdult && p.WorkplaceId >= 0)
            {
                Building work = state.Buildings.Get(p.WorkplaceId);
                if (work != null && !work.IsDestroyed)
                {
                    v.Mode = Mode.Commuter;
                    if (home == null) { v.Ax = cx; v.Ay = cy; }
                    PerimeterPoint(work, UnitMath.Unit(UnitMath.Hash(id, 6)), 0.25f, out v.Bx, out v.By);
                    return true;
                }
            }

            if (home != null)
            {
                v.Mode = Mode.Homebody;
                if (p.IsAdult && s != null) { v.Bx = cx; v.By = cy; v.DayTrip = true; }
                return true;
            }

            if (s != null)
            {
                v.Mode = Mode.Homeless;
                v.Ax = cx; v.Ay = cy;
                v.GoesIndoors = false;
                return true;
            }

            v.Mode = Mode.Idle;
            v.GoesIndoors = false;
            return true;
        }

        private static void DoorPoint(Building b, uint h, out float x, out float y)
        {
            float cx = b.Origin.X + (b.Width - 1) * 0.5f;
            float cy = b.Origin.Y + (b.Height - 1) * 0.5f;
            float jitter = UnitMath.Signed(h >> 3) * 0.25f;
            switch (b.Rotation & 3)
            {
                case 0: x = cx + jitter; y = b.Origin.Y - 0.6f; break;
                case 1: x = b.Origin.X - 0.6f; y = cy + jitter; break;
                case 2: x = cx + jitter; y = b.Origin.Y + b.Height - 0.4f; break;
                default: x = b.Origin.X + b.Width - 0.4f; y = cy + jitter; break;
            }
        }

        private static void PerimeterPoint(Building b, float u, float margin, out float x, out float y)
        {
            float cx = b.Origin.X + (b.Width - 1) * 0.5f;
            float cy = b.Origin.Y + (b.Height - 1) * 0.5f;
            float a = u * UnitMath.TwoPi;
            x = cx + Mathf.Cos(a) * (b.Width * 0.5f + margin);
            y = cy + Mathf.Sin(a) * (b.Height * 0.5f + margin);
        }

        private Building PickConstructionSite(WorldState state, Settlement s, int personId)
        {
            List<int> list;
            int stamp;
            if (!_incomplete.TryGetValue(s.Id, out list))
            {
                list = new List<int>(8);
                _incomplete[s.Id] = list;
                stamp = -1;
            }
            else if (!_incompleteStamp.TryGetValue(s.Id, out stamp)) stamp = -1;

            if (stamp != _passStamp)
            {
                list.Clear();
                List<int> ids = s.BuildingIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    Building b = state.Buildings.Get(ids[i]);
                    if (b != null && !b.IsDestroyed && !b.IsComplete) list.Add(b.Id);
                }
                _incompleteStamp[s.Id] = _passStamp;
            }
            if (list.Count == 0) return null;
            Building site = state.Buildings.Get(list[(int)(UnitMath.Hash(personId, 9) % (uint)list.Count)]);
            return site != null && !site.IsDestroyed ? site : null;
        }

        // ------------------------------------------------------------------ drawing

        private float DayPhase()
        {
            DayNightCycle dnc = DayNightCycle.Instance;
            if (dnc != null && dnc.isActiveAndEnabled) return dnc.DayPhase;
            GodSimRunner r = Runner;
            float tps = r != null ? r.TicksPerSecond : 0f;
            float factor = tps > 0f ? DayNightCycle.SpeedFactorFor(tps, 4f, 0.6f, 3f) : 0f;
            _ownPhase = UnitMath.Frac(_ownPhase + Time.deltaTime * factor / 180f);
            return _ownPhase;
        }

        private void Draw(WorldState state)
        {
            WorldMap map = state.Map;
            float phase = DayPhase();
            float time = Time.time * Mathf.Max(0f, AnimationSpeed);
            float size = Mathf.Max(0.01f, PersonHeight) * _frame.TileSize;
            bool hideAtNight = HideIndoorsAtNight;

            _buffer.Clear();
            for (int i = 0; i < _frontCount; i++)
            {
                Person p = _front[i].P;
                if (p == null || !p.IsAlive || p.ArmyId >= 0) continue;

                float x, y, dx, dy;
                bool walking;
                if (!Animate(ref _front[i], phase, time, hideAtNight, out x, out y, out dx, out dy, out walking)) continue;

                Vector3 pos = GodSimCoords.TileToWorld(map, x, y);
                float s = _front[i].Scale * size;
                if (walking) pos.y += Mathf.Abs(Mathf.Sin(time * 9f + _front[i].S1)) * 0.07f * s;
                Vector3 wd = _frame.TileDirToWorld(dx, dy);
                float yaw = (wd.x * wd.x + wd.z * wd.z) > 1e-8f ? UnitMath.YawOf(wd.x, wd.z) : _front[i].S2 * Mathf.Rad2Deg;
                Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(s, s, s));
                if (!_buffer.Add(m, _front[i].Color)) break;
            }
            DrawnCount = _buffer.Count;
            _buffer.Draw(_mesh, MaterialLibrary.VertexColorInstanced, CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off, true, gameObject.layer);
        }

        /// <summary>Tile-space position + facing for a person. Returns false when hidden (indoors).</summary>
        private static bool Animate(ref Visible v, float phase, float time, bool hideAtNight,
            out float x, out float y, out float dx, out float dy, out bool walking)
        {
            float ph = UnitMath.Frac(phase + v.PhaseOffset);
            bool lateNight = ph < 0.2f || ph > 0.9f;
            switch (v.Mode)
            {
                case Mode.Commuter:
                case Mode.Builder:
                    if (ph >= LeaveHome && ph < ArriveWork) { Walk(ref v, v.Ax, v.Ay, v.Bx, v.By, (ph - LeaveHome) / (ArriveWork - LeaveHome), out x, out y, out dx, out dy); walking = true; return true; }
                    if (ph >= ArriveWork && ph < LeaveWork)
                    {
                        Wander(ref v, v.Bx, v.By, v.Mode == Mode.Builder ? 0.2f : 0.45f, time, out x, out y, out dx, out dy, out walking);
                        if (v.Mode == Mode.Builder) walking = true; // hammering bob
                        return true;
                    }
                    if (ph >= LeaveWork && ph < ArriveHome) { Walk(ref v, v.Bx, v.By, v.Ax, v.Ay, (ph - LeaveWork) / (ArriveHome - LeaveWork), out x, out y, out dx, out dy); walking = true; return true; }
                    break;
                case Mode.Homebody:
                    if (v.DayTrip)
                    {
                        if (ph >= 0.3f && ph < 0.36f) { Walk(ref v, v.Ax, v.Ay, v.Bx, v.By, (ph - 0.3f) / 0.06f, out x, out y, out dx, out dy); walking = true; return true; }
                        if (ph >= 0.36f && ph < 0.62f) { Wander(ref v, v.Bx, v.By, 1.2f, time, out x, out y, out dx, out dy, out walking); return true; }
                        if (ph >= 0.62f && ph < 0.68f) { Walk(ref v, v.Bx, v.By, v.Ax, v.Ay, (ph - 0.62f) / 0.06f, out x, out y, out dx, out dy); walking = true; return true; }
                    }
                    else if (ph >= 0.3f && ph < 0.75f)
                    {
                        Wander(ref v, v.Ax, v.Ay, 1.6f, time * 1.3f, out x, out y, out dx, out dy, out walking);
                        return true;
                    }
                    break;
                case Mode.Homeless:
                    Wander(ref v, v.Ax, v.Ay, lateNight ? 0.8f : 2.2f, time, out x, out y, out dx, out dy, out walking);
                    return true;
                default:
                    Wander(ref v, v.Ax, v.Ay, 0.6f, time, out x, out y, out dx, out dy, out walking);
                    return true;
            }

            // At home (night / evening / morning).
            if (hideAtNight && lateNight && v.GoesIndoors) { x = y = dx = dy = 0f; walking = false; return false; }
            Wander(ref v, v.Ax, v.Ay, 0.35f, time * 0.6f, out x, out y, out dx, out dy, out walking);
            return true;
        }

        private static void Walk(ref Visible v, float ax, float ay, float bx, float by, float t,
            out float x, out float y, out float dx, out float dy)
        {
            float s = UnitMath.Smooth01(t);
            dx = bx - ax;
            dy = by - ay;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            float lane = len > 1e-4f ? v.Lane * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) / len : 0f;
            x = ax + dx * s - dy * lane;
            y = ay + dy * s + dx * lane;
        }

        private static void Wander(ref Visible v, float cx, float cy, float radius, float time,
            out float x, out float y, out float dx, out float dy, out bool walking)
        {
            float t = time * 0.35f;
            float k = radius / 1.5f;
            x = cx + (Mathf.Sin(t + v.S1) + 0.5f * Mathf.Sin(t * 2.3f + v.S2)) * k;
            y = cy + (Mathf.Cos(t * 0.8f + v.S3) + 0.5f * Mathf.Sin(t * 1.9f + v.S4)) * k;
            dx = (Mathf.Cos(t + v.S1) + 1.15f * Mathf.Cos(t * 2.3f + v.S2)) * k;
            dy = (-0.8f * Mathf.Sin(t * 0.8f + v.S3) + 0.95f * Mathf.Cos(t * 1.9f + v.S4)) * k;
            walking = (dx * dx + dy * dy) * 0.1225f > 0.0025f;
        }
    }
}
