using System;
using System.Globalization;
using System.IO;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Optional timelapse of a world's growth: every <see cref="IntervalYears"/> in-game years it renders the
    /// world from a fixed high camera (a hidden, separate camera, so the player's view and the HUD are never
    /// disturbed) into a render texture and writes a PNG to
    /// Application.persistentDataPath/Timelapse/World_&lt;seed&gt;_&lt;utc time&gt;/frame_NNNNN_year_NNNNN.png.
    /// Names are generated, never user-supplied; the number of frames per session is capped.
    /// Start with <see cref="StartRecording"/>, <see cref="ToggleKey"/>, or <see cref="RecordOnNewWorld"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimelapseRecorder : MonoBehaviour
    {
        /// <summary>Absolute upper bound on frames per recording session.</summary>
        public const int HardMaxFrames = 2000;

        [Tooltip("Start a new recording automatically whenever a world is created or loaded.")]
        public bool RecordOnNewWorld;
        [Tooltip("Key that starts/stops recording (None = no key).")]
        public KeyCode ToggleKey = KeyCode.None;
        [Range(1, 100)] public int IntervalYears = 5;
        [Range(1, HardMaxFrames)] public int MaxFrames = 400;
        [Tooltip("Capture a first frame as soon as recording starts.")]
        public bool CaptureOnStart = true;

        [Header("Fixed camera")]
        [Range(256, 3840)] public int Width = 1280;
        [Range(144, 2160)] public int Height = 720;
        [Range(20f, 90f)] public float PitchDegrees = 55f;
        [Range(0f, 360f)] public float YawDegrees;
        [Range(15f, 90f)] public float FieldOfView = 40f;
        [Tooltip("Copy clear flags, culling mask and similar settings from Camera.main.")]
        public bool CopyMainCameraSettings = true;

        private Action<GodSimulation> _onSimCreated;
        private Action<YearPassedEvent> _onYear;
        private GodSimRunner _runner;
        private GodSimulation _sim;
        private WorldState _state;
        private EventBus _events;

        private bool _recording;
        private bool _pending;
        private int _frames;
        private int _lastCaptureYear;
        private string _sessionDir;

        private Camera _camera;
        private RenderTexture _rt;
        private Texture2D _tex;
        private int _w, _h;

        /// <summary>True while a session is recording.</summary>
        public bool Recording => _recording;

        /// <summary>Frames written in the current (or last) session.</summary>
        public int FramesCaptured => _frames;

        /// <summary>Folder of the current (or last) session, or null.</summary>
        public string SessionDirectory => _sessionDir;

        private void Awake()
        {
            _onSimCreated = OnSimulationCreated;
            _onYear = OnYearPassed;
        }

        private void OnEnable() { SyncRunner(); }

        private void OnDisable()
        {
            StopRecording(null);
            UnhookRunner();
            Detach();
        }

        private void OnDestroy()
        {
            StopRecording(null);
            UnhookRunner();
            Detach();
            ReleaseTargets();
            if (_camera != null) Destroy(_camera.gameObject);
            _camera = null;
        }

        // ------------------------------------------------------------------ wiring

        private void SyncRunner()
        {
            if (_runner == null)
            {
                if (!ReferenceEquals(_runner, null)) UnhookRunner();
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
            StopRecording(null);
            Attach(sim);
            if (RecordOnNewWorld && _state != null) StartRecording();
        }

        private void Attach(GodSimulation sim)
        {
            Detach();
            if (sim == null || sim.State == null || sim.Events == null) return;
            _sim = sim;
            _state = sim.State;
            _events = sim.Events;
            _events.Subscribe(_onYear);
        }

        private void Detach()
        {
            if (_events != null && _onYear != null) _events.Unsubscribe(_onYear);
            _events = null;
            _state = null;
            _sim = null;
            _pending = false;
        }

        private void OnYearPassed(YearPassedEvent e)
        {
            // Runs inside a simulation tick: only flag the capture; rendering happens in LateUpdate.
            if (_recording && e.Year - _lastCaptureYear >= Mathf.Max(1, IntervalYears)) _pending = true;
        }

        // ------------------------------------------------------------------ public control

        /// <summary>Starts a new session folder. Returns false without a world or a writable data folder.</summary>
        public bool StartRecording()
        {
            if (_recording) return true;
            if (_state == null) return false;
            string root = CinematicFiles.Root(CinematicFiles.TimelapseFolder);
            string folder = "World_" + _state.Seed.ToString("X16", CultureInfo.InvariantCulture) + "_" + CinematicFiles.Timestamp();
            string dir = root != null ? CinematicFiles.SafeCombine(root, folder) : null;
            if (dir == null) { Debug.LogWarning("[GodSim] Timelapse: no writable data folder."); return false; }
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Debug.LogWarning("[GodSim] Timelapse: cannot create folder: " + ex.Message);
                return false;
            }
            _sessionDir = dir;
            _frames = 0;
            _lastCaptureYear = _state.Year;
            _recording = true;
            _pending = CaptureOnStart;
            GodSimToasts.Show("Timelapse recording started.", new Color(0.8f, 0.9f, 1f), 3f);
            return true;
        }

        /// <summary>Stops the current session (no-op when not recording).</summary>
        public void StopRecording() => StopRecording("Timelapse stopped.");

        private void StopRecording(string toast)
        {
            if (!_recording) return;
            _recording = false;
            _pending = false;
            if (toast != null) GodSimToasts.Show(toast + " " + _frames + " frames.", new Color(0.8f, 0.9f, 1f), 3f);
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            SyncRunner();
            if (ToggleKey != KeyCode.None && InputAdapter.GetKeyDown(ToggleKey))
            {
                if (_recording) StopRecording();
                else if (!StartRecording()) GodSimToasts.Show("Timelapse needs a running world.", new Color(1f, 0.6f, 0.4f), 3f);
            }
        }

        private void LateUpdate()
        {
            if (!_pending || !_recording) return;
            _pending = false;
            if (_state == null || _state.Map == null) return;
            if (_frames >= Mathf.Clamp(MaxFrames, 1, HardMaxFrames)) { StopRecording("Timelapse frame cap reached."); return; }
            CaptureFrame();
        }

        private void CaptureFrame()
        {
            int year = _state.Year;
            try
            {
                EnsureCamera();
                EnsureTargets();
                PlaceCamera(_state.Map);

                RenderTexture previous = RenderTexture.active;
                try
                {
                    _camera.targetTexture = _rt;
                    _camera.Render();
                    RenderTexture.active = _rt;
                    _tex.ReadPixels(new Rect(0f, 0f, _w, _h), 0, 0, false);
                    _tex.Apply(false);
                }
                finally
                {
                    RenderTexture.active = previous;
                    _camera.targetTexture = null;
                }

                string name = "frame_" + (_frames + 1).ToString("D5", CultureInfo.InvariantCulture) + "_year_" + Math.Max(0, year).ToString("D5", CultureInfo.InvariantCulture) + ".png";
                string path = CinematicFiles.SafeCombine(_sessionDir, name);
                if (path == null) { StopRecording("Timelapse stopped (bad path)."); return; }
                File.WriteAllBytes(path, _tex.EncodeToPNG());
                _frames++;
                _lastCaptureYear = year;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Debug.LogWarning("[GodSim] Timelapse frame failed: " + ex.Message);
                StopRecording("Timelapse stopped (write failed).");
            }
        }

        private void EnsureCamera()
        {
            if (_camera != null) return;
            var go = new GameObject("GodSim Timelapse Camera");
            go.hideFlags = HideFlags.HideAndDontSave;
            _camera = go.AddComponent<Camera>();
            Camera main = Camera.main;
            if (CopyMainCameraSettings && main != null) _camera.CopyFrom(main);
            _camera.enabled = false;
            _camera.targetTexture = null;
        }

        private void EnsureTargets()
        {
            int w = Mathf.Clamp(Width, 256, 3840);
            int h = Mathf.Clamp(Height, 144, 2160);
            if (_rt != null && _tex != null && w == _w && h == _h) return;
            ReleaseTargets();
            _w = w;
            _h = h;
            _rt = new RenderTexture(w, h, 24) { name = "GodSim Timelapse RT", hideFlags = HideFlags.HideAndDontSave };
            _tex = new Texture2D(w, h, TextureFormat.RGB24, false) { name = "GodSim Timelapse Frame", hideFlags = HideFlags.HideAndDontSave };
        }

        private void ReleaseTargets()
        {
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_tex != null) Destroy(_tex);
            _rt = null;
            _tex = null;
        }

        /// <summary>Frames the whole map from a fixed, high, tilted viewpoint.</summary>
        private void PlaceCamera(WorldMap map)
        {
            float ts = CinematicUtil.TileSize(map);
            Vector3 center = new Vector3(map.Width * ts * 0.5f, 0f, map.Height * ts * 0.5f);
            float pitch = Mathf.Clamp(PitchDegrees, 20f, 90f);
            float fov = Mathf.Clamp(FieldOfView, 15f, 90f);
            Quaternion rot = Quaternion.Euler(pitch, YawDegrees, 0f);
            Vector3 forward = rot * Vector3.forward;

            float aspect = _w / (float)_h;
            float halfV = fov * 0.5f * Mathf.Deg2Rad;
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            float ex = map.Width * ts * 0.5f, ez = map.Height * ts * 0.5f;
            float radius = Mathf.Sqrt(ex * ex + ez * ez);
            // Fit the map's bounding circle: horizontally, and vertically as foreshortened by the pitch.
            float sin = Mathf.Sin(pitch * Mathf.Deg2Rad);
            float dist = Mathf.Max(radius / Mathf.Tan(halfH), radius * sin / Mathf.Tan(halfV)) * 1.05f + radius * (1f - sin) * 0.5f;

            _camera.transform.SetPositionAndRotation(center - forward * dist, rot);
            _camera.fieldOfView = fov;
            _camera.nearClipPlane = Mathf.Max(0.3f, dist * 0.01f);
            _camera.farClipPlane = dist + radius * 2f + 100f;
        }
    }
}
