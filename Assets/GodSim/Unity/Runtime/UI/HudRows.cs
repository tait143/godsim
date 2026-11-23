using System.Collections.Generic;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    internal enum HudRowKind : byte { Header, Section, KeyValue, Pair, Bar, Stock, Text, Link, Spacer }

    /// <summary>One pooled display row; its GUIContents are reused across refreshes.</summary>
    internal sealed class HudRow
    {
        public HudRowKind Kind;
        public readonly GUIContent A = new GUIContent();
        public readonly GUIContent B = new GUIContent();
        public readonly GUIContent C = new GUIContent();
        public float Value;
        public Color Color;
        public Color Accent;
        public bool HasSwatch;
        public int LinkKind;
        public int LinkId;
        public Vec2i LinkTile;
        public string Tip;
        public float Height;
        public float MeasuredWidth;
    }

    /// <summary>
    /// A pooled list of rows rebuilt by throttled refreshes and drawn allocation-free. Heights of wrapped text
    /// rows are measured lazily (only when the content or width changed).
    /// </summary>
    internal sealed class HudRowList
    {
        public const int LinkFocusTile = 1;
        public const int LinkSelectSettlement = 2;

        private readonly List<HudRow> _rows = new List<HudRow>(64);
        private int _count;
        private float _measuredWidth = -1f;
        private float _measuredScale = -1f;
        private float _total;

        public int Count => _count;
        public HudRow this[int i] => _rows[i];

        public void Clear() { _count = 0; _measuredWidth = -1f; }

        public HudRow Add(HudRowKind kind, string a = "", string b = "")
        {
            if (_count == _rows.Count) _rows.Add(new HudRow());
            var r = _rows[_count++];
            r.Kind = kind;
            r.A.text = a ?? ""; r.B.text = b ?? ""; r.C.text = "";
            r.Value = 0f;
            r.Color = HudSkin.Text;
            r.Accent = HudSkin.Gold;
            r.HasSwatch = false;
            r.LinkKind = 0; r.LinkId = -1; r.LinkTile = Vec2i.Invalid;
            r.Tip = null;
            r.Height = 0f; r.MeasuredWidth = -1f;
            _measuredWidth = -1f;
            return r;
        }

        public HudRow Header(string title, string sub, Color swatch, bool hasSwatch)
        {
            var r = Add(HudRowKind.Header, title, sub);
            r.Accent = swatch; r.HasSwatch = hasSwatch;
            return r;
        }

        public HudRow Section(string title) => Add(HudRowKind.Section, title);
        public HudRow KV(string key, string value) => Add(HudRowKind.KeyValue, key, value);
        public HudRow KV(string key, string value, Color valueColor) { var r = Add(HudRowKind.KeyValue, key, value); r.Color = valueColor; return r; }
        public HudRow Pair(string left, string right) => Add(HudRowKind.Pair, left, right);
        public HudRow Bar(string label, float value01, Color fill, string text) { var r = Add(HudRowKind.Bar, label, text); r.Value = value01; r.Accent = fill; return r; }
        public HudRow Stock(string name, string amount, string net, Color netColor, float fill01)
        {
            var r = Add(HudRowKind.Stock, name, amount);
            r.C.text = net ?? ""; r.Color = netColor; r.Value = fill01;
            return r;
        }
        public HudRow Text(string text, Color color) { var r = Add(HudRowKind.Text, text); r.Color = color; return r; }
        public HudRow Link(string text, int kind, int id, Vec2i tile) { var r = Add(HudRowKind.Link, text); r.LinkKind = kind; r.LinkId = id; r.LinkTile = tile; return r; }
        public void Spacer() => Add(HudRowKind.Spacer);

        private float RowHeight(HudSkin sk, HudRow r, float w)
        {
            switch (r.Kind)
            {
                case HudRowKind.Header: return sk.U(26f);
                case HudRowKind.Section: return sk.U(21f);
                case HudRowKind.Bar: return sk.U(18f);
                case HudRowKind.Link: return sk.U(22f);
                case HudRowKind.Spacer: return sk.U(5f);
                case HudRowKind.Text:
                    if (r.MeasuredWidth != w || r.Height <= 0f)
                    {
                        r.Height = Mathf.Ceil(sk.LabelWrapSmall.CalcHeight(r.A, w - sk.U(4f))) + sk.U(2f);
                        r.MeasuredWidth = w;
                    }
                    return r.Height;
                default: return sk.U(17f);
            }
        }

        /// <summary>Total content height for the given width (cached until rows/width/scale change).</summary>
        public float Measure(HudSkin sk, float w)
        {
            if (_measuredWidth == w && _measuredScale == sk.Scale) return _total;
            float t = 0f;
            for (int i = 0; i < _count; i++) t += RowHeight(sk, _rows[i], w);
            _total = t;
            _measuredWidth = w;
            _measuredScale = sk.Scale;
            return t;
        }

        public void InvalidateMeasure()
        {
            _measuredWidth = -1f;
            for (int i = 0; i < _rows.Count; i++) _rows[i].MeasuredWidth = -1f;
        }

        /// <summary>Draws all rows; returns the clicked link row (or null). Rows outside [clipTop, clipBottom] are skipped.</summary>
        public HudRow Draw(GodSimHUD hud, HudSkin sk, float x, float y, float w, float clipTop, float clipBottom)
        {
            HudRow clicked = null;
            float cy = y;
            for (int i = 0; i < _count; i++)
            {
                var r = _rows[i];
                float h = RowHeight(sk, r, w);
                if (cy + h >= clipTop && cy <= clipBottom)
                {
                    var rect = new Rect(x, cy, w, h);
                    if (DrawRow(hud, sk, r, rect)) clicked = r;
                    if (!string.IsNullOrEmpty(r.Tip)) hud.Tooltip(rect, r.Tip);
                }
                cy += h;
            }
            return clicked;
        }

        private static bool DrawRow(GodSimHUD hud, HudSkin sk, HudRow r, Rect rect)
        {
            float x = rect.x, y = rect.y, w = rect.width, h = rect.height;
            switch (r.Kind)
            {
                case HudRowKind.Header:
                {
                    float sx = x;
                    if (r.HasSwatch)
                    {
                        float sw = sk.U(12f);
                        HudDraw.Swatch(sk, new Rect(x, y + (h - sw) * 0.5f, sw, sw), r.Accent);
                        sx += sw + sk.Gap * 1.5f;
                    }
                    float subW = r.B.text.Length > 0 ? Mathf.Min(w * 0.45f, sk.LabelDim.CalcSize(r.B).x + 2f) : 0f;
                    HudDraw.Label(new Rect(sx, y, x + w - sx - subW - sk.Gap, h), r.A, sk.Title);
                    if (subW > 0f) HudDraw.Label(new Rect(x + w - subW, y, subW, h), r.B, sk.LabelDim);
                    return false;
                }
                case HudRowKind.Section:
                    HudDraw.Label(new Rect(x, y, w, h - sk.U(4f)), r.A, sk.Section);
                    HudDraw.HLine(sk, x, y + h - sk.U(3f), w, new Color(HudSkin.Gold.r, HudSkin.Gold.g, HudSkin.Gold.b, 0.25f));
                    return false;
                case HudRowKind.KeyValue:
                    HudDraw.Label(new Rect(x, y, w * 0.5f, h), r.A, sk.LabelDim);
                    HudDraw.Label(new Rect(x + w * 0.35f, y, w * 0.65f, h), r.B, sk.LabelRightSmall, r.Color);
                    return false;
                case HudRowKind.Pair:
                    HudDraw.Label(new Rect(x, y, w * 0.62f, h), r.A, sk.LabelSmall, r.Color);
                    HudDraw.Label(new Rect(x + w * 0.5f, y, w * 0.5f, h), r.B, sk.LabelRightSmall, HudSkin.TextDim);
                    return false;
                case HudRowKind.Bar:
                {
                    float lw = w * 0.36f;
                    HudDraw.Label(new Rect(x, y, lw, h), r.A, sk.LabelDim);
                    float bh = sk.U(13f);
                    HudDraw.Bar(sk, new Rect(x + lw, y + (h - bh) * 0.5f, w - lw, bh), r.Value, r.Accent, r.B);
                    return false;
                }
                case HudRowKind.Stock:
                {
                    float c1 = w * 0.32f, c2 = w * 0.40f;
                    float bh = Mathf.Max(2f, sk.U(3f));
                    HudDraw.Label(new Rect(x, y, c1, h), r.A, sk.LabelSmall);
                    HudDraw.Label(new Rect(x + c1, y, c2, h), r.B, sk.LabelRightSmall);
                    HudDraw.Bar(sk, new Rect(x + c1 + sk.Gap, y + h - bh, c2 - sk.Gap, bh), r.Value, new Color(1f, 1f, 1f, 0.45f));
                    HudDraw.Label(new Rect(x + c1 + c2, y, w - c1 - c2, h), r.C, sk.LabelRightSmall, r.Color);
                    return false;
                }
                case HudRowKind.Text:
                    HudDraw.Label(new Rect(x + sk.U(2f), y, w - sk.U(4f), h), r.A, sk.LabelWrapSmall, r.Color);
                    return false;
                case HudRowKind.Link:
                {
                    bool click = GUI.Button(new Rect(x, y + sk.U(1f), w, h - sk.U(2f)), GUIContent.none, sk.ButtonSmall);
                    HudDraw.Label(new Rect(x, y + sk.U(1f), w, h - sk.U(2f)), r.A, sk.LabelCenterSmall, HudSkin.Gold);
                    return click;
                }
                default:
                    return false;
            }
        }
    }
}
