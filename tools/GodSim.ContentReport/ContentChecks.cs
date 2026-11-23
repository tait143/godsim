using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.ContentReport
{
    /// <summary>
    /// Content and balance checks. Errors mean the content is broken for play (CI fails on them with --check);
    /// warnings are likely mistakes or balance smells; infos are notes for designers.
    /// </summary>
    internal sealed partial class ContentModel
    {
        private void RunChecks()
        {
            foreach (var e in Load.CatalogErrors) Add(Severity.Error, "Load", e);
            foreach (var e in Load.ValidationErrors) Add(Severity.Error, "Validation", e);
            CheckTechs();
            CheckUnlocks();
            CheckAges();
            CheckBuildings();
            CheckResources();
            CheckResourceTiming();
            CheckServices();
            CheckRaces();
            CheckPowers();
            CheckBalance();
        }

        private void CheckTechs()
        {
            foreach (var t in Techs)
            {
                bool anyone = false;
                var blocked = new List<string>();
                for (int r = 0; r < NumRaces; r++)
                {
                    if (RaceDef(r) == null) continue;
                    if (TechFirstAge[r, t.Id] >= 0) anyone = true;
                    else if (t.AllowedRaces.Allows((RaceId)r)) blocked.Add(RaceName(r));
                }
                if (!anyone)
                    Add(Severity.Error, "Techs", "`" + t.Key + "` can never be researched by any race (allowed: " + RacesText(t.AllowedRaces) + "; check its prerequisite chain).");
                else if (blocked.Count > 0)
                    Add(Severity.Warning, "Techs", "`" + t.Key + "` is allowed for " + string.Join(", ", blocked) + " but its prerequisites are not reachable for them.");
                if (t.Effects == null || t.Effects.Length == 0)
                    Add(Severity.Warning, "Techs", "`" + t.Key + "` has no effects.");
                if (string.IsNullOrEmpty(t.Name)) Add(Severity.Warning, "Techs", "`" + t.Key + "` has no display name.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var k in t.PrerequisiteKeys ?? new string[0])
                    if (!seen.Add(k)) Add(Severity.Warning, "Techs", "`" + t.Key + "` lists prerequisite `" + k + "` twice.");
            }
        }

        private void CheckUnlocks()
        {
            foreach (var t in Techs)
            {
                foreach (var e in t.Effects ?? new TechEffect[0])
                {
                    if (e.Kind != TechEffectKind.UnlockBuilding) continue;
                    var b = BuildingDef(e.Building);
                    if (b == null) { Add(Severity.Error, "Unlocks", "`" + t.Key + "` unlocks unregistered building " + e.Building + "."); continue; }
                    if (t.Age >= b.MinAge)
                        Add(Severity.Warning, "Unlocks", "`" + t.Key + "` (" + t.Age + ") unlocks " + b.Name + ", which is already available from " + b.MinAge + " (redundant unlock).");
                    if (t.Age > b.MaxAge)
                        Add(Severity.Warning, "Unlocks", "`" + t.Key + "` (" + t.Age + ") unlocks " + b.Name + ", which is obsolete after " + b.MaxAge + ".");
                    if ((t.AllowedRaces & b.AllowedRaces & RaceMask.All) == RaceMask.None)
                        Add(Severity.Warning, "Unlocks", "`" + t.Key + "` (" + RacesText(t.AllowedRaces) + ") unlocks " + b.Name + " (" + RacesText(b.AllowedRaces) + "): no race can use the unlock.");
                }
            }
        }

        private void CheckAges()
        {
            for (int a = 1; a < NumAges; a++)
            {
                var def = AgeDef(a);
                if (def == null) continue;
                int req = def.RequiredTechsFromPreviousAge;
                for (int r = 0; r < NumRaces; r++)
                {
                    if (RaceDef(r) == null) continue;
                    int have = ResearchableCount(r, a - 1);
                    if (req > have)
                        Add(Severity.Error, "Ages", RaceName(r) + " can never enter " + def.Name + ": it needs " + req + " " + AgeName(a - 1) + " techs but only " + have + " are researchable.");
                }
                if (TechsOf(a).Count == 0) Add(Severity.Warning, "Ages", def.Name + " has no technologies.");
            }
            for (int a = 1; a < NumAges; a++)
            {
                var prev = AgeDef(a - 1); var cur = AgeDef(a);
                if (prev == null || cur == null) continue;
                if (cur.MinPopulation < prev.MinPopulation)
                    Add(Severity.Warning, "Ages", cur.Name + " needs less population (" + cur.MinPopulation + ") than " + prev.Name + " (" + prev.MinPopulation + ").");
                if (cur.ResearchThreshold <= prev.ResearchThreshold)
                    Add(Severity.Warning, "Ages", cur.Name + " research threshold does not increase over " + prev.Name + ".");
            }
        }

        private void CheckBuildings()
        {
            foreach (var b in Buildings)
            {
                if (FirstAgeAnyRace(b) < 0)
                    Add(Severity.Error, "Buildings", b.Name + " is unavailable to every race (races: " + RacesText(b.AllowedRaces) + ", window " + b.MinAge + "–" + b.MaxAge + ").");
                bool hasOutputs = (b.OutputsPerWorker ?? ResourceAmount.None).Length > 0;
                bool hasServices = (b.Services ?? ServiceAmount.None).Length > 0;
                if (b.HasJobs && !hasOutputs && !hasServices && b.Job != JobType.Soldier && b.Job != JobType.Builder)
                    Add(Severity.Warning, "Buildings", b.Name + " employs " + b.JobSlots + " " + b.Job + "(s) but produces nothing and provides no service.");
                if (!b.HasJobs && (hasOutputs || (b.InputsPerWorker ?? ResourceAmount.None).Length > 0))
                    Add(Severity.Warning, "Buildings", b.Name + " has per-worker inputs/outputs but no job slots, so it never produces.");
                if (b.IsWonder != (b.Category == BuildingCategory.Wonder))
                    Add(Severity.Info, "Buildings", b.Name + ": IsWonder=" + b.IsWonder + " but category is " + b.Category + ".");
                if (b.IsWonder && !b.UniquePerCivilization)
                    Add(Severity.Warning, "Buildings", b.Name + " is a wonder but not unique per civilization.");
                if ((b.Cost ?? ResourceAmount.None).Length == 0 && b.Type != BuildingType.TownCenter)
                    Add(Severity.Warning, "Buildings", b.Name + " costs nothing.");
                if (b.Desirability < -1f || b.Desirability > 1f)
                    Add(Severity.Warning, "Buildings", b.Name + " desirability " + Md.N(b.Desirability) + " is outside -1..1.");
                foreach (var o in b.OutputsPerWorker ?? ResourceAmount.None)
                    if (InputPerWorker(b, o.Type) >= o.Amount && o.Amount > 0f)
                        Add(Severity.Warning, "Buildings", b.Name + " consumes at least as much " + o.Type + " as it produces.");

                if (OutputPerWorker(b, ResourceType.Food) > 0f)
                {
                    float net = OutputPerWorker(b, ResourceType.Food) - InputPerWorker(b, ResourceType.Food);
                    for (int r = 0; r < NumRaces; r++)
                    {
                        var race = RaceDef(r);
                        if (race == null || BuildingFirstAge[r, (int)b.Type] < 0 || race.FoodPerTick <= 0f) continue;
                        float fed = net * race.ProductionMultiplier[(int)ResourceType.Food] / race.FoodPerTick;
                        if (fed < 1f)
                            Add(Severity.Warning, "Balance", "A " + race.Name + " " + b.Name + " worker feeds only " + Md.N(fed) + " people (cannot feed themselves).");
                    }
                }
            }

            // Jobs that no building offers (Builder comes from construction).
            for (int j = 1; j < Counts.Jobs; j++)
            {
                var job = (JobType)j;
                if (job == JobType.Builder) continue;
                bool any = false;
                foreach (var b in Buildings) if (b.HasJobs && b.Job == job) { any = true; break; }
                if (!any) Add(Severity.Info, "Jobs", "No building employs " + job + ".");
            }
        }

        private void CheckResources()
        {
            for (int res = 0; res < Counts.Resources; res++)
            {
                var rt = (ResourceType)res;
                bool produced = Producers[res].Count > 0 || (rt == ResourceType.Gold && ServiceProviders[(int)ServiceType.Commerce].Count > 0) || rt == ResourceType.Food;
                bool consumed = InputUsers[res].Count > 0 || CostUsers[res].Count > 0 || rt == ResourceType.Food;
                if (rt == ResourceType.Gold) foreach (var b in Buildings) if (b.UpkeepGold > 0f) { consumed = true; break; }
                if (!produced && consumed)
                    Add(Severity.Error, "Resources", rt + " is required by " + Names(Union(InputUsers[res], CostUsers[res]), 6) + " but no building produces it.");
                else if (!produced && !consumed)
                    Add(Severity.Warning, "Resources", rt + " is never produced and never consumed (dead resource).");
                else if (produced && !consumed)
                    Add(Severity.Warning, "Resources", rt + " is produced by " + Names(Producers[res], 6) + " but nothing consumes it (no worker input, construction cost or upkeep)" +
                        (rt == ResourceType.Weapons ? "; Diplomacy only checks the stockpile for arming soldiers, so it accumulates up to storage capacity." : "."));
                else if (InputUsers[res].Count == 0 && CostUsers[res].Count > 0 && rt != ResourceType.Food && rt != ResourceType.Gold)
                    Add(Severity.Info, "Resources", rt + " is only used for construction costs (no building consumes it as a worker input).");
            }
        }

        /// <summary>A race can build B at age A but cannot produce one of B's cost/input resources until later (or never).</summary>
        private void CheckResourceTiming()
        {
            foreach (var b in Buildings)
            {
                CheckTiming(b, b.InputsPerWorker, "input");
                CheckTiming(b, b.Cost, "cost");
            }
        }

        private void CheckTiming(BuildingDefinition b, ResourceAmount[] list, string kind)
        {
            foreach (var x in list ?? ResourceAmount.None)
            {
                if (x.Amount <= 0f) continue;
                var never = new List<string>();
                var late = new List<string>();
                int lateFrom = -1, lateTo = -1;
                for (int r = 0; r < NumRaces; r++)
                {
                    int fa = BuildingFirstAge[r, (int)b.Type];
                    if (fa < 0) continue;
                    int pa = ResourceFirstAge[r, (int)x.Type];
                    if (pa < 0) never.Add(RaceName(r));
                    else if (pa > fa) { late.Add(RaceName(r)); lateFrom = fa; lateTo = Math.Max(lateTo, pa); }
                }
                if (never.Count > 0)
                    Add(Severity.Warning, "Supply", b.Name + " needs " + x.Type + " (" + kind + ") but " + string.Join(", ", never) + " can never produce " + x.Type + ".");
                if (late.Count > 0)
                    Add(kind == "input" ? Severity.Warning : Severity.Info, "Supply",
                        b.Name + " is available from " + AgeName(lateFrom) + " but its " + kind + " " + x.Type + " is first producible in " + AgeName(lateTo) + " (" + string.Join(", ", late) + ").");
            }
        }

        private void CheckServices()
        {
            for (int s = 0; s < Counts.Services; s++)
                if (ServiceProviders[s].Count == 0)
                    Add(Severity.Warning, "Services", "No building provides the " + (ServiceType)s + " service.");
        }

        private void CheckRaces()
        {
            foreach (var race in Races)
            {
                int r = (int)race.Id;
                foreach (var u in race.UniqueBuildings ?? new BuildingType[0])
                {
                    var b = BuildingDef(u);
                    if (b == null) Add(Severity.Error, "Races", race.Name + " lists unregistered unique building " + u + ".");
                    else if (BuildingFirstAge[r, (int)u] < 0) Add(Severity.Error, "Races", race.Name + " unique building " + b.Name + " is not available to " + race.Name + ".");
                    else if (b.AllowedRaces != race.Id.ToMask()) Add(Severity.Warning, "Races", race.Name + " unique building " + b.Name + " is also allowed for " + RacesText(b.AllowedRaces) + ".");
                }
                foreach (var f in race.FavoredBuildings ?? new BuildingType[0])
                {
                    var b = BuildingDef(f);
                    if (b == null || BuildingFirstAge[r, (int)f] < 0)
                        Add(Severity.Warning, "Races", race.Name + " favors " + (b != null ? b.Name : f.ToString()) + " but can never build it.");
                }
                float best = 0f;
                for (int i = 0; i < race.BiomeHabitability.Length; i++) best = Math.Max(best, race.BiomeHabitability[i]);
                if (best < 0.6f) Add(Severity.Warning, "Races", race.Name + " has no biome with habitability >= 0.6.");
                if (race.RaceAffinity != null && r < race.RaceAffinity.Length && race.RaceAffinity[r] < 0.5f)
                    Add(Severity.Warning, "Races", race.Name + " self-affinity is " + Md.N(race.RaceAffinity[r]) + " (expected ~+1).");
                for (int o = 0; o < NumRaces; o++)
                {
                    var other = RaceDef(o);
                    if (other == null || o <= r || race.RaceAffinity == null || other.RaceAffinity == null) continue;
                    float d = Math.Abs(race.RaceAffinity[o] - other.RaceAffinity[r]);
                    if (d >= 0.75f)
                        Add(Severity.Info, "Races", "Asymmetric affinity " + race.Name + "→" + other.Name + " " + Md.Signed(race.RaceAffinity[o]) + " vs " + other.Name + "→" + race.Name + " " + Md.Signed(other.RaceAffinity[r]) + ".");
                }
            }
        }

        private void CheckPowers()
        {
            foreach (var p in Powers)
            {
                if (p.Category == GodPowerCategory.Wrath && p.AlignmentDelta > 0f)
                    Add(Severity.Warning, "God powers", p.Name + " is Wrath but shifts alignment towards benevolent (" + Md.Signed(p.AlignmentDelta, 3) + ").");
                if (p.Category == GodPowerCategory.Blessing && p.AlignmentDelta < 0f)
                    Add(Severity.Warning, "God powers", p.Name + " is a Blessing but shifts alignment towards wrathful (" + Md.Signed(p.AlignmentDelta, 3) + ").");
                if (p.UsesRaceParameter && p.Type != GodPowerType.SpawnTribe)
                    Add(Severity.Info, "God powers", p.Name + " uses a race parameter.");
            }
        }

        private static List<BuildingDefinition> Union(List<BuildingDefinition> a, List<BuildingDefinition> b)
        {
            var l = new List<BuildingDefinition>(a);
            foreach (var x in b) if (!l.Contains(x)) l.Add(x);
            return l;
        }

        public static string Names(List<BuildingDefinition> list, int max)
        {
            var parts = new List<string>();
            for (int i = 0; i < list.Count && i < max; i++) parts.Add(list[i].Name);
            if (list.Count > max) parts.Add("+" + (list.Count - max) + " more");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }
    }
}
