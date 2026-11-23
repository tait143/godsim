using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Small self-contained IMGUI legend for <see cref="MapOverlayRenderer"/>: title, color bar (or civ /
    /// faith swatches for categorical modes), min/max labels and key hints. Drawn at the bottom-left, above
    /// the chronicle panel (<see cref="BottomOffset"/>), scaled for the screen height. Hidden when the
    /// overlay mode is <see cref="OverlayMode.None"/>. Does not depend on HUD internals.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OverlayLegend : MonoBehaviour
    {
        [SerializeField, Tooltip("Renderer to describe (defaults to MapOverlayRenderer.Instance).")]
        private MapOverlayRenderer _renderer = null;

        [Tooltip("Distance from the left screen edge, in reference pixels (1080p).")]
        public float LeftOffset = 12f;

        [Tooltip("Distance from the bottom screen edge, in reference pixels; clears the chronicle panel.")]
        public float BottomOffset = 210f;

        [Tooltip("Panel width in reference pixels.")]
        public float Width = 300f;

        [Tooltip("Show the overlay key hints.")]
        public bool ShowKeyHints = true;

        [Tooltip("IMGUI depth (lower draws on top).")]
        public int GuiDepth = -5;

        private const int BarResolution = 128;
        private const float Pad = 8f;
        private const float TitleHeight = 20f;
        private const float BarHeight = 14f;
        private const float LabelHeight = 16f;
        private const float SwatchRow = 18f;
        private const float HintHeight = 30f;

        private Texture2D _bar;
        private readonly Color32[] _barPixels = new Color32[BarResolution];
        private ColorRampId _barRamp = (ColorRampId)(-1);
        private GUIStyle _titleStyle, _labelStyle, _labelRightStyle, _hintStyle;

        private void OnDestroy()
        {
            if (_bar != null) Destroy(_bar);
            _bar = null;
        }

        private void OnGUI()
        {
            var r = _renderer != null ? _renderer : MapOverlayRenderer.Instance;
            if (r == null || !r.isActiveAndEnabled || r.Mode == OverlayMode.None) return;
            EnsureStyles();

            bool categorical = r.IsCategorical;
            int swatches = categorical ? r.LegendCategoryCount : 0;
            float height = Pad + TitleHeight + (categorical ? Mathf.Max(1, swatches) * SwatchRow : BarHeight + LabelHeight)
                           + (ShowKeyHints ? HintHeight : 0f) + Pad;

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 3f);
            Matrix4x4 prevMatrix = GUI.matrix;
            Color prevColor = GUI.color;
            int prevDepth = GUI.depth;
            GUI.depth = GuiDepth;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float vh = Screen.height / scale;
            float w = Mathf.Max(160f, Width);
            var panel = new Rect(LeftOffset, Mathf.Max(0f, vh - BottomOffset - height), w, height);

            GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.72f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = prevColor;

            float x = panel.x + Pad, y = panel.y + Pad, inner = w - 2f * Pad;
            GUI.Label(new Rect(x, y, inner, TitleHeight), r.LegendTitle, _titleStyle);
            y += TitleHeight;

            if (categorical)
            {
                if (swatches == 0)
                {
                    GUI.Label(new Rect(x, y, inner, SwatchRow), r.Mode == OverlayMode.Religion ? "No founded religions yet" : "No territory", _labelStyle);
                    y += SwatchRow;
                }
                for (int i = 0; i < swatches; i++)
                {
                    string label;
                    Color c;
                    if (!r.TryGetLegendCategory(i, out label, out c)) continue;
                    c.a = 1f;
                    GUI.color = c;
                    GUI.DrawTexture(new Rect(x, y + 3f, 12f, 12f), Texture2D.whiteTexture);
                    GUI.color = prevColor;
                    GUI.Label(new Rect(x + 18f, y, inner - 18f, SwatchRow), label, _labelStyle);
                    y += SwatchRow;
                }
            }
            else
            {
                EnsureBar(r.Ramp);
                if (_bar != null) GUI.DrawTexture(new Rect(x, y, inner, BarHeight), _bar);
                y += BarHeight;
                GUI.Label(new Rect(x, y, inner * 0.5f, LabelHeight), r.LegendMinLabel, _labelStyle);
                GUI.Label(new Rect(x + inner * 0.5f, y, inner * 0.5f, LabelHeight), r.LegendMaxLabel, _labelRightStyle);
                y += LabelHeight;
            }

            if (ShowKeyHints) GUI.Label(new Rect(x, y + 2f, inner, HintHeight), MapOverlayRenderer.KeyHints, _hintStyle);

            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
            GUI.depth = prevDepth;
        }

        private void EnsureBar(ColorRampId ramp)
        {
            if (_bar != null && _barRamp == ramp) return;
            if (_bar == null)
            {
                _bar = new Texture2D(BarResolution, 1, TextureFormat.RGBA32, false, false)
                {
                    name = "GodSim Overlay Legend",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
            }
            for (int i = 0; i < BarResolution; i++)
            {
                Color32 c = MapOverlayRenderer.ToColor32(ColorRamps.Evaluate(ramp, i / (float)(BarResolution - 1)));
                c.a = 255;
                _barPixels[i] = c;
            }
            _bar.SetPixels32(_barPixels);
            _bar.Apply(false, false);
            _barRamp = ramp;
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;
            var baseLabel = GUI.skin != null && GUI.skin.label != null ? GUI.skin.label : new GUIStyle();
            _titleStyle = new GUIStyle(baseLabel) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _titleStyle.normal.textColor = Color.white;
            _labelStyle = new GUIStyle(baseLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            _labelRightStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleRight };
            _hintStyle = new GUIStyle(baseLabel) { fontSize = 10, alignment = TextAnchor.UpperLeft, wordWrap = true };
            _hintStyle.normal.textColor = new Color(0.7f, 0.72f, 0.75f, 1f);
        }
    }
}
