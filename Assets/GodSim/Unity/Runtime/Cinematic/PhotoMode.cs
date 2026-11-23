using System;
using System.IO;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Photo mode (P). Pauses the simulation (the previous pause state is restored on exit), takes over the
    /// camera with a free-fly rig (WASD + Q/E, hold right mouse to look, Shift fast, Ctrl slow), adjustable
    /// field of view (wheel or -/=) and roll (Z/X, R resets), and a depth-of-field hint (F toggles, [ ] change
    /// the f-stop) whose values are exposed for an optional post-processing integration. The HUD should hide
    /// while <see cref="Active"/> is true. Enter/F12 saves a screenshot with ScreenCapture.CaptureScreenshot to
    /// Application.persistentDataPath/Screenshots under a generated file name. Escape or P exits.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhotoMode : MonoBehaviour
    {
        private static readonly float[] s_fStops = { 1.4f, 2f, 2.8f, 4f, 5.6f, 8f, 11f, 16f, 22f };
        private static readonly string[] s_fStopNames = { "f/1.4", "f/2", "f/2.8", "f/4", "f/5.6", "f/8", "f/11", "f/16", "f/22" };
        private const int DefaultStop = 4;

        private const string HelpText =
            "PHOTO MODE   (P / Esc to exit)\n" +
            "WASD move   Q/E down/up   Shift fast   Ctrl slow\n" +
            "Hold right mouse: look   Wheel or -/=: field of view\n" +
            "Z/X roll   R reset roll   F depth of field   [ ] aperture\n" +
            "Enter / F12 screenshot   H hide this panel";

        private const string DofHint = "Depth of field needs a post-processing volume; values are published on PhotoMode.";

        /// <summary>The photo mode component in the scene (null when none is enabled).</summary>
        public static PhotoMode Instance { get; private set; }

        /// <summary>True while photo mode runs. The HUD and other overlays should hide themselves.</summary>
        public static bool Active { get; private set; }

        /// <summary>Raised when photo mode starts (true) or ends (false).</summary>
        public static event Action<bool> ActiveChanged;

        /// <summary>Depth-of-field hint for an optional post-processing integration (photo mode only).</summary>
        public static bool DepthOfFieldEnabled { get; private set; }

        /// <summary>Distance in world units from the camera to the terrain at the screen centre (updated while the DOF hint is on).</summary>
        public static float FocusDistance { get; private set; } = 50f;

        /// <summary>Selected aperture as an f-number (1.4..22).</summary>
        public static float Aperture { get; private set; } = s_fStops[DefaultStop];

        [Header("Keys")]
        public KeyCode ToggleKey = KeyCode.P;
        public KeyCode CaptureKey = KeyCode.Return;
        public KeyCode AltCaptureKey = KeyCode.F12;

        [Header("Free-fly")]
        [Range(1f, 200f)] public float MoveSpeed = 12f;
        [Range(1f, 10f)] public float FastMultiplier = 4f;
        [Range(0.05f, 1f)] public float SlowMultiplier = 0.25f;
        [Range(0.02f, 1f)] public float LookSensitivity = 0.15f;
        [Range(5f, 180f)] public float RollSpeed = 45f;
        [Range(0.1f, 10f)] public float TerrainClearance = 0.6f;
        [Range(10f, 2000f)] public float MaxAltitude = 400f;

        [Header("Lens")]
        [Range(5f, 60f)] public float MinFieldOfView = 10f;
        [Range(60f, 120f)] public float MaxFieldOfView = 100f;
        [Range(0.5f, 10f)] public float FovPerWheelNotch = 2f;

        [Header("Capture")]
        [Tooltip("Resolution multiplier for screenshots (1 = screen resolution).")]
        [Range(1, 4)] public int SuperSize = 1;
        public bool ShowHelp = true;

        private CinematicCameraLease _lease;
        private Camera _cam;
        private Transform _camT;
        private GodSimRunner _pausedRunner;
        private bool _prevPaused;
        private bool _duplicate;
        private float _yaw, _pitch, _roll, _fov;
        private Vector3 _pos;
        private Vector2 _lastMouse;
        private int _stop = DefaultStop;
        private int _captureStage;
        private string _savedMessage;
        private float _savedMessageUntil;
        private GUIStyle _panelStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Active = false;
            ActiveChanged = null;
            DepthOfFieldEnabled = false;
            FocusDistance = 50f;
            Aperture = s_fStops[DefaultStop];
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GodSim] Duplicate PhotoMode on '" + name + "' removed.");
                _duplicate = true;
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (!_duplicate && Instance == null) Instance = this;
        }

        private void OnDisable()
        {
            if (_duplicate) return;
            Exit();
        }

        private void OnDestroy()
        {
            if (_duplicate) return;
            Exit();
            if (Instance == this) Instance = null;
        }

        /// <summary>Starts photo mode. Returns false when no camera is available.</summary>
        public bool Enter()
        {
            if (Active) return true;
            if (_duplicate || !isActiveAndEnabled) return false;

            SpectatorDirector director = SpectatorDirector.Instance;
            CinematicCameraLease lease = default;
            bool handedOff = director != null && director.Active && director.HandOff(out lease);
            if (!handedOff && !CinematicCameraLease.TryAcquire(out lease))
            {
                GodSimToasts.Show("Photo mode: no camera found.", new Color(1f, 0.6f, 0.4f), 3f);
                return false;
            }
            if (lease.Camera == null)
            {
                lease.Release(false);
                return false;
            }
            _lease = lease;
            _cam = lease.Camera;
            _camT = _cam.transform;

            GodSimRunner runner = GodSimRunner.Instance;
            _pausedRunner = runner;
            if (runner != null)
            {
                _prevPaused = runner.Paused;
                runner.Paused = true;
            }

            _pos = _camT.position;
            Vector3 e = _camT.eulerAngles;
            _pitch = Mathf.Clamp(NormalizeAngle(e.x), -89f, 89f);
            _yaw = e.y;
            _roll = 0f;
            _fov = Mathf.Clamp(_cam.fieldOfView, MinFieldOfView, MaxFieldOfView);
            _lastMouse = InputAdapter.MousePosition;
            _captureStage = 0;
            _savedMessage = null;
            DepthOfFieldEnabled = false;

            Active = true;
            RaiseActiveChanged(true);
            return true;
        }

        /// <summary>Leaves photo mode: restores the camera pose, the god camera and the previous pause state.</summary>
        public void Exit()
        {
            if (!Active) return;
            Active = false;
            DepthOfFieldEnabled = false;
            _captureStage = 0;
            if (_pausedRunner != null) _pausedRunner.Paused = _prevPaused;
            _pausedRunner = null;
            _lease.Release(true);
            _cam = null;
            _camT = null;
            RaiseActiveChanged(false);
        }

        /// <summary>Toggles photo mode.</summary>
        public void Toggle() { if (Active) Exit(); else Enter(); }

        /// <summary>Requests a screenshot (taken two frames later, with the photo overlay hidden).</summary>
        public void RequestCapture() { if (Active && _captureStage == 0) _captureStage = 1; }

        private static void RaiseActiveChanged(bool value)
        {
            Action<bool> handler = ActiveChanged;
            if (handler == null) return;
            try { handler(value); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private static float NormalizeAngle(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f; else if (a < -180f) a += 360f;
            return a;
        }

        private void Update()
        {
            if (_duplicate) return;
            if (InputAdapter.GetKeyDown(ToggleKey)) { Toggle(); return; }
            if (!Active) return;
            if (_cam == null) { Exit(); return; }
            if (InputAdapter.GetKeyDown(KeyCode.Escape)) { Exit(); return; }

            StepCapture();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // Look (hold right mouse). Position deltas keep this independent of the input backend.
            Vector2 mouse = InputAdapter.MousePosition;
            if (InputAdapter.MouseHeld(1) && !InputAdapter.MouseDown(1))
            {
                Vector2 d = mouse - _lastMouse;
                if (d.sqrMagnitude < 250f * 250f)
                {
                    _yaw += d.x * LookSensitivity;
                    _pitch -= d.y * LookSensitivity;
                }
            }
            _lastMouse = mouse;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            _yaw = Mathf.Repeat(_yaw, 360f);

            // Roll and lens.
            if (InputAdapter.GetKey(KeyCode.Z)) _roll += RollSpeed * dt;
            if (InputAdapter.GetKey(KeyCode.X)) _roll -= RollSpeed * dt;
            if (InputAdapter.GetKeyDown(KeyCode.R)) _roll = 0f;
            _roll = Mathf.Clamp(_roll, -90f, 90f);

            float wheel = InputAdapter.PointerOverUI ? 0f : InputAdapter.ScrollDelta;
            _fov -= wheel * FovPerWheelNotch;
            if (InputAdapter.GetKey(KeyCode.Minus)) _fov += 25f * dt;
            if (InputAdapter.GetKey(KeyCode.Equals)) _fov -= 25f * dt;
            _fov = Mathf.Clamp(MathUtil.Sanitize(_fov, 60f), MinFieldOfView, Mathf.Max(MinFieldOfView, MaxFieldOfView));

            if (InputAdapter.GetKeyDown(KeyCode.F)) DepthOfFieldEnabled = !DepthOfFieldEnabled;
            if (InputAdapter.GetKeyDown(KeyCode.LeftBracket)) _stop = Mathf.Max(0, _stop - 1);
            if (InputAdapter.GetKeyDown(KeyCode.RightBracket)) _stop = Mathf.Min(s_fStops.Length - 1, _stop + 1);
            Aperture = s_fStops[_stop];
            if (InputAdapter.GetKeyDown(KeyCode.H)) ShowHelp = !ShowHelp;
            if (InputAdapter.GetKeyDown(CaptureKey) || InputAdapter.GetKeyDown(AltCaptureKey)) RequestCapture();

            // Move.
            WorldMap map = CurrentMap();
            float tileSize = CinematicUtil.TileSize(map);
            Quaternion look = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 move = Vector3.zero;
            if (InputAdapter.GetKey(KeyCode.W)) move += look * Vector3.forward;
            if (InputAdapter.GetKey(KeyCode.S)) move -= look * Vector3.forward;
            if (InputAdapter.GetKey(KeyCode.D)) move += look * Vector3.right;
            if (InputAdapter.GetKey(KeyCode.A)) move -= look * Vector3.right;
            if (InputAdapter.GetKey(KeyCode.E)) move += Vector3.up;
            if (InputAdapter.GetKey(KeyCode.Q)) move -= Vector3.up;
            if (move.sqrMagnitude > 1f) move.Normalize();

            float speed = MoveSpeed * tileSize;
            if (InputAdapter.GetKey(KeyCode.LeftShift) || InputAdapter.GetKey(KeyCode.RightShift)) speed *= FastMultiplier;
            if (InputAdapter.GetKey(KeyCode.LeftControl) || InputAdapter.GetKey(KeyCode.RightControl)) speed *= SlowMultiplier;
            float altitude = _pos.y - CinematicUtil.GroundAt(map, tileSize, _pos);
            speed *= Mathf.Clamp(1f + altitude / (30f * tileSize), 1f, 4f);
            _pos += move * (speed * dt);

            if (!CinematicUtil.IsFinite(_pos)) _pos = _lease.Position;
            CinematicUtil.ClampToMap(map, tileSize, ref _pos, 60f * tileSize);
            float ground = CinematicUtil.GroundAround(map, tileSize, _pos);
            _pos.y = Mathf.Clamp(_pos.y, ground + TerrainClearance * tileSize, Mathf.Max(ground + TerrainClearance * tileSize, MaxAltitude * tileSize));

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, _roll);
            _camT.SetPositionAndRotation(_pos, rot);
            _cam.fieldOfView = _fov;

            if (DepthOfFieldEnabled) FocusDistance = MeasureFocus(map, tileSize, _pos, look * Vector3.forward);
        }

        private WorldMap CurrentMap()
        {
            GodSimRunner runner = GodSimRunner.Instance;
            GodSimulation sim = runner != null ? runner.Simulation : null;
            WorldState state = sim != null ? sim.State : null;
            return state != null ? state.Map : null;
        }

        /// <summary>Marches the view ray over the height field (bounded, allocation-free) to find the focus distance.</summary>
        private static float MeasureFocus(WorldMap map, float tileSize, Vector3 origin, Vector3 dir)
        {
            const float maxDistance = 1500f;
            if (map == null) return 50f;
            float step = 0.5f * tileSize;
            float d = step;
            for (int i = 0; i < 256 && d < maxDistance * tileSize; i++)
            {
                Vector3 p = origin + dir * d;
                if (p.y <= CinematicUtil.GroundAt(map, tileSize, p)) return d;
                d += step;
                step *= 1.04f;
            }
            return Mathf.Min(d, maxDistance * tileSize);
        }

        private void StepCapture()
        {
            // Stage 1: overlay hides this frame. Stage 2: capture (end of this frame, overlay still hidden).
            // Stage 3: report on the following frame so the toast is not in the picture.
            switch (_captureStage)
            {
                case 1: _captureStage = 2; break;
                case 2: Capture(); _captureStage = 3; break;
                case 3:
                    _captureStage = 0;
                    if (_savedMessage != null) GodSimToasts.Show(_savedMessage, new Color(0.8f, 1f, 0.8f), 3f);
                    break;
            }
        }

        private void Capture()
        {
            _savedMessage = null;
            try
            {
                string dir = CinematicFiles.Root(CinematicFiles.ScreenshotFolder);
                if (dir == null) { Fail("no data folder"); return; }
                Directory.CreateDirectory(dir);
                string path = CinematicFiles.UniquePath(dir, "GodSim_" + CinematicFiles.Timestamp(), ".png");
                if (path == null) { Fail("could not create a file name"); return; }
                ScreenCapture.CaptureScreenshot(path, Mathf.Clamp(SuperSize, 1, 4));
                _savedMessage = "Screenshot saved: " + Path.GetFileName(path);
                _savedMessageUntil = Time.unscaledTime + 4f;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Fail(ex.Message);
            }
        }

        private void Fail(string reason)
        {
            Debug.LogWarning("[GodSim] Screenshot failed: " + reason);
            _savedMessage = "Screenshot failed.";
            _savedMessageUntil = Time.unscaledTime + 4f;
        }

        private void OnGUI()
        {
            if (!Active || _captureStage != 0) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_panelStyle == null)
            {
                _panelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false, richText = false, fontSize = 13 };
                _panelStyle.normal.textColor = Color.white;
            }
            GUI.depth = -60;
            Color prev = GUI.color;
            float h = Screen.height;

            if (ShowHelp)
            {
                Rect panel = new Rect(12f, h - 170f, 470f, 158f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(panel, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, panel.width - 20f, 92f), HelpText, _panelStyle);

                float y = panel.y + 104f;
                float x = panel.x + 10f;
                GUI.Label(new Rect(x, y, 40f, 20f), "FOV", _panelStyle);
                GUI.Label(new Rect(x + 36f, y, 50f, 20f), CinematicUtil.Number(Mathf.RoundToInt(_fov)), _panelStyle);
                GUI.Label(new Rect(x + 90f, y, 40f, 20f), "Roll", _panelStyle);
                GUI.Label(new Rect(x + 128f, y, 50f, 20f), CinematicUtil.Number(Mathf.RoundToInt(_roll)), _panelStyle);
                GUI.Label(new Rect(x + 180f, y, 60f, 20f), DepthOfFieldEnabled ? "DOF on" : "DOF off", _panelStyle);
                if (DepthOfFieldEnabled)
                {
                    GUI.Label(new Rect(x + 244f, y, 50f, 20f), s_fStopNames[_stop], _panelStyle);
                    GUI.Label(new Rect(x + 296f, y, 50f, 20f), "focus", _panelStyle);
                    GUI.Label(new Rect(x + 340f, y, 60f, 20f), CinematicUtil.Number(Mathf.RoundToInt(FocusDistance)), _panelStyle);
                    GUI.color = new Color(1f, 1f, 1f, 0.7f);
                    GUI.Label(new Rect(x, y + 22f, panel.width - 20f, 20f), DofHint, _panelStyle);
                }
            }

            if (_savedMessage != null && Time.unscaledTime < _savedMessageUntil)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                Rect r = new Rect(12f, 12f, 420f, 26f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 20f), _savedMessage, _panelStyle);
            }
            GUI.color = prev;
        }
    }
}
