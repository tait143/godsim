using System.Collections.Generic;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>Collapsible civilizations list (one row per living civ) with the population-history chart.</summary>
    internal sealed class HudCivPanel
    {
        private sealed class Row
        {
            public int CivId, CapitalId;
            public int Population;
            public Color Color;
            public bool AtWar;
            public float Progress, Devotion, Favor;
            public readonly GUIContent Name = new GUIContent();
            public readonly GUIContent Age = new GUIContent();
            public readonly GUIContent Stats = new GUIContent();
            public readonly GUIContent ProgressText = new GUIContent();
            public string Tip, ProgressTip, DevotionTip, FavorTip, WarTip;
        }

        private const string AgeProgressKey = "prog.ageProgress";

        private readonly GodSimHUD _hud;
        private readonly List<Row> _rows = new List<Row>(8);
        private int _count;
        private Vector2 _scroll;

        private readonly GUIContent _header = new GUIContent("Civilizations");
        private readonly GUIContent _expand = new GUIContent("►", "Expand");
        private readonly GUIContent _collapse = new GUIContent("▼", "Collapse");
        private readonly GUIContent _chart = new GUIContent("Chart", "Population history (one sample per year, last 300 years)");
        private readonly GUIContent _war = new GUIContent("WAR");
        private readonly GUIContent _dev = new GUIContent("Devotion");
        private readonly GUIContent _fav = new GUIContent("Favor");
        private readonly GUIContent _chartMax = new GUIContent();
        private readonly GUIContent _chartRange = new GUIContent();
        private readonly GUIContent _chartEmpty = new GUIContent("Population history appears after the first year.");
        private readonly GUIContent _none = new GUIContent("No living civilizations.");

        public bool Collapsed;
        public Rect LastRect { get; private set; }

        public HudCivPanel(GodSimHUD hud) { _hud = hud; }

        public void OnSimulationChanged() { _count = 0; _scroll = Vector2.zero; }

        public void Refresh()
        {
            var st = _hud.State;
            var db = _hud.Db;
            _count = 0;
            if (st == null) return;
            var civs = st.Civilizations.Items;
            int alive = 0;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c == null || !c.IsAlive) continue;
                alive++;
                if (Collapsed) continue;
                if (_count == _rows.Count) _rows.Add(new Row());
                FillRow(_rows[_count++], c, st, db);
            }
            _header.text = "Civilizations (" + alive + ")";
            if (Collapsed) return;

            // Sort by population, descending (insertion sort: tiny list, no allocations).
            for (int i = 1; i < _count; i++)
            {
                var r = _rows[i];
                int j = i - 1;
                while (j >= 0 && _rows[j].Population < r.Population) { _rows[j + 1] = _rows[j]; j--; }
                _rows[j + 1] = r;
            }

            var h = _hud.History;
            if (h.Count > 0)
            {
                _chartMax.text = HudText.Compact(_hud.Chart.MaxValue);
                _chartRange.text = "Y" + HudText.Int(h.YearAt(0)) + " – Y" + HudText.Int(h.YearAt(h.Count - 1));
            }
        }

        private void FillRow(Row r, Civilization c, WorldState st, GameDatabase db)
        {
            r.CivId = c.Id;
            r.CapitalId = c.CapitalId;
            r.Color = HudDraw.FromRgba(c.Color);
            r.Color.a = 1f;

            int pop = 0, towns = 0;
            for (int k = 0; k < c.SettlementIds.Count; k++)
            {
                var s = st.Settlements.Get(c.SettlementIds[k]);
                if (s == null || !s.IsAlive) continue;
                towns++;
                pop += s.Population;
            }
            if (pop == 0 && c.Population > 0) pop = c.Population;
            r.Population = pop;

            r.Name.text = string.IsNullOrEmpty(c.Name) ? "Civilization " + c.Id : c.Name;
            r.Age.text = _hud.AgeName(c.Age);
            r.Stats.text = _hud.RaceName(c.Race) + "  ·  " + HudText.Compact(pop) + " pop  ·  " + towns + (towns == 1 ? " town" : " towns") + "  ·  " + c.KnownTechs.Count + " techs";

            // Research progress toward the next age.
            float prog;
            string progTip;
            if (c.Memory.TryGetValue(AgeProgressKey, out float p))
            {
                prog = MathUtil.Clamp01(MathUtil.Sanitize(p));
                progTip = "Progress toward the next age: " + HudText.Pct(prog);
            }
            else if ((int)c.Age >= Counts.Ages - 1)
            {
                prog = 1f;
                progTip = "Final age reached.";
            }
            else
            {
                float cur = 0f, next = 1f;
                if (db != null)
                {
                    var a0 = db.Ages[(int)c.Age];
                    var a1 = db.Ages[(int)c.Age + 1];
                    if (a0 != null) cur = a0.ResearchThreshold;
                    if (a1 != null) next = a1.ResearchThreshold;
                }
                prog = next > cur ? MathUtil.Clamp01((c.TotalResearch - cur) / (next - cur)) : 1f;
                progTip = "Research toward the " + _hud.AgeName(c.Age + 1) + ": " + HudText.F0(c.TotalResearch) + " / " + HudText.F0(next);
            }
            r.Progress = prog;
            r.ProgressText.text = (int)c.Age >= Counts.Ages - 1 ? "Max" : HudText.Pct(prog);
            r.ProgressTip = progTip;

            r.Devotion = MathUtil.Clamp01(MathUtil.Sanitize(c.Devotion));
            r.Favor = MathUtil.Clamp(MathUtil.Sanitize(c.GodFavor), -1f, 1f);
            r.DevotionTip = "Devotion " + HudText.Pct(r.Devotion) + ": how strongly they worship you (drives mana).";
            r.FavorTip = "Favor " + HudText.Signed(r.Favor) + ": " + (r.Favor < -0.3f ? "they feel smitten." : r.Favor > 0.3f ? "they feel blessed." : "indifferent.");

            // Wars
            r.AtWar = false;
            r.WarTip = null;
            var wars = st.Diplomacy.Wars;
            var sb = HudText.Begin();
            for (int w = 0; w < wars.Count; w++)
            {
                var war = wars[w];
                if (war == null || !war.IsActive) continue;
                int enemy = war.AttackerId == c.Id ? war.DefenderId : war.DefenderId == c.Id ? war.AttackerId : -1;
                if (enemy < 0) continue;
                sb.Append(r.AtWar ? ", " : "At war with ").Append(_hud.CivName(enemy));
                r.AtWar = true;
            }
            if (r.AtWar) r.WarTip = HudText.End();

            r.Tip = r.Name.text + " (" + _hud.RaceName(c.Race) + ")\n" + _hud.AgeName(c.Age) + " since year " + SimTime.Year(c.AgeEnteredTick) +
                    "\nMilitary power " + HudText.F0(c.MilitaryPower) + "\nClick to focus the capital.";
        }

        public void Draw(float x, float y, float w, float maxH)
        {
            var sk = _hud.Skin;
            float pad = sk.Pad;
            float headH = sk.U(26f);
            float rowH = sk.U(56f);
            float chartH = _hud.ShowChart && !Collapsed ? sk.U(118f) : 0f;
            float listH = Collapsed ? 0f : Mathf.Max(rowH, _count * rowH);
            float wanted = pad * 2f + headH + (Collapsed ? 0f : sk.Gap + chartH + listH);
            float h = Mathf.Max(pad * 2f + headH, Mathf.Min(wanted, maxH));
            var panel = new Rect(x, y, w, h);
            if (maxH < pad * 2f + headH) { LastRect = new Rect(x, y, 0f, 0f); return; }
            HudDraw.Paint(panel, sk.Panel);
            _hud.AddHitRect(panel);
            LastRect = panel;

            float cx = x + pad, cw = w - pad * 2f, cy = y + pad;
            var tr = new Rect(cx, cy, cw, headH);
            if (GUI.Button(new Rect(cx, cy, sk.U(22f), headH), Collapsed ? _expand : _collapse, sk.Row))
            {
                Collapsed = !Collapsed;
                _hud.MarkDirty();
            }
            HudDraw.Label(new Rect(cx + sk.U(22f), cy, cw - sk.U(90f), headH), _header, sk.Title);
            var chartBtn = new Rect(cx + cw - sk.U(62f), cy + sk.U(2f), sk.U(62f), headH - sk.U(4f));
            if (!Collapsed)
            {
                bool nv = GUI.Toggle(chartBtn, _hud.ShowChart, _chart, sk.ButtonSmall);
                if (nv != _hud.ShowChart) { _hud.ShowChart = nv; _hud.MarkDirty(); }
                _hud.Tooltip(chartBtn, _chart.tooltip);
            }
            if (Collapsed) return;
            cy += headH + sk.Gap;

            if (chartH > 0f)
            {
                var cr = new Rect(cx, cy, cw, chartH - sk.Gap);
                DrawChart(sk, cr);
                cy += chartH;
            }

            float viewH = y + h - pad - cy;
            if (viewH <= 0f) return;
            if (_count == 0) { HudDraw.Label(new Rect(cx, cy, cw, sk.LineH), _none, sk.LabelDim); return; }

            float contentH = _count * rowH;
            bool scroll = contentH > viewH + 0.5f;
            float rw = scroll ? cw - sk.ScrollbarW - sk.U(3f) : cw;
            var view = new Rect(cx, cy, cw, viewH);
            if (scroll) _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0f, 0f, rw, contentH), false, false, sk.ScrollH, sk.ScrollV);
            float ox = scroll ? 0f : cx, oy = scroll ? 0f : cy;
            float visTop = scroll ? _scroll.y : 0f, visBottom = visTop + viewH;
            for (int i = 0; i < _count; i++)
            {
                float ry = oy + i * rowH;
                if (scroll && (i * rowH + rowH < visTop || i * rowH > visBottom)) continue;
                DrawRow(sk, _rows[i], new Rect(ox, ry, rw, rowH - sk.U(3f)));
            }
            if (scroll) GUI.EndScrollView();
        }

        private void DrawRow(HudSkin sk, Row r, Rect rect)
        {
            if (GUI.Button(rect, GUIContent.none, sk.Row)) _hud.FocusSettlement(r.CapitalId);
            _hud.Tooltip(rect, r.Tip);

            float x = rect.x + sk.U(6f), w = rect.width - sk.U(12f), y = rect.y + sk.U(3f);
            float sw = sk.U(11f);
            HudDraw.Swatch(sk, new Rect(x, y + sk.U(4f), sw, sw), r.Color);
            float ageW = sk.U(104f);
            HudDraw.Label(new Rect(x + sw + sk.Gap * 1.5f, y, w - sw - ageW - sk.Gap * 2f, sk.U(19f)), r.Name, sk.LabelBold);
            HudDraw.Label(new Rect(x + w - ageW, y, ageW, sk.U(19f)), r.Age, sk.LabelRightSmall, HudSkin.Gold);
            y += sk.U(19f);

            float warW = r.AtWar ? sk.U(34f) : 0f;
            HudDraw.Label(new Rect(x, y, w - warW - sk.Gap, sk.U(15f)), r.Stats, sk.LabelDim);
            if (r.AtWar)
            {
                var wr = new Rect(x + w - warW, y + sk.U(1f), warW, sk.U(13f));
                HudDraw.Paint(wr, sk.BadgeBg, HudSkin.Bad);
                HudDraw.Label(wr, _war, sk.Badge);
                _hud.Tooltip(wr, r.WarTip);
            }
            y += sk.U(17f);

            float bh = sk.U(10f);
            float gw = sk.Gap * 2f;
            float pw = (w - gw * 2f) * 0.5f, dw = (w - gw * 2f) * 0.25f;
            var pr = new Rect(x, y, pw, bh);
            HudDraw.Bar(sk, pr, r.Progress, HudSkin.Research);
            HudDraw.Label(new Rect(pr.x, pr.y - sk.U(2f), pr.width, bh + sk.U(4f)), r.ProgressText, sk.LabelCenterSmall);
            _hud.Tooltip(pr, r.ProgressTip);
            var dr = new Rect(x + pw + gw, y, dw, bh);
            HudDraw.Bar(sk, dr, r.Devotion, HudSkin.Faith);
            _hud.Tooltip(dr, r.DevotionTip);
            var fr = new Rect(dr.xMax + gw, y, dw, bh);
            HudDraw.SignedBar(sk, fr, r.Favor, HudSkin.Bad, HudSkin.Gold);
            _hud.Tooltip(fr, r.FavorTip);
        }

        private void DrawChart(HudSkin sk, Rect r)
        {
            HudDraw.Paint(r, sk.BarBack);
            var h = _hud.History;
            var tex = _hud.Chart.Texture;
            if (h.Count < 2 || tex == null)
            {
                HudDraw.Label(r, _chartEmpty, sk.LabelCenterSmall, HudSkin.TextDim);
                return;
            }
            var inner = new Rect(r.x + sk.U(3f), r.y + sk.U(14f), r.width - sk.U(6f), r.height - sk.U(28f));
            if (HudDraw.IsRepaint) GUI.DrawTexture(inner, tex, ScaleMode.StretchToFill, true);
            HudDraw.Label(new Rect(r.x + sk.U(5f), r.y + sk.U(1f), r.width * 0.5f, sk.U(13f)), _chartMax, sk.LabelTiny);
            HudDraw.Label(new Rect(r.x, r.yMax - sk.U(14f), r.width - sk.U(5f), sk.U(13f)), _chartRange, sk.LabelRightSmall, HudSkin.TextDim);
        }
    }
}
