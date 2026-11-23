using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Auto-director ("spectator mode", toggled with C). Scores interest points from simulation events
    /// (battles, disasters, god powers, ages, wars, foundings, captures, wonders, important chronicle entries),
    /// decays them over time, and picks the best shot with a minimum dwell and hysteresis. Between events it
    /// shows establishing shots of the largest settlements. While active it owns the camera: it disables
    /// <see cref="GodCameraController"/>, drives the camera with damped orbit / push-in / crane / army-tracking /
    /// river-or-road flyover shots kept clear of the terrain, and draws letterbox bars with a caption.
    /// Any manual camera input (movement keys, wheel, right/middle mouse, Escape) exits and restores the pose.
    /// Main thread only; allocation-free per frame (strings are built only when an event arrives or a shot starts).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpectatorDirector : MonoBehaviour
    {
        /// <summary>Camera move used for a shot.</summary>
        public enum ShotKind : byte { Orbit, PushIn, Crane, Track, Flyover }

        private struct InterestPoint
        {
            public bool Used;
            public int Serial;
            public Vec2i Tile;
            public float Score;          // score at Stamp (decays with InterestHalfLifeSeconds)
            public float Stamp;          // unscaled time of the last boost
            public float Headline;       // largest single contribution: decides shot, focus and caption
            public ShotKind Shot;
            public float Radius;         // framing radius in tiles
            public int ArmyId;
            public int CivId;
            public string Caption;
            public bool CaptionFromChronicle;
        }

        private const int MaxPoints = 32;
        private const int MaxPath = 32;
        private const int MinPathTiles = 6;
        private const int MergeRadiusTiles = 5;
        private const float MaxPointScore = 40f;
        private const float DeadScore = 0.25f;
        private const float IdleScore = 1f;

        private static readonly KeyCode[] s_manualKeys =
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.PageUp, KeyCode.PageDown, KeyCode.Escape
        };

        /// <summary>The active director (null when none is enabled in the scene).</summary>
        public static SpectatorDirector Instance { get; private set; }

        [Header("Controls")]
        [Tooltip("Toggles spectator mode.")]
        public KeyCode ToggleKey = KeyCode.C;
        [Tooltip("Leave spectator mode when the player uses a camera control (movement keys, wheel, right/middle mouse, Escape).")]
        public bool ExitOnManualInput = true;
        [Tooltip("On a manual exit, re-focus the god camera on the current shot instead of restoring the saved pose.")]
        public bool StayAtShotOnManualExit;

        [Header("Direction")]
        [Range(3f, 20f)] public float MinDwellSeconds = 6f;
        [Range(4f, 30f)] public float MaxDwellSeconds = 12f;
        [Tooltip("A new shot must beat the current one by this factor (plus a small margin) to cut early.")]
        [Range(1f, 3f)] public float Hysteresis = 1.35f;
        [Range(2f, 120f)] public float InterestHalfLifeSeconds = 25f;
        [Tooltip("Below this score the director shows establishing shots of the largest settlements.")]
        [Range(0.5f, 10f)] public float IdleThreshold = 2f;
        [Range(1f, 30f)] public float ArmyScanSeconds = 4f;

        [Header("Camera")]
        [Range(0.1f, 5f)] public float PositionSmoothTime = 1.1f;
        [Range(0.1f, 5f)] public float TargetSmoothTime = 0.7f;
        [Tooltip("Shots whose first pose is farther than this (world units) hard-cut with a dip from black instead of gliding.")]
        public float CutDistance = 90f;
        [Range(0.2f, 20f)] public float TerrainClearance = 2.5f;
        [Range(15f, 90f)] public float FieldOfView = 45f;
        [Range(1f, 30f)] public float OrbitDegreesPerSecond = 7f;

        [Header("Overlay")]
        [Range(0f, 0.25f)] public float LetterboxFraction = 0.11f;
        public bool ShowCaptions = true;

        private readonly InterestPoint[] _points = new InterestPoint[MaxPoints];
        private readonly Vec2i[] _pathTiles = new Vec2i[MaxPath];
        private readonly Vector3[] _pathWorld = new Vector3[MaxPath];
        private readonly float[] _pathCum = new float[MaxPath];
        private readonly int[] _topSettlements = new int[4];
        private int _pathCount;
        private float _pathLength;
        private int _serial;

        // Cached delegates so subscribe/unsubscribe never allocate and always match.
        private Action<GodSimulation> _onSimCreated;
        private Action<BattleEvent> _onBattle;
        private Action<DisasterEvent> _onDisaster;
        private Action<GodPowerCastEvent> _onPower;
        private Action<AgeAdvancedEvent> _onAge;
        private Action<WarDeclaredEvent> _onWar;
        private Action<SettlementFoundedEvent> _onFounded;
        private Action<SettlementCapturedEvent> _onCaptured;
        private Action<BuildingCompletedEvent> _onBuilding;
        private Action<ChronicleAddedEvent> _onChronicle;

        private GodSimRunner _runner;
        private GodSimulation _sim;
        private WorldState _state;
        private GameDatabase _db;
        private EventBus _events;
        private WorldMap _map;
        private float _tileSize = 1f;
        private bool _duplicate;

        // Camera ownership & motion.
        private bool _active;
        private CinematicCameraLease _lease;
        private Camera _cam;
        private Transform _camT;
        private Vector3 _pos, _target, _posVel, _targetVel;
        private float _fovVel;
        private float _maxCamY = 500f;
        private bool _cutPending;
        private float _fade;
        private float _bars;

        // Current shot.
        private bool _shotValid;
        private ShotKind _kind;
        private Vec2i _focusTile;
        private Vector3 _focus;
        private Vector3 _heading = Vector3.forward;
        private float _radius;
        private float _shotElapsed;
        private float _shotDwell;
        private float _startAngle;
        private float _orbitSign = 1f;
        private int _shotCounter;
        private int _pointIndex = -1;
        private int _pointSerial;
        private int _armyId = -1;
        private bool _subjectLost;
        private float _idleScore;
        private string _caption;
        private int _idleCursor;
        private int _establishCycle;
        private float _nextArmyScan;

        // Overlay.
        private GUIStyle _captionStyle;
        private GUIStyle _hintStyle;
        private int _styleScreenHeight;
        private string _hint;

        /// <summary>True while the director drives the camera.</summary>
        public bool Active => _active;

        /// <summary>Kind of the current shot (meaningful while <see cref="Active"/>).</summary>
        public ShotKind CurrentShot => _kind;

        /// <summary>Caption of the current shot, or null.</summary>
        public string CurrentCaption => _active && _shotValid ? _caption : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GodSim] Duplicate SpectatorDirector on '" + name + "' removed.");
                _duplicate = true;
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
            _onSimCreated = OnSimulationCreated;
            _onBattle = OnBattle;
            _onDisaster = OnDisaster;
            _onPower = OnGodPower;
            _onAge = OnAgeAdvanced;
            _onWar = OnWarDeclared;
            _onFounded = OnSettlementFounded;
            _onCaptured = OnSettlementCaptured;
            _onBuilding = OnBuildingCompleted;
            _onChronicle = OnChronicle;
        }

        private void OnEnable()
        {
            if (_duplicate) return;
            if (Instance == null) Instance = this;
            SyncRunner();
        }

        private void OnDisable()
        {
            if (_duplicate) return;
            Exit(true, false);
            UnhookRunner();
            Detach();
        }

        private void OnDestroy()
        {
            if (_duplicate) return;
            Exit(true, false);
            UnhookRunner();
            Detach();
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ runner / world wiring

        private void SyncRunner()
        {
            if (_runner == null)
            {
                if (!ReferenceEquals(_runner, null)) UnhookRunner(); // destroyed runner
                GodSimRunner runner = GodSimRunner.Instance;
                if (runner != null)
                {
                    _runner = runner;
                    _runner.SimulationCreated += _onSimCreated;
                }
            }
            GodSimulation current = _runner != null ? _runner.Simulation : null;
            if (!ReferenceEquals(current, _sim)) OnSimulationCreated(current);
        }

        private void UnhookRunner()
        {
            if (!ReferenceEquals(_runner, null) && _onSimCreated != null) _runner.SimulationCreated -= _onSimCreated;
            _runner = null;
        }

        private void OnSimulationCreated(GodSimulation sim)
        {
            if (ReferenceEquals(sim, _sim)) return;
            // A new world invalidates every shot; hand the camera back without the old world's pose.
            Exit(false, false);
            Attach(sim);
        }

        private void Attach(GodSimulation sim)
        {
            Detach();
            if (sim == null || sim.State == null || sim.Events == null) return;
            _sim = sim;
            _state = sim.State;
            _db = sim.Db;
            _events = sim.Events;
            _map = _state.Map;
            _tileSize = CinematicUtil.TileSize(_map);
            _events.Subscribe(_onBattle);
            _events.Subscribe(_onDisaster);
            _events.Subscribe(_onPower);
            _events.Subscribe(_onAge);
            _events.Subscribe(_onWar);
            _events.Subscribe(_onFounded);
            _events.Subscribe(_onCaptured);
            _events.Subscribe(_onBuilding);
            _events.Subscribe(_onChronicle);
        }

        private void Detach()
        {
            if (_events != null)
            {
                _events.Unsubscribe(_onBattle);
                _events.Unsubscribe(_onDisaster);
                _events.Unsubscribe(_onPower);
                _events.Unsubscribe(_onAge);
                _events.Unsubscribe(_onWar);
                _events.Unsubscribe(_onFounded);
                _events.Unsubscribe(_onCaptured);
                _events.Unsubscribe(_onBuilding);
                _events.Unsubscribe(_onChronicle);
            }
            _sim = null;
            _state = null;
            _db = null;
            _events = null;
            _map = null;
            _shotValid = false;
            for (int i = 0; i < MaxPoints; i++) _points[i] = default;
        }

        // ------------------------------------------------------------------ public control

        /// <summary>Starts spectator mode. Returns false without a world or a camera, or while photo mode is active.</summary>
        public bool Enter()
        {
            if (_active) return true;
            if (PhotoMode.Active || _duplicate || !isActiveAndEnabled) return false;
            SyncRunner();
            if (_state == null) { GodSimToasts.Show("Spectator mode needs a running world.", new Color(1f, 0.6f, 0.4f), 3f); return false; }
            if (!CinematicCameraLease.TryAcquire(out _lease)) { GodSimToasts.Show("Spectator mode: no camera found.", new Color(1f, 0.6f, 0.4f), 3f); return false; }

            _active = true;
            _cam = _lease.Camera;
            _camT = _cam.transform;
            _pos = _camT.position;
            _target = _pos + _camT.forward * (20f * _tileSize);
            _posVel = Vector3.zero;
            _targetVel = Vector3.zero;
            _fovVel = 0f;
            _shotValid = false;
            _pointIndex = -1;
            _nextArmyScan = 0f;
            GodCameraController controller = _lease.Controller;
            _maxCamY = controller != null && controller.MaxHeight > 0f && !float.IsInfinity(controller.MaxHeight) ? controller.MaxHeight : 500f;
            _hint = "SPECTATOR   " + ToggleKey + " to exit";
            GodSimToasts.Show("Spectator mode on. Any camera input returns control.", new Color(0.85f, 0.9f, 1f), 3f);
            return true;
        }

        /// <summary>Leaves spectator mode, restoring the saved camera pose and re-enabling the god camera.</summary>
        public void Exit() => Exit(true, false);

        /// <summary>Toggles spectator mode.</summary>
        public void Toggle() { if (_active) Exit(); else Enter(); }

        /// <summary>
        /// Stops directing without restoring anything and hands the camera lease (original pose + controller state)
        /// to the caller, e.g. photo mode starting from the current cinematic framing.
        /// </summary>
        internal bool HandOff(out CinematicCameraLease lease)
        {
            lease = default;
            if (!_active) return false;
            lease = _lease;
            _lease = default;
            _active = false;
            _shotValid = false;
            _cam = null;
            _camT = null;
            return lease.IsValid;
        }

        private void Exit(bool restorePose, bool focusOnShot)
        {
            if (!_active) return;
            _active = false;
            GodCameraController controller = _lease.Controller;
            bool hadShot = _shotValid;
            Vector3 focus = _focus;
            _shotValid = false;
            _cam = null;
            _camT = null;
            _lease.Release(restorePose);
            if (focusOnShot && hadShot && controller != null && controller.enabled) controller.FocusOn(focus);
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            if (_duplicate) return;
            SyncRunner();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _bars = Mathf.MoveTowards(_bars, _active ? 1f : 0f, dt * 1.6f);
            _fade = Mathf.MoveTowards(_fade, 0f, dt * 2.5f);

            if (PhotoMode.Active) return;
            if (InputAdapter.GetKeyDown(ToggleKey)) { Toggle(); return; }
            if (_active && ExitOnManualInput && ManualCameraInput())
                Exit(!StayAtShotOnManualExit, StayAtShotOnManualExit);
        }

        private static bool ManualCameraInput()
        {
            for (int i = 0; i < s_manualKeys.Length; i++)
                if (InputAdapter.GetKeyDown(s_manualKeys[i])) return true;
            if (InputAdapter.PointerOverUI) return false;
            return InputAdapter.MouseDown(1) || InputAdapter.MouseDown(2) || InputAdapter.ScrollDelta != 0f;
        }

        private void LateUpdate()
        {
            if (!_active) return;
            if (_cam == null || _state == null) { Exit(false, false); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (!(dt > 0f)) return;
            float now = Time.unscaledTime;

            if (now >= _nextArmyScan) { _nextArmyScan = now + Mathf.Max(1f, ArmyScanSeconds); ScanArmies(now); }
            Direct(now);
            if (!_shotValid) return;

            _shotElapsed += dt;
            ComputePose(dt, out Vector3 wantPos, out Vector3 wantTarget);
            ConstrainCamera(ref wantPos, wantTarget);

            if (_cutPending)
            {
                _cutPending = false;
                _pos = wantPos;
                _target = wantTarget;
                _posVel = Vector3.zero;
                _targetVel = Vector3.zero;
            }
            else
            {
                _pos = Vector3.SmoothDamp(_pos, wantPos, ref _posVel, PositionSmoothTime, Mathf.Infinity, dt);
                _target = Vector3.SmoothDamp(_target, wantTarget, ref _targetVel, TargetSmoothTime, Mathf.Infinity, dt);
            }
            ConstrainCamera(ref _pos, _target);
            if (!CinematicUtil.IsFinite(_target)) _target = _focus;

            Vector3 dir = _target - _pos;
            if (dir.sqrMagnitude < 1e-4f) dir = _camT.forward;
            _camT.SetPositionAndRotation(_pos, Quaternion.LookRotation(dir, Vector3.up));
            _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, FieldOfView, ref _fovVel, 1f, Mathf.Infinity, dt);
        }

        private void ConstrainCamera(ref Vector3 p, Vector3 target)
        {
            CinematicUtil.ClampToMap(_map, _tileSize, ref p, 30f * _tileSize);
            CinematicUtil.ClearTerrain(_map, _tileSize, ref p, target, TerrainClearance);
            float cap = Mathf.Max(_maxCamY, target.y + 8f * _tileSize);
            if (p.y > cap) p.y = cap;
        }

        // ------------------------------------------------------------------ direction (shot selection)

        private void Direct(float now)
        {
            float bestScore;
            int best = BestPoint(now, out bestScore);
            if (!_shotValid) { StartNext(best, bestScore, now); return; }

            float minDwell = Mathf.Max(1f, MinDwellSeconds);
            bool lost = _subjectLost && _shotElapsed > 2f;
            if (_shotElapsed < minDwell && !lost) return;

            float current = CurrentScore(now);
            bool better = best >= 0 && bestScore >= IdleThreshold && bestScore > current * Hysteresis + 0.75f;
            if (better || lost || _shotElapsed >= _shotDwell) StartNext(best, bestScore, now);
        }

        private float Effective(ref InterestPoint p, float now)
        {
            float half = Mathf.Max(1f, InterestHalfLifeSeconds);
            float age = Mathf.Max(0f, now - p.Stamp);
            return p.Score * Mathf.Pow(0.5f, age / half);
        }

        private bool IsCurrentPoint(int i) => _shotValid && i == _pointIndex && _points[i].Used && _points[i].Serial == _pointSerial;

        private int BestPoint(float now, out float bestScore)
        {
            int best = -1;
            bestScore = 0f;
            for (int i = 0; i < MaxPoints; i++)
            {
                if (!_points[i].Used) continue;
                float s = Effective(ref _points[i], now);
                if (s < DeadScore && !IsCurrentPoint(i)) { _points[i] = default; continue; }
                if (IsCurrentPoint(i)) continue;
                if (s > bestScore) { bestScore = s; best = i; }
            }
            return best;
        }

        private float CurrentScore(float now)
        {
            if (_pointIndex >= 0 && IsCurrentPoint(_pointIndex)) return Effective(ref _points[_pointIndex], now);
            return _idleScore;
        }

        private void StartNext(int best, float bestScore, float now)
        {
            // The shot just shown is "consumed" so the director does not bounce straight back to it.
            if (_pointIndex >= 0 && IsCurrentPoint(_pointIndex))
            {
                ref InterestPoint shown = ref _points[_pointIndex];
                shown.Score = Effective(ref shown, now) * 0.15f;
                shown.Headline *= 0.15f;
                shown.Stamp = now;
            }
            _pointIndex = -1;

            if (best >= 0 && bestScore >= IdleThreshold) StartFromPoint(best, bestScore);
            else StartEstablishing();
        }

        private void StartFromPoint(int i, float score)
        {
            ref InterestPoint p = ref _points[i];
            _pointIndex = i;
            _pointSerial = p.Serial;
            _kind = p.Shot;
            _focusTile = p.Tile;
            _radius = p.Radius;
            _caption = p.Caption;
            _armyId = -1;
            _idleScore = IdleScore;
            if (_kind == ShotKind.Track)
            {
                _armyId = ArmyAlive(p.ArmyId) ? p.ArmyId : FindArmy(p.CivId, p.Tile);
                if (_armyId < 0) _kind = p.CivId >= 0 ? ShotKind.Crane : ShotKind.Orbit;
                else _focusTile = _state.Diplomacy.Armies.Get(_armyId).Position;
            }
            BeginShot(score);
        }

        private void StartEstablishing()
        {
            _pointIndex = -1;
            _armyId = -1;
            _idleScore = IdleScore;
            int count = CollectLargestSettlements();
            if (count == 0)
            {
                _focusTile = new Vec2i(_map.Width / 2, _map.Height / 2);
                _radius = Mathf.Min(_map.Width, _map.Height) * 0.3f;
                _kind = ShotKind.Crane;
                _caption = null;
                BeginShot(6f);
                return;
            }

            Settlement s = _state.Settlements.Get(_topSettlements[_idleCursor % count]);
            _idleCursor++;
            _focusTile = s.Center;
            _radius = Mathf.Clamp(s.TerritoryRadius, 4, 24);
            int cycle = _establishCycle++ % 3;
            if (cycle == 1 && TryBuildPath(s.Center, s.TerritoryRadius + 6)) _kind = ShotKind.Flyover;
            else _kind = cycle == 2 ? ShotKind.Orbit : ShotKind.Crane;

            Civilization civ = _state.Civilizations.Get(s.CivilizationId);
            string civName = civ != null && !string.IsNullOrEmpty(civ.Name) ? civ.Name : "an unclaimed people";
            _caption = s.Name + " - " + s.Tier + " of " + civName + ", population " + s.Population;
            BeginShot(6f);
        }

        private void BeginShot(float score)
        {
            _shotCounter++;
            _shotElapsed = 0f;
            _subjectLost = false;
            float minD = Mathf.Max(1f, MinDwellSeconds);
            float maxD = Mathf.Max(minD, MaxDwellSeconds);
            _shotDwell = Mathf.Lerp(minD, maxD, Mathf.Clamp01(score / 14f));
            _startAngle = CinematicUtil.ShotAngle(_focusTile, _shotCounter);
            _orbitSign = (_shotCounter & 1) == 0 ? 1f : -1f;
            _focus = GodSimCoords.TileToWorld(_map, _map.ClampToBounds(_focusTile));
            Vector3 fwd = _camT.forward;
            _heading = new Vector3(fwd.x, 0f, fwd.z);
            if (_heading.sqrMagnitude < 1e-4f) _heading = Vector3.forward;
            _heading.Normalize();
            if (_kind == ShotKind.Flyover && _pathCount < 2) _kind = ShotKind.Orbit;
            _shotValid = true;

            ComputePose(0f, out Vector3 firstPos, out Vector3 firstTarget);
            ConstrainCamera(ref firstPos, firstTarget);
            if ((firstPos - _pos).magnitude > CutDistance * _tileSize)
            {
                _cutPending = true;
                _fade = 1f;
            }
        }

        // ------------------------------------------------------------------ shot poses

        private void ComputePose(float dt, out Vector3 pos, out Vector3 target)
        {
            float ts = _tileSize;
            float t = _shotElapsed;
            float u = Mathf.Clamp01(t / Mathf.Max(0.1f, _shotDwell));
            float d0 = Mathf.Clamp((10f + _radius * 1.4f) * ts, 10f * ts, 70f * ts);
            Vector3 up = Vector3.up;

            switch (_kind)
            {
                case ShotKind.PushIn:
                {
                    float k = Mathf.SmoothStep(0f, 1f, u);
                    float d = Mathf.Lerp(d0 * 1.7f, d0 * 0.65f, k);
                    float a = (_startAngle + t * 1.5f * _orbitSign) * Mathf.Deg2Rad;
                    pos = _focus + new Vector3(Mathf.Cos(a) * d, d * 0.5f, Mathf.Sin(a) * d);
                    target = _focus + up * (0.5f * ts);
                    return;
                }
                case ShotKind.Crane:
                {
                    float k = Mathf.SmoothStep(0f, 1f, u);
                    float h = Mathf.Lerp(d0 * 0.3f, d0 * 1.5f, k);
                    float d = Mathf.Lerp(d0 * 0.8f, d0 * 1.3f, k);
                    float a = (_startAngle + t * 3f * _orbitSign) * Mathf.Deg2Rad;
                    pos = _focus + new Vector3(Mathf.Cos(a) * d, h, Mathf.Sin(a) * d);
                    target = _focus + up * (Mathf.Lerp(3f, 0f, k) * ts);
                    return;
                }
                case ShotKind.Track:
                {
                    Army army = _armyId >= 0 ? _state.Diplomacy.Armies.Get(_armyId) : null;
                    if (army != null && army.IsAlive && _map.InBounds(army.Position))
                    {
                        _focus = GodSimCoords.TileToWorld(_map, army.Position);
                        Vec2i dv = army.Destination - army.Position;
                        if ((dv.X != 0 || dv.Y != 0) && dt > 0f)
                        {
                            Vector3 h = new Vector3(dv.X, 0f, dv.Y).normalized;
                            _heading = Vector3.Lerp(_heading, h, Mathf.Clamp01(dt * 0.7f));
                            if (_heading.sqrMagnitude < 1e-4f) _heading = h;
                            _heading.Normalize();
                        }
                    }
                    else _subjectLost = true;
                    float d = d0 * 0.9f;
                    Vector3 side = new Vector3(_heading.z, 0f, -_heading.x);
                    pos = _focus - _heading * d + side * (d * 0.35f * Mathf.Sin(t * 0.25f)) + up * (d * 0.6f);
                    target = _focus + _heading * (3f * ts) + up * (0.5f * ts);
                    return;
                }
                case ShotKind.Flyover:
                {
                    float k = Mathf.Lerp(u, Mathf.SmoothStep(0f, 1f, u), 0.5f);
                    float s = k * _pathLength;
                    Vector3 p = SamplePath(s, out Vector3 dir);
                    Vector3 side = new Vector3(dir.z, 0f, -dir.x);
                    pos = p + up * (7f * ts) + side * (3f * ts) - dir * (4f * ts);
                    float ahead = s + 8f * ts;
                    target = ahead <= _pathLength ? SamplePath(ahead, out _) : p + dir * (8f * ts);
                    return;
                }
                default: // Orbit
                {
                    float a = (_startAngle + t * OrbitDegreesPerSecond * _orbitSign) * Mathf.Deg2Rad;
                    pos = _focus + new Vector3(Mathf.Cos(a) * d0, d0 * 0.55f, Mathf.Sin(a) * d0);
                    target = _focus + up * ts;
                    return;
                }
            }
        }

        private Vector3 SamplePath(float s, out Vector3 dir)
        {
            dir = Vector3.forward;
            if (_pathCount == 0) return _focus;
            if (_pathCount == 1) return _pathWorld[0];
            for (int i = 1; i < _pathCount; i++)
            {
                if (s <= _pathCum[i] || i == _pathCount - 1)
                {
                    Vector3 a = _pathWorld[i - 1], b = _pathWorld[i];
                    float seg = _pathCum[i] - _pathCum[i - 1];
                    float f = seg > 1e-4f ? Mathf.Clamp01((s - _pathCum[i - 1]) / seg) : 0f;
                    Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
                    if (flat.sqrMagnitude > 1e-6f) dir = flat.normalized;
                    return Vector3.Lerp(a, b, f);
                }
            }
            return _pathWorld[_pathCount - 1];
        }

        // ------------------------------------------------------------------ flyover path (river, else road)

        private bool TryBuildPath(Vec2i center, int searchRadius)
        {
            _pathCount = 0;
            _pathLength = 0f;
            if (_map == null) return false;
            int r = Mathf.Clamp(searchRadius, 4, 24);
            if (!TraceFlag(center, r, TileFlags.River) && !TraceFlag(center, r, TileFlags.Road)) { _pathCount = 0; return false; }

            for (int i = 0; i < _pathCount; i++) _pathWorld[i] = GodSimCoords.TileToWorld(_map, _pathTiles[i]);
            // Light smoothing of the staircase a tile path makes (in place, using the previous original point).
            Vector3 prev = _pathWorld[0];
            for (int i = 1; i < _pathCount - 1; i++)
            {
                Vector3 cur = _pathWorld[i];
                _pathWorld[i] = (prev + cur * 2f + _pathWorld[i + 1]) * 0.25f;
                prev = cur;
            }
            _pathCum[0] = 0f;
            for (int i = 1; i < _pathCount; i++) _pathCum[i] = _pathCum[i - 1] + (_pathWorld[i] - _pathWorld[i - 1]).magnitude;
            _pathLength = _pathCum[_pathCount - 1];
            return _pathLength > 2f * _tileSize;
        }

        private bool TraceFlag(Vec2i center, int r, TileFlags flag)
        {
            Vec2i start = Vec2i.Invalid;
            int bestD = int.MaxValue;
            for (int y = center.Y - r; y <= center.Y + r; y++)
                for (int x = center.X - r; x <= center.X + r; x++)
                {
                    if (!_map.InBounds(x, y) || !_map.Has(_map.Index(x, y), flag)) continue;
                    int dx = x - center.X, dy = y - center.Y, d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; start = new Vec2i(x, y); }
                }
            if (!start.IsValid) return false;

            _pathCount = 0;
            _pathTiles[_pathCount++] = start;
            Extend(flag);
            for (int a = 0, b = _pathCount - 1; a < b; a++, b--) { Vec2i tmp = _pathTiles[a]; _pathTiles[a] = _pathTiles[b]; _pathTiles[b] = tmp; }
            Extend(flag);
            return _pathCount >= MinPathTiles;
        }

        private void Extend(TileFlags flag)
        {
            Vec2i[] dirs = Vec2i.Dirs8;
            while (_pathCount < MaxPath)
            {
                Vec2i cur = _pathTiles[_pathCount - 1];
                Vec2i prevDir = _pathCount >= 2 ? cur - _pathTiles[_pathCount - 2] : Vec2i.Zero;
                int bestScore = int.MinValue;
                Vec2i best = Vec2i.Invalid;
                for (int i = 0; i < dirs.Length; i++)
                {
                    Vec2i n = cur + dirs[i];
                    if (!_map.InBounds(n) || !_map.Has(_map.Index(n), flag) || InPath(n)) continue;
                    int score = dirs[i].X * prevDir.X + dirs[i].Y * prevDir.Y;
                    if (score > bestScore) { bestScore = score; best = n; }
                }
                if (!best.IsValid) return;
                _pathTiles[_pathCount++] = best;
            }
        }

        private bool InPath(Vec2i t)
        {
            for (int i = 0; i < _pathCount; i++) if (_pathTiles[i] == t) return true;
            return false;
        }

        // ------------------------------------------------------------------ world queries

        private int CollectLargestSettlements()
        {
            int count = 0;
            var items = _state.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Settlement s = items[i];
                if (s == null || !s.IsAlive || s.Population <= 0 || !_map.InBounds(s.Center)) continue;
                int pos = count;
                while (pos > 0)
                {
                    Settlement other = _state.Settlements.Get(_topSettlements[pos - 1]);
                    bool before = other == null || s.Population > other.Population || (s.Population == other.Population && s.Id < other.Id);
                    if (!before) break;
                    pos--;
                }
                if (pos >= _topSettlements.Length) continue;
                int last = Mathf.Min(count, _topSettlements.Length - 1);
                for (int j = last; j > pos; j--) _topSettlements[j] = _topSettlements[j - 1];
                _topSettlements[pos] = s.Id;
                if (count < _topSettlements.Length) count++;
            }
            return count;
        }

        private bool ArmyAlive(int armyId)
        {
            if (armyId < 0 || _state == null) return false;
            Army a = _state.Diplomacy.Armies.Get(armyId);
            return a != null && a.IsAlive;
        }

        /// <summary>Strongest living army of a civ (marching ones preferred, then nearest to <paramref name="near"/>).</summary>
        private int FindArmy(int civId, Vec2i near)
        {
            if (civId < 0 || _state == null) return -1;
            int best = -1;
            float bestScore = float.MinValue;
            var armies = _state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                Army a = armies[i];
                if (a == null || !a.IsAlive || a.CivilizationId != civId) continue;
                float score = MathUtil.Sanitize(a.Strength) + (a.State == ArmyState.Marching ? 1000f : 0f) - (near.IsValid ? a.Position.DistanceTo(near) * 0.1f : 0f);
                if (score > bestScore) { bestScore = score; best = a.Id; }
            }
            return best;
        }

        private void ScanArmies(float now)
        {
            if (_state == null) return;
            Army best = null;
            var armies = _state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                Army a = armies[i];
                if (a == null || !a.IsAlive || a.State != ArmyState.Marching || a.WarId < 0 || !_map.InBounds(a.Position)) continue;
                if (best == null || a.Strength > best.Strength) best = a;
            }
            if (best == null) return;
            float score = 2.5f + Mathf.Clamp(MathUtil.Sanitize(best.Strength) * 0.01f, 0f, 3f);

            for (int i = 0; i < MaxPoints; i++)
            {
                if (!_points[i].Used || _points[i].ArmyId != best.Id) continue;
                ref InterestPoint p = ref _points[i];
                p.Tile = best.Position;
                p.Score = Mathf.Max(Effective(ref p, now), score);
                p.Stamp = now;
                return;
            }
            Civilization civ = _state.Civilizations.Get(best.CivilizationId);
            string caption = (civ != null ? civ.Name : "An") + " army on the march";
            AddInterest(best.Position, score, ShotKind.Track, 6f, caption, false, best.Id, best.CivilizationId);
        }

        private string SettlementName(int id)
        {
            Settlement s = id >= 0 && _state != null ? _state.Settlements.Get(id) : null;
            return s != null ? s.Name : null;
        }

        private string CivName(int id)
        {
            Civilization c = id >= 0 && _state != null ? _state.Civilizations.Get(id) : null;
            return c != null && !string.IsNullOrEmpty(c.Name) ? c.Name : "Unknown";
        }

        private bool CapitalTile(int civId, out Vec2i tile)
        {
            tile = Vec2i.Invalid;
            Civilization c = civId >= 0 && _state != null ? _state.Civilizations.Get(civId) : null;
            if (c == null) return false;
            Settlement s = _state.Settlements.Get(c.CapitalId);
            if (s == null || !s.IsAlive)
            {
                s = null;
                for (int i = 0; i < c.SettlementIds.Count && s == null; i++)
                {
                    Settlement cand = _state.Settlements.Get(c.SettlementIds[i]);
                    if (cand != null && cand.IsAlive) s = cand;
                }
            }
            if (s == null || !_map.InBounds(s.Center)) return false;
            tile = s.Center;
            return true;
        }

        private string NearestSettlementName(Vec2i p, int maxDist)
        {
            string best = null;
            int bestD = maxDist * maxDist + 1;
            var items = _state.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Settlement s = items[i];
                if (s == null || !s.IsAlive) continue;
                int d = s.Center.DistanceSqTo(p);
                if (d < bestD) { bestD = d; best = s.Name; }
            }
            return best;
        }

        // ------------------------------------------------------------------ interest intake

        private void AddInterest(Vec2i tile, float score, ShotKind shot, float radius, string caption, bool fromChronicle, int armyId, int civId)
        {
            if (_map == null || !_map.InBounds(tile) || !(score > 0f) || float.IsInfinity(score)) return;
            float now = Time.unscaledTime;
            int free = -1, weakest = -1;
            float weakestScore = float.MaxValue;
            for (int i = 0; i < MaxPoints; i++)
            {
                if (!_points[i].Used) { if (free < 0) free = i; continue; }
                ref InterestPoint p = ref _points[i];
                float eff = Effective(ref p, now);
                if (eff < DeadScore && !IsCurrentPoint(i)) { p = default; if (free < 0) free = i; continue; }
                if (p.Tile.ChebyshevTo(tile) <= MergeRadiusTiles)
                {
                    p.Score = Mathf.Min(MaxPointScore, eff + score);
                    p.Stamp = now;
                    if (score >= p.Headline)
                    {
                        p.Headline = score;
                        p.Shot = shot;
                        p.Radius = radius;
                        p.Tile = tile;
                        if (armyId >= 0) p.ArmyId = armyId;
                        if (civId >= 0) p.CivId = civId;
                        if (!p.CaptionFromChronicle && caption != null) p.Caption = caption;
                    }
                    if (fromChronicle && !string.IsNullOrEmpty(caption)) { p.Caption = caption; p.CaptionFromChronicle = true; }
                    if (IsCurrentPoint(i)) _caption = p.Caption;
                    return;
                }
                if (eff < weakestScore && !IsCurrentPoint(i)) { weakestScore = eff; weakest = i; }
            }
            int slot = free >= 0 ? free : (weakest >= 0 && score > weakestScore ? weakest : -1);
            if (slot < 0) return;
            _points[slot] = new InterestPoint
            {
                Used = true,
                Serial = ++_serial,
                Tile = tile,
                Score = Mathf.Min(MaxPointScore, score),
                Stamp = now,
                Headline = score,
                Shot = shot,
                Radius = radius,
                ArmyId = armyId,
                CivId = civId,
                Caption = caption,
                CaptionFromChronicle = fromChronicle && !string.IsNullOrEmpty(caption)
            };
        }

        private void OnBattle(BattleEvent e)
        {
            if (_state == null) return;
            float losses = Mathf.Max(0, e.AttackerLosses) + Mathf.Max(0, e.DefenderLosses);
            float score = 9f + Mathf.Min(10f, losses * 0.25f);
            string where = SettlementName(e.SettlementId) ?? NearestSettlementName(e.Position, 12);
            string caption = "Battle: " + CivName(e.AttackerCivId) + " against " + CivName(e.DefenderCivId) + (where != null ? " near " + where : "");
            AddInterest(e.Position, score, ShotKind.Orbit, 5f, caption, false, -1, e.AttackerCivId);
        }

        private void OnDisaster(DisasterEvent e)
        {
            if (_state == null) return;
            float score = 6f + Mathf.Clamp(MathUtil.Sanitize(e.Severity), 0f, 2f) * 3f;
            string where = NearestSettlementName(e.Position, 14);
            string caption = e.Type + (where != null ? " strikes " + where : " strikes the land");
            AddInterest(e.Position, score, ShotKind.PushIn, Mathf.Clamp(e.Radius, 2, 20), caption, false, -1, -1);
        }

        private void OnGodPower(GodPowerCastEvent e)
        {
            if (_state == null || !e.Success) return;
            float score = e.Power >= GodPowerType.Lightning ? 6f : (e.Power >= GodPowerType.RaiseLand ? 3.5f : 3f);
            string caption = !string.IsNullOrEmpty(e.Message) ? e.Message : "The god casts " + e.Power;
            AddInterest(e.Target, score, ShotKind.PushIn, Mathf.Clamp(e.Radius, 2, 20), caption, false, -1, -1);
        }

        private void OnAgeAdvanced(AgeAdvancedEvent e)
        {
            if (_state == null || !CapitalTile(e.CivilizationId, out Vec2i tile)) return;
            AddInterest(tile, 8f, ShotKind.Crane, 10f, CivName(e.CivilizationId) + " enters the " + e.NewAge + " Age", false, -1, e.CivilizationId);
        }

        private void OnWarDeclared(WarDeclaredEvent e)
        {
            if (_state == null || !CapitalTile(e.AttackerId, out Vec2i tile)) return;
            int army = FindArmy(e.AttackerId, tile);
            AddInterest(tile, 7f, ShotKind.Track, 8f, CivName(e.AttackerId) + " declares war on " + CivName(e.DefenderId), false, army, e.AttackerId);
        }

        private void OnSettlementFounded(SettlementFoundedEvent e)
        {
            if (_state == null) return;
            string name = SettlementName(e.SettlementId);
            AddInterest(e.Position, 4f, ShotKind.PushIn, 5f, (name ?? "A new settlement") + " is founded by " + CivName(e.CivilizationId), false, -1, e.CivilizationId);
        }

        private void OnSettlementCaptured(SettlementCapturedEvent e)
        {
            if (_state == null) return;
            Settlement s = _state.Settlements.Get(e.SettlementId);
            if (s == null) return;
            AddInterest(s.Center, 9f, ShotKind.Orbit, Mathf.Clamp(s.TerritoryRadius, 4, 16), s.Name + " falls to " + CivName(e.NewCivilizationId), false, -1, e.NewCivilizationId);
        }

        private void OnBuildingCompleted(BuildingCompletedEvent e)
        {
            if (_state == null || _db == null) return;
            if (!_db.TryGetBuilding(e.Type, out BuildingDefinition def) || def == null || !def.IsWonder) return;
            Building b = _state.Buildings.Get(e.BuildingId);
            Settlement s = _state.Settlements.Get(e.SettlementId);
            Vec2i tile = b != null ? b.Center : (s != null ? s.Center : Vec2i.Invalid);
            if (!tile.IsValid) return;
            string caption = "Wonder completed: " + def.Name + (s != null ? " in " + s.Name : "");
            AddInterest(tile, 9f, ShotKind.Orbit, 4f, caption, false, -1, s != null ? s.CivilizationId : -1);
        }

        private void OnChronicle(ChronicleAddedEvent e)
        {
            ChronicleEntry entry = e.Entry;
            if (_state == null || entry.Importance < 2) return;
            Vec2i tile = Vec2i.Invalid;
            Settlement s = entry.SettlementId >= 0 ? _state.Settlements.Get(entry.SettlementId) : null;
            if (s != null) tile = s.Center;
            else if (_map.InBounds(entry.Position) && (entry.Position.X != 0 || entry.Position.Y != 0)) tile = entry.Position;
            else if (!CapitalTile(entry.CivilizationId, out tile)) return;
            float score = 2f + Mathf.Min(entry.Importance, 3) * 2f;
            AddInterest(tile, score, entry.Importance >= 3 ? ShotKind.Orbit : ShotKind.PushIn, 6f, entry.Text, true, -1, entry.CivilizationId);
        }

        // ------------------------------------------------------------------ overlay (letterbox + caption)

        private void EnsureStyles()
        {
            int h = Screen.height;
            if (_captionStyle == null)
            {
                _captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = false };
                _captionStyle.normal.textColor = Color.white;
                _hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, wordWrap = false, richText = false };
                _hintStyle.normal.textColor = Color.white;
                _styleScreenHeight = -1;
            }
            if (_styleScreenHeight != h)
            {
                _styleScreenHeight = h;
                _captionStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(h * 0.026f), 13, 48);
                _hintStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(h * 0.015f), 10, 24);
            }
        }

        private void OnGUI()
        {
            if (_bars <= 0.001f || PhotoMode.Active) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            GUI.depth = -50;
            float w = Screen.width, h = Screen.height;
            float bar = Mathf.Round(h * LetterboxFraction * Mathf.SmoothStep(0f, 1f, _bars));
            Color prev = GUI.color;
            Texture2D white = Texture2D.whiteTexture;

            GUI.color = Color.black;
            if (bar > 0f)
            {
                GUI.DrawTexture(new Rect(0f, 0f, w, bar), white);
                GUI.DrawTexture(new Rect(0f, h - bar, w, bar), white);
            }
            if (_fade > 0.001f && _active)
            {
                GUI.color = new Color(0f, 0f, 0f, _fade);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), white);
            }

            if (_active && bar > 12f)
            {
                if (_hint != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.45f * _bars);
                    GUI.Label(new Rect(w * 0.05f, 0f, w * 0.9f, bar), _hint, _hintStyle);
                }
                if (ShowCaptions && _shotValid && !string.IsNullOrEmpty(_caption))
                {
                    float fadeIn = Mathf.Clamp01((_shotElapsed - 0.3f) / 0.8f);
                    float fadeOut = Mathf.Clamp01((_shotDwell - _shotElapsed) / 0.6f);
                    float a = Mathf.Min(fadeIn, fadeOut) * _bars;
                    if (a > 0.01f)
                    {
                        Rect r = new Rect(w * 0.08f, h - bar, w * 0.84f, bar);
                        GUI.color = new Color(0f, 0f, 0f, a * 0.8f);
                        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), _caption, _captionStyle);
                        GUI.color = new Color(1f, 0.96f, 0.86f, a);
                        GUI.Label(r, _caption, _captionStyle);
                    }
                }
            }
            GUI.color = prev;
        }
    }
}
