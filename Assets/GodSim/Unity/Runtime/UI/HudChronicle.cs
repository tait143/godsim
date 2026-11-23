using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Bottom-left chronicle feed: newest entries first, colored by category, filterable by category and
    /// minimum importance. Entries with a location focus the camera (and select their settlement) on click.
    /// </summary>
    internal sealed class HudChronicle
    {
        private sealed class Entry
        {
            public readonly GUIContent Year = new GUIContent();
            public readonly GUIContent Text = new GUIContent();
            public Color Color;
            public int Importance;
            public bool HasTarget;
            public Vec2i Tile;
            public int SettlementId;
            public float Height;
            public float MeasuredWidth = -1f;
        }

        private const int MaxShown = 60;
        private static readonly string[] ChipNames = { "World", "Births", "Deaths", "Builds", "Towns", "Civs", "Ages", "Tech", "War", "Diplo", "Divine", "Disaster" };

        private readonly GodSimHUD _hud;
        private readonly Entry[] _entries = new Entry[MaxShown];
        private int _count;
        private readonly bool[] _catOn;
        private readonly GUIContent[] _chips;
        private readonly GUIContent[] _importance =
        {
            new GUIContent("All", "Show every entry"), new GUIContent("1+", "Notable and above"),
            new GUIContent("2+", "Major and above"), new GUIContent("3", "Legendary only")
        };
        private readonly GUIContent _title = new GUIContent("Chronicle");
        private readonly GUIContent _expand = new GUIContent("▲", "Expand");
        private readonly GUIContent _collapse = new GUIContent("▼", "Collapse");
        private readonly GUIContent _filters = new GUIContent("Filters", "Category filters");
        private readonly GUIContent _empty = new GUIContent("Nothing recorded yet.");
        private int _minImportance = 1;
        private bool _collapsed;
        private bool _showFilters;
        private Vector2 _scroll;

        private bool _changed = true;
        private int _lastCount = -1;
        private long _lastTick = -1;
        private string _lastText;

        public Rect LastRect { get; private set; }

        public HudChronicle(GodSimHUD hud)
        {
            _hud = hud;
            int n = System.Enum.GetValues(typeof(ChronicleCategory)).Length;
            _catOn = new bool[n];
            _chips = new GUIContent[n];
            for (int i = 0; i < n; i++)
            {
                var c = (ChronicleCategory)i;
                _catOn[i] = c != ChronicleCategory.Birth && c != ChronicleCategory.Death;
                _chips[i] = new GUIContent(i < ChipNames.Length ? ChipNames[i] : c.ToString(), c.ToString());
            }
            for (int i = 0; i < _entries.Length; i++) _entries[i] = new Entry();
        }

        public void OnSimulationChanged() { _count = 0; _changed = true; _lastCount = -1; _scroll = Vector2.zero; }
        public void OnSkinChanged() { for (int i = 0; i < _entries.Length; i++) _entries[i].MeasuredWidth = -1f; }
        public void MarkChanged() { _changed = true; }

        public void Refresh()
        {
            var st = _hud.State;
            if (st == null) { _count = 0; return; }
            var list = st.Chronicle.Entries;
            int n = list.Count;
            if (!_changed && n == _lastCount && (n == 0 || (list[n - 1].Tick == _lastTick && ReferenceEquals(list[n - 1].Text, _lastText)))) return;
            _changed = false;
            _lastCount = n;
            _lastTick = n > 0 ? list[n - 1].Tick : -1;
            _lastText = n > 0 ? list[n - 1].Text : null;

            var map = st.Map;
            _count = 0;
            for (int i = n - 1; i >= 0 && _count < MaxShown; i--)
            {
                var e = list[i];
                int cat = (int)e.Category;
                if (cat < 0 || cat >= _catOn.Length || !_catOn[cat] || e.Importance < _minImportance) continue;
                var en = _entries[_count++];
                en.Year.text = HudText.Year(e.Tick);
                if (!ReferenceEquals(en.Text.text, e.Text)) { en.Text.text = e.Text ?? ""; en.MeasuredWidth = -1f; }
                en.Color = HudDraw.CategoryColor(e.Category);
                en.Importance = e.Importance;
                en.SettlementId = e.SettlementId;
                var s = e.SettlementId > 0 ? st.Settlements.Get(e.SettlementId) : null;
                bool hasPos = e.Position.IsValid && map != null && map.InBounds(e.Position) && !(e.Position.X == 0 && e.Position.Y == 0);
                en.Tile = hasPos ? e.Position : (s != null ? s.Center : Vec2i.Invalid);
                en.HasTarget = en.Tile.IsValid;
            }
        }

        public Rect Draw(float x, float screenH, float w, float h)
        {
            var sk = _hud.Skin;
            float pad = sk.Pad;
            float headH = sk.U(24f);
            if (_collapsed) h = pad * 2f + headH;
            var panel = new Rect(x, screenH - x - h, w, h);
            HudDraw.Paint(panel, sk.Panel);
            _hud.AddHitRect(panel);
            LastRect = panel;

            float cx = panel.x + pad, cy = panel.y + pad, cw = w - pad * 2f;
            if (GUI.Button(new Rect(cx, cy, sk.U(22f), headH), _collapsed ? _expand : _collapse, sk.Row)) _collapsed = !_collapsed;
            HudDraw.Label(new Rect(cx + sk.U(22f), cy, sk.U(100f), headH), _title, sk.Title);

            // Importance segmented control + filter toggle
            float segW = sk.U(30f);
            float fx = cx + cw - segW * _importance.Length - sk.U(64f) - sk.Gap;
            if (!_collapsed)
            {
                var fr = new Rect(fx, cy + sk.U(2f), sk.U(60f), headH - sk.U(4f));
                bool nf = GUI.Toggle(fr, _showFilters, _filters, sk.ButtonSmall);
                if (nf != _showFilters) _showFilters = nf;
                _hud.Tooltip(fr, _filters.tooltip);
                float sx = cx + cw - segW * _importance.Length;
                for (int i = 0; i < _importance.Length; i++)
                {
                    var r = new Rect(sx + i * segW, cy + sk.U(2f), segW - sk.U(1f), headH - sk.U(4f));
                    bool on = _minImportance == i;
                    if (GUI.Toggle(r, on, _importance[i], sk.Tab) && !on) { _minImportance = i; _changed = true; _hud.MarkDirty(); }
                    _hud.Tooltip(r, _importance[i].tooltip);
                }
            }
            if (_collapsed) return panel;
            cy += headH + sk.Gap;

            if (_showFilters)
            {
                int perRow = 6;
                float chipH = sk.U(18f);
                float chipW = (cw - (perRow - 1) * sk.U(3f)) / perRow;
                Color prevBg = GUI.backgroundColor;
                for (int i = 0; i < _chips.Length; i++)
                {
                    int col = i % perRow, row = i / perRow;
                    var r = new Rect(cx + col * (chipW + sk.U(3f)), cy + row * (chipH + sk.U(3f)), chipW, chipH);
                    GUI.backgroundColor = _catOn[i] ? HudDraw.CategoryColor((ChronicleCategory)i) : prevBg;
                    bool nv = GUI.Toggle(r, _catOn[i], _chips[i], sk.Chip);
                    if (nv != _catOn[i]) { _catOn[i] = nv; _changed = true; _hud.MarkDirty(); }
                    _hud.Tooltip(r, _chips[i].tooltip);
                }
                GUI.backgroundColor = prevBg;
                int rows = (_chips.Length + perRow - 1) / perRow;
                cy += rows * (chipH + sk.U(3f)) + sk.Gap;
            }

            float viewH = panel.yMax - pad - cy;
            if (viewH < sk.U(16f)) return panel;
            if (_count == 0) { HudDraw.Label(new Rect(cx, cy, cw, sk.LineH), _empty, sk.LabelDim); return panel; }

            float yearW = sk.U(44f), stripW = Mathf.Max(2f, sk.U(3f));
            float rowW = cw - sk.ScrollbarW - sk.U(4f);
            float textW = rowW - yearW - stripW - sk.U(10f);
            float total = 0f;
            for (int i = 0; i < _count; i++)
            {
                var en = _entries[i];
                if (en.MeasuredWidth != textW)
                {
                    en.Height = Mathf.Max(sk.U(17f), Mathf.Ceil(sk.LabelWrapSmall.CalcHeight(en.Text, textW)) + sk.U(3f));
                    en.MeasuredWidth = textW;
                }
                total += en.Height;
            }

            var view = new Rect(cx, cy, cw, viewH);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0f, 0f, rowW, Mathf.Max(total, viewH - 1f)), false, false, sk.ScrollH, sk.ScrollV);
            float y = 0f;
            float visTop = _scroll.y, visBottom = _scroll.y + viewH;
            for (int i = 0; i < _count; i++)
            {
                var en = _entries[i];
                float eh = en.Height;
                if (y + eh >= visTop && y <= visBottom)
                {
                    var rr = new Rect(0f, y, rowW, eh);
                    if (en.HasTarget && GUI.Button(rr, GUIContent.none, sk.Row))
                    {
                        GodSimHUD.FocusTile(en.Tile);
                        var sel = _hud.Selection;
                        if (sel != null && en.SettlementId > 0 && _hud.State != null && _hud.State.Settlements.Get(en.SettlementId) != null) sel.SelectSettlement(en.SettlementId);
                    }
                    HudDraw.Paint(new Rect(sk.U(2f), y + sk.U(3f), en.Importance >= 3 ? stripW * 1.7f : stripW, eh - sk.U(6f)), sk.White, en.Color);
                    HudDraw.Label(new Rect(stripW + sk.U(6f), y + sk.U(1f), yearW, sk.U(15f)), en.Year, sk.LabelTiny, HudSkin.GoldDim);
                    Color tc = en.Importance >= 2 ? Color.Lerp(HudSkin.Text, en.Color, 0.35f) : Color.Lerp(HudSkin.TextDim, en.Color, 0.25f);
                    HudDraw.Label(new Rect(stripW + yearW + sk.U(6f), y + sk.U(1f), textW, eh), en.Text, sk.LabelWrapSmall, tc);
                }
                y += eh;
            }
            GUI.EndScrollView();
            return panel;
        }
    }
}
