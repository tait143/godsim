using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.ContentReport
{
    internal sealed partial class MarkdownReport
    {
        // ------------------------------------------------------------------ god powers
        private void GodPowers()
        {
            H(2, "God powers");
            if (_m.Powers.Count == 0) { P("_No god powers registered._"); return; }
            var t = new MdTable("Power", "Category", ">Mana", ">Cooldown (ticks)", ">≈ Game time", "^Radius", ">Alignment Δ", "^Leaning", "Requirements");
            foreach (var p in _m.Powers)
            {
                var req = new List<string>();
                if (p.RequiresLandTarget) req.Add("land target");
                if (p.RequiresSettlementInRange) req.Add("settlement in range");
                if (p.UsesRaceParameter) req.Add("race parameter");
                if (p.MinWorldAge != AgeId.Dawn) req.Add("world age ≥ " + Age(p.MinWorldAge));
                string lean = p.AlignmentDelta > 0f ? "benevolent" : p.AlignmentDelta < 0f ? "wrathful" : "neutral";
                t.Row("**" + p.Name + "**", p.Category, Md.N(p.ManaCost, 1), p.CooldownTicks, CooldownText(p.CooldownTicks),
                    p.MinRadius == p.MaxRadius ? p.MinRadius.ToString() : p.MinRadius + "–" + p.MaxRadius, Md.Signed(p.AlignmentDelta, 3), lean, Md.Join(req));
            }
            Table(t);

            var cats = new MdTable("Category", ">Powers", ">Avg mana", ">Avg cooldown (ticks)", ">Total alignment Δ");
            foreach (GodPowerCategory c in Enum.GetValues(typeof(GodPowerCategory)))
            {
                int n = 0; float mana = 0f, cd = 0f, al = 0f;
                foreach (var p in _m.Powers) if (p.Category == c) { n++; mana += p.ManaCost; cd += p.CooldownTicks; al += p.AlignmentDelta; }
                cats.Row(c, n, n > 0 ? Md.N(mana / n, 1) : "—", n > 0 ? Md.N(cd / n, 0) : "—", n > 0 ? Md.Signed(al, 3) : "—");
            }
            Table(cats);

            H(3, "Power descriptions");
            foreach (var p in _m.Powers) Line("- **" + p.Name + "** (" + p.Category + "): " + Md.Cell(p.Description));
            Line("");
        }

        private static string CooldownText(int ticks)
        {
            if (ticks < SimTime.TicksPerYear) return ticks + " d";
            return Md.N(ticks / (double)SimTime.TicksPerYear, 1) + " y";
        }

        // ------------------------------------------------------------------ balance
        private void Balance()
        {
            H(2, "Balance sanity");
            P("Computed from the data with base rates (no weather, no tier/age bonuses unless stated). Availability assumes a civilization that researched every tech it could in each age.");
            FoodBalance();
            ModifierCeilings();
            ResearchBalance();
            CostGrowth();
            Warnings();
        }

        private void FoodBalance()
        {
            H(3, "Food per worker vs consumption");
            var producers = new List<BuildingDefinition>();
            foreach (var b in SortedBuildings()) if (b.HasJobs && ContentModel.OutputPerWorker(b, ResourceType.Food) > 0f) producers.Add(b);
            if (producers.Count == 0) { P("_No building produces food._"); return; }

            var headers = new List<string> { "Building", "Age", ">Slots", ">Food / worker", "Inputs / worker" };
            foreach (var r in _m.Races) headers.Add(">" + r.Name);
            headers.Add(">Fed / building (Human)");
            var t = new MdTable(headers.ToArray());
            foreach (var b in producers)
            {
                float net = ContentModel.OutputPerWorker(b, ResourceType.Food) - ContentModel.InputPerWorker(b, ResourceType.Food);
                var row = new List<object> { "**" + b.Name + "**", Age(b.MinAge) + (b.MaxAge != AgeId.Ascension ? "–" + Age(b.MaxAge) : "+"), b.JobSlots, Md.N(net, 3), Amounts(b.InputsPerWorker) };
                foreach (var r in _m.Races)
                {
                    if (_m.BuildingFirstAge[(int)r.Id, (int)b.Type] < 0) { row.Add("·"); continue; }
                    float fed = r.FoodPerTick > 0f ? net * r.ProductionMultiplier[(int)ResourceType.Food] / r.FoodPerTick : 0f;
                    row.Add(fed < 1f ? "**" + Md.F(fed, 1) + "**" : Md.F(fed, 1));
                }
                var human = _m.RaceDef((int)RaceId.Human) ?? (_m.Races.Count > 0 ? _m.Races[0] : null);
                float perBuilding = human != null && human.FoodPerTick > 0f ? net * b.JobSlots * human.ProductionMultiplier[(int)ResourceType.Food] / human.FoodPerTick : 0f;
                row.Add(Md.N(perBuilding, 1));
                t.Row(row.ToArray());
            }
            Table(t);
            P("Race columns: people fed per worker = net food per worker × race food multiplier ÷ race `FoodPerTick` (· = race cannot build it). " +
              "Bold values below 1 mean a worker cannot even feed themselves. With roughly half of a population being working adults, a food building needs ≥ 2 to sustain a town on its own. " +
              "Idle adults also forage a little (Economy rule), which is not shown here.");

            var share = new MdTable("Race", ">Food / person / yr", ">Adults (est.)", "Founding, no techs", ">Adults on food", "Dawn with techs", ">Adults on food ", "Best overall", ">Adults on food  ", ">Forage feeds");
            foreach (var r in _m.Races)
            {
                var f0 = _m.BestFood(r, 0); var f1 = _m.BestFood(r, 1); var f2 = _m.BestFood(r, 2);
                share.Row("**" + r.Name + "**", Md.N(r.FoodPerTick * SimTime.TicksPerYear, 1), Md.N(ContentModel.AdultFraction(r) * 100, 0) + "%",
                    FoodPickText(f0), SharePct(f0), FoodPickText(f1), SharePct(f1), FoodPickText(f2), SharePct(f2),
                    r.FoodPerTick > 0f ? Md.F(SystemRules.EarlyForagePerTick / r.FoodPerTick, 2) : "—");
            }
            Table(share);
            P("*Adults (est.)* = (life expectancy − adulthood) ÷ life expectancy. *Adults on food* = share of adults that must work in that building to feed the whole population " +
              "(bold above 60%). *Forage feeds* = people fed by one idle adult foraging in Dawn–Bronze (" + Md.N(SystemRules.EarlyForagePerTick, 3) + " food/tick, Economy rule; " +
              Md.N(SystemRules.LateForagePerTick, 3) + " later).");
        }

        private static string FoodPickText(FoodPick f) => f.Building != null ? f.Building.Name + " (" + Md.F(f.FedPerWorker, 1) + ")" : "—";

        private static string SharePct(FoodPick f)
        {
            if (f.Building == null) return "—";
            string s = Md.N(f.AdultShare * 100, 0) + "%";
            return f.AdultShare > 0.6f ? "**" + s + "**" : s;
        }

        /// <summary>Sum of general-tech bonuses known by the end of each age plus that age's own bonus.</summary>
        private void ModifierCeilings()
        {
            H(3, "Modifier ceilings per age");
            var t = new MdTable("Age", ">Food prod.", ">All prod.", ">Research", ">Growth", ">Build speed", ">Faith", ">Military", ">Storage", ">Trade", ">Health", ">Happiness", ">Lifespan");
            var sum = new float[Enum.GetValues(typeof(TechEffectKind)).Length];
            float food = 0f;
            for (int a = 0; a < _m.NumAges; a++)
            {
                foreach (var tech in _m.TechsOf(a))
                {
                    if (tech.AllowedRaces != RaceMask.All) continue;
                    foreach (var e in tech.Effects ?? new TechEffect[0])
                    {
                        if (e.Kind == TechEffectKind.ProductionMultiplier) { if (e.Resource == ResourceType.Food) food += e.Value; }
                        else sum[(int)e.Kind] += e.Value;
                    }
                }
                var def = _m.AgeDef(a);
                float ageProd = def != null ? def.ProductionBonus : 0f, ageRes = def != null ? def.ResearchBonus : 0f;
                float ageLife = def != null ? def.LifespanBonusYears : 0f, ageHealth = def != null ? def.HealthBonus : 0f;
                t.Row("**" + Age(a) + "**", Md.Pct(food + sum[(int)TechEffectKind.AllProductionMultiplier] + ageProd), Md.Pct(sum[(int)TechEffectKind.AllProductionMultiplier] + ageProd),
                    Md.Pct(sum[(int)TechEffectKind.ResearchMultiplier] + ageRes), Md.Pct(sum[(int)TechEffectKind.GrowthMultiplier]), Md.Pct(sum[(int)TechEffectKind.BuildSpeedMultiplier]),
                    Md.Pct(sum[(int)TechEffectKind.FaithMultiplier]), Md.Pct(sum[(int)TechEffectKind.MilitaryMultiplier]), Md.Pct(sum[(int)TechEffectKind.StorageMultiplier]),
                    Md.Pct(sum[(int)TechEffectKind.TradeMultiplier]), Md.Signed(sum[(int)TechEffectKind.HealthBonus] + ageHealth), Md.Signed(sum[(int)TechEffectKind.HappinessBonus]),
                    "+" + Md.N(sum[(int)TechEffectKind.LifespanBonus] + ageLife, 1) + " y");
            }
            Table(t);
            P("Additive sum of every general (all-race) tech of that age and earlier, plus the age's own bonus. Racial techs and race aptitudes come on top.");
        }

        private void ResearchBalance()
        {
            H(3, "Research per age vs thresholds");
            var t = new MdTable("Age", ">Enter at", ">Gap to next", ">Tech cost (general)", ">Cost ÷ gap", ">Required next", ">Cheapest required",
                "Best research building", ">RP / tick", ">Ref. town RP / tick", ">Years (base)", ">Years (ceiling)");
            for (int a = 0; a < _m.NumAges; a++)
            {
                var def = _m.AgeDef(a);
                if (def == null) continue;
                var next = _m.AgeDef(a + 1);
                float gap = next != null ? next.ResearchThreshold - def.ResearchThreshold : float.NaN;
                var costs = new List<float>();
                float general = 0f;
                foreach (var tech in _m.TechsOf(a)) if (tech.AllowedRaces == RaceMask.All) { general += tech.Cost; costs.Add(tech.Cost); }
                costs.Sort();
                int req = next != null ? next.RequiredTechsFromPreviousAge : 0;
                float cheapest = 0f;
                for (int i = 0; i < req && i < costs.Count; i++) cheapest += costs[i];

                BuildingDefinition best = null; float bestRp = 0f;
                foreach (var b in _m.Buildings)
                {
                    if (b.IsWonder) continue;
                    bool avail = false;
                    for (int r = 0; r < _m.NumRaces; r++) if (_m.Available[r, a, (int)b.Type]) { avail = true; break; }
                    if (!avail) continue;
                    float rp = ContentModel.ServicePoints(b, ServiceType.Research);
                    if (rp > bestRp) { bestRp = rp; best = b; }
                }
                double years = _m.YearsInAge(a, out float townRp, out int pop, out int nb);
                double ceiling = double.IsNaN(years) ? double.NaN : years / (1.0 + Math.Max(0f, _m.ResearchCeiling(a)));
                t.Row("**" + def.Name + "**", Md.N(def.ResearchThreshold, 0), float.IsNaN(gap) ? "—" : Md.N(gap, 0), Md.N(general, 0),
                    float.IsNaN(gap) || gap <= 0f ? "—" : Md.F(general / gap, 2), next != null ? req + " of " + costs.Count : "—",
                    next != null ? Md.N(cheapest, 0) : "—", best != null ? best.Name : "—", bestRp > 0f ? Md.N(bestRp, 2) : "—",
                    townRp > 0f ? Md.N(townRp, 2) + " (pop " + pop + ", " + nb + " bldgs)" : "—",
                    double.IsNaN(years) ? "—" : YearsText(years), double.IsNaN(ceiling) ? "—" : YearsText(ceiling));
            }
            Table(t);
            P("*Gap to next* = research between this age's threshold and the next. *Cost ÷ gap* > 1 means the age's general tree cannot be finished before the next threshold. " +
              "*Ref. town* = Town Center + one of each general (non-wonder) research building available in the age + " + Md.N(SystemRules.ResearchPerResident, 3) +
              " RP per resident (Progression rule) for the next age's minimum population. *Years (base)* = years for that town to bridge the gap without multipliers; " +
              "*Years (ceiling)* applies the age's research-bonus ceiling. Design target: ~" + Md.N(SystemRules.MinAgeYears, 0) + "–" + Md.N(SystemRules.MaxAgeYears, 0) +
              " years per age (bold = under half or over twice that).");
        }

        private static string YearsText(double y)
        {
            string s = Md.N(y, 1);
            return y < SystemRules.MinAgeYears / 2 || y > SystemRules.MaxAgeYears * 2 ? "**" + s + "**" : s;
        }

        private void CostGrowth()
        {
            H(3, "Cost growth per age");
            var t = new MdTable("Age", ">Buildings", ">Avg cost", ">Max cost", "Most expensive", ">Avg work", ">Avg upkeep", ">Cost growth", ">Avg tech cost", ">Tech growth", "Cost resources");
            float prevCost = 0f, prevTech = 0f;
            for (int a = 0; a < _m.NumAges; a++)
            {
                int n = 0; float cost = 0f, work = 0f, upkeep = 0f, max = 0f; BuildingDefinition maxB = null;
                var res = new SortedSet<int>();
                foreach (var b in _m.Buildings)
                {
                    if ((int)b.MinAge != a || b.IsWonder) continue;
                    n++;
                    float c = ContentModel.TotalCost(b);
                    cost += c; work += b.BuildWork; upkeep += b.UpkeepGold;
                    if (c > max) { max = c; maxB = b; }
                    foreach (var x in b.Cost ?? ResourceAmount.None) res.Add((int)x.Type);
                }
                var techs = _m.TechsOf(a);
                float tc = 0f; foreach (var tech in techs) tc += tech.Cost;
                float avgTech = techs.Count > 0 ? tc / techs.Count : 0f;
                float avgCost = n > 0 ? cost / n : 0f;
                var resNames = new List<string>(); foreach (int r in res) resNames.Add(((ResourceType)r).ToString());
                t.Row("**" + Age(a) + "**", n, n > 0 ? Md.N(avgCost, 0) : "—", n > 0 ? Md.N(max, 0) : "—", maxB != null ? maxB.Name : "—",
                    n > 0 ? Md.N(work / n, 0) : "—", n > 0 ? Md.N(upkeep / n, 3) : "—",
                    prevCost > 0f && n > 0 ? "×" + Md.F(avgCost / prevCost, 2) : "—", techs.Count > 0 ? Md.N(avgTech, 0) : "—",
                    prevTech > 0f && techs.Count > 0 ? "×" + Md.F(avgTech / prevTech, 2) : "—", Md.Join(resNames));
                if (n > 0) prevCost = avgCost;
                if (techs.Count > 0) prevTech = avgTech;
            }
            Table(t);
            P("Building costs are the sum of all resource units, excluding wonders, grouped by `MinAge`. Growth is relative to the previous age that introduced buildings/techs.");
        }

        private void Warnings()
        {
            H(3, "Warnings");
            P("Detected automatically. **Errors** break play and fail `--check`; warnings are likely mistakes or balance smells; notes are for designers.");
            if (_m.Findings.Count == 0) { P("No problems found."); return; }
            var t = new MdTable("Severity", "Area", "Finding");
            foreach (var f in _m.Findings)
            {
                string sev = f.Severity == Severity.Error ? "**ERROR**" : f.Severity == Severity.Warning ? "warning" : "note";
                t.Row(sev, f.Area, f.Message);
            }
            Table(t);
        }
    }
}
