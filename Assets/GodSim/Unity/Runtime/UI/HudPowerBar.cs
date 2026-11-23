using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Bottom god-powers bar: every power grouped by category with name, mana cost, cooldown and hotkey;
    /// radius slider and race picker for the selected power. Selection goes through <see cref="GodPowerController"/>.
    /// </summary>
    internal sealed class HudPowerBar
    {
        private sealed class Item
        {
            public GodPowerType Type;
            public readonly GUIContent Name = new GUIContent();
            public readonly GUIContent Cost = new GUIContent();
            public readonly GUIContent Cooldown = new GUIContent();
            public readonly GUIContent Hotkey = new GUIContent();
            public string Tip;
            public bool Usable;
            public bool Affordable;
            public float CooldownFrac;
            public Color CatColor;
            public Rect Rect; // relative to the button area origin
        }

        private static readonly GodPowerCategory[] CategoryOrder = { GodPowerCategory.Blessing, GodPowerCategory.Shaping, GodPowerCategory.Creation, GodPowerCategory.Wrath };

        private readonly GodSimHUD _hud;
        private readonly Item[] _items = new Item[Counts.GodPowers];
        private readonly GUIContent[] _groupLabels = new GUIContent[CategoryOrder.Length];
        private readonly Rect[] _groupRects = new Rect[CategoryOrder.Length];
        private readonly bool[] _groupUsed = new bool[CategoryOrder.Length];
        private readonly GUIContent _selected = new GUIContent();
        private readonly GUIContent _radius = new GUIContent();
        private readonly GUIContent _raceCaption = new GUIContent("Race");
        private readonly GUIContent _cancel = new GUIContent("Cancel", "Deselect the power (Esc / right-click)");
        private readonly GUIContent _hint = new GUIContent("Click the map to cast.");
        private GUIContent[] _races = new GUIContent[0];
        private float _layoutWidth = -1f, _layoutScale = -1f;
        private float _contentW, _contentH;
        private int _shownRadius = int.MinValue;
        private int _shownSelected = -1;

        public Rect LastRect { get; private set; }

        public HudPowerBar(GodSimHUD hud)
        {
            _hud = hud;
            for (int i = 0; i < _items.Length; i++)
            {
                _items[i] = new Item { Type = (GodPowerType)i };
                _items[i].Hotkey.text = GodSimHUD.HotkeyLabel(i);
            }
            for (int g = 0; g < CategoryOrder.Length; g++) _groupLabels[g] = new GUIContent(CategoryOrder[g].ToString().ToUpperInvariant());
        }

        public void OnSimulationChanged()
        {
            _layoutWidth = -1f;
            var races = EnumUtil.AllRaces;
            _races = new GUIContent[races.Length];
            for (int i = 0; i < races.Length; i++) _races[i] = new GUIContent(_hud.RaceName(races[i]), "Spawn a " + _hud.RaceName(races[i]) + " tribe");
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                it.Name.text = _hud.PowerName(it.Type);
                it.CatColor = HudDraw.PowerCategoryColor(_hud.PowerCategory(it.Type));
            }
            _shownRadius = int.MinValue;
            _shownSelected = -1;
        }

        public void MarkCast() { _hud.MarkDirty(); }

        /// <summary>True when the power can be selected right now; otherwise <paramref name="reason"/> explains why.</summary>
        private bool CanUse(GodPowerType p, out string reason)
        {
            reason = null;
            var st = _hud.State;
            var def = _hud.PowerDef(p);
            if (st == null) { reason = "No world."; return false; }
            if (def == null) { reason = "This power is not available."; return false; }
            if (st.WorldAge < def.MinWorldAge) { reason = "Unlocks when a civilization reaches the " + _hud.AgeName(def.MinWorldAge) + "."; return false; }
            int i = (int)p;
            long until = i >= 0 && i < st.God.CooldownUntil.Length ? st.God.CooldownUntil[i] : 0;
            if (until > st.Tick) { reason = _hud.PowerName(p) + " is recharging (" + (until - st.Tick) + " days)."; return false; }
            if (st.God.Mana + 1e-4f < def.ManaCost) { reason = "Not enough mana for " + _hud.PowerName(p) + " (" + HudText.F0(def.ManaCost) + " needed)."; return false; }
            return true;
        }

        public void Refresh()
        {
            var st = _hud.State;
            if (st == null) return;
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                var def = _hud.PowerDef(it.Type);
                it.Usable = CanUse(it.Type, out string reason);
                float cost = def != null ? def.ManaCost : 0f;
                it.Affordable = st.God.Mana + 1e-4f >= cost;
                it.Cost.text = HudText.F0(cost);
                long until = i < st.God.CooldownUntil.Length ? st.God.CooldownUntil[i] : 0;
                long left = until - st.Tick;
                int cd = def != null ? Math.Max(1, def.CooldownTicks) : 1;
                it.CooldownFrac = left > 0 ? MathUtil.Clamp01(left / (float)cd) : 0f;
                it.Cooldown.text = left > 0 ? left + "d" : "";

                var sb = HudText.Begin();
                sb.Append(it.Name.text);
                if (def != null)
                {
                    sb.Append("  (").Append(def.Category).Append(")\n");
                    if (!string.IsNullOrEmpty(def.Description)) sb.Append(def.Description).Append('\n');
                    sb.Append("Mana ").Append(HudText.F0(def.ManaCost)).Append("  ·  cooldown ").Append(def.CooldownTicks).Append(" days");
                    sb.Append("\nRadius ").Append(def.MinRadius).Append('-').Append(def.MaxRadius);
                    if (Math.Abs(def.AlignmentDelta) > 1e-4f) sb.Append("  ·  alignment ").Append(HudText.Signed(def.AlignmentDelta));
                }
                sb.Append("\nHotkey: ").Append(it.Hotkey.text);
                if (!it.Usable && reason != null) sb.Append("\n\n").Append(reason);
                it.Tip = HudText.End();
            }
        }

        public void ToggleByHotkey(GodPowerType p)
        {
            var pc = _hud.Powers;
            if (pc == null) return;
            if (pc.HasPower && pc.SelectedPower == p) { pc.ClearPower(); return; }
            if (CanUse(p, out string reason)) Select(pc, p);
            else GodSimToasts.Show(reason, HudSkin.Bad, 2.5f);
        }

        private void Select(GodPowerController pc, GodPowerType p)
        {
            pc.SelectPower(p);
            var def = _hud.PowerDef(p);
            if (def != null)
            {
                int r = MathUtil.Clamp(pc.Radius, def.MinRadius, def.MaxRadius);
                if (r != pc.Radius) pc.Radius = r;
            }
            _shownRadius = int.MinValue;
            _hud.MarkDirty();
        }

        // ------------------------------------------------------------------ layout
        private void Layout(HudSkin sk, float avail)
        {
            if (_layoutWidth == avail && _layoutScale == sk.Scale) return;
            _layoutWidth = avail;
            _layoutScale = sk.Scale;
            float bw = sk.U(70f), bh = sk.U(52f), gap = sk.U(3f), groupGap = sk.U(12f), labelH = sk.U(14f);
            float x = 0f, y = 0f, rowH = 0f, maxX = 0f;
            for (int g = 0; g < CategoryOrder.Length; g++)
            {
                int n = 0;
                for (int i = 0; i < _items.Length; i++) if (_hud.PowerCategory(_items[i].Type) == CategoryOrder[g]) n++;
                _groupUsed[g] = n > 0;
                if (n == 0) continue;
                int cols = Mathf.Max(1, Mathf.Min(n, Mathf.FloorToInt((avail + gap) / (bw + gap))));
                int rows = (n + cols - 1) / cols;
                float gw = cols * bw + (cols - 1) * gap;
                float gh = labelH + rows * bh + (rows - 1) * gap;
                if (x > 0f && x + gw > avail) { x = 0f; y += rowH + gap * 2f; rowH = 0f; }
                _groupRects[g] = new Rect(x, y, gw, gh);
                int k = 0;
                for (int i = 0; i < _items.Length; i++)
                {
                    if (_hud.PowerCategory(_items[i].Type) != CategoryOrder[g]) continue;
                    int c = k % cols, r = k / cols;
                    _items[i].Rect = new Rect(x + c * (bw + gap), y + labelH + r * (bh + gap), bw, bh);
                    k++;
                }
                x += gw + groupGap;
                maxX = Mathf.Max(maxX, x - groupGap);
                rowH = Mathf.Max(rowH, gh);
            }
            _contentW = maxX;
            _contentH = y + rowH;
        }

        // ------------------------------------------------------------------ draw
        public void Draw(float left, float right, float bottom)
        {
            var sk = _hud.Skin;
            var st = _hud.State;
            float pad = sk.Pad;
            float avail = right - left - pad * 2f;
            if (st == null || avail < sk.U(80f)) { LastRect = new Rect(left, bottom, 0f, 0f); return; }
            Layout(sk, avail);

            var pc = _hud.Powers;
            bool hasSel = pc != null && pc.HasPower;
            GodPowerDefinition selDef = hasSel ? _hud.PowerDef(pc.SelectedPower) : null;
            float optH = hasSel ? sk.U(30f) : 0f;
            float w = Mathf.Max(_contentW, hasSel ? Mathf.Min(avail, sk.U(560f)) : 0f) + pad * 2f;
            float h = _contentH + optH + pad * 2f;
            float x = left + Mathf.Max(0f, (right - left - w) * 0.5f);
            var panel = new Rect(x, bottom - h, w, h);
            HudDraw.Paint(panel, sk.Panel);
            _hud.AddHitRect(panel);
            LastRect = panel;

            float ox = x + pad, oy = panel.y + pad;
            if (hasSel) DrawOptions(sk, pc, selDef, new Rect(ox, oy, w - pad * 2f, optH - sk.Gap));
            oy += optH;

            for (int g = 0; g < CategoryOrder.Length; g++)
            {
                if (!_groupUsed[g]) continue;
                var gr = _groupRects[g];
                HudDraw.Label(new Rect(ox + gr.x, oy + gr.y, gr.width, sk.U(13f)), _groupLabels[g], sk.Section, HudDraw.PowerCategoryColor(CategoryOrder[g]));
            }

            bool baseEnabled = GUI.enabled;
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                var r = new Rect(ox + it.Rect.x, oy + it.Rect.y, it.Rect.width, it.Rect.height);
                bool selected = hasSel && pc.SelectedPower == it.Type;
                GUI.enabled = baseEnabled && pc != null && (it.Usable || selected);
                bool nv = GUI.Toggle(r, selected, it.Name, sk.PowerButton);
                if (nv != selected && pc != null)
                {
                    if (selected) pc.ClearPower();
                    else if (CanUse(it.Type, out string reason)) Select(pc, it.Type);
                    else GodSimToasts.Show(reason, HudSkin.Bad, 2.5f);
                }
                GUI.enabled = baseEnabled;

                float inset = sk.U(4f);
                HudDraw.Paint(new Rect(r.x + inset * 2f, r.y + sk.U(1f), r.width - inset * 4f, Mathf.Max(1f, sk.U(2f))), sk.White, HudDraw.WithAlpha(it.CatColor, 0.8f));
                var low = new Rect(r.x + inset, r.yMax - sk.U(15f), r.width - inset * 2f, sk.U(13f));
                HudDraw.Label(low, it.Cost, sk.LabelTiny, it.Affordable ? HudSkin.ManaBlue : HudSkin.Bad);
                HudDraw.Label(low, it.Hotkey, sk.LabelRightSmall, HudSkin.TextDim);
                if (it.CooldownFrac > 0f)
                {
                    float ch = r.height * it.CooldownFrac;
                    HudDraw.Paint(new Rect(r.x, r.yMax - ch, r.width, ch), sk.BarFill, new Color(0f, 0f, 0f, 0.55f));
                    HudDraw.Label(new Rect(r.x, r.y + r.height * 0.35f, r.width, sk.U(16f)), it.Cooldown, sk.LabelCenter, HudSkin.Warn);
                }
                _hud.Tooltip(r, it.Tip);
            }
        }

        private void DrawOptions(HudSkin sk, GodPowerController pc, GodPowerDefinition def, Rect r)
        {
            float x = r.x, y = r.y, h = r.height;
            if (_shownSelected != (int)pc.SelectedPower) { _shownSelected = (int)pc.SelectedPower; _selected.text = _hud.PowerName(pc.SelectedPower); }
            float nameW = Mathf.Min(sk.U(130f), r.width * 0.3f);
            HudDraw.Label(new Rect(x, y, nameW, h), _selected, sk.LabelBold, HudSkin.Gold);
            x += nameW + sk.Gap;

            float cancelW = sk.U(64f);
            var cancel = new Rect(r.xMax - cancelW, y + sk.U(1f), cancelW, h - sk.U(2f));
            if (GUI.Button(cancel, _cancel, sk.ButtonSmall)) { pc.ClearPower(); _hud.MarkDirty(); return; }
            _hud.Tooltip(cancel, _cancel.tooltip);
            float right = cancel.x - sk.Gap * 2f;

            int min = def != null ? def.MinRadius : 1, max = def != null ? def.MaxRadius : 1;
            if (max > min)
            {
                if (_shownRadius != pc.Radius) { _shownRadius = pc.Radius; _radius.text = "Radius " + HudText.Int(Mathf.Max(0, pc.Radius)); }
                float lw = sk.U(62f);
                HudDraw.Label(new Rect(x, y, lw, h), _radius, sk.LabelSmall);
                x += lw;
                float sw = Mathf.Min(sk.U(130f), right - x);
                if (sw > sk.U(30f))
                {
                    var srect = new Rect(x, y + (h - sk.Slider.fixedHeight) * 0.5f, sw, sk.Slider.fixedHeight);
                    float v = GUI.HorizontalSlider(srect, MathUtil.Clamp(pc.Radius, min, max), min, max, sk.Slider, sk.SliderThumb);
                    int ri = MathUtil.Clamp(Mathf.RoundToInt(v), min, max);
                    if (ri != pc.Radius) pc.Radius = ri;
                    x += sw + sk.Gap * 3f;
                }
            }

            bool race = pc.SelectedPower == GodPowerType.SpawnTribe || (def != null && def.UsesRaceParameter);
            if (race && _races.Length > 0)
            {
                float cw = sk.U(36f);
                HudDraw.Label(new Rect(x, y, cw, h), _raceCaption, sk.LabelDim);
                x += cw;
                float bw = Mathf.Max(sk.U(30f), Mathf.Min(sk.U(64f), (right - x) / _races.Length - sk.U(2f)));
                for (int i = 0; i < _races.Length; i++)
                {
                    var rr = new Rect(x, y + sk.U(1f), bw, h - sk.U(2f));
                    if (rr.xMax > right) break;
                    var rid = EnumUtil.AllRaces[i];
                    bool on = pc.SpawnRace == rid;
                    if (GUI.Toggle(rr, on, _races[i], sk.ButtonSmall) && !on) pc.SpawnRace = rid;
                    _hud.Tooltip(rr, _races[i].tooltip);
                    x += bw + sk.U(2f);
                }
            }
            else if (right - x > sk.U(120f))
            {
                HudDraw.Label(new Rect(x, y, right - x, h), _hint, sk.LabelDim);
            }
        }
    }
}
