using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Procedural audio for GodSim (the project ships no audio assets). On Start it synthesizes every clip with
    /// <see cref="ProceduralSynth"/>, spread across frames under a time budget; it keeps a pool of 2D (UI) and
    /// 3D (world, logarithmic rolloff) AudioSources, listens to the simulation <see cref="EventBus"/> and plays
    /// rate-limited, positioned one-shots, and runs an ambient layer: a generative music bed whose mode follows
    /// the world's highest age and the god's alignment, plus wind / rain / nature loops that crossfade with camera
    /// height and weather. Master / music / sfx volumes persist in PlayerPrefs.
    /// <para>Sim event handlers only enqueue (thread-safe); all Unity calls happen in Update on the main thread.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("GodSim/Audio/GodSim Audio")]
    public sealed class GodSimAudio : MonoBehaviour
    {
        /// <summary>Sound kinds. Rate limiting is per kind; some kinds have several variants.</summary>
        public enum Sound : byte
        {
            UiClick, UiError, PowerSelect, Cast, Lightning, Meteor, Earthquake, Fire, Flood, Omen,
            AgeBell, WarDrums, Construction, Wonder, Battle, SettlementFounded, Legendary
        }

        private static readonly int SoundCount = Enum.GetValues(typeof(Sound)).Length;

        /// <summary>The active instance (null until Awake).</summary>
        public static GodSimAudio Instance { get; private set; }

        public const string PrefMaster = "GodSim.Audio.Master";
        public const string PrefMusic = "GodSim.Audio.Music";
        public const string PrefSfx = "GodSim.Audio.Sfx";
        public const string PrefMuteWhenPaused = "GodSim.Audio.MuteWhenPaused";

        [Header("Mix (persisted in PlayerPrefs)")]
        [SerializeField, Range(0f, 1f)] private float _masterVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.8f;
        [Tooltip("Silence world sounds, ambience and music while the simulation is paused (UI stays audible).")]
        [SerializeField] private bool _muteWhenPaused;
        [SerializeField, Range(0f, 1f)] private float _ambienceLevel = 0.6f;

        [Header("Voices")]
        [SerializeField, Range(2, 16)] private int _uiVoices = 6;
        [SerializeField, Range(4, 48)] private int _worldVoices = 24;
        [SerializeField, Range(1, 8)] private int _fireEmitters = 4;
        [SerializeField] private float _minDistance = 14f;
        [SerializeField] private float _maxDistance = 600f;

        [Header("Throttling")]
        [Tooltip("Global cap on positional one-shots per second (on top of the per-kind limits).")]
        [SerializeField] private float _maxWorldOneShotsPerSecond = 10f;
        [Tooltip("Building completions are only heard within this many world units of the camera focus (+ half the camera height).")]
        [SerializeField] private float _constructionHearingRadius = 45f;
        [Tooltip("Legendary chronicle stingers are skipped within this many seconds of another stinger.")]
        [SerializeField] private float _stingerSpacingSeconds = 1.5f;

        [Header("Ambience")]
        [SerializeField] private float _lowCameraHeight = 12f;
        [SerializeField] private float _highCameraHeight = 140f;
        [SerializeField] private float _ambienceFadePerSecond = 0.35f;

        [Header("Music")]
        [Tooltip("Stream the generative bed through a PCM reader callback. Off (or WebGL) = pre-rendered loop, re-rendered when the harmony changes.")]
        [SerializeField] private bool _streamMusic = true;
        [SerializeField] private float _prerenderedLoopSeconds = 48f;
        [SerializeField] private float _harmonyCheckSeconds = 2f;

        [Header("Startup")]
        [Tooltip("Synthesize PCM on a worker thread (forced off on WebGL); only AudioClip creation runs on the main thread.")]
        [SerializeField] private bool _backgroundSynthesis = true;
        [Tooltip("Main-thread milliseconds per frame spent synthesizing / uploading clips.")]
        [SerializeField] private float _clipBuildBudgetMs = 4f;
        [SerializeField] private bool _logStartup = false;

        // ---------------------------------------------------------------------------------------------
        private struct Pending
        {
            public Sound Sound;
            public int Variant;
            public Vec2i Tile;
            public bool Positional;
            public float Volume;
            public bool NearCameraOnly;
            public bool FromPower;
            public float FireSeconds;
        }

        private sealed class Voice
        {
            public AudioSource Source;
            public float BaseVolume;
            public float StartTime;
            public bool World;
        }

        private sealed class FireEmitter
        {
            public AudioSource Source;
            public float EndTime;
            public float Level;
            public float BaseVolume;
            public float StartTime;
        }

        private sealed class ClipJob
        {
            public string Name;
            public Func<float[]> Generate;
            public Action<AudioClip> Assign;
            public float[] Result;
            public string Error;
        }

        /// <summary>Runs clip generators sequentially on a worker thread (ProceduralSynth is pure and thread-safe).</summary>
        private sealed class SynthBatch
        {
            private readonly List<ClipJob> _jobs;
            private volatile int _done;
            public volatile bool Cancel;

            public SynthBatch(List<ClipJob> jobs) { _jobs = jobs; }
            public int Done => _done;

            public void Run(object unused)
            {
                for (int i = 0; i < _jobs.Count; i++)
                {
                    if (Cancel) { _done = _jobs.Count; return; }
                    ClipJob job = _jobs[i];
                    try { job.Result = job.Generate(); }
                    catch (Exception ex) { job.Error = ex.Message; }
                    _done = i + 1;
                }
            }
        }

        private static readonly float[] DefaultRatePerSecond =
        {
            20f, // UiClick
            4f,  // UiError
            8f,  // PowerSelect
            4f,  // Cast
            4f,  // Lightning
            3f,  // Meteor
            1.5f,// Earthquake
            3f,  // Fire
            2f,  // Flood
            0.5f,// Omen
            1f,  // AgeBell
            0.5f,// WarDrums
            3f,  // Construction
            1f,  // Wonder
            3f,  // Battle
            2f,  // SettlementFounded
            0.5f // Legendary
        };

        private static readonly float[] BaseVolumes =
        {
            0.55f, 0.5f, 0.6f, 0.9f, 1f, 1f, 0.95f, 0.7f, 0.8f, 0.6f,
            0.75f, 0.8f, 0.55f, 0.75f, 0.7f, 0.7f, 0.6f
        };

        private static bool IsStinger(Sound s) => s == Sound.AgeBell || s == Sound.WarDrums || s == Sound.Wonder || s == Sound.Legendary || s == Sound.Omen;

        // Clips.
        private readonly AudioClip[][] _clips = new AudioClip[SoundCount][];
        private AudioClip _windClip, _rainClip, _natureClip, _fireClip;
        private readonly List<AudioClip> _ownedClips = new List<AudioClip>();
        private bool _clipsReady;
        private SynthBatch _batch;

        // Pools.
        private Voice[] _uiPool;
        private Voice[] _worldPool;
        private FireEmitter[] _fires;
        private AudioSource _windSrc, _rainSrc, _natureSrc;
        private float _windLevel, _rainLevel, _natureLevel;

        // Music.
        private GenerativeMusicBed _bed;
        private AudioSource _musicA, _musicB;
        private AudioClip _musicStreamClip;
        private AudioClip _musicLoopA, _musicLoopB;
        private bool _musicOnA = true;
        private float _musicXfade = 1f;
        private bool _musicRendering;
        private bool _musicDirty;
        private float _nextHarmonyCheck;
        private ProceduralSynth.Mode _harmonyMode = ProceduralSynth.Mode.Dorian;
        private int _harmonyRoot = -1;
        private bool _useStreaming;

        // Simulation hookup.
        private GodSimRunner _runner;
        private GodSimulation _sim;
        private Action<GodSimulation> _onSimulationCreated;
        private Action<GodPowerCastEvent> _onPowerCast;
        private Action<GodCommandRejectedEvent> _onCommandRejected;
        private Action<DisasterEvent> _onDisaster;
        private Action<AgeAdvancedEvent> _onAgeAdvanced;
        private Action<WarDeclaredEvent> _onWarDeclared;
        private Action<BuildingCompletedEvent> _onBuildingCompleted;
        private Action<BattleEvent> _onBattle;
        private Action<SettlementFoundedEvent> _onSettlementFounded;
        private Action<ChronicleAddedEvent> _onChronicle;
        private float _nextRunnerCheck;

        // Event queue (filled by sim handlers, drained in Update).
        private readonly object _pendingLock = new object();
        private List<Pending> _pending = new List<Pending>(64);
        private List<Pending> _draining = new List<Pending>(64);
        private const int MaxPending = 256;

        // Rate limiting.
        private readonly float[] _rate = new float[SoundCount];
        private readonly float[] _tokens = new float[SoundCount];
        private readonly float[] _lastPowerSound = new float[SoundCount];
        private float _worldTokens;
        private float _lastStingerTime = -100f;

        // State.
        private ProceduralSynth.Xorshift32 _rng = new ProceduralSynth.Xorshift32(0xA0D10u);
        private float _pauseDuck = 1f;
        private bool _hasCamera;
        private Vector3 _cameraPos;
        private Vector3 _cameraFocus;
        private bool _listenerChecked;
        private bool _duplicate;

        // =============================================================================================
        // Public API
        // =============================================================================================

        /// <summary>True once every clip has been synthesized.</summary>
        public bool ClipsReady => _clipsReady;

        /// <summary>Master volume 0..1 (persisted).</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set { _masterVolume = Clamp01Safe(value, _masterVolume); PlayerPrefs.SetFloat(PrefMaster, _masterVolume); }
        }

        /// <summary>Music volume 0..1 (persisted).</summary>
        public float MusicVolume
        {
            get => _musicVolume;
            set { _musicVolume = Clamp01Safe(value, _musicVolume); PlayerPrefs.SetFloat(PrefMusic, _musicVolume); }
        }

        /// <summary>Sound-effects (and ambience) volume 0..1 (persisted).</summary>
        public float SfxVolume
        {
            get => _sfxVolume;
            set { _sfxVolume = Clamp01Safe(value, _sfxVolume); PlayerPrefs.SetFloat(PrefSfx, _sfxVolume); }
        }

        /// <summary>Silence world audio and music while the simulation is paused (persisted).</summary>
        public bool MuteWhenPaused
        {
            get => _muteWhenPaused;
            set { _muteWhenPaused = value; PlayerPrefs.SetInt(PrefMuteWhenPaused, value ? 1 : 0); }
        }

        /// <summary>Writes the volume settings to disk now.</summary>
        public void SaveSettings() => PlayerPrefs.Save();

        /// <summary>Overrides the per-kind rate limit (one-shots per second; 0 disables the kind).</summary>
        public void SetRateLimit(Sound sound, float perSecond)
        {
            int k = (int)sound;
            if (k < 0 || k >= SoundCount) return;
            _rate[k] = float.IsNaN(perSecond) ? 0f : Mathf.Clamp(perSecond, 0f, 1000f);
            _tokens[k] = Mathf.Min(_tokens[k], Mathf.Max(1f, _rate[k]));
        }

        /// <summary>HUD button click.</summary>
        public void PlayUiClick() => Play2D(Sound.UiClick, 0, 1f);

        /// <summary>Invalid action / rejected command.</summary>
        public void PlayUiError() => Play2D(Sound.UiError, 0, 1f);

        /// <summary>Power selected in the HUD (themed by its category).</summary>
        public void PlayPowerSelect(GodPowerType power) => Play2D(Sound.PowerSelect, (int)GodSimAudioMapping.CategoryOf(power), 1f);

        /// <summary>Plays a non-positional sound (respects rate limits). variant &lt; 0 = random.</summary>
        public bool Play2D(Sound sound, int variant = -1, float volume = 1f)
        {
            AudioClip clip = ClipFor(sound, variant);
            if (clip == null || !TryConsume(sound)) return false;
            if (IsStinger(sound)) _lastStingerTime = Time.unscaledTime;
            PlayOn(_uiPool, clip, BaseVolumes[(int)sound] * Clamp01Safe(volume, 1f), 1f, Vector3.zero, false, IsStinger(sound));
            return true;
        }

        /// <summary>Plays a positional sound at a world position (respects per-kind and global rate limits).</summary>
        public bool PlayAt(Sound sound, Vector3 worldPosition, int variant = -1, float volume = 1f)
        {
            AudioClip clip = ClipFor(sound, variant);
            if (clip == null || !TryConsume(sound) || !TryConsumeWorld()) return false;
            float pitch = 1f + _rng.NextSigned() * 0.04f;
            PlayOn(_worldPool, clip, BaseVolumes[(int)sound] * Clamp01Safe(volume, 1f), pitch, worldPosition, true, false);
            return true;
        }

        /// <summary>Plays a positional sound at a simulation tile.</summary>
        public bool PlayAtTile(Sound sound, Vec2i tile, int variant = -1, float volume = 1f)
        {
            WorldMap map = _sim != null && _sim.State != null ? _sim.State.Map : null;
            if (map == null || !tile.IsValid) return Play2D(sound, variant, volume * 0.6f);
            return PlayAt(sound, GodSimCoords.TileToWorld(map, map.ClampToBounds(tile)), variant, volume);
        }

        /// <summary>Creates a GodSimAudio in the scene if none exists, and returns the instance.</summary>
        public static GodSimAudio EnsureExists()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<GodSimAudio>();
            if (existing != null) return existing;
            var go = new GameObject("GodSimAudio");
            return go.AddComponent<GodSimAudio>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            // Zero-setup integration: if the scene has a simulation runner but no audio, add audio.
            if (FindFirstObjectByType<GodSimAudio>() == null && FindFirstObjectByType<GodSimRunner>() != null) EnsureExists();
        }

        // =============================================================================================
        // Lifecycle
        // =============================================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                _duplicate = true;
                Destroy(this);
                return;
            }
            Instance = this;
            for (int i = 0; i < SoundCount; i++)
            {
                _rate[i] = DefaultRatePerSecond[i];
                _tokens[i] = Mathf.Max(1f, _rate[i]);
                _lastPowerSound[i] = -100f;
            }
            _worldTokens = _maxWorldOneShotsPerSecond;
            _onSimulationCreated = OnSimulationCreated;
            _onPowerCast = OnGodPowerCast;
            _onCommandRejected = OnCommandRejected;
            _onDisaster = OnDisaster;
            _onAgeAdvanced = OnAgeAdvanced;
            _onWarDeclared = OnWarDeclared;
            _onBuildingCompleted = OnBuildingCompleted;
            _onBattle = OnBattle;
            _onSettlementFounded = OnSettlementFounded;
            _onChronicle = OnChronicle;
            LoadSettings();
            CreateSources();
        }

        private void OnEnable()
        {
            if (_duplicate) return;
            if (Instance == null) Instance = this;
            TryHookRunner();
        }

        private void Start()
        {
            if (_duplicate) return;
            EnsureListener();
            _useStreaming = _streamMusic && Application.platform != RuntimePlatform.WebGLPlayer;
            _bed = new GenerativeMusicBed(0x60D5u);
            UpdateHarmony(true);
            if (_useStreaming)
            {
                _musicStreamClip = AudioClip.Create("GodSim.MusicBed", ProceduralSynth.SampleRate * 30, 2, ProceduralSynth.SampleRate, true, OnMusicPcmRead);
                _ownedClips.Add(_musicStreamClip);
                _musicA.clip = _musicStreamClip;
                _musicA.volume = 0f;
                _musicA.Play();
            }
            else
            {
                StartCoroutine(RenderMusicLoop());
            }
            StartCoroutine(BuildClips());
        }

        private void OnDisable()
        {
            if (_duplicate) return;
            UnhookRunner();
            DetachSimulation();
            PlayerPrefs.Save();
        }

        private void OnDestroy()
        {
            if (_duplicate) return;
            if (Instance == this) Instance = null;
            if (_batch != null) _batch.Cancel = true;
            for (int i = 0; i < _ownedClips.Count; i++) if (_ownedClips[i] != null) Destroy(_ownedClips[i]);
            _ownedClips.Clear();
        }

        private void OnApplicationQuit() => PlayerPrefs.Save();

        private void Update()
        {
            if (_duplicate) return;
            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);

            if (now >= _nextRunnerCheck)
            {
                _nextRunnerCheck = now + 0.5f;
                RefreshRunner();
                if (!_listenerChecked) EnsureListener();
            }

            RefillTokens(dt);
            UpdateCamera();

            bool paused = _runner != null && _runner.Paused;
            _pauseDuck = Mathf.MoveTowards(_pauseDuck, _muteWhenPaused && paused ? 0f : 1f, dt * 3f);

            DrainPending(now);
            UpdateVoices();
            UpdateFires(now, dt);
            UpdateAmbience(dt);
            UpdateMusic(now, dt);
        }

        // =============================================================================================
        // Setup
        // =============================================================================================

        private void LoadSettings()
        {
            _masterVolume = Clamp01Safe(PlayerPrefs.GetFloat(PrefMaster, _masterVolume), 0.8f);
            _musicVolume = Clamp01Safe(PlayerPrefs.GetFloat(PrefMusic, _musicVolume), 0.5f);
            _sfxVolume = Clamp01Safe(PlayerPrefs.GetFloat(PrefSfx, _sfxVolume), 0.8f);
            _muteWhenPaused = PlayerPrefs.GetInt(PrefMuteWhenPaused, _muteWhenPaused ? 1 : 0) != 0;
        }

        private void CreateSources()
        {
            var uiRoot = new GameObject("UI Voices");
            uiRoot.transform.SetParent(transform, false);
            _uiPool = new Voice[Mathf.Max(2, _uiVoices)];
            for (int i = 0; i < _uiPool.Length; i++)
            {
                var src = uiRoot.AddComponent<AudioSource>();
                Configure2D(src, false);
                src.priority = 32;
                _uiPool[i] = new Voice { Source = src, World = false };
            }

            var worldRoot = new GameObject("World Voices");
            worldRoot.transform.SetParent(transform, false);
            _worldPool = new Voice[Mathf.Max(4, _worldVoices)];
            for (int i = 0; i < _worldPool.Length; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(worldRoot.transform, false);
                var src = go.AddComponent<AudioSource>();
                Configure3D(src, false);
                _worldPool[i] = new Voice { Source = src, World = true };
            }

            _fires = new FireEmitter[Mathf.Max(1, _fireEmitters)];
            for (int i = 0; i < _fires.Length; i++)
            {
                var go = new GameObject("Fire " + i);
                go.transform.SetParent(worldRoot.transform, false);
                var src = go.AddComponent<AudioSource>();
                Configure3D(src, true);
                _fires[i] = new FireEmitter { Source = src, BaseVolume = BaseVolumes[(int)Sound.Fire] };
            }

            var ambRoot = new GameObject("Ambience");
            ambRoot.transform.SetParent(transform, false);
            _windSrc = ambRoot.AddComponent<AudioSource>();
            _rainSrc = ambRoot.AddComponent<AudioSource>();
            _natureSrc = ambRoot.AddComponent<AudioSource>();
            Configure2D(_windSrc, true);
            Configure2D(_rainSrc, true);
            Configure2D(_natureSrc, true);

            var musicRoot = new GameObject("Music");
            musicRoot.transform.SetParent(transform, false);
            _musicA = musicRoot.AddComponent<AudioSource>();
            _musicB = musicRoot.AddComponent<AudioSource>();
            Configure2D(_musicA, true);
            Configure2D(_musicB, true);
            _musicA.priority = 0;
            _musicB.priority = 0;
        }

        private static void Configure2D(AudioSource src, bool loop)
        {
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.volume = 0f;
            src.dopplerLevel = 0f;
            src.ignoreListenerPause = false;
        }

        private void Configure3D(AudioSource src, bool loop)
        {
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = Mathf.Max(0.1f, _minDistance);
            src.maxDistance = Mathf.Max(src.minDistance + 1f, _maxDistance);
            src.dopplerLevel = 0f;
            src.spread = 30f;
            src.volume = 0f;
            src.priority = 128;
        }

        private void EnsureListener()
        {
            if (FindFirstObjectByType<AudioListener>() != null) { _listenerChecked = true; return; }
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.gameObject.AddComponent<AudioListener>();
                _listenerChecked = true;
            }
            else if (Time.unscaledTime > 3f)
            {
                gameObject.AddComponent<AudioListener>();
                _listenerChecked = true;
            }
        }

        private List<ClipJob> CreateJobs()
        {
            var jobs = new List<ClipJob>(48);
            for (int i = 0; i < SoundCount; i++) _clips[i] = new AudioClip[1];
            _clips[(int)Sound.PowerSelect] = new AudioClip[4];
            _clips[(int)Sound.Cast] = new AudioClip[4];
            _clips[(int)Sound.Lightning] = new AudioClip[2];
            _clips[(int)Sound.AgeBell] = new AudioClip[Counts.Ages];
            _clips[(int)Sound.Construction] = new AudioClip[2];
            _clips[(int)Sound.Battle] = new AudioClip[2];

            Add(jobs, Sound.UiClick, 0, () => ProceduralSynth.UiClick());
            Add(jobs, Sound.UiError, 0, () => ProceduralSynth.UiError());
            for (int c = 0; c < 4; c++)
            {
                var cat = (GodPowerCategory)c;
                Add(jobs, Sound.PowerSelect, c, () => ProceduralSynth.PowerSelect(cat));
            }
            for (int c = 0; c < 4; c++)
            {
                var cat = (GodPowerCategory)c;
                Add(jobs, Sound.Cast, c, () => ProceduralSynth.Cast(cat));
            }
            Add(jobs, Sound.Lightning, 0, () => ProceduralSynth.LightningCrack());
            Add(jobs, Sound.Lightning, 1, () => ProceduralSynth.WrathThunder(77));
            Add(jobs, Sound.Meteor, 0, () => ProceduralSynth.MeteorImpact());
            Add(jobs, Sound.Earthquake, 0, () => ProceduralSynth.EarthquakeRumble());
            Add(jobs, Sound.Construction, 0, () => ProceduralSynth.ConstructionHammer(41));
            Add(jobs, Sound.Construction, 1, () => ProceduralSynth.ConstructionHammer(97));
            Add(jobs, Sound.Battle, 0, () => ProceduralSynth.BattleClash(43));
            Add(jobs, Sound.Battle, 1, () => ProceduralSynth.BattleClash(101));
            Add(jobs, Sound.SettlementFounded, 0, () => ProceduralSynth.SettlementFounded());
            Add(jobs, Sound.WarDrums, 0, () => ProceduralSynth.WarDrums());
            Add(jobs, Sound.Wonder, 0, () => ProceduralSynth.WonderHarp());
            for (int a = 0; a < Counts.Ages; a++)
            {
                var age = (AgeId)a;
                Add(jobs, Sound.AgeBell, a, () => ProceduralSynth.AgeBell(age));
            }
            Add(jobs, Sound.Legendary, 0, () => ProceduralSynth.Legendary());
            Add(jobs, Sound.Flood, 0, () => ProceduralSynth.FloodSurge());
            Add(jobs, Sound.Omen, 0, () => ProceduralSynth.Omen());
            jobs.Add(new ClipJob { Name = "GodSim.Fire", Generate = () => ProceduralSynth.FireCrackleLoop(), Assign = c => { _fireClip = c; _clips[(int)Sound.Fire][0] = c; } });
            jobs.Add(new ClipJob { Name = "GodSim.Wind", Generate = () => ProceduralSynth.WindLoop(), Assign = c => _windClip = c });
            jobs.Add(new ClipJob { Name = "GodSim.Nature", Generate = () => ProceduralSynth.NatureLoop(), Assign = c => _natureClip = c });
            jobs.Add(new ClipJob { Name = "GodSim.Rain", Generate = () => ProceduralSynth.RainLoop(), Assign = c => _rainClip = c });
            return jobs;
        }

        private void Add(List<ClipJob> jobs, Sound sound, int variant, Func<float[]> gen)
        {
            int s = (int)sound;
            jobs.Add(new ClipJob { Name = "GodSim." + sound + "." + variant, Generate = gen, Assign = c => _clips[s][variant] = c });
        }

        private IEnumerator BuildClips()
        {
            List<ClipJob> jobs = CreateJobs();
            float start = Time.realtimeSinceStartup;
            bool threaded = _backgroundSynthesis && Application.platform != RuntimePlatform.WebGLPlayer;
            if (threaded)
            {
                _batch = new SynthBatch(jobs);
                if (!ThreadPool.QueueUserWorkItem(_batch.Run)) { threaded = false; _batch = null; }
            }
            float frameStart = Time.realtimeSinceStartup;
            int next = 0;
            while (next < jobs.Count)
            {
                ClipJob job = jobs[next];
                if (threaded)
                {
                    if (next >= _batch.Done) { yield return null; frameStart = Time.realtimeSinceStartup; continue; }
                }
                else
                {
                    try { job.Result = job.Generate(); }
                    catch (Exception ex) { job.Error = ex.Message; }
                }
                UploadClip(job);
                next++;
                if ((Time.realtimeSinceStartup - frameStart) * 1000f >= _clipBuildBudgetMs)
                {
                    yield return null;
                    frameStart = Time.realtimeSinceStartup;
                }
            }
            _batch = null;
            _clipsReady = true;
            StartAmbience();
            if (_logStartup) Debug.Log("[GodSimAudio] Synthesized " + jobs.Count + " clips in " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("F0") + " ms (wall, " + (threaded ? "worker thread" : "main thread") + ").");
        }

        private void UploadClip(ClipJob job)
        {
            if (job.Error != null) Debug.LogWarning("[GodSimAudio] Failed to synthesize " + job.Name + ": " + job.Error);
            float[] pcm = job.Result;
            job.Result = null;
            if (pcm == null || pcm.Length == 0) return;
            AudioClip clip = AudioClip.Create(job.Name, pcm.Length, 1, ProceduralSynth.SampleRate, false);
            if (clip == null) return;
            clip.SetData(pcm, 0);
            _ownedClips.Add(clip);
            job.Assign(clip);
        }

        private void StartAmbience()
        {
            StartLoop(_windSrc, _windClip);
            StartLoop(_rainSrc, _rainClip);
            StartLoop(_natureSrc, _natureClip);
        }

        private static void StartLoop(AudioSource src, AudioClip clip)
        {
            if (src == null || clip == null) return;
            src.clip = clip;
            src.volume = 0f;
            src.Play();
        }

        // =============================================================================================
        // Simulation hookup
        // =============================================================================================

        private void TryHookRunner()
        {
            if (!ReferenceEquals(_runner, null)) return;
            GodSimRunner r = GodSimRunner.Instance;
            if (r == null) return;
            _runner = r;
            _runner.SimulationCreated += _onSimulationCreated;
            if (_runner.Simulation != null) AttachSimulation(_runner.Simulation);
        }

        private void UnhookRunner()
        {
            if (ReferenceEquals(_runner, null)) return;
            _runner.SimulationCreated -= _onSimulationCreated;
            _runner = null;
        }

        private void RefreshRunner()
        {
            // Runner destroyed (Unity null) or replaced (scene reload): rehook.
            if (!ReferenceEquals(_runner, null) && (_runner == null || GodSimRunner.Instance != _runner))
            {
                UnhookRunner();
                DetachSimulation();
            }
            if (ReferenceEquals(_runner, null)) TryHookRunner();
            else if (_runner.Simulation != null && !ReferenceEquals(_runner.Simulation, _sim)) AttachSimulation(_runner.Simulation);
        }

        private void OnSimulationCreated(GodSimulation sim) => AttachSimulation(sim);

        private void AttachSimulation(GodSimulation sim)
        {
            if (ReferenceEquals(sim, _sim)) return;
            DetachSimulation();
            if (sim == null || sim.Events == null) return;
            _sim = sim;
            EventBus bus = sim.Events;
            bus.Subscribe(_onPowerCast);
            bus.Subscribe(_onCommandRejected);
            bus.Subscribe(_onDisaster);
            bus.Subscribe(_onAgeAdvanced);
            bus.Subscribe(_onWarDeclared);
            bus.Subscribe(_onBuildingCompleted);
            bus.Subscribe(_onBattle);
            bus.Subscribe(_onSettlementFounded);
            bus.Subscribe(_onChronicle);
            _harmonyRoot = -1; // force a harmony refresh for the new world
        }

        private void DetachSimulation()
        {
            if (_sim != null && _sim.Events != null)
            {
                EventBus bus = _sim.Events;
                bus.Unsubscribe(_onPowerCast);
                bus.Unsubscribe(_onCommandRejected);
                bus.Unsubscribe(_onDisaster);
                bus.Unsubscribe(_onAgeAdvanced);
                bus.Unsubscribe(_onWarDeclared);
                bus.Unsubscribe(_onBuildingCompleted);
                bus.Unsubscribe(_onBattle);
                bus.Unsubscribe(_onSettlementFounded);
                bus.Unsubscribe(_onChronicle);
            }
            _sim = null;
            lock (_pendingLock) _pending.Clear();
            if (_fires != null)
                for (int i = 0; i < _fires.Length; i++) { _fires[i].EndTime = 0f; }
        }

        // =============================================================================================
        // Event handlers (may run on the simulation thread: enqueue only, no Unity calls)
        // =============================================================================================

        private void Enqueue(Pending p)
        {
            lock (_pendingLock)
            {
                if (_pending.Count >= MaxPending) return;
                _pending.Add(p);
            }
        }

        private void EnqueueAt(Sound s, int variant, Vec2i tile, float volume, bool fromPower = false)
            => Enqueue(new Pending { Sound = s, Variant = variant, Tile = tile, Positional = tile.IsValid, Volume = volume, FromPower = fromPower });

        private void Enqueue2D(Sound s, int variant, float volume)
            => Enqueue(new Pending { Sound = s, Variant = variant, Tile = Vec2i.Invalid, Positional = false, Volume = volume });

        private void OnGodPowerCast(GodPowerCastEvent e)
        {
            if (!e.Success) { Enqueue2D(Sound.UiError, 0, 0.7f); return; }
            GodPowerCategory cat = GodSimAudioMapping.CategoryOf(e.Power);
            EnqueueAt(Sound.Cast, (int)cat, e.Target, 1f);
            switch (e.Power)
            {
                case GodPowerType.Lightning: EnqueueAt(Sound.Lightning, 0, e.Target, 1f, true); break;
                case GodPowerType.Meteor: EnqueueAt(Sound.Meteor, 0, e.Target, 1f, true); break;
                case GodPowerType.Earthquake: EnqueueAt(Sound.Earthquake, 0, e.Target, 1f, true); break;
                case GodPowerType.Flood: EnqueueAt(Sound.Flood, 0, e.Target, 1f, true); break;
                case GodPowerType.Wildfire:
                    Enqueue(new Pending { Sound = Sound.Fire, Tile = e.Target, Positional = e.Target.IsValid, Volume = 1f, FromPower = true, FireSeconds = 14f });
                    break;
            }
        }

        private void OnCommandRejected(GodCommandRejectedEvent e) => Enqueue2D(Sound.UiError, 0, 0.8f);

        private void OnDisaster(DisasterEvent e)
        {
            float sev = MathUtil.Clamp01(MathUtil.Sanitize(e.Severity, 0.5f));
            float vol = 0.6f + 0.4f * sev;
            switch (e.Type)
            {
                case DisasterType.Lightning: EnqueueAt(Sound.Lightning, 0, e.Position, vol); break;
                case DisasterType.Storm: EnqueueAt(Sound.Lightning, 1, e.Position, vol); break;
                case DisasterType.Meteor: EnqueueAt(Sound.Meteor, 0, e.Position, vol); break;
                case DisasterType.Volcano:
                    EnqueueAt(Sound.Meteor, 0, e.Position, vol);
                    EnqueueAt(Sound.Earthquake, 0, e.Position, vol);
                    break;
                case DisasterType.Earthquake: EnqueueAt(Sound.Earthquake, 0, e.Position, vol); break;
                case DisasterType.Flood: EnqueueAt(Sound.Flood, 0, e.Position, vol); break;
                case DisasterType.Wildfire:
                    Enqueue(new Pending { Sound = Sound.Fire, Tile = e.Position, Positional = e.Position.IsValid, Volume = vol, FireSeconds = 8f + 12f * sev });
                    break;
                default: // Drought, Plague, Famine
                    Enqueue2D(Sound.Omen, 0, 0.5f + 0.4f * sev);
                    break;
            }
        }

        private void OnAgeAdvanced(AgeAdvancedEvent e) => Enqueue2D(Sound.AgeBell, MathUtil.Clamp((int)e.NewAge, 0, Counts.Ages - 1), 1f);

        private void OnWarDeclared(WarDeclaredEvent e) => Enqueue2D(Sound.WarDrums, 0, 1f);

        private void OnBuildingCompleted(BuildingCompletedEvent e)
        {
            if (GodSimAudioMapping.IsWonder(e.Type)) { Enqueue2D(Sound.Wonder, 0, 1f); return; }
            GodSimulation sim = _sim;
            if (sim == null || sim.State == null) return;
            Building b = sim.State.Buildings.Get(e.BuildingId);
            if (b == null) return;
            Enqueue(new Pending { Sound = Sound.Construction, Variant = -1, Tile = b.Center, Positional = true, Volume = 1f, NearCameraOnly = true });
        }

        private void OnBattle(BattleEvent e)
        {
            int losses = Math.Max(0, e.AttackerLosses) + Math.Max(0, e.DefenderLosses);
            EnqueueAt(Sound.Battle, -1, e.Position, 0.5f + 0.5f * MathUtil.Clamp01(losses / 40f));
        }

        private void OnSettlementFounded(SettlementFoundedEvent e) => EnqueueAt(Sound.SettlementFounded, 0, e.Position, 1f);

        private void OnChronicle(ChronicleAddedEvent e)
        {
            if (e.Entry.Importance >= 3) Enqueue2D(Sound.Legendary, 0, 1f);
        }

        // =============================================================================================
        // Playback
        // =============================================================================================

        private void DrainPending(float now)
        {
            lock (_pendingLock)
            {
                if (_pending.Count == 0) return;
                List<Pending> t = _pending;
                _pending = _draining;
                _draining = t;
            }
            // Legendary chronicle stingers go last so a same-tick age bell / war drums / wonder harp wins.
            for (int i = 0; i < _draining.Count; i++) if (_draining[i].Sound != Sound.Legendary) Process(_draining[i], now);
            for (int i = 0; i < _draining.Count; i++) if (_draining[i].Sound == Sound.Legendary) Process(_draining[i], now);
            _draining.Clear();
        }

        private void Process(Pending p, float now)
        {
            int k = (int)p.Sound;
            if (k < 0 || k >= SoundCount) return;
            if (p.FromPower) _lastPowerSound[k] = now;
            else if (p.Sound != Sound.Cast && now - _lastPowerSound[k] < 0.75f) return; // the power already played it

            WorldMap map = _sim != null && _sim.State != null ? _sim.State.Map : null;
            bool positional = p.Positional && map != null && p.Tile.IsValid;
            Vector3 pos = positional ? GodSimCoords.TileToWorld(map, map.ClampToBounds(p.Tile)) : Vector3.zero;

            if (p.FireSeconds > 0f)
            {
                if (positional) StartFire(pos, p.FireSeconds, p.Volume, now);
                return;
            }
            if (p.NearCameraOnly && (!positional || !IsNearCamera(pos))) return;
            if (p.Sound == Sound.Legendary && now - _lastStingerTime < _stingerSpacingSeconds) return;

            if (positional) PlayAt(p.Sound, pos, p.Variant, p.Volume);
            else Play2D(p.Sound, p.Variant, p.Volume);
        }

        private AudioClip ClipFor(Sound sound, int variant)
        {
            int k = (int)sound;
            if (k < 0 || k >= SoundCount) return null;
            AudioClip[] arr = _clips[k];
            if (arr == null || arr.Length == 0) return null;
            int v = variant < 0 ? _rng.Range(0, arr.Length) : variant % arr.Length;
            return arr[v];
        }

        private void PlayOn(Voice[] pool, AudioClip clip, float baseVolume, float pitch, Vector3 pos, bool world, bool stinger)
        {
            if (pool == null || clip == null) return;
            Voice v = Acquire(pool);
            if (v == null) return;
            AudioSource src = v.Source;
            src.Stop();
            src.clip = clip;
            src.pitch = pitch;
            v.BaseVolume = baseVolume;
            v.StartTime = Time.unscaledTime;
            v.World = world || stinger; // stingers duck with pause like world audio
            if (world) src.transform.position = pos;
            src.volume = VolumeFor(v);
            src.Play();
        }

        private static Voice Acquire(Voice[] pool)
        {
            Voice oldest = null;
            for (int i = 0; i < pool.Length; i++)
            {
                Voice v = pool[i];
                if (v == null || v.Source == null) continue;
                if (!v.Source.isPlaying) return v;
                if (oldest == null || v.StartTime < oldest.StartTime) oldest = v;
            }
            return oldest;
        }

        private float VolumeFor(Voice v)
        {
            float vol = v.BaseVolume * _sfxVolume * _masterVolume;
            if (v.World) vol *= _pauseDuck;
            return Mathf.Clamp01(vol);
        }

        private void UpdateVoices()
        {
            UpdatePool(_uiPool);
            UpdatePool(_worldPool);
        }

        private void UpdatePool(Voice[] pool)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++)
            {
                Voice v = pool[i];
                if (v == null || v.Source == null || !v.Source.isPlaying) continue;
                v.Source.volume = VolumeFor(v);
            }
        }

        private void StartFire(Vector3 pos, float seconds, float volume, float now)
        {
            if (_fireClip == null || _fires == null || !TryConsume(Sound.Fire)) return;
            FireEmitter target = null;
            for (int i = 0; i < _fires.Length; i++)
            {
                FireEmitter f = _fires[i];
                // Reuse an emitter already burning nearby.
                if (f.Source.isPlaying && (f.Source.transform.position - pos).sqrMagnitude < 36f) { target = f; break; }
            }
            if (target == null)
            {
                for (int i = 0; i < _fires.Length; i++)
                {
                    FireEmitter f = _fires[i];
                    if (!f.Source.isPlaying) { target = f; break; }
                    if (target == null || f.StartTime < target.StartTime) target = f;
                }
            }
            if (target == null) return;
            if (!target.Source.isPlaying)
            {
                target.Level = 0f;
                target.Source.clip = _fireClip;
                target.Source.transform.position = pos;
                target.Source.pitch = 1f + _rng.NextSigned() * 0.06f;
                target.Source.volume = 0f;
                target.Source.Play();
                target.StartTime = now;
            }
            target.BaseVolume = BaseVolumes[(int)Sound.Fire] * Clamp01Safe(volume, 1f);
            target.EndTime = Mathf.Max(target.EndTime, now + Mathf.Clamp(seconds, 1f, 60f));
        }

        private void UpdateFires(float now, float dt)
        {
            if (_fires == null) return;
            for (int i = 0; i < _fires.Length; i++)
            {
                FireEmitter f = _fires[i];
                if (f.Source == null || !f.Source.isPlaying) continue;
                f.Level = Mathf.MoveTowards(f.Level, now < f.EndTime ? 1f : 0f, dt * (now < f.EndTime ? 1.5f : 0.4f));
                f.Source.volume = Mathf.Clamp01(f.Level * f.BaseVolume * _sfxVolume * _masterVolume * _pauseDuck);
                if (f.Level <= 0f && now >= f.EndTime) f.Source.Stop();
            }
        }

        // =============================================================================================
        // Rate limiting
        // =============================================================================================

        private void RefillTokens(float dt)
        {
            for (int i = 0; i < SoundCount; i++)
            {
                float burst = Mathf.Max(1f, _rate[i]);
                _tokens[i] = Mathf.Min(burst, _tokens[i] + _rate[i] * dt);
            }
            float wb = Mathf.Max(1f, _maxWorldOneShotsPerSecond);
            _worldTokens = Mathf.Min(wb, _worldTokens + _maxWorldOneShotsPerSecond * dt);
        }

        private bool TryConsume(Sound s)
        {
            int k = (int)s;
            if (_tokens[k] < 1f) return false;
            _tokens[k] -= 1f;
            return true;
        }

        private bool TryConsumeWorld()
        {
            if (_worldTokens < 1f) return false;
            _worldTokens -= 1f;
            return true;
        }

        // =============================================================================================
        // Camera, ambience, music
        // =============================================================================================

        private void UpdateCamera()
        {
            Camera cam = Camera.main;
            _hasCamera = cam != null;
            if (!_hasCamera) return;
            Transform t = cam.transform;
            _cameraPos = t.position;
            Vector3 fwd = t.forward;
            if (fwd.y < -0.01f)
            {
                float dist = Mathf.Min(_cameraPos.y / -fwd.y, 4000f);
                _cameraFocus = _cameraPos + fwd * Mathf.Max(0f, dist);
            }
            else _cameraFocus = _cameraPos;
        }

        private bool IsNearCamera(Vector3 pos)
        {
            if (!_hasCamera) return true;
            float dx = pos.x - _cameraFocus.x, dz = pos.z - _cameraFocus.z;
            float r = _constructionHearingRadius + Mathf.Max(0f, _cameraPos.y) * 0.5f;
            return dx * dx + dz * dz <= r * r;
        }

        private void UpdateAmbience(float dt)
        {
            if (!_clipsReady) return;
            float h = _hasCamera ? Mathf.InverseLerp(_lowCameraHeight, _highCameraHeight, _cameraPos.y) : 0.5f;
            WorldState state = _sim != null ? _sim.State : null;
            float storm = 0f;
            bool raining = false, winter = false;
            if (state != null)
            {
                storm = Mathf.Clamp01(MathUtil.Sanitize(state.Weather.StormIntensity));
                raining = state.Weather.RainTicksRemaining > 0 || storm > 0.3f;
                winter = state.Weather.Season == Season.Winter;
            }
            float wind = Mathf.Lerp(0.15f, 0.85f, h) + storm * 0.4f;
            float nature = state != null ? Mathf.Lerp(0.75f, 0f, h * 1.3f) * (winter ? 0.35f : 1f) * (raining ? 0.45f : 1f) : 0f;
            float rain = raining ? Mathf.Lerp(0.6f, 0.35f, h) + storm * 0.3f : 0f;

            float step = _ambienceFadePerSecond * dt;
            _windLevel = Mathf.MoveTowards(_windLevel, Mathf.Clamp01(wind), step);
            _natureLevel = Mathf.MoveTowards(_natureLevel, Mathf.Clamp01(nature), step);
            _rainLevel = Mathf.MoveTowards(_rainLevel, Mathf.Clamp01(rain), step);

            float g = _ambienceLevel * _sfxVolume * _masterVolume * _pauseDuck;
            if (_windSrc != null) _windSrc.volume = Mathf.Clamp01(_windLevel * g * 0.7f);
            if (_natureSrc != null) _natureSrc.volume = Mathf.Clamp01(_natureLevel * g * 0.8f);
            if (_rainSrc != null) _rainSrc.volume = Mathf.Clamp01(_rainLevel * g);
        }

        private void OnMusicPcmRead(float[] data)
        {
            // Unity may call this off the main thread; the bed never allocates and only reads volatile requests.
            GenerativeMusicBed bed = _bed;
            if (bed != null) bed.Render(data, 2);
            else Array.Clear(data, 0, data.Length);
        }

        private bool UpdateHarmony(bool force)
        {
            AgeId age = AgeId.Dawn;
            float align = 0f;
            WorldState state = _sim != null ? _sim.State : null;
            if (state != null)
            {
                age = state.WorldAge;
                align = MathUtil.Sanitize(state.God.Alignment);
            }
            ProceduralSynth.Mode mode = GodSimAudioMapping.MusicModeFor(align);
            int root = GodSimAudioMapping.MusicRootFor(age);
            if (!force && mode == _harmonyMode && root == _harmonyRoot) return false;
            _harmonyMode = mode;
            _harmonyRoot = root;
            if (_bed != null)
                _bed.SetHarmony(mode, root, GodSimAudioMapping.MusicBrightnessFor(age, align), GodSimAudioMapping.MusicDensityFor(age));
            return true;
        }

        private void UpdateMusic(float now, float dt)
        {
            if (now >= _nextHarmonyCheck)
            {
                _nextHarmonyCheck = now + Mathf.Max(0.25f, _harmonyCheckSeconds);
                bool changed = UpdateHarmony(false);
                if (changed && !_useStreaming)
                {
                    if (_musicRendering) _musicDirty = true;
                    else StartCoroutine(RenderMusicLoop());
                }
            }
            float target = _musicVolume * _masterVolume * _pauseDuck;
            if (_useStreaming)
            {
                if (_musicA != null) _musicA.volume = Mathf.Clamp01(target);
                return;
            }
            _musicXfade = Mathf.MoveTowards(_musicXfade, 1f, dt / 4f);
            float a = _musicOnA ? _musicXfade : 1f - _musicXfade;
            if (_musicA != null) _musicA.volume = Mathf.Clamp01(target * a);
            if (_musicB != null) _musicB.volume = Mathf.Clamp01(target * (1f - a));
        }

        /// <summary>Non-streaming fallback: renders a seamless loop over several frames, then crossfades to it.</summary>
        private IEnumerator RenderMusicLoop()
        {
            _musicRendering = true;
            _musicDirty = false;
            if (_bed != null) _bed.ApplyHarmonyNow();
            int sr = ProceduralSynth.SampleRate;
            int loopFrames = Mathf.Max(sr * 8, (int)(Mathf.Max(8f, _prerenderedLoopSeconds) * sr));
            int xfFrames = sr * 2;
            int total = loopFrames + xfFrames;
            var pcm = new float[total * 2];
            int chunk = sr / 2;
            for (int f = 0; f < total; f += chunk)
            {
                if (_bed != null) _bed.Render(pcm, f, Math.Min(chunk, total - f), 2);
                yield return null;
            }
            float[] loop = ProceduralSynth.MakeSeamlessLoop(pcm, loopFrames, 2);
            AudioClip clip = AudioClip.Create("GodSim.MusicLoop", loopFrames, 2, sr, false);
            clip.SetData(loop, 0);
            _ownedClips.Add(clip);

            bool first = _musicLoopA == null && _musicLoopB == null;
            AudioSource next = first ? _musicA : (_musicOnA ? _musicB : _musicA);
            AudioClip old = first ? null : (_musicOnA ? _musicLoopA : _musicLoopB);
            next.clip = clip;
            next.volume = 0f;
            next.Play();
            if (ReferenceEquals(next, _musicA)) { _musicLoopA = clip; _musicOnA = true; }
            else { _musicLoopB = clip; _musicOnA = false; }
            _musicXfade = 0f;
            if (old != null && old != clip)
            {
                yield return null;
                // Free the previous loop once the crossfade has finished.
                float until = Time.unscaledTime + 5f;
                while (Time.unscaledTime < until) yield return null;
                AudioSource oldSrc = _musicOnA ? _musicB : _musicA;
                if (oldSrc.clip == old) { oldSrc.Stop(); oldSrc.clip = null; }
                if (_musicLoopA == old) _musicLoopA = null;
                if (_musicLoopB == old) _musicLoopB = null;
                _ownedClips.Remove(old);
                Destroy(old);
            }
            _musicRendering = false;
            if (_musicDirty) StartCoroutine(RenderMusicLoop());
        }

        private static float Clamp01Safe(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return Mathf.Clamp01(fallback);
            return Mathf.Clamp01(v);
        }
    }
}
