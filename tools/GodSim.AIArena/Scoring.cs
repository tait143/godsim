using System;
using System.Collections.Generic;
using System.Linq;

namespace GodSim.AIArena
{
    /// <summary>One weighted component of the overall AI rating (0..10).</summary>
    public sealed class SubScore
    {
        public string Key = "";
        public string Name = "";
        public double Weight;
        public double Score;
        public string Inputs = "";
        public string Formula = "";
    }

    /// <summary>A reproducible problem: worst instance plus prevalence across seeds.</summary>
    public sealed class Problem
    {
        public string Kind = "";
        public string Title = "";
        public double Severity;
        public ulong Seed;
        public string Detail = "";
        public int SeedsAffected;
        public int SeedsTotal;
        public double Rank;
    }

    /// <summary>Aggregated metrics for a set of civilizations (a race, or everyone).</summary>
    public sealed class GroupStats
    {
        public string Name = "";
        public int Civs;
        public double SurvivalPct;
        public double GrowthMultiple;     // median final/initial population
        public double PeakPop;
        public double FinalPop;
        public double AgesGained;
        public double YearsPerAge;        // mean over civs that advanced (-1 none)
        public double StarvationPct;      // starvation deaths / all deaths
        public double HomelessPct;
        public double PeoplePerHouse;
        public double PeoplePerHouseStd;
        public double InBandPct;
        public double HousesPerCapita;
        public double TierFit;
        public double StallPct;
        public double AvgBuildYears;
        public double IdlePct;
        public double FoodDays;
        public double ExpansionsPerCentury;
        public double FirstExpansionYear; // mean over civs that expanded (-1 none)
        public double WarsPerCentury;     // wars involved in, per civ per century
        public double BattleWinPct;
        public double TechsPerCentury;
        public double ResearchPerYear;
        public double GoldSolventPct;
        public double AiLinesPerSettlementYear;
        public double[] CategoryShare = new double[0];
        public double[] TypeVector = new double[0];
        public double SubScore;           // civ-level composite (survival, growth, urban, progression, economy)
    }

    public sealed class ArenaAnalysis
    {
        public List<SimResult> Sims = new List<SimResult>();
        public double Overall;
        public string Grade = "";
        public List<SubScore> Scores = new List<SubScore>();
        public GroupStats All;
        public List<GroupStats> Races = new List<GroupStats>();
        public double[,] RaceDistance = new double[0, 0];
        public double MeanRaceDistance;
        public List<Problem> Problems = new List<Problem>();
        public int Crashed;
        public double WarsPerCenturyWorld, DisastersPerCenturyWorld, BattlesPerCenturyWorld;
        public double AvgTickMs, P99TickMs, MaxTickMs, AvgPop;
        public int TotalFaults, TotalDisabled, TotalHandlerFaults, TotalLogErrors;
        public double RecoveredPct = -1, AvgRecoveryYears = -1;
        public int Incidents;
        public double CivSurvivalPct;
        public string[] CategoryNames = Enum.GetNames(typeof(GodSim.Core.BuildingCategory));
    }

    /// <summary>
    /// Turns raw per-world results into the scored AI-quality analysis. Every formula is documented in the report
    /// (Methodology section); keep that text in sync with the code below.
    /// </summary>
    public static class Scoring
    {
        public static double Clamp01(double v) => double.IsNaN(v) ? 0 : v < 0 ? 0 : v > 1 ? 1 : v;
        private static double Band(double v, double lo, double hi, double soft) => Band(v, lo, hi, soft, soft);
        private static double Band(double v, double lo, double hi, double softLo, double softHi)
        {
            // 1 inside [lo,hi], decays linearly to 0 at lo-softLo / hi+softHi.
            if (v >= lo && v <= hi) return 1;
            if (v < lo) return Clamp01(1 - (lo - v) / softLo);
            return Clamp01(1 - (v - hi) / softHi);
        }

        public static ArenaAnalysis Analyze(List<SimResult> sims, int years)
        {
            var an = new ArenaAnalysis { Sims = sims };
            var civs = sims.SelectMany(s => s.Civs.Where(c => c.Initial)).ToList();
            var allCivs = sims.SelectMany(s => s.Civs).ToList();
            double yrs = Math.Max(1, sims.Count > 0 ? sims.Average(s => Math.Max(1, s.YearsCompleted)) : years);
            double cent = yrs / 100.0;

            an.Crashed = sims.Count(s => s.Crashed);
            an.All = Group("All", civs, yrs);
            foreach (var race in civs.Select(c => c.Race).Distinct().OrderBy(r => r))
                an.Races.Add(Group(race, civs.Where(c => c.Race == race).ToList(), yrs));

            // Diversity: cosine distance between race building-type vectors.
            int n = an.Races.Count;
            an.RaceDistance = new double[n, n];
            double dsum = 0; int dn = 0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double d = 1 - Cosine(an.Races[i].TypeVector, an.Races[j].TypeVector);
                    an.RaceDistance[i, j] = Math.Round(d, 3);
                    if (j > i) { dsum += d; dn++; }
                }
            an.MeanRaceDistance = dn > 0 ? dsum / dn : 0;

            // World-level
            an.WarsPerCenturyWorld = sims.Count > 0 ? sims.Average(s => s.WarsDeclared / Math.Max(0.01, Math.Max(1, s.YearsCompleted) / 100.0)) : 0;
            an.DisastersPerCenturyWorld = sims.Count > 0 ? sims.Average(s => s.Disasters / Math.Max(0.01, Math.Max(1, s.YearsCompleted) / 100.0)) : 0;
            an.BattlesPerCenturyWorld = sims.Count > 0 ? sims.Average(s => s.Battles / Math.Max(0.01, Math.Max(1, s.YearsCompleted) / 100.0)) : 0;
            var ran = sims.Where(s => s.AvgTickMs > 0).ToList();
            an.AvgTickMs = ran.Count > 0 ? ran.Average(s => s.AvgTickMs) : 0;
            an.P99TickMs = ran.Count > 0 ? ran.Max(s => s.P99TickMs) : 0;
            an.MaxTickMs = ran.Count > 0 ? ran.Max(s => s.MaxTickMs) : 0;
            an.AvgPop = sims.Count > 0 ? sims.Average(s => s.AvgPopulation) : 0;
            an.TotalFaults = sims.Sum(s => s.SystemFaults);
            an.TotalDisabled = sims.Sum(s => s.DisabledSystems.Count);
            an.TotalHandlerFaults = sims.Sum(s => s.HandlerFaults);
            an.TotalLogErrors = sims.Sum(s => s.LogErrors);
            an.Incidents = sims.Sum(s => s.Incidents);
            int recovered = sims.Sum(s => s.IncidentsRecovered);
            if (an.Incidents > 0) an.RecoveredPct = 100.0 * recovered / an.Incidents;
            var rec = sims.Where(s => s.AvgRecoveryYears >= 0).ToList();
            if (rec.Count > 0) an.AvgRecoveryYears = rec.Average(s => s.AvgRecoveryYears);
            an.CivSurvivalPct = an.All.SurvivalPct;

            // ---------------------------------------------------------------- sub-scores
            var g = an.All;
            double expectedAges = yrs / 110.0;
            double targetGrowth = Math.Min(12, 1 + yrs / 15.0);

            double sSurv = 10 * (0.4 * Clamp01(g.SurvivalPct / 100) + 0.3 * Clamp01(1 - g.StarvationPct / 20) + 0.3 * Clamp01(1 - g.HomelessPct / 25));
            Add(an, "survival", "Survival & stability", 0.20, sSurv,
                $"civ survival {g.SurvivalPct:0.#}%, starvation {g.StarvationPct:0.#}% of deaths, homeless {g.HomelessPct:0.#}%",
                "10 x (0.4 x survival + 0.3 x (1 - starvation%/20) + 0.3 x (1 - homeless%/25))");

            double growth = g.GrowthMultiple > 0 ? Clamp01(Math.Log(Math.Max(1e-3, g.GrowthMultiple)) / Math.Log(targetGrowth)) : 0;
            double expansion = Clamp01(g.ExpansionsPerCentury / 2.0);
            Add(an, "growth", "Growth & expansion", 0.15, 10 * (0.7 * growth + 0.3 * expansion),
                $"median pop x{g.GrowthMultiple:0.##} (target x{targetGrowth:0.#} for {yrs:0} y), {g.ExpansionsPerCentury:0.##} expansions/civ/century",
                "10 x (0.7 x clamp(ln(growth)/ln(1+years/15, max 12)) + 0.3 x clamp(expansions per century / 2))");

            double sBand = Clamp01(g.InBandPct / 100);
            double sVar = Clamp01(1 - g.PeoplePerHouseStd / 2.0);
            double sStall = Clamp01(1 - g.StallPct / 50);
            double sIdle = Clamp01(1 - g.IdlePct / 30);
            Add(an, "urban", "Urban quality", 0.15, 10 * (0.3 * sBand + 0.15 * sVar + 0.2 * Clamp01(g.TierFit) + 0.2 * sStall + 0.15 * sIdle),
                $"{g.InBandPct:0.#}% of samples at 3-5 ppl/house (avg {g.PeoplePerHouse:0.##}, sd {g.PeoplePerHouseStd:0.##}), tier-mix fit {100 * g.TierFit:0.#}%, stall {g.StallPct:0.#}%, idle {g.IdlePct:0.#}%",
                "10 x (0.3 x in-band% + 0.15 x (1 - sd/2) + 0.2 x tier-fit + 0.2 x (1 - stall%/50) + 0.15 x (1 - idle%/30))");

            double ageScore = Clamp01(g.AgesGained / Math.Max(0.25, expectedAges));
            if (g.YearsPerAge > 0 && g.YearsPerAge < 40) ageScore *= Clamp01(g.YearsPerAge / 40);
            double techScore = Clamp01(g.TechsPerCentury / 8.0);
            Add(an, "progression", "Progression", 0.15, 10 * (0.7 * ageScore + 0.3 * techScore),
                $"{g.AgesGained:0.##} ages gained/civ (expected {expectedAges:0.##}), {(g.YearsPerAge > 0 ? g.YearsPerAge.ToString("0") : "-")} y/age, {g.TechsPerCentury:0.#} techs/century",
                "10 x (0.7 x clamp(ages gained / (years/110)) [x clamp(y-per-age/40) if faster than 40 y] + 0.3 x clamp(techs per century / 8))");

            Add(an, "economy", "Economy", 0.10, 10 * (0.5 * Clamp01(g.GoldSolventPct / 100) + 0.5 * Clamp01(g.FoodDays / 60)),
                $"gold solvent {g.GoldSolventPct:0.#}% of samples, food security {g.FoodDays:0.#} days",
                "10 x (0.5 x solvency% + 0.5 x clamp(food days / 60))");

            double rRec = an.Incidents > 0 ? Clamp01(an.RecoveredPct / 100) : 0.8;
            double rTime = an.AvgRecoveryYears >= 0 ? Clamp01(1 - an.AvgRecoveryYears / 10) : 0.8;
            double rPeak = Clamp01((civs.Count > 0 ? civs.Average(c => c.PeakPop > 0 ? c.FinalPop / (double)c.PeakPop : 0) : 0) - 0.5) / 0.5;
            rPeak = Clamp01(rPeak);
            Add(an, "resilience", "Resilience", 0.10, 10 * (0.4 * rRec + 0.3 * rTime + 0.3 * rPeak),
                $"{an.Incidents} destruction incidents, {(an.RecoveredPct >= 0 ? an.RecoveredPct.ToString("0.#") + "% rebuilt" : "none to rebuild")}, avg {(an.AvgRecoveryYears >= 0 ? an.AvgRecoveryYears.ToString("0.##") + " y" : "-")}, final/peak pop {100 * (0.5 + 0.5 * rPeak):0.#}%+",
                "10 x (0.4 x rebuilt% + 0.3 x (1 - recovery years/10) + 0.3 x clamp((final/peak - 0.5)/0.5)); untested parts count 0.8");

            Add(an, "diversity", "Race diversity", 0.05, 10 * Clamp01(an.MeanRaceDistance / 0.25),
                $"mean pairwise cosine distance of race building-type vectors {an.MeanRaceDistance:0.###}",
                "10 x clamp(mean cosine distance / 0.25)");

            double dWar = Band(an.WarsPerCenturyWorld, 1, 6, 4);
            double dDis = Band(an.DisastersPerCenturyWorld, 3, 40, 20);
            double dBat = an.WarsPerCenturyWorld > 0 ? Band(an.BattlesPerCenturyWorld / Math.Max(0.01, an.WarsPerCenturyWorld), 1, 30, 1, 20) : 0;
            Add(an, "drama", "Drama", 0.05, 10 * (0.45 * dWar + 0.35 * dDis + 0.2 * dBat),
                $"{an.WarsPerCenturyWorld:0.##} wars, {an.BattlesPerCenturyWorld:0.#} battles, {an.DisastersPerCenturyWorld:0.#} disasters per world-century",
                "10 x (0.45 x band(wars/century, 1..6, soft 4) + 0.35 x band(disasters/century, 3..40, soft 20) + 0.2 x band(battles per war, 1..30, 0 battles = 0))");

            double perf = Clamp01(1 - (an.AvgTickMs - 1.5) / 4.5);
            double robust = an.Crashed > 0 ? 0 : Clamp01(1 - (an.TotalDisabled * 0.5 + Math.Min(1, an.TotalFaults / 50.0) * 0.5));
            Add(an, "performance", "Performance & robustness", 0.05, 10 * (0.5 * perf + 0.5 * robust),
                $"{an.AvgTickMs:0.###} ms/tick avg (p99 {an.P99TickMs:0.##}, max {an.MaxTickMs:0.#}) at ~{an.AvgPop:0} people; {an.TotalFaults} system faults, {an.TotalDisabled} disabled, {an.Crashed} crashed sims",
                "10 x (0.5 x clamp(1 - (ms/tick - 1.5)/4.5) + 0.5 x robustness), robustness = 0 on any crash else 1 - 0.5 x disabled - 0.5 x min(1, faults/50)");

            an.Overall = Math.Round(an.Scores.Sum(s => s.Weight * s.Score), 2);
            if (an.Crashed > 0) an.Overall = Math.Round(Math.Min(an.Overall, 10 - 2.0 * an.Crashed / Math.Max(1, sims.Count) * 5), 2);
            an.Grade = an.Overall >= 9 ? "excellent" : an.Overall >= 7.5 ? "good" : an.Overall >= 6 ? "fair" : an.Overall >= 4 ? "weak" : "poor";

            // Civ-level composite per race (same formulas, race subset)
            foreach (var r in an.Races.Concat(new[] { an.All }))
            {
                double surv = 0.4 * Clamp01(r.SurvivalPct / 100) + 0.3 * Clamp01(1 - r.StarvationPct / 20) + 0.3 * Clamp01(1 - r.HomelessPct / 25);
                double gr = 0.7 * (r.GrowthMultiple > 0 ? Clamp01(Math.Log(Math.Max(1e-3, r.GrowthMultiple)) / Math.Log(targetGrowth)) : 0) + 0.3 * Clamp01(r.ExpansionsPerCentury / 2);
                double ur = 0.3 * Clamp01(r.InBandPct / 100) + 0.15 * Clamp01(1 - r.PeoplePerHouseStd / 2) + 0.2 * Clamp01(r.TierFit) + 0.2 * Clamp01(1 - r.StallPct / 50) + 0.15 * Clamp01(1 - r.IdlePct / 30);
                double pr = 0.7 * Clamp01(r.AgesGained / Math.Max(0.25, expectedAges)) + 0.3 * Clamp01(r.TechsPerCentury / 8);
                double ec = 0.5 * Clamp01(r.GoldSolventPct / 100) + 0.5 * Clamp01(r.FoodDays / 60);
                r.SubScore = Math.Round(10 * (0.3 * surv + 0.2 * gr + 0.2 * ur + 0.2 * pr + 0.1 * ec), 2);
            }

            an.Problems = FindProblems(sims, yrs);
            return an;
        }

        private static void Add(ArenaAnalysis an, string key, string name, double w, double score, string inputs, string formula)
        {
            if (double.IsNaN(score) || double.IsInfinity(score)) score = 0;
            an.Scores.Add(new SubScore { Key = key, Name = name, Weight = w, Score = Math.Round(Math.Max(0, Math.Min(10, score)), 2), Inputs = inputs, Formula = formula });
        }

        private static double Median(IEnumerable<double> xs)
        {
            var a = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToArray();
            if (a.Length == 0) return 0;
            return a.Length % 2 == 1 ? a[a.Length / 2] : 0.5 * (a[a.Length / 2 - 1] + a[a.Length / 2]);
        }

        private static double MeanOr(IEnumerable<double> xs, double fallback)
        {
            var a = xs.Where(x => !double.IsNaN(x)).ToArray();
            return a.Length > 0 ? a.Average() : fallback;
        }

        public static GroupStats Group(string name, List<CivResult> cs, double yrs)
        {
            var g = new GroupStats { Name = name, Civs = cs.Count };
            if (cs.Count == 0) return g;
            double cent = Math.Max(0.01, yrs / 100.0);
            g.SurvivalPct = 100.0 * cs.Count(c => c.Alive) / cs.Count;
            g.GrowthMultiple = Median(cs.Select(c => c.InitialPop > 0 ? c.FinalPop / (double)c.InitialPop : 0));
            g.PeakPop = cs.Average(c => c.PeakPop);
            g.FinalPop = cs.Average(c => c.FinalPop);
            g.AgesGained = cs.Average(c => c.AgesGained);
            g.YearsPerAge = MeanOr(cs.Where(c => c.AvgYearsPerAge > 0).Select(c => c.AvgYearsPerAge), -1);
            long deaths = cs.Sum(c => c.Deaths), starv = cs.Sum(c => c.StarvationDeaths);
            g.StarvationPct = deaths > 0 ? 100.0 * starv / deaths : 0;
            g.HomelessPct = cs.Average(c => c.HomelessPct);
            var housed = cs.Where(c => c.PeoplePerHouse >= 0).ToList();
            g.PeoplePerHouse = housed.Count > 0 ? housed.Average(c => c.PeoplePerHouse) : -1;
            g.PeoplePerHouseStd = housed.Count > 0 ? housed.Average(c => c.PeoplePerHouseStd) : 0;
            g.InBandPct = housed.Count > 0 ? housed.Average(c => c.InBandPct) : 0;
            g.HousesPerCapita = cs.Average(c => c.HousesPerCapita);
            g.TierFit = cs.Average(c => c.TierFit);
            g.StallPct = cs.Average(c => c.StallPct);
            g.AvgBuildYears = MeanOr(cs.Where(c => c.AvgBuildYears > 0).Select(c => c.AvgBuildYears), 0);
            g.IdlePct = cs.Average(c => c.IdlePct);
            g.FoodDays = cs.Average(c => c.FoodDays);
            g.ExpansionsPerCentury = cs.Average(c => c.Expansions) / cent;
            g.FirstExpansionYear = MeanOr(cs.Where(c => c.FirstExpansionYear >= 0).Select(c => c.FirstExpansionYear), -1);
            g.WarsPerCentury = cs.Average(c => c.WarsAttacker + c.WarsDefender) / cent;
            int battles = cs.Sum(c => c.Battles);
            g.BattleWinPct = battles > 0 ? 100.0 * cs.Sum(c => c.BattlesWon) / battles : -1;
            g.TechsPerCentury = cs.Average(c => c.TechsResearched) / cent;
            g.ResearchPerYear = cs.Average(c => c.ResearchPerYear);
            g.GoldSolventPct = cs.Average(c => c.GoldSolventPct);
            g.AiLinesPerSettlementYear = cs.Average(c => c.AiLinesPerSettlementYear);

            var cats = Enum.GetNames(typeof(GodSim.Core.BuildingCategory));
            g.CategoryShare = new double[cats.Length];
            double tot = 0;
            foreach (var c in cs) foreach (var kv in c.CategoryMix) { int i = Array.IndexOf(cats, kv.Key); if (i >= 0) { g.CategoryShare[i] += kv.Value; tot += kv.Value; } }
            if (tot > 0) for (int i = 0; i < cats.Length; i++) g.CategoryShare[i] = g.CategoryShare[i] / tot;
            int dims = cs.Max(c => c.TypeMix?.Length ?? 0);
            g.TypeVector = new double[dims];
            foreach (var c in cs)
            {
                if (c.TypeMix == null) continue;
                double ct = c.TypeMix.Sum();
                if (ct <= 0) continue;
                for (int i = 0; i < c.TypeMix.Length; i++) g.TypeVector[i] += c.TypeMix[i] / ct; // each civ weighs the same
            }
            return g;
        }

        public static double Cosine(double[] a, double[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < n; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            if (na <= 0 || nb <= 0) return 0;
            return dot / Math.Sqrt(na * nb);
        }

        // ---------------------------------------------------------------- problems

        private static List<Problem> FindProblems(List<SimResult> sims, double yrs)
        {
            var found = new List<Problem>();
            void P(string kind, string title, double sev, SimResult s, string detail)
            {
                if (sev <= 0 || double.IsNaN(sev)) return;
                // Faults/crashes can reach 20; gameplay problems cap at 12 so one extreme civ cannot outrank a widespread issue.
                double maxSev = kind == "crash" || kind.StartsWith("disabled:", StringComparison.Ordinal) ? 20 : 12;
                found.Add(new Problem { Kind = kind, Title = title, Severity = Math.Min(maxSev, sev), Seed = s.Seed, Detail = detail });
            }

            foreach (var s in sims)
            {
                if (s.Crashed) P("crash", "Simulation crashed", 20, s, s.Exception);
                foreach (var d in s.DisabledSystems) P("disabled:" + d, "System disabled after repeated faults: " + d, 15, s, string.Join("; ", s.FaultySystems.Where(f => f.StartsWith(d, StringComparison.Ordinal))));
                foreach (var f in s.FaultySystems) P("fault:" + f.Split(' ')[0], "System faults: " + f.Split(' ')[0], 6 + Math.Min(6, s.SystemFaults / 10.0), s, f);
                if (s.HandlerFaults > 0) P("handler", "Event handler faults", 5, s, s.HandlerFaults + " handler faults");
                if (s.AvgTickMs > 3) P("perf", "Tick budget exceeded (> 3 ms avg)", 4 + s.AvgTickMs, s, $"{s.AvgTickMs:0.##} ms/tick avg, p99 {s.P99TickMs:0.##} ms, ~{s.AvgPopulation:0} people; slowest: " + string.Join(", ", s.SlowestSystems.Take(3).Select(x => x.Name + " " + x.AvgMs.ToString("0.###") + "ms")));
                if (s.Incidents >= 3 && s.IncidentsRecovered < s.Incidents * 0.5) P("recovery", "Settlements do not rebuild after destruction", 6 * (1 - s.IncidentsRecovered / (double)s.Incidents) + 2, s, $"{s.IncidentsRecovered}/{s.Incidents} incidents rebuilt ({s.IncidentBuildingsLost} buildings lost)");
                foreach (var w in s.Wars.Where(w => w.DurationYears > 40)) P("endless-war", "Endless war (> 40 years)", 3 + w.DurationYears / 20, s, $"{w.AttackerRace} vs {w.DefenderRace} war #{w.Id} lasted {w.DurationYears:0} y ({w.Outcome}, {w.Battles} battles)");
                int cap = new GodSim.Core.SimConfig().MaxPopulation;
                int peakWorld = s.WorldPopByYear.Count > 0 ? s.WorldPopByYear.Max() : 0;
                if (peakWorld >= cap * 0.98)
                {
                    int yr = s.WorldPopByYear.FindIndex(v => v >= cap * 0.98) + 1;
                    P("pop-cap", "World population pinned at the MaxPopulation safety cap", 9, s, $"world pop reached {peakWorld} (cap {cap}) in year {yr}: growth is limited by the engine cap, not by AI/economy");
                }
                if (yrs >= 60 && s.WarsDeclared == 0 && s.Civs.Count(c => c.Initial) > 1) P("no-war", "No wars at all (no conflict drama)", 3, s, "0 wars declared in " + s.YearsCompleted + " years");

                foreach (var c in s.Civs.Where(c => c.Initial))
                {
                    string who = $"{c.Race} civ #{c.Id}";
                    if (!c.Alive) P("collapse", "Civilization collapsed", 12, s, $"{who} collapsed in year {c.CollapseYear:0.#} (peak {c.PeakPop} pop in year {c.PeakYear})");
                    else if (c.InitialPop > 0 && c.FinalPop < c.InitialPop * 0.75) P("decline", "Population decline", 8 * (1 - c.FinalPop / (double)c.InitialPop) + 3, s, $"{who}: {c.InitialPop} -> {c.FinalPop} pop (peak {c.PeakPop} in year {c.PeakYear})");
                    double starv = c.Deaths > 0 ? 100.0 * c.StarvationDeaths / c.Deaths : 0;
                    if (starv > 10 && c.StarvationDeaths >= 5) P("starvation", "Starvation deaths", starv / 5, s, $"{who}: {c.StarvationDeaths}/{c.Deaths} deaths from starvation ({starv:0.#}%), {c.Famines} famines, food {c.FoodDays:0} days avg / {c.FoodDaysMin:0} min");
                    if (c.HomelessPct > 10) P("homeless", "Chronic homelessness", c.HomelessPct / 4, s, $"{who}: {c.HomelessPct:0.#}% homeless avg (max decade {c.HomelessPctMax:0.#}%), {c.PeoplePerHouse:0.##} people/house");
                    if (c.PeoplePerHouse > 6) P("overcrowded", "Overcrowding: too few dwellings (> 6 people/house)", (c.PeoplePerHouse - 5) * 2, s, $"{who}: {c.PeoplePerHouse:0.##} people per dwelling (target 3-5), {c.InBandPct:0}% of samples in band");
                    else if (c.PeoplePerHouse >= 0 && c.PeoplePerHouse < 2.5) P("overbuilt", "Over-built housing (< 2.5 people/house)", (3 - c.PeoplePerHouse) * 3, s, $"{who}: {c.PeoplePerHouse:0.##} people per dwelling (target 3-5)");
                    if (c.StallPct > 35) P("stall", "Construction stalls", c.StallPct / 8, s, $"{who}: {c.StallPct:0.#}% of construction time stalled, {c.StuckAtEnd} buildings stuck > 2 y at end, avg build {c.AvgBuildYears:0.##} y");
                    if (c.IdlePct > 20) P("idle", "Idle workers", c.IdlePct / 5, s, $"{who}: {c.IdlePct:0.#}% of adults unemployed on average");
                    if (c.GoldSolventPct < 60) P("insolvent", "Gold insolvency", (100 - c.GoldSolventPct) / 10, s, $"{who}: solvent in only {c.GoldSolventPct:0.#}% of samples, gold at end {c.GoldEnd:0}");
                    if (yrs >= 90 && c.AgesGained == 0 && c.Alive) P("stuck-age", "Stuck in the starting age", 7, s, $"{who}: still {c.FinalAge} after {s.YearsCompleted} years ({c.TotalResearch:0} research, {c.TechsResearched} techs)");
                    if (c.AvgYearsPerAge > 0 && c.AvgYearsPerAge < 25) P("fast-age", "Ages advance too fast (< 25 y/age)", 5, s, $"{who}: {c.AgesGained} ages in {s.YearsCompleted} y ({c.AvgYearsPerAge:0} y/age), now {c.FinalAge}");
                    if (yrs >= 60 && c.Expansions == 0 && c.Alive && c.PeakPop > 40) P("no-expansion", "Never expands", 4, s, $"{who}: 1 settlement after {s.YearsCompleted} y with peak pop {c.PeakPop}");
                    if (c.TierFit < 0.6 && c.Alive) P("mix", "Building mix misses tier essentials", (0.6 - c.TierFit) * 12, s, $"{who}: tier-mix fit {100 * c.TierFit:0}% (best tier {c.BestTier}); mix " + string.Join(", ", c.CategoryMix.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value)));
                    if (c.Alive && c.AiLinesPerSettlementYear < 0.5) P("ai-silent", "AI barely acts (AiLog silent)", 4, s, $"{who}: {c.AiLinesPerSettlementYear:0.##} AI decisions per settlement-year");
                    if (c.AiLinesPerSettlementYear > 60) P("ai-churn", "AI decision churn (> 60 decisions per settlement-year)", 3 + c.AiLinesPerSettlementYear / 60, s, $"{who}: {c.AiLinesPerSettlementYear:0} AiLog lines per settlement-year");
                    if (c.FoodDays < 10 && c.Alive) P("food", "Living hand to mouth (< 10 days of food)", (10 - c.FoodDays) / 1.5 + 2, s, $"{who}: {c.FoodDays:0.#} days of food on average (min {c.FoodDaysMin:0.#})");
                }
            }

            // One entry per kind: worst instance, prevalence across seeds, rank = severity x (0.5 + 0.5 x prevalence)
            int total = Math.Max(1, sims.Count);
            var list = new List<Problem>();
            foreach (var grp in found.GroupBy(p => p.Kind))
            {
                var worst = grp.OrderByDescending(p => p.Severity).First();
                worst.SeedsAffected = grp.Select(p => p.Seed).Distinct().Count();
                worst.SeedsTotal = total;
                worst.Rank = worst.Severity * (0.5 + 0.5 * worst.SeedsAffected / (double)total);
                list.Add(worst);
            }
            return list.OrderByDescending(p => p.Rank).ToList();
        }
    }
}
