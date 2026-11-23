using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>`stats`: civilizations, settlements, buildings by category, wars, chronicle highlights, state hash.</summary>
    public static class StatsCommand
    {
        public static int Run(IReadOnlyList<string> args)
        {
            var p = new ArgParser("stats", "file").Option("--key-file").Option("--key-hex").Option("--top").Parse(args, 1);
            int top = p.Int("--top", 40, 1, 100000);
            byte[] key = KeyMaterial.FromArgs(p);
            LoadedSave save;
            try { save = SaveAccess.Load(p.Positional(0), key, false); }
            finally { KeyMaterial.Wipe(key); }

            WorldState s = save.State;
            GameDatabase db = save.Db;
            WorldSummary w = WorldSummary.Of(s);

            TextOut.Line("Save: " + TextOut.Safe(save.Path, 300) + (save.Header.IsSigned ? "  [signed, verified]" : "  [unsigned]"));
            TextOut.Section("World");
            TextOut.Field("Seed", s.Seed + " (0x" + s.Seed.ToString("X16") + ")");
            TextOut.Field("Tick", s.Tick + " (" + SimTime.Format(s.Tick) + ")");
            TextOut.Field("Map", s.Map.Width + " x " + s.Map.Height);
            TextOut.Field("World age", s.WorldAge.ToString());
            TextOut.Field("People", w.PeopleAlive + " alive / " + w.PeopleTotal + " stored (peak " + s.Stats.PeakPopulation + ")");
            TextOut.Field("Buildings", w.BuildingsComplete + " complete, " + w.BuildingsUnderConstruction + " under construction, " + w.BuildingsDestroyed + " destroyed (pending flush)");
            TextOut.Field("Settlements", w.SettlementsAlive + " alive / " + w.SettlementsTotal);
            TextOut.Field("Civilizations", w.CivsAlive + " alive / " + w.CivsTotal);
            TextOut.Field("God", "mana " + TextOut.Num(s.God.Mana, "0.#") + "/" + TextOut.Num(s.God.MaxMana, "0.#") + ", alignment " + TextOut.Num(s.God.Alignment, "0.00") + ", powers cast " + s.God.PowersCast);
            TextOut.Field("Weather", s.Weather.Season + ", rain x" + TextOut.Num(s.Weather.Rainfall, "0.00") + ", food x" + TextOut.Num(s.Weather.FoodYieldMultiplier, "0.00"));
            TextOut.Field("Lifetime", "births " + s.Stats.TotalBirths + ", deaths " + s.Stats.TotalDeaths + ", built " + s.Stats.BuildingsCompleted +
                                      ", wars " + s.Stats.WarsDeclared + ", battles " + s.Stats.BattlesFought);

            PrintCivs(s, w);
            PrintSettlements(s, top);
            PrintBuildings(s, db);
            PrintWars(s);
            PrintChronicle(s, top);

            TextOut.Section("Hash");
            TextOut.Field("StateHasher", TextOut.Hash(StateHasher.Compute(s)));
            return ExitCodes.Ok;
        }

        private static void PrintCivs(WorldState s, WorldSummary w)
        {
            TextOut.Section("Civilizations (" + w.CivsTotal + ")");
            var t = new TextTable("Id", "Name", "Race", "Age", "Status", "Pop", "Settl", "Bldgs", "Techs", "Research", "Military", "Devotion").Right(0, 5, 6, 7, 8, 9, 10, 11);
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                Civilization c = civs[i];
                CivSummary cs = w.Civs[c.Id];
                long ageYears = c.IsAlive ? SimTime.Year(System.Math.Max(0, s.Tick - c.AgeEnteredTick)) : 0;
                t.Add(c.Id, c.Name, c.Race.ToString(), c.Age + (c.IsAlive ? " (" + ageYears + "y)" : ""),
                      c.IsAlive ? "alive" : "fallen@" + c.CollapsedTick, cs.Population, cs.Settlements, cs.Buildings, cs.Techs,
                      TextOut.Num(c.TotalResearch, "0"), TextOut.Num(c.MilitaryPower, "0.#"), TextOut.Num(c.Devotion, "0.00"));
            }
            t.Write(TextOut.Out);
        }

        private static void PrintSettlements(WorldState s, int top)
        {
            var list = new List<Settlement>();
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) list.Add(items[i]);
            list.Sort((a, b) => a.Population != b.Population ? b.Population.CompareTo(a.Population) : a.Id.CompareTo(b.Id));
            TextOut.Section("Settlements (" + list.Count + " alive" + (list.Count > top ? ", top " + top + " by population" : "") + ")");
            var t = new TextTable("Id", "Name", "Civ", "Race", "Tier", "Pop", "Bldgs", "Homeless", "Unemp", "Soldiers", "Happy", "Health", "Queue").Right(0, 5, 6, 7, 8, 9, 10, 11, 12);
            for (int i = 0; i < list.Count && i < top; i++)
            {
                Settlement st = list[i];
                int live = 0;
                for (int k = 0; k < st.BuildingIds.Count; k++)
                {
                    Building b = s.Buildings.Get(st.BuildingIds[k]);
                    if (b != null && !b.IsDestroyed) live++;
                }
                t.Add(st.Id, st.Name + (st.IsCapital ? " *" : ""), WorldSummary.CivName(s, st.CivilizationId), st.Race.ToString(), st.Tier.ToString(),
                      st.Population, live, st.Homeless, st.Unemployed, st.Soldiers,
                      TextOut.Num(st.Happiness, "0.00"), TextOut.Num(st.Health, "0.00"), st.BuildQueue.Count);
            }
            t.Write(TextOut.Out);
            TextOut.Line("  (* = capital)");
        }

        private static void PrintBuildings(WorldState s, GameDatabase db)
        {
            var complete = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            var building = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            var byType = new int[256];
            var items = s.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Building b = items[i];
                if (b.IsDestroyed) continue;
                string cat = db != null && db.TryGetBuilding(b.Type, out BuildingDefinition def) ? def.Category.ToString() : "(unregistered)";
                var target = b.IsComplete ? complete : building;
                target.TryGetValue(cat, out int n);
                target[cat] = n + 1;
                if (b.IsComplete) byType[(byte)b.Type]++;
            }
            var cats = new SortedSet<string>(complete.Keys, System.StringComparer.Ordinal);
            foreach (string k in building.Keys) cats.Add(k);
            TextOut.Section("Buildings by category");
            var t = new TextTable("Category", "Complete", "Building").Right(1, 2);
            foreach (string c in cats)
            {
                complete.TryGetValue(c, out int a);
                building.TryGetValue(c, out int u);
                t.Add(c, a, u);
            }
            t.Write(TextOut.Out);

            // Top building types (complete), useful for balancing sanity checks.
            var types = new List<int>();
            for (int i = 0; i < byType.Length; i++) if (byType[i] > 0) types.Add(i);
            types.Sort((a, b) => byType[a] != byType[b] ? byType[b].CompareTo(byType[a]) : a.CompareTo(b));
            var tt = new TextTable("Type", "Complete").Right(1);
            for (int i = 0; i < types.Count && i < 15; i++) tt.Add(((BuildingType)types[i]).ToString(), byType[types[i]]);
            if (tt.RowCount > 0) { TextOut.Line(); TextOut.Line("  Most common types:"); tt.Write(TextOut.Out); }
        }

        private static void PrintWars(WorldState s)
        {
            var wars = s.Diplomacy.Wars;
            TextOut.Section("Wars (" + wars.Count + ")");
            if (wars.Count == 0) { TextOut.Line("  none"); return; }
            var t = new TextTable("Id", "Attacker", "Defender", "Status", "Started", "Score", "Battles", "Losses A/D", "Captured", "Casus belli").Right(0, 5, 6, 8);
            for (int i = 0; i < wars.Count; i++)
            {
                War w = wars[i];
                t.Add(w.Id, WorldSummary.CivName(s, w.AttackerId), WorldSummary.CivName(s, w.DefenderId),
                      w.IsActive ? "active" : "ended " + (w.EndTick >= 0 ? "Y" + SimTime.Year(w.EndTick) : "?"),
                      "Y" + SimTime.Year(System.Math.Max(0, w.StartTick)), TextOut.Num(w.WarScore, "0.#"), w.Battles,
                      w.AttackerLosses + "/" + w.DefenderLosses, w.SettlementsCaptured, w.CasusBelli);
            }
            t.Write(TextOut.Out);
        }

        private static void PrintChronicle(WorldState s, int top)
        {
            var entries = s.Chronicle.Entries;
            var picked = new List<ChronicleEntry>();
            for (int i = entries.Count - 1; i >= 0 && picked.Count < top; i--) if (entries[i].Importance >= 2) picked.Add(entries[i]);
            TextOut.Section("Chronicle highlights (importance >= 2; " + picked.Count + " most recent of " + entries.Count + " entries)");
            if (picked.Count == 0) { TextOut.Line("  none"); return; }
            for (int i = picked.Count - 1; i >= 0; i--)
            {
                ChronicleEntry e = picked[i];
                string stars = new string('*', System.Math.Max(0, System.Math.Min(3, e.Importance)));
                TextOut.Line("  " + SimTime.Format(System.Math.Max(0, e.Tick)).PadRight(26) + " " + e.Category.ToString().PadRight(12) + " " + stars.PadRight(3) + " " + TextOut.Safe(e.Text, 200));
            }
        }
    }
}
