using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Unity.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// World-space billboard labels (legacy <see cref="TextMesh"/> with Unity's built-in font) above every
    /// alive settlement: capital star + name, tier numeral/name and population, tinted with the civilization
    /// color. Labels face the camera, scale with distance to stay readable, and fade out far away (bigger
    /// tiers and capitals stay visible farther). Text is rebuilt only when name/tier/population/capital/civ
    /// change (checked every <see cref="RefreshInterval"/> seconds), so steady state allocates nothing.
    /// Label objects are pooled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettlementLabels : SimulationBoundBehaviour
    {
        public static SettlementLabels Instance { get; private set; }

        [Header("Placement")]
        [Tooltip("Label height above the town center, in tiles.")]
        public float HeightAboveGround = 2.8f;
        [Tooltip("Base world scale per tile.")]
        public float BaseScale = 0.55f;
        [Tooltip("Distance (world units) at which labels have BaseScale.")]
        public float ReferenceDistance = 30f;
        public float MinDistanceScale = 0.7f;
        public float MaxDistanceScale = 4f;

        [Header("Fading (world units)")]
        public float FadeStart = 80f;
        public float FadeEnd = 130f;
        [Tooltip("Labels closer than this fade out so they do not cover the town.")]
        public float NearFade = 5f;

        [Header("Text")]
        [Tooltip("Optional font; defaults to Unity's built-in LegacyRuntime.ttf (Arial.ttf on older versions).")]
        public Font FontOverride;
        public int FontSize = 48;
        public float CharacterSize = 0.1f;
        [Tooltip("Glyph shown before a capital's name.")]
        public string CapitalGlyph = "★";
        public float RefreshInterval = 0.25f;

        private static readonly string[] TierNumerals = { "·", "I", "II", "III", "IV", "V", "VI" };
        private static readonly string[] TierNames = { "Camp", "Hamlet", "Village", "Town", "City", "Metropolis", "Megalopolis" };

        private sealed class Label
        {
            public GameObject Go;
            public Transform T;
            public TextMesh Text;
            public MeshRenderer Renderer;
            public int SettlementId;
            public string Name;
            public SettlementTier Tier;
            public int Population = -1;
            public bool Capital;
            public uint CivColor;
            public Color Base = Color.white;
            public float Alpha = -1f;
            public bool Shown;
            public int Stamp;
            public Vector3 WorldPos;
            public float Range = 1f;
        }

        private readonly Dictionary<int, Label> _labels = new Dictionary<int, Label>();
        private readonly Stack<Label> _pool = new Stack<Label>();
        private readonly List<int> _stale = new List<int>();
        private readonly StringBuilder _sb = new StringBuilder(96);
        private readonly char[] _digits = new char[12];
        private Font _font;
        private Transform _root;
        private MapFrame _frame;
        private float _nextRefresh;
        private int _stamp;

        protected override void OnEnable()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("Multiple SettlementLabels instances; the last enabled one is SettlementLabels.Instance.", this);
            Instance = this;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<int, Label> kv in _labels) if (kv.Value.Go != null) Destroy(kv.Value.Go);
            _labels.Clear();
            while (_pool.Count > 0) { Label l = _pool.Pop(); if (l.Go != null) Destroy(l.Go); }
            if (_root != null) Destroy(_root.gameObject);
        }

        protected override void OnSimulationBound(GodSimulation sim)
        {
            _frame = MapFrame.From(sim.State.Map);
            ReleaseAll();
            _nextRefresh = 0f;
        }

        protected override void OnSimulationUnbound(GodSimulation old)
        {
            ReleaseAll();
        }

        private void ReleaseAll()
        {
            foreach (KeyValuePair<int, Label> kv in _labels) Release(kv.Value);
            _labels.Clear();
        }

        private void Release(Label l)
        {
            if (l.Go != null) l.Go.SetActive(false);
            l.Name = null;
            l.Population = -1;
            l.Alpha = -1f;
            l.Shown = false;
            _pool.Push(l);
        }

        private static Font LoadBuiltinFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { f = null; }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { f = null; }
            }
            return f;
        }

        private void LateUpdate()
        {
            if (!EnsureBound()) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            WorldState state = Sim.State;
            if (!ReferenceEquals(state.Map, _frame.Map)) { _frame = MapFrame.From(state.Map); _nextRefresh = 0f; }

            if (_font == null) _font = FontOverride != null ? FontOverride : LoadBuiltinFont();
            if (_font == null) return;
            if (_root == null)
            {
                var go = new GameObject("GodSim Settlement Labels");
                _root = go.transform;
                _root.SetParent(transform, false);
            }

            float now = Time.unscaledTime;
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + Mathf.Max(0.05f, RefreshInterval);
                Refresh(state);
            }

            Transform ct = cam.transform;
            Vector3 camPos = ct.position;
            Vector3 camFwd = ct.forward;
            Quaternion camRot = ct.rotation;
            foreach (KeyValuePair<int, Label> kv in _labels) UpdateVisual(kv.Value, camPos, camFwd, camRot);
        }

        private void Refresh(WorldState state)
        {
            _stamp++;
            IReadOnlyList<Settlement> items = state.Settlements.Items;
            float lift = HeightAboveGround * _frame.TileSize;
            for (int i = 0; i < items.Count; i++)
            {
                Settlement s = items[i];
                if (s == null || !s.IsAlive) continue;
                Label l;
                if (!_labels.TryGetValue(s.Id, out l))
                {
                    l = Rent();
                    l.SettlementId = s.Id;
                    _labels[s.Id] = l;
                }
                l.Stamp = _stamp;
                Vector3 p = GodSimCoords.TileToWorld(state.Map, s.Center);
                p.y += lift;
                l.WorldPos = p;
                UpdateText(l, s, state);
            }

            _stale.Clear();
            foreach (KeyValuePair<int, Label> kv in _labels) if (kv.Value.Stamp != _stamp) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++)
            {
                Label l;
                if (_labels.TryGetValue(_stale[i], out l)) { Release(l); _labels.Remove(_stale[i]); }
            }
        }

        private Label Rent()
        {
            Label l = _pool.Count > 0 ? _pool.Pop() : null;
            if (l == null || l.Go == null)
            {
                l = new Label();
                l.Go = new GameObject("Settlement Label");
                l.T = l.Go.transform;
                l.T.SetParent(_root, false);
                l.Text = l.Go.AddComponent<TextMesh>();
                l.Renderer = l.Go.GetComponent<MeshRenderer>();
                if (l.Renderer == null) l.Renderer = l.Go.AddComponent<MeshRenderer>();
                l.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                l.Renderer.receiveShadows = false;
            }
            l.Text.font = _font;
            l.Renderer.sharedMaterial = _font.material;
            l.Text.fontSize = Mathf.Clamp(FontSize, 8, 256);
            l.Text.characterSize = Mathf.Max(0.001f, CharacterSize);
            l.Text.anchor = TextAnchor.LowerCenter;
            l.Text.alignment = TextAlignment.Center;
            l.Text.richText = true;
            l.Renderer.enabled = false;
            l.Go.SetActive(true);
            l.Shown = false;
            l.Alpha = -1f;
            return l;
        }

        private void UpdateText(Label l, Settlement s, WorldState state)
        {
            Civilization civ = s.CivilizationId >= 0 ? state.Civilizations.Get(s.CivilizationId) : null;
            uint civColor = civ != null ? civ.Color : 0xDDDDDDFFu;
            int pop = s.Population;
            bool capital = s.IsCapital;
            if (l.Name != null && string.Equals(l.Name, s.Name, StringComparison.Ordinal) && l.Tier == s.Tier &&
                l.Population == pop && l.Capital == capital && l.CivColor == civColor)
                return;

            l.Name = s.Name ?? "";
            l.Tier = s.Tier;
            l.Population = pop;
            l.Capital = capital;
            l.CivColor = civColor;

            int tier = Mathf.Clamp((int)s.Tier, 0, TierNames.Length - 1);
            l.Range = 1f + 0.2f * tier + (capital ? 0.5f : 0f);

            _sb.Length = 0;
            if (capital && !string.IsNullOrEmpty(CapitalGlyph)) _sb.Append("<color=#FFD54A>").Append(CapitalGlyph).Append("</color> ");
            AppendSanitized(_sb, l.Name, 40);
            _sb.Append('\n').Append("<size=").Append(SizeTag(l.Text.fontSize)).Append('>');
            _sb.Append(TierNumerals[tier]).Append(' ').Append(TierNames[tier]).Append("  •  ");
            AppendPopulation(_sb, pop);
            _sb.Append("</size>");
            l.Text.text = _sb.ToString();

            Color c = GodSimPalette.FromRgba(civColor);
            c = Color.Lerp(c, Color.white, 0.35f);
            c.a = 1f;
            l.Base = c;
            l.Alpha = -1f; // force color re-apply
        }

        private static string SizeTag(int fontSize)
        {
            // Small fixed table so the size tag never allocates.
            if (fontSize <= 24) return "18";
            if (fontSize <= 32) return "24";
            if (fontSize <= 48) return "34";
            if (fontSize <= 64) return "46";
            return "60";
        }

        /// <summary>Appends a name without rich-text control characters, truncated to maxLen.</summary>
        private static void AppendSanitized(StringBuilder sb, string s, int maxLen)
        {
            if (s == null) return;
            int n = Math.Min(s.Length, maxLen);
            for (int i = 0; i < n; i++)
            {
                char ch = s[i];
                if (ch == '<' || ch == '>' || ch == '\n' || ch == '\r' || char.IsControl(ch)) continue;
                sb.Append(ch);
            }
        }

        private void AppendPopulation(StringBuilder sb, int pop)
        {
            if (pop < 0) pop = 0;
            if (pop >= 100000) { AppendInt(sb, pop / 1000); sb.Append('k'); return; }
            if (pop >= 10000) { AppendInt(sb, pop / 1000); sb.Append('.').Append((char)('0' + (pop / 100) % 10)).Append('k'); return; }
            AppendInt(sb, pop);
        }

        private void AppendInt(StringBuilder sb, int v)
        {
            if (v == 0) { sb.Append('0'); return; }
            int n = 0;
            while (v > 0 && n < _digits.Length) { _digits[n++] = (char)('0' + v % 10); v /= 10; }
            while (n > 0) sb.Append(_digits[--n]);
        }

        private void UpdateVisual(Label l, Vector3 camPos, Vector3 camFwd, Quaternion camRot)
        {
            if (l.Go == null) return;
            Vector3 d = l.WorldPos - camPos;
            float dist = d.magnitude;
            float fadeStart = FadeStart * l.Range, fadeEnd = Mathf.Max(fadeStart + 1f, FadeEnd * l.Range);
            float alpha = 1f - UnitMath.Smooth01((dist - fadeStart) / (fadeEnd - fadeStart));
            if (NearFade > 0f) alpha *= Mathf.Clamp01((dist - NearFade * 0.5f) / (NearFade * 0.5f));
            if (Vector3.Dot(d, camFwd) <= 0f) alpha = 0f;

            bool show = alpha > 0.02f;
            if (show != l.Shown)
            {
                l.Shown = show;
                l.Renderer.enabled = show;
            }
            if (!show) return;

            if (Mathf.Abs(alpha - l.Alpha) > 0.03f)
            {
                Color c = l.Base;
                c.a = alpha;
                l.Text.color = c;
                l.Alpha = alpha;
            }
            float s = BaseScale * _frame.TileSize * Mathf.Clamp(dist / Mathf.Max(0.01f, ReferenceDistance), MinDistanceScale, MaxDistanceScale);
            if (l.Capital) s *= 1.15f;
            l.T.position = l.WorldPos;
            l.T.rotation = camRot;
            l.T.localScale = new Vector3(s, s, s);
        }
    }
}
