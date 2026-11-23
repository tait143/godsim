using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.ContentReport
{
    internal sealed partial class MarkdownReport
    {
        /// <summary>Maximum mermaid nodes per production-chain diagram.</summary>
        private const int MaxChainNodes = 18;

        private void BuildingsSection()
        {
            H(2, "Buildings");
            if (_m.Buildings.Count == 0) { P("_No buildings registered._"); return; }

            H(3, "Overview by age and category");
            var cats = (BuildingCategory[])Enum.GetValues(typeof(BuildingCategory));
            var headers = new List<string> { "Age" };
            foreach (var c in cats) headers.Add(">" + c);
            headers.Add(">Total");
            var ov = new MdTable(headers.ToArray());
            for (int a = 0; a < _m.NumAges; a++)
            {
                var row = new List<object> { "**" + Age(a) + "**" };
                int total = 0;
                foreach (var c in cats)
                {
                    int n = 0;
                    foreach (var b in _m.Buildings) if ((int)b.MinAge == a && b.Category == c) n++;
                    row.Add(n == 0 ? "·" : n.ToString());
                    total += n;
                }
                row.Add(total);
                ov.Row(row.ToArray());
            }
            Table(ov);
            P("Counted by `MinAge` (the age a building becomes available without techs).");

            H(3, "Buildings by category");
            var ct = new MdTable("Category", "Buildings (available from)");
            foreach (var c in cats)
            {
                var parts = new List<string>();
                foreach (var b in SortedBuildings()) if (b.Category == c) parts.Add(b.Name + " (" + Age(b.MinAge) + (b.AllowedRaces != RaceMask.All ? ", " + _m.RacesText(b.AllowedRaces) : "") + ")");
                ct.Row("**" + c + "**", Md.Join(parts));
            }
            Table(ct);

            for (int a = 0; a < _m.NumAges; a++)
            {
                var list = new List<BuildingDefinition>();
                foreach (var b in SortedBuildings()) if ((int)b.MinAge == a) list.Add(b);
                if (list.Count == 0) continue;
                var def = _m.AgeDef(a);
                H(3, (def != null ? def.Name : Age(a)) + " buildings");

                var econ = new MdTable("Building", "Category", "Jobs", "Inputs / worker", "Outputs / worker", "Services", ">Radius", ">Storage", ">Upkeep", ">Housing");
                foreach (var b in list)
                    econ.Row("**" + b.Name + "**", b.Category, b.HasJobs ? b.JobSlots + " × " + b.Job : "—", Amounts(b.InputsPerWorker), Amounts(b.OutputsPerWorker),
                        Services(b.Services), (b.Services ?? ServiceAmount.None).Length > 0 ? b.ServiceRadius.ToString() : "—",
                        b.StorageCapacity > 0f ? Md.N(b.StorageCapacity, 0) : "—", b.UpkeepGold > 0f ? Md.N(b.UpkeepGold, 3) : "—",
                        b.IsDwelling ? b.HousingMin + "–" + b.HousingMax : "—");
                Table(econ);

                var build = new MdTable("Building", "^Size", "Cost", ">Work", "Placement", ">Desir.", "Window", "Limits", "Races");
                foreach (var b in list)
                    build.Row("**" + b.Name + "**", b.Width + "×" + b.Height, Amounts(b.Cost), Md.N(b.BuildWork, 0), PlacementText(b.Placement),
                        Md.Signed(b.Desirability), Window(b), Limits(b), _m.RacesText(b.AllowedRaces));
                Table(build);
            }

            H(3, "Services by provider");
            var sv = new MdTable("Service", "Providers (points per tick, fully staffed)");
            for (int s = 0; s < Counts.Services; s++)
            {
                var parts = new List<string>();
                var providers = new List<BuildingDefinition>(_m.ServiceProviders[s]);
                providers.Sort((x, y) => x.MinAge != y.MinAge ? x.MinAge.CompareTo(y.MinAge) : x.Type.CompareTo(y.Type));
                foreach (var b in providers) parts.Add(b.Name + " " + Md.N(ContentModel.ServicePoints(b, (ServiceType)s)) + " (" + Age(b.MinAge) + ")");
                sv.Row("**" + (ServiceType)s + "**", Md.Join(parts, ", ", "**none**"));
            }
            Table(sv);

            ProductionChains();
            Wonders();
        }

        private List<BuildingDefinition> SortedBuildings()
        {
            var l = new List<BuildingDefinition>(_m.Buildings);
            l.Sort((x, y) => x.MinAge != y.MinAge ? x.MinAge.CompareTo(y.MinAge) : x.Type.CompareTo(y.Type));
            return l;
        }

        private string Window(BuildingDefinition b)
        {
            string w = Age(b.MinAge) + (b.MaxAge == AgeId.Ascension ? "+" : "–" + Age(b.MaxAge));
            var early = new List<string>();
            foreach (var t in _m.UnlockedBy[(int)b.Type])
                early.Add("`" + t.Key + "`" + (t.Age < b.MinAge ? " (" + Age(t.Age) + (t.AllowedRaces != RaceMask.All ? ", " + _m.RacesText(t.AllowedRaces) : "") + ")" : " (redundant)"));
            if (early.Count > 0) w += "; unlocked by " + string.Join(", ", early);
            return w;
        }

        private static string Limits(BuildingDefinition b)
        {
            var parts = new List<string>();
            if (b.MinTier != SettlementTier.Camp) parts.Add("≥ " + b.MinTier);
            if (b.MinPopulation > 0) parts.Add("pop ≥ " + b.MinPopulation);
            if (b.MaxPerSettlement >= 0) parts.Add("max " + b.MaxPerSettlement + "/town");
            if (b.UniquePerCivilization) parts.Add("1 per civ");
            return Md.Join(parts);
        }

        private static string PlacementText(PlacementRule p)
        {
            if (p == PlacementRule.None) return "—";
            var parts = new List<string>();
            foreach (PlacementRule v in Enum.GetValues(typeof(PlacementRule)))
                if (v != PlacementRule.None && (p & v) == v) parts.Add(v.ToString());
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------------ production chains
        private void ProductionChains()
        {
            H(3, "Production chains");
            P("Resources (rounded nodes) flow into the buildings that consume them (rectangles), which output new resources. " +
              "Edge labels in the detailed diagrams are amounts per worker per tick. Food, raw materials and processing are separate diagrams, split by age when large.");

            // Resource-level overview: raw sources -> resource, resource -> resource via processing buildings.
            var ov = new List<string> { "flowchart LR", "  classDef res fill:#e8f1fb,stroke:#3a6ea5,color:#222", "  classDef src fill:#f2f2f2,stroke:#999,color:#555" };
            ov.Add("  src_land([\"Land and labor\"]):::src");
            var used = new bool[Counts.Resources];
            var edges = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var b in SortedBuildings())
            {
                if (!b.HasJobs) continue;
                var ins = b.InputsPerWorker ?? ResourceAmount.None;
                foreach (var o in b.OutputsPerWorker ?? ResourceAmount.None)
                {
                    if (o.Amount <= 0f) continue;
                    used[(int)o.Type] = true;
                    if (ins.Length == 0) AddEdge(edges, "src_land", "r_" + o.Type, b.Name);
                    foreach (var i in ins)
                    {
                        if (i.Amount <= 0f || i.Type == o.Type) continue;
                        used[(int)i.Type] = true;
                        AddEdge(edges, "r_" + i.Type, "r_" + o.Type, b.Name);
                    }
                }
            }
            if (_m.ServiceProviders[(int)ServiceType.Commerce].Count > 0) { used[(int)ResourceType.Gold] = true; AddEdge(edges, "svc_commerce", "r_Gold", "Commerce service"); ov.Add("  svc_commerce([\"Commerce service\"]):::src"); }
            for (int r = 0; r < Counts.Resources; r++) if (used[r]) ov.Add("  r_" + (ResourceType)r + "([\"" + (ResourceType)r + "\"]):::res");
            foreach (var kv in edges)
            {
                var ends = kv.Key.Split('>');
                var names = kv.Value;
                string label = names.Count <= 3 ? string.Join(", ", names) : names[0] + ", " + names[1] + " +" + (names.Count - 2);
                ov.Add("  " + ends[0] + " -->|\"" + Md.Label(label) + "\"| " + ends[1]);
            }
            H(4, "Resource overview");
            Mermaid(ov);

            // Detailed building-level diagrams, grouped and chunked.
            var food = new List<BuildingDefinition>();
            var raw = new List<BuildingDefinition>();
            var proc = new List<BuildingDefinition>();
            foreach (var b in SortedBuildings())
            {
                if (!b.HasJobs || (b.OutputsPerWorker ?? ResourceAmount.None).Length == 0) continue;
                if ((b.InputsPerWorker ?? ResourceAmount.None).Length > 0) proc.Add(b);
                else if (ContentModel.OutputPerWorker(b, ResourceType.Food) > 0f) food.Add(b);
                else raw.Add(b);
            }
            ChainGroup("Food", food);
            ChainGroup("Raw materials", raw);
            ChainGroup("Processing and industry", proc);
        }

        private static void AddEdge(SortedDictionary<string, List<string>> edges, string from, string to, string name)
        {
            string k = from + ">" + to;
            if (!edges.TryGetValue(k, out var l)) edges[k] = l = new List<string>();
            if (!l.Contains(name)) l.Add(name);
        }

        /// <summary>Splits buildings (in age order) into diagrams of at most MaxChainNodes nodes and maxBuildings buildings.</summary>
        private static List<List<BuildingDefinition>> SplitChains(List<BuildingDefinition> list, int maxBuildings)
        {
            var chunks = new List<List<BuildingDefinition>>();
            var cur = new List<BuildingDefinition>();
            var curRes = new HashSet<ResourceType>();
            foreach (var b in list)
            {
                var res = new HashSet<ResourceType>(curRes);
                AddResources(res, b);
                if (cur.Count > 0 && (cur.Count + 1 + res.Count > MaxChainNodes || cur.Count >= maxBuildings))
                {
                    chunks.Add(cur);
                    cur = new List<BuildingDefinition>();
                    res = new HashSet<ResourceType>();
                    AddResources(res, b);
                }
                cur.Add(b);
                curRes = res;
            }
            if (cur.Count > 0) chunks.Add(cur);
            return chunks;
        }

        private static void AddResources(HashSet<ResourceType> set, BuildingDefinition b)
        {
            foreach (var x in b.InputsPerWorker ?? ResourceAmount.None) set.Add(x.Type);
            foreach (var x in b.OutputsPerWorker ?? ResourceAmount.None) set.Add(x.Type);
        }

        private void ChainGroup(string title, List<BuildingDefinition> list)
        {
            if (list.Count == 0) return;
            // Greedy pass finds how many diagrams are needed; the second pass spreads buildings evenly over them.
            var chunks = SplitChains(list, int.MaxValue);
            if (chunks.Count > 1) chunks = SplitChains(list, (list.Count + chunks.Count - 1) / chunks.Count);

            for (int c = 0; c < chunks.Count; c++)
            {
                var part = chunks[c];
                string range = Age(part[0].MinAge) + (part[part.Count - 1].MinAge != part[0].MinAge ? "–" + Age(part[part.Count - 1].MinAge) : "");
                H(4, title + (chunks.Count > 1 ? " (" + range + ")" : ""));
                var l = new List<string> { "flowchart LR", "  classDef res fill:#e8f1fb,stroke:#3a6ea5,color:#222", "  classDef bld fill:#fff7e0,stroke:#b08800,color:#222" };
                var resSeen = new SortedSet<int>();
                foreach (var b in part)
                {
                    foreach (var x in b.InputsPerWorker ?? ResourceAmount.None) resSeen.Add((int)x.Type);
                    foreach (var x in b.OutputsPerWorker ?? ResourceAmount.None) resSeen.Add((int)x.Type);
                }
                foreach (int r in resSeen) l.Add("  r_" + (ResourceType)r + "([\"" + (ResourceType)r + "\"]):::res");
                foreach (var b in part)
                {
                    string id = Md.Id("b_", b.Type.ToString());
                    string races = b.AllowedRaces != RaceMask.All ? " · " + _m.RacesText(b.AllowedRaces) : "";
                    l.Add("  " + id + "[\"" + Md.Label(b.Name) + "<br/>" + Age(b.MinAge) + Md.Label(races) + "\"]:::bld");
                }
                foreach (var b in part)
                {
                    string id = Md.Id("b_", b.Type.ToString());
                    foreach (var x in b.InputsPerWorker ?? ResourceAmount.None) l.Add("  r_" + x.Type + " -->|" + Md.N(x.Amount, 3) + "| " + id);
                    foreach (var x in b.OutputsPerWorker ?? ResourceAmount.None) l.Add("  " + id + " -->|" + Md.N(x.Amount, 3) + "| r_" + x.Type);
                }
                Mermaid(l);
            }
        }

        // ------------------------------------------------------------------ wonders
        private void Wonders()
        {
            H(3, "Wonders");
            var t = new MdTable("Wonder", "Available", "Races", "Cost", ">Work", "Services", "Limits");
            var notes = new List<string>();
            int n = 0;
            foreach (var b in SortedBuildings())
            {
                if (!b.IsWonder && b.Category != BuildingCategory.Wonder) continue;
                int first = _m.FirstAgeAnyRace(b);
                string avail = Age(b.MinAge) + (first >= 0 && first < (int)b.MinAge ? " (from " + Age(first) + " via tech)" : "");
                t.Row("**" + b.Name + "**", avail, _m.RacesText(b.AllowedRaces), Amounts(b.Cost), Md.N(b.BuildWork, 0), Services(b.Services), Limits(b));
                if (!string.IsNullOrEmpty(b.Description)) notes.Add("- **" + b.Name + "**: " + Md.Cell(b.Description));
                n++;
            }
            if (n == 0) { P("_No wonders._"); return; }
            Table(t);
            foreach (var line in notes) Line(line);
            Line("");
        }
    }
}
