using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    /// <summary>Game time. 1 tick = 1 day; a year is compressed to 96 days (4 seasons x 24 days).</summary>
    public static class SimTime
    {
        public const int TicksPerSeason = 24;
        public const int TicksPerYear = TicksPerSeason * 4;

        public static int Year(long tick) => (int)(tick / TicksPerYear);
        public static int DayOfYear(long tick) => (int)(tick % TicksPerYear);
        public static Season SeasonOf(long tick) => (Season)(DayOfYear(tick) / TicksPerSeason);
        public static long YearsToTicks(float years) => (long)(years * TicksPerYear);
        public static float TicksToYears(long ticks) => ticks / (float)TicksPerYear;
        public static string Format(long tick) => "Year " + Year(tick) + ", " + SeasonOf(tick) + " day " + (DayOfYear(tick) % TicksPerSeason + 1);
    }

    public sealed class Relation
    {
        public int CivA;                 // always the smaller id
        public int CivB;
        public float Opinion;            // -100..100
        public DiplomaticStance Stance = DiplomaticStance.Neutral;
        public bool HasContact;
        public bool TradeAgreement;
        public long TruceUntilTick = -1;
        public long LastContactTick = -1;
        public int WarId = -1;
        public bool AtWar => WarId >= 0;
    }

    public sealed class War
    {
        public int Id;
        public int AttackerId;
        public int DefenderId;
        public long StartTick;
        public long EndTick = -1;
        public bool IsActive = true;
        public float WarScore;           // + favors attacker, - favors defender (-100..100)
        public string CasusBelli = "";
        public int Battles;
        public int AttackerLosses;
        public int DefenderLosses;
        public int SettlementsCaptured;
    }

    public sealed class DiplomacyState
    {
        private readonly SortedDictionary<long, Relation> _relations = new SortedDictionary<long, Relation>();
        public readonly List<War> Wars = new List<War>();
        public readonly EntityStore<Army> Armies = new EntityStore<Army>();

        public static long Key(int a, int b) { int lo = Math.Min(a, b), hi = Math.Max(a, b); return ((long)lo << 32) | (uint)hi; }

        /// <summary>Gets (creating on demand) the symmetric relation between two civs.</summary>
        public Relation Get(int civA, int civB)
        {
            if (civA == civB) throw new ArgumentException("No relation with self.");
            long k = Key(civA, civB);
            if (!_relations.TryGetValue(k, out var r))
            {
                r = new Relation { CivA = Math.Min(civA, civB), CivB = Math.Max(civA, civB) };
                _relations[k] = r;
            }
            return r;
        }

        public bool TryGet(int civA, int civB, out Relation r) => _relations.TryGetValue(Key(civA, civB), out r);
        public IEnumerable<Relation> AllRelations => _relations.Values;
        public void AddRelation(Relation r) => _relations[Key(r.CivA, r.CivB)] = r; // persistence
        public bool RemoveRelation(int civA, int civB) => _relations.Remove(Key(civA, civB));
        public void RemoveRelationsOf(int civId)
        {
            var dead = new List<long>();
            foreach (var kv in _relations) if (kv.Value.CivA == civId || kv.Value.CivB == civId) dead.Add(kv.Key);
            foreach (var k in dead) _relations.Remove(k);
        }

        public War GetWar(int warId) { foreach (var w in Wars) if (w.Id == warId) return w; return null; }
        public bool AreAtWar(int a, int b) => a != b && TryGet(a, b, out var r) && r.AtWar;
    }

    public sealed class GodState
    {
        public float Mana = 100f;
        public float MaxMana = 500f;
        public float ManaPerTick;              // last computed regen (informational)
        public float Alignment;                // -1 wrathful .. +1 benevolent
        public readonly long[] CooldownUntil = new long[Counts.GodPowers];
        public int PowersCast;
        public long LastCastTick = -1;
    }

    public sealed class WeatherState
    {
        public Season Season;
        public float Rainfall = 1f;            // multiplier on moisture-dependent yields
        public float TemperatureOffset;        // -1..1 climate swing
        public float FoodYieldMultiplier = 1f; // aggregated seasonal/weather food modifier
        public int DroughtTicksRemaining;
        public int RainTicksRemaining;
        public float StormIntensity;           // 0..1 (visual + minor damage)
    }

    public struct ChronicleEntry
    {
        public long Tick;
        public ChronicleCategory Category;
        public string Text;
        public int CivilizationId;
        public int SettlementId;
        public Vec2i Position;
        public int Importance;                 // 0 minor .. 3 legendary
    }

    /// <summary>Bounded history log of notable events (rendered as the world chronicle).</summary>
    public sealed class Chronicle
    {
        public const int MaxEntries = 2000;
        public readonly List<ChronicleEntry> Entries = new List<ChronicleEntry>();

        public void Add(ChronicleEntry e)
        {
            if (e.Text != null && e.Text.Length > 300) e.Text = e.Text.Substring(0, 300);
            Entries.Add(e);
            if (Entries.Count > MaxEntries) Entries.RemoveRange(0, Entries.Count - MaxEntries);
        }
    }

    public sealed class WorldStats
    {
        public long TotalBirths;
        public long TotalDeaths;
        public long BuildingsCompleted;
        public long BuildingsDestroyed;
        public long SettlementsFounded;
        public long WarsDeclared;
        public long BattlesFought;
        public long GodPowersCast;
        public int PeakPopulation;
        public readonly long[] DeathsByCause = new long[Enum.GetValues(typeof(DeathCause)).Length];
    }

    /// <summary>
    /// Root of all mutable, persistent simulation state. Everything needed to resume a world lives here
    /// (plus the frozen GameDatabase, which is rebuilt from code). Persistence serializes this whole graph.
    /// </summary>
    public sealed class WorldState
    {
        /// <summary>Save schema. 2: Stockpile current-period flow counters are persisted.</summary>
        public const int SchemaVersion = 2;

        public ulong Seed;
        public long Tick;
        public WorldMap Map;
        public Rng Rng;
        public readonly IdAllocator Ids = new IdAllocator();

        public readonly EntityStore<Person> People = new EntityStore<Person>();
        public readonly EntityStore<Building> Buildings = new EntityStore<Building>();
        public readonly EntityStore<Settlement> Settlements = new EntityStore<Settlement>();
        public readonly EntityStore<Civilization> Civilizations = new EntityStore<Civilization>();

        public readonly DiplomacyState Diplomacy = new DiplomacyState();
        public readonly GodState God = new GodState();
        public readonly WeatherState Weather = new WeatherState();
        public readonly Chronicle Chronicle = new Chronicle();
        public readonly WorldStats Stats = new WorldStats();

        /// <summary>World-level blackboard for systems (serialized).</summary>
        public readonly Dictionary<string, float> Memory = new Dictionary<string, float>(StringComparer.Ordinal);

        // Deferred removals (flushed by WorldOps.FlushRemovals after each system tick). Not serialized: always empty between ticks.
        internal readonly List<int> PendingPersonRemovals = new List<int>();
        internal readonly List<int> PendingBuildingRemovals = new List<int>();
        internal readonly List<int> PendingSettlementRemovals = new List<int>();
        internal readonly List<int> PendingArmyRemovals = new List<int>();

        public WorldState(ulong seed, WorldMap map)
        {
            Seed = seed;
            Map = Guard.NotNull(map, nameof(map));
            Rng = new Rng(seed);
        }

        public int Year => SimTime.Year(Tick);
        public Season Season => SimTime.SeasonOf(Tick);

        public int LivingPopulation
        {
            get { int n = 0; var items = People.Items; for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) n++; return n; }
        }

        /// <summary>The highest age reached by any living civilization.</summary>
        public AgeId WorldAge
        {
            get
            {
                AgeId best = AgeId.Dawn;
                var civs = Civilizations.Items;
                for (int i = 0; i < civs.Count; i++) if (civs[i].IsAlive && civs[i].Age > best) best = civs[i].Age;
                return best;
            }
        }
    }
}
