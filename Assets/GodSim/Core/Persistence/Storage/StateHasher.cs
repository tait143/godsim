using System;
using System.Collections.Generic;

namespace GodSim.Core.Persistence
{
    /// <summary>Sections of the canonical state traversal (see <see cref="StateHasher.ComputeSections"/>).</summary>
    public enum StateHashSection : byte
    {
        Header = 0, Map = 1, Rng = 2, Ids = 3, People = 4, Buildings = 5, Settlements = 6, Civilizations = 7,
        Diplomacy = 8, God = 9, Weather = 10, Chronicle = 11, Stats = 12, Memory = 13
    }

    /// <summary>
    /// Deterministic 64-bit hash of a <see cref="WorldState"/> for desync detection, determinism tests and
    /// replay verification.
    /// <para>
    /// Algorithm: FNV-1a 64 over a canonical little-endian byte encoding. Each <see cref="StateHashSection"/>
    /// is hashed separately (FNV-1a seeded with the section id) and the final value is FNV-1a over the
    /// sequence of section hashes, so <see cref="ComputeSections"/> can pinpoint where two states diverge.
    /// Floats are hashed by bit pattern (<see cref="BitConverter.SingleToInt32Bits"/>; every NaN maps to one
    /// canonical pattern), strings as length + UTF-16 code units (null = length -1), collections with their
    /// count, entity stores in store order, and Memory blackboards sorted by key (ordinal), so dictionary
    /// insertion order never matters. Independent of process, platform and string.GetHashCode.
    /// </para>
    /// <para>
    /// Covered: every field of WorldState, WorldMap (all tile arrays + SeaLevel), Rng, IdAllocator, Person,
    /// Building, Settlement (incl. Stockpile, BuildQueue, service/job arrays, Memory), Civilization (incl.
    /// KnownTechs, UnlockedBuildings, Modifiers, Memory), Relation, War, Army, GodState, WeatherState,
    /// Chronicle, WorldStats. Deliberately excluded: Settlement.AiLog (debug-only, documented as not
    /// serialized), WorldState pending-removal lists (always empty between ticks), WorldMap.TerrainVersion
    /// (render-refresh counter, derived from terrain edits that are hashed themselves). The Stockpile's current-period
    /// flow counters (PeriodProduced/PeriodConsumed) are covered since encoding 2.
    /// </para>
    /// </summary>
    public static class StateHasher
    {
        /// <summary>Version of the canonical encoding. Bump when the traversal changes. 2: Stockpile period flows.</summary>
        public const int EncodingVersion = 2;

        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private const int CanonicalNaNBits = 0x7FC00000;

        /// <summary>Number of sections in the canonical traversal.</summary>
        public static readonly int SectionCount = Enum.GetValues(typeof(StateHashSection)).Length;

        /// <summary>Computes the 64-bit state hash. Allocates only a small scratch list for key sorting.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="s"/> is null.</exception>
        public static ulong Compute(WorldState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var scratch = new List<string>();
            var outer = new Fnv(OffsetBasis);
            outer.Int32(EncodingVersion);
            outer.Int32(SectionCount);
            for (int i = 0; i < SectionCount; i++) outer.UInt64(HashSection(s, (StateHashSection)i, scratch));
            return outer.H;
        }

        /// <summary>
        /// Returns the per-section hashes (indexed by <see cref="StateHashSection"/>). Compare two arrays to find
        /// which part of the state diverged after a desync.
        /// </summary>
        public static ulong[] ComputeSections(WorldState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var scratch = new List<string>();
            var result = new ulong[SectionCount];
            for (int i = 0; i < SectionCount; i++) result[i] = HashSection(s, (StateHashSection)i, scratch);
            return result;
        }

        /// <summary>Hash of a single section.</summary>
        public static ulong ComputeSection(WorldState s, StateHashSection section)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return HashSection(s, section, new List<string>());
        }

        /// <summary>Formats a hash the way logs and desync reports print it (16 upper-case hex digits).</summary>
        public static string Format(ulong hash) => hash.ToString("X16");

        private static ulong HashSection(WorldState s, StateHashSection section, List<string> scratch)
        {
            var h = new Fnv(OffsetBasis);
            h.Byte((byte)section);
            switch (section)
            {
                case StateHashSection.Header:
                    h.Int32(WorldState.SchemaVersion);
                    h.UInt64(s.Seed);
                    h.Int64(s.Tick);
                    break;
                case StateHashSection.Map: HashMap(ref h, s.Map); break;
                case StateHashSection.Rng:
                    if (s.Rng == null) h.Byte(0);
                    else { h.Byte(1); h.UInt64(s.Rng.State); h.UInt64(s.Rng.Increment); }
                    break;
                case StateHashSection.Ids:
                    var ids = s.Ids;
                    h.Int32(ids.NextPersonId); h.Int32(ids.NextBuildingId); h.Int32(ids.NextSettlementId);
                    h.Int32(ids.NextCivilizationId); h.Int32(ids.NextArmyId); h.Int32(ids.NextWarId);
                    break;
                case StateHashSection.People:
                {
                    var items = s.People.Items;
                    h.Int32(items.Count);
                    for (int i = 0; i < items.Count; i++) HashPerson(ref h, items[i]);
                    break;
                }
                case StateHashSection.Buildings:
                {
                    var items = s.Buildings.Items;
                    h.Int32(items.Count);
                    for (int i = 0; i < items.Count; i++) HashBuilding(ref h, items[i]);
                    break;
                }
                case StateHashSection.Settlements:
                {
                    var items = s.Settlements.Items;
                    h.Int32(items.Count);
                    for (int i = 0; i < items.Count; i++) HashSettlement(ref h, items[i], scratch);
                    break;
                }
                case StateHashSection.Civilizations:
                {
                    var items = s.Civilizations.Items;
                    h.Int32(items.Count);
                    for (int i = 0; i < items.Count; i++) HashCivilization(ref h, items[i], scratch);
                    break;
                }
                case StateHashSection.Diplomacy: HashDiplomacy(ref h, s.Diplomacy); break;
                case StateHashSection.God: HashGod(ref h, s.God); break;
                case StateHashSection.Weather:
                {
                    var w = s.Weather;
                    h.Byte((byte)w.Season);
                    h.Float(w.Rainfall);
                    h.Float(w.TemperatureOffset);
                    h.Float(w.FoodYieldMultiplier);
                    h.Int32(w.DroughtTicksRemaining);
                    h.Int32(w.RainTicksRemaining);
                    h.Float(w.StormIntensity);
                    break;
                }
                case StateHashSection.Chronicle:
                {
                    var entries = s.Chronicle.Entries;
                    h.Int32(entries.Count);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        h.Int64(e.Tick);
                        h.Byte((byte)e.Category);
                        h.Str(e.Text);
                        h.Int32(e.CivilizationId);
                        h.Int32(e.SettlementId);
                        h.Vec(e.Position);
                        h.Int32(e.Importance);
                    }
                    break;
                }
                case StateHashSection.Stats:
                {
                    var st = s.Stats;
                    h.Int64(st.TotalBirths); h.Int64(st.TotalDeaths);
                    h.Int64(st.BuildingsCompleted); h.Int64(st.BuildingsDestroyed);
                    h.Int64(st.SettlementsFounded); h.Int64(st.WarsDeclared);
                    h.Int64(st.BattlesFought); h.Int64(st.GodPowersCast);
                    h.Int32(st.PeakPopulation);
                    h.Int64Array(st.DeathsByCause);
                    break;
                }
                case StateHashSection.Memory: HashMemory(ref h, s.Memory, scratch); break;
            }
            return h.H;
        }

        // ------------------------------------------------------------------ sections

        private static void HashMap(ref Fnv h, WorldMap m)
        {
            if (m == null) { h.Byte(0); return; }
            h.Byte(1);
            h.Int32(m.Width);
            h.Int32(m.Height);
            h.Float(m.SeaLevel);
            h.FloatArray(m.Elevation);
            h.FloatArray(m.Moisture);
            h.FloatArray(m.Temperature);
            h.FloatArray(m.Fertility);
            h.BiomeArray(m.Biomes);
            h.FlagArray(m.Flags);
            h.FloatArray(m.Resource);
            h.Int32Array(m.Owner);
            h.Int32Array(m.BuildingAt);
        }

        private static void HashPerson(ref Fnv h, Person p)
        {
            h.Int32(p.Id);
            h.Str(p.Name);
            h.Byte((byte)p.Race);
            h.Byte((byte)p.Sex);
            h.Int64(p.BirthTick);
            h.Float(p.AgeYears);
            h.Byte((byte)p.Stage);
            h.Bool(p.IsAlive);
            h.Int64(p.DeathTick);
            h.Byte((byte)p.DeathCause);
            h.Float(p.Health);
            h.Float(p.Happiness);
            h.Float(p.Satiation);
            h.Float(p.Faith);
            h.UInt16((ushort)p.Traits);
            h.Float(p.Skill);
            h.Float(p.Experience);
            h.Byte((byte)p.Job);
            h.Int32(p.WorkplaceId);
            h.Int32(p.HomeId);
            h.Int32(p.SettlementId);
            h.Int32(p.CivilizationId);
            h.Int32(p.ArmyId);
            h.Int32(p.MotherId);
            h.Int32(p.FatherId);
            h.Int32(p.SpouseId);
            h.Int32(p.ChildrenCount);
            h.Int32(p.Generation);
            h.Vec(p.Position);
        }

        private static void HashBuilding(ref Fnv h, Building b)
        {
            h.Int32(b.Id);
            h.Byte((byte)b.Type);
            h.Int32(b.SettlementId);
            h.Int32(b.CivilizationId);
            h.Vec(b.Origin);
            h.Int32(b.Width);
            h.Int32(b.Height);
            h.Byte(b.Rotation);
            h.Int32(b.Variant);
            h.Bool(b.IsComplete);
            h.Bool(b.IsDestroyed);
            h.Float(b.ConstructionProgress);
            h.Bool(b.CostPaid);
            h.Float(b.Condition);
            h.Int32(b.Level);
            h.Bool(b.IsOnFire);
            h.Int32(b.HousingCapacity);
            h.IntList(b.Residents);
            h.IntList(b.Workers);
            h.Float(b.Efficiency);
            h.Byte((byte)b.StyleRace);
            h.Byte((byte)b.StyleAge);
            h.Int64(b.PlacedTick);
            h.Int64(b.CompletedTick);
        }

        private static void HashSettlement(ref Fnv h, Settlement s, List<string> scratch)
        {
            h.Int32(s.Id);
            h.Str(s.Name);
            h.Int32(s.CivilizationId);
            h.Byte((byte)s.Race);
            h.Vec(s.Center);
            h.Int64(s.FoundedTick);
            h.Byte((byte)s.Tier);
            h.Bool(s.IsCapital);
            h.Bool(s.IsAlive);
            h.Int32(s.TownCenterId);
            h.Int32(s.ParentSettlementId);
            h.Int32(s.TerritoryRadius);
            h.IntList(s.BuildingIds);
            h.IntList(s.ResidentIds);

            var sp = s.Stockpile;
            h.Int32(Counts.Resources);
            for (int r = 0; r < Counts.Resources; r++) h.Float(sp.Get((ResourceType)r));
            h.FloatArray(sp.AvgIncome);
            h.FloatArray(sp.AvgExpense);
            h.Float(sp.Capacity);
            // Encoding 2: current-period flow counters (persisted since save schema 2; they feed the next RollStatistics).
            for (int r = 0; r < Counts.Resources; r++) { h.Float(sp.PeriodProduced((ResourceType)r)); h.Float(sp.PeriodConsumed((ResourceType)r)); }

            var queue = s.BuildQueue;
            h.Int32(queue.Count);
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null) { h.Byte(0); continue; }
                h.Byte(1);
                h.Byte((byte)o.Type);
                h.Float(o.Priority);
                h.Int64(o.RequestedTick);
                h.Str(o.Reason);
                h.Int32(o.Attempts);
                h.Bool(o.HasPreferredSite);
                h.Vec(o.PreferredSite);
            }

            h.FloatArray(s.ServiceOutput);
            h.FloatArray(s.ServiceCoverage);
            h.FloatArray(s.JobPriority);
            h.Int32(s.DesiredBuilders);
            h.Int32(s.Homeless);
            h.Int32(s.Unemployed);
            h.Int32(s.Soldiers);
            h.Float(s.Happiness);
            h.Float(s.Health);
            h.Float(s.Unrest);
            h.Float(s.Faith);
            h.Float(s.DefenseStrength);
            h.Int64(s.LastStarvationTick);
            HashMemory(ref h, s.Memory, scratch);
        }

        private static void HashCivilization(ref Fnv h, Civilization c, List<string> scratch)
        {
            h.Int32(c.Id);
            h.Str(c.Name);
            h.Byte((byte)c.Race);
            h.UInt32(c.Color);
            h.Byte((byte)c.Age);
            h.Int64(c.AgeEnteredTick);
            h.Int64(c.FoundedTick);
            h.Bool(c.IsAlive);
            h.Int64(c.CollapsedTick);
            h.Int32(c.CapitalId);
            h.IntList(c.SettlementIds);
            h.Int32(c.LeaderId);
            h.Float(c.ResearchPool);
            h.Float(c.TotalResearch);
            h.Int32(c.CurrentResearchTechId);

            h.Int32(c.KnownTechs.Count);
            foreach (int tech in c.KnownTechs) h.Int32(tech); // SortedSet: ascending, deterministic

            var unlocked = c.UnlockedBuildings;
            h.Int32(unlocked.Length);
            for (int i = 0; i < unlocked.Length; i++) h.Bool(unlocked[i]);

            var m = c.Modifiers;
            h.FloatArray(m.Production);
            h.FloatArray(m.Service);
            h.Float(m.Research); h.Float(m.Growth); h.Float(m.BuildSpeed); h.Float(m.Faith);
            h.Float(m.Military); h.Float(m.Storage); h.Float(m.Trade); h.Float(m.MoveSpeed);
            h.Float(m.HealthBonus); h.Float(m.HappinessBonus); h.Float(m.LifespanBonusYears);

            h.Float(c.Devotion);
            h.Float(c.GodFavor);
            h.Int32(c.Population);
            h.Float(c.MilitaryPower);
            HashMemory(ref h, c.Memory, scratch);
        }

        private static void HashDiplomacy(ref Fnv h, DiplomacyState d)
        {
            // Relations: SortedDictionary order (by packed civ-pair key) is deterministic. Count goes last.
            int relations = 0;
            foreach (var r in d.AllRelations)
            {
                relations++;
                if (r == null) { h.Byte(0); continue; }
                h.Byte(1);
                h.Int32(r.CivA);
                h.Int32(r.CivB);
                h.Float(r.Opinion);
                h.Byte((byte)r.Stance);
                h.Bool(r.HasContact);
                h.Bool(r.TradeAgreement);
                h.Int64(r.TruceUntilTick);
                h.Int64(r.LastContactTick);
                h.Int32(r.WarId);
            }
            h.Int32(relations);

            var wars = d.Wars;
            h.Int32(wars.Count);
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null) { h.Byte(0); continue; }
                h.Byte(1);
                h.Int32(w.Id);
                h.Int32(w.AttackerId);
                h.Int32(w.DefenderId);
                h.Int64(w.StartTick);
                h.Int64(w.EndTick);
                h.Bool(w.IsActive);
                h.Float(w.WarScore);
                h.Str(w.CasusBelli);
                h.Int32(w.Battles);
                h.Int32(w.AttackerLosses);
                h.Int32(w.DefenderLosses);
                h.Int32(w.SettlementsCaptured);
            }

            var armies = d.Armies.Items;
            h.Int32(armies.Count);
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                h.Int32(a.Id);
                h.Int32(a.CivilizationId);
                h.Int32(a.HomeSettlementId);
                h.Int32(a.TargetSettlementId);
                h.Int32(a.WarId);
                h.Byte((byte)a.State);
                h.Vec(a.Position);
                h.Vec(a.Destination);
                h.Float(a.MoveProgress);
                h.Float(a.Strength);
                h.Float(a.Morale);
                h.Bool(a.IsAlive);
                h.IntList(a.SoldierIds);
                var path = a.Path;
                h.Int32(path.Count);
                for (int k = 0; k < path.Count; k++) h.Vec(path[k]);
                h.Int32(a.PathIndex);
                h.Int64(a.CreatedTick);
            }
        }

        private static void HashGod(ref Fnv h, GodState g)
        {
            h.Float(g.Mana);
            h.Float(g.MaxMana);
            h.Float(g.ManaPerTick);
            h.Float(g.Alignment);
            h.Int64Array(g.CooldownUntil);
            h.Int32(g.PowersCast);
            h.Int64(g.LastCastTick);
        }

        private static void HashMemory(ref Fnv h, Dictionary<string, float> memory, List<string> scratch)
        {
            if (memory == null) { h.Int32(-1); return; }
            scratch.Clear();
            foreach (var kv in memory) scratch.Add(kv.Key);
            scratch.Sort(StringComparer.Ordinal);
            h.Int32(scratch.Count);
            for (int i = 0; i < scratch.Count; i++)
            {
                string key = scratch[i];
                h.Str(key);
                h.Float(memory[key]);
            }
            scratch.Clear();
        }

        // ------------------------------------------------------------------ FNV-1a 64 sink

        /// <summary>Mutable FNV-1a 64 accumulator over a little-endian canonical encoding. Pass by ref.</summary>
        private struct Fnv
        {
            public ulong H;

            public Fnv(ulong seed) { H = seed; }

            public void Byte(byte b) { H = unchecked((H ^ b) * Prime); }

            public void Bool(bool b) => Byte(b ? (byte)1 : (byte)0);

            public void UInt16(ushort v)
            {
                ulong h = H;
                h = unchecked((h ^ (byte)v) * Prime);
                h = unchecked((h ^ (byte)(v >> 8)) * Prime);
                H = h;
            }

            public void UInt32(uint v)
            {
                ulong h = H;
                h = unchecked((h ^ (v & 0xFFu)) * Prime);
                h = unchecked((h ^ ((v >> 8) & 0xFFu)) * Prime);
                h = unchecked((h ^ ((v >> 16) & 0xFFu)) * Prime);
                h = unchecked((h ^ (v >> 24)) * Prime);
                H = h;
            }

            public void Int32(int v) => UInt32(unchecked((uint)v));

            public void UInt64(ulong v)
            {
                UInt32(unchecked((uint)v));
                UInt32(unchecked((uint)(v >> 32)));
            }

            public void Int64(long v) => UInt64(unchecked((ulong)v));

            public void Float(float f) => Int32(CanonicalBits(f));

            public void Vec(Vec2i v) { Int32(v.X); Int32(v.Y); }

            public void Str(string s)
            {
                if (s == null) { Int32(-1); return; }
                Int32(s.Length);
                for (int i = 0; i < s.Length; i++) UInt16(s[i]);
            }

            public void IntList(List<int> list)
            {
                if (list == null) { Int32(-1); return; }
                Int32(list.Count);
                for (int i = 0; i < list.Count; i++) Int32(list[i]);
            }

            public void Int32Array(int[] a)
            {
                if (a == null) { Int32(-1); return; }
                Int32(a.Length);
                ulong h = H;
                for (int i = 0; i < a.Length; i++)
                {
                    uint v = unchecked((uint)a[i]);
                    h = unchecked((h ^ (v & 0xFFu)) * Prime);
                    h = unchecked((h ^ ((v >> 8) & 0xFFu)) * Prime);
                    h = unchecked((h ^ ((v >> 16) & 0xFFu)) * Prime);
                    h = unchecked((h ^ (v >> 24)) * Prime);
                }
                H = h;
            }

            public void Int64Array(long[] a)
            {
                if (a == null) { Int32(-1); return; }
                Int32(a.Length);
                for (int i = 0; i < a.Length; i++) Int64(a[i]);
            }

            public void FloatArray(float[] a)
            {
                if (a == null) { Int32(-1); return; }
                Int32(a.Length);
                ulong h = H;
                for (int i = 0; i < a.Length; i++)
                {
                    uint v = unchecked((uint)CanonicalBits(a[i]));
                    h = unchecked((h ^ (v & 0xFFu)) * Prime);
                    h = unchecked((h ^ ((v >> 8) & 0xFFu)) * Prime);
                    h = unchecked((h ^ ((v >> 16) & 0xFFu)) * Prime);
                    h = unchecked((h ^ (v >> 24)) * Prime);
                }
                H = h;
            }

            public void BiomeArray(Biome[] a)
            {
                if (a == null) { Int32(-1); return; }
                Int32(a.Length);
                ulong h = H;
                for (int i = 0; i < a.Length; i++) h = unchecked((h ^ (byte)a[i]) * Prime);
                H = h;
            }

            public void FlagArray(TileFlags[] a)
            {
                if (a == null) { Int32(-1); return; }
                Int32(a.Length);
                ulong h = H;
                for (int i = 0; i < a.Length; i++)
                {
                    ushort v = (ushort)a[i];
                    h = unchecked((h ^ (byte)v) * Prime);
                    h = unchecked((h ^ (byte)(v >> 8)) * Prime);
                }
                H = h;
            }

            private static int CanonicalBits(float f)
            {
                int bits = BitConverter.SingleToInt32Bits(f);
                return (bits & 0x7F800000) == 0x7F800000 && (bits & 0x007FFFFF) != 0 ? CanonicalNaNBits : bits;
            }
        }
    }
}
