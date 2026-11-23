using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.SaveTool
{
    /// <summary>Per-civilization figures computed from a loaded state (people counted directly, not from caches).</summary>
    public sealed class CivSummary
    {
        public int Id;
        public string Name;
        public RaceId Race;
        public AgeId Age;
        public bool IsAlive;
        public int Population;
        public int Settlements;
        public int Buildings;
        public int Techs;
        public float TotalResearch;
        public float MilitaryPower;
        public int Armies;
    }

    /// <summary>Counts and aggregates over a <see cref="WorldState"/>, iterating stores by index (deterministic).</summary>
    public sealed class WorldSummary
    {
        public int PeopleTotal, PeopleAlive;
        public int BuildingsTotal, BuildingsComplete, BuildingsUnderConstruction, BuildingsDestroyed;
        public int SettlementsTotal, SettlementsAlive;
        public int CivsTotal, CivsAlive;
        public int Armies, WarsTotal, WarsActive, Relations, ChronicleEntries;
        public readonly SortedDictionary<int, CivSummary> Civs = new SortedDictionary<int, CivSummary>();

        public static WorldSummary Of(WorldState s)
        {
            var w = new WorldSummary();
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                Civilization c = civs[i];
                w.CivsTotal++;
                if (c.IsAlive) w.CivsAlive++;
                w.Civs[c.Id] = new CivSummary
                {
                    Id = c.Id, Name = c.Name, Race = c.Race, Age = c.Age, IsAlive = c.IsAlive,
                    Techs = c.KnownTechs.Count, TotalResearch = c.TotalResearch, MilitaryPower = c.MilitaryPower,
                };
            }

            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                Person p = people[i];
                w.PeopleTotal++;
                if (!p.IsAlive) continue;
                w.PeopleAlive++;
                if (w.Civs.TryGetValue(p.CivilizationId, out CivSummary cs)) cs.Population++;
            }

            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement st = settlements[i];
                w.SettlementsTotal++;
                if (!st.IsAlive) continue;
                w.SettlementsAlive++;
                if (w.Civs.TryGetValue(st.CivilizationId, out CivSummary cs)) cs.Settlements++;
            }

            var buildings = s.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building b = buildings[i];
                w.BuildingsTotal++;
                if (b.IsDestroyed) { w.BuildingsDestroyed++; continue; }
                if (b.IsComplete) w.BuildingsComplete++; else w.BuildingsUnderConstruction++;
                if (w.Civs.TryGetValue(b.CivilizationId, out CivSummary cs)) cs.Buildings++;
            }

            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                if (!armies[i].IsAlive) continue;
                w.Armies++;
                if (w.Civs.TryGetValue(armies[i].CivilizationId, out CivSummary cs)) cs.Armies++;
            }

            var wars = s.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++) { w.WarsTotal++; if (wars[i].IsActive) w.WarsActive++; }
            foreach (Relation r in s.Diplomacy.AllRelations) { if (r != null) w.Relations++; }
            w.ChronicleEntries = s.Chronicle.Entries.Count;
            return w;
        }

        public static string CivName(WorldState s, int civId)
        {
            Civilization c = s.Civilizations.Get(civId);
            return c != null ? TextOut.Safe(c.Name, 32) + " #" + civId : "#" + civId;
        }
    }
}
