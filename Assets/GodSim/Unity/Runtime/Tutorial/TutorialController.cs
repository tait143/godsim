using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// First-session tutorial: drives a <see cref="TutorialScript"/> from the game's controllers and
    /// draws an IMGUI card (title, body, objective, progress dots, Next/Skip/Close, "Don't show again")
    /// anchored next to a HUD region with a pulsing arrow, plus one-time contextual tip toasts
    /// (first famine, first war, first age advance, first time mana runs short).
    /// <para>Starts by itself on the first launch once a simulation exists (PlayerPrefs), or via
    /// <see cref="StartTutorial"/>. Input arbitration: other controllers should treat
    /// <see cref="PointerOverUI"/> like <c>GodSimHUD.PointerOverUI</c>.</para>
    /// <para>No per-frame allocations: GUIContents, layout heights and delegates are cached; IMGUI
    /// layout is disabled (<c>useGUILayout = false</c>).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialController : MonoBehaviour
    {
        /// <summary>PlayerPrefs key: serialized <see cref="TutorialScript"/> progress.</summary>
        public const string PrefsProgressKey = "GodSim.Tutorial.Progress";
        /// <summary>PlayerPrefs key: 1 once the tutorial was finished.</summary>
        public const string PrefsDoneKey = "GodSim.Tutorial.Done";
        /// <summary>PlayerPrefs key: 1 when the player ticked "Don't show again".</summary>
        public const string PrefsDontShowKey = "GodSim.Tutorial.DontShow";
        /// <summary>PlayerPrefs key: bitmask of contextual tips already shown.</summary>
        public const string PrefsTipsKey = "GodSim.Tutorial.Tips";

        /// <summary>Resolves a highlight anchor name to a rect in IMGUI screen coordinates (top-left origin).</summary>
        public delegate bool AnchorResolver(string anchor, out Rect guiRect);

        /// <summary>The live controller, or null.</summary>
        public static TutorialController Instance { get; private set; }

        /// <summary>True while the pointer is over the tutorial card or a tip toast (last OnGUI).</summary>
        public static bool PointerOverUI { get; private set; }

        /// <summary>
        /// Optional hook for the HUD to report where its panels really are. When it returns false (or is
        /// null) a default layout is assumed and no highlight frame is drawn, only the arrow.
        /// </summary>
        public static AnchorResolver ResolveAnchor { get; set; }

        [Header("Behaviour")]
        [Tooltip("Start automatically on the first launch (until finished or 'Don't show again').")]
        public bool AutoStartOnFirstLaunch = true;
        [Tooltip("Show one-time hint toasts (first famine, war, age advance, low mana).")]
        public bool ContextualTips = true;
        [Tooltip("Seconds after an objective is met before moving on automatically (negative = wait for Next).")]
        [Range(-1f, 10f)] public float AutoAdvanceDelay = 1.6f;
        [Tooltip("Accumulated camera motion (world units; 20 degrees of rotation ~ 1 unit) that counts as 'moved'.")]
        [Range(0.1f, 20f)] public float CameraMoveThreshold = 1.5f;

        [Header("Look")]
        [Tooltip("Extra multiplier on top of the automatic DPI scale.")]
        [Range(0.5f, 3f)] public float UiScale = 1f;
        [Tooltip("Card width in reference pixels (96 dpi).")]
        [Range(280f, 640f)] public float CardWidth = 440f;
        [Tooltip("How long a tip toast stays up (seconds; hovering pauses it).")]
        [Range(3f, 30f)] public float ToastSeconds = 10f;
        [Tooltip("IMGUI depth (lower = drawn above other OnGUI scripts such as the HUD).")]
        public int GuiDepth = -50;

        private enum Side : byte { None, Top, Bottom, Left, Right }
        private enum PendingAction : byte { None, Next, Skip, Close, Focus, ToastClose, ToastShow }

        private const int ToastQueueSize = 8;

        // ---- model
        private TutorialScript _script;
        private readonly TutorialTipTracker _tips = new TutorialTipTracker();
        private bool _visible;
        private bool _dontShowAgain;
        private bool _autoStartPending;

        // ---- hooks
        private GodSimRunner _runner;
        private GodPowerController _powers;
        private SelectionController _selection;
        private GodSimulation _sim;
        private EventBus _bus;
        private bool _lastPaused;
        private int _lastSpeed;
        private long _lastTick;
        private bool _haveTick;

        // ---- camera polling
        private Camera _cam;
        private float _camRetryAt;
        private bool _haveCamPose;
        private Vector3 _camPos;
        private Quaternion _camRot;
        private float _camZoom;
        private float _camAccum;

        // ---- cached delegates (no per-hook allocation, and stable for Unsubscribe)
        private Action<GodSimulation> _onSimCreated;
        private Action<GodPowerType, Vec2i> _onCastSucceeded;
        private Action _onSelectionChanged;
        private Action<FamineEvent> _onFamine;
        private Action<WarDeclaredEvent> _onWar;
        private Action<AgeAdvancedEvent> _onAge;
        private Action<GodCommandRejectedEvent> _onRejected;
        private Action<GodPowerCastEvent> _onPowerCastEvent;
        private Action<TutorialScript> _onStepChanged;
        private Action<TutorialScript> _onFinished;

        // ---- GUI (contents cached; text swapped only on step/toast change)
        private TutorialSkin _skin;
        private readonly GUIContent _title = new GUIContent();
        private readonly GUIContent _body = new GUIContent();
        private readonly GUIContent _objective = new GUIContent();
        private readonly GUIContent _counter = new GUIContent();
        private readonly GUIContent _next = new GUIContent("Next");
        private readonly GUIContent _continue = new GUIContent("Continue");
        private readonly GUIContent _finish = new GUIContent("Finish");
        private readonly GUIContent _skip = new GUIContent("Skip");
        private readonly GUIContent _showMe = new GUIContent("Show me");
        private readonly GUIContent _dontShow = new GUIContent("Don't show again");
        private readonly GUIContent _close = new GUIContent("×", "Close (resume later)");
        private readonly GUIContent _toastTitle = new GUIContent();
        private readonly GUIContent _toastBody = new GUIContent();
        private readonly GUIContent _toastShow = new GUIContent("Show");
        private string[] _counterTexts;

        // ---- cached layout
        private int _layoutIndex = int.MinValue;
        private float _layoutWidth = -1f;
        private float _layoutScale = -1f;
        private float _titleH, _bodyH, _objH, _cardH, _counterW, _nextW, _skipW, _showW, _toggleW;
        private bool _twoRowFooter;
        private Rect _cardRect, _toastRect;
        private PendingAction _pending;

        // ---- toasts
        private readonly TutorialTipKind[] _toastQueue = new TutorialTipKind[ToastQueueSize];
        private readonly Vec2i[] _toastQueueTile = new Vec2i[ToastQueueSize];
        private int _toastHead, _toastCount;
        private bool _toastActive, _toastHovered;
        private Vec2i _toastTile = Vec2i.Invalid;
        private float _toastAge;
        private float _toastLayoutWidth = -1f, _toastLayoutScale = -1f, _toastTitleH, _toastBodyH;
        private float _nextLowManaPoll;

        // ---- "Show me"
        private int _focusCursor;

        // =================================================================== public API

        /// <summary>The step state machine (never null after Awake).</summary>
        public TutorialScript Script => _script;

        /// <summary>True while the tutorial card is shown.</summary>
        public bool IsVisible => _visible && _script != null && _script.IsActive;

        /// <summary>Persisted "Don't show again" flag (disables the automatic start).</summary>
        public bool DontShowAgain
        {
            get => _dontShowAgain;
            set
            {
                if (_dontShowAgain == value) return;
                _dontShowAgain = value;
                PlayerPrefs.SetInt(PrefsDontShowKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Shows the tutorial: resumes the current step, or starts over when finished / never started.</summary>
        public void StartTutorial()
        {
            if (_script == null) return;
            _autoStartPending = false;
            if (_script.IsFinished) _script.Reset();
            if (!_script.IsActive) _script.Start();
            _visible = true;
            SaveProgress();
        }

        /// <summary>Hides the card; progress is kept and resumes on the next <see cref="StartTutorial"/> or launch.</summary>
        public void HideTutorial()
        {
            _visible = false;
            SaveProgress();
        }

        /// <summary>Forgets all tutorial progress and shown tips (does not change "Don't show again").</summary>
        public void ResetTutorial()
        {
            if (_script == null) return;
            _script.Reset();
            _tips.Reset();
            _visible = false;
            PlayerPrefs.SetInt(PrefsDoneKey, 0);
            PlayerPrefs.SetInt(PrefsTipsKey, 0);
            SaveProgress();
        }

        /// <summary>For the HUD: call when the help panel opens (F1 is also detected directly).</summary>
        public static void NotifyHelpOpened()
        {
            var c = Instance;
            if (c != null && c._script != null && c._visible) c._script.OnHelpOpened();
        }

        // =================================================================== lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            PointerOverUI = false;
            ResolveAnchor = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
            useGUILayout = false; // we never use GUILayout: skips the layout pass and its allocations

            _onSimCreated = HandleSimulationCreated;
            _onCastSucceeded = HandleCastSucceeded;
            _onSelectionChanged = HandleSelectionChanged;
            _onFamine = HandleFamine;
            _onWar = HandleWar;
            _onAge = HandleAgeAdvanced;
            _onRejected = HandleCommandRejected;
            _onPowerCastEvent = HandlePowerCastEvent;
            _onStepChanged = HandleStepChanged;
            _onFinished = HandleFinished;

            _script = TutorialContent.CreateDefaultScript();
            _script.CategoryResolver = ResolveCategory;
            _script.AutoAdvanceDelay = AutoAdvanceDelay;
            _counterTexts = new string[_script.StepCount];
            for (int i = 0; i < _counterTexts.Length; i++) _counterTexts[i] = (i + 1) + " / " + _counterTexts.Length;

            _dontShowAgain = PlayerPrefs.GetInt(PrefsDontShowKey, 0) != 0;
            _tips.Mask = PlayerPrefs.GetInt(PrefsTipsKey, 0);
            _script.TryDeserialize(PlayerPrefs.GetString(PrefsProgressKey, ""));
            bool done = PlayerPrefs.GetInt(PrefsDoneKey, 0) != 0 || _script.IsFinished;
            _autoStartPending = AutoStartOnFirstLaunch && !_dontShowAgain && !done;

            _script.StepChanged += _onStepChanged;
            _script.Finished += _onFinished;
            RefreshStepContent();
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            EnsureHooks();
        }

        private void OnDisable()
        {
            UnhookAll();
            if (Instance == this) PointerOverUI = false;
        }

        private void OnDestroy()
        {
            UnhookAll();
            if (_script != null)
            {
                _script.StepChanged -= _onStepChanged;
                _script.Finished -= _onFinished;
            }
            if (_skin != null) { _skin.Dispose(); _skin = null; }
            if (Instance == this) { Instance = null; PointerOverUI = false; }
        }

        private void OnApplicationQuit()
        {
            if (Instance == this) SaveProgress();
        }

        private void Update()
        {
            if (Instance != this || _script == null) return;
            EnsureHooks();
            PollRunner();

            if (_autoStartPending && _sim != null)
            {
                _autoStartPending = false;
                StartTutorial();
            }

            _script.AutoAdvanceDelay = AutoAdvanceDelay;
            if (_visible) _script.Update(Time.unscaledDeltaTime);

            UpdateToasts(Time.unscaledDeltaTime);
            PollLowMana();
        }

        private void LateUpdate()
        {
            if (Instance != this || _script == null) return;
            PollCamera(); // after the camera controller moved this frame
        }

        // =================================================================== hooks

        private void EnsureHooks()
        {
            var runner = GodSimRunner.Instance;
            if (!ReferenceEquals(runner, _runner) || IsDestroyed(_runner))
            {
                UnhookRunner();
                if (runner != null)
                {
                    _runner = runner;
                    runner.SimulationCreated += _onSimCreated;
                    _lastPaused = runner.Paused;
                    _lastSpeed = runner.SpeedIndex;
                    AttachSimulation(runner.Simulation);
                }
            }

            var powers = GodPowerController.Instance;
            if (!ReferenceEquals(powers, _powers) || IsDestroyed(_powers))
            {
                if (!ReferenceEquals(_powers, null)) _powers.CastSucceeded -= _onCastSucceeded;
                _powers = null;
                if (powers != null) { _powers = powers; powers.CastSucceeded += _onCastSucceeded; }
            }

            var selection = SelectionController.Instance;
            if (!ReferenceEquals(selection, _selection) || IsDestroyed(_selection))
            {
                if (!ReferenceEquals(_selection, null)) _selection.SelectionChanged -= _onSelectionChanged;
                _selection = null;
                if (selection != null) { _selection = selection; selection.SelectionChanged += _onSelectionChanged; }
            }
        }

        private void UnhookRunner()
        {
            if (!ReferenceEquals(_runner, null)) _runner.SimulationCreated -= _onSimCreated;
            _runner = null;
            AttachSimulation(null);
        }

        private void UnhookAll()
        {
            UnhookRunner();
            if (!ReferenceEquals(_powers, null)) _powers.CastSucceeded -= _onCastSucceeded;
            _powers = null;
            if (!ReferenceEquals(_selection, null)) _selection.SelectionChanged -= _onSelectionChanged;
            _selection = null;
        }

        /// <summary>Unity "fake null": the C# reference is set but the native object was destroyed.</summary>
        private static bool IsDestroyed(UnityEngine.Object o) => !ReferenceEquals(o, null) && o == null;

        /// <summary>Switches the EventBus subscriptions to <paramref name="sim"/> (null detaches).</summary>
        private void AttachSimulation(GodSimulation sim)
        {
            if (ReferenceEquals(sim, _sim)) return;
            if (_bus != null)
            {
                _bus.Unsubscribe(_onFamine);
                _bus.Unsubscribe(_onWar);
                _bus.Unsubscribe(_onAge);
                _bus.Unsubscribe(_onRejected);
                _bus.Unsubscribe(_onPowerCastEvent);
            }
            if (_sim != null)
            {
                // A different world: tiles of pending/active tip toasts no longer mean anything.
                _toastTile = Vec2i.Invalid;
                for (int i = 0; i < ToastQueueSize; i++) _toastQueueTile[i] = Vec2i.Invalid;
            }
            _sim = sim;
            _bus = sim != null ? sim.Events : null;
            if (_bus != null)
            {
                _bus.Subscribe(_onFamine);
                _bus.Subscribe(_onWar);
                _bus.Subscribe(_onAge);
                _bus.Subscribe(_onRejected);
                _bus.Subscribe(_onPowerCastEvent);
            }
            _haveTick = false;
            _focusCursor = 0;
        }

        private void HandleSimulationCreated(GodSimulation sim) => AttachSimulation(sim);

        private void HandleCastSucceeded(GodPowerType power, Vec2i tile)
        {
            if (_visible && _script != null) _script.OnPowerCast(power);
        }

        private void HandleSelectionChanged()
        {
            var sel = _selection;
            if (!_visible || _script == null || sel == null) return;
            if (sel.HasSelection && sel.SelectedSettlementId >= 0) _script.OnSettlementSelected();
        }

        // =================================================================== polling

        private void PollRunner()
        {
            var runner = _runner;
            if (runner == null) return;

            bool paused = runner.Paused;
            int speed = runner.SpeedIndex;
            if (paused != _lastPaused || speed != _lastSpeed)
            {
                _lastPaused = paused;
                _lastSpeed = speed;
                if (_visible) _script.OnSpeedChanged();
            }

            var sim = runner.Simulation;
            if (!ReferenceEquals(sim, _sim)) AttachSimulation(sim); // e.g. a load that raised no event
            var state = _sim != null ? _sim.State : null;
            if (state == null) return;

            long tick = state.Tick;
            if (_haveTick && tick > _lastTick)
            {
                long d = tick - _lastTick;
                if (_visible) _script.OnDaysPassed(d > int.MaxValue ? int.MaxValue : (int)d);
            }
            _lastTick = tick;
            _haveTick = true;
        }

        private void PollCamera()
        {
            var step = _visible ? _script.CurrentStep : null;
            if (step == null || step.Objective.Kind != TutorialObjectiveKind.CameraMoved || _script.ObjectiveMet)
            {
                _haveCamPose = false;
                return;
            }

            if (_cam == null && Time.unscaledTime >= _camRetryAt)
            {
                _camRetryAt = Time.unscaledTime + 1f;
                _cam = Camera.main;
            }
            Transform tf = null;
            if (_cam != null) tf = _cam.transform;
            else
            {
                var rig = GodCameraController.Instance;
                if (rig != null) tf = rig.transform;
            }
            if (tf == null) { _haveCamPose = false; return; }

            Vector3 pos = tf.position;
            Quaternion rot = tf.rotation;
            float zoom = _cam != null ? (_cam.orthographic ? _cam.orthographicSize : _cam.fieldOfView * 0.1f) : 0f;
            if (_haveCamPose)
            {
                float delta = Vector3.Distance(pos, _camPos) + Quaternion.Angle(rot, _camRot) * 0.05f + Mathf.Abs(zoom - _camZoom);
                if (delta > 0f && !float.IsNaN(delta) && !float.IsInfinity(delta)) _camAccum += delta;
                if (_camAccum >= CameraMoveThreshold) _script.OnCameraMoved();
            }
            _camPos = pos;
            _camRot = rot;
            _camZoom = zoom;
            _haveCamPose = true;
        }

        private void PollLowMana()
        {
            if (!ContextualTips || _tips.WasShown(TutorialTipKind.LowMana)) return;
            float now = Time.unscaledTime;
            if (now < _nextLowManaPoll) return;
            _nextLowManaPoll = now + 0.5f;
            var pc = _powers;
            if (pc == null || !pc.HasPower) return;
            if (IsManaShort(pc.SelectedPower)) QueueTip(TutorialTipKind.LowMana, Vec2i.Invalid);
        }

        // =================================================================== sim events -> tips

        private void HandleFamine(FamineEvent e)
        {
            if (!ContextualTips || _tips.WasShown(TutorialTipKind.FirstFamine)) return;
            var state = _sim != null ? _sim.State : null;
            Vec2i tile = Vec2i.Invalid;
            if (state != null && state.Settlements.TryGet(e.SettlementId, out var s) && s != null) tile = s.Center;
            QueueTip(TutorialTipKind.FirstFamine, tile);
        }

        private void HandleWar(WarDeclaredEvent e)
        {
            if (!ContextualTips || _tips.WasShown(TutorialTipKind.FirstWar)) return;
            QueueTip(TutorialTipKind.FirstWar, CapitalOf(e.DefenderId));
        }

        private void HandleAgeAdvanced(AgeAdvancedEvent e)
        {
            if (!ContextualTips || _tips.WasShown(TutorialTipKind.FirstAgeAdvance)) return;
            if (e.NewAge <= e.OldAge) return;
            QueueTip(TutorialTipKind.FirstAgeAdvance, CapitalOf(e.CivilizationId));
        }

        private void HandleCommandRejected(GodCommandRejectedEvent e)
        {
            if (!ContextualTips || _tips.WasShown(TutorialTipKind.LowMana)) return;
            if (IsManaShort(e.Power) || MentionsMana(e.Reason)) QueueTip(TutorialTipKind.LowMana, Vec2i.Invalid);
        }

        private void HandlePowerCastEvent(GodPowerCastEvent e)
        {
            if (e.Success)
            {
                // Fallback for scenes without a GodPowerController; a duplicate signal is harmless.
                if (_visible && _script != null) _script.OnPowerCast(e.Power);
            }
            else if (ContextualTips && !_tips.WasShown(TutorialTipKind.LowMana) && MentionsMana(e.Message))
            {
                QueueTip(TutorialTipKind.LowMana, Vec2i.Invalid);
            }
        }

        private static bool MentionsMana(string s) => s != null && s.IndexOf("mana", StringComparison.OrdinalIgnoreCase) >= 0;

        private GodPowerDefinition PowerDef(GodPowerType power)
        {
            var db = _sim != null ? _sim.Db : null;
            var list = db != null ? db.GodPowers : null;
            int i = (int)power;
            return list != null && i >= 0 && i < list.Count ? list[i] : null;
        }

        private bool IsManaShort(GodPowerType power)
        {
            var state = _sim != null ? _sim.State : null;
            var def = PowerDef(power);
            return state != null && def != null && state.God.Mana < def.ManaCost;
        }

        private GodPowerCategory ResolveCategory(GodPowerType power)
        {
            var def = PowerDef(power);
            return def != null ? def.Category : TutorialScript.DefaultCategoryOf(power);
        }

        private Vec2i CapitalOf(int civId)
        {
            var state = _sim != null ? _sim.State : null;
            if (state == null) return Vec2i.Invalid;
            if (!state.Civilizations.TryGet(civId, out var civ) || civ == null || civ.CapitalId < 0) return Vec2i.Invalid;
            return state.Settlements.TryGet(civ.CapitalId, out var s) && s != null ? s.Center : Vec2i.Invalid;
        }

        private void QueueTip(TutorialTipKind kind, Vec2i tile)
        {
            if (!ContextualTips || _toastCount >= ToastQueueSize) return;
            if (!_tips.TryConsume(kind)) return;
            PlayerPrefs.SetInt(PrefsTipsKey, _tips.Mask);
            PlayerPrefs.Save();
            int slot = (_toastHead + _toastCount) % ToastQueueSize;
            _toastQueue[slot] = kind;
            _toastQueueTile[slot] = tile;
            _toastCount++;
        }

        private void UpdateToasts(float dt)
        {
            if (!_toastActive && _toastCount > 0)
            {
                var kind = _toastQueue[_toastHead];
                _toastTile = _toastQueueTile[_toastHead];
                _toastHead = (_toastHead + 1) % ToastQueueSize;
                _toastCount--;
                _toastTitle.text = TutorialContent.TipTitle(kind);
                _toastBody.text = TutorialContent.TipBody(kind);
                _toastLayoutWidth = -1f;
                _toastAge = 0f;
                _toastActive = true;
                _toastHovered = false;
            }
            if (!_toastActive) return;
            if (!(dt > 0f)) dt = 0f;
            if (!_toastHovered) _toastAge += Mathf.Min(dt, 0.25f);
            if (_toastAge >= ToastSeconds) _toastActive = false;
        }

        // =================================================================== script callbacks

        private void HandleStepChanged(TutorialScript s)
        {
            _camAccum = 0f;
            _haveCamPose = false;
            RefreshStepContent();
            SaveProgress();
        }

        private void HandleFinished(TutorialScript s)
        {
            _visible = false;
            PlayerPrefs.SetInt(PrefsDoneKey, 1);
            SaveProgress();
        }

        private void RefreshStepContent()
        {
            _layoutIndex = int.MinValue;
            var step = _script != null ? _script.CurrentStep : null;
            if (step == null)
            {
                _title.text = ""; _body.text = ""; _objective.text = ""; _counter.text = "";
                return;
            }
            _title.text = step.Title;
            _body.text = step.Body;
            _objective.text = step.Objective.Label;
            int i = _script.CurrentIndex;
            _counter.text = _counterTexts != null && i >= 0 && i < _counterTexts.Length ? _counterTexts[i] : "";
        }

        private void SaveProgress()
        {
            if (_script == null) return;
            PlayerPrefs.SetString(PrefsProgressKey, _script.Serialize());
            PlayerPrefs.Save();
        }

        // =================================================================== IMGUI

        private void OnGUI()
        {
            if (Instance != this || _script == null) return;
            Event e = Event.current;
            if (e == null) return;

            bool showCard = _visible && _script.IsActive;
            if (!showCard && !_toastActive)
            {
                PointerOverUI = false;
                return;
            }

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.F1) { if (showCard) _script.OnHelpOpened(); } // not consumed: the HUD toggles its help too
                else if (showCard && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                         && GUIUtility.keyboardControl == 0 && _script.CanAdvance)
                {
                    _pending = PendingAction.Next;
                    e.Use();
                }
            }

            EnsureSkin();
            GUI.depth = GuiDepth;
            Color prevColor = GUI.color;
            bool prevEnabled = GUI.enabled;
            float now = Time.unscaledTime;
            Vector2 mouse = e.mousePosition;
            bool over = false;

            if (showCard)
            {
                DrawCard(now);
                over |= _cardRect.Contains(mouse);
            }
            if (_toastActive)
            {
                DrawToast(showCard);
                _toastHovered = _toastRect.Contains(mouse);
                over |= _toastHovered;
            }
            else _toastHovered = false;

            GUI.color = prevColor;
            GUI.enabled = prevEnabled;
            PointerOverUI = over;

            // Clicks/scrolls on our panels never fall through to IMGUI drawn below.
            if (over && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel)) e.Use();

            ApplyPending();
        }

        private void ApplyPending()
        {
            var action = _pending;
            _pending = PendingAction.None;
            switch (action)
            {
                case PendingAction.Next: _script.Advance(); break;
                case PendingAction.Skip: _script.Skip(); break;
                case PendingAction.Close: HideTutorial(); break;
                case PendingAction.Focus: FocusNextSettlement(); break;
                case PendingAction.ToastClose: _toastActive = false; break;
                case PendingAction.ToastShow: FocusTile(_toastTile); _toastActive = false; break;
            }
        }

        private void EnsureSkin()
        {
            float scale = ComputeScale();
            if (_skin != null && Mathf.Abs(_skin.Scale - scale) < 0.001f) return;
            if (_skin != null) _skin.Dispose();
            _skin = new TutorialSkin(GUI.skin, scale);
            _layoutIndex = int.MinValue;
            _toastLayoutWidth = -1f;
        }

        /// <summary>DPI-aware scale (96 dpi = 1), never below what the resolution suggests, quantized to 5%.</summary>
        private float ComputeScale()
        {
            float dpi = Screen.dpi;
            float byDpi = dpi > 1f && dpi < 1000f ? dpi / 96f : 1f;
            float byRes = Screen.height / 1200f;
            float s = Mathf.Clamp(Mathf.Max(byDpi, byRes), 0.75f, 4f) * Mathf.Clamp(UiScale, 0.5f, 3f);
            return Mathf.Round(s * 20f) / 20f;
        }

        // ------------------------------------------------------------------ card

        private void EnsureCardLayout(float width)
        {
            var sk = _skin;
            if (_layoutIndex == _script.CurrentIndex && Mathf.Abs(_layoutWidth - width) < 0.5f && _layoutScale == sk.Scale) return;
            _layoutIndex = _script.CurrentIndex;
            _layoutWidth = width;
            _layoutScale = sk.Scale;

            float inner = width - 2f * sk.Pad;
            _counterW = sk.SmallRight.CalcSize(_counter).x + sk.Gap;
            _titleH = Mathf.Max(sk.CloseSize, sk.Title.CalcHeight(_title, Mathf.Max(10f, inner - sk.CloseSize - _counterW)));
            _bodyH = sk.Body.CalcHeight(_body, inner);
            _objH = 0f;
            var step = _script.CurrentStep;
            if (step != null && _objective.text.Length > 0)
            {
                _objH = Mathf.Max(sk.CheckSize, sk.Objective.CalcHeight(_objective, inner - sk.CheckSize - sk.Gap));
                if (step.Objective.Kind == TutorialObjectiveKind.WaitDays) _objH += sk.Gap + sk.BarH;
            }

            float bw = Mathf.Max(sk.Button.CalcSize(_next).x, Mathf.Max(sk.Button.CalcSize(_continue).x, sk.Button.CalcSize(_finish).x));
            _nextW = Mathf.Max(sk.U(84f), bw + sk.Gap);
            _skipW = Mathf.Max(sk.U(64f), sk.Button.CalcSize(_skip).x);
            _showW = Mathf.Max(sk.U(80f), sk.Button.CalcSize(_showMe).x);
            _toggleW = sk.CheckSize + sk.Gap + sk.Small.CalcSize(_dontShow).x;
            float buttons = _nextW + sk.Gap + _skipW + sk.Gap + _showW;
            _twoRowFooter = _toggleW + sk.Gap * 2f + buttons > inner;

            _cardH = sk.Pad + _titleH + sk.Gap + _bodyH
                     + (_objH > 0f ? sk.Gap * 1.5f + _objH : 0f)
                     + sk.Gap * 1.5f + sk.DotSize
                     + sk.Gap * 1.5f + sk.ButtonH + (_twoRowFooter ? sk.Gap + sk.ButtonH * 0.8f : 0f)
                     + sk.Pad;
        }

        private void DrawCard(float now)
        {
            var sk = _skin;
            var step = _script.CurrentStep;
            if (step == null) return;

            float sw = Screen.width, sh = Screen.height;
            float width = Mathf.Min(sk.U(CardWidth), sw - 2f * sk.Margin);
            if (width < 120f) return;
            EnsureCardLayout(width);
            float height = Mathf.Min(_cardH, sh - 2f * sk.Margin);

            bool precise = TryGetAnchor(step.Highlight, out Rect anchor);
            Side side = SideOf(step.Highlight, anchor, precise);
            _cardRect = PlaceCard(side, anchor, width, height);
            Rect card = _cardRect;

            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 4.5f);
            if (precise) DrawHighlightFrame(anchor, pulse);
            if (side != Side.None) DrawArrow(side, anchor, card, pulse);

            GUI.color = Color.white;
            GUI.Box(card, GUIContent.none, sk.Card);

            float x = card.x + sk.Pad, inner = card.width - 2f * sk.Pad, y = card.y + sk.Pad;

            // Header: title, step counter, close.
            Rect closeR = new Rect(card.xMax - sk.Pad * 0.5f - sk.CloseSize, card.y + sk.Pad * 0.5f, sk.CloseSize, sk.CloseSize);
            if (GUI.Button(closeR, _close, sk.Close)) _pending = PendingAction.Close;
            GUI.Label(new Rect(closeR.x - _counterW, closeR.y, _counterW, sk.CloseSize), _counter, sk.SmallRight);
            GUI.Label(new Rect(x, y, Mathf.Max(10f, inner - sk.CloseSize - _counterW), _titleH), _title, sk.Title);
            y += _titleH + sk.Gap;

            GUI.Label(new Rect(x, y, inner, _bodyH), _body, sk.Body);
            y += _bodyH;

            bool met = _script.ObjectiveMet;
            if (_objH > 0f)
            {
                y += sk.Gap * 1.5f;
                Rect icon = new Rect(x, y + 1f, sk.CheckSize, sk.CheckSize);
                if (met && !step.IsInformational)
                {
                    GUI.color = TutorialSkin.Good;
                    GUI.DrawTexture(icon, sk.Dot);
                }
                else
                {
                    GUI.color = new Color(TutorialSkin.Gold.r, TutorialSkin.Gold.g, TutorialSkin.Gold.b, step.IsInformational ? 0.6f : 0.55f + 0.45f * pulse);
                    GUI.DrawTexture(icon, sk.Ring);
                }
                GUI.color = Color.white;
                float tx = x + sk.CheckSize + sk.Gap;
                float textH = _objH - (step.Objective.Kind == TutorialObjectiveKind.WaitDays ? sk.Gap + sk.BarH : 0f);
                GUI.Label(new Rect(tx, y, inner - sk.CheckSize - sk.Gap, textH), _objective,
                    met && !step.IsInformational ? sk.ObjectiveDone : sk.Objective);
                if (step.Objective.Kind == TutorialObjectiveKind.WaitDays)
                {
                    Rect bar = new Rect(tx, y + textH + sk.Gap, inner - sk.CheckSize - sk.Gap, sk.BarH);
                    GUI.color = TutorialSkin.BarBack;
                    GUI.DrawTexture(bar, sk.White);
                    GUI.color = met ? TutorialSkin.Good : TutorialSkin.Gold;
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_script.ObjectiveProgress), bar.height), sk.White);
                    GUI.color = Color.white;
                }
                y += _objH;
            }

            // Progress dots.
            y += sk.Gap * 1.5f;
            DrawDots(x, y, inner, pulse);

            // Footer: toggle (left), buttons (right).
            // Narrow cards put the toggle on its own row below the buttons.
            float footerBottom = card.yMax - sk.Pad;
            float toggleH = _twoRowFooter ? sk.ButtonH * 0.8f : sk.ButtonH;
            Rect toggleR = new Rect(x, footerBottom - toggleH, Mathf.Min(_toggleW, inner), toggleH);
            float by = _twoRowFooter ? toggleR.y - sk.Gap - sk.ButtonH : footerBottom - sk.ButtonH;
            DrawToggle(toggleR);

            float bx = card.xMax - sk.Pad - _nextW;
            bool canNext = _script.CanAdvance;
            GUI.enabled = canNext;
            if (canNext && met && !step.IsInformational) GUI.color = new Color(1f, 1f, 1f, 0.8f + 0.2f * pulse);
            GUIContent nextLabel = _script.IsLastStep ? _finish : (met && !step.IsInformational ? _continue : _next);
            if (GUI.Button(new Rect(bx, by, _nextW, sk.ButtonH), nextLabel, sk.Primary)) _pending = PendingAction.Next;
            GUI.enabled = true;
            GUI.color = Color.white;

            if (_script.CanSkip && !met)
            {
                bx -= sk.Gap + _skipW;
                if (GUI.Button(new Rect(bx, by, _skipW, sk.ButtonH), _skip, sk.Button)) _pending = PendingAction.Skip;
            }
            if (step.Focus == TutorialFocus.Settlements && _sim != null && GodCameraController.Instance != null)
            {
                bx -= sk.Gap + _showW;
                if (GUI.Button(new Rect(bx, by, _showW, sk.ButtonH), _showMe, sk.Button)) _pending = PendingAction.Focus;
            }
        }

        private void DrawDots(float x, float y, float inner, float pulse)
        {
            var sk = _skin;
            int n = _script.StepCount;
            float stepW = Mathf.Min(sk.DotSize + sk.DotSpacing, inner / Mathf.Max(1, n));
            float d = Mathf.Min(sk.DotSize, stepW * 0.8f);
            int current = _script.CurrentIndex;
            for (int i = 0; i < n; i++)
            {
                float size = d;
                Color c;
                if (i == current) { c = Color.white; size = d * (1.15f + 0.15f * pulse); }
                else if (_script.IsStepCompleted(i)) c = TutorialSkin.Gold;
                else if (_script.IsStepSkipped(i)) c = TutorialSkin.DotSkipped;
                else c = TutorialSkin.DotFuture;
                float cx = x + i * stepW + d * 0.5f, cy = y + sk.DotSize * 0.5f;
                GUI.color = c;
                GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), sk.Dot);
            }
            GUI.color = Color.white;
        }

        private void DrawToggle(Rect r)
        {
            var sk = _skin;
            bool v = GUI.Toggle(r, _dontShowAgain, GUIContent.none, GUIStyle.none);
            if (v != _dontShowAgain) DontShowAgain = v;
            Rect box = new Rect(r.x, r.y + (r.height - sk.CheckSize) * 0.5f, sk.CheckSize, sk.CheckSize);
            GUI.DrawTexture(box, sk.Box);
            if (_dontShowAgain)
            {
                float inset = Mathf.Max(2f, sk.CheckSize * 0.25f);
                GUI.color = TutorialSkin.Gold;
                GUI.DrawTexture(new Rect(box.x + inset, box.y + inset, box.width - 2f * inset, box.height - 2f * inset), sk.Dot);
                GUI.color = Color.white;
            }
            GUI.Label(new Rect(box.xMax + sk.Gap, r.y, r.width - sk.CheckSize - sk.Gap, r.height), _dontShow, sk.Small);
        }

        private void DrawHighlightFrame(Rect a, float pulse)
        {
            var sk = _skin;
            float t = Mathf.Max(2f, sk.U(2f));
            float grow = sk.U(3f) + pulse * sk.U(3f);
            Rect r = new Rect(a.x - grow, a.y - grow, a.width + 2f * grow, a.height + 2f * grow);
            GUI.color = new Color(TutorialSkin.Gold.r, TutorialSkin.Gold.g, TutorialSkin.Gold.b, 0.35f + 0.5f * pulse);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), sk.White);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), sk.White);
            GUI.DrawTexture(new Rect(r.x, r.y + t, t, r.height - 2f * t), sk.White);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), sk.White);
            GUI.color = Color.white;
        }

        private void DrawArrow(Side side, Rect anchor, Rect card, float pulse)
        {
            var sk = _skin;
            float a = sk.ArrowSize, bob = pulse * a * 0.3f;
            Rect r;
            Texture2D tex;
            switch (side)
            {
                case Side.Top:
                    r = new Rect(Mathf.Clamp(anchor.center.x, card.xMin + a, card.xMax - a) - a * 0.5f, card.yMin - a - sk.Gap * 0.5f - bob, a, a);
                    tex = sk.ArrowUp; break;
                case Side.Bottom:
                    r = new Rect(Mathf.Clamp(anchor.center.x, card.xMin + a, card.xMax - a) - a * 0.5f, card.yMax + sk.Gap * 0.5f + bob, a, a);
                    tex = sk.ArrowDown; break;
                case Side.Left:
                    r = new Rect(card.xMin - a - sk.Gap * 0.5f - bob, Mathf.Clamp(anchor.center.y, card.yMin + a, card.yMax - a) - a * 0.5f, a, a);
                    tex = sk.ArrowLeft; break;
                default:
                    r = new Rect(card.xMax + sk.Gap * 0.5f + bob, Mathf.Clamp(anchor.center.y, card.yMin + a, card.yMax - a) - a * 0.5f, a, a);
                    tex = sk.ArrowRight; break;
            }
            GUI.color = new Color(TutorialSkin.Gold.r, TutorialSkin.Gold.g, TutorialSkin.Gold.b, 0.65f + 0.35f * pulse);
            GUI.DrawTexture(r, tex);
            GUI.color = Color.white;
        }

        private Rect PlaceCard(Side side, Rect anchor, float w, float h)
        {
            var sk = _skin;
            float sw = Screen.width, sh = Screen.height, m = sk.Margin, off = sk.ArrowSize + sk.Gap;
            float x, y;
            switch (side)
            {
                case Side.Top: x = anchor.center.x - w * 0.5f; y = anchor.yMax + off; break;
                case Side.Bottom: x = anchor.center.x - w * 0.5f; y = anchor.yMin - off - h; break;
                case Side.Left: x = anchor.xMax + off; y = anchor.center.y - h * 0.5f; break;
                case Side.Right: x = anchor.xMin - off - w; y = anchor.center.y - h * 0.5f; break;
                default: x = (sw - w) * 0.5f; y = (sh - h) * 0.45f; break;
            }
            x = Mathf.Clamp(x, m, Mathf.Max(m, sw - w - m));
            y = Mathf.Clamp(y, m, Mathf.Max(m, sh - h - m));
            return new Rect(x, y, w, h);
        }

        /// <summary>Anchor rect from <see cref="ResolveAnchor"/> (precise) or a default HUD layout guess.</summary>
        private bool TryGetAnchor(string name, out Rect r)
        {
            var resolver = ResolveAnchor;
            if (resolver != null && name != TutorialAnchors.Center)
            {
                try
                {
                    if (resolver(name, out r) && r.width > 0f && r.height > 0f) return true;
                }
                catch (Exception) { /* a faulty HUD hook must never break the tutorial */ }
            }
            r = DefaultAnchor(name);
            return false;
        }

        private Rect DefaultAnchor(string name)
        {
            float w = Screen.width, h = Screen.height, s = _skin.Scale;
            switch (name)
            {
                case TutorialAnchors.TopBar: return new Rect(0f, 0f, w, 40f * s);
                case TutorialAnchors.Help: return new Rect(w - 56f * s, 0f, 56f * s, 40f * s);
                case TutorialAnchors.PowersBar: return new Rect(w * 0.2f, h - 76f * s, w * 0.6f, 76f * s);
                case TutorialAnchors.Inspector: return new Rect(w - 330f * s, 48f * s, 330f * s, h * 0.5f);
                case TutorialAnchors.Chronicle: return new Rect(0f, h * 0.45f, 300f * s, h * 0.35f);
                case TutorialAnchors.Minimap: return new Rect(w - 210f * s, h - 210f * s, 210f * s, 210f * s);
                default: return new Rect(w * 0.5f, h * 0.5f, 0f, 0f);
            }
        }

        private static Side SideOf(string name, Rect anchor, bool precise)
        {
            switch (name)
            {
                case TutorialAnchors.TopBar:
                case TutorialAnchors.Help: return Side.Top;
                case TutorialAnchors.PowersBar:
                case TutorialAnchors.Minimap: return Side.Bottom; // card above the minimap: keeps the powers bar clear
                case TutorialAnchors.Inspector: return Side.Right;
                case TutorialAnchors.Chronicle: return Side.Left;
            }
            if (!precise) return Side.None;
            // Unknown anchor reported by the HUD: point toward whichever screen edge it hugs.
            float cx = anchor.center.x / Mathf.Max(1f, Screen.width), cy = anchor.center.y / Mathf.Max(1f, Screen.height);
            if (cy < 0.25f) return Side.Top;
            if (cy > 0.75f) return Side.Bottom;
            return cx >= 0.5f ? Side.Right : Side.Left;
        }

        // ------------------------------------------------------------------ toast

        private void DrawToast(bool cardVisible)
        {
            var sk = _skin;
            float sw = Screen.width;
            float w = Mathf.Min(sk.U(340f), sw - 2f * sk.Margin);
            if (w < 120f) return;
            float inner = w - 2f * sk.Pad;
            if (Mathf.Abs(_toastLayoutWidth - w) > 0.5f || _toastLayoutScale != sk.Scale)
            {
                _toastLayoutWidth = w;
                _toastLayoutScale = sk.Scale;
                _toastTitleH = Mathf.Max(sk.CloseSize, sk.ToastTitle.CalcHeight(_toastTitle, inner - sk.CloseSize));
                _toastBodyH = sk.ToastBody.CalcHeight(_toastBody, inner);
            }
            bool canShow = _toastTile.X != int.MinValue && GodCameraController.Instance != null;
            float h = sk.Pad + _toastTitleH + sk.Gap * 0.5f + _toastBodyH + (canShow ? sk.Gap + sk.ButtonH * 0.85f : 0f) + sk.Pad;

            TryGetAnchor(TutorialAnchors.TopBar, out Rect top);
            float y = top.yMax + sk.Margin;
            // Avoid covering the tutorial card when it sits in the top-right corner.
            if (cardVisible && _cardRect.xMax > sw - w - sk.Margin && _cardRect.yMin < y + h) y = Mathf.Max(y, _cardRect.yMax + sk.Margin);
            _toastRect = new Rect(sw - w - sk.Margin, Mathf.Min(y, Mathf.Max(sk.Margin, Screen.height - h - sk.Margin)), w, h);

            float fadeIn = Mathf.Clamp01(_toastAge / 0.25f);
            float fadeOut = Mathf.Clamp01((ToastSeconds - _toastAge) / 0.6f);
            GUI.color = new Color(1f, 1f, 1f, Mathf.Min(fadeIn, fadeOut));

            Rect r = _toastRect;
            GUI.Box(r, GUIContent.none, sk.Toast);
            Rect closeR = new Rect(r.xMax - sk.Pad * 0.5f - sk.CloseSize, r.y + sk.Pad * 0.5f, sk.CloseSize, sk.CloseSize);
            if (GUI.Button(closeR, _close, sk.Close)) _pending = PendingAction.ToastClose;
            float x = r.x + sk.Pad, y2 = r.y + sk.Pad;
            GUI.Label(new Rect(x, y2, inner - sk.CloseSize, _toastTitleH), _toastTitle, sk.ToastTitle);
            y2 += _toastTitleH + sk.Gap * 0.5f;
            GUI.Label(new Rect(x, y2, inner, _toastBodyH), _toastBody, sk.ToastBody);
            if (canShow)
            {
                float bw = Mathf.Max(sk.U(64f), sk.Button.CalcSize(_toastShow).x);
                Rect b = new Rect(r.xMax - sk.Pad - bw, r.yMax - sk.Pad - sk.ButtonH * 0.85f, bw, sk.ButtonH * 0.85f);
                if (GUI.Button(b, _toastShow, sk.Button)) _pending = PendingAction.ToastShow;
            }
        }

        // ------------------------------------------------------------------ camera helpers

        private void FocusNextSettlement()
        {
            var cam = GodCameraController.Instance;
            var state = _sim != null ? _sim.State : null;
            if (cam == null || state == null) return;
            var items = state.Settlements.Items;
            int n = items.Count;
            if (n == 0) return;

            bool anyCapital = false;
            for (int i = 0; i < n; i++)
            {
                var s = items[i];
                if (s != null && s.IsAlive && s.IsCapital) { anyCapital = true; break; }
            }
            for (int k = 0; k < n; k++)
            {
                int i = (_focusCursor + k) % n;
                var s = items[i];
                if (s == null || !s.IsAlive || (anyCapital && !s.IsCapital)) continue;
                _focusCursor = i + 1;
                cam.FocusOn(s.Center);
                return;
            }
        }

        private static void FocusTile(Vec2i tile)
        {
            if (tile.X == int.MinValue) return;
            var cam = GodCameraController.Instance;
            if (cam != null) cam.FocusOn(tile);
        }
    }
}
