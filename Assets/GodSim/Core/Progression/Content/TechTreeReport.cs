using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.Progression
{
    /// <summary>
    /// Read-only statistics over the technology tree in a <see cref="GameDatabase"/> (frozen or not):
    /// per-age tech counts, root counts, total costs, the cost-to-research-window ratio used for balancing,
    /// and a supply check for early building unlocks.
    /// Intended for tests, tools and debug overlays; never called per tick.
    /// </summary>
    public static class TechTreeReport
    {
        /// <summary>
        /// Research gap an age's techs are balanced against: next threshold minus this threshold. The final age has
        /// no next threshold, so it uses the last real gap (its own threshold minus the previous one); no extra
        /// slack is invented for the endgame. Returns 0 when the needed ages are not registered.
        /// </summary>
        public static float ResearchWindow(GameDatabase db, AgeId age)
        {
            if (db == null) return 0f;
            int i = (int)age;
            if (i < 0 || i >= Counts.Ages) return 0f;
            var ages = db.Ages;
            int lo = i + 1 < Counts.Ages ? i : i - 1;
            if (lo < 0 || ages[lo] == null || ages[lo + 1] == null) return 0f;
            return Math.Max(0f, ages[lo + 1].ResearchThreshold - ages[lo].ResearchThreshold);
        }

        /// <summary>Number of techs (general + racial) registered for an age.</summary>
        public static int CountTechs(GameDatabase db, AgeId age)
        {
            if (db == null) return 0;
            int n = 0;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++) if (techs[i] != null && techs[i].Age == age) n++;
            return n;
        }

        /// <summary>Sum of research costs of all techs registered for an age.</summary>
        public static float TotalCost(GameDatabase db, AgeId age)
        {
            if (db == null) return 0f;
            float sum = 0f;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++) if (techs[i] != null && techs[i].Age == age) sum += techs[i].Cost;
            return sum;
        }

        /// <summary>Sum of research costs of every registered tech.</summary>
        public static float GrandTotalCost(GameDatabase db)
        {
            if (db == null) return 0f;
            float sum = 0f;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++) if (techs[i] != null) sum += techs[i].Cost;
            return sum;
        }

        /// <summary>
        /// True if the tech is an entry point of its age: available to all races and none of its
        /// prerequisite keys names a tech of the same (or a later) age. Dawn roots have no prerequisites.
        /// Unknown prerequisite keys are ignored (Freeze reports them).
        /// </summary>
        public static bool IsRoot(GameDatabase db, TechDefinition tech)
        {
            if (db == null || tech == null || tech.AllowedRaces != RaceMask.All) return false;
            var keys = tech.PrerequisiteKeys ?? new string[0];
            for (int i = 0; i < keys.Length; i++)
            {
                var pre = db.TechByKey(keys[i]);
                if (pre != null && pre.Age >= tech.Age) return false;
            }
            return true;
        }

        /// <summary>Builds the summary of one age.</summary>
        public static AgeTechSummary Summarize(GameDatabase db, AgeId age)
        {
            var s = new AgeTechSummary { Age = age, ResearchWindow = ResearchWindow(db, age) };
            if (db == null) return s;
            bool any = false;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (t == null || t.Age != age) continue;
                s.TechCount++;
                s.TotalCost += t.Cost;
                if (t.AllowedRaces == RaceMask.All) { s.GeneralTechCount++; s.GeneralCost += t.Cost; }
                else s.RacialTechCount++;
                if (IsRoot(db, t)) s.RootCount++;
                if (!any) { s.MinCost = t.Cost; s.MaxCost = t.Cost; any = true; }
                else { s.MinCost = Math.Min(s.MinCost, t.Cost); s.MaxCost = Math.Max(s.MaxCost, t.Cost); }
            }
            s.CostToWindowRatio = s.ResearchWindow > 0f ? s.TotalCost / s.ResearchWindow : 0f;
            return s;
        }

        /// <summary>Builds the summaries of all ten ages, indexed by AgeId.</summary>
        public static AgeTechSummary[] Summarize(GameDatabase db)
        {
            var result = new AgeTechSummary[Counts.Ages];
            for (int i = 0; i < result.Length; i++) result[i] = Summarize(db, (AgeId)i);
            return result;
        }

        /// <summary>Summaries of the shipped catalogs (AgeCatalog + TechCatalog) in a scratch, unfrozen database.</summary>
        public static AgeTechSummary[] SummarizeCatalog()
        {
            var db = new GameDatabase();
            AgeCatalog.Register(db);
            TechCatalog.Register(db);
            return Summarize(db);
        }

        /// <summary>
        /// Checks every <see cref="TechEffectKind.UnlockBuilding"/> effect against the registered buildings. An early
        /// unlock is only useful if the civilization can pay for the target and run it in the tech's age. So every
        /// resource in the target's Cost or InputsPerWorker must be an output (OutputsPerWorker) of another building
        /// the researching civilization can already build in that age. That building must be allowed for every race
        /// that may research the tech, must not be past its MaxAge, and must either have a MinAge no later than the
        /// tech's age or be unlocked by the tech itself or by one of its prerequisites (transitively). Unlocks from
        /// techs that are not ancestors do not count, because the civilization may not know them yet.
        /// Returns one human-readable problem per unregistered target or missing resource; an empty list means
        /// every early unlock can be paid for and run. Works on frozen and unfrozen databases: prerequisites are
        /// followed by key, and unknown keys are ignored (Freeze reports them).
        /// </summary>
        public static List<string> FindUnsuppliedUnlocks(GameDatabase db)
        {
            var problems = new List<string>();
            if (db == null) return problems;
            var techs = db.Techs;
            var granted = new bool[Counts.Buildings];
            var visited = new bool[techs.Count];
            var stack = new Stack<TechDefinition>();
            for (int i = 0; i < techs.Count; i++)
            {
                var tech = techs[i];
                if (tech == null || !HasUnlock(tech)) continue;
                CollectAncestorUnlocks(db, tech, granted, visited, stack);
                for (int e = 0; e < tech.Effects.Length; e++)
                {
                    var fx = tech.Effects[e];
                    if (fx.Kind != TechEffectKind.UnlockBuilding) continue;
                    if (!db.TryGetBuilding(fx.Building, out var target))
                    {
                        problems.Add(tech.Key + " unlocks " + fx.Building + ", which is not registered.");
                        continue;
                    }
                    CheckSupplied(db, tech, target, target.Cost, "cost", granted, problems);
                    CheckSupplied(db, tech, target, target.InputsPerWorker, "input", granted, problems);
                }
            }
            return problems;
        }

        private static bool HasUnlock(TechDefinition tech)
        {
            if (tech.Effects == null) return false;
            for (int i = 0; i < tech.Effects.Length; i++) if (tech.Effects[i].Kind == TechEffectKind.UnlockBuilding) return true;
            return false;
        }

        // Marks in `granted` every building unlocked by the tech or by any of its (transitive) prerequisites.
        private static void CollectAncestorUnlocks(GameDatabase db, TechDefinition tech, bool[] granted, bool[] visited, Stack<TechDefinition> stack)
        {
            Array.Clear(granted, 0, granted.Length);
            Array.Clear(visited, 0, visited.Length);
            stack.Clear();
            stack.Push(tech);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t.Effects != null)
                    for (int e = 0; e < t.Effects.Length; e++)
                    {
                        int b = (int)t.Effects[e].Building;
                        if (t.Effects[e].Kind == TechEffectKind.UnlockBuilding && b >= 0 && b < granted.Length) granted[b] = true;
                    }
                var keys = t.PrerequisiteKeys;
                if (keys == null) continue;
                for (int k = 0; k < keys.Length; k++)
                {
                    int id = db.TechIdOf(keys[k]);
                    if (id < 0 || id >= visited.Length || visited[id]) continue; // unknown key, or already walked (also stops cycles)
                    visited[id] = true;
                    var pre = db.Tech(id);
                    if (pre != null) stack.Push(pre);
                }
            }
        }

        private static void CheckSupplied(GameDatabase db, TechDefinition tech, BuildingDefinition target, ResourceAmount[] needs,
            string what, bool[] granted, List<string> problems)
        {
            if (needs == null) return;
            for (int i = 0; i < needs.Length; i++)
            {
                if (!(needs[i].Amount > 0f)) continue;
                var r = needs[i].Type;
                if (IsProduced(db, tech, target.Type, r, granted)) continue;
                problems.Add(tech.Key + " unlocks " + target.Type + " in the " + tech.Age + " age, but nothing it can build by then produces "
                    + r + " (" + what + ").");
            }
        }

        private static bool IsProduced(GameDatabase db, TechDefinition tech, BuildingType target, ResourceType resource, bool[] granted)
        {
            var buildings = db.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null || b.Type == target || b.OutputsPerWorker == null) continue;
                if ((b.AllowedRaces & tech.AllowedRaces) != tech.AllowedRaces) continue; // every researching race must be able to build it
                if (b.MaxAge < tech.Age) continue;
                if (b.MinAge > tech.Age && !granted[i]) continue;
                for (int o = 0; o < b.OutputsPerWorker.Length; o++)
                    if (b.OutputsPerWorker[o].Type == resource && b.OutputsPerWorker[o].Amount > 0f) return true;
            }
            return false;
        }

        /// <summary>Human-readable table: one line per age plus a total line. Uses only integer formatting (culture-safe).</summary>
        public static string Format(GameDatabase db)
        {
            var sb = new StringBuilder();
            var rows = Summarize(db);
            int count = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                count += r.TechCount;
                string name = db != null && db.Ages[i] != null && !string.IsNullOrEmpty(db.Ages[i].Name) ? db.Ages[i].Name : r.Age.ToString();
                sb.Append(name).Append(": ").Append(r.TechCount).Append(" techs (")
                  .Append(r.GeneralTechCount).Append(" general, ").Append(r.RacialTechCount).Append(" racial, ")
                  .Append(r.RootCount).Append(" roots), cost ").Append(Round(r.TotalCost))
                  .Append(" [").Append(Round(r.MinCost)).Append("-").Append(Round(r.MaxCost)).Append("]")
                  .Append(" / window ").Append(Round(r.ResearchWindow))
                  .Append(" = ").Append(Round(r.CostToWindowRatio * 100f)).Append("%\n");
            }
            sb.Append("Total: ").Append(count).Append(" techs, cost ").Append(Round(GrandTotalCost(db))).Append('\n');
            return sb.ToString();
        }

        private static int Round(float v) => MathUtil.RoundToInt(MathUtil.Sanitize(v));
    }
}
