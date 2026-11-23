using System;
using System.Collections.Generic;

namespace GodSim.Core.History
{
    /// <summary>
    /// Records world history for HUD charts, the headless exporter and the chronicle UI. Read-only with respect
    /// to the simulation: it never mutates WorldState, never touches <c>ctx.Rng</c>, and so cannot affect
    /// determinism or state hashes.
    /// <para>Every season (TickInterval 24) it appends a <see cref="WorldSample"/> and one <see cref="CivSample"/>
    /// per living civilization into bounded series (default 2,000 samples each). When a series is full it is
    /// downsampled 2:1, so long games keep an evenly spaced timeline from the first sample to the latest one.</para>
    /// <para>It also listens to notable events and keeps a bounded <see cref="Eras"/> timeline (default 5,000
    /// entries, oldest evicted first) and a <see cref="LifespanTracker"/> of ages at death.</para>
    /// <para><b>In-memory only.</b> Nothing here is saved: after a save is loaded (a new engine is composed) the
    /// recorder starts empty from the load tick. The persistent <c>WorldState.Chronicle</c> survives and
    /// <see cref="ChronicleNarrator"/> still works over it.</para>
    /// <para>Find the instance with <see cref="Find"/> instead of a static singleton (no global mutable state).</para>
    /// </summary>
    public sealed class HistoryRecorder : ISimSystem
    {
        public const int DefaultSampleCapacity = 2000;
        public const int DefaultTimelineCapacity = 5000;
        /// <summary>Upper bound on civilizations with stored series (collapsed civs are evicted first).</summary>
        public const int MaxTrackedCivilizations = 256;

        private static readonly int CauseCount = Enum.GetValues(typeof(DeathCause)).Length;
        private static readonly float[] EmptySeries = new float[0];

        private readonly int _sampleCapacity;
        private readonly DownsamplingBuffer<WorldSample> _world;
        private readonly BoundedRing<TimelineEvent> _timeline;
        private readonly LifespanTracker _lifespans = new LifespanTracker();
        private readonly Dictionary<int, CivHistory> _civById = new Dictionary<int, CivHistory>();
        private readonly List<CivHistory> _civOrder = new List<CivHistory>();
        private readonly List<int> _civIds = new List<int>();
        private readonly ReadOnlyListView<int> _civIdsView;
        private readonly long[] _lastDeathsByCause = new long[CauseCount];

        private SimContext _ctx;
        private EventBus _bus;
        private long _lastBirths;
        private long _lastDeaths;
        private long _lastSampleTick = long.MinValue;

        // Cached handlers (allocated once) so they can be unsubscribed.
        private readonly Action<CivilizationFoundedEvent> _onCivFounded;
        private readonly Action<AgeAdvancedEvent> _onAge;
        private readonly Action<WarDeclaredEvent> _onWar;
        private readonly Action<PeaceSignedEvent> _onPeace;
        private readonly Action<SettlementFoundedEvent> _onSettlementFounded;
        private readonly Action<SettlementCapturedEvent> _onCaptured;
        private readonly Action<CivilizationCollapsedEvent> _onCollapsed;
        private readonly Action<TechResearchedEvent> _onTech;
        private readonly Action<PersonDiedEvent> _onDied;

        /// <summary>Default capacities: 2,000 samples per series and 5,000 timeline entries.</summary>
        public HistoryRecorder() : this(DefaultSampleCapacity, DefaultTimelineCapacity) { }

        /// <param name="sampleCapacity">Max stored samples per series (clamped to 4..100,000).</param>
        /// <param name="timelineCapacity">Max timeline entries (clamped to 1..100,000).</param>
        public HistoryRecorder(int sampleCapacity, int timelineCapacity)
        {
            _sampleCapacity = MathUtil.Clamp(sampleCapacity, 4, 100000);
            _world = new DownsamplingBuffer<WorldSample>(_sampleCapacity);
            _timeline = new BoundedRing<TimelineEvent>(MathUtil.Clamp(timelineCapacity, 1, 100000));
            _civIdsView = new ReadOnlyListView<int>(_civIds);
            _onCivFounded = OnCivFounded;
            _onAge = OnAgeAdvanced;
            _onWar = OnWarDeclared;
            _onPeace = OnPeaceSigned;
            _onSettlementFounded = OnSettlementFounded;
            _onCaptured = OnSettlementCaptured;
            _onCollapsed = OnCivCollapsed;
            _onTech = OnTechResearched;
            _onDied = OnPersonDied;
        }

        public string Name => "History";
        public int TickInterval => SimTime.TicksPerSeason;

        /// <summary>Max stored samples per series.</summary>
        public int SampleCapacity => _sampleCapacity;
        /// <summary>World samples, oldest first (first and latest always kept).</summary>
        public IReadOnlyList<WorldSample> World => _world;
        /// <summary>Raw sampling intervals represented by each stored world sample (1, 2, 4, ...).</summary>
        public long WorldStride => _world.Stride;
        /// <summary>Total sampling passes since this recorder started (including downsampled ones).</summary>
        public long SamplesTaken => _world.TotalAdded;
        /// <summary>Notable events, oldest first (bounded; the oldest are evicted).</summary>
        public IReadOnlyList<TimelineEvent> Eras => _timeline;
        /// <summary>Timeline entries evicted because of the cap.</summary>
        public long ErasEvicted => _timeline.Evicted;
        /// <summary>Ages at death tallied from PersonDiedEvent since the recorder started.</summary>
        public LifespanTracker Lifespans => _lifespans;
        /// <summary>Ids of civilizations with recorded series, in first-seen order (read-only live view).</summary>
        public IEnumerable<int> CivIds => _civIdsView;
        public int CivCount => _civIds.Count;

        /// <summary>Finds the recorder registered in an engine (null if none). Prefer this over global singletons.</summary>
        public static HistoryRecorder Find(SimulationEngine engine)
        {
            if (engine == null) return null;
            var systems = engine.Systems;
            for (int i = 0; i < systems.Count; i++)
                if (systems[i] is HistoryRecorder r) return r;
            return null;
        }

        public bool TryGetCiv(int civId, out CivHistory history) => _civById.TryGetValue(civId, out history);

        // ================================================================== ISimSystem
        public void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            Detach();
            _ctx = ctx;
            _bus = ctx.Events;
            if (_bus != null)
            {
                _bus.Subscribe(_onCivFounded);
                _bus.Subscribe(_onAge);
                _bus.Subscribe(_onWar);
                _bus.Subscribe(_onPeace);
                _bus.Subscribe(_onSettlementFounded);
                _bus.Subscribe(_onCaptured);
                _bus.Subscribe(_onCollapsed);
                _bus.Subscribe(_onTech);
                _bus.Subscribe(_onDied);
            }
            Rebaseline(ctx.State);
        }

        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            if (_ctx == null) _ctx = ctx;
            if (ctx.State.Tick == _lastSampleTick) return;
            _lastSampleTick = ctx.State.Tick;
            SampleWorld(ctx.State);
            SampleCivs(ctx.State);
        }

        /// <summary>Unsubscribes from the event bus (e.g. when the UI tears the world down). Data is kept.</summary>
        public void Detach()
        {
            if (_bus == null) return;
            _bus.Unsubscribe(_onCivFounded);
            _bus.Unsubscribe(_onAge);
            _bus.Unsubscribe(_onWar);
            _bus.Unsubscribe(_onPeace);
            _bus.Unsubscribe(_onSettlementFounded);
            _bus.Unsubscribe(_onCaptured);
            _bus.Unsubscribe(_onCollapsed);
            _bus.Unsubscribe(_onTech);
            _bus.Unsubscribe(_onDied);
            _bus = null;
        }

        /// <summary>Drops all recorded data (series, timeline, lifespans).</summary>
        public void Clear()
        {
            _world.Clear();
            _timeline.Clear();
            _lifespans.Clear();
            _civById.Clear();
            _civOrder.Clear();
            _civIds.Clear();
            _lastSampleTick = long.MinValue;
            if (_ctx != null) Rebaseline(_ctx.State);
        }

        // ================================================================== chart queries
        /// <summary>
        /// Up to <paramref name="maxPoints"/> evenly spaced values of a civ metric (first and latest included).
        /// maxPoints &lt;= 0 returns every stored sample. Unknown civ: empty array. Allocates the result.
        /// </summary>
        public float[] Series(int civId, CivMetric metric, int maxPoints)
        {
            if (!_civById.TryGetValue(civId, out var h) || h.Count == 0) return EmptySeries;
            int n = maxPoints <= 0 || maxPoints > h.Count ? h.Count : maxPoints;
            var result = new float[n];
            FillCiv(h, metric, result, n);
            return result;
        }

        /// <summary>Non-allocating variant: fills <paramref name="buffer"/> and returns the number of points written.</summary>
        public int Series(int civId, CivMetric metric, float[] buffer)
        {
            if (buffer == null || buffer.Length == 0 || !_civById.TryGetValue(civId, out var h) || h.Count == 0) return 0;
            int n = Math.Min(buffer.Length, h.Count);
            FillCiv(h, metric, buffer, n);
            return n;
        }

        /// <summary>Up to <paramref name="maxPoints"/> evenly spaced world values (first and latest included).</summary>
        public float[] WorldSeries(WorldMetric metric, int maxPoints)
        {
            int count = _world.Count;
            if (count == 0) return EmptySeries;
            int n = maxPoints <= 0 || maxPoints > count ? count : maxPoints;
            var result = new float[n];
            for (int i = 0; i < n; i++) result[i] = _world[DownsamplingBuffer<WorldSample>.ResampleIndex(i, count, n)].Get(metric);
            return result;
        }

        /// <summary>Up to <paramref name="maxPoints"/> evenly spaced values of deaths-by-cause per stored sample.</summary>
        public float[] WorldDeathSeries(DeathCause cause, int maxPoints)
        {
            int count = _world.Count;
            if (count == 0) return EmptySeries;
            int n = maxPoints <= 0 || maxPoints > count ? count : maxPoints;
            var result = new float[n];
            for (int i = 0; i < n; i++) result[i] = _world[DownsamplingBuffer<WorldSample>.ResampleIndex(i, count, n)].DeathsBy(cause);
            return result;
        }

        /// <summary>Ticks matching <see cref="Series(int, CivMetric, int)"/> points (x axis).</summary>
        public long[] SeriesTicks(int civId, int maxPoints)
        {
            if (!_civById.TryGetValue(civId, out var h) || h.Count == 0) return new long[0];
            int count = h.Count;
            int n = maxPoints <= 0 || maxPoints > count ? count : maxPoints;
            var result = new long[n];
            for (int i = 0; i < n; i++) result[i] = h.Buffer[DownsamplingBuffer<CivSample>.ResampleIndex(i, count, n)].Tick;
            return result;
        }

        private static void FillCiv(CivHistory h, CivMetric metric, float[] dst, int n)
        {
            int count = h.Count;
            for (int i = 0; i < n; i++) dst[i] = h.Buffer[DownsamplingBuffer<CivSample>.ResampleIndex(i, count, n)].Get(metric);
        }

        // ================================================================== sampling
        private void Rebaseline(WorldState s)
        {
            if (s == null) return;
            _lastBirths = s.Stats.TotalBirths;
            _lastDeaths = s.Stats.TotalDeaths;
            var dbc = s.Stats.DeathsByCause;
            for (int i = 0; i < _lastDeathsByCause.Length; i++) _lastDeathsByCause[i] = i < dbc.Length ? dbc[i] : 0;
        }

        private void SampleWorld(WorldState s)
        {
            var w = new WorldSample { Tick = s.Tick };

            var people = s.People.Items;
            int pop = 0;
            for (int i = 0; i < people.Count; i++) if (people[i].IsAlive) pop++;
            w.Population = pop;

            var settlements = s.Settlements.Items;
            double happy = 0; int happyPop = 0;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i];
                if (!st.IsAlive) continue;
                w.Settlements++;
                int sp = st.Population;
                happy += MathUtil.Sanitize(st.Happiness) * sp;
                happyPop += sp;
            }
            w.Happiness = happyPop > 0 ? MathUtil.Clamp01((float)(happy / happyPop)) : 0f;

            var buildings = s.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b.IsDestroyed) continue;
                if (b.IsComplete) w.Buildings++; else w.BuildingsUnderConstruction++;
            }

            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++) if (armies[i].IsAlive) w.Armies++;
            var wars = s.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++) if (wars[i] != null && wars[i].IsActive) w.ActiveWars++;

            var civs = s.Civilizations.Items;
            AgeId worldAge = AgeId.Dawn;
            for (int i = 0; i < civs.Count; i++)
            {
                if (!civs[i].IsAlive) continue;
                w.LivingCivilizations++;
                if (civs[i].Age > worldAge) worldAge = civs[i].Age;
            }
            w.WorldAge = worldAge;
            w.Mana = MathUtil.Sanitize(s.God.Mana);
            w.MaxMana = MathUtil.Sanitize(s.God.MaxMana);
            w.Alignment = MathUtil.Clamp(MathUtil.Sanitize(s.God.Alignment), -1f, 1f);

            // Births/deaths since the previous sample (a decreasing counter means the state was swapped: rebaseline).
            var st2 = s.Stats;
            w.TotalBirths = st2.TotalBirths;
            w.TotalDeaths = st2.TotalDeaths;
            w.Births = Delta(st2.TotalBirths, _lastBirths);
            w.Deaths = Delta(st2.TotalDeaths, _lastDeaths);
            _lastBirths = st2.TotalBirths;
            _lastDeaths = st2.TotalDeaths;
            var dbc = st2.DeathsByCause;
            int n = Math.Min(dbc.Length, _lastDeathsByCause.Length);
            for (int c = 0; c < n; c++)
            {
                w.AddDeaths((DeathCause)c, Delta(dbc[c], _lastDeathsByCause[c]));
                _lastDeathsByCause[c] = dbc[c];
            }
            _world.Add(w);
        }

        private static int Delta(long now, long before)
        {
            long d = now - before;
            if (d <= 0) return 0;
            return d > int.MaxValue ? int.MaxValue : (int)d;
        }

        private void SampleCivs(WorldState s)
        {
            long tick = s.Tick;
            for (int i = 0; i < _civOrder.Count; i++)
            {
                var h = _civOrder[i];
                h.AccPopulation = 0; h.AccSettlements = 0; h.AccBuildings = 0; h.AccHappiness = 0; h.SeenThisPass = false;
            }

            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                CivHistory h;
                if (!_civById.TryGetValue(civ.Id, out h))
                {
                    if (!civ.IsAlive) continue; // never seen alive: nothing to chart
                    h = Track(civ.Id);
                    if (h == null) continue;
                }
                if (!h.IsAlive) continue;   // closed series
                h.SeenThisPass = true;
            }

            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i];
                if (!st.IsAlive || !_civById.TryGetValue(st.CivilizationId, out var h) || !h.SeenThisPass) continue;
                int sp = st.Population;
                h.AccPopulation += sp;
                h.AccSettlements++;
                h.AccHappiness += MathUtil.Sanitize(st.Happiness) * sp;
            }

            var buildings = s.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b.IsDestroyed || !b.IsComplete) continue;
                if (_civById.TryGetValue(b.CivilizationId, out var h) && h.SeenThisPass) h.AccBuildings++;
            }

            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (!_civById.TryGetValue(civ.Id, out var h) || !h.SeenThisPass) continue;
                var sample = new CivSample { Tick = tick, Age = civ.Age, TotalResearch = Math.Max(0f, MathUtil.Sanitize(civ.TotalResearch)) };
                h.Name = civ.Name ?? "";
                h.Race = civ.Race;
                if (civ.IsAlive)
                {
                    sample.Population = h.AccPopulation;
                    sample.Settlements = h.AccSettlements;
                    sample.Buildings = h.AccBuildings;
                    sample.Happiness = h.AccPopulation > 0 ? MathUtil.Clamp01((float)(h.AccHappiness / h.AccPopulation)) : 0f;
                    sample.MilitaryPower = Math.Max(0f, MathUtil.Sanitize(civ.MilitaryPower));
                    sample.Devotion = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion));
                    sample.GodFavor = MathUtil.Clamp(MathUtil.Sanitize(civ.GodFavor), -1f, 1f);
                }
                else
                {
                    // Final sample: the collapse shows as a drop to zero; then the series is closed.
                    h.IsAlive = false;
                    h.CollapsedTick = civ.CollapsedTick >= 0 ? civ.CollapsedTick : tick;
                }
                if (h.FirstTick < 0) h.FirstTick = tick;
                h.LastTick = tick;
                h.Buffer.Add(sample);
                h.SeenThisPass = false;
            }
        }

        private CivHistory Track(int civId)
        {
            if (_civOrder.Count >= MaxTrackedCivilizations)
            {
                // Evict the closed (collapsed) series that ended earliest; if none, stop tracking new civs.
                int victim = -1;
                for (int i = 0; i < _civOrder.Count; i++)
                    if (!_civOrder[i].IsAlive && (victim < 0 || _civOrder[i].CollapsedTick < _civOrder[victim].CollapsedTick)) victim = i;
                if (victim < 0) return null;
                _civById.Remove(_civOrder[victim].CivId);
                _civOrder.RemoveAt(victim);
                _civIds.RemoveAt(victim);
            }
            var h = new CivHistory(civId, _sampleCapacity);
            _civById[civId] = h;
            _civOrder.Add(h);
            _civIds.Add(civId);
            return h;
        }

        // ================================================================== timeline
        private void AddEvent(TimelineEventKind kind, int civId, int otherCivId, int subjectId, int value, string label)
        {
            var s = _ctx?.State;
            string text = label ?? "";
            if (text.Length > 160) text = text.Substring(0, 160);
            _timeline.Add(new TimelineEvent
            {
                Tick = s != null ? s.Tick : 0,
                Kind = kind,
                CivId = civId,
                OtherCivId = otherCivId,
                SubjectId = subjectId,
                Value = value,
                Label = text,
            });
        }

        private string CivName(int civId)
        {
            var s = _ctx?.State;
            var c = s != null && civId >= 0 ? s.Civilizations.Get(civId) : null;
            return c != null && !string.IsNullOrEmpty(c.Name) ? c.Name : (civId >= 0 ? "Civilization #" + civId : "an unknown people");
        }

        private string SettlementName(int settlementId)
        {
            var s = _ctx?.State;
            var st = s != null && settlementId >= 0 ? s.Settlements.Get(settlementId) : null;
            return st != null && !string.IsNullOrEmpty(st.Name) ? st.Name : "a settlement";
        }

        private string AgeName(AgeId age)
        {
            var db = _ctx?.Db;
            int i = (int)age;
            if (db != null && i >= 0 && i < db.Ages.Count && db.Ages[i] != null && !string.IsNullOrEmpty(db.Ages[i].Name)) return db.Ages[i].Name;
            return age.ToString();
        }

        private void OnCivFounded(CivilizationFoundedEvent e) =>
            AddEvent(TimelineEventKind.CivilizationFounded, e.CivilizationId, -1, -1, (int)e.Race, CivName(e.CivilizationId) + " arose");

        private void OnAgeAdvanced(AgeAdvancedEvent e) =>
            AddEvent(TimelineEventKind.AgeAdvanced, e.CivilizationId, -1, (int)e.OldAge, (int)e.NewAge, CivName(e.CivilizationId) + " entered the " + AgeName(e.NewAge));

        private void OnWarDeclared(WarDeclaredEvent e) =>
            AddEvent(TimelineEventKind.WarDeclared, e.AttackerId, e.DefenderId, e.WarId, 0, CivName(e.AttackerId) + " declared war on " + CivName(e.DefenderId));

        private void OnPeaceSigned(PeaceSignedEvent e)
        {
            float score = MathUtil.Clamp(MathUtil.Sanitize(e.WarScore), -100f, 100f);
            AddEvent(TimelineEventKind.PeaceSigned, e.AttackerId, e.DefenderId, e.WarId, MathUtil.RoundToInt(score), CivName(e.AttackerId) + " and " + CivName(e.DefenderId) + " made peace");
        }

        private void OnSettlementFounded(SettlementFoundedEvent e) =>
            AddEvent(TimelineEventKind.SettlementFounded, e.CivilizationId, -1, e.SettlementId, e.ParentSettlementId, CivName(e.CivilizationId) + " founded " + SettlementName(e.SettlementId));

        private void OnSettlementCaptured(SettlementCapturedEvent e) =>
            AddEvent(TimelineEventKind.SettlementCaptured, e.NewCivilizationId, e.OldCivilizationId, e.SettlementId, 0,
                CivName(e.NewCivilizationId) + " captured " + SettlementName(e.SettlementId) + " from " + CivName(e.OldCivilizationId));

        private void OnCivCollapsed(CivilizationCollapsedEvent e) =>
            AddEvent(TimelineEventKind.CivilizationCollapsed, e.CivilizationId, e.ConquerorId, -1, 0,
                e.ConquerorId >= 0 ? CivName(e.CivilizationId) + " fell to " + CivName(e.ConquerorId) : CivName(e.CivilizationId) + " collapsed");

        private void OnTechResearched(TechResearchedEvent e)
        {
            var tech = _ctx?.Db?.Tech(e.TechId);
            string tn = tech != null && !string.IsNullOrEmpty(tech.Name) ? tech.Name : "a new art";
            AddEvent(TimelineEventKind.TechResearched, e.CivilizationId, -1, e.TechId, 0, CivName(e.CivilizationId) + " discovered " + tn);
        }

        private void OnPersonDied(PersonDiedEvent e)
        {
            var s = _ctx?.State;
            var p = s != null ? s.People.Get(e.PersonId) : null; // removal is deferred: still in the store
            RaceId race = p != null ? p.Race : (RaceId)255;
            _lifespans.Record(race, e.Cause, e.AgeYears);
        }
    }
}
