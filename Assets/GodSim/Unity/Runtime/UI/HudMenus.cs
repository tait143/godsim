using System;
using System.Collections.Generic;
using System.Globalization;
using GodSim.Core;
using GodSim.Core.Persistence;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>Modal Esc menu: main page, New World dialog, Save, Load, Settings and Quit confirmation.</summary>
    internal sealed class HudMenus
    {
        public enum Page { None, Main, NewWorld, Save, Load, Settings, Quit }

        private static readonly int[] MapSizes = { 128, 192, 256, 384 };
        private static readonly string[] PresetNames = Enum.GetNames(typeof(WorldPreset));

        private readonly GodSimHUD _hud;
        private Page _page = Page.None;
        private bool _pausedBeforeMenu;
        private bool _pausedByMenu;

        // Main
        private readonly GUIContent _mainTitle = new GUIContent("GODSIM");
        private readonly GUIContent _resume = new GUIContent("Resume");
        private readonly GUIContent _newWorld = new GUIContent("New World");
        private readonly GUIContent _save = new GUIContent("Save Game");
        private readonly GUIContent _load = new GUIContent("Load Game");
        private readonly GUIContent _settings = new GUIContent("Settings");
        private readonly GUIContent _quit = new GUIContent("Quit");
        private readonly GUIContent _back = new GUIContent("Back");
        private readonly GUIContent _hint = new GUIContent("Esc closes menus  ·  F1 help  ·  F3 performance");

        // New world
        private readonly GUIContent _nwTitle = new GUIContent("New World");
        private readonly GUIContent _seedLabel = new GUIContent("Seed");
        private readonly GUIContent _random = new GUIContent("Random", "Pick a random seed");
        private readonly GUIContent _sizeLabel = new GUIContent("Map size");
        private readonly GUIContent[] _sizes;
        private readonly GUIContent _presetLabel = new GUIContent("World");
        private readonly GUIContent[] _presets;
        private readonly GUIContent _racesLabel = new GUIContent("Races");
        private GUIContent[] _raceToggles = new GUIContent[0];
        private readonly GUIContent _disasters = new GUIContent("Natural disasters");
        private readonly GUIContent _warfare = new GUIContent("Warfare");
        private readonly GUIContent _growth = new GUIContent();
        private readonly GUIContent _research = new GUIContent();
        private readonly GUIContent _create = new GUIContent("Create World");
        private readonly GUIContent _seedInfo = new GUIContent();
        private string _seedText = "";
        private int _sizeIndex = 1;
        private int _preset;
        private bool[] _raceOn = new bool[0];
        private bool _enableDisasters = true, _enableWarfare = true;
        private float _growthMul = 1f, _researchMul = 1f;

        // Save / load
        private readonly GUIContent _saveTitle = new GUIContent("Save Game");
        private readonly GUIContent _loadTitle = new GUIContent("Load Game");
        private readonly GUIContent _slotLabel = new GUIContent("Slot name");
        private readonly GUIContent _slotStatus = new GUIContent();
        private readonly GUIContent _saveBtn = new GUIContent("Save");
        private readonly GUIContent _overwriteBtn = new GUIContent("Overwrite");
        private readonly GUIContent _loadBtn = new GUIContent("Load");
        private readonly GUIContent _refresh = new GUIContent("Refresh");
        private readonly GUIContent _noSaves = new GUIContent("No saved games yet.");
        private readonly GUIContent _noRunner = new GUIContent("The simulation runner is not available.");
        private readonly List<GUIContent[]> _slotRows = new List<GUIContent[]>();
        private readonly List<string> _slotNames = new List<string>();
        private string _slotText = "slot1";
        private string _lastValidated;
        private bool _slotValid, _slotExists;
        private int _loadIndex = -1;
        private Vector2 _listScroll;

        // Settings
        private readonly GUIContent _setTitle = new GUIContent("Settings");
        private readonly GUIContent _scaleLabel = new GUIContent();
        private readonly GUIContent _territory = new GUIContent("Territory overlay");
        private readonly GUIContent _chart = new GUIContent("Population chart");
        private readonly GUIContent _toasts = new GUIContent("Event notifications");
        private readonly GUIContent _pauseMenu = new GUIContent("Pause while the menu is open");
        private readonly GUIContent _scaleNote = new GUIContent();
        private float _pendingScale = -1f;
        private float _shownPending = -1f;
        private int _shownNoteKey = int.MinValue;

        // Quit
        private readonly GUIContent _quitTitle = new GUIContent("Quit GodSim?");
        private readonly GUIContent _quitNote = new GUIContent("Unsaved progress will be lost.");
        private readonly GUIContent _yes = new GUIContent("Quit");
        private readonly GUIContent _no = new GUIContent("Cancel");

        public HudMenus(GodSimHUD hud)
        {
            _hud = hud;
            _sizes = new GUIContent[MapSizes.Length];
            for (int i = 0; i < MapSizes.Length; i++) _sizes[i] = new GUIContent(MapSizes[i].ToString(CultureInfo.InvariantCulture), MapSizes[i] + " × " + MapSizes[i] + " tiles");
            _presets = new GUIContent[PresetNames.Length];
            for (int i = 0; i < PresetNames.Length; i++) _presets[i] = new GUIContent(PresetNames[i]);
        }

        public bool IsOpen => _page != Page.None;

        // ------------------------------------------------------------------ navigation
        public void Open(Page page = Page.Main)
        {
            if (!IsOpen)
            {
                var r = _hud.Runner;
                _pausedByMenu = false;
                if (r != null && _hud.PauseWhileMenuOpen && r.Simulation != null)
                {
                    _pausedBeforeMenu = r.Paused;
                    if (!r.Paused) { r.Paused = true; _pausedByMenu = true; }
                }
            }
            GUIUtility.keyboardControl = 0;
            _page = page;
            if (page == Page.NewWorld) InitNewWorld();
            if (page == Page.Save || page == Page.Load) RefreshSlots();
            if (page == Page.Settings) _pendingScale = _hud.UiScale;
            _hud.MarkDirty();
        }

        public void Close()
        {
            if (!IsOpen) return;
            _page = Page.None;
            GUIUtility.keyboardControl = 0;
            var r = _hud.Runner;
            if (r != null && _pausedByMenu && r.Paused) r.Paused = _pausedBeforeMenu;
            _pausedByMenu = false;
            _hud.MarkDirty();
        }

        public void Back()
        {
            if (_page == Page.Main || _page == Page.None) Close();
            else Open(Page.Main);
        }

        public void Refresh() { }

        // ------------------------------------------------------------------ page setup
        private void InitNewWorld()
        {
            var sim = _hud.Sim;
            var cfg = sim != null && sim.Config != null ? sim.Config : new SimConfig();
            _seedText = cfg.Seed.ToString(CultureInfo.InvariantCulture);
            _sizeIndex = 1;
            for (int i = 0; i < MapSizes.Length; i++) if (MapSizes[i] == cfg.MapWidth) _sizeIndex = i;
            _preset = (int)cfg.Preset;
            var races = EnumUtil.AllRaces;
            if (_raceOn.Length != races.Length) _raceOn = new bool[races.Length];
            _raceToggles = new GUIContent[races.Length];
            for (int i = 0; i < races.Length; i++)
            {
                _raceToggles[i] = new GUIContent(_hud.RaceName(races[i]));
                _raceOn[i] = cfg.StartingRaces == null || Array.IndexOf(cfg.StartingRaces, races[i]) >= 0;
            }
            _enableDisasters = cfg.EnableDisasters;
            _enableWarfare = cfg.EnableWarfare;
            _growthMul = MathUtil.Clamp(MathUtil.Sanitize(cfg.GrowthMultiplier, 1f), 0.25f, 4f);
            _researchMul = MathUtil.Clamp(MathUtil.Sanitize(cfg.ResearchMultiplier, 1f), 0.25f, 10f);
            UpdateMultiplierLabels();
            UpdateSeedInfo();
        }

        private void UpdateMultiplierLabels()
        {
            _growth.text = "Growth ×" + _growthMul.ToString("0.00", CultureInfo.InvariantCulture);
            _research.text = "Research ×" + _researchMul.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private ulong ParseSeed(out bool hashed)
        {
            hashed = false;
            string t = (_seedText ?? "").Trim();
            if (t.Length == 0) return 0UL;
            if (ulong.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out ulong v)) return v;
            hashed = true;
            return Hashing.Fnv1a64(t);
        }

        private void UpdateSeedInfo()
        {
            ulong seed = ParseSeed(out bool hashed);
            _seedInfo.text = (_seedText ?? "").Trim().Length == 0 ? "Empty: a random seed will be used." : hashed ? "Text seed → " + seed : "Numeric seed";
        }

        private void RefreshSlots()
        {
            _slotRows.Clear();
            _slotNames.Clear();
            _loadIndex = -1;
            var r = _hud.Runner;
            if (r == null) return;
            IReadOnlyList<SaveSlotInfo> saves = null;
            try { saves = r.ListSaves(); }
            catch (Exception ex) { GodSimToasts.Show("Could not list saves: " + ex.Message, HudSkin.Bad, 4f); }
            if (saves == null) return;
            for (int i = 0; i < saves.Count; i++)
            {
                var s = saves[i];
                if (string.IsNullOrEmpty(s.Slot)) continue;
                _slotNames.Add(s.Slot);
                _slotRows.Add(new[] { new GUIContent(s.Slot), new GUIContent(HudText.Bytes(s.SizeBytes)), new GUIContent(HudText.LocalDate(s.LastWriteUtc)) });
            }
            _lastValidated = null;
        }

        private void ValidateSlot()
        {
            if (ReferenceEquals(_lastValidated, _slotText)) return;
            _lastValidated = _slotText;
            string t = _slotText ?? "";
            bool valid;
            try { valid = SaveFileStore.IsValidSlotName(t); }
            catch (Exception) { valid = false; }
            _slotValid = valid;
            _slotExists = valid && _slotNames.Contains(t);
            _slotStatus.text = !valid ? "Invalid name (letters, digits, '-' and '_')." : _slotExists ? "A save with this name exists and will be overwritten." : "New save slot.";
        }

        // ------------------------------------------------------------------ drawing
        public void Draw(float W, float H)
        {
            var sk = _hud.Skin;
            HudDraw.Paint(new Rect(0f, 0f, W, H), sk.White, HudSkin.Backdrop);
            _hud.AddHitRect(new Rect(0f, 0f, W, H));
            switch (_page)
            {
                case Page.Main: DrawMain(sk, W, H); break;
                case Page.NewWorld: DrawNewWorld(sk, W, H); break;
                case Page.Save: DrawSave(sk, W, H); break;
                case Page.Load: DrawLoad(sk, W, H); break;
                case Page.Settings: DrawSettings(sk, W, H); break;
                case Page.Quit: DrawQuit(sk, W, H); break;
            }
        }

        private static Rect Centered(float W, float H, float w, float h)
        {
            w = Mathf.Min(w, W - 16f);
            h = Mathf.Min(h, H - 16f);
            return new Rect(Mathf.Round((W - w) * 0.5f), Mathf.Round((H - h) * 0.5f), w, h);
        }

        private void DrawMain(HudSkin sk, float W, float H)
        {
            float bh = sk.U(34f), gap = sk.U(8f);
            var r = Centered(W, H, sk.U(320f), sk.U(64f) + 7 * (bh + gap) + sk.U(40f));
            HudDraw.Paint(r, sk.Modal);
            float x = r.x + sk.U(24f), w = r.width - sk.U(48f), y = r.y + sk.U(18f);
            HudDraw.Label(new Rect(r.x, y, r.width, sk.U(34f)), _mainTitle, sk.Big);
            y += sk.U(50f);
            bool hasSim = _hud.Sim != null;
            bool hasRunner = _hud.Runner != null;
            bool en = GUI.enabled;
            if (hasSim && GUI.Button(new Rect(x, y, w, bh), _resume, sk.Button)) { Close(); return; }
            if (hasSim) y += bh + gap;
            GUI.enabled = en && hasRunner;
            if (GUI.Button(new Rect(x, y, w, bh), _newWorld, sk.Button)) Open(Page.NewWorld);
            y += bh + gap;
            GUI.enabled = en && hasRunner && hasSim;
            if (GUI.Button(new Rect(x, y, w, bh), _save, sk.Button)) Open(Page.Save);
            y += bh + gap;
            GUI.enabled = en && hasRunner;
            if (GUI.Button(new Rect(x, y, w, bh), _load, sk.Button)) Open(Page.Load);
            y += bh + gap;
            GUI.enabled = en;
            if (GUI.Button(new Rect(x, y, w, bh), _settings, sk.Button)) Open(Page.Settings);
            y += bh + gap;
            if (GUI.Button(new Rect(x, y, w, bh), _quit, sk.Button)) Open(Page.Quit);
            y += bh + gap * 2f;
            if (!hasRunner) { HudDraw.Label(new Rect(x, y, w, sk.LineH), _noRunner, sk.LabelCenterSmall, HudSkin.Warn); y += sk.LineH; }
            HudDraw.Label(new Rect(r.x, r.yMax - sk.U(28f), r.width, sk.U(18f)), _hint, sk.LabelCenterSmall, HudSkin.TextDim);
        }

        private float Caption(HudSkin sk, float x, float y, float w, GUIContent c)
        {
            HudDraw.Label(new Rect(x, y, w, sk.U(16f)), c, sk.Section);
            return y + sk.U(18f);
        }

        private void DrawNewWorld(HudSkin sk, float W, float H)
        {
            float bh = sk.U(28f), gap = sk.U(10f);
            var r = Centered(W, H, sk.U(560f), sk.U(560f));
            HudDraw.Paint(r, sk.Modal);
            float x = r.x + sk.U(22f), w = r.width - sk.U(44f), y = r.y + sk.U(16f);
            HudDraw.Label(new Rect(x, y, w, sk.U(28f)), _nwTitle, sk.Title);
            y += sk.U(36f);

            // Seed
            y = Caption(sk, x, y, w, _seedLabel);
            float rw = sk.U(90f);
            string nt = GUI.TextField(new Rect(x, y, w - rw - sk.Gap, bh), _seedText ?? "", 32, sk.TextField);
            if (!string.Equals(nt, _seedText, StringComparison.Ordinal)) { _seedText = nt; UpdateSeedInfo(); }
            if (GUI.Button(new Rect(x + w - rw, y, rw, bh), _random, sk.Button))
            {
                _seedText = RandomSeed().ToString(CultureInfo.InvariantCulture);
                GUIUtility.keyboardControl = 0;
                UpdateSeedInfo();
            }
            y += bh + sk.U(2f);
            HudDraw.Label(new Rect(x, y, w, sk.U(15f)), _seedInfo, sk.LabelTiny);
            y += sk.U(15f) + gap;

            // Size + preset
            float half = (w - gap) * 0.5f;
            Caption(sk, x, y, half, _sizeLabel);
            y = Caption(sk, x + half + gap, y, half, _presetLabel);
            float sbw = half / MapSizes.Length;
            for (int i = 0; i < MapSizes.Length; i++)
            {
                var br = new Rect(x + i * sbw, y, sbw - sk.U(2f), bh);
                if (GUI.Toggle(br, _sizeIndex == i, _sizes[i], sk.Tab) && _sizeIndex != i) _sizeIndex = i;
                _hud.Tooltip(br, _sizes[i].tooltip);
            }
            float pbw = half / Mathf.Max(1, _presets.Length);
            for (int i = 0; i < _presets.Length; i++)
            {
                var br = new Rect(x + half + gap + i * pbw, y, pbw - sk.U(2f), bh);
                if (GUI.Toggle(br, _preset == i, _presets[i], sk.Tab) && _preset != i) _preset = i;
                _hud.Tooltip(br, _presets[i].text);
            }
            y += bh + gap;

            // Races
            y = Caption(sk, x, y, w, _racesLabel);
            float rbw = w / Mathf.Max(1, _raceToggles.Length);
            int enabledCount = 0;
            for (int i = 0; i < _raceOn.Length; i++) if (_raceOn[i]) enabledCount++;
            for (int i = 0; i < _raceToggles.Length; i++)
            {
                bool nv = HudDraw.Check(sk, new Rect(x + i * rbw, y, rbw - sk.U(2f), bh), _raceOn[i], _raceToggles[i]);
                if (nv != _raceOn[i] && (nv || enabledCount > 1)) _raceOn[i] = nv; // keep at least one race
            }
            y += bh + gap;

            // Toggles
            _enableDisasters = HudDraw.Check(sk, new Rect(x, y, half, bh), _enableDisasters, _disasters);
            _enableWarfare = HudDraw.Check(sk, new Rect(x + half + gap, y, half, bh), _enableWarfare, _warfare);
            y += bh + gap;

            // Multipliers
            float lw = sk.U(130f);
            HudDraw.Label(new Rect(x, y, lw, bh), _growth, sk.Label);
            float ng = GUI.HorizontalSlider(new Rect(x + lw, y + (bh - sk.Slider.fixedHeight) * 0.5f, w - lw, sk.Slider.fixedHeight), _growthMul, 0.25f, 4f, sk.Slider, sk.SliderThumb);
            y += bh;
            HudDraw.Label(new Rect(x, y, lw, bh), _research, sk.Label);
            float nr = GUI.HorizontalSlider(new Rect(x + lw, y + (bh - sk.Slider.fixedHeight) * 0.5f, w - lw, sk.Slider.fixedHeight), _researchMul, 0.25f, 10f, sk.Slider, sk.SliderThumb);
            ng = Mathf.Round(ng * 20f) / 20f;
            nr = Mathf.Round(nr * 4f) / 4f;
            if (nr < 0.25f) nr = 0.25f;
            if (ng != _growthMul || nr != _researchMul) { _growthMul = ng; _researchMul = nr; UpdateMultiplierLabels(); }

            // Buttons
            float bw = sk.U(150f);
            float by = r.yMax - sk.U(20f) - sk.U(34f);
            if (GUI.Button(new Rect(x, by, sk.U(100f), sk.U(34f)), _back, sk.Button)) { Open(Page.Main); return; }
            bool en = GUI.enabled;
            GUI.enabled = en && _hud.Runner != null;
            if (GUI.Button(new Rect(x + w - bw, by, bw, sk.U(34f)), _create, sk.Button)) CreateWorld();
            GUI.enabled = en;
        }

        private static ulong RandomSeed()
        {
            // UI-only randomness (not simulation state): wall clock mixed through the core hash.
            return Hashing.Mix64(unchecked((ulong)DateTime.UtcNow.Ticks)) % 1000000000000UL;
        }

        private void CreateWorld()
        {
            var runner = _hud.Runner;
            if (runner == null) return;
            var sim = _hud.Sim;
            SimConfig cfg = sim != null && sim.Config != null ? sim.Config.Clone() : new SimConfig();
            ulong seed = ParseSeed(out _);
            if ((_seedText ?? "").Trim().Length == 0) seed = RandomSeed();
            cfg.Seed = seed;
            int size = MapSizes[MathUtil.Clamp(_sizeIndex, 0, MapSizes.Length - 1)];
            cfg.MapWidth = size;
            cfg.MapHeight = size;
            cfg.Preset = (WorldPreset)MathUtil.Clamp(_preset, 0, PresetNames.Length - 1);
            var races = new List<RaceId>(EnumUtil.AllRaces.Length);
            for (int i = 0; i < _raceOn.Length && i < EnumUtil.AllRaces.Length; i++) if (_raceOn[i]) races.Add(EnumUtil.AllRaces[i]);
            if (races.Count == 0) races.Add(RaceId.Human);
            cfg.StartingRaces = races.ToArray();
            cfg.EnableDisasters = _enableDisasters;
            cfg.EnableWarfare = _enableWarfare;
            cfg.GrowthMultiplier = _growthMul;
            cfg.ResearchMultiplier = _researchMul;
            try
            {
                Close();
                runner.NewWorld(cfg.Sanitize());
                GodSimToasts.Clear();
                GodSimToasts.Show("A new world takes shape (seed " + seed + ").", HudSkin.Gold, 4f);
            }
            catch (Exception ex)
            {
                Debug.LogError("[GodSim] NewWorld failed: " + ex);
                GodSimToasts.Show("World creation failed: " + ex.Message, HudSkin.Bad, 6f);
            }
        }

        private void DrawSlotList(HudSkin sk, Rect area, bool selectable)
        {
            HudDraw.Paint(area, sk.BarBack);
            if (_slotRows.Count == 0) { HudDraw.Label(area, _noSaves, sk.LabelCenterSmall, HudSkin.TextDim); return; }
            float rh = sk.U(24f);
            float contentH = _slotRows.Count * rh;
            float rw = area.width - sk.ScrollbarW - sk.U(6f);
            _listScroll = GUI.BeginScrollView(new Rect(area.x + sk.U(2f), area.y + sk.U(2f), area.width - sk.U(4f), area.height - sk.U(4f)), _listScroll,
                new Rect(0f, 0f, rw, contentH), false, false, sk.ScrollH, sk.ScrollV);
            for (int i = 0; i < _slotRows.Count; i++)
            {
                var row = new Rect(0f, i * rh, rw, rh);
                bool on = selectable ? _loadIndex == i : string.Equals(_slotNames[i], _slotText, StringComparison.Ordinal);
                if (GUI.Toggle(row, on, GUIContent.none, sk.Row) && !on)
                {
                    if (selectable) _loadIndex = i;
                    else { _slotText = _slotNames[i]; GUIUtility.keyboardControl = 0; }
                }
                var c = _slotRows[i];
                HudDraw.Label(new Rect(row.x + sk.U(6f), row.y, rw * 0.45f, rh), c[0], sk.Label);
                HudDraw.Label(new Rect(row.x + rw * 0.45f, row.y, rw * 0.18f, rh), c[1], sk.LabelRightSmall, HudSkin.TextDim);
                HudDraw.Label(new Rect(row.x + rw * 0.63f, row.y, rw * 0.37f - sk.U(6f), rh), c[2], sk.LabelRightSmall, HudSkin.TextDim);
            }
            GUI.EndScrollView();
        }

        private void DrawSave(HudSkin sk, float W, float H)
        {
            float bh = sk.U(28f);
            var r = Centered(W, H, sk.U(480f), sk.U(430f));
            HudDraw.Paint(r, sk.Modal);
            float x = r.x + sk.U(22f), w = r.width - sk.U(44f), y = r.y + sk.U(16f);
            HudDraw.Label(new Rect(x, y, w, sk.U(28f)), _saveTitle, sk.Title);
            y += sk.U(36f);
            y = Caption(sk, x, y, w, _slotLabel);
            GUI.SetNextControlName("GodSimSlotName");
            string nt = GUI.TextField(new Rect(x, y, w, bh), _slotText ?? "", 64, sk.TextField);
            if (!string.Equals(nt, _slotText, StringComparison.Ordinal)) _slotText = nt;
            ValidateSlot();
            y += bh + sk.U(2f);
            HudDraw.Label(new Rect(x, y, w, sk.U(16f)), _slotStatus, sk.LabelTiny, !_slotValid ? HudSkin.Bad : _slotExists ? HudSkin.Warn : HudSkin.Good);
            y += sk.U(24f);
            float listH = r.yMax - sk.U(20f) - sk.U(34f) - sk.U(10f) - y;
            DrawSlotList(sk, new Rect(x, y, w, listH), false);

            float by = r.yMax - sk.U(20f) - sk.U(34f);
            if (GUI.Button(new Rect(x, by, sk.U(100f), sk.U(34f)), _back, sk.Button)) { Open(Page.Main); return; }
            bool en = GUI.enabled;
            var runner = _hud.Runner;
            GUI.enabled = en && _slotValid && runner != null && _hud.Sim != null;
            float bw = sk.U(140f);
            bool submit = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                          && GUI.GetNameOfFocusedControl() == "GodSimSlotName" && GUI.enabled;
            if (GUI.Button(new Rect(x + w - bw, by, bw, sk.U(34f)), _slotExists ? _overwriteBtn : _saveBtn, sk.Button) || submit)
            {
                if (submit) Event.current.Use();
                string error = null;
                bool ok;
                try { ok = runner.SaveGame(_slotText, out error); }
                catch (Exception ex) { ok = false; error = ex.Message; }
                if (ok)
                {
                    GodSimToasts.Show("Saved to '" + _slotText + "'.", HudSkin.Good, 3f);
                    Close();
                }
                else
                {
                    GodSimToasts.Show("Save failed: " + (string.IsNullOrEmpty(error) ? "unknown error" : error), HudSkin.Bad, 5f);
                    RefreshSlots();
                }
            }
            GUI.enabled = en;
        }

        private void DrawLoad(HudSkin sk, float W, float H)
        {
            var r = Centered(W, H, sk.U(480f), sk.U(430f));
            HudDraw.Paint(r, sk.Modal);
            float x = r.x + sk.U(22f), w = r.width - sk.U(44f), y = r.y + sk.U(16f);
            HudDraw.Label(new Rect(x, y, w, sk.U(28f)), _loadTitle, sk.Title);
            var rf = new Rect(x + w - sk.U(80f), y + sk.U(2f), sk.U(80f), sk.U(24f));
            if (GUI.Button(rf, _refresh, sk.ButtonSmall)) RefreshSlots();
            y += sk.U(38f);
            float by = r.yMax - sk.U(20f) - sk.U(34f);
            DrawSlotList(sk, new Rect(x, y, w, by - sk.U(10f) - y), true);

            if (GUI.Button(new Rect(x, by, sk.U(100f), sk.U(34f)), _back, sk.Button)) { Open(Page.Main); return; }
            bool en = GUI.enabled;
            var runner = _hud.Runner;
            GUI.enabled = en && runner != null && _loadIndex >= 0 && _loadIndex < _slotNames.Count;
            float bw = sk.U(140f);
            if (GUI.Button(new Rect(x + w - bw, by, bw, sk.U(34f)), _loadBtn, sk.Button))
            {
                string slot = _slotNames[_loadIndex];
                string error = null;
                bool ok;
                try { ok = runner.LoadGame(slot, out error); }
                catch (Exception ex) { ok = false; error = ex.Message; }
                if (ok)
                {
                    _pausedByMenu = false; // keep whatever pause state the loaded game/runner chose
                    Close();
                    GodSimToasts.Clear();
                    GodSimToasts.Show("Loaded '" + slot + "'.", HudSkin.Good, 3f);
                }
                else GodSimToasts.Show("Load failed: " + (string.IsNullOrEmpty(error) ? "unknown error" : error), HudSkin.Bad, 5f);
            }
            GUI.enabled = en;
        }

        private void DrawSettings(HudSkin sk, float W, float H)
        {
            float bh = sk.U(28f);
            var r = Centered(W, H, sk.U(440f), sk.U(360f));
            HudDraw.Paint(r, sk.Modal);
            float x = r.x + sk.U(22f), w = r.width - sk.U(44f), y = r.y + sk.U(16f);
            HudDraw.Label(new Rect(x, y, w, sk.U(28f)), _setTitle, sk.Title);
            y += sk.U(40f);

            if (_pendingScale < 0f) _pendingScale = _hud.UiScale;
            float lw = sk.U(120f);
            if (_shownPending != _pendingScale) { _shownPending = _pendingScale; _scaleLabel.text = "UI scale " + Mathf.RoundToInt(_pendingScale * 100f) + "%"; }
            HudDraw.Label(new Rect(x, y, lw, bh), _scaleLabel, sk.Label);
            float ns = GUI.HorizontalSlider(new Rect(x + lw, y + (bh - sk.Slider.fixedHeight) * 0.5f, w - lw, sk.Slider.fixedHeight), _pendingScale, 0.5f, 2.5f, sk.Slider, sk.SliderThumb);
            _pendingScale = Mathf.Round(ns * 20f) / 20f;
            // Apply on release so textures are not regenerated on every drag step.
            if (GUIUtility.hotControl == 0 && Mathf.Abs(_pendingScale - _hud.UiScale) > 0.001f) _hud.SetUiScale(_pendingScale);
            y += bh;
            float dpi = Screen.dpi;
            int noteKey = Screen.width * 31 + Screen.height * 17 + Mathf.RoundToInt(dpi) * 7 + Mathf.RoundToInt(_hud.Scale * 100f);
            if (noteKey != _shownNoteKey)
            {
                _shownNoteKey = noteKey;
                _scaleNote.text = "Screen " + Screen.width + "×" + Screen.height + (dpi > 1f ? ", " + Mathf.RoundToInt(dpi) + " dpi" : "") + "  →  effective scale " + _hud.Scale.ToString("0.00", CultureInfo.InvariantCulture);
            }
            HudDraw.Label(new Rect(x, y, w, sk.U(16f)), _scaleNote, sk.LabelTiny);
            y += sk.U(26f);

            var tr = TerrainRenderer.Instance;
            bool en = GUI.enabled;
            GUI.enabled = en && tr != null;
            bool terr = tr != null && tr.ShowTerritory;
            bool nt = HudDraw.Check(sk, new Rect(x, y, w, bh), terr, _territory);
            if (tr != null && nt != terr) tr.ShowTerritory = nt;
            GUI.enabled = en;
            y += bh + sk.U(4f);
            _hud.ShowChart = HudDraw.Check(sk, new Rect(x, y, w, bh), _hud.ShowChart, _chart);
            y += bh + sk.U(4f);
            _hud.ToastsEnabled = HudDraw.Check(sk, new Rect(x, y, w, bh), _hud.ToastsEnabled, _toasts);
            y += bh + sk.U(4f);
            _hud.PauseWhileMenuOpen = HudDraw.Check(sk, new Rect(x, y, w, bh), _hud.PauseWhileMenuOpen, _pauseMenu);

            float by = r.yMax - sk.U(20f) - sk.U(34f);
            if (GUI.Button(new Rect(x, by, sk.U(100f), sk.U(34f)), _back, sk.Button)) Open(Page.Main);
        }

        private void DrawQuit(HudSkin sk, float W, float H)
        {
            var r = Centered(W, H, sk.U(360f), sk.U(170f));
            HudDraw.Paint(r, sk.Modal);
            float x = r.x + sk.U(22f), w = r.width - sk.U(44f), y = r.y + sk.U(18f);
            HudDraw.Label(new Rect(x, y, w, sk.U(28f)), _quitTitle, sk.Title);
            y += sk.U(34f);
            HudDraw.Label(new Rect(x, y, w, sk.U(18f)), _quitNote, sk.LabelDim);
            float by = r.yMax - sk.U(20f) - sk.U(34f);
            float bw = (w - sk.U(10f)) * 0.5f;
            if (GUI.Button(new Rect(x, by, bw, sk.U(34f)), _no, sk.Button)) { Open(Page.Main); return; }
            if (GUI.Button(new Rect(x + bw + sk.U(10f), by, bw, sk.U(34f)), _yes, sk.Button))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
