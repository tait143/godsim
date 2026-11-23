using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// The in-game IMGUI HUD: top bar (date, age, mana, alignment, population, time controls), civilizations
    /// panel with a population chart, selection inspector, god-powers bar, chronicle feed, minimap, toasts,
    /// the Esc menu (new world / save / load / settings / quit) and the F1 help / F3 performance overlays.
    /// <para>
    /// Allocation policy: every visible string is cached in a <see cref="GUIContent"/> and rebuilt by a throttled
    /// refresh (4 Hz, in LateUpdate) or on user interaction; OnGUI itself only draws cached content.
    /// </para>
    /// <para>
    /// Input arbitration: <see cref="PointerOverUI"/> is true while the pointer is over any panel drawn last
    /// frame (union of panel rects, screen space, top-left origin, compared against the bottom-left-origin
    /// device pointer flipped by Screen.height), while a HUD control is being dragged, or while a modal menu is open.
    /// World-input controllers (camera, selection, god powers) must ignore clicks/scroll while it is true.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("GodSim/GodSim HUD")]
    public sealed class GodSimHUD : MonoBehaviour
    {
        // ------------------------------------------------------------------ public API
        /// <summary>The active HUD (null when none exists).</summary>
        public static GodSimHUD Instance { get; private set; }

        /// <summary>True when the pointer is over HUD UI this frame (world input should be ignored).</summary>
        public static bool PointerOverUI
        {
            get
            {
                var hud = Instance;
                if (hud == null || !hud.isActiveAndEnabled) return false;
                int frame = Time.frameCount;
                if (frame != _pointerFrame)
                {
                    _pointerFrame = frame;
                    _pointerOverUI = hud.ComputePointerOverUI();
                }
                return _pointerOverUI;
            }
        }

        /// <summary>User UI scale multiplier on top of the automatic DPI scale (Screen.dpi / 96).</summary>
        [Tooltip("User UI scale multiplier on top of the automatic DPI scale (Screen.dpi / 96).")]
        public float UiScale = 1f;

        // ------------------------------------------------------------------ inspector options (private)
        [SerializeField, Tooltip("HUD handles 1-0 / Shift+1-9 power hotkeys. Leave off: GodPowerController owns them (same mapping).")]
        private bool _handlePowerHotkeys = false;
        [SerializeField, Tooltip("HUD handles Space / + / - when no GameShortcuts component is active (GameShortcuts owns them otherwise).")]
        private bool _handleTimeHotkeys = true;
        [SerializeField, Tooltip("Pause the simulation while the Esc menu is open.")]
        private bool _pauseWhileMenuOpen = true;
        [SerializeField, Tooltip("Seconds between HUD text refreshes (0.25 = 4 Hz).")]
        private float _refreshInterval = 0.25f;

        private const string ScalePrefKey = "GodSim.HUD.UiScale";
        private static int _pointerFrame = -1;
        private static bool _pointerOverUI;

        // ------------------------------------------------------------------ state
        private HudSkin _skin;
        private float _builtScale = -1f;
        private float _scale = 1f;

        private GodSimRunner _runner;
        private GodSimulation _sim;
        private EventBus _bus;
        private GodPowerController _powers;
        private SelectionController _selection;

        private readonly List<Rect> _hitRects = new List<Rect>(16);
        private readonly List<Rect> _buildRects = new List<Rect>(16);
        private bool _captured;
        private bool _modal;
        private Vector2 _guiMouse = new Vector2(-10000f, -10000f);
        private bool _powerSelectedAtRepaint;
        private bool _selectionAtRepaint;

        private float _nextRefresh;
        private bool _dirty = true;
        private readonly long[] _directToastTick = new long[16];

        private string _tooltip;
        private readonly GUIContent _tooltipContent = new GUIContent();

        // FPS
        private float _fpsAccum;
        private int _fpsFrames;
        private float _fpsTimer;

        // Panels
        private HudTopBar _topBar;
        private HudCivPanel _civs;
        private HudInspector _inspector;
        private HudPowerBar _powerBar;
        private HudChronicle _chronicle;
        private HudMinimap _minimap;
        private HudMenus _menus;
        private HudOverlays _overlays;

        // Cached handlers (stable delegates for EventBus unsubscribe)
        private Action<GodSimulation> _onSimCreated;
        private Action _onSelectionChanged;
        private Action<GodPowerType, Vec2i> _onCastSucceeded;
        private Action<string> _onCastFailed;
        private Action<ChronicleAddedEvent> _onChronicle;
        private Action<AgeAdvancedEvent> _onAge;
        private Action<WarDeclaredEvent> _onWar;
        private Action<BuildingCompletedEvent> _onBuildingCompleted;
        private Action<YearPassedEvent> _onYear;

        // ------------------------------------------------------------------ internal accessors for panels
        internal HudSkin Skin => _skin;
        internal float Scale => _scale;
        internal GodSimRunner Runner => _runner != null ? _runner : null;
        internal GodSimulation Sim => _sim;
        internal WorldState State => _sim != null ? _sim.State : null;
        internal GameDatabase Db => _sim != null ? _sim.Db : null;
        internal GodPowerController Powers => _powers != null ? _powers : null;
        internal SelectionController Selection => _selection != null ? _selection : null;
        internal readonly PopulationHistory History = new PopulationHistory();
        internal readonly HudLineChart Chart = new HudLineChart();
        internal bool ShowHelp;
        internal bool ShowPerf;
        internal bool ShowChart = true;
        internal bool ToastsEnabled = true;
        internal bool PauseWhileMenuOpen { get => _pauseWhileMenuOpen; set => _pauseWhileMenuOpen = value; }
        internal Vector2 GuiMouse => _guiMouse;
        internal float Fps { get; private set; }
        internal float FrameMs { get; private set; }

        // ------------------------------------------------------------------ lifecycle
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "Enter Play Mode Options" with domain reload disabled.
            Instance = null;
            _pointerFrame = -1;
            _pointerOverUI = false;
            GodSimToasts.Clear();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GodSim] A second GodSimHUD was disabled (only one HUD may be active).");
                enabled = false;
                return;
            }
            Instance = this;
            try { if (PlayerPrefs.HasKey(ScalePrefKey)) UiScale = PlayerPrefs.GetFloat(ScalePrefKey, UiScale); }
            catch (Exception) { /* PlayerPrefs unavailable (e.g. sandboxed WebGL) */ }
            UiScale = Mathf.Clamp(MathUtil.Sanitize(UiScale, 1f), 0.5f, 3f);

            _onSimCreated = BindSimulation;
            _onSelectionChanged = OnSelectionChanged;
            _onCastSucceeded = OnCastSucceeded;
            _onCastFailed = OnCastFailed;
            _onChronicle = OnChronicle;
            _onAge = OnAgeAdvanced;
            _onWar = OnWarDeclared;
            _onBuildingCompleted = OnBuildingCompleted;
            _onYear = OnYearPassed;

            _topBar = new HudTopBar(this);
            _civs = new HudCivPanel(this);
            _inspector = new HudInspector(this);
            _powerBar = new HudPowerBar(this);
            _chronicle = new HudChronicle(this);
            _minimap = new HudMinimap(this);
            _menus = new HudMenus(this);
            _overlays = new HudOverlays(this);
            GodSimToasts.Clear();
            for (int i = 0; i < _directToastTick.Length; i++) _directToastTick[i] = long.MinValue;
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
            _dirty = true;
        }

        private void OnDisable()
        {
            _hitRects.Clear();
            _buildRects.Clear();
            _captured = false;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            BindRunner(null);
            BindSimulation(null);
            BindPowers(null);
            BindSelection(null);
            if (_menus != null) _menus.Close();
            if (_skin != null) _skin.Dispose();
            _skin = null;
            Chart.Dispose();
            if (_minimap != null) _minimap.Dispose();
            Instance = null;
        }

        private void Update()
        {
            if (_topBar == null) return; // disabled duplicate
            BindRunner(GodSimRunner.Instance);
            var sim = _runner != null ? _runner.Simulation : null;
            if (!ReferenceEquals(sim, _sim)) BindSimulation(sim);
            BindPowers(GodPowerController.Instance);
            BindSelection(SelectionController.Instance);

            float dt = Time.unscaledDeltaTime;
            _fpsAccum += dt; _fpsFrames++; _fpsTimer += dt;
            if (_fpsTimer >= 0.5f)
            {
                Fps = _fpsAccum > 0f ? _fpsFrames / _fpsAccum : 0f;
                FrameMs = _fpsFrames > 0 ? _fpsAccum * 1000f / _fpsFrames : 0f;
                _fpsAccum = 0f; _fpsFrames = 0; _fpsTimer = 0f;
            }
            if (_captured && !AnyPointerButtonHeld()) _captured = false; // release missed outside the window
            GodSimToasts.Update();
        }

        private static bool AnyPointerButtonHeld()
        {
#if ENABLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM_PACKAGE
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null) return mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
#else
            return true; // unknown backend: rely on the IMGUI MouseUp event
#endif
        }

        private void LateUpdate()
        {
            if (_topBar == null) return;
            float now = Time.unscaledTime;
            if (!_dirty && now < _nextRefresh) return;
            _dirty = false;
            _nextRefresh = now + Mathf.Clamp(_refreshInterval, 0.05f, 2f);
            RefreshAll();
        }

        /// <summary>Requests a text refresh on the next LateUpdate (cheap; coalesced).</summary>
        internal void MarkDirty() { _dirty = true; }

        private void RefreshAll()
        {
            try
            {
                _topBar.Refresh();
                _civs.Refresh();
                _inspector.Refresh();
                _powerBar.Refresh();
                _chronicle.Refresh();
                _minimap.Refresh();
                _menus.Refresh();
                if (ShowPerf) _overlays.Refresh();
                if (ShowChart && !_civs.Collapsed) Chart.Render(History);
            }
            catch (Exception ex)
            {
                // The HUD must never take the game down; log and continue next refresh.
                Debug.LogError("[GodSim] HUD refresh failed: " + ex);
            }
        }

        // ------------------------------------------------------------------ binding
        private void BindRunner(GodSimRunner runner)
        {
            if (runner == null) runner = null; // normalize destroyed Unity objects
            if (ReferenceEquals(runner, _runner)) return;
            if (!ReferenceEquals(_runner, null)) _runner.SimulationCreated -= _onSimCreated;
            _runner = runner;
            if (_runner != null)
            {
                _runner.SimulationCreated += _onSimCreated;
                BindSimulation(_runner.Simulation);
            }
            else BindSimulation(null);
        }

        private void BindPowers(GodPowerController p)
        {
            if (p == null) p = null;
            if (ReferenceEquals(p, _powers)) return;
            if (!ReferenceEquals(_powers, null)) { _powers.CastSucceeded -= _onCastSucceeded; _powers.CastFailed -= _onCastFailed; }
            _powers = p;
            if (_powers != null) { _powers.CastSucceeded += _onCastSucceeded; _powers.CastFailed += _onCastFailed; }
            _dirty = true;
        }

        private void BindSelection(SelectionController s)
        {
            if (s == null) s = null;
            if (ReferenceEquals(s, _selection)) return;
            if (!ReferenceEquals(_selection, null)) _selection.SelectionChanged -= _onSelectionChanged;
            _selection = s;
            if (_selection != null) _selection.SelectionChanged += _onSelectionChanged;
            OnSelectionChanged();
        }

        private void BindSimulation(GodSimulation sim)
        {
            if (_bus != null)
            {
                _bus.Unsubscribe(_onChronicle);
                _bus.Unsubscribe(_onAge);
                _bus.Unsubscribe(_onWar);
                _bus.Unsubscribe(_onBuildingCompleted);
                _bus.Unsubscribe(_onYear);
                _bus = null;
            }
            bool changed = !ReferenceEquals(sim, _sim);
            _sim = sim;
            if (_sim != null && _sim.Events != null)
            {
                _bus = _sim.Events;
                _bus.Subscribe(_onChronicle);
                _bus.Subscribe(_onAge);
                _bus.Subscribe(_onWar);
                _bus.Subscribe(_onBuildingCompleted);
                _bus.Subscribe(_onYear);
            }
            if (!changed) return;
            for (int i = 0; i < _directToastTick.Length; i++) _directToastTick[i] = long.MinValue;
            History.Clear();
            if (_sim != null && _sim.State != null) History.Sample(_sim.State);
            if (_topBar == null) return; // Awake not run yet
            _topBar.OnSimulationChanged();
            _civs.OnSimulationChanged();
            _inspector.OnSimulationChanged();
            _powerBar.OnSimulationChanged();
            _chronicle.OnSimulationChanged();
            _minimap.OnSimulationChanged();
            _overlays.OnSimulationChanged();
            Chart.Render(History, true);
            _dirty = true;
        }

        // ------------------------------------------------------------------ event handlers
        private void OnSelectionChanged()
        {
            if (_inspector != null) _inspector.OnSelectionChanged();
            _dirty = true;
        }

        private void OnCastSucceeded(GodPowerType power, Vec2i tile)
        {
            if (_powerBar != null) _powerBar.MarkCast();
            GodSimToasts.Show(PowerName(power) + " invoked at " + tile.X + ", " + tile.Y, HudDraw.PowerCategoryColor(PowerCategory(power)), 2f);
            _dirty = true;
        }

        private void OnCastFailed(string error)
        {
            GodSimToasts.Show(string.IsNullOrEmpty(error) ? "The power could not be cast." : error, HudSkin.Bad, 3.5f);
        }

        private void OnChronicle(ChronicleAddedEvent e)
        {
            if (_chronicle != null) _chronicle.MarkChanged();
            var en = e.Entry;
            if (!ToastsEnabled || en.Importance < 3 || string.IsNullOrEmpty(en.Text)) return;
            int cat = (int)en.Category;
            // Skip when a dedicated toast (age / war / wonder) already announced this on the same tick.
            if (cat >= 0 && cat < _directToastTick.Length && _directToastTick[cat] == en.Tick) return;
            GodSimToasts.Show(en.Text, HudDraw.CategoryColor(en.Category), 5f);
        }

        private void OnAgeAdvanced(AgeAdvancedEvent e)
        {
            _directToastTick[(int)ChronicleCategory.Age] = CurrentTick;
            if (!ToastsEnabled) return;
            GodSimToasts.Show(CivName(e.CivilizationId) + " entered the " + AgeName(e.NewAge) + ".", HudSkin.Gold, 6f);
        }

        private void OnWarDeclared(WarDeclaredEvent e)
        {
            _directToastTick[(int)ChronicleCategory.War] = CurrentTick;
            if (!ToastsEnabled) return;
            GodSimToasts.Show(CivName(e.AttackerId) + " declared war on " + CivName(e.DefenderId) + "!", HudSkin.Bad, 6f);
        }

        private void OnBuildingCompleted(BuildingCompletedEvent e)
        {
            var db = Db;
            if (db == null || !db.TryGetBuilding(e.Type, out var def) || !def.IsWonder) return;
            _directToastTick[(int)ChronicleCategory.Building] = CurrentTick;
            if (!ToastsEnabled) return;
            var st = State;
            Settlement s = st != null ? st.Settlements.Get(e.SettlementId) : null;
            string who = s != null ? s.Name : "A settlement";
            GodSimToasts.Show(who + " completed the wonder " + def.Name + "!", HudSkin.Gold, 6f);
        }

        private void OnYearPassed(YearPassedEvent e)
        {
            var st = State;
            if (st != null) History.Sample(st);
        }

        private long CurrentTick => _sim != null && _sim.State != null ? _sim.State.Tick : 0;

        // ------------------------------------------------------------------ helpers for panels
        internal string CivName(int civId)
        {
            var st = State;
            var c = st != null ? st.Civilizations.Get(civId) : null;
            return c != null && !string.IsNullOrEmpty(c.Name) ? c.Name : "Unknown";
        }

        internal string RaceName(RaceId r)
        {
            var db = Db;
            int i = (int)r;
            if (db != null && i >= 0 && i < db.Races.Count && db.Races[i] != null && !string.IsNullOrEmpty(db.Races[i].Name)) return db.Races[i].Name;
            return r.ToString();
        }

        internal string AgeName(AgeId a)
        {
            var db = Db;
            int i = (int)a;
            if (db != null && i >= 0 && i < db.Ages.Count && db.Ages[i] != null && !string.IsNullOrEmpty(db.Ages[i].Name)) return db.Ages[i].Name;
            return a + " Age";
        }

        internal string BuildingName(BuildingType t)
        {
            var db = Db;
            if (db != null && db.TryGetBuilding(t, out var def) && !string.IsNullOrEmpty(def.Name)) return def.Name;
            return HudText.Spaced(t.ToString());
        }

        internal GodPowerDefinition PowerDef(GodPowerType p)
        {
            var db = Db;
            int i = (int)p;
            return db != null && i >= 0 && i < db.GodPowers.Count ? db.GodPowers[i] : null;
        }

        internal string PowerName(GodPowerType p)
        {
            var d = PowerDef(p);
            return d != null && !string.IsNullOrEmpty(d.Name) ? d.Name : HudText.Spaced(p.ToString());
        }

        internal GodPowerCategory PowerCategory(GodPowerType p)
        {
            var d = PowerDef(p);
            if (d != null) return d.Category;
            int i = (int)p;
            return i <= 6 ? GodPowerCategory.Blessing : i <= 11 ? GodPowerCategory.Shaping : GodPowerCategory.Wrath;
        }

        internal static void FocusTile(Vec2i tile)
        {
            var cam = GodCameraController.Instance;
            if (cam != null && tile.IsValid) cam.FocusOn(tile);
        }

        /// <summary>Selects a settlement and moves the camera to it.</summary>
        internal void FocusSettlement(int settlementId)
        {
            var st = State;
            var s = st != null ? st.Settlements.Get(settlementId) : null;
            if (s == null || !s.IsAlive) return;
            FocusTile(s.Center);
            var sel = Selection;
            if (sel != null) sel.SelectSettlement(s.Id);
        }

        internal void SetUiScale(float v)
        {
            UiScale = Mathf.Clamp(MathUtil.Sanitize(v, 1f), 0.5f, 3f);
            try { PlayerPrefs.SetFloat(ScalePrefKey, UiScale); PlayerPrefs.Save(); }
            catch (Exception) { /* ignore persistence failures */ }
            _dirty = true;
        }

        /// <summary>Adds a panel rect (GUI space, pixels) to this frame's pointer-blocking set. Call during every event.</summary>
        internal void AddHitRect(Rect r)
        {
            if (Event.current != null && Event.current.type == EventType.Repaint && r.width > 0f && r.height > 0f) _buildRects.Add(r);
        }

        /// <summary>Shows <paramref name="text"/> as a tooltip when the mouse is over <paramref name="localRect"/> (current GUI clip space).</summary>
        internal void Tooltip(Rect localRect, string text)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint || string.IsNullOrEmpty(text)) return;
            if (localRect.Contains(e.mousePosition)) _tooltip = text;
        }

        internal void Tooltip(Rect localRect, GUIContent content)
        {
            if (content != null) Tooltip(localRect, content.tooltip);
        }

        internal void TogglePause()
        {
            var r = Runner;
            if (r != null) r.Paused = !r.Paused;
            _dirty = true;
        }

        internal void ChangeSpeed(int delta)
        {
            var r = Runner;
            var levels = GodSimRunner.SpeedLevels;
            if (r == null || levels == null || levels.Length == 0) return;
            int idx = Mathf.Clamp(r.SpeedIndex + delta, 0, levels.Length - 1);
            r.SpeedIndex = idx;
            if (r.Paused && delta > 0) r.Paused = false;
            _dirty = true;
        }

        internal void SetSpeed(int index)
        {
            var r = Runner;
            var levels = GodSimRunner.SpeedLevels;
            if (r == null || levels == null || levels.Length == 0) return;
            r.SpeedIndex = Mathf.Clamp(index, 0, levels.Length - 1);
            r.Paused = false;
            _dirty = true;
        }

        internal void OpenMenu() { _menus.Open(); }
        internal void OpenNewWorld() { _menus.Open(HudMenus.Page.NewWorld); }

        // ------------------------------------------------------------------ pointer
        private bool ComputePointerOverUI()
        {
            if (_modal || _captured) return true;
            Vector2 p;
            if (!TryGetDevicePointer(out p)) p = _guiMouse;
            return HitTest(p);
        }

        private bool HitTest(Vector2 guiPoint)
        {
            for (int i = 0; i < _hitRects.Count; i++) if (_hitRects[i].Contains(guiPoint)) return true;
            return false;
        }

        /// <summary>Device pointer in GUI space (top-left origin). Device APIs use a bottom-left origin, hence the flip.</summary>
        private static bool TryGetDevicePointer(out Vector2 gui)
        {
#if ENABLE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM_PACKAGE
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer != null)
            {
                Vector2 sp = pointer.position.ReadValue();
                gui = new Vector2(sp.x, Screen.height - sp.y);
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            Vector3 mp = Input.mousePosition;
            gui = new Vector2(mp.x, Screen.height - mp.y);
            return true;
#else
            gui = default(Vector2);
            return false;
#endif
        }

        // ------------------------------------------------------------------ OnGUI
        private float ComputeScale()
        {
            float dpi = Screen.dpi;
            float auto = dpi > 1f ? Mathf.Clamp(dpi / 96f, 0.75f, 4f) : 1f;
            float s = Mathf.Clamp(auto * Mathf.Clamp(MathUtil.Sanitize(UiScale, 1f), 0.5f, 3f), 0.5f, 6f);
            return Mathf.Round(s * 20f) / 20f; // quantize so tiny dpi jitter never rebuilds textures
        }

        private void EnsureSkin()
        {
            float s = ComputeScale();
            if (_skin != null && Mathf.Abs(s - _builtScale) < 0.001f) return;
            var old = _skin;
            _skin = HudSkin.Build(GUI.skin, s);
            _builtScale = s;
            _scale = s;
            if (old != null) old.Dispose();
            _topBar.OnSkinChanged();
            _chronicle.OnSkinChanged();
            _inspector.OnSkinChanged();
            _dirty = true;
        }

        private void OnGUI()
        {
            Event e = Event.current;
            if (e == null || _topBar == null) return;
            try { DrawGUI(e); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex)
            {
                // Never let a HUD bug spam every frame or break input; report at most every 5 seconds.
                float now = Time.unscaledTime;
                if (now >= _nextGuiErrorLog) { _nextGuiErrorLog = now + 5f; Debug.LogError("[GodSim] HUD OnGUI failed: " + ex); }
            }
        }

        private float _nextGuiErrorLog;

        private void DrawGUI(Event e)
        {
            _guiMouse = e.mousePosition;
            if (_skin == null || e.type == EventType.Layout) EnsureSkin();
            if (_skin == null) return;
            GUI.skin = _skin.Skin;

            bool repaint = e.type == EventType.Repaint;
            if (repaint) { _buildRects.Clear(); _tooltip = null; }

            if (e.type == EventType.MouseDown && (HitTest(e.mousePosition) || _menus.IsOpen)) _captured = true;
            if (e.rawType == EventType.MouseUp) _captured = false;

            HandleKeyboard(e);

            var sk = _skin;
            float W = Screen.width, H = Screen.height;
            float m = sk.U(8f);
            _modal = _menus.IsOpen;

            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && !_modal;

            Rect top = _topBar.Draw(W);
            float y0 = top.yMax + m;

            if (_sim != null && _sim.State != null)
            {
                Rect mini = _minimap.Draw(W, H, m);
                Rect chron = _chronicle.Draw(m, H, Mathf.Min(sk.U(390f), W * 0.34f), Mathf.Min(sk.U(260f), H * 0.34f));
                float barLeft = chron.xMax + m;
                float barRight = (mini.width > 0f ? mini.x : W) - m;
                _powerBar.Draw(barLeft, barRight, H - m);
                float leftBottom = chron.y - m;
                _civs.Draw(m, y0, Mathf.Min(sk.U(340f), W * 0.3f), leftBottom - y0);
                float rightBottom = (mini.width > 0f ? mini.y : H) - m;
                float iw = Mathf.Min(sk.U(350f), W * 0.32f);
                _inspector.Draw(W - m - iw, y0, iw, rightBottom - y0);
                if (ShowPerf) _overlays.DrawPerf(_civs.LastRect.xMax + m, y0, sk.U(440f));
            }
            GUI.enabled = prevEnabled;

            GodSimToasts.Draw(sk, W * 0.5f, y0, W - m * 2f);
            if (ShowHelp) _overlays.DrawHelp(W, H);

            if (_modal) _menus.Draw(W, H);

            if (repaint && _tooltip != null && !_modal) DrawTooltip(sk, W, H);

            if (repaint)
            {
                _hitRects.Clear();
                _hitRects.AddRange(_buildRects);
                var pc = Powers;
                _powerSelectedAtRepaint = pc != null && pc.HasPower;
                var sel = Selection;
                _selectionAtRepaint = sel != null && sel.HasSelection;
            }
        }

        private void DrawTooltip(HudSkin sk, float W, float H)
        {
            _tooltipContent.text = _tooltip;
            float maxW = sk.U(300f);
            Vector2 size = sk.Tooltip.CalcSize(_tooltipContent);
            float w = Mathf.Min(maxW, size.x + 1f);
            float h = sk.Tooltip.CalcHeight(_tooltipContent, w);
            float x = _guiMouse.x + sk.U(14f);
            float y = _guiMouse.y + sk.U(18f);
            if (x + w > W - 4f) x = _guiMouse.x - w - sk.U(8f);
            if (y + h > H - 4f) y = _guiMouse.y - h - sk.U(8f);
            sk.Tooltip.Draw(new Rect(Mathf.Max(2f, x), Mathf.Max(2f, y), w, h), _tooltipContent, false, false, false, false);
        }

        private void HandleKeyboard(Event e)
        {
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
            bool typing = GUIUtility.keyboardControl != 0;
            KeyCode k = e.keyCode;

            if (k == KeyCode.Escape)
            {
                // Esc cascade. GodPowerController / SelectionController react to Esc on their own (device input,
                // earlier in the frame), so state captured at the last repaint tells us whether they consumed it.
                var pc = Powers;
                var sel = Selection;
                if (typing) GUIUtility.keyboardControl = 0;
                else if (_menus.IsOpen) _menus.Back();
                else if (ShowHelp) ShowHelp = false;
                else if (pc != null && (pc.HasPower || _powerSelectedAtRepaint)) { if (pc.HasPower) pc.ClearPower(); }
                else if (sel != null && (sel.HasSelection || _selectionAtRepaint)) { if (sel.HasSelection) sel.Clear(); }
                else _menus.Open();
                e.Use();
                return;
            }
            if (typing) return;
            if (k == KeyCode.F1) { ShowHelp = !ShowHelp; e.Use(); return; }
            if (k == KeyCode.F3) { ShowPerf = !ShowPerf; _dirty = true; e.Use(); return; }
            if (_menus.IsOpen || _sim == null) return;

            if (_handleTimeHotkeys && GameShortcuts.Instance == null)
            {
                if (k == KeyCode.Space) { TogglePause(); e.Use(); return; }
                if (k == KeyCode.Equals || k == KeyCode.Plus || k == KeyCode.KeypadPlus) { ChangeSpeed(+1); e.Use(); return; }
                if (k == KeyCode.Minus || k == KeyCode.KeypadMinus) { ChangeSpeed(-1); e.Use(); return; }
            }
            if (_handlePowerHotkeys && k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9)
            {
                int digit = k - KeyCode.Alpha0;
                int index = digit == 0 ? 9 : digit - 1;
                if (e.shift) index = digit == 0 ? -1 : 9 + digit;
                if (index >= 0 && index < Counts.GodPowers)
                {
                    _powerBar.ToggleByHotkey((GodPowerType)index);
                    e.Use();
                }
            }
        }

        /// <summary>Hotkey label for a power index (matches <see cref="HandleKeyboard"/>).</summary>
        internal static string HotkeyLabel(int index)
        {
            if (index < 0) return "";
            if (index <= 8) return HudText.Int(index + 1);
            if (index == 9) return "0";
            if (index <= 18) return "S+" + HudText.Int(index - 9);
            return "";
        }
    }
}
