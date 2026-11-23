using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>
    /// `diff &lt;a&gt; &lt;b&gt;`: compares two saves for desync investigations. Exit 0 when the state hashes are
    /// equal, 6 when they differ. Per-section hashes (StateHasher.ComputeSections) point at the subsystem that
    /// diverged first; civ rows are matched by id.
    /// </summary>
    public static class DiffCommand
    {
        public static int Run(IReadOnlyList<string> args)
        {
            var p = new ArgParser("diff", "a", "b").Option("--key-file").Option("--key-hex").Parse(args, 1);
            byte[] key = KeyMaterial.FromArgs(p);
            LoadedSave a, b;
            try
            {
                a = SaveAccess.Load(p.Positional(0), key, false);
                b = SaveAccess.Load(p.Positional(1), key, false);
            }
            finally { KeyMaterial.Wipe(key); }

            WorldState sa = a.State, sb = b.State;
            ulong ha = StateHasher.Compute(sa), hb = StateHasher.Compute(sb);
            int differences = 0;

            TextOut.Line("A: " + TextOut.Safe(a.Path, 300));
            TextOut.Line("B: " + TextOut.Safe(b.Path, 300));

            TextOut.Section("World");
            var t = new TextTable("Field", "A", "B", "Delta").Right(1, 2, 3);
            differences += RowText(t, "Seed", sa.Seed.ToString(), sb.Seed.ToString());
            differences += Row(t, "Tick", sa.Tick, sb.Tick, true);
            differences += Row(t, "Map width", sa.Map.Width, sb.Map.Width, true);
            differences += Row(t, "Map height", sa.Map.Height, sb.Map.Height, true);
            differences += Row(t, "Schema", a.Header.SchemaVersion, b.Header.SchemaVersion, true);
            differences += RowText(t, "RNG state", "0x" + sa.Rng.State.ToString("X16"), "0x" + sb.Rng.State.ToString("X16"));
            differences += RowText(t, "God mana", TextOut.Num(sa.God.Mana, "0.###"), TextOut.Num(sb.God.Mana, "0.###"));
            t.Write(TextOut.Out);

            WorldSummary wa = WorldSummary.Of(sa), wb = WorldSummary.Of(sb);
            TextOut.Section("Entity counts");
            var e = new TextTable("Entity", "A", "B", "Delta").Right(1, 2, 3);
            differences += Row(e, "People (alive)", wa.PeopleAlive, wb.PeopleAlive, true);
            differences += Row(e, "People (stored)", wa.PeopleTotal, wb.PeopleTotal, true);
            differences += Row(e, "Buildings (complete)", wa.BuildingsComplete, wb.BuildingsComplete, true);
            differences += Row(e, "Buildings (constructing)", wa.BuildingsUnderConstruction, wb.BuildingsUnderConstruction, true);
            differences += Row(e, "Settlements (alive)", wa.SettlementsAlive, wb.SettlementsAlive, true);
            differences += Row(e, "Civilizations (alive)", wa.CivsAlive, wb.CivsAlive, true);
            differences += Row(e, "Armies", wa.Armies, wb.Armies, true);
            differences += Row(e, "Wars (active)", wa.WarsActive, wb.WarsActive, true);
            differences += Row(e, "Wars (total)", wa.WarsTotal, wb.WarsTotal, true);
            differences += Row(e, "Relations", wa.Relations, wb.Relations, true);
            differences += Row(e, "Chronicle entries", wa.ChronicleEntries, wb.ChronicleEntries, true);
            differences += Row(e, "Next person id", sa.Ids.NextPersonId, sb.Ids.NextPersonId, true);
            differences += Row(e, "Next building id", sa.Ids.NextBuildingId, sb.Ids.NextBuildingId, true);
            differences += Row(e, "Total births", sa.Stats.TotalBirths, sb.Stats.TotalBirths, true);
            differences += Row(e, "Total deaths", sa.Stats.TotalDeaths, sb.Stats.TotalDeaths, true);
            e.Write(TextOut.Out);

            TextOut.Section("Civilizations (matched by id; only rows that differ)");
            var c = new TextTable("Id", "Name", "Age A>B", "Pop", "Settl", "Bldgs", "Techs", "Research").Right(0, 3, 4, 5, 6, 7);
            var ids = new SortedSet<int>(wa.Civs.Keys);
            foreach (int id in wb.Civs.Keys) ids.Add(id);
            foreach (int id in ids)
            {
                wa.Civs.TryGetValue(id, out CivSummary ca);
                wb.Civs.TryGetValue(id, out CivSummary cb);
                if (ca == null || cb == null)
                {
                    c.Add(id, (ca ?? cb).Name, ca == null ? "only in B" : "only in A", "", "", "", "", "");
                    differences++;
                    continue;
                }
                bool same = ca.Age == cb.Age && ca.IsAlive == cb.IsAlive && ca.Population == cb.Population && ca.Settlements == cb.Settlements &&
                            ca.Buildings == cb.Buildings && ca.Techs == cb.Techs && ca.TotalResearch.Equals(cb.TotalResearch) && ca.Name == cb.Name;
                if (same) continue;
                differences++;
                c.Add(id, ca.Name == cb.Name ? ca.Name : ca.Name + " > " + cb.Name,
                      ca.Age == cb.Age ? ca.Age.ToString() : ca.Age + ">" + cb.Age,
                      Delta(ca.Population, cb.Population), Delta(ca.Settlements, cb.Settlements), Delta(ca.Buildings, cb.Buildings),
                      Delta(ca.Techs, cb.Techs), TextOut.Signed(cb.TotalResearch - (double)ca.TotalResearch, "0.#"));
            }
            if (c.RowCount == 0) TextOut.Line("  identical");
            else c.Write(TextOut.Out);

            TextOut.Section("State hash sections");
            ulong[] secA = StateHasher.ComputeSections(sa), secB = StateHasher.ComputeSections(sb);
            var h = new TextTable("Section", "A", "B", "").Right();
            int n = Math.Min(secA.Length, secB.Length);
            for (int i = 0; i < n; i++)
            {
                string name = Enum.IsDefined(typeof(StateHashSection), (byte)i) ? ((StateHashSection)(byte)i).ToString() : "section" + i;
                h.Add(name, TextOut.Hash(secA[i]), TextOut.Hash(secB[i]), secA[i] == secB[i] ? "" : "DIFF");
            }
            h.Write(TextOut.Out);

            TextOut.Section("Result");
            TextOut.Field("Hash A", TextOut.Hash(ha));
            TextOut.Field("Hash B", TextOut.Hash(hb));
            if (ha == hb)
            {
                TextOut.Line("IDENTICAL (state hashes match" + (differences > 0 ? "; " + differences + " informational field(s) differ" : "") + ")");
                return ExitCodes.Ok;
            }
            TextOut.Line("DIFFERENT (" + differences + " summarized field(s) differ)");
            if (sa.Seed == sb.Seed && sa.Tick == sb.Tick) TextOut.Line("  same seed and tick: this is a desync.");
            return ExitCodes.Mismatch;
        }

        private static int Row(TextTable t, string field, long a, long b, bool showDelta)
        {
            if (a == b) { t.Add(field, a, b, ""); return 0; }
            t.Add(field, a, b, showDelta ? TextOut.Signed(b - a) : "differs");
            return 1;
        }

        private static int RowText(TextTable t, string field, string a, string b)
        {
            bool same = string.Equals(a, b, StringComparison.Ordinal);
            t.Add(field, a, b, same ? "" : "differs");
            return same ? 0 : 1;
        }

        private static string Delta(int a, int b) => a == b ? a.ToString() : a + ">" + b + " (" + TextOut.Signed(b - a) + ")";
    }
}
