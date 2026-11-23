using System.Collections.Generic;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Runtime-built IMGUI skin for the HUD: dark translucent rounded panels, gold accents. Every texture is
    /// generated procedurally (anti-aliased rounded rects used as 9-slice backgrounds) at the current UI scale
    /// so corners and fonts stay crisp. Rebuilt only when the scale changes.
    /// </summary>
    internal sealed class HudSkin
    {
        // ------------------------------------------------------------------ palette
        public static readonly Color Gold = new Color(0.95f, 0.78f, 0.32f, 1f);
        public static readonly Color GoldDim = new Color(0.72f, 0.58f, 0.26f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.92f, 0.89f, 1f);
        public static readonly Color TextDim = new Color(0.64f, 0.65f, 0.68f, 1f);
        public static readonly Color Good = new Color(0.46f, 0.86f, 0.46f, 1f);
        public static readonly Color Bad = new Color(0.96f, 0.38f, 0.33f, 1f);
        public static readonly Color Warn = new Color(0.98f, 0.70f, 0.26f, 1f);
        public static readonly Color ManaBlue = new Color(0.36f, 0.62f, 1f, 1f);
        public static readonly Color Health = new Color(0.92f, 0.42f, 0.48f, 1f);
        public static readonly Color Faith = new Color(0.78f, 0.62f, 1f, 1f);
        public static readonly Color Research = new Color(0.40f, 0.85f, 0.95f, 1f);
        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.55f);

        private static readonly Color PanelFill = new Color(0.075f, 0.082f, 0.10f, 0.88f);
        private static readonly Color PanelEdge = new Color(1f, 1f, 1f, 0.10f);

        public readonly float Scale;
        public readonly GUISkin Skin;

        public GUIStyle Panel, TopBar, Modal, Tooltip, Toast;
        public GUIStyle Label, LabelBold, LabelSmall, LabelTiny, LabelDim, LabelRight, LabelRightSmall, LabelCenter, LabelCenterSmall, LabelWrap, LabelWrapSmall;
        public GUIStyle Title, Big, Section;
        public GUIStyle Button, ButtonSmall, PowerButton, Row, Chip, Tab;
        public GUIStyle TextField, Slider, SliderThumb, ScrollV, ScrollVThumb, ScrollH, ScrollHThumb;
        public GUIStyle BarBack, BarFill, Swatch, CheckBox, CheckMark, Line, White, Badge, BadgeBg;

        /// <summary>Scaled pixel sizes used by every panel.</summary>
        public readonly float Pad, Gap, LineH, SmallLineH, ButtonH, ScrollbarW;

        private readonly List<Texture2D> _textures = new List<Texture2D>(32);

        private HudSkin(GUISkin baseSkin, float scale)
        {
            Scale = scale;
            Pad = Mathf.Round(8f * scale);
            Gap = Mathf.Round(4f * scale);
            LineH = Mathf.Round(18f * scale);
            SmallLineH = Mathf.Round(15f * scale);
            ButtonH = Mathf.Round(24f * scale);
            ScrollbarW = Mathf.Round(8f * scale);

            Skin = Object.Instantiate(baseSkin);
            Skin.hideFlags = HideFlags.HideAndDontSave;
            Skin.name = "GodSimHUDSkin";

            int r = Mathf.Max(2, Mathf.RoundToInt(6f * scale));
            int rs = Mathf.Max(2, Mathf.RoundToInt(4f * scale));
            int rb = Mathf.Max(1, Mathf.RoundToInt(3f * scale));
            float bw = Mathf.Max(1f, scale);

            Texture2D panelTex = Rounded(r, PanelFill, PanelEdge, bw, 0.04f, out RectOffset panelBorder);
            Texture2D topTex = Rounded(r, new Color(0.06f, 0.065f, 0.08f, 0.92f), new Color(Gold.r, Gold.g, Gold.b, 0.30f), bw, 0.05f, out RectOffset topBorder);
            Texture2D modalTex = Rounded(r, new Color(0.08f, 0.085f, 0.105f, 0.97f), new Color(Gold.r, Gold.g, Gold.b, 0.45f), bw, 0.05f, out RectOffset modalBorder);
            Texture2D tipTex = Rounded(rs, new Color(0.03f, 0.035f, 0.045f, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 0.55f), bw, 0f, out RectOffset tipBorder);
            Texture2D toastTex = Rounded(rs, new Color(0.05f, 0.055f, 0.07f, 0.93f), new Color(1f, 1f, 1f, 0.16f), bw, 0f, out RectOffset toastBorder);

            Texture2D btn = Rounded(rs, new Color(0.20f, 0.21f, 0.25f, 0.95f), new Color(1f, 1f, 1f, 0.14f), bw, 0.10f, out RectOffset btnBorder);
            Texture2D btnHover = Rounded(rs, new Color(0.27f, 0.28f, 0.33f, 0.97f), new Color(Gold.r, Gold.g, Gold.b, 0.50f), bw, 0.10f, out _);
            Texture2D btnActive = Rounded(rs, new Color(0.14f, 0.14f, 0.17f, 0.98f), new Color(Gold.r, Gold.g, Gold.b, 0.80f), bw, -0.06f, out _);
            Texture2D btnOn = Rounded(rs, new Color(0.34f, 0.27f, 0.10f, 0.97f), Gold, bw * 1.5f, 0.10f, out _);
            Texture2D btnOnHover = Rounded(rs, new Color(0.42f, 0.33f, 0.13f, 0.98f), Gold, bw * 1.5f, 0.10f, out _);

            Texture2D rowHover = Rounded(rs, new Color(1f, 1f, 1f, 0.07f), new Color(1f, 1f, 1f, 0f), 0f, 0f, out RectOffset rowBorder);
            Texture2D rowActive = Rounded(rs, new Color(Gold.r, Gold.g, Gold.b, 0.16f), new Color(1f, 1f, 1f, 0f), 0f, 0f, out _);
            Texture2D chipOff = Rounded(rb, new Color(0.16f, 0.17f, 0.20f, 0.90f), new Color(1f, 1f, 1f, 0.08f), bw, 0f, out RectOffset chipBorder);
            Texture2D chipOn = Rounded(rb, new Color(0.85f, 0.85f, 0.85f, 0.95f), new Color(1f, 1f, 1f, 0.35f), bw, 0f, out _);
            Texture2D tabOff = Rounded(rs, new Color(0.13f, 0.14f, 0.17f, 0.9f), new Color(1f, 1f, 1f, 0.08f), bw, 0f, out RectOffset tabBorder);
            Texture2D tabOn = Rounded(rs, new Color(0.24f, 0.20f, 0.10f, 0.97f), new Color(Gold.r, Gold.g, Gold.b, 0.85f), bw, 0f, out _);

            Texture2D field = Rounded(rs, new Color(0.03f, 0.035f, 0.045f, 0.95f), new Color(1f, 1f, 1f, 0.18f), bw, 0f, out RectOffset fieldBorder);
            Texture2D fieldFocus = Rounded(rs, new Color(0.03f, 0.035f, 0.045f, 0.98f), Gold, bw, 0f, out _);

            int sliderH = Mathf.Max(8, Mathf.RoundToInt(16f * scale));
            int trackHalf = Mathf.Max(2, Mathf.RoundToInt(3f * scale));
            Texture2D track = Shape(rb * 2 + 4, sliderH, rb * 2 + 4, trackHalf * 2, Mathf.Min(rb, trackHalf), new Color(0.03f, 0.035f, 0.045f, 0.95f), new Color(1f, 1f, 1f, 0.2f), bw, 0f);
            int thumbSize = Mathf.Max(8, Mathf.RoundToInt(14f * scale));
            Texture2D thumb = Shape(thumbSize, thumbSize, thumbSize, thumbSize, thumbSize * 0.5f, Gold, new Color(0.25f, 0.18f, 0.05f, 1f), bw, 0.15f);
            Texture2D thumbHover = Shape(thumbSize, thumbSize, thumbSize, thumbSize, thumbSize * 0.5f, new Color(1f, 0.88f, 0.5f, 1f), new Color(0.25f, 0.18f, 0.05f, 1f), bw, 0.15f);

            Texture2D barBack = Rounded(rb, new Color(0f, 0f, 0f, 0.55f), new Color(1f, 1f, 1f, 0.10f), bw, 0f, out RectOffset barBorder);
            Texture2D barFill = Rounded(rb, Color.white, new Color(1f, 1f, 1f, 0f), 0f, 0.18f, out _);
            Texture2D swatch = Rounded(rb, Color.white, new Color(0f, 0f, 0f, 0.6f), bw, 0f, out RectOffset swatchBorder);
            Texture2D checkBox = Rounded(rb, new Color(0.03f, 0.035f, 0.045f, 0.95f), new Color(1f, 1f, 1f, 0.35f), bw, 0f, out RectOffset checkBorder);
            Texture2D checkMark = Rounded(Mathf.Max(1, rb - 1), Gold, new Color(1f, 1f, 1f, 0f), 0f, 0.2f, out RectOffset markBorder);
            Texture2D white = Solid(Color.white);
            Texture2D scrollTrack = Rounded(rb, new Color(0f, 0f, 0f, 0.35f), new Color(1f, 1f, 1f, 0f), 0f, 0f, out RectOffset scrollBorder);
            Texture2D scrollThumb = Rounded(rb, new Color(1f, 1f, 1f, 0.28f), new Color(1f, 1f, 1f, 0f), 0f, 0f, out _);
            Texture2D scrollThumbHover = Rounded(rb, new Color(Gold.r, Gold.g, Gold.b, 0.75f), new Color(1f, 1f, 1f, 0f), 0f, 0f, out _);

            int fs = Font(13f), fsSmall = Font(11.5f), fsTiny = Font(10f), fsTitle = Font(15f), fsBig = Font(19f);

            Panel = Box(panelTex, panelBorder, Pad);
            TopBar = Box(topTex, topBorder, Pad);
            Modal = Box(modalTex, modalBorder, Mathf.Round(14f * scale));
            Tooltip = Box(tipTex, tipBorder, Mathf.Round(7f * scale));
            Tooltip.fontSize = fsSmall; Tooltip.wordWrap = true; Tooltip.richText = true; Tooltip.normal.textColor = Text; Tooltip.alignment = TextAnchor.UpperLeft;
            Toast = Box(toastTex, toastBorder, Mathf.Round(7f * scale));
            Toast.fontSize = fs; Toast.wordWrap = true; Toast.alignment = TextAnchor.MiddleLeft; Toast.normal.textColor = Text;
            Toast.padding = new RectOffset(Mathf.RoundToInt(14f * scale), Mathf.RoundToInt(10f * scale), Mathf.RoundToInt(7f * scale), Mathf.RoundToInt(7f * scale));

            Label = Text0(fs, Text, TextAnchor.MiddleLeft);
            LabelBold = Text0(fs, Text, TextAnchor.MiddleLeft); LabelBold.fontStyle = FontStyle.Bold;
            LabelSmall = Text0(fsSmall, Text, TextAnchor.MiddleLeft);
            LabelTiny = Text0(fsTiny, TextDim, TextAnchor.MiddleLeft);
            LabelDim = Text0(fsSmall, TextDim, TextAnchor.MiddleLeft);
            LabelRight = Text0(fs, Text, TextAnchor.MiddleRight);
            LabelRightSmall = Text0(fsSmall, Text, TextAnchor.MiddleRight);
            LabelCenter = Text0(fs, Text, TextAnchor.MiddleCenter);
            LabelCenterSmall = Text0(fsSmall, Text, TextAnchor.MiddleCenter);
            LabelWrap = Text0(fs, Text, TextAnchor.UpperLeft); LabelWrap.wordWrap = true;
            LabelWrapSmall = Text0(fsSmall, Text, TextAnchor.UpperLeft); LabelWrapSmall.wordWrap = true;
            Title = Text0(fsTitle, Gold, TextAnchor.MiddleLeft); Title.fontStyle = FontStyle.Bold;
            Big = Text0(fsBig, Gold, TextAnchor.MiddleCenter); Big.fontStyle = FontStyle.Bold;
            Section = Text0(fsTiny, GoldDim, TextAnchor.LowerLeft); Section.fontStyle = FontStyle.Bold;
            Badge = Text0(fsTiny, Color.white, TextAnchor.MiddleCenter); Badge.fontStyle = FontStyle.Bold;
            BadgeBg = Box(chipOn, chipBorder, 0f);

            Button = Btn(btn, btnHover, btnActive, btnOn, btnOnHover, btnBorder, fs);
            ButtonSmall = Btn(btn, btnHover, btnActive, btnOn, btnOnHover, btnBorder, fsSmall);
            PowerButton = Btn(btn, btnHover, btnActive, btnOn, btnOnHover, btnBorder, fsSmall);
            PowerButton.alignment = TextAnchor.UpperCenter;
            PowerButton.wordWrap = true;

            Row = new GUIStyle();
            Row.normal.background = null;
            Row.hover.background = rowHover;
            Row.active.background = rowActive;
            Row.onNormal.background = rowActive;
            Row.onHover.background = rowActive;
            Row.border = rowBorder;
            Row.fontSize = fs;
            Row.normal.textColor = Text; Row.hover.textColor = Color.white;
            Row.alignment = TextAnchor.MiddleLeft;
            Row.padding = new RectOffset(Px(6f), Px(6f), 0, 0);

            Chip = Btn(chipOff, chipOff, chipOn, chipOn, chipOn, chipBorder, fsTiny);
            Chip.normal.textColor = TextDim; Chip.hover.textColor = Text;
            Chip.onNormal.textColor = new Color(0.05f, 0.05f, 0.06f, 1f); Chip.onHover.textColor = Color.black; Chip.onActive.textColor = Color.black;
            Chip.padding = new RectOffset(Px(2f), Px(2f), 0, 0);
            Chip.clipping = TextClipping.Clip;

            Tab = Btn(tabOff, btnHover, btnActive, tabOn, tabOn, tabBorder, fsSmall);

            TextField = new GUIStyle();
            TextField.normal.background = field; TextField.hover.background = field;
            TextField.focused.background = fieldFocus; TextField.onFocused.background = fieldFocus;
            TextField.border = fieldBorder;
            TextField.normal.textColor = Text; TextField.hover.textColor = Text; TextField.focused.textColor = Color.white;
            TextField.fontSize = fs; TextField.alignment = TextAnchor.MiddleLeft; TextField.clipping = TextClipping.Clip;
            TextField.padding = new RectOffset(Px(6f), Px(6f), Px(3f), Px(3f));

            Slider = new GUIStyle();
            Slider.normal.background = track;
            Slider.border = new RectOffset(rb + 1, rb + 1, 0, 0);
            Slider.fixedHeight = sliderH;
            Slider.padding = new RectOffset(0, 0, Mathf.Max(0, (sliderH - thumbSize) / 2), Mathf.Max(0, (sliderH - thumbSize) / 2));
            SliderThumb = new GUIStyle();
            SliderThumb.normal.background = thumb; SliderThumb.hover.background = thumbHover; SliderThumb.active.background = thumbHover;
            SliderThumb.fixedWidth = thumbSize; SliderThumb.fixedHeight = thumbSize;

            ScrollV = new GUIStyle(); ScrollV.normal.background = scrollTrack; ScrollV.border = scrollBorder; ScrollV.fixedWidth = ScrollbarW;
            ScrollV.margin = new RectOffset(Px(2f), 0, 0, 0);
            ScrollVThumb = new GUIStyle(); ScrollVThumb.normal.background = scrollThumb; ScrollVThumb.hover.background = scrollThumbHover; ScrollVThumb.active.background = scrollThumbHover;
            ScrollVThumb.border = scrollBorder; ScrollVThumb.fixedWidth = ScrollbarW;
            ScrollH = new GUIStyle(); ScrollH.fixedHeight = 0f; ScrollH.fixedWidth = 0f;
            // GUI.VerticalScrollbar resolves "<name>thumb" / "<name>upbutton" / "<name>downbutton" in GUI.skin,
            // so the explicit styles passed to BeginScrollView must carry the canonical names.
            ScrollV.name = "verticalscrollbar";
            ScrollH.name = "horizontalscrollbar";
            ScrollHThumb = new GUIStyle();

            BarBack = Box(barBack, barBorder, 0f);
            BarFill = Box(barFill, barBorder, 0f);
            Swatch = Box(swatch, swatchBorder, 0f);
            CheckBox = Box(checkBox, checkBorder, 0f);
            CheckMark = Box(checkMark, markBorder, 0f);
            White = new GUIStyle(); White.normal.background = white;
            Line = new GUIStyle(); Line.normal.background = white;

            // Wire the skin itself so that any control drawn with default styles (scroll views, sliders) matches.
            Skin.box = new GUIStyle(Panel);
            Skin.label = new GUIStyle(Label);
            Skin.button = new GUIStyle(Button);
            Skin.textField = new GUIStyle(TextField);
            Skin.horizontalSlider = new GUIStyle(Slider);
            Skin.horizontalSliderThumb = new GUIStyle(SliderThumb);
            Skin.verticalScrollbar = new GUIStyle(ScrollV);
            Skin.verticalScrollbarThumb = new GUIStyle(ScrollVThumb);
            Skin.verticalScrollbarUpButton = new GUIStyle();
            Skin.verticalScrollbarDownButton = new GUIStyle();
            Skin.scrollView = new GUIStyle();
            Skin.settings.cursorColor = Gold;
            Skin.settings.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
        }

        /// <summary>Builds a skin for the given scale. Must be called from OnGUI (needs the built-in GUI.skin).</summary>
        public static HudSkin Build(GUISkin baseSkin, float scale) => new HudSkin(baseSkin, scale);

        public int Px(float v) => Mathf.Max(0, Mathf.RoundToInt(v * Scale));
        public float U(float v) => Mathf.Round(v * Scale);

        /// <summary>Destroys every generated texture and the skin instance.</summary>
        public void Dispose()
        {
            for (int i = 0; i < _textures.Count; i++) if (_textures[i] != null) Object.Destroy(_textures[i]);
            _textures.Clear();
            if (Skin != null) Object.Destroy(Skin);
        }

        // ------------------------------------------------------------------ style helpers
        private int Font(float basePx) => Mathf.Max(8, Mathf.RoundToInt(basePx * Scale));

        private GUIStyle Box(Texture2D bg, RectOffset border, float pad)
        {
            var s = new GUIStyle();
            s.normal.background = bg;
            s.border = border;
            int p = Mathf.RoundToInt(pad);
            s.padding = new RectOffset(p, p, p, p);
            s.normal.textColor = Text;
            return s;
        }

        private static GUIStyle Text0(int size, Color color, TextAnchor anchor)
        {
            var s = new GUIStyle();
            s.fontSize = size;
            s.normal.textColor = color;
            s.alignment = anchor;
            s.clipping = TextClipping.Clip;
            s.wordWrap = false;
            s.richText = false;
            return s;
        }

        private GUIStyle Btn(Texture2D n, Texture2D h, Texture2D a, Texture2D on, Texture2D onHover, RectOffset border, int size)
        {
            var s = new GUIStyle();
            s.normal.background = n; s.hover.background = h; s.active.background = a; s.focused.background = n;
            s.onNormal.background = on; s.onHover.background = onHover; s.onActive.background = a; s.onFocused.background = on;
            s.normal.textColor = Text; s.hover.textColor = Color.white; s.active.textColor = Gold; s.focused.textColor = Text;
            s.onNormal.textColor = Gold; s.onHover.textColor = new Color(1f, 0.9f, 0.6f, 1f); s.onActive.textColor = Gold; s.onFocused.textColor = Gold;
            s.border = border;
            s.fontSize = size;
            s.alignment = TextAnchor.MiddleCenter;
            s.clipping = TextClipping.Clip;
            s.padding = new RectOffset(Px(6f), Px(6f), Px(3f), Px(3f));
            return s;
        }

        // ------------------------------------------------------------------ procedural textures
        private Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            _textures.Add(t);
            return t;
        }

        private Texture2D Solid(Color c)
        {
            var t = NewTex(2, 2);
            var px = new Color32[4];
            Color32 c32 = c;
            for (int i = 0; i < 4; i++) px[i] = c32;
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>A (2r+4)^2 rounded rect suitable for 9-slicing with a border of r+1.</summary>
        private Texture2D Rounded(int radius, Color fill, Color border, float borderWidth, float gradient, out RectOffset slice)
        {
            int size = radius * 2 + 4;
            slice = new RectOffset(radius + 1, radius + 1, radius + 1, radius + 1);
            return Shape(size, size, size, size, radius, fill, border, borderWidth, gradient);
        }

        /// <summary>
        /// Anti-aliased rounded box of extent (boxW x boxH) centered in a (texW x texH) texture, using a signed
        /// distance field. <paramref name="gradient"/> brightens the top and darkens the bottom.
        /// </summary>
        private Texture2D Shape(int texW, int texH, float boxW, float boxH, float radius, Color fill, Color border, float borderWidth, float gradient)
        {
            var t = NewTex(texW, texH);
            var px = new Color32[texW * texH];
            float cx = texW * 0.5f, cy = texH * 0.5f;
            float ix = boxW * 0.5f - radius, iy = boxH * 0.5f - radius;
            for (int y = 0; y < texH; y++)
            {
                float g = texH > 1 ? (y / (float)(texH - 1) - 0.5f) * gradient : 0f; // texture y=0 is the bottom row
                for (int x = 0; x < texW; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - cx) - ix;
                    float qy = Mathf.Abs(y + 0.5f - cy) - iy;
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float dist = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float cover = Mathf.Clamp01(0.5f - dist);
                    if (cover <= 0f) { px[y * texW + x] = new Color32(0, 0, 0, 0); continue; }
                    Color c = fill;
                    c.r = Mathf.Clamp01(c.r + g); c.g = Mathf.Clamp01(c.g + g); c.b = Mathf.Clamp01(c.b + g);
                    if (borderWidth > 0f)
                    {
                        float tb = Mathf.Clamp01(dist + borderWidth + 0.5f);
                        c = Color.Lerp(c, border, tb);
                    }
                    c.a *= cover;
                    px[y * texW + x] = c;
                }
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
