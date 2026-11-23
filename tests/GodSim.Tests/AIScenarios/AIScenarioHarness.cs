using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Tests.AIScenarios
{
    /// <summary>One AiLog line observed while the harness was running.</summary>
    public struct AiLogLine
    {
        public long Tick;
        public int SettlementId;
        public string Text;
    }

    /// <summary>Yearly state sample of one settlement (taken right after the year rolled over).</summary>
    public sealed class SettlementYearSample
    {
        public int Year;
        public int SettlementId;
        public int CivilizationId;
        public RaceId Race;
        public bool IsCapital;
        public int Population;
        public int Homeless;
        public int Houses;            // complete, standing House buildings
        public int HousingCapacity;   // sum of HousingCapacity of complete, standing dwellings
        public int FoodBuildings;     // complete, standing Category == Food
        public int QueueLength;
        public float Food;

        public float HomelessShare => Population <= 0 ? 0f : Homeless / (float)Population;
    }

    /// <summary>Snapshot of a capital (composition, logs, queue) at a chosen year.</summary>
    public sealed class CapitalSnapshot
    {
        public int Year;
        public int CivilizationId;
        public RaceId Race;
        public int SettlementId;
        public string Name = "";
        public AgeId Age;
        public int Population;
        public int Houses;
        public int HousingCapacity;
        public int FoodBuildings;
        public bool HasTownCenter;
        public bool HasWaterOrStorage;
        public readonly int[] CompleteByType = new int[Counts.Buildings];
        public readonly int[] CompleteByCategory = new int[Enum.GetValues(typeof(BuildingCategory)).Length];
        public readonly List<string> AiLog = new List<string>();

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Race).Append(" capital '").Append(Name).Append("' y").Append(Year).Append(" age=").Append(Age)
              .Append(" pop=").Append(Population).Append(" houses=").Append(Houses).Append(" cap=").Append(HousingCapacity)
              .Append(" food=").Append(FoodBuildings).Append(" TC=").Append(HasTownCenter).Append(" water/storage=").Append(HasWaterOrStorage)
              .Append(" mix=[");
            for (int c = 0; c < CompleteByCategory.Length; c++)
                if (CompleteByCategory[c] > 0) sb.Append((BuildingCategory)c).Append(':').Append(CompleteByCategory[c]).Append(' ');
            sb.Append(']');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Drives a full <see cref="GodSimulation"/> (every module, every AI system) and records AI-level metrics:
    /// per-tick build-queue churn, AiLog history (diffed every tick, so the 16-line rolling buffer loses
    /// nothing unless &gt;16 lines are written in a single tick), placed/completed/destroyed/upgraded buildings
    /// and age advances via the EventBus, and yearly settlement samples (population, homeless, houses,
    /// housing capacity, food buildings, food stock). Capital snapshots can be requested for given years.
    /// </summary>
    public sealed class AIScenarioHarness : IDisposable
    {
        public const int MaxQueueAllowed = 8;
        public const int MaxSameTypeQueued = 2;
        private const int MaxLogLinesKept = 20000;

        private readonly List<Action> _unsub = new List<Action>();
        private readonly Dictionary<int, List<string>> _lastAiLog = new Dictionary<int, List<string>>();
        private readonly int[] _typeCount = new int[Counts.Buildings];
        private readonly HashSet<int> _snapshotYears = new HashSet<int>();

        public AIScenarioHarness(SimConfig config)
        {
            Sim = GodSimulation.CreateNew(config);
            Sub<BuildingPlacedEvent>(e =>
            {
                Placed.Add(new Stamped<BuildingPlacedEvent>(Now, e));
                var b = State.Buildings.Get(e.BuildingId);
                if (b != null) PlacedStyle[e.BuildingId] = b.StyleAge;
            });
            Sub<BuildingCompletedEvent>(e => Completed.Add(new Stamped<BuildingCompletedEvent>(Now, e)));
            Sub<BuildingDestroyedEvent>(e => Destroyed.Add(new Stamped<BuildingDestroyedEvent>(Now, e)));
            Sub<BuildingUpgradedEvent>(e => Upgraded.Add(new Stamped<BuildingUpgradedEvent>(Now, e)));
            Sub<AgeAdvancedEvent>(e => AgeAdvances.Add(new Stamped<AgeAdvancedEvent>(Now, e)));
            Sub<PersonDiedEvent>(e => { if (e.Cause == DeathCause.Starvation) StarvationDeaths.Add(new Stamped<PersonDiedEvent>(Now, e)); });
        }

        /// <summary>Standard scenario config: 128x128, strict mode (system exceptions are rethrown).</summary>
        public static SimConfig Config(ulong seed, float researchMultiplier = 1f)
        {
            return new SimConfig { Seed = seed, MapWidth = 128, MapHeight = 128, StrictMode = true, ResearchMultiplier = researchMultiplier };
        }

        public GodSimulation Sim { get; }
        public WorldState State => Sim.State;
        public GameDatabase Db => Sim.Db;
        public long Now => Sim.State.Tick;
        public int Year => Sim.State.Year;

        /// <summary>Called after every tick (scenario hooks: watch a settlement's queue/jobs etc.).</summary>
        public Action<AIScenarioHarness> AfterStep;
        /// <summary>Record AiLog lines only of capitals (default) or of every settlement.</summary>
        public bool LogAllSettlements;

        // ------------------------------------------------------------------ records
        public readonly List<Stamped<BuildingPlacedEvent>> Placed = new List<Stamped<BuildingPlacedEvent>>();
        public readonly List<Stamped<BuildingCompletedEvent>> Completed = new List<Stamped<BuildingCompletedEvent>>();
        public readonly List<Stamped<BuildingDestroyedEvent>> Destroyed = new List<Stamped<BuildingDestroyedEvent>>();
        public readonly List<Stamped<BuildingUpgradedEvent>> Upgraded = new List<Stamped<BuildingUpgradedEvent>>();
        public readonly List<Stamped<AgeAdvancedEvent>> AgeAdvances = new List<Stamped<AgeAdvancedEvent>>();
        public readonly List<Stamped<PersonDiedEvent>> StarvationDeaths = new List<Stamped<PersonDiedEvent>>();
        public readonly List<AiLogLine> AiLines = new List<AiLogLine>();
        public readonly List<SettlementYearSample> YearSamples = new List<SettlementYearSample>();
        public readonly Dictionary<int, List<CapitalSnapshot>> Snapshots = new Dictionary<int, List<CapitalSnapshot>>();
        /// <summary>Building id -> StyleAge at placement time (kept after the building is destroyed).</summary>
        public readonly Dictionary<int, AgeId> PlacedStyle = new Dictionary<int, AgeId>();

        // churn
        public int MaxQueueSeen;
        public string MaxQueueWhere = "";
        public int MaxSameTypeSeen;
        public string MaxSameTypeWhere = "";
        public readonly List<string> ChurnViolations = new List<string>();

        public void RequestSnapshot(int year) => _snapshotYears.Add(year);

        // ------------------------------------------------------------------ running
        public void Step()
        {
            int before = Year;
            Sim.Step();
            ObserveQueues();
            ObserveAiLogs();
            AfterStep?.Invoke(this);
            if (Year != before)
            {
                SampleYear();
                if (_snapshotYears.Contains(Year)) TakeSnapshots(Year);
            }
        }

        public void RunTicks(int ticks) { for (int i = 0; i < ticks; i++) Step(); }

        public void RunYears(int years) => RunTicks(years * SimTime.TicksPerYear);

        /// <summary>Runs until the world year is at least <paramref name="year"/> (year rollover tick).</summary>
        public void RunUntilYear(int year) { while (Year < year) Step(); }

        // ------------------------------------------------------------------ observation
        private void ObserveQueues()
        {
            var items = State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (!s.IsAlive) continue;
                int n = s.BuildQueue.Count;
                if (n > MaxQueueSeen) { MaxQueueSeen = n; MaxQueueWhere = Where(s) + " queue=" + QueueText(s); }
                if (n > MaxQueueAllowed && ChurnViolations.Count < 50) ChurnViolations.Add("queue " + n + " > " + MaxQueueAllowed + " at " + Where(s) + ": " + QueueText(s));
                if (n == 0) continue;
                for (int q = 0; q < n; q++) _typeCount[(int)s.BuildQueue[q].Type]++;
                for (int q = 0; q < n; q++)
                {
                    var t = s.BuildQueue[q].Type;
                    int c = _typeCount[(int)t];
                    if (t == BuildingType.House || c == 0) continue;
                    if (c > MaxSameTypeSeen) { MaxSameTypeSeen = c; MaxSameTypeWhere = Where(s) + " " + t + " x" + c + " queue=" + QueueText(s); }
                    if (c > MaxSameTypeQueued && ChurnViolations.Count < 50) ChurnViolations.Add(t + " queued x" + c + " at " + Where(s) + ": " + QueueText(s));
                    _typeCount[(int)t] = 0; // report each type once per settlement per tick
                }
                for (int q = 0; q < n; q++) _typeCount[(int)s.BuildQueue[q].Type] = 0;
            }
        }

        private void ObserveAiLogs()
        {
            var items = State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (!s.IsAlive || (!LogAllSettlements && !s.IsCapital)) continue;
                if (!_lastAiLog.TryGetValue(s.Id, out var prev)) { prev = new List<string>(); _lastAiLog[s.Id] = prev; }
                var cur = s.AiLog;
                int overlap = Overlap(prev, cur);
                for (int k = overlap; k < cur.Count; k++)
                    if (AiLines.Count < MaxLogLinesKept) AiLines.Add(new AiLogLine { Tick = Now, SettlementId = s.Id, Text = cur[k] ?? "" });
                prev.Clear();
                prev.AddRange(cur);
            }
        }

        /// <summary>Largest k such that the last k lines of prev equal the first k lines of cur.</summary>
        private static int Overlap(List<string> prev, List<string> cur)
        {
            for (int k = Math.Min(prev.Count, cur.Count); k > 0; k--)
            {
                bool ok = true;
                for (int j = 0; j < k && ok; j++) ok = string.Equals(prev[prev.Count - k + j], cur[j], StringComparison.Ordinal);
                if (ok) return k;
            }
            return 0;
        }

        private void SampleYear()
        {
            var items = State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (!s.IsAlive) continue;
                YearSamples.Add(SampleOf(s, Year));
            }
        }

        public SettlementYearSample SampleOf(Settlement s, int year)
        {
            var smp = new SettlementYearSample
            {
                Year = year, SettlementId = s.Id, CivilizationId = s.CivilizationId, Race = s.Race, IsCapital = s.IsCapital,
                Population = s.Population, QueueLength = s.BuildQueue.Count, Food = s.Stockpile.Get(ResourceType.Food),
            };
            for (int r = 0; r < s.ResidentIds.Count; r++)
            {
                var p = State.People.Get(s.ResidentIds[r]);
                if (p != null && p.IsAlive && p.HomeId < 0) smp.Homeless++;
            }
            ForEachStanding(s, (b, def) =>
            {
                if (!b.IsComplete) return;
                if (b.Type == BuildingType.House) smp.Houses++;
                if (def.IsDwelling) smp.HousingCapacity += b.HousingCapacity;
                if (def.Category == BuildingCategory.Food) smp.FoodBuildings++;
            });
            return smp;
        }

        public void TakeSnapshots(int year)
        {
            var civs = State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (!civ.IsAlive) continue;
                var cap = State.Settlements.Get(civ.CapitalId);
                if (cap == null || !cap.IsAlive) continue;
                if (!Snapshots.TryGetValue(year, out var list)) { list = new List<CapitalSnapshot>(); Snapshots[year] = list; }
                list.Add(SnapshotOf(civ, cap, year));
            }
        }

        public CapitalSnapshot SnapshotOf(Civilization civ, Settlement cap, int year)
        {
            var snap = new CapitalSnapshot
            {
                Year = year, CivilizationId = civ.Id, Race = civ.Race, SettlementId = cap.Id, Name = cap.Name, Age = civ.Age,
                Population = cap.Population,
            };
            snap.AiLog.AddRange(cap.AiLog);
            ForEachStanding(cap, (b, def) =>
            {
                if (!b.IsComplete) return;
                snap.CompleteByType[(int)b.Type]++;
                snap.CompleteByCategory[(int)def.Category]++;
                if (b.Type == BuildingType.House) snap.Houses++;
                if (def.IsDwelling) snap.HousingCapacity += b.HousingCapacity;
                if (def.Category == BuildingCategory.Food) snap.FoodBuildings++;
                if (b.Type == BuildingType.TownCenter) snap.HasTownCenter = true;
                if (IsWaterOrStorage(def)) snap.HasWaterOrStorage = true;
            });
            return snap;
        }

        // ------------------------------------------------------------------ queries
        /// <summary>Well/Storehouse-class: any Storage building or any building providing the Water service.</summary>
        public static bool IsWaterOrStorage(BuildingDefinition def)
        {
            if (def == null) return false;
            if (def.Category == BuildingCategory.Storage) return true;
            if (def.Type == BuildingType.Well || def.Type == BuildingType.Storehouse || def.Type == BuildingType.Granary || def.Type == BuildingType.Aqueduct) return true;
            var svc = def.Services;
            if (svc != null) for (int i = 0; i < svc.Length; i++) if (svc[i].Type == ServiceType.Water && svc[i].Amount > 0f) return true;
            return false;
        }

        public void ForEachStanding(Settlement s, Action<Building, BuildingDefinition> visit)
        {
            for (int i = 0; i < s.BuildingIds.Count; i++)
            {
                var b = State.Buildings.Get(s.BuildingIds[i]);
                if (b == null || b.IsDestroyed) continue;
                if (!Db.TryGetBuilding(b.Type, out var def)) continue;
                visit(b, def);
            }
        }

        public int HousingCapacity(Settlement s)
        {
            int cap = 0;
            ForEachStanding(s, (b, def) => { if (b.IsComplete && def.IsDwelling) cap += b.HousingCapacity; });
            return cap;
        }

        public List<Settlement> Capitals()
        {
            var list = new List<Settlement>();
            var civs = State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                if (!civs[i].IsAlive) continue;
                var cap = State.Settlements.Get(civs[i].CapitalId);
                if (cap != null && cap.IsAlive) list.Add(cap);
            }
            return list;
        }

        /// <summary>Most populous living capital (ties: lowest id).</summary>
        public Settlement LargestCapital()
        {
            Settlement best = null;
            foreach (var c in Capitals()) if (best == null || c.Population > best.Population) best = c;
            return best;
        }

        public IEnumerable<AiLogLine> LinesOf(int settlementId, long fromTick, long toTick)
        {
            for (int i = 0; i < AiLines.Count; i++)
            {
                var l = AiLines[i];
                if (l.SettlementId == settlementId && l.Tick >= fromTick && l.Tick <= toTick) yield return l;
            }
        }

        /// <summary>A line "has a reason": the governor/advisor formats "Action X (...): why" or uses because/due to.</summary>
        public static bool HasReason(string line)
        {
            if (string.IsNullOrEmpty(line) || line.Length < 12) return false;
            string l = line.ToLowerInvariant();
            if (l.Contains("because") || l.Contains("due to") || l.Contains(" since ") || l.Contains("reason")) return true;
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            return colon > 0 && line.Length - colon - 2 >= 6;
        }

        public static string QueueText(Settlement s)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < s.BuildQueue.Count; i++) { if (i > 0) sb.Append(','); sb.Append(s.BuildQueue[i].Type); }
            return sb.Append(']').ToString();
        }

        public string Where(Settlement s) => s.Race + " '" + s.Name + "'#" + s.Id + " @" + SimTime.Format(Now);

        public static string Tail(IList<string> lines, int n)
        {
            var sb = new StringBuilder();
            for (int i = Math.Max(0, lines.Count - n); i < lines.Count; i++) sb.Append("\n    | ").Append(lines[i]);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ plumbing
        private void Sub<T>(Action<T> h) where T : struct
        {
            var bus = Sim.Events;
            bus.Subscribe(h);
            _unsub.Add(() => bus.Unsubscribe(h));
        }

        public void Dispose()
        {
            foreach (var u in _unsub) u();
            _unsub.Clear();
        }
    }

    /// <summary>An event plus the tick it was observed at.</summary>
    public struct Stamped<T> where T : struct
    {
        public long Tick;
        public T Event;
        public Stamped(long tick, T e) { Tick = tick; Event = e; }
        public int Year => SimTime.Year(Tick);
    }

    /// <summary>
    /// Shared long runs, computed once per process and reused by several criteria (growth, anticipation, race
    /// distinctness, explainability, churn) so the suite stays affordable. Tests run sequentially (NUnit default).
    /// </summary>
    public static class AIScenarioRuns
    {
        public const ulong BaselineSeed = 20261119UL;
        /// <summary>Seeds of the shared baseline worlds (criteria 1, 2, 7, 8, 9 are evaluated over all of them).</summary>
        public static readonly ulong[] BaselineSeeds = { BaselineSeed, 31337UL, 8675309UL };
        public const int GrowthYear = 60;
        public const int MixYear = 80;

        private static readonly Dictionary<ulong, AIScenarioHarness> s_baseline = new Dictionary<ulong, AIScenarioHarness>();

        /// <summary>Default-config world run to year 80 with capital snapshots at years 30, 60 and 80.</summary>
        public static AIScenarioHarness Baseline(ulong seed = BaselineSeed)
        {
            if (s_baseline.TryGetValue(seed, out var h)) return h;
            h = new AIScenarioHarness(AIScenarioHarness.Config(seed));
            h.RequestSnapshot(30);
            h.RequestSnapshot(GrowthYear);
            h.RequestSnapshot(MixYear);
            h.RunUntilYear(MixYear);
            h.Dispose();
            s_baseline[seed] = h;
            return h;
        }
    }
}
