using System;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Procedural textures and GUIStyles for the tutorial card and toasts, built for one DPI scale.
    /// Must be constructed from inside OnGUI (it reads <see cref="GUI.skin"/>). Dispose destroys the textures.
    /// Independent of the HUD skin so the tutorial works in any scene.
    /// </summary>
    internal sealed class TutorialSkin : IDisposable
    {
        public static readonly Color Gold = new Color(0.95f, 0.78f, 0.32f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.92f, 0.89f, 1f);
        public static readonly Color TextDim = new Color(0.64f, 0.65f, 0.68f, 1f);
        public static readonly Color Good = new Color(0.46f, 0.86f, 0.46f, 1f);
        public static readonly Color DotFuture = new Color(1f, 1f, 1f, 0.22f);
        public static readonly Color DotSkipped = new Color(0.55f, 0.55f, 0.58f, 0.75f);
        public static readonly Color BarBack = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color CardFill = new Color(0.075f, 0.08f, 0.105f, 0.95f);
        private static readonly Color CardBorder = new Color(0.95f, 0.78f, 0.32f, 0.85f);
        private static readonly Color ToastFill = new Color(0.09f, 0.085f, 0.07f, 0.94f);
        private static readonly Color ButtonFill = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color ButtonHover = new Color(1f, 1f, 1f, 0.17f);
        private static readonly Color ButtonActive = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color ButtonBorder = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color PrimaryFill = new Color(0.86f, 0.68f, 0.24f, 1f);
        private static readonly Color PrimaryHover = new Color(0.97f, 0.80f, 0.36f, 1f);
        private static readonly Color PrimaryActive = new Color(0.74f, 0.57f, 0.18f, 1f);
        private static readonly Color DarkText = new Color(0.10f, 0.08f, 0.04f, 1f);

        public readonly float Scale;
        public readonly GUIStyle Card, Toast, Title, Body, Small, SmallRight, Objective, ObjectiveDone;
        public readonly GUIStyle Button, Primary, Close, ToastTitle, ToastBody;
        public readonly Texture2D White, Clear, Dot, Ring, Box, ArrowUp, ArrowDown, ArrowLeft, ArrowRight;
        public readonly float Pad, Gap, ButtonH, DotSize, DotSpacing, ArrowSize, CloseSize, CheckSize, BarH, Margin;

        private readonly Texture2D[] _owned;

        public TutorialSkin(GUISkin baseSkin, float scale)
        {
            Scale = scale;
            Pad = U(16f); Gap = U(8f); ButtonH = U(30f); DotSize = U(8f); DotSpacing = U(6f);
            ArrowSize = U(26f); CloseSize = U(24f); CheckSize = U(14f); BarH = U(6f); Margin = U(12f);

            int radius = Mathf.Max(3, Px(10f));
            int bw = Mathf.Max(1, Px(1.5f));
            int br = Mathf.Max(2, Px(6f));
            White = Solid(Color.white);
            Clear = Solid(Color.clear);
            Dot = Disc(Mathf.Max(8, Px(16f)), 0f);
            Ring = Disc(Mathf.Max(8, Px(16f)), Mathf.Max(1.5f, 2.2f * scale));
            Box = Rounded(Mathf.Max(2, Px(4f)), new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.55f), Mathf.Max(1, Px(1.5f)));
            var cardTex = Rounded(radius, CardFill, CardBorder, bw);
            var toastTex = Rounded(radius, ToastFill, CardBorder, bw);
            var btn = Rounded(br, ButtonFill, ButtonBorder, Mathf.Max(1, Px(1f)));
            var btnHover = Rounded(br, ButtonHover, ButtonBorder, Mathf.Max(1, Px(1f)));
            var btnActive = Rounded(br, ButtonActive, ButtonBorder, Mathf.Max(1, Px(1f)));
            var pri = Rounded(br, PrimaryFill, PrimaryFill, 0);
            var priHover = Rounded(br, PrimaryHover, PrimaryHover, 0);
            var priActive = Rounded(br, PrimaryActive, PrimaryActive, 0);
            int arrow = Mathf.Max(8, Px(32f));
            ArrowUp = Triangle(arrow, 0); ArrowDown = Triangle(arrow, 1); ArrowLeft = Triangle(arrow, 2); ArrowRight = Triangle(arrow, 3);
            _owned = new[] { White, Clear, Dot, Ring, Box, cardTex, toastTex, btn, btnHover, btnActive, pri, priHover, priActive, ArrowUp, ArrowDown, ArrowLeft, ArrowRight };

            GUIStyle label = baseSkin != null && baseSkin.label != null ? baseSkin.label : new GUIStyle();

            Card = Panel(cardTex, radius + 1);
            Toast = Panel(toastTex, radius + 1);

            Title = TextStyle(label, 19f, FontStyle.Bold, Gold, TextAnchor.UpperLeft, true);
            Body = TextStyle(label, 14f, FontStyle.Normal, Text, TextAnchor.UpperLeft, true);
            Small = TextStyle(label, 11.5f, FontStyle.Normal, TextDim, TextAnchor.MiddleLeft, false);
            SmallRight = TextStyle(label, 11.5f, FontStyle.Normal, TextDim, TextAnchor.MiddleRight, false);
            Objective = TextStyle(label, 13f, FontStyle.Bold, Text, TextAnchor.UpperLeft, true);
            ObjectiveDone = TextStyle(label, 13f, FontStyle.Bold, Good, TextAnchor.UpperLeft, true);
            ToastTitle = TextStyle(label, 14.5f, FontStyle.Bold, Gold, TextAnchor.UpperLeft, true);
            ToastBody = TextStyle(label, 12.5f, FontStyle.Normal, Text, TextAnchor.UpperLeft, true);

            Button = Btn(label, btn, btnHover, btnActive, br + 1, Text, Text);
            Primary = Btn(label, pri, priHover, priActive, br + 1, DarkText, DarkText);
            Primary.fontStyle = FontStyle.Bold;

            Close = TextStyle(label, 18f, FontStyle.Bold, TextDim, TextAnchor.MiddleCenter, false);
            // IMGUI only switches to the hover/active state when that state has a background.
            Close.hover.background = Clear; Close.active.background = Clear;
            Close.hover.textColor = Gold;
            Close.active.textColor = Color.white;
        }

        /// <summary>Scaled pixels, rounded.</summary>
        public int Px(float v) => Mathf.Max(0, Mathf.RoundToInt(v * Scale));
        /// <summary>Scaled units as float (pixel aligned).</summary>
        public float U(float v) => Mathf.Round(v * Scale);

        public void Dispose()
        {
            for (int i = 0; i < _owned.Length; i++)
                if (_owned[i] != null) UnityEngine.Object.Destroy(_owned[i]);
        }

        // ------------------------------------------------------------------ styles

        private GUIStyle TextStyle(GUIStyle baseStyle, float size, FontStyle fontStyle, Color color, TextAnchor anchor, bool wrap)
        {
            var s = new GUIStyle(baseStyle)
            {
                fontSize = Mathf.Max(8, Px(size)),
                fontStyle = fontStyle,
                alignment = anchor,
                wordWrap = wrap,
                richText = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            SetTextColor(s, color);
            s.normal.background = null; s.hover.background = null; s.active.background = null;
            return s;
        }

        private static GUIStyle Panel(Texture2D tex, int border)
        {
            var s = new GUIStyle { border = new RectOffset(border, border, border, border) };
            s.normal.background = tex;
            return s;
        }

        private GUIStyle Btn(GUIStyle baseStyle, Texture2D normal, Texture2D hover, Texture2D active, int border, Color text, Color hoverText)
        {
            var s = TextStyle(baseStyle, 13f, FontStyle.Normal, text, TextAnchor.MiddleCenter, false);
            s.border = new RectOffset(border, border, border, border);
            s.padding = new RectOffset(Px(12f), Px(12f), Px(4f), Px(4f));
            s.normal.background = normal;
            s.hover.background = hover;
            s.active.background = active;
            s.focused.background = normal;
            s.hover.textColor = hoverText;
            return s;
        }

        private static void SetTextColor(GUIStyle s, Color c)
        {
            s.normal.textColor = c; s.hover.textColor = c; s.active.textColor = c; s.focused.textColor = c;
        }

        // ------------------------------------------------------------------ textures

        private static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return t;
        }

        private static Texture2D Finish(Texture2D t, Color[] px)
        {
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        private static Texture2D Solid(Color c)
        {
            var t = NewTex(2, 2);
            return Finish(t, new[] { c, c, c, c });
        }

        /// <summary>9-slice-able rounded rectangle (size = 2r+2) with an anti-aliased edge and optional border.</summary>
        private static Texture2D Rounded(int r, Color fill, Color border, int borderPx)
        {
            int size = r * 2 + 2;
            var t = NewTex(size, size);
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - r);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - r);
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float dist = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float cover = Mathf.Clamp01(0.5f - dist);
                    Color c = fill;
                    if (borderPx > 0)
                    {
                        float b = Mathf.Clamp01(dist + borderPx + 0.5f);
                        c = Color.Lerp(fill, border, b);
                    }
                    c.a *= cover;
                    px[y * size + x] = c;
                }
            return Finish(t, px);
        }

        /// <summary>White anti-aliased disc (thickness 0) or ring (thickness &gt; 0), tinted with GUI.color.</summary>
        private static Texture2D Disc(int size, float thickness)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            float c = size * 0.5f, rad = c - 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rad - d + 0.5f);
                    if (thickness > 0f) a *= Mathf.Clamp01(d - (rad - thickness) + 0.5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            return Finish(t, px);
        }

        /// <summary>White anti-aliased triangle. dir: 0 up, 1 down, 2 left, 3 right (screen space).</summary>
        private static Texture2D Triangle(int size, int dir)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            // Texture rows are bottom-up in IMGUI, so "up" on screen means apex at high y.
            float s = size, m = size * 0.5f, inset = 1f;
            float ax, ay, bx, by, cx, cy;
            switch (dir)
            {
                case 0: ax = m; ay = s - inset; bx = inset; by = inset; cx = s - inset; cy = inset; break;
                case 1: ax = m; ay = inset; bx = s - inset; by = s - inset; cx = inset; cy = s - inset; break;
                case 2: ax = inset; ay = m; bx = s - inset; by = s - inset; cx = s - inset; cy = inset; break;
                default: ax = s - inset; ay = m; bx = inset; by = inset; cx = inset; cy = s - inset; break;
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f;
                    float d = Mathf.Min(EdgeDist(px0, py0, ax, ay, bx, by, cx, cy),
                              Mathf.Min(EdgeDist(px0, py0, bx, by, cx, cy, ax, ay), EdgeDist(px0, py0, cx, cy, ax, ay, bx, by)));
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(d + 0.5f));
                }
            return Finish(t, px);
        }

        /// <summary>Signed distance from p to the line a-b, positive on the side of the third vertex c.</summary>
        private static float EdgeDist(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float ex = bx - ax, ey = by - ay;
            float len = Mathf.Sqrt(ex * ex + ey * ey);
            if (len < 1e-4f) return 0f;
            float d = ((px - ax) * ey - (py - ay) * ex) / len;
            float side = (cx - ax) * ey - (cy - ay) * ex;
            return side >= 0f ? d : -d;
        }
    }
}
