using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.Headless
{
    /// <summary>Per-civilization aggregate computed from ground-truth state (not cached fields).</summary>
    public struct CivTally
    {
        public int Population;
        public int Settlements;
        public int Buildings;          // complete, standing
    }

    /// <summary>Read-only aggregate queries used by the report, history recorder and exporter.</summary>
    public static class WorldQueries
    {
        /// <summary>Living people, settlements and complete buildings per civilization id.</summary>
        public static Dictionary<int, CivTally> TallyCivs(WorldState s)
        {
            var map = new Dictionary<int, CivTally>();
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) map[civs[i].Id] = new CivTally();

            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!p.IsAlive || !map.TryGetValue(p.CivilizationId, out var t)) continue;
                t.Population++;
                map[p.CivilizationId] = t;
            }
            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i];
                if (!st.IsAlive || !map.TryGetValue(st.CivilizationId, out var t)) continue;
                t.Settlements++;
                map[st.CivilizationId] = t;
            }
            var buildings = s.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b.IsDestroyed || !b.IsComplete || !map.TryGetValue(b.CivilizationId, out var t)) continue;
                t.Buildings++;
                map[b.CivilizationId] = t;
            }
            return map;
        }

        public static int CompleteBuildings(WorldState s)
        {
            int n = 0;
            var items = s.Buildings.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsComplete && !items[i].IsDestroyed) n++;
            return n;
        }

        public static int StandingBuildings(WorldState s)
        {
            int n = 0;
            var items = s.Buildings.Items;
            for (int i = 0; i < items.Count; i++) if (!items[i].IsDestroyed) n++;
            return n;
        }

        public static int ActiveWars(WorldState s)
        {
            int n = 0;
            var wars = s.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++) if (wars[i] != null && wars[i].IsActive) n++;
            return n;
        }

        public static int LivingSettlements(WorldState s)
        {
            int n = 0;
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) n++;
            return n;
        }

        /// <summary>Sum of HousingCapacity over complete, standing buildings of the given type.</summary>
        public static int HousingCapacity(WorldState s, BuildingType type)
        {
            int n = 0;
            var items = s.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b.Type == type && b.IsComplete && !b.IsDestroyed) n += b.HousingCapacity;
            }
            return n;
        }

        public static string AgeAbbrev(AgeId age)
        {
            switch (age)
            {
                case AgeId.Dawn: return "Daw";
                case AgeId.Bronze: return "Brz";
                case AgeId.Iron: return "Irn";
                case AgeId.Classical: return "Cls";
                case AgeId.Medieval: return "Med";
                case AgeId.Renaissance: return "Ren";
                case AgeId.Industrial: return "Ind";
                case AgeId.Modern: return "Mod";
                case AgeId.Information: return "Inf";
                case AgeId.Ascension: return "Asc";
                default: return "???";
            }
        }
    }

    public sealed class CivSample
    {
        public int Id;
        public int Population;
        public AgeId Age;
        public int Settlements;
        public int Buildings;
    }

    public sealed class HistorySample
    {
        public int Year;
        public int TotalPopulation;
        public readonly List<CivSample> Civs = new List<CivSample>();
    }

    /// <summary>Collects the "history" array of the snapshot (one sample per interval).</summary>
    public sealed class HistoryRecorder
    {
        public const int MaxSamples = 5000;
        private readonly List<HistorySample> _samples = new List<HistorySample>();

        public IReadOnlyList<HistorySample> Samples => _samples;

        public HistorySample Record(WorldState s)
        {
            if (_samples.Count > 0 && _samples[_samples.Count - 1].Year == s.Year) _samples.RemoveAt(_samples.Count - 1);
            var tally = TallyCivs(s);
            var sample = new HistorySample { Year = s.Year };
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                tally.TryGetValue(c.Id, out var t);
                if (!c.IsAlive && t.Population == 0) continue;
                sample.Civs.Add(new CivSample { Id = c.Id, Population = t.Population, Age = c.Age, Settlements = t.Settlements, Buildings = t.Buildings });
            }
            sample.Civs.Sort((a, b) => a.Id.CompareTo(b.Id));
            sample.TotalPopulation = s.LivingPopulation;
            if (_samples.Count >= MaxSamples) _samples.RemoveAt(0);
            _samples.Add(sample);
            return sample;
        }

        private static Dictionary<int, CivTally> TallyCivs(WorldState s) => WorldQueries.TallyCivs(s);
    }
}
