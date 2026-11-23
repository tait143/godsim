using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// God / RTS camera: a ground pivot with an orbit rig (yaw, zoom-dependent pitch, height above the
    /// pivot). Controls: WASD / arrows pan (speed scales with height, Shift = fast), screen-edge scroll
    /// (toggleable), middle-mouse drag pan (grab the ground), mouse wheel zoom (smooth, exponential,
    /// toward the cursor), Q / E or right-drag rotate, PageUp / PageDown zoom. Everything is damped,
    /// clamped to the map and runs on unscaled time (works while paused). Mouse input is ignored while
    /// the pointer is over the HUD. Camera shake: call <see cref="AddShake"/> (integration wires the VFX event).
    /// <para>The camera is this object's Camera, a child Camera, Camera.main, or a created one (in that order).
    /// When the camera is not this object (or its parent), this transform is moved to the pivot.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("GodSim/God Camera Controller")]
    public sealed class GodCameraController : MonoBehaviour
    {
        /// <summary>The active controller (last enabled), or null.</summary>
        public static GodCameraController Instance { get; private set; }

        /// <summary>Closest allowed camera height above the pivot (world units).</summary>
        public float MinHeight = 8f;
        /// <summary>Farthest allowed camera height above the pivot (world units).</summary>
        public float MaxHeight = 160f;

        [Header("Start")]
        [SerializeField] private float _startHeight = 70f;
        [SerializeField] private float _startYaw = 0f;

        [Header("Pan")]
        [Tooltip("Pan speed in world units per second per unit of camera height.")]
        [SerializeField] private float _panSpeed = 1.1f;
        [SerializeField] private float _fastPanMultiplier = 2.5f;
        [SerializeField] private bool _edgeScroll = true;
        [SerializeField] private float _edgeScrollBorder = 12f;
        [SerializeField] private float _panSharpness = 12f;

        [Header("Zoom")]
        [Tooltip("Height factor per wheel notch.")]
        [SerializeField] private float _zoomStep = 1.18f;
        [SerializeField] private float _zoomSharpness = 9f;
        [SerializeField] private bool _zoomTowardCursor = true;
        [SerializeField] private float _keyZoomNotchesPerSecond = 6f;

        [Header("Rotate")]
        [SerializeField] private float _keyRotateSpeed = 90f;
        [SerializeField] private float _dragRotateSpeed = 0.3f;
        [SerializeField] private float _rotateSharpness = 12f;

        [Header("Pitch (degrees below horizon)")]
        [SerializeField] private float _pitchAtMinHeight = 30f;
        [SerializeField] private float _pitchAtMaxHeight = 68f;

        [Header("Bounds & terrain")]
        [SerializeField] private float _boundsMargin = 0f;
        [SerializeField] private float _groundFollowSharpness = 5f;
        [SerializeField] private float _minClearance = 2f;

        [Header("Focus")]
        [SerializeField] private float _focusDuration = 0.6f;
        [Tooltip("Focusing from higher than this zooms in to it (0 = keep zoom).")]
        [SerializeField] private float _focusHeight = 45f;

        [Header("Shake")]
        [SerializeField] private float _maxShakeOffset = 0.9f;
        [SerializeField] private float _maxShakeRoll = 2.5f;
        [SerializeField] private float _shakeDecayPerSecond = 1.3f;
        [SerializeField] private float _shakeFrequency = 22f;

        private Camera _cam;
        private bool _createdCamera;

        private Vector3 _pivot;
        private Vector3 _targetPivot;
        private float _yaw, _targetYaw;
        private float _height, _targetHeight;

        private GodSimulation _lastSim;
        private WorldMap _lastMap;

        private bool _focusing;
        private Vector3 _focusFrom, _focusTo;
        private float _focusT;

        private bool _middleDragging;
        private Vector3 _dragAnchor;
        private float _dragPlaneY;
        private bool _rightDragging;
        private Vector2 _lastMouse;

        private float _trauma;
        private float _shakeTime;

        /// <summary>The controlled camera (resolved in Awake).</summary>
        public Camera Camera
        {
            get
            {
                if (_cam == null) _cam = FindCamera();
                return _cam;
            }
        }

        /// <summary>Enables / disables screen-edge scrolling (also settable in the inspector).</summary>
        public bool EdgeScrollEnabled
        {
            get { return _edgeScroll; }
            set { _edgeScroll = value; }
        }

        /// <summary>Current (damped) camera height above the pivot.</summary>
        internal float CurrentHeight => _height;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            _cam = FindCamera();
            if (_cam == null)
            {
                var go = new GameObject("GodCamera");
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
                _cam.nearClipPlane = 0.3f;
                _cam.farClipPlane = 3000f;
                _createdCamera = true;
            }
            SanitizeSettings();
            _height = _targetHeight = Mathf.Clamp(_startHeight, MinHeight, MaxHeight);
            _yaw = _targetYaw = _startYaw;
            // Start from wherever the scene camera looks until a world exists.
            Vector3 p;
            Transform ct = _cam.transform;
            if (InteractionUtil.RayToPlane(new Ray(ct.position, ct.forward), 0f, out p)) _pivot = p;
            else _pivot = new Vector3(ct.position.x, 0f, ct.position.z);
            _targetPivot = _pivot;
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("GodCameraController: more than one active instance; the latest enabled one is used.");
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            _middleDragging = _rightDragging = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_createdCamera && _cam != null) Destroy(_cam.gameObject);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Smoothly moves the pivot to the center of a tile (clamped to the map).</summary>
        public void FocusOn(Vec2i tile)
        {
            WorldMap map = InteractionUtil.Map;
            Vector3 world;
            if (map != null) world = GodSimCoords.TileToWorld(map, map.ClampToBounds(tile));
            else if (tile.IsValid) world = new Vector3((tile.X + 0.5f) * GodSimCoords.TileSize, 0f, (tile.Y + 0.5f) * GodSimCoords.TileSize);
            else return;
            FocusOn(world);
        }

        /// <summary>Smoothly moves the pivot to a world position (XZ; clamped to the map).</summary>
        public void FocusOn(Vector3 world)
        {
            if (!IsFinite(world.x) || !IsFinite(world.z)) return;
            _focusFrom = _pivot;
            _focusTo = new Vector3(world.x, 0f, world.z);
            ClampToMap(ref _focusTo, InteractionUtil.Map);
            _focusT = 0f;
            _focusing = true;
            _middleDragging = false;
            if (_focusHeight > 0f && _targetHeight > _focusHeight)
                _targetHeight = Mathf.Clamp(_focusHeight, MinHeight, MaxHeight);
        }

        /// <summary>
        /// Adds camera shake "trauma" (about 0..1 per event; values are clamped and the total saturates
        /// at 1). Offset grows with the square of the trauma and decays over time.
        /// Signature matches <c>Action&lt;float&gt;</c> so it can subscribe to <c>GodPowerVFX.CameraShake</c>.
        /// </summary>
        public void AddShake(float intensity)
        {
            if (!(intensity > 0f) || float.IsInfinity(intensity)) return; // rejects NaN / negative / inf
            _trauma = Mathf.Clamp01(_trauma + Mathf.Min(intensity, 1f));
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            Camera cam = Camera;
            if (cam == null) return;
            SanitizeSettings();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            GodSimulation sim = InteractionUtil.Simulation;
            WorldMap map = sim != null && sim.State != null ? sim.State.Map : null;
            if (sim != _lastSim || map != _lastMap)
            {
                _lastSim = sim;
                _lastMap = map;
                if (map != null) Recenter(map);
            }

            HandleInput(cam, dt, map);
            Integrate(dt, map);
            Apply(cam, dt, map);
        }

        private void HandleInput(Camera cam, float dt, WorldMap map)
        {
            Vector2 mouse = InputAdapter.MousePosition;
            bool overUI = InputAdapter.PointerOverUI;
            bool mouseInWorld = !overUI && InteractionUtil.PointerInScreen(mouse);

            // ---- keyboard + edge pan
            float mx = 0f, my = 0f;
            if (InputAdapter.GetKey(KeyCode.W) || InputAdapter.GetKey(KeyCode.UpArrow)) my += 1f;
            if (InputAdapter.GetKey(KeyCode.S) || InputAdapter.GetKey(KeyCode.DownArrow)) my -= 1f;
            if (InputAdapter.GetKey(KeyCode.D) || InputAdapter.GetKey(KeyCode.RightArrow)) mx += 1f;
            if (InputAdapter.GetKey(KeyCode.A) || InputAdapter.GetKey(KeyCode.LeftArrow)) mx -= 1f;
            if (_edgeScroll && mouseInWorld && Application.isFocused && !_middleDragging && !_rightDragging)
            {
                float b = _edgeScrollBorder;
                if (mouse.x <= b) mx -= 1f; else if (mouse.x >= Screen.width - 1 - b) mx += 1f;
                if (mouse.y <= b) my -= 1f; else if (mouse.y >= Screen.height - 1 - b) my += 1f;
            }
            if (mx != 0f || my != 0f)
            {
                float len = Mathf.Sqrt(mx * mx + my * my);
                if (len > 1f) { mx /= len; my /= len; }
                float speed = _panSpeed * _height * (InteractionUtil.ShiftHeld ? _fastPanMultiplier : 1f) * dt;
                float yawRad = _yaw * Mathf.Deg2Rad;
                float sin = Mathf.Sin(yawRad), cos = Mathf.Cos(yawRad);
                // forward = (sin, 0, cos), right = (cos, 0, -sin)
                _targetPivot.x += (cos * mx + sin * my) * speed;
                _targetPivot.z += (-sin * mx + cos * my) * speed;
                _focusing = false;
            }

            // ---- middle-mouse grab pan
            if (InputAdapter.MouseDown(2) && mouseInWorld)
            {
                _dragPlaneY = _pivot.y;
                Vector3 anchor;
                if (InteractionUtil.RayToPlane(ScreenRay(cam, mouse), _dragPlaneY, out anchor))
                {
                    _middleDragging = true;
                    _dragAnchor = anchor;
                    _focusing = false;
                }
            }
            if (_middleDragging)
            {
                if (!InputAdapter.MouseHeld(2)) _middleDragging = false;
                else
                {
                    Vector3 p;
                    if (InteractionUtil.RayToPlane(ScreenRay(cam, mouse), _dragPlaneY, out p))
                    {
                        _pivot.x += _dragAnchor.x - p.x;
                        _pivot.z += _dragAnchor.z - p.z;
                        ClampToMap(ref _pivot, map);
                        _targetPivot.x = _pivot.x;
                        _targetPivot.z = _pivot.z;
                    }
                }
            }

            // ---- rotation
            if (InputAdapter.GetKey(KeyCode.Q)) _targetYaw -= _keyRotateSpeed * dt;
            if (InputAdapter.GetKey(KeyCode.E)) _targetYaw += _keyRotateSpeed * dt;
            if (InputAdapter.MouseDown(1) && mouseInWorld)
            {
                _rightDragging = true;
                _lastMouse = mouse;
            }
            if (_rightDragging)
            {
                if (!InputAdapter.MouseHeld(1)) _rightDragging = false;
                else
                {
                    _targetYaw += (mouse.x - _lastMouse.x) * _dragRotateSpeed;
                    _lastMouse = mouse;
                }
            }
            if (_targetYaw > 3600f || _targetYaw < -3600f)
            {
                float wrap = Mathf.Floor(_targetYaw / 360f) * 360f;
                _targetYaw -= wrap;
                _yaw -= wrap;
            }

            // ---- zoom (Ctrl+wheel belongs to the god-power radius while a power is selected)
            float notches = 0f;
            float scroll = InputAdapter.ScrollDelta;
            if (scroll != 0f && mouseInWorld && !RadiusWheelActive()) notches += scroll;
            if (InputAdapter.GetKey(KeyCode.PageUp)) notches += _keyZoomNotchesPerSecond * dt;
            if (InputAdapter.GetKey(KeyCode.PageDown)) notches -= _keyZoomNotchesPerSecond * dt;
            if (notches != 0f)
            {
                float old = _targetHeight;
                _targetHeight = Mathf.Clamp(old * Mathf.Pow(_zoomStep, -notches), MinHeight, MaxHeight);
                if (_zoomTowardCursor && scroll != 0f && _targetHeight < old && mouseInWorld)
                {
                    Vector3 p;
                    if (InteractionUtil.RayToPlane(ScreenRay(cam, mouse), _pivot.y, out p))
                    {
                        float f = 1f - _targetHeight / old;
                        _targetPivot.x += (p.x - _targetPivot.x) * f;
                        _targetPivot.z += (p.z - _targetPivot.z) * f;
                    }
                }
                _focusing = false;
            }
        }

        private void Integrate(float dt, WorldMap map)
        {
            if (_focusing)
            {
                _focusT += dt / Mathf.Max(0.01f, _focusDuration);
                float s = Mathf.SmoothStep(0f, 1f, _focusT);
                _targetPivot.x = Mathf.LerpUnclamped(_focusFrom.x, _focusTo.x, s);
                _targetPivot.z = Mathf.LerpUnclamped(_focusFrom.z, _focusTo.z, s);
                if (_focusT >= 1f) _focusing = false;
            }
            ClampToMap(ref _targetPivot, map);

            float kPan = _focusing ? 1f : Damp(_panSharpness, dt);
            _pivot.x = Mathf.LerpUnclamped(_pivot.x, _targetPivot.x, kPan);
            _pivot.z = Mathf.LerpUnclamped(_pivot.z, _targetPivot.z, kPan);
            ClampToMap(ref _pivot, map);

            float ground = map != null ? InteractionUtil.SurfaceAt(map, _pivot.x, _pivot.z) : 0f;
            _pivot.y = Mathf.LerpUnclamped(_pivot.y, ground, Damp(_groundFollowSharpness, dt));

            _yaw = Mathf.LerpUnclamped(_yaw, _targetYaw, Damp(_rotateSharpness, dt));

            _targetHeight = Mathf.Clamp(_targetHeight, MinHeight, MaxHeight);
            float logH = Mathf.LerpUnclamped(Mathf.Log(Mathf.Max(_height, 0.01f)), Mathf.Log(_targetHeight), Damp(_zoomSharpness, dt));
            _height = Mathf.Clamp(Mathf.Exp(logH), MinHeight, MaxHeight);

            if (!IsFinite(_pivot.x) || !IsFinite(_pivot.y) || !IsFinite(_pivot.z)) _pivot = _targetPivot = Vector3.zero;
            if (!IsFinite(_yaw)) _yaw = _targetYaw = 0f;
            if (!IsFinite(_height)) _height = _targetHeight = MaxHeight;
        }

        private void Apply(Camera cam, float dt, WorldMap map)
        {
            float t = Mathf.InverseLerp(MinHeight, MaxHeight, _height);
            t = Mathf.Sqrt(t); // steepen quickly when leaving the ground, flatten only when close
            float pitch = Mathf.Lerp(_pitchAtMinHeight, _pitchAtMaxHeight, t);
            Quaternion rot = Quaternion.Euler(pitch, _yaw, 0f);
            float dist = _height / Mathf.Max(0.1f, Mathf.Sin(pitch * Mathf.Deg2Rad));
            Vector3 pos = _pivot + rot * new Vector3(0f, 0f, -dist);

            if (map != null)
            {
                float minY = InteractionUtil.SurfaceAt(map, pos.x, pos.z) + _minClearance;
                if (pos.y < minY) pos.y = minY;
            }

            if (_trauma > 0f)
            {
                _trauma = Mathf.Max(0f, _trauma - _shakeDecayPerSecond * dt);
                _shakeTime += dt * _shakeFrequency;
                float s = _trauma * _trauma;
                float scale = s * _maxShakeOffset * Mathf.Clamp(_height / 40f, 0.5f, 3f);
                float ox = Mathf.PerlinNoise(_shakeTime, 0.37f) * 2f - 1f;
                float oy = Mathf.PerlinNoise(13.1f, _shakeTime) * 2f - 1f;
                float roll = Mathf.PerlinNoise(_shakeTime, 71.7f) * 2f - 1f;
                pos += rot * new Vector3(ox * scale, oy * scale, 0f);
                rot = rot * Quaternion.Euler(0f, 0f, roll * s * _maxShakeRoll);
            }

            Transform ct = cam.transform;
            if (!transform.IsChildOf(ct))
                transform.SetPositionAndRotation(_pivot, Quaternion.Euler(0f, _yaw, 0f));
            ct.SetPositionAndRotation(pos, rot);
        }

        // ------------------------------------------------------------------ helpers

        private void Recenter(WorldMap map)
        {
            Vec2i center = new Vec2i(map.Width / 2, map.Height / 2);
            Vector3 c = GodSimCoords.TileToWorld(map, center);
            _pivot = _targetPivot = c;
            _height = _targetHeight = Mathf.Clamp(_startHeight, MinHeight, MaxHeight);
            _focusing = false;
            _middleDragging = false;
        }

        private static bool RadiusWheelActive()
        {
            GodPowerController pc = GodPowerController.Instance;
            return pc != null && pc.HasPower && InteractionUtil.CtrlHeld;
        }

        private static Ray ScreenRay(Camera cam, Vector2 mouse)
        {
            return cam.ScreenPointToRay(new Vector3(mouse.x, mouse.y, 0f));
        }

        private void ClampToMap(ref Vector3 p, WorldMap map)
        {
            if (map == null) return;
            float ts = GodSimCoords.TileSize;
            float m = _boundsMargin;
            float minX = 0.5f * ts - m, maxX = (map.Width - 0.5f) * ts + m;
            float minZ = 0.5f * ts - m, maxZ = (map.Height - 0.5f) * ts + m;
            p.x = Mathf.Clamp(p.x, minX, maxX);
            p.z = Mathf.Clamp(p.z, minZ, maxZ);
        }

        private void SanitizeSettings()
        {
            if (!(MinHeight >= 0.5f)) MinHeight = 0.5f;
            if (!(MaxHeight >= MinHeight)) MaxHeight = MinHeight;
            if (!(_zoomStep > 1.001f)) _zoomStep = 1.001f;
        }

        private static float Damp(float sharpness, float dt)
        {
            return 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * dt);
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private Camera FindCamera()
        {
            Camera c = GetComponent<Camera>();
            if (c == null) c = GetComponentInChildren<Camera>();
            if (c == null) c = Camera.main;
            return c;
        }
    }
}
