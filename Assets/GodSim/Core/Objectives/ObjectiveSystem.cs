using System;
using System.Collections.Generic;

namespace GodSim.Core.Objectives
{
    /// <summary>
    /// Tracks the god's objectives (quests, milestones, achievements) and grants their rewards.
    /// <para>
    /// Event handlers (subscribed in <see cref="Initialize"/>) only tally counters in WorldState.Memory
    /// ("obj.count.*"), tag civilizations spawned by the god (Civilization.Memory "obj.spawnedByGod") and record the
    /// Ascension victory ("game.ascended"). The periodic <see cref="Tick"/> (every 24 ticks) measures every
    /// unfinished objective, writes "obj.&lt;key&gt;.progress", and on fulfilment writes "obj.&lt;key&gt;.done", grants the
    /// reward (clamped) and adds a chronicle entry. Everything that must survive a save lives in Memory, so a
    /// freshly created system resumes exactly where the previous one stopped.
    /// </para>
    /// </summary>
    public sealed class ObjectiveSystem : ISimSystem
    {
        /// <summary>Periodic evaluation interval in ticks (one season).</summary>
        public const int Interval = 24;
        /// <summary>Upper bound for God.MaxMana after rewards.</summary>
        public const float MaxManaCap = 10000f;
        /// <summary>Phoenix: a new settlement must be founded within this many tiles of the ruined one.</summary>
        public const int PhoenixRadius = 10;
        /// <summary>Phoenix: ... and within this many years.</summary>
        public const int PhoenixWindowYears = 50;
        /// <summary>A settlement destroyed this many tiles beyond a meteor's radius still counts as struck by it.</summary>
        public const int MeteorReachMargin = 6;
        /// <summary>A settlement abandoned this many ticks after a nearby meteor still counts as destroyed by it.</summary>
        public const int MeteorAftermathTicks = SimTime.TicksPerYear;
        private const int MaxTransient = 64;

        private struct FoundedCiv { public int CivId; public int Race; public bool Claimed; }
        private struct PendingSpawn { public int Race; }

        private readonly IReadOnlyList<ObjectiveDefinition> _defs;
        private readonly List<FoundedCiv> _recentFounded = new List<FoundedCiv>();
        private readonly List<PendingSpawn> _pendingSpawns = new List<PendingSpawn>();
        private readonly List<Vec2i> _recentDestroyed = new List<Vec2i>();
        private long _transientTick = long.MinValue;

        private readonly bool[] _trackedBuilding = new bool[Counts.Buildings];
        private readonly bool[] _isWonder = new bool[Counts.Buildings];
        private readonly int[] _completeByType = new int[Counts.Buildings];
        private readonly int[] _completeByRaceType = new int[Counts.Races * Counts.Buildings];
        private int _wondersStanding;
        private bool _cachesBuilt;

        private SimContext _ctx;
        private EventBus _bus;

        private readonly Action<GodPowerCastEvent> _onCast;
        private readonly Action<CivilizationFoundedEvent> _onCivFounded;
        private readonly Action<CivilizationCollapsedEvent> _onCivCollapsed;
        private readonly Action<AgeAdvancedEvent> _onAge;
        private readonly Action<BuildingCompletedEvent> _onBuilt;
        private readonly Action<SettlementTierChangedEvent> _onTier;
        private readonly Action<SettlementFoundedEvent> _onSettlementFounded;
        private readonly Action<SettlementDestroyedEvent> _onSettlementDestroyed;
        private readonly Action<SettlementCapturedEvent> _onCaptured;
        private readonly Action<BattleEvent> _onBattle;
        private readonly Action<WarDeclaredEvent> _onWar;
        private readonly Action<DisasterEvent> _onDisaster;

        public ObjectiveSystem()
        {
            _defs = ObjectiveCatalog.All;
            _onCast = OnPowerCast;
            _onCivFounded = OnCivilizationFounded;
            _onCivCollapsed = OnCivilizationCollapsed;
            _onAge = OnAgeAdvanced;
            _onBuilt = OnBuildingCompleted;
            _onTier = OnTierChanged;
            _onSettlementFounded = OnSettlementFounded;
            _onSettlementDestroyed = OnSettlementDestroyed;
            _onCaptured = OnSettlementCaptured;
            _onBattle = OnBattle;
            _onWar = OnWarDeclared;
            _onDisaster = OnDisaster;
        }

        public string Name => "Objectives";
        public int TickInterval => Interval;

        /// <summary>Subscribes to the event bus (re-initializing first detaches from any previous bus).</summary>
        public void Initialize(SimContext ctx)
        {
            Detach();
            _ctx = ctx;
            ClearTransient();
            if (ctx == null) return;
            BuildCaches(ctx.Db);
            var s = ctx.State;
            if (s != null && !s.Memory.ContainsKey(ObjectiveKeys.PeaceLastWarTick)) s.Memory[ObjectiveKeys.PeaceLastWarTick] = s.Tick;
            _bus = ctx.Events;
            if (_bus == null) return;
            _bus.Subscribe(_onCast);
            _bus.Subscribe(_onCivFounded);
            _bus.Subscribe(_onCivCollapsed);
            _bus.Subscribe(_onAge);
            _bus.Subscribe(_onBuilt);
            _bus.Subscribe(_onTier);
            _bus.Subscribe(_onSettlementFounded);
            _bus.Subscribe(_onSettlementDestroyed);
            _bus.Subscribe(_onCaptured);
            _bus.Subscribe(_onBattle);
            _bus.Subscribe(_onWar);
            _bus.Subscribe(_onDisaster);
        }

        /// <summary>Unsubscribes from the event bus (call before discarding the system while its bus lives on).</summary>
        public void Detach()
        {
            if (_bus == null) return;
            _bus.Unsubscribe(_onCast);
            _bus.Unsubscribe(_onCivFounded);
            _bus.Unsubscribe(_onCivCollapsed);
            _bus.Unsubscribe(_onAge);
            _bus.Unsubscribe(_onBuilt);
            _bus.Unsubscribe(_onTier);
            _bus.Unsubscribe(_onSettlementFounded);
            _bus.Unsubscribe(_onSettlementDestroyed);
            _bus.Unsubscribe(_onCaptured);
            _bus.Unsubscribe(_onBattle);
            _bus.Unsubscribe(_onWar);
            _bus.Unsubscribe(_onDisaster);
            _bus = null;
        }

        /// <summary>Periodic check: measures every unfinished objective and fulfils those whose condition is met.</summary>
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            _ctx = ctx;
            if (!_cachesBuilt) BuildCaches(ctx.Db);
            var s = ctx.State;
            ScanWorld(s);
            UpdatePeace(s);
            for (int i = 0; i < _defs.Count; i++)
            {
                var d = _defs[i];
                if (d != null) EvaluateOne(d, s);
            }
        }

        // ================================================================== evaluation

        private void EvaluateOne(ObjectiveDefinition d, WorldState s)
        {
            float times = Mem(s, d.DoneKey);
            if (!d.Repeatable && times >= 1f) return; // one-shot objectives never re-complete
            float progress = Measure(d, s, out bool met);
            if (d.Repeatable && !IsCounterKind(d.Kind) && s.Memory.TryGetValue(d.ArmedKey, out float armed) && armed < 0.5f)
            {
                // A repeatable state objective re-arms only after its condition lapses.
                if (!met) s.Memory[d.ArmedKey] = 1f;
                met = false;
            }
            s.Memory[d.ProgressKey] = progress;
            if (met) Complete(d, s, times);
        }

        private void Complete(ObjectiveDefinition d, WorldState s, float previousTimes)
        {
            int count = (previousTimes > 0f ? (int)Math.Min(previousTimes, 1e6f) : 0) + 1;
            if (d.Repeatable)
            {
                s.Memory[d.DoneKey] = count;
                if (IsCounterKind(d.Kind))
                {
                    s.Memory[d.BaseKey] = Mem(s, d.BaseKey) + d.Target;
                    s.Memory[d.ProgressKey] = Measure(d, s, out _);
                }
                else s.Memory[d.ArmedKey] = 0f;
            }
            else
            {
                s.Memory[d.DoneKey] = 1f;
                s.Memory[d.ProgressKey] = 1f;
            }
            GrantReward(s.God, d.Reward);
            string text = count > 1
                ? "The heavens rejoice: " + d.Title + " fulfilled again (" + count + " times)."
                : "The heavens rejoice: " + d.Title + " fulfilled.";
            AddChronicle(s, text, -1, -1, Vec2i.Invalid, d.Importance < 2 ? 2 : (d.Importance > 3 ? 3 : d.Importance));
        }

        /// <summary>Applies a reward to the god, clamping every value (no NaN/Infinity can enter state).</summary>
        public static void GrantReward(GodState god, ObjectiveReward reward)
        {
            if (god == null) return;
            float bonus = Math.Max(0f, MathUtil.Sanitize(reward.MaxManaBonus));
            god.MaxMana = MathUtil.Clamp(MathUtil.Sanitize(god.MaxMana, 500f) + bonus, 1f, MaxManaCap);
            float mana = MathUtil.Sanitize(god.Mana);
            float gain = Math.Max(0f, MathUtil.Sanitize(reward.Mana));
            float ceiling = Math.Max(god.MaxMana, mana); // never take away mana another system granted above the cap
            god.Mana = MathUtil.Clamp(mana + gain, 0f, ceiling);
            god.Alignment = MathUtil.Clamp(MathUtil.Sanitize(god.Alignment) + MathUtil.Sanitize(reward.AlignmentNudge), -1f, 1f);
        }

        private static bool IsCounterKind(ObjectiveKind k)
        {
            switch (k)
            {
                case ObjectiveKind.BuildingCompleted:
                case ObjectiveKind.WondersCompleted:
                case ObjectiveKind.BuildingsCompleted:
                case ObjectiveKind.PowerCast:
                case ObjectiveKind.AnyPowerCast:
                case ObjectiveKind.BattlesFought:
                case ObjectiveKind.WarsDeclared:
                case ObjectiveKind.CivilizationsCollapsed:
                case ObjectiveKind.SettlementsCaptured:
                case ObjectiveKind.DisastersWitnessed:
                case ObjectiveKind.AgeAdvancements:
                case ObjectiveKind.Phoenix:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Returns 0..1 progress and whether the condition is met right now.</summary>
        private float Measure(ObjectiveDefinition d, WorldState s, out bool met)
        {
            float target = d.Target;
            switch (d.Kind)
            {
                case ObjectiveKind.SettlementTier:
                {
                    int best = MaxTier(d, s);
                    if (Unfiltered(d)) best = Math.Max(best, (int)Mem(s, ObjectiveKeys.BestTier));
                    return Ratio(best, target, out met);
                }
                case ObjectiveKind.SettlementsAtTier: return Ratio(CountSettlements(d, s, d.Tier), target, out met);
                case ObjectiveKind.SettlementCount: return Ratio(CountSettlements(d, s, SettlementTier.Camp), target, out met);
                case ObjectiveKind.SettlementPopulation: return Ratio(SettlementPopulation(d, s, false), target, out met);
                case ObjectiveKind.WorldPopulation: return Ratio(SettlementPopulation(d, s, true), target, out met);
                case ObjectiveKind.CivilizationAge: return MeasureAge(d, s, false, out met);
                case ObjectiveKind.SpawnedCivilizationAge: return MeasureAge(d, s, true, out met);
                case ObjectiveKind.CivilizationsAlive: return Ratio(CivStat(d, s, 0), target, out met);
                case ObjectiveKind.TechsKnown: return Ratio(CivStat(d, s, 1), target, out met);
                case ObjectiveKind.CivilizationDevotion: return Ratio(CivStat(d, s, 2), target, out met);
                case ObjectiveKind.RacesAlive:
                {
                    int races = CountRaces(s);
                    int year = s.Year;
                    float yearPart = d.MinYear > 0 ? MathUtil.Clamp01(year / (float)d.MinYear) : 1f;
                    float racePart = target > 0f ? MathUtil.Clamp01(races / target) : 1f;
                    met = races >= target && year >= d.MinYear;
                    return met ? 1f : Math.Min(racePart, yearPart);
                }
                case ObjectiveKind.BuildingCompleted:
                {
                    int t = (int)d.Building;
                    if (t < 0 || t >= Counts.Buildings) { met = false; return 0f; }
                    float value;
                    if (d.RaceFilter.HasValue)
                    {
                        int r = (int)d.RaceFilter.Value;
                        value = r >= 0 && r < Counts.Races ? _completeByRaceType[r * Counts.Buildings + t] : 0f;
                    }
                    else value = Math.Max(_completeByType[t], Mem(s, ObjectiveKeys.BuiltCount(d.Building)));
                    return Counter(d, s, value, out met);
                }
                case ObjectiveKind.WondersCompleted: return Counter(d, s, Math.Max(_wondersStanding, Mem(s, ObjectiveKeys.CountWonders)), out met);
                case ObjectiveKind.BuildingsCompleted: return Counter(d, s, Mem(s, ObjectiveKeys.CountBuildings), out met);
                case ObjectiveKind.PowerCast: return Counter(d, s, Mem(s, ObjectiveKeys.PowerCount(d.Power)), out met);
                case ObjectiveKind.AnyPowerCast: return Counter(d, s, Mem(s, ObjectiveKeys.CountCasts), out met);
                case ObjectiveKind.BattlesFought: return Counter(d, s, Mem(s, ObjectiveKeys.CountBattles), out met);
                case ObjectiveKind.WarsDeclared: return Counter(d, s, Mem(s, ObjectiveKeys.CountWars), out met);
                case ObjectiveKind.CivilizationsCollapsed: return Counter(d, s, Mem(s, ObjectiveKeys.CountCollapses), out met);
                case ObjectiveKind.SettlementsCaptured: return Counter(d, s, Mem(s, ObjectiveKeys.CountCaptures), out met);
                case ObjectiveKind.DisastersWitnessed: return Counter(d, s, Mem(s, ObjectiveKeys.CountDisasters), out met);
                case ObjectiveKind.AgeAdvancements: return Counter(d, s, Mem(s, ObjectiveKeys.CountAgeAdvances), out met);
                case ObjectiveKind.Phoenix: return Counter(d, s, Mem(s, ObjectiveKeys.CountPhoenix), out met);
                case ObjectiveKind.AlignmentAtLeast:
                {
                    float a = MathUtil.Sanitize(s.God.Alignment);
                    met = target > 0f && a >= target;
                    return target > 0f ? MathUtil.Clamp01(a / target) : 0f;
                }
                case ObjectiveKind.AlignmentAtMost:
                {
                    float a = MathUtil.Sanitize(s.God.Alignment);
                    met = target < 0f && a <= target;
                    return target < 0f ? MathUtil.Clamp01(a / target) : 0f;
                }
                case ObjectiveKind.ManaReached: return Ratio(MathUtil.Sanitize(s.God.Mana), target, out met);
                case ObjectiveKind.YearsOfPeace:
                {
                    float since = s.Tick - Mem(s, ObjectiveKeys.PeaceLastWarTick);
                    return Ratio(since / SimTime.TicksPerYear, target, out met);
                }
                case ObjectiveKind.YearReached: return Ratio(s.Year, target, out met);
                default:
                    met = false;
                    return 0f;
            }
        }

        private static float Ratio(float value, float target, out bool met)
        {
            value = MathUtil.Sanitize(value);
            if (!(target > 0f)) { met = false; return 0f; }
            met = value >= target;
            return MathUtil.Clamp01(value / target);
        }

        private static float Counter(ObjectiveDefinition d, WorldState s, float value, out bool met)
        {
            float baseline = d.Repeatable ? Mem(s, d.BaseKey) : 0f;
            return Ratio(value - baseline, d.Target, out met);
        }

        private static bool Unfiltered(ObjectiveDefinition d) => !d.RaceFilter.HasValue && !d.AgeFilter.HasValue;

        private static bool MatchesCiv(ObjectiveDefinition d, Civilization c)
        {
            if (c == null || !c.IsAlive) return false;
            if (d.RaceFilter.HasValue && c.Race != d.RaceFilter.Value) return false;
            if (d.AgeFilter.HasValue && c.Age < d.AgeFilter.Value) return false;
            return true;
        }

        private static bool MatchesSettlement(ObjectiveDefinition d, Settlement st, WorldState s)
        {
            if (st == null || !st.IsAlive) return false;
            if (d.RaceFilter.HasValue && st.Race != d.RaceFilter.Value) return false;
            if (d.AgeFilter.HasValue)
            {
                var civ = s.Civilizations.Get(st.CivilizationId);
                if (civ == null || civ.Age < d.AgeFilter.Value) return false;
            }
            return true;
        }

        private static int MaxTier(ObjectiveDefinition d, WorldState s)
        {
            int best = 0;
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (MatchesSettlement(d, st, s) && (int)st.Tier > best) best = (int)st.Tier;
            }
            return best;
        }

        private static int CountSettlements(ObjectiveDefinition d, WorldState s, SettlementTier minTier)
        {
            int n = 0;
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (MatchesSettlement(d, st, s) && st.Tier >= minTier) n++;
            }
            return n;
        }

        private static int SettlementPopulation(ObjectiveDefinition d, WorldState s, bool sum)
        {
            int total = 0, best = 0;
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (!MatchesSettlement(d, st, s)) continue;
                int p = st.Population;
                total += p;
                if (p > best) best = p;
            }
            return sum ? total : best;
        }

        /// <summary>stat 0 = matching civs alive, 1 = max known techs, 2 = max devotion.</summary>
        private static float CivStat(ObjectiveDefinition d, WorldState s, int stat)
        {
            float result = 0f;
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (!MatchesCiv(d, c)) continue;
                if (stat == 0) result += 1f;
                else if (stat == 1) result = Math.Max(result, c.KnownTechs.Count);
                else result = Math.Max(result, MathUtil.Clamp01(MathUtil.Sanitize(c.Devotion)));
            }
            return result;
        }

        private static int CountRaces(WorldState s)
        {
            int mask = 0;
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (st == null || !st.IsAlive || st.Population <= 0) continue;
                int r = (int)st.Race;
                if (r >= 0 && r < 31) mask |= 1 << r;
            }
            int n = 0;
            while (mask != 0) { n += mask & 1; mask >>= 1; }
            return n;
        }

        private static float MeasureAge(ObjectiveDefinition d, WorldState s, bool spawnedOnly, out bool met)
        {
            int ageTarget = d.AgeFilter.HasValue ? (int)d.AgeFilter.Value : 0;
            int count = 0, best = 0;
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c == null || !c.IsAlive) continue;
                if (d.RaceFilter.HasValue && c.Race != d.RaceFilter.Value) continue;
                if (spawnedOnly && c.Mem(ObjectiveKeys.SpawnedByGod) < 1f) continue;
                int a = (int)c.Age;
                if (a > best) best = a;
                if (a >= ageTarget) count++;
            }
            float needed = Math.Max(1f, MathUtil.Sanitize(d.Target, 1f));
            if (!spawnedOnly && !d.RaceFilter.HasValue && needed <= 1f)
            {
                int ever = (int)Mem(s, ObjectiveKeys.BestAge); // sticky: the age was reached even if that civ later fell
                if (ever > best) best = ever;
                if (ever >= ageTarget && count < 1) count = 1;
            }
            met = count >= needed;
            if (met) return 1f;
            if (needed <= 1f) return ageTarget <= 0 ? 0f : MathUtil.Clamp01(best / (float)ageTarget);
            return MathUtil.Clamp01(count / needed);
        }

        // ================================================================== periodic world scan

        private void ScanWorld(WorldState s)
        {
            Array.Clear(_completeByType, 0, _completeByType.Length);
            Array.Clear(_completeByRaceType, 0, _completeByRaceType.Length);
            _wondersStanding = 0;
            Building gate = null;
            var bs = s.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                if (b == null || b.IsDestroyed || !b.IsComplete) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                _completeByType[t]++;
                int r = (int)b.StyleRace;
                if (r >= 0 && r < Counts.Races) _completeByRaceType[r * Counts.Buildings + t]++;
                if (_isWonder[t]) _wondersStanding++;
                if (gate == null && b.Type == BuildingType.AscensionGate) gate = b;
            }
            if (gate != null && Mem(s, ObjectiveKeys.Ascended) < 1f) Ascend(s, gate.Id, gate.SettlementId);

            int bestTier = (int)Mem(s, ObjectiveKeys.BestTier);
            var sts = s.Settlements.Items;
            for (int i = 0; i < sts.Count; i++)
                if (sts[i] != null && sts[i].IsAlive && (int)sts[i].Tier > bestTier) bestTier = (int)sts[i].Tier;
            RaiseTo(s, ObjectiveKeys.BestTier, bestTier);

            int bestAge = (int)Mem(s, ObjectiveKeys.BestAge);
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (civs[i] != null && civs[i].IsAlive && (int)civs[i].Age > bestAge) bestAge = (int)civs[i].Age;
            RaiseTo(s, ObjectiveKeys.BestAge, bestAge);
        }

        private static void UpdatePeace(WorldState s)
        {
            int living = 0;
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i] != null && civs[i].IsAlive) living++;
            bool war = false;
            var wars = s.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++) if (wars[i] != null && wars[i].IsActive) { war = true; break; }
            // Peace only counts while there is someone to be at peace with.
            if (war || living < 2 || !s.Memory.ContainsKey(ObjectiveKeys.PeaceLastWarTick)) s.Memory[ObjectiveKeys.PeaceLastWarTick] = s.Tick;
        }

        // ================================================================== event handlers (tally only)

        private WorldState State => _ctx != null ? _ctx.State : null;

        private void OnPowerCast(GodPowerCastEvent e)
        {
            var s = State;
            if (s == null || !e.Success) return;
            string key = ObjectiveKeys.PowerCount(e.Power);
            if (key == null) return; // unknown power value
            Inc(s, key);
            Inc(s, ObjectiveKeys.CountCasts);
            if (e.Power == GodPowerType.SpawnTribe) MatchSpawnFromCast(s, e.Parameter);
            else if (e.Power == GodPowerType.Meteor) RecordMeteor(s, e.Target, e.Radius);
        }

        private void OnCivilizationFounded(CivilizationFoundedEvent e)
        {
            var s = State;
            if (s == null) return;
            Inc(s, ObjectiveKeys.CountCivilizations);
            SyncTransient(s.Tick);
            int race = (int)e.Race;
            for (int i = 0; i < _pendingSpawns.Count; i++)
            {
                if (!RaceMatches(_pendingSpawns[i].Race, race)) continue;
                _pendingSpawns.RemoveAt(i);
                MarkSpawned(s, e.CivilizationId);
                return;
            }
            if (_recentFounded.Count >= MaxTransient) _recentFounded.RemoveAt(0);
            _recentFounded.Add(new FoundedCiv { CivId = e.CivilizationId, Race = race });
        }

        /// <summary>SpawnTribe succeeded: tag the civilization founded in the same tick (whichever event came first).</summary>
        private void MatchSpawnFromCast(WorldState s, int race)
        {
            SyncTransient(s.Tick);
            for (int i = _recentFounded.Count - 1; i >= 0; i--)
            {
                var f = _recentFounded[i];
                if (f.Claimed || !RaceMatches(race, f.Race)) continue;
                f.Claimed = true;
                _recentFounded[i] = f;
                MarkSpawned(s, f.CivId);
                return;
            }
            if (_pendingSpawns.Count >= MaxTransient) _pendingSpawns.RemoveAt(0);
            _pendingSpawns.Add(new PendingSpawn { Race = race });
        }

        private static bool RaceMatches(int castRace, int civRace) => castRace < 0 || castRace >= Counts.Races || castRace == civRace;

        private static void MarkSpawned(WorldState s, int civId)
        {
            var civ = civId >= 0 ? s.Civilizations.Get(civId) : null;
            if (civ == null || civ.Mem(ObjectiveKeys.SpawnedByGod) >= 1f) return;
            civ.Memory[ObjectiveKeys.SpawnedByGod] = 1f;
            Inc(s, ObjectiveKeys.CountSpawned);
        }

        private void RecordMeteor(WorldState s, Vec2i target, int radius)
        {
            int r = MathUtil.Clamp(radius, 0, 64);
            s.Memory[ObjectiveKeys.MeteorTick] = s.Tick;
            s.Memory[ObjectiveKeys.MeteorX] = target.X;
            s.Memory[ObjectiveKeys.MeteorY] = target.Y;
            s.Memory[ObjectiveKeys.MeteorRadius] = r;
            SyncTransient(s.Tick);
            int reach = r + MeteorReachMargin;
            for (int i = 0; i < _recentDestroyed.Count; i++)
                if (_recentDestroyed[i].DistanceSqTo(target) <= reach * reach) { SetPhoenixSite(s, _recentDestroyed[i]); break; }
        }

        private void OnSettlementDestroyed(SettlementDestroyedEvent e)
        {
            var s = State;
            if (s == null) return;
            SyncTransient(s.Tick);
            if (_recentDestroyed.Count >= MaxTransient) _recentDestroyed.RemoveAt(0);
            _recentDestroyed.Add(e.Position);
            if (!s.Memory.TryGetValue(ObjectiveKeys.MeteorTick, out float mt)) return;
            if (s.Tick - (long)mt > MeteorAftermathTicks) return;
            var meteor = new Vec2i((int)Mem(s, ObjectiveKeys.MeteorX), (int)Mem(s, ObjectiveKeys.MeteorY));
            int reach = (int)Mem(s, ObjectiveKeys.MeteorRadius) + MeteorReachMargin;
            if (meteor.DistanceSqTo(e.Position) <= reach * reach) SetPhoenixSite(s, e.Position);
        }

        private static void SetPhoenixSite(WorldState s, Vec2i at)
        {
            s.Memory[ObjectiveKeys.PhoenixSiteTick] = s.Tick;
            s.Memory[ObjectiveKeys.PhoenixSiteX] = at.X;
            s.Memory[ObjectiveKeys.PhoenixSiteY] = at.Y;
        }

        private void OnSettlementFounded(SettlementFoundedEvent e)
        {
            var s = State;
            if (s == null || !s.Memory.TryGetValue(ObjectiveKeys.PhoenixSiteTick, out float st) || st < 0f) return;
            long since = s.Tick - (long)st;
            if (since <= 0) return; // must rise after the fall
            if (since > (long)PhoenixWindowYears * SimTime.TicksPerYear) { s.Memory[ObjectiveKeys.PhoenixSiteTick] = -1f; return; }
            var site = new Vec2i((int)Mem(s, ObjectiveKeys.PhoenixSiteX), (int)Mem(s, ObjectiveKeys.PhoenixSiteY));
            if (site.DistanceSqTo(e.Position) > PhoenixRadius * PhoenixRadius) return;
            Inc(s, ObjectiveKeys.CountPhoenix);
            s.Memory[ObjectiveKeys.PhoenixSiteTick] = -1f;
        }

        private void OnBuildingCompleted(BuildingCompletedEvent e)
        {
            var s = State;
            if (s == null) return;
            Inc(s, ObjectiveKeys.CountBuildings);
            int t = (int)e.Type;
            if (t >= 0 && t < Counts.Buildings)
            {
                if (_trackedBuilding[t]) Inc(s, ObjectiveKeys.BuiltCount(e.Type));
                if (_isWonder[t]) Inc(s, ObjectiveKeys.CountWonders);
            }
            if (e.Type == BuildingType.AscensionGate) Ascend(s, e.BuildingId, e.SettlementId);
        }

        private void OnTierChanged(SettlementTierChangedEvent e)
        {
            var s = State;
            if (s != null) RaiseTo(s, ObjectiveKeys.BestTier, (int)e.NewTier);
        }

        private void OnAgeAdvanced(AgeAdvancedEvent e)
        {
            var s = State;
            if (s == null || e.NewAge <= e.OldAge) return;
            Inc(s, ObjectiveKeys.CountAgeAdvances);
            RaiseTo(s, ObjectiveKeys.BestAge, (int)e.NewAge);
        }

        private void OnBattle(BattleEvent e) { var s = State; if (s != null) Inc(s, ObjectiveKeys.CountBattles); }
        private void OnCivilizationCollapsed(CivilizationCollapsedEvent e) { var s = State; if (s != null) Inc(s, ObjectiveKeys.CountCollapses); }
        private void OnSettlementCaptured(SettlementCapturedEvent e) { var s = State; if (s != null) Inc(s, ObjectiveKeys.CountCaptures); }
        private void OnDisaster(DisasterEvent e) { var s = State; if (s != null) Inc(s, ObjectiveKeys.CountDisasters); }

        private void OnWarDeclared(WarDeclaredEvent e)
        {
            var s = State;
            if (s == null) return;
            Inc(s, ObjectiveKeys.CountWars);
            s.Memory[ObjectiveKeys.PeaceLastWarTick] = s.Tick;
        }

        // ================================================================== ascension (endgame victory)

        private void Ascend(WorldState s, int buildingId, int settlementId)
        {
            if (Mem(s, ObjectiveKeys.Ascended) >= 1f) return;
            var b = buildingId >= 0 ? s.Buildings.Get(buildingId) : null;
            var st = settlementId >= 0 ? s.Settlements.Get(settlementId) : null;
            int civId = b != null ? b.CivilizationId : (st != null ? st.CivilizationId : -1);
            var civ = civId >= 0 ? s.Civilizations.Get(civId) : null;
            s.Memory[ObjectiveKeys.Ascended] = 1f;
            s.Memory[ObjectiveKeys.AscendedTick] = s.Tick;
            s.Memory[ObjectiveKeys.AscendedCivilization] = civId;
            string who = civ != null ? "the " + civ.Name : "a mortal people";
            string where = st != null ? " above " + st.Name : "";
            AddChronicle(s, "Ascension! The Gate of " + who + " blazes open" + where +
                ": mortals step beyond the veil and the heavens crown their god. The world endures, and the watch goes on.",
                civId, settlementId, b != null ? b.Center : Vec2i.Invalid, 3);
        }

        // ================================================================== helpers

        private void AddChronicle(WorldState s, string text, int civId, int settlementId, Vec2i pos, int importance)
        {
            if (_ctx != null && _ctx.Ops != null && _ctx.Ops.State == s)
            {
                _ctx.Ops.AddChronicle(ChronicleCategory.Divine, text, civId, settlementId, pos, importance);
                return;
            }
            s.Chronicle.Add(new ChronicleEntry
            {
                Tick = s.Tick, Category = ChronicleCategory.Divine, Text = text ?? "", CivilizationId = civId,
                SettlementId = settlementId, Position = pos, Importance = MathUtil.Clamp(importance, 0, 3)
            });
        }

        private void BuildCaches(GameDatabase db)
        {
            for (int t = 0; t < Counts.Buildings; t++)
            {
                BuildingDefinition def = null;
                bool known = db != null && db.TryGetBuilding((BuildingType)t, out def);
                _isWonder[t] = known ? (def.IsWonder || def.Category == BuildingCategory.Wonder) : t >= (int)BuildingType.AscensionGate;
                _trackedBuilding[t] = false;
            }
            for (int i = 0; i < _defs.Count; i++)
            {
                var d = _defs[i];
                int t = d != null ? (int)d.Building : -1;
                if (d != null && d.Kind == ObjectiveKind.BuildingCompleted && t >= 0 && t < Counts.Buildings) _trackedBuilding[t] = true;
            }
            _cachesBuilt = true;
        }

        private void SyncTransient(long tick)
        {
            if (_transientTick == tick) return;
            ClearTransient();
            _transientTick = tick;
        }

        private void ClearTransient()
        {
            _recentFounded.Clear();
            _pendingSpawns.Clear();
            _recentDestroyed.Clear();
            _transientTick = long.MinValue;
        }

        private static float Mem(WorldState s, string key)
        {
            if (key == null) return 0f;
            return s.Memory.TryGetValue(key, out float v) ? MathUtil.Sanitize(v) : 0f;
        }

        private static void Inc(WorldState s, string key)
        {
            if (key == null) return;
            s.Memory[key] = Mem(s, key) + 1f;
        }

        private static void RaiseTo(WorldState s, string key, int value)
        {
            if (value > (int)Mem(s, key)) s.Memory[key] = value;
        }
    }
}
