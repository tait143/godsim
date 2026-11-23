using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Core.Persistence;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GodSim.Unity
{
    /// <summary>
    /// Owns the running <see cref="GodSimulation"/> and drives it from Unity's frame loop at a selectable
    /// speed, within a per-frame time budget. Scene singleton: renderers, camera, input and HUD find it via
    /// <see cref="Instance"/>, rebuild on <see cref="SimulationCreated"/> and refresh on <see cref="Ticked"/>.
    /// Main thread only.
    /// <para>
    /// Subscribers that come up after the world was created (e.g. in a later Start) must check
    /// <see cref="Simulation"/> != null and build immediately, then listen to <see cref="SimulationCreated"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class GodSimRunner : MonoBehaviour
    {
        /// <summary>Simulation speeds in ticks (days) per second.</summary>
        public static readonly float[] SpeedLevels = { 1f, 4f, 16f, 64f, 240f };

        /// <summary>Maximum simulated backlog, in seconds of game time at the current speed.</summary>
        public const float MaxBacklogSeconds = 2f;

        /// <summary>The active runner (null before Awake or after it is destroyed).</summary>
        public static GodSimRunner Instance { get; private set; }

        [Tooltip("World + visual settings. When null, SimConfig defaults are used.")]
        public GodSimSettings Settings;

        [Tooltip("Real-time milliseconds per frame the simulation may use. At least one tick runs per frame when ticks are due.")]
        public float FrameBudgetMs = 8f;

        [Tooltip("Create a world from Settings in Start. Disable when a menu calls NewWorld/LoadGame instead.")]
        public bool CreateWorldOnStart = true;

        [Tooltip("Keep this object's root alive across scene loads (DontDestroyOnLoad).")]
        public bool PersistAcrossScenes;

        [SerializeField, Tooltip("Initial index into SpeedLevels.")]
        private int _speedIndex;

        [SerializeField, Tooltip("Start paused.")]
        private bool _paused;

        private readonly Stopwatch _frameWatch = new Stopwatch();
        private UnityLogSink _log;
        private SaveFileStore _store;
        private byte[] _hmacKey;
        private double _accumulator;
        private bool _isDuplicate;

        /// <summary>Raised after <see cref="NewWorld"/> or <see cref="LoadGame"/> installs a simulation. Renderers rebuild here.</summary>
        public event Action<GodSimulation> SimulationCreated;

        /// <summary>Raised at most once per frame, after one or more ticks were stepped in that frame.</summary>
        public event Action Ticked;

        /// <summary>The running simulation, or null before the first world exists.</summary>
        public GodSimulation Simulation { get; private set; }

        /// <summary>When true no ticks are stepped (rendering continues).</summary>
        public bool Paused
        {
            get => _paused;
            set { _paused = value; if (value) _accumulator = 0; }
        }

        /// <summary>Index into <see cref="SpeedLevels"/>; clamped to a valid index.</summary>
        public int SpeedIndex
        {
            get => _speedIndex;
            set => _speedIndex = Mathf.Clamp(value, 0, SpeedLevels.Length - 1);
        }

        /// <summary>Effective simulation rate in ticks per second: 0 while paused or without a world, else SpeedLevels[SpeedIndex].</summary>
        public float TicksPerSecond => _paused || Simulation == null ? 0f : SpeedLevels[Mathf.Clamp(_speedIndex, 0, SpeedLevels.Length - 1)];

        /// <summary>Ticks stepped during the last frame.</summary>
        public int LastFrameSteps { get; private set; }

        /// <summary>Real time spent stepping during the last frame, in milliseconds.</summary>
        public double LastFrameStepMs { get; private set; }

        /// <summary>Last error message from world creation, saving, loading or stepping (null if none yet).</summary>
        public string LastError { get; private set; }

        /// <summary>Folder that holds save slots: Application.persistentDataPath/Saves.</summary>
        public string SaveDirectory => Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>The logger handed to the simulation (routes to the Unity console).</summary>
        public ISimLogger Logger => EnsureLog();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "Enter Play Mode Options" with domain reload disabled.
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(UnityLogSink.Prefix + "Duplicate GodSimRunner on '" + name + "' destroyed; using the one on '" + Instance.name + "'.");
                _isDuplicate = true;
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
            _speedIndex = Mathf.Clamp(_speedIndex, 0, SpeedLevels.Length - 1);
            EnsureLog();
            if (PersistAcrossScenes) DontDestroyOnLoad(transform.root.gameObject);
        }

        private void Start()
        {
            if (_isDuplicate) return;
            ApplyVisualSettings();
            if (CreateWorldOnStart && Simulation == null)
                NewWorld(Settings != null ? Settings.ToConfig() : new SimConfig().Sanitize());
        }

        private void OnValidate()
        {
            if (float.IsNaN(FrameBudgetMs)) FrameBudgetMs = 8f;
            FrameBudgetMs = Mathf.Clamp(FrameBudgetMs, 0.5f, 100f);
            _speedIndex = Mathf.Clamp(_speedIndex, 0, SpeedLevels.Length - 1);
        }

        private void OnDestroy()
        {
            if (_isDuplicate || Instance != this) return;
            Instance = null;
            SimulationCreated = null;
            Ticked = null;
            Simulation = null;
        }

        private void Update()
        {
            GodSimulation sim = Simulation;
            LastFrameSteps = 0;
            LastFrameStepMs = 0;
            if (sim == null) return;

            float tps = TicksPerSecond;
            float dt = Time.deltaTime;
            if (tps <= 0f || !(dt > 0f)) return;

            _accumulator += tps * (double)dt;
            double maxBacklog = tps * (double)MaxBacklogSeconds;
            if (_accumulator > maxBacklog) _accumulator = maxBacklog;
            if (_accumulator < 1.0) return;

            double budget = float.IsNaN(FrameBudgetMs) ? 8.0 : Math.Max(0.1, FrameBudgetMs);
            int steps = 0;
            _frameWatch.Restart();
            while (_accumulator >= 1.0)
            {
                try
                {
                    sim.Step();
                }
                catch (Exception ex)
                {
                    // Core isolates system faults itself; reaching here means StrictMode or a facade bug.
                    // Pause instead of re-faulting (and spamming the console) every frame.
                    Fail("Simulation step failed at tick " + sim.Tick + "; pausing. " + ex);
                    Paused = true;
                    break;
                }
                _accumulator -= 1.0;
                steps++;
                if (_frameWatch.Elapsed.TotalMilliseconds >= budget) break;
            }
            _frameWatch.Stop();
            LastFrameSteps = steps;
            LastFrameStepMs = _frameWatch.Elapsed.TotalMilliseconds;

            if (steps > 0 && Ticked != null)
            {
                try { Ticked(); }
                catch (Exception ex) { EnsureLog().Error("A Ticked subscriber threw: " + ex); }
            }
        }

        /// <summary>
        /// Replaces the running world with a freshly generated one (config is cloned and sanitized; the
        /// caller's instance is not modified). On failure the previous world keeps running and the error is
        /// logged and stored in <see cref="LastError"/>.
        /// </summary>
        public void NewWorld(SimConfig config)
        {
            SimConfig cfg = (config ?? new SimConfig()).Clone();
            if (cfg.StartingRaces != null) cfg.StartingRaces = (RaceId[])cfg.StartingRaces.Clone();
            cfg.Sanitize();

            GodSimulation sim;
            try
            {
                sim = GodSimulation.CreateNew(cfg, EnsureLog());
            }
            catch (Exception ex)
            {
                Fail("World creation failed (seed " + cfg.Seed + ", " + cfg.MapWidth + "x" + cfg.MapHeight + "): " + ex);
                return;
            }
            if (sim == null)
            {
                Fail("World creation returned no simulation.");
                return;
            }
            EnsureLog().Info("New world: seed " + cfg.Seed + ", " + cfg.MapWidth + "x" + cfg.MapHeight + ", " + cfg.Preset + ".");
            Install(sim);
        }

        /// <summary>Saves the running world to a slot (HMAC-signed). Returns false with a reason on any failure.</summary>
        public bool SaveGame(string slot, out string error)
        {
            error = null;
            GodSimulation sim = Simulation;
            if (sim == null) return Reject("No world is running.", out error);
            try
            {
                if (!SaveFileStore.IsValidSlotName(slot)) return Reject("Invalid save slot name '" + slot + "'.", out error);
                SaveFileStore store = GetStore();
                byte[] key = GetHmacKey();
                byte[] data = sim.Save(key);
                if (data == null || data.Length == 0) return Reject("Serializer produced no data.", out error);
                if (!store.TryWrite(slot, data, out error))
                    return Reject("Could not write save '" + slot + "': " + (error ?? "unknown error"), out error);
                EnsureLog().Info("Saved '" + slot + "' (" + data.Length + " bytes, tick " + sim.Tick + ").");
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                return Reject("Save '" + slot + "' failed: " + ex.Message, out error, ex);
            }
        }

        /// <summary>
        /// Loads a slot and replaces the running world (raises <see cref="SimulationCreated"/>). Corrupt, tampered
        /// or incompatible saves return false and leave the current world untouched. Runtime tuning comes from the
        /// current world's config (or <see cref="Settings"/> / defaults when there is none).
        /// </summary>
        public bool LoadGame(string slot, out string error)
        {
            error = null;
            try
            {
                if (!SaveFileStore.IsValidSlotName(slot)) return Reject("Invalid save slot name '" + slot + "'.", out error);
                SaveFileStore store = GetStore();
                byte[] data;
                if (!store.TryRead(slot, out data, out error))
                    return Reject("Could not read save '" + slot + "': " + (error ?? "unknown error"), out error);
                if (data == null || data.Length == 0) return Reject("Save '" + slot + "' is empty.", out error);

                byte[] key = GetHmacKey();
                SimConfig cfg = CurrentConfigForLoad();
                GodSimulation loaded;
                try
                {
                    loaded = GodSimulation.Load(data, cfg, EnsureLog(), key);
                }
                catch (Exception ex)
                {
                    return Reject("Save '" + slot + "' is corrupt, tampered with or incompatible: " + ex.Message, out error, ex);
                }
                if (loaded == null) return Reject("Save '" + slot + "' could not be loaded.", out error);

                EnsureLog().Info("Loaded '" + slot + "' (tick " + loaded.Tick + ").");
                Install(loaded);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                return Reject("Load '" + slot + "' failed: " + ex.Message, out error, ex);
            }
        }

        /// <summary>Save slots on disk (empty on any IO failure, which is logged).</summary>
        public IReadOnlyList<SaveSlotInfo> ListSaves()
        {
            try
            {
                IReadOnlyList<SaveSlotInfo> list = GetStore().List();
                return list ?? Array.Empty<SaveSlotInfo>();
            }
            catch (Exception ex)
            {
                Fail("Listing saves failed: " + ex.Message);
                return Array.Empty<SaveSlotInfo>();
            }
        }

        /// <summary>Pushes visual settings (height scale, shadow quality) into the shared static state.</summary>
        public void ApplyVisualSettings()
        {
            if (Settings == null) return;
            float h = Settings.HeightScale;
            GodSimCoords.HeightScale = float.IsNaN(h) || h <= 0f ? 6f : h;
            QualitySettings.shadows = Settings.ShadowQuality;
            if (_log != null) _log.MinLevel = Settings.MinLogLevel;
        }

        private void Install(GodSimulation sim)
        {
            ApplyVisualSettings();
            Simulation = sim;
            _accumulator = 0;
            LastError = null;
            Action<GodSimulation> handler = SimulationCreated;
            if (handler == null) return;
            try { handler(sim); }
            catch (Exception ex) { EnsureLog().Error("A SimulationCreated subscriber threw: " + ex); }
        }

        private SimConfig CurrentConfigForLoad()
        {
            SimConfig baseCfg = Simulation != null && Simulation.Config != null
                ? Simulation.Config.Clone()
                : (Settings != null ? Settings.ToConfig() : new SimConfig());
            if (baseCfg.StartingRaces != null) baseCfg.StartingRaces = (RaceId[])baseCfg.StartingRaces.Clone();
            return baseCfg.Sanitize();
        }

        private SaveFileStore GetStore()
        {
            if (_store == null) _store = new SaveFileStore(SaveDirectory);
            return _store;
        }

        private byte[] GetHmacKey()
        {
            if (_hmacKey == null)
            {
                var provider = InstallKeyFile.LoadOrCreate(Application.persistentDataPath);
                byte[] key = provider != null ? provider.GetKey() : null;
                if (key == null || key.Length == 0) throw new IOException("Save signing key is unavailable.");
                _hmacKey = key;
            }
            return _hmacKey;
        }

        private UnityLogSink EnsureLog()
        {
            if (_log == null) _log = new UnityLogSink(Settings != null ? Settings.MinLogLevel : LogLevel.Info);
            return _log;
        }

        private void Fail(string message)
        {
            LastError = message;
            EnsureLog().Error(message);
        }

        private bool Reject(string message, out string error, Exception ex = null)
        {
            error = message;
            LastError = message;
            EnsureLog().Error(ex != null ? message + "\n" + ex : message);
            return false;
        }
    }
}
