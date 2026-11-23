using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Bottom-right minimap: draws <see cref="TerrainRenderer.Minimap"/> (texel (x, y) = tile (x, y), y up)
    /// with settlement markers; click or drag to move the camera.
    /// </summary>
    internal sealed class HudMinimap
    {
        private struct Dot
        {
            public Vec2i Tile;
            public Color Color;
            public bool Capital;
        }

        private readonly GodSimHUD _hud;
        private Dot[] _dots = new Dot[64];
        private int _dotCount;
        private bool _dragging;
        private readonly GUIContent _title = new GUIContent("World");
        private readonly GUIContent _territory = new GUIContent("Borders", "Toggle the territory overlay");
        private readonly GUIContent _missing = new GUIContent("Minimap unavailable");
        private readonly GUIContent _coords = new GUIContent();
        private string _tip = "Click or drag to move the camera.";

        public Rect LastRect { get; private set; }

        public HudMinimap(GodSimHUD hud) { _hud = hud; }

        public void OnSimulationChanged() { _dotCount = 0; _dragging = false; }
        public void Dispose() { }

        public void Refresh()
        {
            _dotCount = 0;
            var st = _hud.State;
            if (st == null) return;
            var ss = st.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var s = ss[i];
                if (s == null || !s.IsAlive) continue;
                if (_dotCount == _dots.Length) System.Array.Resize(ref _dots, _dots.Length * 2);
                var civ = st.Civilizations.Get(s.CivilizationId);
                Color c = civ != null ? HudDraw.FromRgba(civ.Color) : Color.white;
                c.a = 1f;
                _dots[_dotCount++] = new Dot { Tile = s.Center, Color = c, Capital = s.IsCapital };
            }
            if (st.Map != null) _coords.text = st.Map.Width + " × " + st.Map.Height;
        }

        public Rect Draw(float screenW, float screenH, float margin)
        {
            var sk = _hud.Skin;
            var st = _hud.State;
            var map = st != null ? st.Map : null;
            if (map == null) { LastRect = new Rect(screenW, screenH, 0f, 0f); return LastRect; }
            float pad = sk.Pad;
            float headH = sk.U(22f);
            float size = Mathf.Min(sk.U(210f), Mathf.Min(screenW, screenH) * 0.28f);
            float pw = size + pad * 2f, ph = size + headH + sk.Gap + pad * 2f;
            var panel = new Rect(screenW - margin - pw, screenH - margin - ph, pw, ph);
            HudDraw.Paint(panel, sk.Panel);
            _hud.AddHitRect(panel);
            LastRect = panel;

            float cx = panel.x + pad, cy = panel.y + pad;
            HudDraw.Label(new Rect(cx, cy, size * 0.5f, headH), _title, sk.Title);
            var tr = TerrainRenderer.Instance;
            if (tr != null)
            {
                var br = new Rect(cx + size - sk.U(66f), cy + sk.U(1f), sk.U(66f), headH - sk.U(2f));
                bool nv = GUI.Toggle(br, tr.ShowTerritory, _territory, sk.ButtonSmall);
                if (nv != tr.ShowTerritory) tr.ShowTerritory = nv;
                _hud.Tooltip(br, _territory.tooltip);
                HudDraw.Label(new Rect(cx + size * 0.35f, cy, size * 0.65f - sk.U(70f), headH), _coords, sk.LabelRightSmall, HudSkin.TextDim);
            }
            cy += headH + sk.Gap;

            // Fit the map aspect into the square.
            float aspect = map.Width / (float)Mathf.Max(1, map.Height);
            float mw = aspect >= 1f ? size : size * aspect;
            float mh = aspect >= 1f ? size / aspect : size;
            var mr = new Rect(cx + (size - mw) * 0.5f, cy + (size - mh) * 0.5f, mw, mh);
            HudDraw.Paint(new Rect(mr.x - 1f, mr.y - 1f, mr.width + 2f, mr.height + 2f), sk.BarBack);

            Texture2D tex = tr != null ? tr.Minimap : null;
            if (tex != null)
            {
                if (HudDraw.IsRepaint) GUI.DrawTexture(mr, tex, ScaleMode.StretchToFill, false);
            }
            else HudDraw.Label(mr, _missing, sk.LabelCenterSmall, HudSkin.TextDim);

            if (HudDraw.IsRepaint) DrawMarkers(sk, mr, map);
            _hud.Tooltip(mr, _tip);
            HandleInput(mr, map);
            return panel;
        }

        private static Vector2 TileToGui(Rect mr, WorldMap map, Vec2i t)
        {
            float u = (t.X + 0.5f) / map.Width;
            float v = (t.Y + 0.5f) / map.Height;
            return new Vector2(mr.x + u * mr.width, mr.yMax - v * mr.height); // tile y grows upward on the texture
        }

        private void DrawMarkers(HudSkin sk, Rect mr, WorldMap map)
        {
            float d = Mathf.Max(3f, sk.U(4f));
            float dc = Mathf.Max(5f, sk.U(6f));
            for (int i = 0; i < _dotCount; i++)
            {
                var dot = _dots[i];
                var p = TileToGui(mr, map, dot.Tile);
                float s = dot.Capital ? dc : d;
                HudDraw.Paint(new Rect(p.x - s * 0.5f - 1f, p.y - s * 0.5f - 1f, s + 2f, s + 2f), sk.White, new Color(0f, 0f, 0f, 0.75f));
                HudDraw.Paint(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), sk.White, dot.Color);
            }
            var sel = _hud.Selection;
            if (sel != null && sel.HasSelection && map.InBounds(sel.SelectedTile))
            {
                var p = TileToGui(mr, map, sel.SelectedTile);
                float s = Mathf.Max(7f, sk.U(9f));
                Color g = HudSkin.Gold;
                float t = Mathf.Max(1f, sk.U(1.5f));
                HudDraw.Paint(new Rect(p.x - s, p.y - t * 0.5f, s * 2f, t), sk.White, g);
                HudDraw.Paint(new Rect(p.x - t * 0.5f, p.y - s, t, s * 2f), sk.White, g);
            }
        }

        private void HandleInput(Rect mr, WorldMap map)
        {
            var e = Event.current;
            if (e == null) return;
            if (e.rawType == EventType.MouseUp) _dragging = false;
            if (!GUI.enabled) return;
            bool start = e.type == EventType.MouseDown && e.button == 0 && mr.Contains(e.mousePosition);
            bool drag = e.type == EventType.MouseDrag && _dragging;
            if (!start && !drag) return;
            _dragging = true;
            float u = Mathf.Clamp01((e.mousePosition.x - mr.x) / Mathf.Max(1f, mr.width));
            float v = Mathf.Clamp01((mr.yMax - e.mousePosition.y) / Mathf.Max(1f, mr.height));
            var tile = map.ClampToBounds(new Vec2i(Mathf.FloorToInt(u * map.Width), Mathf.FloorToInt(v * map.Height)));
            GodSimHUD.FocusTile(tile);
            e.Use();
        }
    }
}
