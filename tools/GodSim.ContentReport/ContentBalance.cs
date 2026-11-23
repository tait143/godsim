using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.ContentReport
{
    /// <summary>
    /// Simulation rules that live in systems code (not in the content data) but matter for balance math.
    /// Mirrored here because the content-only check build does not compile the systems; keep in sync.
    /// </summary>
    internal static class SystemRules
    {
        /// <summary>Progression.TechQueries.ResearchPerResident: research per resident per tick.</summary>
        public const float ResearchPerResident = 0.004f;
        /// <summary>Progression.TechQueries.ResearchPerServicePoint: research points per Research service point.</summary>
        public const float ResearchPerServicePoint = 0.4f;
        /// <summary>Economy.ProductionSystem.EarlyForagePerTick (Dawn–Bronze) / LateForagePerTick: food per idle adult per tick.</summary>
        public const float EarlyForagePerTick = 0.035f;
        public const float LateForagePerTick = 0.015f;
        /// <summary>Economy.ServiceSystem.GoldPerCommercePoint: gold per Commerce point per tick.</summary>
        public const float GoldPerCommercePoint = 0.02f;
        /// <summary>Design target from the engineering contract: ages last ~80–200 years.</summary>
        public const float MinAgeYears = 80f, MaxAgeYears = 200f;
    }

    /// <summary>Food result for one race at one stage of the game.</summary>
    internal struct FoodPick
    {
        public BuildingDefinition Building;
        public float FedPerWorker;
        /// <summary>Fraction of adults that must work in that building to feed everyone (no foraging).</summary>
        public float AdultShare;
    }

    internal sealed partial class ContentModel
    {
        /// <summary>Rough adult fraction of a stable population (uniform age distribution up to life expectancy).</summary>
        public static float AdultFraction(RaceDefinition r)
        {
            if (r == null || r.LifeExpectancy <= 0f) return 0.5f;
            return Math.Max(0.05f, Math.Min(1f, (r.LifeExpectancy - r.AdulthoodAge) / r.LifeExpectancy));
        }

        public static float FedPerWorker(RaceDefinition r, BuildingDefinition b)
        {
            if (r == null || b == null || r.FoodPerTick <= 0f) return 0f;
            float net = OutputPerWorker(b, ResourceType.Food) - InputPerWorker(b, ResourceType.Food);
            if (net <= 0f) return 0f;
            return net * r.ProductionMultiplier[(int)ResourceType.Food] / r.FoodPerTick;
        }

        /// <summary>
        /// Best food building per worker for a race. stage 0 = founding (Dawn, no techs), 1 = Dawn with every Dawn tech,
        /// 2 = any age.
        /// </summary>
        public FoodPick BestFood(RaceDefinition r, int stage)
        {
            var pick = new FoodPick();
            if (r == null) return pick;
            foreach (var b in Buildings)
            {
                if (!b.HasJobs) continue;
                bool ok;
                if (stage == 0) ok = b.MinAge == AgeId.Dawn && b.AllowedRaces.Allows(r.Id);
                else if (stage == 1) ok = Available[(int)r.Id, 0, (int)b.Type];
                else ok = BuildingFirstAge[(int)r.Id, (int)b.Type] >= 0;
                if (!ok) continue;
                float fed = FedPerWorker(r, b);
                if (fed > pick.FedPerWorker) { pick.FedPerWorker = fed; pick.Building = b; }
            }
            if (pick.FedPerWorker > 0f) pick.AdultShare = 1f / (pick.FedPerWorker * AdultFraction(r));
            return pick;
        }

        /// <summary>
        /// Research of a reference town in an age: Town Center + one of each general (all-race, non-wonder) research
        /// building available in that age + the per-resident rule, for <paramref name="population"/> residents. No multipliers.
        /// </summary>
        public float ReferenceResearchPerTick(int age, int population, out int buildings)
        {
            buildings = 0;
            float rp = population * SystemRules.ResearchPerResident;
            foreach (var b in Buildings)
            {
                if (b.IsWonder || b.AllowedRaces != RaceMask.All) continue;
                float s = ServicePoints(b, ServiceType.Research);
                if (s <= 0f) continue;
                bool avail = false;
                for (int r = 0; r < NumRaces; r++) if (Available[r, age, (int)b.Type]) { avail = true; break; }
                if (!avail) continue;
                rp += s * SystemRules.ResearchPerServicePoint;
                if (b.Type != BuildingType.TownCenter) buildings++;
            }
            return rp;
        }

        /// <summary>Additive research bonus ceiling in an age (general techs up to the age + the age bonus).</summary>
        public float ResearchCeiling(int age)
        {
            float sum = 0f;
            foreach (var t in Techs)
            {
                if ((int)t.Age > age || t.AllowedRaces != RaceMask.All) continue;
                foreach (var e in t.Effects ?? new TechEffect[0]) if (e.Kind == TechEffectKind.ResearchMultiplier) sum += e.Value;
            }
            var def = AgeDef(age);
            return sum + (def != null ? def.ResearchBonus : 0f);
        }

        /// <summary>Years a reference town of the next age's minimum population needs to bridge the research gap (no multipliers); NaN if n/a.</summary>
        public double YearsInAge(int age, out float rpPerTick, out int population, out int researchBuildings)
        {
            rpPerTick = 0f; population = 0; researchBuildings = 0;
            var def = AgeDef(age); var next = AgeDef(age + 1);
            if (def == null || next == null) return double.NaN;
            population = Math.Max(1, next.MinPopulation);
            rpPerTick = ReferenceResearchPerTick(age, population, out researchBuildings);
            float gap = next.ResearchThreshold - def.ResearchThreshold;
            if (rpPerTick <= 0f || gap <= 0f) return double.NaN;
            return gap / (rpPerTick * (double)SimTime.TicksPerYear);
        }

        private void CheckBalance()
        {
            // Research pace vs the ~80-200 years per age design target.
            var fast = new List<string>();
            var slow = new List<string>();
            for (int a = 0; a + 1 < NumAges; a++)
            {
                double years = YearsInAge(a, out float rp, out int pop, out int n);
                if (double.IsNaN(years)) continue;
                string s = AgeName(a) + " " + Md.N(years, 1) + " y";
                if (years < SystemRules.MinAgeYears / 2) fast.Add(s);
                else if (years > SystemRules.MaxAgeYears * 2) slow.Add(s);
            }
            if (fast.Count > 0)
                Add(Severity.Warning, "Balance", "Research outpaces the age thresholds: a reference town (Town Center + one of each research building, next age's min. population, no multipliers) " +
                    "bridges the gap in " + string.Join(", ", fast) + " (design: ~" + SystemRules.MinAgeYears + "–" + SystemRules.MaxAgeYears + " years per age).");
            if (slow.Count > 0)
                Add(Severity.Warning, "Balance", "Research is too slow for the age thresholds: " + string.Join(", ", slow) + " for a reference town (design: ~" + SystemRules.MinAgeYears + "–" + SystemRules.MaxAgeYears + " years per age).");

            // Feeding a fresh camp.
            foreach (var r in Races)
            {
                var f = BestFood(r, 0);
                if (f.Building == null) Add(Severity.Error, "Balance", r.Name + " has no food building at founding.");
                else if (f.AdultShare > 0.6f)
                    Add(Severity.Warning, "Balance", "A new " + r.Name + " camp needs " + Md.N(f.AdultShare * 100, 0) + "% of its adults in its best Dawn food building (" + f.Building.Name +
                        ", " + Md.N(f.FedPerWorker, 1) + " fed per worker) before Dawn techs; foraging covers only " + Md.N(SystemRules.EarlyForagePerTick / r.FoodPerTick, 2) + " people per idle adult.");
            }

            // Later food buildings that are worse than earlier ones still standing.
            foreach (var b in Buildings)
            {
                float fb = OutputPerWorker(b, ResourceType.Food) - InputPerWorker(b, ResourceType.Food);
                if (!b.HasJobs || fb <= 0f) continue;
                foreach (var o in Buildings)
                {
                    float fo = OutputPerWorker(o, ResourceType.Food) - InputPerWorker(o, ResourceType.Food);
                    if (o == b || !o.HasJobs || fo <= 0f || o.MinAge >= b.MinAge || o.MaxAge < b.MinAge) continue;
                    if (fb < fo * 0.75f && o.AllowedRaces == RaceMask.All)
                    {
                        Add(Severity.Info, "Balance", b.Name + " (" + b.MinAge + ") yields " + Md.N(fb, 3) + " food per worker, less than " + o.Name + " (" + o.MinAge + ", " + Md.N(fo, 3) + ").");
                        break;
                    }
                }
            }

            // Average building cost should grow with the ages.
            float prev = 0f; int prevAge = -1;
            for (int a = 0; a < NumAges; a++)
            {
                int n = 0; float sum = 0f;
                foreach (var b in Buildings) if ((int)b.MinAge == a && !b.IsWonder) { n++; sum += TotalCost(b); }
                if (n == 0) continue;
                float avg = sum / n;
                if (prevAge >= 0 && avg < prev)
                    Add(Severity.Info, "Balance", AgeName(a) + " buildings cost less on average (" + Md.N(avg, 0) + ") than " + AgeName(prevAge) + " ones (" + Md.N(prev, 0) + ").");
                prev = avg; prevAge = a;
            }
        }
    }
}
