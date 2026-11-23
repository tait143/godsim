using System.Collections.Generic;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>F1 help overlay and F3 performance overlay (engine step time, per-system profiles, FPS, entity counts).</summary>
    internal sealed class HudOverlays
    {
        private sealed class ProfileRow
        {
            public readonly GUIContent Name = new GUIContent();
            public readonly GUIContent Avg = new GUIContent();
            public readonly GUIContent Last = new GUIContent();
            public readonly GUIContent Faults = new GUIContent();
            public bool Disabled;
            public bool Slow;
            public string Tip;
        }

        private static readonly string[,] HelpLines =
        {
            { "Left click", "Select a settlement, building, army or tile / cast the selected power" },
            { "Right click / Esc", "Cancel the selected power" },
            { "1 - 9, 0", "Select god powers 1-10" },
            { "Shift + 1 - 9", "Select god powers 11-19" },
            { "Space", "Pause / resume" },
            { "+ / -", "Faster / slower simulation" },
            { "Esc", "Menu: new world, save, load, settings, quit" },
            { "F1", "Toggle this help" },
            { "F3", "Toggle the performance overlay" },
            { "Minimap", "Click or drag to move the camera" },
            { "Civilizations", "Click a row to focus its capital" },
            { "Chronicle", "Click an entry to jump to where it happened" },
            { "Camera", "See the camera controls of your build (pan / rotate / zoom)" },
        };

        private readonly GodSimHUD _hud;
        private readonly GUIContent _helpTitle = new GUIContent("Controls");
        private readonly GUIContent _helpFoot = new GUIContent("Press F1 or Esc to close");
        private readonly GUIContent[] _helpKeys;
        private readonly GUIContent[] _helpText;

        private readonly GUIContent _perfTitle = new GUIContent("Performance");
        private readonly GUIContent _summary = new GUIContent();
        private readonly GUIContent _entities = new GUIContent();
        private readonly GUIContent[] _cols = { new GUIContent("System"), new GUIContent("avg ms"), new GUIContent("last ms"), new GUIContent("faults") };
        private readonly List<ProfileRow> _profiles = new List<ProfileRow>(16);
        private int _profileCount;

        public HudOverlays(GodSimHUD hud)
        {
            _hud = hud;
            int n = HelpLines.GetLength(0);
            _helpKeys = new GUIContent[n];
            _helpText = new GUIContent[n];
            for (int i = 0; i < n; i++) { _helpKeys[i] = new GUIContent(HelpLines[i, 0]); _helpText[i] = new GUIContent(HelpLines[i, 1]); }
        }

        public void OnSimulationChanged() { _profileCount = 0; }

        public void Refresh()
        {
            var sim = _hud.Sim;
            var st = _hud.State;
            _profileCount = 0;
            double step = 0.0;
            if (sim != null && sim.Engine != null)
            {
                var engine = sim.Engine;
                step = engine.LastStepMs;
                var ps = engine.Profiles;
                for (int i = 0; i < ps.Count; i++)
                {
                    var p = ps[i];
                    if (p == null) continue;
                    if (_profileCount == _profiles.Count) _profiles.Add(new ProfileRow());
                    var r = _profiles[_profileCount++];
                    r.Name.text = p.Name ?? "?";
                    r.Avg.text = HudText.Ms(p.AvgMs);
                    r.Last.text = HudText.Ms(p.LastMs);
                    r.Faults.text = p.Disabled ? "OFF (" + p.TotalFaults + ")" : HudText.Int(p.TotalFaults);
                    r.Disabled = p.Disabled;
                    r.Slow = p.AvgMs > 1.0;
                    r.Tip = string.IsNullOrEmpty(p.LastError) ? p.Name + ": " + p.Runs + " runs" : p.Name + " last error:\n" + p.LastError;
                }
            }
            var runner = _hud.Runner;
            float tps = runner != null ? runner.TicksPerSecond : 0f;
            long gc = System.GC.GetTotalMemory(false);
            _summary.text = "FPS " + HudText.F0(_hud.Fps) + " (" + HudText.F1(_hud.FrameMs) + " ms)   Step " + HudText.Ms(step) + " ms   " +
                            HudText.F1(tps) + " ticks/s   Managed " + HudText.Bytes(gc);
            if (st != null)
            {
                int armies = st.Diplomacy.Armies.Count, wars = 0;
                for (int i = 0; i < st.Diplomacy.Wars.Count; i++) if (st.Diplomacy.Wars[i] != null && st.Diplomacy.Wars[i].IsActive) wars++;
                var bus = sim != null ? sim.Events : null;
                _entities.text = "People " + HudText.Int(st.LivingPopulation) + " / " + HudText.Int(st.People.Count) + " stored   Buildings " + HudText.Int(st.Buildings.Count) +
                                 "   Settlements " + HudText.Int(st.Settlements.Count) + "   Civs " + HudText.Int(st.Civilizations.Count) +
                                 "\nArmies " + armies + "   Active wars " + wars + "   Chronicle " + st.Chronicle.Entries.Count +
                                 (bus != null ? "   Events " + bus.PublishedCount + " (handler faults " + bus.HandlerFaults + ")" : "") +
                                 "   Tick " + st.Tick;
            }
            else _entities.text = "No simulation.";
        }

        public void DrawPerf(float x, float y, float w)
        {
            var sk = _hud.Skin;
            float pad = sk.Pad, lh = sk.U(16f);
            float h = pad * 2f + sk.U(24f) + lh * 3.4f + sk.Gap + lh * (_profileCount + 1);
            w = Mathf.Min(w, Screen.width - x - sk.U(8f));
            if (w < sk.U(200f)) return;
            var r = new Rect(x, y, w, h);
            HudDraw.Paint(r, sk.Panel);
            _hud.AddHitRect(r);
            float cx = x + pad, cw = w - pad * 2f, cy = y + pad;
            HudDraw.Label(new Rect(cx, cy, cw, sk.U(22f)), _perfTitle, sk.Title);
            cy += sk.U(24f);
            HudDraw.Label(new Rect(cx, cy, cw, lh), _summary, sk.LabelSmall);
            cy += lh + sk.U(2f);
            HudDraw.Label(new Rect(cx, cy, cw, lh * 2.2f), _entities, sk.LabelWrapSmall, HudSkin.TextDim);
            cy += lh * 2.4f + sk.Gap;

            float c0 = cw * 0.46f, c1 = cw * 0.17f, c2 = cw * 0.17f, c3 = cw * 0.20f;
            HudDraw.Label(new Rect(cx, cy, c0, lh), _cols[0], sk.Section);
            HudDraw.Label(new Rect(cx + c0, cy, c1, lh), _cols[1], sk.LabelRightSmall, HudSkin.GoldDim);
            HudDraw.Label(new Rect(cx + c0 + c1, cy, c2, lh), _cols[2], sk.LabelRightSmall, HudSkin.GoldDim);
            HudDraw.Label(new Rect(cx + c0 + c1 + c2, cy, c3, lh), _cols[3], sk.LabelRightSmall, HudSkin.GoldDim);
            cy += lh;
            for (int i = 0; i < _profileCount; i++)
            {
                var p = _profiles[i];
                Color c = p.Disabled ? HudSkin.Bad : p.Slow ? HudSkin.Warn : HudSkin.Text;
                var rowRect = new Rect(cx, cy, cw, lh);
                if ((i & 1) == 1) HudDraw.Paint(rowRect, sk.White, new Color(1f, 1f, 1f, 0.035f));
                HudDraw.Label(new Rect(cx, cy, c0, lh), p.Name, sk.LabelSmall, c);
                HudDraw.Label(new Rect(cx + c0, cy, c1, lh), p.Avg, sk.LabelRightSmall, c);
                HudDraw.Label(new Rect(cx + c0 + c1, cy, c2, lh), p.Last, sk.LabelRightSmall, HudSkin.TextDim);
                HudDraw.Label(new Rect(cx + c0 + c1 + c2, cy, c3, lh), p.Faults, sk.LabelRightSmall, p.Disabled ? HudSkin.Bad : HudSkin.TextDim);
                _hud.Tooltip(rowRect, p.Tip);
                cy += lh;
            }
        }

        public void DrawHelp(float W, float H)
        {
            var sk = _hud.Skin;
            int n = _helpKeys.Length;
            float lh = sk.U(21f);
            float w = Mathf.Min(sk.U(560f), W - sk.U(16f));
            float h = Mathf.Min(sk.U(70f) + n * lh + sk.U(30f), H - sk.U(16f));
            var r = new Rect(Mathf.Round((W - w) * 0.5f), Mathf.Round((H - h) * 0.5f), w, h);
            HudDraw.Paint(r, sk.Modal);
            _hud.AddHitRect(r);
            float x = r.x + sk.U(20f), cw = w - sk.U(40f), y = r.y + sk.U(14f);
            HudDraw.Label(new Rect(x, y, cw, sk.U(28f)), _helpTitle, sk.Title);
            y += sk.U(36f);
            float kw = cw * 0.32f;
            for (int i = 0; i < n && y + lh <= r.yMax - sk.U(24f); i++)
            {
                HudDraw.Label(new Rect(x, y, kw, lh), _helpKeys[i], sk.LabelBold, HudSkin.Gold);
                HudDraw.Label(new Rect(x + kw, y, cw - kw, lh), _helpText[i], sk.LabelSmall);
                y += lh;
            }
            HudDraw.Label(new Rect(r.x, r.yMax - sk.U(24f), r.width, sk.U(18f)), _helpFoot, sk.LabelCenterSmall, HudSkin.TextDim);
        }
    }
}
