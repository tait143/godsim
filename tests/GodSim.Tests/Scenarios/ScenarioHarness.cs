using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Tests.Scenarios
{
    /// <summary>Result of <see cref="ScenarioHarness.Cast"/>: enqueue result plus what the divine module did with it.</summary>
    public sealed class CastOutcome
    {
        public GodPowerType Power;
        public Vec2i Target;
        public int Radius;
        public int Parameter;
        /// <summary>GodSimulation.TryCastPower result (validation/rate limit at enqueue time).</summary>
        public bool Accepted;
        public string Error;
        /// <summary>A GodPowerCastEvent for this power arrived.</summary>
        public bool Executed;
        public bool Success;
        public string Message;
        /// <summary>A GodCommandRejectedEvent for this power arrived (re-validation at execution).</summary>
        public bool RejectedAtExecution;
        public string RejectReason;
        public long IssuedTick;
        public long ResolvedTick = -1;
        public int TicksWaited;
        public float ManaBefore;
        public float ManaAfter;
        public int BuildingsDestroyed;
        public int UnnaturalDeaths;
        public int CivsFounded;

        public bool SucceededInWorld => Executed && Success;
        public float ManaSpent => ManaBefore - ManaAfter;

        public override string ToString() =>
            Power + "@" + Target + " r=" + Radius + " p=" + Parameter + " accepted=" + Accepted + (Error != null ? " error='" + Error + "'" : "") +
            " executed=" + Executed + " success=" + Success + " msg='" + Message + "' rejectedAtExec=" + RejectedAtExecution +
            (RejectReason != null ? " reason='" + RejectReason + "'" : "") + " mana " + ManaBefore.ToString("0.0") + "->" + ManaAfter.ToString("0.0") +
            " destroyed=" + BuildingsDestroyed + " deaths=" + UnnaturalDeaths + " civsFounded=" + CivsFounded;
    }

    /// <summary>
    /// Drives a whole <see cref="GodSimulation"/> tick by tick while collecting per-year metrics from EventBus
    /// subscriptions (births, deaths by cause, buildings completed by type, age advancements, wars, battles,
    /// settlements founded/destroyed/captured...), periodic samples of population and housing per settlement,
    /// and event-time invariant violations (age skipping, battles outside wars, inconsistent conquest, houses
    /// over capacity). Re-attach after Load with <see cref="Attach"/>.
    /// </summary>
    public sealed class ScenarioHarness : IDisposable
    {
        private const int MaxViolationsKept = 200;

        private GodSimulation _sim;
        private readonly List<Action> _unsubscribe = new List<Action>();
        private readonly SortedDictionary<int, YearMetrics> _years = new SortedDictionary<int, YearMetrics>();
        private readonly List<SettlementCapturedEvent> _pendingCaptureChecks = new List<SettlementCapturedEvent>();
        private readonly Dictionary<int, AgeId> _lastAge = new Dictionary<int, AgeId>();

        public ScenarioHarness(GodSimulation sim, CollectingLogger log = null)
        {
            Log = log;
            Attach(sim);
            StartTick = sim.State.Tick;
            var civs = sim.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                if (!civs[i].IsAlive) continue;
                InitialCivIds.Add(civs[i].Id);
                InitialAges[civs[i].Id] = civs[i].Age;
                _lastAge[civs[i].Id] = civs[i].Age;
                MaxAgeReached[civs[i].Id] = civs[i].Age;
            }
        }

        // ------------------------------------------------------------------ configuration
        /// <summary>Sample settlements/world every N ticks (0 = never). Default once per season.</summary>
        public int SampleEveryTicks = SimTime.TicksPerSeason;
        public bool SampleSettlements = true;
        public bool CheckHousingOnSample = true;
        public Action<ScenarioHarness> BeforeStep;
        public Action<ScenarioHarness> AfterStep;
        public Action<ScenarioHarness> OnSample;
        public Action<ScenarioHarness, YearMetrics> OnYearClosed;
        public CollectingLogger Log;

        // ------------------------------------------------------------------ state
        public GodSimulation Sim => _sim;
        public WorldState State => _sim.State;
        public GameDatabase Db => _sim.Db;
        public long StartTick { get; private set; }
        public long TicksRun { get; private set; }
        public long Now => _sim.State.Tick;
        public readonly List<int> InitialCivIds = new List<int>();
        /// <summary>Age of each initial civ when the harness attached.</summary>
        public readonly Dictionary<int, AgeId> InitialAges = new Dictionary<int, AgeId>();
        /// <summary>Highest age ever observed per civ id (events + samples, dead civs included).</summary>
        public readonly Dictionary<int, AgeId> MaxAgeReached = new Dictionary<int, AgeId>();

        // ------------------------------------------------------------------ logs
        public readonly List<Timed<PersonDiedEvent>> Deaths = new List<Timed<PersonDiedEvent>>();
        public readonly List<Timed<BuildingCompletedEvent>> Completions = new List<Timed<BuildingCompletedEvent>>();
        public readonly List<Timed<BuildingDestroyedEvent>> Destructions = new List<Timed<BuildingDestroyedEvent>>();
        public readonly List<UpgradeRecord> Upgrades = new List<UpgradeRecord>();
        public readonly List<Timed<AgeAdvancedEvent>> AgeAdvances = new List<Timed<AgeAdvancedEvent>>();
        public readonly List<Timed<WarDeclaredEvent>> WarsDeclared = new List<Timed<WarDeclaredEvent>>();
        public readonly List<Timed<PeaceSignedEvent>> PeacesSigned = new List<Timed<PeaceSignedEvent>>();
        public readonly List<Timed<BattleEvent>> Battles = new List<Timed<BattleEvent>>();
        public readonly List<Timed<SettlementFoundedEvent>> SettlementsFounded = new List<Timed<SettlementFoundedEvent>>();
        public readonly List<Timed<SettlementDestroyedEvent>> SettlementsDestroyed = new List<Timed<SettlementDestroyedEvent>>();
        public readonly List<Timed<SettlementCapturedEvent>> Captures = new List<Timed<SettlementCapturedEvent>>();
        public readonly List<Timed<CivilizationFoundedEvent>> CivsFounded = new List<Timed<CivilizationFoundedEvent>>();
        public readonly List<Timed<CivilizationCollapsedEvent>> CivsCollapsed = new List<Timed<CivilizationCollapsedEvent>>();
        public readonly List<Timed<FamineEvent>> Famines = new List<Timed<FamineEvent>>();
        public readonly List<Timed<DisasterEvent>> Disasters = new List<Timed<DisasterEvent>>();
        public readonly List<Timed<GodPowerCastEvent>> Casts = new List<Timed<GodPowerCastEvent>>();
        public readonly List<Timed<GodCommandRejectedEvent>> Rejections = new List<Timed<GodCommandRejectedEvent>>();

        public readonly List<SettlementSample> Samples = new List<SettlementSample>();
        public readonly List<WorldSample> WorldSamples = new List<WorldSample>();

        // ------------------------------------------------------------------ invariant violations (event time / sample time)
        public readonly List<string> AgeViolations = new List<string>();
        public readonly List<string> UpgradeViolations = new List<string>();
        public readonly List<string> BattleViolations = new List<string>();
        public readonly List<string> CaptureViolations = new List<string>();
        public readonly List<string> HousingViolations = new List<string>();
        public int HousingViolationCount { get; private set; }

        public IEnumerable<YearMetrics> Years => _years.Values;

        public YearMetrics YearOf(int year)
        {
            if (!_years.TryGetValue(year, out var m)) { m = new YearMetrics { Year = year }; _years[year] = m; }
            return m;
        }

        private YearMetrics Y() => YearOf(SimTime.Year(_sim.State.Tick));

        // ------------------------------------------------------------------ attach / detach
        /// <summary>Subscribes to a (possibly freshly loaded) simulation. Logs and metrics are kept.</summary>
        public void Attach(GodSimulation sim)
        {
            Detach();
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _pendingCaptureChecks.Clear();
            Sub<PersonBornEvent>(OnBorn);
            Sub<PersonDiedEvent>(OnDied);
            Sub<PersonMigratedEvent>(OnMigrated);
            Sub<BuildingPlacedEvent>(OnPlaced);
            Sub<BuildingCompletedEvent>(OnCompleted);
            Sub<BuildingDestroyedEvent>(OnDestroyed);
            Sub<BuildingUpgradedEvent>(OnUpgraded);
            Sub<AgeAdvancedEvent>(OnAgeAdvanced);
            Sub<TechResearchedEvent>(OnTech);
            Sub<WarDeclaredEvent>(OnWarDeclared);
            Sub<PeaceSignedEvent>(OnPeace);
            Sub<BattleEvent>(OnBattle);
            Sub<SettlementFoundedEvent>(OnSettlementFounded);
            Sub<SettlementDestroyedEvent>(OnSettlementDestroyed);
            Sub<SettlementCapturedEvent>(OnCaptured);
            Sub<CivilizationFoundedEvent>(OnCivFounded);
            Sub<CivilizationCollapsedEvent>(OnCivCollapsed);
            Sub<FamineEvent>(OnFamine);
            Sub<DisasterEvent>(OnDisaster);
            Sub<GodPowerCastEvent>(OnCast);
            Sub<GodCommandRejectedEvent>(OnRejected);
        }

        public void Detach()
        {
            foreach (var u in _unsubscribe) u();
            _unsubscribe.Clear();
        }

        public void Dispose() => Detach();

        private void Sub<T>(Action<T> handler) where T : struct
        {
            var bus = _sim.Events;
            bus.Subscribe(handler);
            _unsubscribe.Add(() => bus.Unsubscribe(handler));
        }

        // ------------------------------------------------------------------ running
        public void Step()
        {
            int yearBefore = SimTime.Year(_sim.State.Tick);
            BeforeStep?.Invoke(this);
            _sim.Step();
            TicksRun++;
            ProcessPendingCaptureChecks();
            AfterStep?.Invoke(this);
            long t = _sim.State.Tick;
            if (SampleEveryTicks > 0 && t % SampleEveryTicks == 0) Sample();
            int yearAfter = SimTime.Year(t);
            if (yearAfter != yearBefore) CloseYear(yearBefore);
        }

        public void RunTicks(int ticks) { for (int i = 0; i < ticks; i++) Step(); }

        public void RunYears(int years) => RunTicks(years * SimTime.TicksPerYear);

        /// <summary>Steps until the condition holds (checked before each step) or maxTicks elapse. Returns the condition.</summary>
        public bool RunUntil(Func<ScenarioHarness, bool> condition, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (condition(this)) return true;
                Step();
            }
            return condition(this);
        }

        private void CloseYear(int year)
        {
            var m = YearOf(year);
            m.PopulationAtEnd = _sim.State.LivingPopulation;
            int s = 0;
            foreach (var _ in ScenarioWorlds.AliveSettlements(_sim.State)) s++;
            m.SettlementsAtEnd = s;
            m.CivsAtEnd = ScenarioWorlds.AliveCivCount(_sim.State);
            OnYearClosed?.Invoke(this, m);
        }

        // ------------------------------------------------------------------ god powers
        /// <summary>Casts through GodSimulation.TryCastPower and steps until the divine module reports it (or maxWaitTicks).</summary>
        public CastOutcome Cast(GodPowerType power, Vec2i target, int radius, int parameter = 0, int maxWaitTicks = 4)
        {
            var o = new CastOutcome
            {
                Power = power, Target = target, Radius = radius, Parameter = parameter,
                ManaBefore = _sim.State.God.Mana, IssuedTick = _sim.State.Tick,
            };
            int casts0 = Casts.Count, rej0 = Rejections.Count, destroyed0 = Destructions.Count, deaths0 = Deaths.Count, civs0 = CivsFounded.Count;
            o.Accepted = _sim.TryCastPower(power, target, radius, parameter, out o.Error);
            if (o.Accepted)
            {
                // Some implementations execute synchronously; otherwise the divine system drains the queue next tick.
                for (int i = 0; i <= maxWaitTicks; i++)
                {
                    if (Resolve(o, casts0, rej0)) break;
                    if (i == maxWaitTicks) break;
                    Step();
                    o.TicksWaited++;
                }
            }
            o.ManaAfter = _sim.State.God.Mana;
            o.BuildingsDestroyed = Destructions.Count - destroyed0;
            for (int i = deaths0; i < Deaths.Count; i++) if (Deaths[i].Event.Cause != DeathCause.OldAge) o.UnnaturalDeaths++;
            o.CivsFounded = CivsFounded.Count - civs0;
            return o;
        }

        private bool Resolve(CastOutcome o, int casts0, int rej0)
        {
            for (int i = casts0; i < Casts.Count; i++)
                if (Casts[i].Event.Power == o.Power)
                {
                    o.Executed = true; o.Success = Casts[i].Event.Success; o.Message = Casts[i].Event.Message; o.ResolvedTick = Casts[i].Tick;
                    return true;
                }
            for (int i = rej0; i < Rejections.Count; i++)
                if (Rejections[i].Event.Power == o.Power)
                {
                    o.RejectedAtExecution = true; o.RejectReason = Rejections[i].Event.Reason; o.ResolvedTick = Rejections[i].Tick;
                    return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ sampling
        public void Sample()
        {
            var s = _sim.State;
            int settlements = 0;
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].IsAlive) continue;
                settlements++;
                if (SampleSettlements) Samples.Add(SampleSettlement(items[i]));
            }
            int armies = 0;
            var a = s.Diplomacy.Armies.Items;
            for (int i = 0; i < a.Count; i++) if (a[i].IsAlive) armies++;
            WorldSamples.Add(new WorldSample
            {
                Tick = s.Tick,
                Population = s.LivingPopulation,
                Settlements = settlements,
                Civilizations = ScenarioWorlds.AliveCivCount(s),
                Buildings = s.Buildings.Count,
                Armies = armies,
                ActiveWars = ScenarioWorlds.ActiveWarCount(s),
                Mana = s.God.Mana,
                DroughtTicksRemaining = s.Weather.DroughtTicksRemaining,
            });
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) NoteAge(civs[i].Id, civs[i].Age);
            if (CheckHousingOnSample) CheckHousing();
            OnSample?.Invoke(this);
        }

        public SettlementSample SampleSettlement(Settlement st)
        {
            var s = _sim.State;
            var db = _sim.Db;
            var smp = new SettlementSample
            {
                Tick = s.Tick, SettlementId = st.Id, CivilizationId = st.CivilizationId, IsCapital = st.IsCapital,
                Population = st.Population, ReportedHomeless = st.Homeless, Food = st.Stockpile.Get(ResourceType.Food),
            };
            foreach (int pid in st.ResidentIds)
            {
                var p = s.People.Get(pid);
                if (p == null || !p.IsAlive) continue;
                if (p.IsAdult) smp.Adults++;
                if (ScenarioWorlds.IsFoodJob(p.Job)) smp.FoodWorkers++;
                Building home = p.HomeId >= 0 ? s.Buildings.Get(p.HomeId) : null;
                if (home == null || home.IsDestroyed || !home.IsComplete) smp.Homeless++;
            }
            foreach (int bid in st.BuildingIds)
            {
                var b = s.Buildings.Get(bid);
                if (b == null || b.IsDestroyed) continue;
                if (!b.IsComplete) { smp.IncompleteBuildings++; continue; }
                smp.CompleteBuildings++;
                if (ScenarioWorlds.IsFoodProducer(db, b.Type)) smp.FoodProducers++;
                if (b.Type == BuildingType.House)
                {
                    smp.CompleteHouses++;
                    smp.HouseResidents += b.Residents.Count;
                    smp.HouseCapacity += b.HousingCapacity;
                }
            }
            return smp;
        }

        /// <summary>Records violations of the dwelling rules: residents never exceed capacity; Houses hold 3-5; no one lives in a construction site.</summary>
        public void CheckHousing()
        {
            var b = _sim.State.Buildings.Items;
            for (int i = 0; i < b.Count; i++)
            {
                var x = b[i];
                if (x.IsDestroyed) continue;
                if (x.Residents.Count > x.HousingCapacity)
                    Housing(x.Type + " #" + x.Id + " has " + x.Residents.Count + " residents > capacity " + x.HousingCapacity);
                if (x.Type == BuildingType.House && (x.HousingCapacity < 3 || x.HousingCapacity > 5))
                    Housing("House #" + x.Id + " HousingCapacity " + x.HousingCapacity + " outside 3..5");
                if (!x.IsComplete && x.Residents.Count > 0)
                    Housing(x.Type + " #" + x.Id + " is under construction but has " + x.Residents.Count + " residents");
            }
        }

        private void Housing(string msg)
        {
            HousingViolationCount++;
            Add(HousingViolations, SimTime.Format(_sim.State.Tick) + ": " + msg);
        }

        private static void Add(List<string> list, string msg) { if (list.Count < MaxViolationsKept) list.Add(msg); }

        private void NoteAge(int civId, AgeId age)
        {
            if (!MaxAgeReached.TryGetValue(civId, out var a) || age > a) MaxAgeReached[civId] = age;
            if (a > age && MaxAgeReached.ContainsKey(civId))
            {
                var civ = _sim.State.Civilizations.Get(civId);
                if (civ != null && civ.IsAlive) Add(AgeViolations, SimTime.Format(Now) + ": civ " + civId + " regressed from " + a + " to " + age);
            }
        }

        // ------------------------------------------------------------------ handlers (must never throw)
        private void OnBorn(PersonBornEvent e) { Y().Births++; }

        private void OnDied(PersonDiedEvent e)
        {
            int c = (int)e.Cause;
            if (c >= 0 && c < YearMetrics.DeathCauseCount) Y().DeathsByCause[c]++;
            Deaths.Add(new Timed<PersonDiedEvent>(Now, e));
        }

        private void OnMigrated(PersonMigratedEvent e) { Y().Migrations++; }

        private void OnPlaced(BuildingPlacedEvent e) { Y().BuildingsPlaced++; }

        private void OnCompleted(BuildingCompletedEvent e)
        {
            int t = (int)e.Type;
            if (t >= 0 && t < Counts.Buildings) Y().BuildingsCompletedByType[t]++;
            Completions.Add(new Timed<BuildingCompletedEvent>(Now, e));
        }

        private void OnDestroyed(BuildingDestroyedEvent e)
        {
            int c = (int)e.Cause;
            if (c >= 0 && c < YearMetrics.DestroyCauseCount) Y().BuildingsDestroyedByCause[c]++;
            Destructions.Add(new Timed<BuildingDestroyedEvent>(Now, e));
        }

        private void OnUpgraded(BuildingUpgradedEvent e)
        {
            Y().BuildingsUpgraded++;
            var rec = new UpgradeRecord { Tick = Now, BuildingId = e.BuildingId, NewLevel = e.NewLevel, StyleAge = e.StyleAge, CivilizationId = -1, SettlementId = -1, PlacedTick = -1 };
            var b = _sim.State.Buildings.Get(e.BuildingId);
            if (b == null)
                Add(UpgradeViolations, SimTime.Format(Now) + ": BuildingUpgradedEvent for unknown building #" + e.BuildingId);
            else
            {
                rec.Type = b.Type;
                rec.CivilizationId = b.CivilizationId;
                rec.SettlementId = b.SettlementId;
                rec.PlacedTick = b.PlacedTick;
                if (b.StyleAge != e.StyleAge)
                    Add(UpgradeViolations, SimTime.Format(Now) + ": upgrade event StyleAge " + e.StyleAge + " but building #" + b.Id + " has StyleAge " + b.StyleAge);
                var civ = _sim.State.Civilizations.Get(b.CivilizationId);
                if (civ != null)
                {
                    rec.OwnerAgeAtEvent = civ.Age;
                    if (e.StyleAge > civ.Age)
                        Add(UpgradeViolations, SimTime.Format(Now) + ": building #" + b.Id + " renovated to " + e.StyleAge + " beyond owner age " + civ.Age);
                }
            }
            Upgrades.Add(rec);
        }

        private void OnAgeAdvanced(AgeAdvancedEvent e)
        {
            Y().AgeAdvancements++;
            AgeAdvances.Add(new Timed<AgeAdvancedEvent>(Now, e));
            string at = SimTime.Format(Now) + ": civ " + e.CivilizationId + " " + e.OldAge + " -> " + e.NewAge;
            if ((int)e.NewAge != (int)e.OldAge + 1) Add(AgeViolations, at + " skips/repeats an age");
            if (_lastAge.TryGetValue(e.CivilizationId, out var last) && last != e.OldAge)
                Add(AgeViolations, at + " but the previous known age was " + last);
            _lastAge[e.CivilizationId] = e.NewAge;
            if (!MaxAgeReached.TryGetValue(e.CivilizationId, out var max) || e.NewAge > max) MaxAgeReached[e.CivilizationId] = e.NewAge;
            var civ = _sim.State.Civilizations.Get(e.CivilizationId);
            if (civ == null) { Add(AgeViolations, at + " for unknown civ"); return; }
            if ((int)e.NewAge < Counts.Ages && _sim.Db.Ages[(int)e.NewAge] != null)
            {
                var def = _sim.Db.Ages[(int)e.NewAge];
                if (civ.TotalResearch + 1e-3f < def.ResearchThreshold)
                    Add(AgeViolations, at + " with TotalResearch " + civ.TotalResearch.ToString("0.0") + " < threshold " + def.ResearchThreshold.ToString("0.0"));
                if (def.MinPopulation > 0)
                {
                    int pop = ScenarioWorlds.CountCivPopulation(_sim.State, civ.Id);
                    if (pop < def.MinPopulation * 0.75f)
                        Add(AgeViolations, at + " with population " + pop + " far below MinPopulation " + def.MinPopulation);
                }
            }
        }

        private void OnTech(TechResearchedEvent e) { Y().TechsResearched++; }

        private void OnWarDeclared(WarDeclaredEvent e)
        {
            Y().WarsDeclared++;
            WarsDeclared.Add(new Timed<WarDeclaredEvent>(Now, e));
            if (e.AttackerId == e.DefenderId) Add(BattleViolations, SimTime.Format(Now) + ": civ " + e.AttackerId + " declared war on itself");
        }

        private void OnPeace(PeaceSignedEvent e)
        {
            Y().PeacesSigned++;
            PeacesSigned.Add(new Timed<PeaceSignedEvent>(Now, e));
        }

        private void OnBattle(BattleEvent e)
        {
            Y().Battles++;
            Battles.Add(new Timed<BattleEvent>(Now, e));
            string at = SimTime.Format(Now) + ": battle " + e.AttackerCivId + " vs " + e.DefenderCivId + " at " + e.Position;
            if (e.AttackerCivId == e.DefenderCivId) Add(BattleViolations, at + " (same civ on both sides)");
            else if (e.AttackerCivId >= 0 && e.DefenderCivId >= 0 && !_sim.State.Diplomacy.AreAtWar(e.AttackerCivId, e.DefenderCivId))
                Add(BattleViolations, at + " while the civs are not at war");
            if (e.AttackerLosses < 0 || e.DefenderLosses < 0) Add(BattleViolations, at + " has negative losses");
        }

        private void OnSettlementFounded(SettlementFoundedEvent e)
        {
            Y().SettlementsFounded++;
            SettlementsFounded.Add(new Timed<SettlementFoundedEvent>(Now, e));
        }

        private void OnSettlementDestroyed(SettlementDestroyedEvent e)
        {
            Y().SettlementsDestroyed++;
            SettlementsDestroyed.Add(new Timed<SettlementDestroyedEvent>(Now, e));
        }

        private void OnCaptured(SettlementCapturedEvent e)
        {
            Y().SettlementsCaptured++;
            Captures.Add(new Timed<SettlementCapturedEvent>(Now, e));
            string at = SimTime.Format(Now) + ": capture of settlement " + e.SettlementId + " " + e.OldCivilizationId + " -> " + e.NewCivilizationId;
            if (e.OldCivilizationId == e.NewCivilizationId) Add(CaptureViolations, at + " (no owner change)");
            var st = _sim.State.Settlements.Get(e.SettlementId);
            if (st == null) { Add(CaptureViolations, at + " for unknown settlement"); return; }
            if (st.CivilizationId != e.NewCivilizationId) Add(CaptureViolations, at + " but settlement.CivilizationId=" + st.CivilizationId + " at event time");
            CheckOwnership(st, e.NewCivilizationId, at + " [event time]");
            _pendingCaptureChecks.Add(e);
        }

        private void ProcessPendingCaptureChecks()
        {
            if (_pendingCaptureChecks.Count == 0) return;
            var s = _sim.State;
            for (int i = 0; i < _pendingCaptureChecks.Count; i++)
            {
                var e = _pendingCaptureChecks[i];
                bool recapturedSameTick = false;
                for (int j = i + 1; j < _pendingCaptureChecks.Count; j++) if (_pendingCaptureChecks[j].SettlementId == e.SettlementId) recapturedSameTick = true;
                if (recapturedSameTick) continue;
                string at = SimTime.Format(Now) + ": after capture of settlement " + e.SettlementId + " " + e.OldCivilizationId + " -> " + e.NewCivilizationId;
                var st = s.Settlements.Get(e.SettlementId);
                if (st == null || !st.IsAlive) continue; // razed in the same tick: nothing left to own
                if (st.CivilizationId != e.NewCivilizationId) { Add(CaptureViolations, at + ": owner is " + st.CivilizationId); continue; }
                CheckOwnership(st, e.NewCivilizationId, at);
                var newCiv = s.Civilizations.Get(e.NewCivilizationId);
                if (newCiv == null) Add(CaptureViolations, at + ": new owner civ missing");
                else if (!newCiv.SettlementIds.Contains(st.Id)) Add(CaptureViolations, at + ": new owner's SettlementIds lacks it");
                var oldCiv = s.Civilizations.Get(e.OldCivilizationId);
                if (oldCiv != null)
                {
                    if (oldCiv.SettlementIds.Contains(st.Id)) Add(CaptureViolations, at + ": old owner still lists it");
                    if (oldCiv.CapitalId == st.Id) Add(CaptureViolations, at + ": old owner's capital still points at it");
                }
            }
            _pendingCaptureChecks.Clear();
        }

        private void CheckOwnership(Settlement st, int civId, string at)
        {
            var s = _sim.State;
            int badPeople = 0, badBuildings = 0;
            foreach (int pid in st.ResidentIds) { var p = s.People.Get(pid); if (p != null && p.IsAlive && p.CivilizationId != civId) badPeople++; }
            foreach (int bid in st.BuildingIds) { var b = s.Buildings.Get(bid); if (b != null && !b.IsDestroyed && b.CivilizationId != civId) badBuildings++; }
            if (badPeople > 0) Add(CaptureViolations, at + ": " + badPeople + " residents keep a different CivilizationId");
            if (badBuildings > 0) Add(CaptureViolations, at + ": " + badBuildings + " buildings keep a different CivilizationId");
        }

        private void OnCivFounded(CivilizationFoundedEvent e)
        {
            Y().CivsFounded++;
            CivsFounded.Add(new Timed<CivilizationFoundedEvent>(Now, e));
            var civ = _sim.State.Civilizations.Get(e.CivilizationId);
            if (civ != null && !_lastAge.ContainsKey(civ.Id)) { _lastAge[civ.Id] = civ.Age; MaxAgeReached[civ.Id] = civ.Age; }
        }

        private void OnCivCollapsed(CivilizationCollapsedEvent e)
        {
            Y().CivsCollapsed++;
            CivsCollapsed.Add(new Timed<CivilizationCollapsedEvent>(Now, e));
        }

        private void OnFamine(FamineEvent e)
        {
            Y().Famines++;
            Famines.Add(new Timed<FamineEvent>(Now, e));
        }

        private void OnDisaster(DisasterEvent e)
        {
            int t = (int)e.Type;
            if (t >= 0 && t < YearMetrics.DisasterTypeCount) Y().DisastersByType[t]++;
            Disasters.Add(new Timed<DisasterEvent>(Now, e));
        }

        private void OnCast(GodPowerCastEvent e)
        {
            if (e.Success) Y().GodPowersSucceeded++; else Y().GodPowersFailed++;
            Casts.Add(new Timed<GodPowerCastEvent>(Now, e));
        }

        private void OnRejected(GodCommandRejectedEvent e)
        {
            Y().GodCommandsRejected++;
            Rejections.Add(new Timed<GodCommandRejectedEvent>(Now, e));
        }

        // ------------------------------------------------------------------ reporting
        /// <summary>Compact table of metrics, one line per <paramref name="bucketYears"/> years.</summary>
        public string Summary(int bucketYears = 10)
        {
            var sb = new StringBuilder();
            sb.AppendLine("years      births deaths(old/starve/disease/violence/disaster/divine) built destroyed upgrades ages wars peace battles founded razed captured famines disasters pop  settl civs");
            YearMetrics acc = null;
            int from = int.MinValue;
            foreach (var m in _years.Values)
            {
                if (acc == null) { acc = new YearMetrics(); from = m.Year; }
                Accumulate(acc, m);
                if (m.Year - from + 1 >= bucketYears) { AppendRow(sb, from, m.Year, acc); acc = null; }
            }
            if (acc != null) AppendRow(sb, from, from + bucketYears - 1, acc);
            sb.Append("profiles: ").AppendLine(ScenarioWorlds.DescribeProfiles(_sim.Engine));
            if (Log != null && Log.ErrorCount > 0) sb.AppendLine("log errors: " + Log.ErrorCount + "\n" + Log.Tail(5));
            return sb.ToString();
        }

        private static void Accumulate(YearMetrics acc, YearMetrics m)
        {
            acc.Births += m.Births;
            for (int i = 0; i < acc.DeathsByCause.Length; i++) acc.DeathsByCause[i] += m.DeathsByCause[i];
            for (int i = 0; i < acc.BuildingsCompletedByType.Length; i++) acc.BuildingsCompletedByType[i] += m.BuildingsCompletedByType[i];
            for (int i = 0; i < acc.BuildingsDestroyedByCause.Length; i++) acc.BuildingsDestroyedByCause[i] += m.BuildingsDestroyedByCause[i];
            for (int i = 0; i < acc.DisastersByType.Length; i++) acc.DisastersByType[i] += m.DisastersByType[i];
            acc.BuildingsUpgraded += m.BuildingsUpgraded;
            acc.AgeAdvancements += m.AgeAdvancements;
            acc.WarsDeclared += m.WarsDeclared;
            acc.PeacesSigned += m.PeacesSigned;
            acc.Battles += m.Battles;
            acc.SettlementsFounded += m.SettlementsFounded;
            acc.SettlementsDestroyed += m.SettlementsDestroyed;
            acc.SettlementsCaptured += m.SettlementsCaptured;
            acc.Famines += m.Famines;
            if (m.PopulationAtEnd >= 0) { acc.PopulationAtEnd = m.PopulationAtEnd; acc.SettlementsAtEnd = m.SettlementsAtEnd; acc.CivsAtEnd = m.CivsAtEnd; }
        }

        private static void AppendRow(StringBuilder sb, int from, int to, YearMetrics a)
        {
            int disasters = 0;
            for (int i = 0; i < a.DisastersByType.Length; i++) disasters += a.DisastersByType[i];
            sb.Append((from + "-" + to).PadRight(10)).Append(' ')
              .Append(a.Births.ToString().PadLeft(6)).Append(' ')
              .Append(a.Deaths.ToString().PadLeft(6)).Append(" (")
              .Append(a.DeathsOf(DeathCause.OldAge)).Append('/').Append(a.DeathsOf(DeathCause.Starvation)).Append('/')
              .Append(a.DeathsOf(DeathCause.Disease)).Append('/').Append(a.DeathsOf(DeathCause.Violence)).Append('/')
              .Append(a.DeathsOf(DeathCause.Disaster)).Append('/').Append(a.DeathsOf(DeathCause.DivineWrath)).Append(") ")
              .Append(a.BuildingsCompleted.ToString().PadLeft(5)).Append(' ')
              .Append(a.BuildingsDestroyed.ToString().PadLeft(9)).Append(' ')
              .Append(a.BuildingsUpgraded.ToString().PadLeft(8)).Append(' ')
              .Append(a.AgeAdvancements.ToString().PadLeft(4)).Append(' ')
              .Append(a.WarsDeclared.ToString().PadLeft(4)).Append(' ')
              .Append(a.PeacesSigned.ToString().PadLeft(5)).Append(' ')
              .Append(a.Battles.ToString().PadLeft(7)).Append(' ')
              .Append(a.SettlementsFounded.ToString().PadLeft(7)).Append(' ')
              .Append(a.SettlementsDestroyed.ToString().PadLeft(5)).Append(' ')
              .Append(a.SettlementsCaptured.ToString().PadLeft(8)).Append(' ')
              .Append(a.Famines.ToString().PadLeft(7)).Append(' ')
              .Append(disasters.ToString().PadLeft(9)).Append(' ')
              .Append(a.PopulationAtEnd.ToString().PadLeft(4)).Append(' ')
              .Append(a.SettlementsAtEnd.ToString().PadLeft(5)).Append(' ')
              .Append(a.CivsAtEnd.ToString().PadLeft(4)).AppendLine();
        }
    }
}
