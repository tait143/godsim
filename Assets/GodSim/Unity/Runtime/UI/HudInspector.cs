using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Right-side inspector for the current selection with tabs for every selected layer
    /// (settlement / building / army / tile). Rows are rebuilt by the throttled refresh.
    /// </summary>
    internal sealed class HudInspector
    {
        private enum Tab { Settlement = 0, Building = 1, Army = 2, Tile = 3 }

        private static readonly int CategoryCount = Enum.GetValues(typeof(BuildingCategory)).Length;
        private static readonly string[] CategoryNames = Enum.GetNames(typeof(BuildingCategory));
        private static readonly string[] ResourceNames = Enum.GetNames(typeof(ResourceType));
        private static readonly string[] ServiceNames = Enum.GetNames(typeof(ServiceType));
        private static readonly string[] JobNames = BuildSpaced(Enum.GetNames(typeof(JobType)));
        private static readonly string[] BiomeNames = BuildSpaced(Enum.GetNames(typeof(Biome)));

        private readonly GodSimHUD _hud;
        private readonly HudRowList _rows = new HudRowList();
        private readonly bool[] _available = new bool[4];
        private readonly GUIContent[] _tabs = { new GUIContent("Town"), new GUIContent("Building"), new GUIContent("Army"), new GUIContent("Tile") };
        private readonly GUIContent _close = new GUIContent("×", "Clear selection");
        private readonly int[] _catCounts = new int[CategoryCount];
        private readonly int[] _catBuilding = new int[CategoryCount];
        private readonly int[] _typeCounts = new int[Counts.Buildings];
        private Tab _tab = Tab.Tile;
        private int _availableCount;
        private Vector2 _scroll;

        public Rect LastRect { get; private set; }

        public HudInspector(GodSimHUD hud) { _hud = hud; }

        private static string[] BuildSpaced(string[] names)
        {
            var a = new string[names.Length];
            for (int i = 0; i < names.Length; i++) a[i] = HudText.Spaced(names[i]);
            return a;
        }

        public void OnSimulationChanged() { _rows.Clear(); _scroll = Vector2.zero; }
        public void OnSkinChanged() { _rows.InvalidateMeasure(); }

        public void OnSelectionChanged()
        {
            ComputeAvailable();
            if (_available[(int)Tab.Building]) _tab = Tab.Building;
            else if (_available[(int)Tab.Army]) _tab = Tab.Army;
            else if (_available[(int)Tab.Settlement]) _tab = Tab.Settlement;
            else _tab = Tab.Tile;
            _scroll = Vector2.zero;
        }

        // ------------------------------------------------------------------ selection resolution
        private Settlement SelSettlement(WorldState st, SelectionController sel)
        {
            var s = st.Settlements.Get(sel.SelectedSettlementId);
            if (s != null && s.IsAlive) return s;
            var b = SelBuilding(st, sel);
            if (b != null) { s = st.Settlements.Get(b.SettlementId); if (s != null && s.IsAlive) return s; }
            var a = SelArmy(st, sel);
            if (a != null) { s = st.Settlements.Get(a.HomeSettlementId); if (s != null && s.IsAlive) return s; }
            return null;
        }

        private static Building SelBuilding(WorldState st, SelectionController sel)
        {
            var b = st.Buildings.Get(sel.SelectedBuildingId);
            return b != null && !b.IsDestroyed ? b : null;
        }

        private static Army SelArmy(WorldState st, SelectionController sel)
        {
            var a = st.Diplomacy.Armies.Get(sel.SelectedArmyId);
            return a != null && a.IsAlive ? a : null;
        }

        private void ComputeAvailable()
        {
            for (int i = 0; i < _available.Length; i++) _available[i] = false;
            _availableCount = 0;
            var st = _hud.State;
            var sel = _hud.Selection;
            if (st == null || sel == null || !sel.HasSelection) return;
            _available[(int)Tab.Building] = SelBuilding(st, sel) != null;
            _available[(int)Tab.Army] = SelArmy(st, sel) != null;
            _available[(int)Tab.Settlement] = SelSettlement(st, sel) != null;
            _available[(int)Tab.Tile] = st.Map != null && st.Map.InBounds(sel.SelectedTile);
            for (int i = 0; i < _available.Length; i++) if (_available[i]) _availableCount++;
            if (_availableCount > 0 && !_available[(int)_tab])
                for (int i = 0; i < _available.Length; i++) if (_available[i]) { _tab = (Tab)i; break; }
        }

        // ------------------------------------------------------------------ refresh
        public void Refresh()
        {
            _rows.Clear();
            ComputeAvailable();
            var st = _hud.State;
            var sel = _hud.Selection;
            if (st == null || sel == null || _availableCount == 0) return;
            switch (_tab)
            {
                case Tab.Settlement: BuildSettlement(st, SelSettlement(st, sel)); break;
                case Tab.Building: BuildBuilding(st, SelBuilding(st, sel)); break;
                case Tab.Army: BuildArmy(st, SelArmy(st, sel)); break;
                default: BuildTile(st, sel.SelectedTile); break;
            }
        }

        private static Color Level(float v, bool inverted = false)
        {
            if (inverted) v = 1f - v;
            return v < 0.35f ? HudSkin.Bad : v < 0.65f ? HudSkin.Warn : HudSkin.Good;
        }

        private Color CivColor(WorldState st, int civId)
        {
            var c = st.Civilizations.Get(civId);
            if (c == null) return HudSkin.TextDim;
            var col = HudDraw.FromRgba(c.Color);
            col.a = 1f;
            return col;
        }

        private void BuildSettlement(WorldState st, Settlement s)
        {
            if (s == null) return;
            var db = _hud.Db;
            var civ = st.Civilizations.Get(s.CivilizationId);
            _rows.Header(s.Name, s.Tier + (s.IsCapital ? " · Capital" : ""), CivColor(st, s.CivilizationId), true);
            _rows.KV("Civilization", civ != null ? civ.Name : "None", CivColor(st, s.CivilizationId));
            _rows.KV("Race", _hud.RaceName(s.Race));
            _rows.KV("Founded", "Year " + HudText.Int(SimTime.Year(s.FoundedTick)));

            _rows.Section("PEOPLE");
            _rows.KV("Population", HudText.Int(s.Population));
            _rows.KV("Homeless", HudText.Int(s.Homeless), s.Homeless > 0 ? HudSkin.Warn : HudSkin.Text);
            _rows.KV("Unemployed", HudText.Int(s.Unemployed), s.Unemployed > 0 ? HudSkin.Warn : HudSkin.Text);
            _rows.KV("Soldiers", HudText.Int(s.Soldiers));
            if (s.DesiredBuilders > 0) _rows.KV("Builders wanted", HudText.Int(s.DesiredBuilders));
            _rows.KV("Defense", HudText.F0(s.DefenseStrength));

            _rows.Section("WELLBEING");
            _rows.Bar("Happiness", s.Happiness, Level(s.Happiness), HudText.Pct(s.Happiness));
            _rows.Bar("Health", s.Health, HudSkin.Health, HudText.Pct(s.Health));
            _rows.Bar("Faith", s.Faith, HudSkin.Faith, HudText.Pct(s.Faith));
            _rows.Bar("Unrest", s.Unrest, Level(MathUtil.Clamp01(s.Unrest), true), HudText.Pct(s.Unrest));

            var sp = s.Stockpile;
            float cap = Mathf.Max(1f, MathUtil.Sanitize(sp.Capacity, 1f));
            _rows.Section("STOCKPILE  (capacity " + HudText.F0(cap) + " each)");
            int shown = 0;
            for (int i = 0; i < Counts.Resources; i++)
            {
                var t = (ResourceType)i;
                float amt = MathUtil.Sanitize(sp.Get(t));
                float net = MathUtil.Sanitize(sp.Net(t));
                if (amt < 0.05f && Math.Abs(net) < 0.001f) continue;
                string arrow; Color col;
                if (net > 0.005f) { arrow = "▲ " + HudText.Signed(net); col = HudSkin.Good; }
                else if (net < -0.005f) { arrow = "▼ " + HudText.Signed(net); col = HudSkin.Bad; }
                else { arrow = "–"; col = HudSkin.TextDim; }
                var row = _rows.Stock(ResourceNames[i], HudText.F0(amt) + " / " + HudText.F0(cap), arrow, col, amt / cap);
                row.Tip = ResourceNames[i] + ": income " + HudText.F2(sp.AvgIncome[i]) + "/day, expense " + HudText.F2(sp.AvgExpense[i]) + "/day";
                shown++;
            }
            if (shown == 0) _rows.Text("Empty.", HudSkin.TextDim);

            // Buildings by category
            Array.Clear(_catCounts, 0, _catCounts.Length);
            Array.Clear(_catBuilding, 0, _catBuilding.Length);
            Array.Clear(_typeCounts, 0, _typeCounts.Length);
            int total = 0;
            for (int i = 0; i < s.BuildingIds.Count; i++)
            {
                var b = st.Buildings.Get(s.BuildingIds[i]);
                if (b == null || b.IsDestroyed) continue;
                int cat = db != null && db.TryGetBuilding(b.Type, out var bd) ? (int)bd.Category : 0;
                if (cat < 0 || cat >= CategoryCount) cat = 0;
                total++;
                _catCounts[cat]++;
                if (!b.IsComplete) _catBuilding[cat]++;
                int ti = (int)b.Type;
                if (ti >= 0 && ti < _typeCounts.Length) _typeCounts[ti]++;
            }
            _rows.Section("BUILDINGS  (" + total + ")");
            for (int c = 0; c < CategoryCount; c++)
            {
                if (_catCounts[c] == 0) continue;
                _rows.Pair(CategoryNames[c], _catBuilding[c] > 0 ? _catCounts[c] + "  (" + _catBuilding[c] + " under construction)" : HudText.Int(_catCounts[c]));
                if (db == null) continue;
                var sb = HudText.Begin();
                for (int t = 0; t < _typeCounts.Length; t++)
                {
                    if (_typeCounts[t] == 0 || !db.TryGetBuilding((BuildingType)t, out var d) || (int)d.Category != c) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(d.Name);
                    if (_typeCounts[t] > 1) sb.Append(" ×").Append(_typeCounts[t]);
                }
                if (sb.Length > 0) _rows.Text(HudText.End(), HudSkin.TextDim);
            }

            _rows.Section("BUILD QUEUE  (" + s.BuildQueue.Count + ")");
            if (s.BuildQueue.Count == 0) _rows.Text("Nothing planned.", HudSkin.TextDim);
            for (int i = 0; i < s.BuildQueue.Count && i < 8; i++)
            {
                var o = s.BuildQueue[i];
                if (o == null) continue;
                var pr = _rows.Pair((i + 1) + ". " + _hud.BuildingName(o.Type), "priority " + HudText.F1(o.Priority) + (o.Attempts > 0 ? ", " + o.Attempts + " failed" : ""));
                if (o.Attempts > 0) pr.Color = HudSkin.Warn;
                if (!string.IsNullOrEmpty(o.Reason)) _rows.Text("    " + o.Reason, HudSkin.TextDim);
            }
            if (s.BuildQueue.Count > 8) _rows.Text("... and " + (s.BuildQueue.Count - 8) + " more", HudSkin.TextDim);

            if (s.AiLog.Count > 0)
            {
                _rows.Section("RECENT AI DECISIONS");
                for (int i = s.AiLog.Count - 1, n = 0; i >= 0 && n < 6; i--, n++)
                    if (!string.IsNullOrEmpty(s.AiLog[i])) _rows.Text("• " + s.AiLog[i], HudSkin.Text);
            }

            _rows.Section("SERVICES  (coverage)");
            for (int i = 0; i < Counts.Services && i < s.ServiceCoverage.Length; i++)
            {
                float cov = MathUtil.Sanitize(s.ServiceCoverage[i]);
                float outp = i < s.ServiceOutput.Length ? MathUtil.Sanitize(s.ServiceOutput[i]) : 0f;
                var row = _rows.Bar(ServiceNames[i], MathUtil.Clamp01(cov), cov >= 0.95f ? HudSkin.Good : cov >= 0.5f ? HudSkin.Warn : HudSkin.Bad, HudText.Pct(cov));
                row.Tip = ServiceNames[i] + ": " + HudText.F2(outp) + " points/day, coverage " + HudText.Pct(cov);
            }

            _rows.Spacer();
            _rows.Link("Focus camera", HudRowList.LinkFocusTile, s.Id, s.Center);
        }

        private void BuildBuilding(WorldState st, Building b)
        {
            if (b == null) return;
            var db = _hud.Db;
            BuildingDefinition def = null;
            if (db != null) db.TryGetBuilding(b.Type, out def);
            string name = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : _hud.BuildingName(b.Type);
            _rows.Header(name, def != null ? def.Category.ToString() : "", CivColor(st, b.CivilizationId), true);
            if (def != null && !string.IsNullOrEmpty(def.Description)) _rows.Text(def.Description, HudSkin.TextDim);

            var s = st.Settlements.Get(b.SettlementId);
            if (s != null && s.IsAlive) _rows.Link("Settlement: " + s.Name, HudRowList.LinkSelectSettlement, s.Id, s.Center);

            _rows.Section("STATUS");
            if (b.IsOnFire) _rows.KV("Status", "ON FIRE", HudSkin.Bad);
            _rows.Bar("Condition", b.Condition, Level(b.Condition), HudText.Pct(b.Condition));
            if (!b.IsComplete)
            {
                _rows.Bar("Construction", b.ConstructionProgress, HudSkin.Gold, HudText.Pct(b.ConstructionProgress));
                _rows.KV("Materials", b.CostPaid ? "Paid" : "Waiting for resources", b.CostPaid ? HudSkin.Good : HudSkin.Warn);
            }
            else if (b.CompletedTick >= 0) _rows.KV("Completed", "Year " + HudText.Int(SimTime.Year(b.CompletedTick)));
            _rows.KV("Level", HudText.Int(b.Level));

            bool jobs = def != null ? def.HasJobs : b.Workers.Count > 0;
            if (jobs)
            {
                int slots = def != null ? def.JobSlots : b.Workers.Count;
                int ji = def != null ? (int)def.Job : 0;
                _rows.Section("WORK");
                _rows.KV("Job", ji >= 0 && ji < JobNames.Length ? JobNames[ji] : "-");
                _rows.KV("Workers", b.Workers.Count + " / " + slots, b.Workers.Count < slots ? HudSkin.Warn : HudSkin.Good);
                _rows.Bar("Efficiency", b.Efficiency, Level(b.Efficiency), HudText.Pct(b.Efficiency));
                if (def != null && def.OutputsPerWorker != null && def.OutputsPerWorker.Length > 0)
                {
                    var sb = HudText.Begin();
                    for (int i = 0; i < def.OutputsPerWorker.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(def.OutputsPerWorker[i].Type).Append(' ').Append(HudText.F2(def.OutputsPerWorker[i].Amount));
                    }
                    _rows.KV("Output / worker / day", HudText.End());
                }
                if (def != null && def.InputsPerWorker != null && def.InputsPerWorker.Length > 0)
                {
                    var sb = HudText.Begin();
                    for (int i = 0; i < def.InputsPerWorker.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(def.InputsPerWorker[i].Type).Append(' ').Append(HudText.F2(def.InputsPerWorker[i].Amount));
                    }
                    _rows.KV("Input / worker / day", HudText.End());
                }
            }
            if ((def != null && def.IsDwelling) || b.HousingCapacity > 0)
            {
                _rows.Section("HOUSING");
                _rows.KV("Residents", b.Residents.Count + " / " + b.HousingCapacity, b.Residents.Count >= b.HousingCapacity ? HudSkin.Warn : HudSkin.Text);
            }
            if (def != null && def.Services != null && def.Services.Length > 0)
            {
                _rows.Section("SERVICES PROVIDED");
                for (int i = 0; i < def.Services.Length; i++)
                {
                    int si = (int)def.Services[i].Type;
                    _rows.KV(si >= 0 && si < ServiceNames.Length ? ServiceNames[si] : "?", HudText.F2(def.Services[i].Amount) + " /day (radius " + def.ServiceRadius + ")");
                }
            }
            _rows.Section("STYLE");
            _rows.KV("Architecture", _hud.RaceName(b.StyleRace) + ", " + _hud.AgeName(b.StyleAge));
            _rows.KV("Footprint", b.Width + " × " + b.Height + " at " + b.Origin.X + ", " + b.Origin.Y);
            _rows.Spacer();
            _rows.Link("Focus camera", HudRowList.LinkFocusTile, b.Id, b.Center);
        }

        private void BuildArmy(WorldState st, Army a)
        {
            if (a == null) return;
            var civ = st.Civilizations.Get(a.CivilizationId);
            _rows.Header("Army of " + (civ != null ? civ.Name : "?"), a.State.ToString(), CivColor(st, a.CivilizationId), true);
            _rows.KV("Soldiers", HudText.Int(a.SoldierIds.Count));
            _rows.KV("Strength", HudText.F0(a.Strength));
            _rows.Bar("Morale", a.Morale, Level(a.Morale), HudText.Pct(a.Morale));
            _rows.KV("State", a.State.ToString());
            var home = st.Settlements.Get(a.HomeSettlementId);
            if (home != null) _rows.KV("Home", home.Name);
            var target = st.Settlements.Get(a.TargetSettlementId);
            _rows.KV("Target", target != null ? target.Name + " (" + _hud.CivName(target.CivilizationId) + ")" : "None", target != null ? HudSkin.Warn : HudSkin.Text);
            var war = a.WarId >= 0 ? st.Diplomacy.GetWar(a.WarId) : null;
            if (war != null)
            {
                int enemy = war.AttackerId == a.CivilizationId ? war.DefenderId : war.AttackerId;
                _rows.KV("War", "vs " + _hud.CivName(enemy) + ", score " + HudText.Signed(war.WarScore, "0"), HudSkin.Bad);
            }
            _rows.KV("Position", a.Position.X + ", " + a.Position.Y);
            _rows.Spacer();
            _rows.Link("Focus camera", HudRowList.LinkFocusTile, a.Id, a.Position);
            if (target != null) _rows.Link("Show target", HudRowList.LinkFocusTile, target.Id, target.Center);
        }

        private void BuildTile(WorldState st, Vec2i t)
        {
            var map = st.Map;
            if (map == null || !map.InBounds(t)) return;
            int i = map.Index(t);
            int bi = (int)map.Biomes[i];
            _rows.Header("Tile " + t.X + ", " + t.Y, bi >= 0 && bi < BiomeNames.Length ? BiomeNames[bi] : "?", HudSkin.Gold, false);
            float e = MathUtil.Sanitize(map.Elevation[i]);
            _rows.KV("Elevation", HudText.F2(e) + (e < map.SeaLevel ? " (below sea level)" : ""));
            _rows.Bar("Moisture", map.Moisture[i], HudSkin.ManaBlue, HudText.Pct(map.Moisture[i]));
            _rows.Bar("Temperature", map.Temperature[i], HudSkin.Warn, HudText.Pct(map.Temperature[i]));
            _rows.Bar("Fertility", map.Fertility[i], HudSkin.Good, HudText.Pct(map.Fertility[i]));
            var flags = map.Flags[i];
            _rows.KV("Features", flags == TileFlags.None ? "None" : "");
            if (flags != TileFlags.None) _rows.Text(flags.ToString(), HudSkin.Text);
            if (map.Resource[i] > 0.01f) _rows.KV("Resource left", HudText.F0(map.Resource[i]));

            int owner = map.Owner[i];
            var os = owner >= 0 ? st.Settlements.Get(owner) : null;
            if (os != null) _rows.Link("Owner: " + os.Name + " (" + _hud.CivName(os.CivilizationId) + ")", HudRowList.LinkSelectSettlement, os.Id, os.Center);
            else _rows.KV("Owner", "Unclaimed", HudSkin.TextDim);
            int bId = map.BuildingAt[i];
            var b = bId >= 0 ? st.Buildings.Get(bId) : null;
            if (b != null && !b.IsDestroyed) _rows.KV("Building", _hud.BuildingName(b.Type) + (b.IsComplete ? "" : " (under construction)"));
            _rows.Spacer();
            _rows.Link("Focus camera", HudRowList.LinkFocusTile, -1, t);
        }

        // ------------------------------------------------------------------ draw
        public void Draw(float x, float y, float w, float maxH)
        {
            var sk = _hud.Skin;
            var sel = _hud.Selection;
            if (sel == null || !sel.HasSelection || _availableCount == 0 || _rows.Count == 0 || maxH < sk.U(80f))
            {
                LastRect = new Rect(x, y, 0f, 0f);
                return;
            }
            float pad = sk.Pad;
            float tabH = sk.U(24f);
            float cw = w - pad * 2f;
            float contentW = cw - sk.ScrollbarW - sk.U(4f);
            float contentH = _rows.Measure(sk, contentW);
            float h = Mathf.Min(maxH, pad * 2f + tabH + sk.Gap + contentH);
            var panel = new Rect(x, y, w, h);
            HudDraw.Paint(panel, sk.Panel);
            _hud.AddHitRect(panel);
            LastRect = panel;

            // Tabs + close
            float tx = x + pad, ty = y + pad;
            float closeW = sk.U(24f);
            float tabW = Mathf.Min(sk.U(76f), (cw - closeW - sk.Gap) / Mathf.Max(1, _availableCount) - sk.U(2f));
            for (int i = 0; i < _available.Length; i++)
            {
                if (!_available[i]) continue;
                bool on = (int)_tab == i;
                if (GUI.Toggle(new Rect(tx, ty, tabW, tabH), on, _tabs[i], sk.Tab) && !on)
                {
                    _tab = (Tab)i;
                    _scroll = Vector2.zero;
                    _hud.MarkDirty();
                }
                tx += tabW + sk.U(2f);
            }
            var cr = new Rect(x + w - pad - closeW, ty, closeW, tabH);
            if (GUI.Button(cr, _close, sk.ButtonSmall)) sel.Clear();
            _hud.Tooltip(cr, _close.tooltip);

            float vy = ty + tabH + sk.Gap;
            float viewH = y + h - pad - vy;
            var view = new Rect(x + pad, vy, cw, viewH);
            bool scroll = contentH > viewH + 0.5f;
            HudRow clicked;
            if (scroll)
            {
                _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0f, 0f, contentW, contentH), false, false, sk.ScrollH, sk.ScrollV);
                clicked = _rows.Draw(_hud, sk, 0f, 0f, contentW, _scroll.y - sk.U(30f), _scroll.y + viewH + sk.U(30f));
                GUI.EndScrollView();
            }
            else
            {
                clicked = _rows.Draw(_hud, sk, view.x, view.y, contentW, float.MinValue, float.MaxValue);
            }
            if (clicked != null)
            {
                if (clicked.LinkKind == HudRowList.LinkSelectSettlement && clicked.LinkId > 0) _hud.FocusSettlement(clicked.LinkId);
                else if (clicked.LinkKind == HudRowList.LinkFocusTile) GodSimHUD.FocusTile(clicked.LinkTile);
            }
        }
    }
}
