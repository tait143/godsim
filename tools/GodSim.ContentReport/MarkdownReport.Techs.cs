using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.ContentReport
{
    internal sealed partial class MarkdownReport
    {
        /// <summary>Maximum mermaid nodes per tech diagram (larger ages are split into parts).</summary>
        private const int MaxGraphNodes = 15;

        private void Technologies()
        {
            H(2, "Technologies");
            if (_m.Techs.Count == 0) { P("_No technologies registered._"); return; }

            H(3, "Totals");
            var tot = new MdTable("Age", ">Techs", ">General", ">Racial", ">Total cost", ">General cost", ">Min", ">Max", ">Avg", ">Unlocks");
            int sumN = 0, sumG = 0, sumR = 0, sumU = 0; float sumC = 0f, sumGC = 0f;
            for (int a = 0; a < _m.NumAges; a++)
            {
                var list = _m.TechsOf(a);
                if (list.Count == 0) { tot.Row(Age(a), 0, 0, 0, "—", "—", "—", "—", "—", 0); continue; }
                int g = 0, u = 0; float c = 0f, gc = 0f, mn = float.MaxValue, mx = 0f;
                foreach (var t in list)
                {
                    c += t.Cost; mn = Math.Min(mn, t.Cost); mx = Math.Max(mx, t.Cost);
                    if (t.AllowedRaces == RaceMask.All) { g++; gc += t.Cost; }
                    foreach (var e in t.Effects ?? new TechEffect[0]) if (e.Kind == TechEffectKind.UnlockBuilding) u++;
                }
                tot.Row("**" + Age(a) + "**", list.Count, g, list.Count - g, Md.N(c, 0), Md.N(gc, 0), Md.N(mn, 0), Md.N(mx, 0), Md.N(c / list.Count, 0), u);
                sumN += list.Count; sumG += g; sumR += list.Count - g; sumC += c; sumGC += gc; sumU += u;
            }
            tot.Row("**Total**", "**" + sumN + "**", sumG, sumR, "**" + Md.N(sumC, 0) + "**", Md.N(sumGC, 0), "", "", sumN > 0 ? Md.N(sumC / sumN, 0) : "—", sumU);
            Table(tot);

            for (int a = 0; a < _m.NumAges; a++)
            {
                var list = _m.TechsOf(a);
                if (list.Count == 0) continue;
                var ageDef = _m.AgeDef(a);
                H(3, (ageDef != null ? ageDef.Name : Age(a)) + " technologies");
                var t = new MdTable("Key", "Name", ">Cost", "Prerequisites", "Effects", "Races");
                foreach (var tech in list)
                    t.Row("`" + tech.Key + "`", tech.Name, Md.N(tech.Cost, 0), Prereqs(tech), Effects(tech), _m.RacesText(tech.AllowedRaces));
                Table(t);
                foreach (var part in TechGraphs(a, list)) Mermaid(part);
            }

            H(3, "Racial technologies");
            var rt = new MdTable("Race", "Age", "Key", "Name", ">Cost", "Effects");
            foreach (var race in _m.Races)
                foreach (var tech in _m.Techs)
                    if (tech.AllowedRaces != RaceMask.All && tech.AllowedRaces.Allows(race.Id))
                        rt.Row("**" + race.Name + "**", Age(tech.Age), "`" + tech.Key + "`", tech.Name, Md.N(tech.Cost, 0), Effects(tech));
            if (rt.RowCount == 0) P("_No racial technologies._"); else Table(rt);

            H(3, "Descriptions");
            foreach (var tech in _m.Techs)
                if (!string.IsNullOrEmpty(tech.Description)) Line("- `" + tech.Key + "` **" + tech.Name + "**: " + Md.Cell(tech.Description));
            Line("");
        }

        private string Prereqs(TechDefinition t)
        {
            var parts = new List<string>();
            foreach (var k in t.PrerequisiteKeys ?? new string[0])
            {
                var p = _m.Db.TechByKey(k);
                parts.Add(p == null ? "**`" + k + "` (missing)**" : ((int)p.Age != (int)t.Age ? p.Name + " (" + Age(p.Age) + ")" : p.Name));
            }
            return Md.Join(parts);
        }

        /// <summary>Readable effect list, e.g. "+10% Wood, unlocks Farm (normally Bronze)".</summary>
        public string Effects(TechDefinition t)
        {
            var parts = new List<string>();
            foreach (var e in t.Effects ?? new TechEffect[0]) parts.Add(Effect(e));
            return Md.Join(parts, "; ");
        }

        public string Effect(TechEffect e)
        {
            switch (e.Kind)
            {
                case TechEffectKind.UnlockBuilding:
                {
                    var b = _m.BuildingDef(e.Building);
                    return "unlocks **" + B(e.Building) + "**" + (b != null ? " (normally " + Age(b.MinAge) + ")" : " (unregistered)");
                }
                case TechEffectKind.ProductionMultiplier: return Md.Pct(e.Value) + " " + e.Resource;
                case TechEffectKind.AllProductionMultiplier: return Md.Pct(e.Value) + " all production";
                case TechEffectKind.ResearchMultiplier: return Md.Pct(e.Value) + " research";
                case TechEffectKind.GrowthMultiplier: return Md.Pct(e.Value) + " growth";
                case TechEffectKind.LifespanBonus: return Md.Signed(e.Value, 1) + " yrs lifespan";
                case TechEffectKind.HealthBonus: return Md.Signed(e.Value) + " health";
                case TechEffectKind.HappinessBonus: return Md.Signed(e.Value) + " happiness";
                case TechEffectKind.MilitaryMultiplier: return Md.Pct(e.Value) + " military";
                case TechEffectKind.BuildSpeedMultiplier: return Md.Pct(e.Value) + " build speed";
                case TechEffectKind.FaithMultiplier: return Md.Pct(e.Value) + " faith";
                case TechEffectKind.StorageMultiplier: return Md.Pct(e.Value) + " storage";
                case TechEffectKind.TradeMultiplier: return Md.Pct(e.Value) + " trade";
                case TechEffectKind.ServiceMultiplier: return Md.Pct(e.Value) + " " + e.Service + " service";
                case TechEffectKind.MoveSpeedMultiplier: return Md.Pct(e.Value) + " move speed";
                default: return e.Kind + " " + Md.N(e.Value);
            }
        }

        /// <summary>
        /// Mermaid dependency graphs for one age. Own techs are chunked so a diagram holds at most
        /// <see cref="MaxGraphNodes"/> nodes; prerequisites from earlier ages (or other parts) appear as dashed nodes,
        /// or are collapsed into one summary node when they would exceed the limit.
        /// </summary>
        private List<List<string>> TechGraphs(int age, List<TechDefinition> list)
        {
            var result = new List<List<string>>();
            var chunks = new List<List<TechDefinition>>();
            int chunkSize = list.Count <= MaxGraphNodes - 3 ? list.Count : 10;
            for (int i = 0; i < list.Count; i += chunkSize) chunks.Add(list.GetRange(i, Math.Min(chunkSize, list.Count - i)));

            for (int c = 0; c < chunks.Count; c++)
            {
                var own = chunks[c];
                var ownIds = new HashSet<int>();
                foreach (var t in own) ownIds.Add(t.Id);
                var external = new List<TechDefinition>();
                foreach (var t in own)
                    foreach (int p in t.Prerequisites ?? new int[0])
                    {
                        var pt = _m.Db.Tech(p);
                        if (pt != null && !ownIds.Contains(p) && !external.Contains(pt)) external.Add(pt);
                    }
                bool collapse = own.Count + external.Count > MaxGraphNodes;

                var l = new List<string>();
                if (chunks.Count > 1) l.Add("%% " + Age(age) + " part " + (c + 1) + " of " + chunks.Count);
                l.Add("flowchart LR");
                l.Add("  classDef ext fill:#f2f2f2,stroke:#999,stroke-dasharray:4 3,color:#555");
                l.Add("  classDef racial fill:#fde9c9,stroke:#c77d1a,color:#222");
                l.Add("  classDef unlock fill:#dff0d8,stroke:#3c763d,color:#222");
                if (collapse)
                    l.Add("  ext_prev[\"" + external.Count + " earlier techs\"]:::ext");
                else
                    foreach (var e in external)
                        l.Add("  " + Md.Id("t_", e.Key) + "[\"" + Md.Label(e.Name) + "<br/>" + Age(e.Age) + "\"]:::ext");
                foreach (var t in own)
                {
                    string cls = t.AllowedRaces != RaceMask.All ? ":::racial" : HasUnlock(t) ? ":::unlock" : "";
                    string races = t.AllowedRaces != RaceMask.All ? " · " + _m.RacesText(t.AllowedRaces) : "";
                    l.Add("  " + Md.Id("t_", t.Key) + "[\"" + Md.Label(t.Name) + "<br/>" + Md.N(t.Cost, 0) + " RP" + Md.Label(races) + "\"]" + cls);
                }
                var collapsedEdges = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in own)
                    foreach (int p in t.Prerequisites ?? new int[0])
                    {
                        var pt = _m.Db.Tech(p);
                        if (pt == null) continue;
                        string from = ownIds.Contains(p) || !collapse ? Md.Id("t_", pt.Key) : "ext_prev";
                        string edge = "  " + from + " --> " + Md.Id("t_", t.Key);
                        if (from == "ext_prev" && !collapsedEdges.Add(edge)) continue;
                        l.Add(edge);
                    }
                result.Add(l);
            }
            return result;
        }

        private static bool HasUnlock(TechDefinition t)
        {
            foreach (var e in t.Effects ?? new TechEffect[0]) if (e.Kind == TechEffectKind.UnlockBuilding) return true;
            return false;
        }
    }
}
