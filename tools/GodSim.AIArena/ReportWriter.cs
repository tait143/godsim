using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GodSim.AIArena
{
    /// <summary>Renders the markdown "AI Quality Report".</summary>
    public static class ReportWriter
    {
        private static string F(double v, string fmt = "0.##") => double.IsNaN(v) ? "-" : v.ToString(fmt, CultureInfo.InvariantCulture);
        private static string Opt(double v, string fmt = "0.#") => v < 0 ? "-" : F(v, fmt);

        private static readonly string Bars = " ▁▂▃▄▅▆▇█";
        private static string Spark(IList<double> v)
        {
            if (v == null || v.Count == 0) return "";
            double max = v.Max();
            var sb = new StringBuilder();
            foreach (var x in v) sb.Append(max > 0 ? Bars[(int)Math.Round(Math.Max(0, x) / max * (Bars.Length - 1))] : Bars[0]);
            return sb.ToString();
        }

        /// <summary>Average curve across civs (each resampled to <paramref name="points"/> points over the run).</summary>
        private static List<double> Curve(IEnumerable<List<int>> series, int points)
        {
            var list = series.Where(s => s != null && s.Count > 0).ToList();
            var outv = new List<double>();
            if (list.Count == 0) return outv;
            int len = list.Max(s => s.Count);
            points = Math.Min(points, len);
            for (int p = 0; p < points; p++)
            {
                int idx = points == 1 ? len - 1 : (int)Math.Round(p * (len - 1) / (double)(points - 1));
                outv.Add(list.Average(s => idx < s.Count ? s[idx] : s[s.Count - 1]));
            }
            return outv;
        }

        public static string Render(ArenaAnalysis an, ArenaOptions o, string commandLine)
        {
            var sb = new StringBuilder();
            var sims = an.Sims;
            var g = an.All;
            sb.AppendLine("# GodSim AI Quality Report");
            sb.AppendLine();
            sb.AppendLine($"**Overall AI rating: {F(an.Overall, "0.0")} / 10** ({an.Grade})");
            sb.AppendLine();
            sb.AppendLine($"- Arena: {sims.Count} seeds x {o.Years} years, {o.Size}x{o.Size} map, preset {o.Preset}; seeds {string.Join(", ", sims.Select(s => s.Seed))}");
            sb.AppendLine($"- Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC; wall time {F(sims.Sum(s => s.WallSeconds), "0.#")} s of simulation ({(o.Parallel > 1 ? o.Parallel + " parallel workers" : "sequential")})");
            sb.AppendLine($"- Command: `{commandLine}`");
            if (an.Crashed > 0) sb.AppendLine($"- **{an.Crashed} simulation(s) crashed**; their partial data is included and the rating is capped.");
            sb.AppendLine();

            sb.AppendLine("## Headline numbers");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Civilizations surviving | {F(g.SurvivalPct, "0.#")}% of {g.Civs} |");
            sb.AppendLine($"| Population per civ (start -> final, peak) | x{F(g.GrowthMultiple)} median; final {F(g.FinalPop, "0")}, peak {F(g.PeakPop, "0")} avg |");
            sb.AppendLine($"| Ages gained per civ | {F(g.AgesGained)} (years per age {Opt(g.YearsPerAge, "0")}) |");
            sb.AppendLine($"| Starvation share of deaths | {F(g.StarvationPct, "0.#")}% |");
            sb.AppendLine($"| Homeless (time avg) | {F(g.HomelessPct, "0.#")}% |");
            sb.AppendLine($"| People per dwelling (target 3-5) | {Opt(g.PeoplePerHouse, "0.##")} avg, sd {F(g.PeoplePerHouseStd)}, {F(g.InBandPct, "0.#")}% of samples in band; houses/capita {F(g.HousesPerCapita, "0.###")} |");
            sb.AppendLine($"| Construction stalled / avg build time | {F(g.StallPct, "0.#")}% / {F(g.AvgBuildYears)} y |");
            sb.AppendLine($"| Idle adults | {F(g.IdlePct, "0.#")}% |");
            sb.AppendLine($"| Food security | {F(g.FoodDays, "0.#")} days |");
            sb.AppendLine($"| Expansions | {F(g.ExpansionsPerCentury)} per civ-century, first at year {Opt(g.FirstExpansionYear, "0")} |");
            sb.AppendLine($"| Wars / battles / disasters per world-century | {F(an.WarsPerCenturyWorld)} / {F(an.BattlesPerCenturyWorld, "0.#")} / {F(an.DisastersPerCenturyWorld, "0.#")} |");
            sb.AppendLine($"| Research | {F(g.TechsPerCentury, "0.#")} techs per civ-century, {F(g.ResearchPerYear, "0.#")} research/y |");
            sb.AppendLine($"| Gold solvency | {F(g.GoldSolventPct, "0.#")}% of samples |");
            sb.AppendLine($"| AI decisions (AiLog lines) | {F(g.AiLinesPerSettlementYear, "0.#")} per settlement-year |");
            sb.AppendLine($"| Performance | {F(an.AvgTickMs, "0.###")} ms/tick avg, p99 {F(an.P99TickMs)} ms, max {F(an.MaxTickMs, "0.#")} ms at ~{F(an.AvgPop, "0")} people |");
            sb.AppendLine($"| Faults | {an.TotalFaults} system faults, {an.TotalDisabled} systems disabled, {an.TotalHandlerFaults} handler faults, {an.TotalLogErrors} logged errors |");
            sb.AppendLine();

            sb.AppendLine("## Score card");
            sb.AppendLine();
            sb.AppendLine("| Sub-score | Weight | Score | Inputs |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var s in an.Scores) sb.AppendLine($"| {s.Name} | {F(s.Weight * 100, "0")}% | **{F(s.Score, "0.0")}** | {s.Inputs} |");
            sb.AppendLine($"| **Overall** | 100% | **{F(an.Overall, "0.0")}** | weighted sum{(an.Crashed > 0 ? ", capped for crashes" : "")} |");
            sb.AppendLine();

            sb.AppendLine("## Top 5 problems");
            sb.AppendLine();
            if (an.Problems.Count == 0) sb.AppendLine("No problems above threshold.");
            int rank = 1;
            foreach (var p in an.Problems.Take(5))
            {
                sb.AppendLine($"{rank++}. **{p.Title}**: seen in {p.SeedsAffected}/{p.SeedsTotal} seeds, worst on seed {p.Seed} (severity {F(p.Severity, "0.#")}).");
                sb.AppendLine($"   - {p.Detail}");
                sb.AppendLine($"   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list {p.Seed} --years {o.Years} --size {o.Size} --preset {o.Preset}`");
            }
            if (an.Problems.Count > 5)
            {
                sb.AppendLine();
                sb.AppendLine("Other issues: " + string.Join("; ", an.Problems.Skip(5).Take(10).Select(p => $"{p.Title} ({p.SeedsAffected}/{p.SeedsTotal} seeds, e.g. {p.Seed})")));
            }
            sb.AppendLine();

            sb.AppendLine("## Per race");
            sb.AppendLine();
            sb.AppendLine("| Race | Civs | Score | Survive | Pop x | Peak | Ages | Y/age | Starv% | Homeless% | Ppl/house | In band% | Tier fit | Stall% | Idle% | Food d | Exp/c | Wars/c | Battle win% | Techs/c | Solvent% | AI/s-y |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in an.Races.Concat(new[] { an.All }))
                sb.AppendLine($"| {r.Name} | {r.Civs} | {F(r.SubScore, "0.0")} | {F(r.SurvivalPct, "0")}% | {F(r.GrowthMultiple)} | {F(r.PeakPop, "0")} | {F(r.AgesGained)} | {Opt(r.YearsPerAge, "0")} | {F(r.StarvationPct, "0.#")} | {F(r.HomelessPct, "0.#")} | {Opt(r.PeoplePerHouse, "0.##")} | {F(r.InBandPct, "0")} | {F(100 * r.TierFit, "0")}% | {F(r.StallPct, "0")} | {F(r.IdlePct, "0.#")} | {F(r.FoodDays, "0")} | {F(r.ExpansionsPerCentury, "0.#")} | {F(r.WarsPerCentury, "0.#")} | {Opt(r.BattleWinPct, "0")} | {F(r.TechsPerCentury, "0.#")} | {F(r.GoldSolventPct, "0")} | {F(r.AiLinesPerSettlementYear, "0.#")} |");
            sb.AppendLine();
            sb.AppendLine("Score = civ-level composite: 10 x (0.3 survival + 0.2 growth + 0.2 urban + 0.2 progression + 0.1 economy), same sub-formulas as the score card.");
            sb.AppendLine();

            sb.AppendLine("### Population curves (average civ, start to end)");
            sb.AppendLine();
            sb.AppendLine("| Race | Curve | Start | 25% | 50% | 75% | End |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            var initialCivs = sims.SelectMany(s => s.Civs.Where(c => c.Initial)).ToList();
            foreach (var race in an.Races.Select(r => r.Name).Concat(new[] { "All" }))
            {
                var cs = race == "All" ? initialCivs : initialCivs.Where(c => c.Race == race).ToList();
                var curve = Curve(cs.Select(c => new List<int>(new[] { c.InitialPop }.Concat(c.PopByYear))), 24);
                var q = Curve(cs.Select(c => new List<int>(new[] { c.InitialPop }.Concat(c.PopByYear))), 5);
                sb.AppendLine($"| {race} | `{Spark(curve)}` | {string.Join(" | ", q.Select(x => F(x, "0")))} |");
            }
            sb.AppendLine();

            sb.AppendLine("### Ages reached");
            sb.AppendLine();
            sb.AppendLine("| Race | Final ages (per civ) | Year each age was reached (per civ) |");
            sb.AppendLine("|---|---|---|");
            foreach (var race in an.Races.Select(r => r.Name))
            {
                var cs = initialCivs.Where(c => c.Race == race).ToList();
                var ages = string.Join(", ", cs.GroupBy(c => c.FinalAge).OrderByDescending(x => x.Count()).Select(x => x.Key + " x" + x.Count()));
                var steps = string.Join("; ", cs.Take(8).Select(c => c.AgeYears.Count == 0 ? "-" : string.Join("/", c.AgeYears.Select(y => F(y, "0")))));
                sb.AppendLine($"| {race} | {ages} | {steps} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Urban form: building mix by race (% of completed buildings at end)");
            sb.AppendLine();
            var cats = an.CategoryNames;
            sb.AppendLine("| Race | " + string.Join(" | ", cats) + " |");
            sb.AppendLine("|---|" + string.Join("|", cats.Select(_ => "---:")) + "|");
            foreach (var r in an.Races.Concat(new[] { an.All }))
                sb.AppendLine($"| {r.Name} | " + string.Join(" | ", r.CategoryShare.Select(v => F(100 * v, "0.#"))) + " |");
            sb.AppendLine();
            sb.AppendLine("### Race diversity (cosine distance of building-type mix; 0 = identical behaviour)");
            sb.AppendLine();
            sb.AppendLine("| | " + string.Join(" | ", an.Races.Select(r => r.Name)) + " |");
            sb.AppendLine("|---|" + string.Join("|", an.Races.Select(_ => "---:")) + "|");
            for (int i = 0; i < an.Races.Count; i++)
                sb.AppendLine($"| {an.Races[i].Name} | " + string.Join(" | ", Enumerable.Range(0, an.Races.Count).Select(j => F(an.RaceDistance[i, j], "0.000"))) + " |");
            sb.AppendLine();
            sb.AppendLine($"Mean pairwise distance: {F(an.MeanRaceDistance, "0.000")} (0.25+ scores 10).");
            sb.AppendLine();

            sb.AppendLine("## Conflict, disasters & recovery");
            sb.AppendLine();
            var wars = sims.SelectMany(s => s.Wars).ToList();
            sb.AppendLine($"- Wars: {wars.Count} total ({wars.Count(w => w.Outcome == "attacker")} attacker wins, {wars.Count(w => w.Outcome == "defender")} defender wins, {wars.Count(w => w.Outcome == "white-peace")} white peace, {wars.Count(w => w.Outcome == "ongoing")} ongoing); avg duration {F(wars.Count > 0 ? wars.Average(w => w.DurationYears) : 0, "0.#")} y; longest {F(wars.Count > 0 ? wars.Max(w => w.DurationYears) : 0, "0.#")} y.");
            var pairs = wars.GroupBy(w => w.AttackerRace + " -> " + w.DefenderRace).OrderByDescending(x => x.Count()).Take(6);
            sb.AppendLine($"- Most common aggressor -> defender: {string.Join(", ", pairs.Select(p => p.Key + " x" + p.Count()))}");
            sb.AppendLine($"- Battles: {sims.Sum(s => s.Battles)}, settlement captures: {sims.Sum(s => s.Captures)}, civ collapses: {sims.Sum(s => s.CivCollapses)}, new civs mid-game: {sims.Sum(s => s.CivsFoundedLater)}.");
            var dis = new Dictionary<string, int>();
            foreach (var s in sims) foreach (var kv in s.DisastersByType) { dis.TryGetValue(kv.Key, out int n); dis[kv.Key] = n + kv.Value; }
            sb.AppendLine($"- Disasters: {sims.Sum(s => s.Disasters)} ({string.Join(", ", dis.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value))}).");
            sb.AppendLine($"- Destruction incidents (fire/disaster/war/divine): {an.Incidents}, buildings lost {sims.Sum(s => s.IncidentBuildingsLost)}, rebuilt {Opt(an.RecoveredPct, "0.#")}% (avg {Opt(an.AvgRecoveryYears, "0.##")} y to regain the pre-incident building count), {sims.Sum(s => s.IncidentsSettlementLost)} settlements lost before rebuilding.");
            sb.AppendLine();

            sb.AppendLine("## Per seed");
            sb.AppendLine();
            sb.AppendLine("| Seed | Years | Civs alive | World pop (end) | Best age | Wars | Battles | Captures | Disasters | Incidents rebuilt | ms/tick | p99 | Faults | Hash | Status |");
            sb.AppendLine("|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
            foreach (var s in sims)
            {
                var init = s.Civs.Where(c => c.Initial).ToList();
                string best = s.Civs.Count > 0 ? s.Civs.Select(c => c.FinalAge).OrderByDescending(a => Enum.TryParse<GodSim.Core.AgeId>(a, out var ag) ? (int)ag : -1).First() : "-";
                sb.AppendLine($"| {s.Seed} | {s.YearsCompleted} | {init.Count(c => c.Alive)}/{init.Count} | {(s.WorldPopByYear.Count > 0 ? s.WorldPopByYear[s.WorldPopByYear.Count - 1] : 0)} | {best} | {s.WarsDeclared} | {s.Battles} | {s.Captures} | {s.Disasters} | {s.IncidentsRecovered}/{s.Incidents} | {F(s.AvgTickMs, "0.###")} | {F(s.P99TickMs)} | {s.SystemFaults} | `{s.FinalHash}` | {(s.Crashed ? "CRASH: " + Esc(s.Exception) : s.DisabledSystems.Count > 0 ? "disabled: " + string.Join(", ", s.DisabledSystems) : "ok")} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Performance & faults");
            sb.AppendLine();
            var sysAgg = sims.SelectMany(s => s.SlowestSystems).GroupBy(x => x.Name).Select(x => new { Name = x.Key, Ms = x.Average(y => y.AvgMs), N = x.Count() }).OrderByDescending(x => x.Ms).Take(8).ToList();
            sb.AppendLine("Slowest systems (avg ms per run, EMA, across seeds where they ranked top 6): " + string.Join(", ", sysAgg.Select(x => $"{x.Name} {F(x.Ms, "0.###")}")));
            sb.AppendLine();
            var faults = sims.SelectMany(s => s.FaultySystems.Select(f => "seed " + s.Seed + ": " + f)).Take(12).ToList();
            if (faults.Count > 0) { sb.AppendLine("Faulting systems:"); foreach (var f in faults) sb.AppendLine("- " + Esc(f)); sb.AppendLine(); }
            var errs = sims.SelectMany(s => s.FirstErrors.Select(e => "seed " + s.Seed + ": " + e)).Take(8).ToList();
            if (errs.Count > 0) { sb.AppendLine("First logged errors:"); foreach (var e in errs) sb.AppendLine("- " + Esc(e)); sb.AppendLine(); }
            if (o.Parallel > 1) { sb.AppendLine("Note: timings were measured with parallel workers sharing the CPU; use sequential runs for budget checks."); sb.AppendLine(); }

            // AI internals (learning module, advisor weights) if present
            var scoreKeys = initialCivs.SelectMany(c => c.AiScores.Keys).Distinct().OrderBy(k => k).ToList();
            var weightKeys = initialCivs.SelectMany(c => c.AdvisorWeights.Keys).Distinct().OrderBy(k => k).ToList();
            if (scoreKeys.Count > 0 || weightKeys.Count > 0)
            {
                sb.AppendLine("## AI internals (civ.Memory at end)");
                sb.AppendLine();
                var keys = scoreKeys.Select(k => ("ai.score." + k, k, true)).Concat(weightKeys.Select(k => ("ai.w." + k, k, false))).ToList();
                sb.AppendLine("| Key | " + string.Join(" | ", an.Races.Select(r => r.Name)) + " |");
                sb.AppendLine("|---|" + string.Join("|", an.Races.Select(_ => "---:")) + "|");
                foreach (var (label, k, isScore) in keys.Take(40))
                {
                    var cells = an.Races.Select(r =>
                    {
                        var vals = initialCivs.Where(c => c.Race == r.Name).Select(c => isScore ? (c.AiScores.TryGetValue(k, out var v) ? (double?)v : null) : (c.AdvisorWeights.TryGetValue(k, out var w) ? (double?)w : null)).Where(v => v.HasValue).Select(v => v.Value).ToList();
                        return vals.Count > 0 ? F(vals.Average(), "0.###") : "-";
                    });
                    sb.AppendLine($"| `{label}` | " + string.Join(" | ", cells) + " |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Methodology");
            sb.AppendLine();
            sb.AppendLine("Each seed runs the full DefaultComposition headless (`GodSimulation.CreateNew` then one `Step` at a time). The arena only observes: it subscribes to EventBus events (births, deaths, buildings, settlements, ages, techs, wars, battles, captures, disasters, famines) and samples state every season (24 ticks). Only civilizations present at world creation are scored (rebels/spawned tribes are reported but not scored).");
            sb.AppendLine();
            sb.AppendLine("| Sub-score | Weight | Formula (all terms clamped to 0..1) |");
            sb.AppendLine("|---|---:|---|");
            foreach (var s in an.Scores) sb.AppendLine($"| {s.Name} | {F(s.Weight * 100, "0")}% | {s.Formula} |");
            sb.AppendLine();
            sb.AppendLine("Metric definitions:");
            sb.AppendLine("- *People per dwelling*: settlement population / completed dwellings (`IsDwelling`), per seasonal sample of settlements with 6+ people; *in band* = 3..5. sd = std-dev across those samples (spread of the 3-5 rule).");
            sb.AppendLine("- *Tier fit*: fraction of categories expected at the settlement's tier that have at least one completed building (Camp: Housing+Food; Hamlet: +Resource; Village: +Storage, Religion; Town: +Industry, Military, Culture; City+: +Commerce, Health, Education).");
            sb.AppendLine("- *Stall*: share of under-construction building-time in which `ConstructionProgress` did not increase since the previous sample (includes waiting for materials/cost).");
            sb.AppendLine("- *Idle*: `Settlement.Unemployed` / adult residents. *Food security*: food stock / max(avg food expense, half of race FoodPerTick x pop), capped at 365 days.");
            sb.AppendLine("- *Gold solvent*: a settlement sample with gold > 1 or non-negative net gold flow.");
            sb.AppendLine("- *Expansion*: `SettlementFoundedEvent` after world creation. *War outcome*: final war score > 15 attacker, < -15 defender, else white peace.");
            sb.AppendLine("- *Destruction incident*: a settlement losing a building to fire, disaster, war or divine power; recovered when its completed-building count returns to its value at the sample before the loss.");
            sb.AppendLine("- *AI decisions*: new `Settlement.AiLog` lines per settlement-year (detected every tick; the log is bounded to 16 lines).");
            sb.AppendLine("- *Diversity*: each race's building-type vector (completed buildings at end, each civ normalized then summed); cosine distance between races.");
            sb.AppendLine("- *Top problems*: per problem kind the worst instance; ranked by severity x (0.5 + 0.5 x share of seeds affected).");
            return sb.ToString();
        }

        private static string Esc(string s) => (s ?? "").Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }
}
