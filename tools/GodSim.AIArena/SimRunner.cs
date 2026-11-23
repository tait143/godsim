using System;
using System.Collections.Generic;
using System.Diagnostics;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.AIArena
{
    /// <summary>Counts and keeps the first errors/warnings of one world. One instance per simulation.</summary>
    public sealed class ArenaLogger : ISimLogger
    {
        public int Errors;
        public int Warnings;
        public readonly List<string> First = new List<string>();
        public LogLevel MinLevel => LogLevel.Warning;

        public void Log(LogLevel level, string message)
        {
            if (level == LogLevel.Error) Errors++;
            else if (level == LogLevel.Warning) Warnings++;
            else return;
            if (level == LogLevel.Error && First.Count < 8)
            {
                string m = message ?? "";
                int nl = m.IndexOf('\n');
                if (nl > 0) m = m.Substring(0, nl);
                if (m.Length > 300) m = m.Substring(0, 300) + "...";
                First.Add(m);
            }
        }
    }

    /// <summary>
    /// Runs one seeded world for N years and collects AI-quality metrics. Observes only: subscribes to events and
    /// samples state between ticks (never mutates the world). Never throws: exceptions are recorded in the result.
    /// </summary>
    public sealed class SimRunner
    {
        private const int SampleInterval = 24; // one season

        private sealed class CivAcc
        {
            public CivResult R;
            public double HomelessSum, PopSum, PopSamples;
            public double HomelessDecadeSum, PopDecadeSum;
            public double PphSum, PphSqSum; public int PphN, InBandN;
            public double CapSum, HouseSum;
            public double IdleSum, AdultSum;
            public double FoodDaysSum; public int FoodDaysN; public double FoodDaysMin = double.MaxValue;
            public double TierFitSum; public int TierFitN;
            public int GoldOk, GoldN;
            public double ConstrTicks, StallTicks;
            public double BuildTicksSum; public int BuildN;
            public long AiLines;
            public double SettlementYears;
        }

        private sealed class Incident { public int SettlementId; public long Tick; public int Target; public int Lost; }

        private readonly ulong _seed;
        private readonly int _years;
        private readonly int _size;
        private readonly WorldPreset _preset;

        private GodSimulation _sim;
        private WorldState _state;
        private GameDatabase _db;
        private SimResult _res;
        private readonly Dictionary<int, CivAcc> _civs = new Dictionary<int, CivAcc>();
        private readonly List<int> _civOrder = new List<int>();
        private readonly Dictionary<int, WarInfo> _wars = new Dictionary<int, WarInfo>();
        private readonly Dictionary<int, float> _lastProgress = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _lastCompleted = new Dictionary<int, int>();
        private readonly Dictionary<int, Incident> _openIncidents = new Dictionary<int, Incident>();
        private readonly List<double> _recoveryYears = new List<double>();
        private readonly Dictionary<int, string> _aiLast = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _aiCount = new Dictionary<int, int>();
        private readonly List<float> _tickMs = new List<float>(16384);
        private double _popSampleSum; private int _popSamples;

        public SimRunner(ulong seed, int years, int size, WorldPreset preset)
        {
            _seed = seed; _years = years; _size = size; _preset = preset;
        }

        public SimResult Run()
        {
            _res = new SimResult { Seed = _seed, Years = _years, Size = _size, Preset = _preset.ToString() };
            var log = new ArenaLogger();
            var wall = Stopwatch.StartNew();
            try
            {
                var cfg = new SimConfig { Seed = _seed, MapWidth = _size, MapHeight = _size, Preset = _preset };
                _sim = GodSimulation.CreateNew(cfg, log);
                _state = _sim.State;
                _db = _sim.Db;
                Subscribe(_sim.Events);
                for (int i = 0; i < _state.Civilizations.Items.Count; i++) EnsureCiv(_state.Civilizations.Items[i], true);
                foreach (var a in _civs.Values) a.R.InitialPop = CivPop(a.R.Id);
                Sample(true);

                long totalTicks = (long)_years * SimTime.TicksPerYear;
                for (long t = 0; t < totalTicks; t++)
                {
                    _sim.Step();
                    _tickMs.Add((float)_sim.Engine.LastStepMs);
                    TrackAiLog();
                    if (_state.Tick % SampleInterval == 0) Sample(false);
                    if (_state.Tick % SimTime.TicksPerYear == 0) YearSample();
                }
                _res.YearsCompleted = _years;
            }
            catch (Exception ex)
            {
                _res.Crashed = true;
                _res.Exception = ex.GetType().Name + ": " + ex.Message + " @ " + FirstFrame(ex);
                _res.YearsCompleted = _state != null ? SimTime.Year(_state.Tick) : 0;
            }
            try { Finish(log); }
            catch (Exception ex)
            {
                _res.Crashed = true;
                _res.Exception += (_res.Exception.Length > 0 ? " | " : "") + "finalize: " + ex.GetType().Name + ": " + ex.Message;
            }
            _res.WallSeconds = wall.Elapsed.TotalSeconds;
            return _res;
        }

        private static string FirstFrame(Exception ex)
        {
            var st = ex.StackTrace ?? "";
            int nl = st.IndexOf('\n');
            return (nl > 0 ? st.Substring(0, nl) : st).Trim();
        }

        // ------------------------------------------------------------------ events

        private void Subscribe(EventBus bus)
        {
            bus.Subscribe<PersonBornEvent>(e => { var a = Civ(e.CivilizationId); if (a != null) a.R.Births++; });
            bus.Subscribe<PersonDiedEvent>(e =>
            {
                var a = Civ(e.CivilizationId);
                if (a == null) return;
                a.R.Deaths++;
                if (e.Cause == DeathCause.Starvation) a.R.StarvationDeaths++;
                string k = e.Cause.ToString();
                a.R.DeathsByCause.TryGetValue(k, out long n);
                a.R.DeathsByCause[k] = n + 1;
            });
            bus.Subscribe<BuildingPlacedEvent>(e => { var a = Civ(SettlementCiv(e.SettlementId)); if (a != null) a.R.BuildingsPlaced++; });
            bus.Subscribe<BuildingCompletedEvent>(e =>
            {
                var a = Civ(SettlementCiv(e.SettlementId));
                if (a == null) return;
                a.R.BuildingsCompleted++;
                var b = _state.Buildings.Get(e.BuildingId);
                if (b != null && b.PlacedTick > 0 && _state.Tick >= b.PlacedTick) { a.BuildTicksSum += _state.Tick - b.PlacedTick; a.BuildN++; }
            });
            bus.Subscribe<BuildingDestroyedEvent>(e =>
            {
                if (e.Cause == DestroyCause.Demolished || e.Cause == DestroyCause.Abandoned || e.Cause == DestroyCause.Decay) return;
                if (e.SettlementId < 0) return;
                _res.IncidentBuildingsLost++;
                if (_openIncidents.TryGetValue(e.SettlementId, out var inc)) { inc.Lost++; return; }
                // The building is already unlinked from the settlement when this fires: pre-loss count = current + it.
                var lostB = _state.Buildings.Get(e.BuildingId);
                int target = CompletedCount(_state.Settlements.Get(e.SettlementId)) + (lostB != null && lostB.IsComplete ? 1 : 0);
                _openIncidents[e.SettlementId] = new Incident { SettlementId = e.SettlementId, Tick = _state.Tick, Target = Math.Max(1, target), Lost = 1 };
                _res.Incidents++;
            });
            bus.Subscribe<SettlementFoundedEvent>(e =>
            {
                var a = Civ(e.CivilizationId);
                if (a == null) return;
                a.R.Expansions++;
                double y = Years(_state.Tick);
                a.R.ExpansionYears.Add(Math.Round(y, 1));
                if (a.R.FirstExpansionYear < 0) a.R.FirstExpansionYear = Math.Round(y, 1);
            });
            bus.Subscribe<SettlementCapturedEvent>(e =>
            {
                _res.Captures++;
                var g = Civ(e.NewCivilizationId); if (g != null) g.R.CapturesGained++;
                var l = Civ(e.OldCivilizationId); if (l != null) l.R.CapturesLost++;
            });
            bus.Subscribe<CivilizationFoundedEvent>(e => { _res.CivsFoundedLater++; var c = _state.Civilizations.Get(e.CivilizationId); if (c != null) EnsureCiv(c, false); });
            bus.Subscribe<CivilizationCollapsedEvent>(e => { _res.CivCollapses++; var a = Civ(e.CivilizationId); if (a != null) a.R.CollapseYear = Math.Round(Years(_state.Tick), 1); });
            bus.Subscribe<AgeAdvancedEvent>(e => { var a = Civ(e.CivilizationId); if (a != null) { a.R.AgesGained++; a.R.AgeYears.Add(Math.Round(Years(_state.Tick), 1)); } });
            bus.Subscribe<TechResearchedEvent>(e => { var a = Civ(e.CivilizationId); if (a != null) a.R.TechsResearched++; });
            bus.Subscribe<FamineEvent>(e => { var a = Civ(SettlementCiv(e.SettlementId)); if (a != null) a.R.Famines++; });
            bus.Subscribe<WarDeclaredEvent>(e =>
            {
                _res.WarsDeclared++;
                var w = new WarInfo { Id = e.WarId, AttackerId = e.AttackerId, DefenderId = e.DefenderId, StartYear = Math.Round(Years(_state.Tick), 1) };
                w.AttackerRace = RaceOf(e.AttackerId); w.DefenderRace = RaceOf(e.DefenderId);
                _wars[e.WarId] = w;
                _res.Wars.Add(w);
                var a = Civ(e.AttackerId); if (a != null) a.R.WarsAttacker++;
                var d = Civ(e.DefenderId); if (d != null) d.R.WarsDefender++;
            });
            bus.Subscribe<PeaceSignedEvent>(e =>
            {
                _res.PeacesSigned++;
                if (!_wars.TryGetValue(e.WarId, out var w))
                {
                    w = new WarInfo { Id = e.WarId, AttackerId = e.AttackerId, DefenderId = e.DefenderId, StartYear = 0, AttackerRace = RaceOf(e.AttackerId), DefenderRace = RaceOf(e.DefenderId) };
                    _wars[e.WarId] = w; _res.Wars.Add(w);
                }
                w.EndYear = Math.Round(Years(_state.Tick), 1);
                w.WarScore = MathUtil.Sanitize(e.WarScore);
                w.Outcome = w.WarScore > 15f ? "attacker" : w.WarScore < -15f ? "defender" : "white-peace";
                var a = Civ(e.AttackerId); var d = Civ(e.DefenderId);
                if (w.Outcome == "attacker") { if (a != null) a.R.WarsWon++; if (d != null) d.R.WarsLost++; }
                else if (w.Outcome == "defender") { if (d != null) d.R.WarsWon++; if (a != null) a.R.WarsLost++; }
            });
            bus.Subscribe<BattleEvent>(e =>
            {
                _res.Battles++;
                var a = Civ(e.AttackerCivId); var d = Civ(e.DefenderCivId);
                if (a != null) { a.R.Battles++; if (e.AttackerWon) a.R.BattlesWon++; }
                if (d != null) { d.R.Battles++; if (!e.AttackerWon) d.R.BattlesWon++; }
            });
            bus.Subscribe<DisasterEvent>(e =>
            {
                _res.Disasters++;
                string k = e.Type.ToString();
                _res.DisastersByType.TryGetValue(k, out int n);
                _res.DisastersByType[k] = n + 1;
            });
        }

        // ------------------------------------------------------------------ sampling

        private void TrackAiLog()
        {
            var ss = _state.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var s = ss[i];
                if (s == null || !s.IsAlive) continue;
                var log = s.AiLog;
                int n = log.Count;
                _aiLast.TryGetValue(s.Id, out string last);
                _aiCount.TryGetValue(s.Id, out int lastCount);
                if (n == 0) { _aiLast[s.Id] = null; _aiCount[s.Id] = 0; continue; }
                string tail = log[n - 1];
                if (ReferenceEquals(tail, last) && n == lastCount) continue;
                int added;
                if (n > lastCount) added = n - lastCount;                 // still filling up (exact)
                else
                {
                    int idx = -1;
                    if (last != null) for (int j = n - 1; j >= 0; j--) if (ReferenceEquals(log[j], last)) { idx = j; break; }
                    added = idx >= 0 ? n - 1 - idx : n;
                    if (added == 0) added = 1;                             // same-literal line appended
                }
                _aiLast[s.Id] = tail; _aiCount[s.Id] = n;
                var a = Civ(s.CivilizationId);
                if (a != null) a.AiLines += added;
            }
        }

        private void Sample(bool initial)
        {
            var ss = _state.Settlements.Items;
            var people = _state.People;
            int worldPop = 0;
            for (int i = 0; i < ss.Count; i++)
            {
                var s = ss[i];
                if (s == null || !s.IsAlive) continue;
                var a = Civ(s.CivilizationId);
                int pop = s.Population;
                worldPop += pop;
                int completed = CompletedCount(s);
                _lastCompleted[s.Id] = completed;
                if (a == null || initial) continue;
                a.SettlementYears += SampleInterval / (double)SimTime.TicksPerYear;

                int houses = 0, cap = 0;
                var bids = s.BuildingIds;
                for (int k = 0; k < bids.Count; k++)
                {
                    var b = _state.Buildings.Get(bids[k]);
                    if (b == null || b.IsDestroyed || !b.IsComplete) continue;
                    if (_db.TryGetBuilding(b.Type, out var def) && def.IsDwelling) { houses++; cap += b.HousingCapacity; }
                }
                int adults = 0;
                for (int k = 0; k < s.ResidentIds.Count; k++) { var p = people.Get(s.ResidentIds[k]); if (p != null && p.IsAlive && p.IsAdult) adults++; }

                a.PopSum += pop; a.PopSamples++;
                a.HomelessSum += Math.Max(0, Math.Min(pop, s.Homeless));
                a.PopDecadeSum += pop; a.HomelessDecadeSum += Math.Max(0, Math.Min(pop, s.Homeless));
                a.CapSum += cap; a.HouseSum += houses;
                if (pop >= 6)
                {
                    double pph = houses > 0 ? pop / (double)houses : pop;
                    a.PphSum += pph; a.PphSqSum += pph * pph; a.PphN++;
                    if (pph >= 3.0 && pph <= 5.0) a.InBandN++;
                }
                a.IdleSum += Math.Max(0, Math.Min(adults, s.Unemployed)); a.AdultSum += adults;

                if (pop > 0)
                {
                    float food = s.Stockpile.Get(ResourceType.Food);
                    float use = s.Stockpile.AvgExpense[(int)ResourceType.Food];
                    float perHead = SafeFoodPerTick(s.Race);
                    float daily = Math.Max(use, pop * perHead * 0.5f);
                    double days = daily > 1e-4f ? food / daily : 365.0;
                    days = Math.Min(365.0, MathUtil.Sanitize((float)days));
                    a.FoodDaysSum += days; a.FoodDaysN++;
                    if (days < a.FoodDaysMin) a.FoodDaysMin = days;

                    float gold = s.Stockpile.Get(ResourceType.Gold);
                    a.GoldN++;
                    if (gold > 1f || s.Stockpile.Net(ResourceType.Gold) >= 0f) a.GoldOk++;

                    a.TierFitSum += TierFit(s); a.TierFitN++;
                }
            }
            if (!initial) { _popSampleSum += worldPop; _popSamples++; }

            // Construction stall: under-construction building-time with no progress since the previous sample.
            var bs = _state.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                if (b == null || b.IsDestroyed || b.IsComplete) { if (b != null) _lastProgress.Remove(b.Id); continue; }
                var a = Civ(SettlementCiv(b.SettlementId));
                if (a != null && !initial)
                {
                    a.ConstrTicks += SampleInterval;
                    if (_lastProgress.TryGetValue(b.Id, out float prev) && b.ConstructionProgress <= prev + 1e-6f) a.StallTicks += SampleInterval;
                }
                _lastProgress[b.Id] = b.ConstructionProgress;
            }

            // Resilience: close incidents whose settlement rebuilt to the pre-incident count.
            if (_openIncidents.Count > 0)
            {
                List<int> done = null;
                foreach (var kv in _openIncidents)
                {
                    var inc = kv.Value;
                    var s = _state.Settlements.Get(inc.SettlementId);
                    if (s == null || !s.IsAlive) { _res.IncidentsSettlementLost++; (done ??= new List<int>()).Add(kv.Key); continue; }
                    if (_lastCompleted.TryGetValue(s.Id, out int c) && c >= inc.Target)
                    {
                        _res.IncidentsRecovered++;
                        _recoveryYears.Add(Years(_state.Tick - inc.Tick));
                        (done ??= new List<int>()).Add(kv.Key);
                    }
                }
                if (done != null) foreach (int k in done) _openIncidents.Remove(k);
            }
        }

        private void YearSample()
        {
            int world = 0;
            foreach (int id in _civOrder)
            {
                var a = _civs[id];
                int p = CivPop(id);
                world += p;
                a.R.PopByYear.Add(p);
                if (p > a.R.PeakPop) { a.R.PeakPop = p; a.R.PeakYear = SimTime.Year(_state.Tick); }
                if (_state.Year % 10 == 0)
                {
                    a.R.HomelessPctByDecade.Add(a.PopDecadeSum > 0 ? Math.Round(100.0 * a.HomelessDecadeSum / a.PopDecadeSum, 1) : 0);
                    a.PopDecadeSum = 0; a.HomelessDecadeSum = 0;
                }
            }
            _res.WorldPopByYear.Add(world);
        }

        private static readonly BuildingCategory[][] s_expected =
        {
            new[] { BuildingCategory.Housing, BuildingCategory.Food },                                                                         // Camp
            new[] { BuildingCategory.Housing, BuildingCategory.Food, BuildingCategory.Resource },                                              // Hamlet
            new[] { BuildingCategory.Housing, BuildingCategory.Food, BuildingCategory.Resource, BuildingCategory.Storage, BuildingCategory.Religion }, // Village
            new[] { BuildingCategory.Housing, BuildingCategory.Food, BuildingCategory.Resource, BuildingCategory.Storage, BuildingCategory.Religion, BuildingCategory.Industry, BuildingCategory.Military, BuildingCategory.Culture }, // Town
            new[] { BuildingCategory.Housing, BuildingCategory.Food, BuildingCategory.Resource, BuildingCategory.Storage, BuildingCategory.Religion, BuildingCategory.Industry, BuildingCategory.Military, BuildingCategory.Culture, BuildingCategory.Commerce, BuildingCategory.Health, BuildingCategory.Education }, // City+
        };

        /// <summary>Fraction of the categories expected for the settlement's tier that it has (completed).</summary>
        private double TierFit(Settlement s)
        {
            int tier = Math.Min((int)s.Tier, s_expected.Length - 1);
            var exp = s_expected[Math.Max(0, tier)];
            int have = 0;
            for (int e = 0; e < exp.Length; e++)
            {
                var bids = s.BuildingIds;
                for (int k = 0; k < bids.Count; k++)
                {
                    var b = _state.Buildings.Get(bids[k]);
                    if (b == null || b.IsDestroyed || !b.IsComplete) continue;
                    if (_db.TryGetBuilding(b.Type, out var def) && def.Category == exp[e]) { have++; break; }
                }
            }
            return have / (double)exp.Length;
        }

        // ------------------------------------------------------------------ finalize

        private void Finish(ArenaLogger log)
        {
            // Performance
            if (_tickMs.Count > 0)
            {
                double sum = 0, max = 0;
                for (int i = 0; i < _tickMs.Count; i++) { sum += _tickMs[i]; if (_tickMs[i] > max) max = _tickMs[i]; }
                var sorted = _tickMs.ToArray(); Array.Sort(sorted);
                _res.AvgTickMs = Math.Round(sum / _tickMs.Count, 4);
                _res.MaxTickMs = Math.Round(max, 3);
                _res.P99TickMs = Math.Round(sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))], 4);
            }
            _res.AvgPopulation = _popSamples > 0 ? Math.Round(_popSampleSum / _popSamples, 1) : 0;
            if (_sim != null)
            {
                var profs = _sim.Engine.Profiles;
                var costs = new List<SystemCost>();
                for (int i = 0; i < profs.Count; i++)
                {
                    var p = profs[i];
                    _res.SystemFaults += p.TotalFaults;
                    if (p.Disabled) _res.DisabledSystems.Add(p.Name);
                    if (p.TotalFaults > 0) _res.FaultySystems.Add(p.Name + " x" + p.TotalFaults + ": " + Trim(p.LastError, 200));
                    costs.Add(new SystemCost { Name = p.Name, AvgMs = Math.Round(p.AvgMs, 4), Faults = p.TotalFaults, Disabled = p.Disabled, LastError = Trim(p.LastError, 200) });
                }
                costs.Sort((x, y) => y.AvgMs.CompareTo(x.AvgMs));
                for (int i = 0; i < Math.Min(6, costs.Count); i++) _res.SlowestSystems.Add(costs[i]);
                _res.HandlerFaults = _sim.Events.HandlerFaults;
                try { _res.FinalHash = _sim.ComputeHash().ToString("x16"); }
                catch (Exception ex) { _res.FinalHash = "hash-failed: " + ex.GetType().Name; }
            }
            _res.LogErrors = log.Errors;
            _res.LogWarnings = log.Warnings;
            _res.FirstErrors.AddRange(log.First);

            // Wars: merge final war records (battles, captures, losses, ongoing duration)
            if (_state != null)
            {
                var wars = _state.Diplomacy.Wars;
                for (int i = 0; i < wars.Count; i++)
                {
                    var w = wars[i];
                    if (!_wars.TryGetValue(w.Id, out var wi)) continue;
                    wi.Battles = w.Battles; wi.Captures = w.SettlementsCaptured;
                    wi.AttackerLosses = w.AttackerLosses; wi.DefenderLosses = w.DefenderLosses;
                    if (w.IsActive) wi.WarScore = MathUtil.Sanitize(w.WarScore);
                }
                double now = Years(_state.Tick);
                foreach (var wi in _res.Wars) wi.DurationYears = Math.Round((wi.EndYear >= 0 ? wi.EndYear : now) - wi.StartYear, 1);
            }
            _res.AvgRecoveryYears = _recoveryYears.Count > 0 ? Math.Round(Avg(_recoveryYears), 2) : -1;

            foreach (int id in _civOrder) FinishCiv(_civs[id]);
            foreach (int id in _civOrder) _res.Civs.Add(_civs[id].R);
        }

        private void FinishCiv(CivAcc a)
        {
            var r = a.R;
            var civ = _state?.Civilizations.Get(r.Id);
            double years = Math.Max(1.0 / SimTime.TicksPerYear, _state != null ? Years(_state.Tick) : 1);
            r.Alive = civ != null && civ.IsAlive;
            r.FinalPop = CivPop(r.Id);
            if (r.FinalPop > r.PeakPop) r.PeakPop = r.FinalPop;
            if (civ != null)
            {
                r.FinalAge = civ.Age.ToString();
                r.TotalResearch = Math.Round(MathUtil.Sanitize(civ.TotalResearch), 1);
                foreach (var kv in civ.Memory)
                {
                    if (kv.Key.StartsWith("ai.score.", StringComparison.Ordinal)) r.AiScores[kv.Key.Substring(9)] = MathUtil.Sanitize(kv.Value);
                    else if (kv.Key.StartsWith("ai.w.", StringComparison.Ordinal)) r.AdvisorWeights[kv.Key.Substring(5)] = MathUtil.Sanitize(kv.Value);
                }
                if (!civ.IsAlive && r.CollapseYear < 0 && civ.CollapsedTick >= 0) r.CollapseYear = Math.Round(Years(civ.CollapsedTick), 1);
            }
            r.ResearchPerYear = Math.Round(r.TotalResearch / years, 2);
            // Years per age: Dawn->first age measured from year 0, then between consecutive ages.
            if (r.AgeYears.Count > 0) r.AvgYearsPerAge = Math.Round(r.AgeYears[r.AgeYears.Count - 1] / r.AgeYears.Count, 1);

            r.HomelessPct = a.PopSum > 0 ? Math.Round(100.0 * a.HomelessSum / a.PopSum, 2) : 0;
            foreach (var d in r.HomelessPctByDecade) if (d > r.HomelessPctMax) r.HomelessPctMax = d;
            if (a.PopDecadeSum > 0) { double last = 100.0 * a.HomelessDecadeSum / a.PopDecadeSum; if (last > r.HomelessPctMax) r.HomelessPctMax = Math.Round(last, 1); }
            if (a.PphN > 0)
            {
                double m = a.PphSum / a.PphN;
                r.PeoplePerHouse = Math.Round(m, 2);
                r.PeoplePerHouseStd = Math.Round(Math.Sqrt(Math.Max(0, a.PphSqSum / a.PphN - m * m)), 2);
                r.InBandPct = Math.Round(100.0 * a.InBandN / a.PphN, 1);
            }
            else r.PeoplePerHouse = -1;
            r.HousingCapPerCapita = a.PopSum > 0 ? Math.Round(a.CapSum / a.PopSum, 3) : 0;
            r.HousesPerCapita = a.PopSum > 0 ? Math.Round(a.HouseSum / a.PopSum, 3) : 0;
            r.IdlePct = a.AdultSum > 0 ? Math.Round(100.0 * a.IdleSum / a.AdultSum, 2) : 0;
            r.FoodDays = a.FoodDaysN > 0 ? Math.Round(a.FoodDaysSum / a.FoodDaysN, 1) : 0;
            r.FoodDaysMin = a.FoodDaysN > 0 ? Math.Round(a.FoodDaysMin, 1) : 0;
            r.TierFit = a.TierFitN > 0 ? Math.Round(a.TierFitSum / a.TierFitN, 3) : 0;
            r.GoldSolventPct = a.GoldN > 0 ? Math.Round(100.0 * a.GoldOk / a.GoldN, 1) : 0;
            r.StallPct = a.ConstrTicks > 0 ? Math.Round(100.0 * a.StallTicks / a.ConstrTicks, 1) : 0;
            r.AvgBuildYears = a.BuildN > 0 ? Math.Round(a.BuildTicksSum / a.BuildN / SimTime.TicksPerYear, 2) : 0;
            r.AiLinesPerYear = Math.Round(a.AiLines / years, 2);
            r.AiLinesPerSettlementYear = a.SettlementYears > 0 ? Math.Round(a.AiLines / a.SettlementYears, 2) : 0;

            // Final building mix and settlements
            r.TypeMix = new int[Counts.Buildings];
            var ss = _state != null ? _state.Settlements.Items : null;
            int bestTier = -1;
            if (ss != null)
            {
                for (int i = 0; i < ss.Count; i++)
                {
                    var s = ss[i];
                    if (s == null || !s.IsAlive || s.CivilizationId != r.Id) continue;
                    r.SettlementsAtEnd++;
                    r.GoldEnd += Math.Round(MathUtil.Sanitize(s.Stockpile.Get(ResourceType.Gold)), 1);
                    if ((int)s.Tier > bestTier) bestTier = (int)s.Tier;
                    for (int k = 0; k < s.BuildingIds.Count; k++)
                    {
                        var b = _state.Buildings.Get(s.BuildingIds[k]);
                        if (b == null || b.IsDestroyed) continue;
                        if (!b.IsComplete) { if (_state.Tick - b.PlacedTick > 2 * SimTime.TicksPerYear) r.StuckAtEnd++; continue; }
                        int t = (int)b.Type;
                        if (t >= 0 && t < r.TypeMix.Length) r.TypeMix[t]++;
                        if (_db.TryGetBuilding(b.Type, out var def))
                        {
                            string c = def.Category.ToString();
                            r.CategoryMix.TryGetValue(c, out int n);
                            r.CategoryMix[c] = n + 1;
                        }
                    }
                }
            }
            r.BestTier = bestTier >= 0 ? ((SettlementTier)bestTier).ToString() : "-";
        }

        // ------------------------------------------------------------------ helpers

        private void EnsureCiv(Civilization c, bool initial)
        {
            if (c == null || _civs.ContainsKey(c.Id)) return;
            _civs[c.Id] = new CivAcc { R = new CivResult { Id = c.Id, Race = c.Race.ToString(), Name = c.Name ?? "", Initial = initial } };
            _civOrder.Add(c.Id);
        }

        private CivAcc Civ(int civId)
        {
            if (civId < 0) return null;
            if (_civs.TryGetValue(civId, out var a)) return a;
            var c = _state?.Civilizations.Get(civId);
            if (c == null) return null;
            EnsureCiv(c, false);
            return _civs[civId];
        }

        private int SettlementCiv(int settlementId)
        {
            var s = settlementId >= 0 ? _state.Settlements.Get(settlementId) : null;
            return s != null ? s.CivilizationId : -1;
        }

        private string RaceOf(int civId) { var c = _state.Civilizations.Get(civId); return c != null ? c.Race.ToString() : "?"; }

        private int CivPop(int civId)
        {
            int n = 0;
            var ss = _state.Settlements.Items;
            for (int i = 0; i < ss.Count; i++) if (ss[i] != null && ss[i].IsAlive && ss[i].CivilizationId == civId) n += ss[i].Population;
            return n;
        }

        private int CompletedCount(Settlement s)
        {
            if (s == null) return 0;
            int n = 0;
            for (int k = 0; k < s.BuildingIds.Count; k++) { var b = _state.Buildings.Get(s.BuildingIds[k]); if (b != null && !b.IsDestroyed && b.IsComplete) n++; }
            return n;
        }

        private float SafeFoodPerTick(RaceId race)
        {
            try { var d = _db.Race(race); return d != null && d.FoodPerTick > 0 ? d.FoodPerTick : 0.05f; }
            catch (Exception) { return 0.05f; }
        }

        private static double Years(long ticks) => ticks / (double)SimTime.TicksPerYear;
        private static double Avg(List<double> v) { double s = 0; foreach (var x in v) s += x; return v.Count > 0 ? s / v.Count : 0; }
        private static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(0, n) + "..." : s);
    }
}
