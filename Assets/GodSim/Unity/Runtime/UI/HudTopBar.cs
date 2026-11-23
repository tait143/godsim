using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>Top bar: menu, date/world age, mana, alignment meter, population, pause and speed controls.</summary>
    internal sealed class HudTopBar
    {
        private readonly GodSimHUD _hud;

        private readonly GUIContent _menu = new GUIContent("≡  Menu", "Menu (Esc)");
        private readonly GUIContent _title = new GUIContent("GODSIM");
        private readonly GUIContent _noWorld = new GUIContent("No world yet. Shape one to begin.");
        private readonly GUIContent _newWorld = new GUIContent("New World...", "Create a new world");
        private readonly GUIContent _date = new GUIContent();
        private readonly GUIContent _age = new GUIContent();
        private readonly GUIContent _manaCaption = new GUIContent("MANA");
        private readonly GUIContent _mana = new GUIContent();
        private readonly GUIContent _regen = new GUIContent();
        private readonly GUIContent _wrath = new GUIContent("WRATH");
        private readonly GUIContent _grace = new GUIContent("GRACE");
        private readonly GUIContent _pop = new GUIContent();
        private readonly GUIContent _popCaption = new GUIContent("POPULATION");
        private readonly GUIContent _pause = new GUIContent("II", "Pause (Space)");
        private readonly GUIContent _play = new GUIContent("►", "Resume (Space)");
        private readonly GUIContent _speed = new GUIContent();
        private GUIContent[] _speedButtons = new GUIContent[0];

        private string _manaTip, _alignTip, _popTip, _dateTip;
        private float _mana01, _alignment;
        private bool _paused;
        private int _speedIndex;

        public Rect LastRect { get; private set; }

        public HudTopBar(GodSimHUD hud) { _hud = hud; }

        public void OnSimulationChanged() { }
        public void OnSkinChanged() { }

        public void Refresh()
        {
            var levels = GodSimRunner.SpeedLevels;
            int n = levels != null ? levels.Length : 0;
            if (_speedButtons.Length != n)
            {
                _speedButtons = new GUIContent[n];
                for (int i = 0; i < n; i++)
                    _speedButtons[i] = new GUIContent(levels[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×",
                        "Speed " + (i + 1) + " (+/- to change)");
            }

            var runner = _hud.Runner;
            if (runner != null)
            {
                _paused = runner.Paused;
                _speedIndex = runner.SpeedIndex;
                _speed.text = _paused ? "Paused" : HudText.F1(runner.TicksPerSecond) + " days/s";
            }

            var st = _hud.State;
            if (st == null) return;
            _date.text = SimTime.Format(st.Tick);
            _dateTip = "Day " + HudText.Int(SimTime.DayOfYear(st.Tick) + 1) + " of " + SimTime.TicksPerYear + " (tick " + st.Tick + ")";
            _age.text = "World age: " + _hud.AgeName(st.WorldAge);

            var god = st.God;
            float max = Mathf.Max(1f, MathUtil.Sanitize(god.MaxMana, 1f));
            float mana = MathUtil.Sanitize(god.Mana);
            _mana01 = mana / max;
            _mana.text = HudText.F0(mana) + " / " + HudText.F0(max);
            _regen.text = HudText.Signed(god.ManaPerTick) + "/day";
            _manaTip = "Divine mana. Regenerates from your followers' faith and devotion.\nRegen: " + HudText.Signed(god.ManaPerTick, "0.000") +
                       " per day (" + HudText.Signed(god.ManaPerTick * SimTime.TicksPerYear, "0.0") + " per year)\nPowers cast: " + god.PowersCast;

            _alignment = MathUtil.Clamp(MathUtil.Sanitize(god.Alignment), -1f, 1f);
            string mood = _alignment < -0.6f ? "Wrathful" : _alignment < -0.2f ? "Stern" : _alignment <= 0.2f ? "Balanced" : _alignment <= 0.6f ? "Kind" : "Benevolent";
            _alignTip = "Alignment: " + mood + " (" + HudText.Signed(_alignment) + ")\nBlessings move you toward grace, wrath toward fury.";

            int living = st.LivingPopulation;
            int civs = 0, towns = 0;
            var cs = st.Civilizations.Items;
            for (int i = 0; i < cs.Count; i++) if (cs[i] != null && cs[i].IsAlive) civs++;
            var ss = st.Settlements.Items;
            for (int i = 0; i < ss.Count; i++) if (ss[i] != null && ss[i].IsAlive) towns++;
            _pop.text = HudText.Compact(living);
            _popTip = HudText.Int(living) + " living people\n" + civs + " civilizations, " + towns + " settlements\nPeak: " + HudText.Int(st.Stats.PeakPopulation) +
                      "\nBirths " + st.Stats.TotalBirths + ", deaths " + st.Stats.TotalDeaths;
        }

        /// <summary>Draws the bar across the top; returns its rect.</summary>
        public Rect Draw(float screenW)
        {
            var sk = _hud.Skin;
            float h = sk.U(46f);
            var bar = new Rect(-sk.U(8f), -sk.U(8f), screenW + sk.U(16f), h + sk.U(8f)); // rounded corners off-screen
            HudDraw.Paint(bar, sk.TopBar);
            var hit = new Rect(0f, 0f, screenW, h);
            _hud.AddHitRect(hit);
            LastRect = hit;

            float pad = sk.Pad;
            float bh = sk.U(28f);
            float by = (h - bh) * 0.5f;
            float x = pad;

            if (GUI.Button(new Rect(x, by, sk.U(84f), bh), _menu, sk.Button)) _hud.OpenMenu();
            _hud.Tooltip(new Rect(x, by, sk.U(84f), bh), _menu.tooltip);
            x += sk.U(84f) + pad * 1.5f;

            var st = _hud.State;
            if (st == null)
            {
                HudDraw.Label(new Rect(x, 0f, sk.U(90f), h), _title, sk.Title);
                x += sk.U(90f);
                HudDraw.Label(new Rect(x, 0f, sk.U(260f), h), _noWorld, sk.LabelDim);
                x += sk.U(260f);
                if (_hud.Runner != null && GUI.Button(new Rect(x, by, sk.U(120f), bh), _newWorld, sk.Button)) _hud.OpenNewWorld();
                return hit;
            }

            // Date & age
            float dateW = sk.U(210f);
            HudDraw.Label(new Rect(x, sk.U(5f), dateW, sk.U(20f)), _date, sk.LabelBold);
            HudDraw.Label(new Rect(x, sk.U(24f), dateW, sk.U(16f)), _age, sk.LabelDim, HudSkin.GoldDim);
            _hud.Tooltip(new Rect(x, 0f, dateW, h), _dateTip);
            x += dateW + pad;

            // Right cluster (laid out right-to-left)
            float rx = screenW - pad;
            var levels = GodSimRunner.SpeedLevels;
            float sbw = sk.U(40f);
            var runner = _hud.Runner;
            for (int i = _speedButtons.Length - 1; i >= 0; i--)
            {
                rx -= sbw;
                var r = new Rect(rx, by, sbw, bh);
                bool on = !_paused && i == _speedIndex;
                if (GUI.Toggle(r, on, _speedButtons[i], sk.ButtonSmall) != on && runner != null) _hud.SetSpeed(i);
                _hud.Tooltip(r, _speedButtons[i].tooltip);
                rx -= sk.U(2f);
            }
            rx -= sk.U(2f) + sbw;
            var pr = new Rect(rx, by, sbw, bh);
            if (runner != null && GUI.Toggle(pr, _paused, _paused ? _play : _pause, sk.Button) != _paused) _hud.TogglePause();
            _hud.Tooltip(pr, _paused ? _play.tooltip : _pause.tooltip);
            float spw = sk.U(78f);
            rx -= spw + sk.Gap;
            HudDraw.Label(new Rect(rx, 0f, spw, h), _speed, sk.LabelRightSmall, _paused ? HudSkin.Warn : HudSkin.TextDim);

            float popW = sk.U(92f);
            rx -= popW + pad * 2f;
            HudDraw.Label(new Rect(rx, sk.U(6f), popW, sk.U(12f)), _popCaption, sk.LabelTiny);
            HudDraw.Label(new Rect(rx, sk.U(18f), popW, sk.U(22f)), _pop, sk.Title, HudSkin.Text);
            _hud.Tooltip(new Rect(rx, 0f, popW, h), _popTip);

            // Center: mana + alignment, centered in the free space
            float manaW = sk.U(210f), alignW = sk.U(150f);
            float centerW = manaW + pad * 2f + alignW;
            float free = rx - pad - x;
            float cx = x + Mathf.Max(0f, (free - centerW) * 0.5f);
            if (free >= manaW)
            {
                HudDraw.Label(new Rect(cx, sk.U(4f), manaW, sk.U(12f)), _manaCaption, sk.LabelTiny, HudSkin.ManaBlue);
                HudDraw.Label(new Rect(cx, sk.U(4f), manaW, sk.U(12f)), _regen, sk.LabelRightSmall, HudSkin.ManaBlue);
                var mr = new Rect(cx, sk.U(19f), manaW, sk.U(18f));
                HudDraw.Bar(sk, mr, _mana01, HudSkin.ManaBlue, _mana);
                _hud.Tooltip(new Rect(cx, 0f, manaW, h), _manaTip);
                cx += manaW + pad * 2f;
            }
            if (free >= centerW)
            {
                HudDraw.Label(new Rect(cx, sk.U(4f), alignW, sk.U(12f)), _wrath, sk.LabelTiny, HudSkin.Bad);
                var gr = new Rect(cx, sk.U(4f), alignW, sk.U(12f));
                HudDraw.Label(gr, _grace, sk.LabelRightSmall, HudSkin.Gold);
                var ar = new Rect(cx, sk.U(22f), alignW, sk.U(12f));
                DrawAlignment(sk, ar);
                _hud.Tooltip(new Rect(cx, 0f, alignW, h), _alignTip);
            }
            return hit;
        }

        private void DrawAlignment(HudSkin sk, Rect r)
        {
            if (!HudDraw.IsRepaint) return;
            sk.BarBack.Draw(r, false, false, false, false);
            const int segments = 10;
            float sw = r.width / segments;
            for (int i = 0; i < segments; i++)
            {
                float t = (i + 0.5f) / segments; // 0 wrath .. 1 grace
                Color c = t < 0.5f ? Color.Lerp(HudSkin.Bad, new Color(0.5f, 0.5f, 0.55f, 1f), t * 2f) : Color.Lerp(new Color(0.5f, 0.5f, 0.55f, 1f), HudSkin.Gold, (t - 0.5f) * 2f);
                c.a = 0.55f;
                HudDraw.Paint(new Rect(r.x + i * sw + 1f, r.y + 2f, sw - 1f, r.height - 4f), sk.White, c);
            }
            float mx = r.x + (_alignment + 1f) * 0.5f * r.width;
            float mw = Mathf.Max(3f, sk.U(4f));
            HudDraw.Paint(new Rect(mx - mw * 0.5f, r.y - sk.U(3f), mw, r.height + sk.U(6f)), sk.BarFill, Color.white);
        }
    }
}
