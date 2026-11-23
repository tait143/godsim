using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven "ladder" of houses of worship, derived from <see cref="BuildingDefinition"/> fields (Faith services,
    /// secondary services, MinAge, MinTier, MinPopulation, MaxPerSettlement). Picks the best worship building a
    /// settlement can add right now: newer, stronger tiers win (SpiritTotem -> Shrine -> Temple -> Monastery ->
    /// Cathedral with the stock catalog), existing copies are penalised and capped so the advisor never spams duplicates.
    /// Tables are rebuilt only when the database instance changes; picking is allocation-free.
    /// </summary>
    public sealed class FaithAdvisorLadder
    {
        /// <summary>Faith output from which a building counts as a "temple-class" sanctuary (holy sites want one).</summary>
        public const float TempleClassFaith = 2.5f;

        /// <summary>Faith points per building type (sum of its Faith services).</summary>
        public readonly float[] FaithOut = new float[Counts.Buildings];
        /// <summary>Faith plus a share of secondary services (a Monastery's research makes it worth more than a Temple).</summary>
        public readonly float[] Value = new float[Counts.Buildings];
        /// <summary>Non-wonder religion or faith-producing building.</summary>
        public readonly bool[] IsWorship = new bool[Counts.Buildings];
        /// <summary>Wonder that produces faith (racial holy wonders).</summary>
        public readonly bool[] IsFaithWonder = new bool[Counts.Buildings];
        /// <summary>Cached "Rebuild the fallen X" reasons (no per-tick string building).</summary>
        public readonly string[] RebuildReason = new string[Counts.Buildings];
        /// <summary>Cached display names.</summary>
        public readonly string[] Names = new string[Counts.Buildings];

        private readonly List<BuildingType> _worship = new List<BuildingType>();
        private readonly List<BuildingType> _wonders = new List<BuildingType>();
        private GameDatabase _db;

        public IReadOnlyList<BuildingType> Worship => _worship;
        public IReadOnlyList<BuildingType> FaithWonders => _wonders;

        /// <summary>(Re)builds the tables for a database. Cheap no-op when already bound to it.</summary>
        public void Bind(GameDatabase db)
        {
            if (db == null || ReferenceEquals(db, _db)) return;
            _db = db;
            _worship.Clear();
            _wonders.Clear();
            for (int i = 0; i < Counts.Buildings; i++)
            {
                FaithOut[i] = 0f; Value[i] = 0f; IsWorship[i] = false; IsFaithWonder[i] = false;
                var t = (BuildingType)i;
                Names[i] = t.ToString();
                RebuildReason[i] = "Rebuild the fallen " + Names[i];
                if (!db.TryGetBuilding(t, out var def)) continue;
                if (!string.IsNullOrEmpty(def.Name)) { Names[i] = def.Name; RebuildReason[i] = "Rebuild the fallen " + def.Name; }
                float faith = 0f, other = 0f;
                var svc = def.Services;
                if (svc != null)
                    for (int k = 0; k < svc.Length; k++)
                    {
                        float a = MathUtil.Sanitize(svc[k].Amount);
                        if (a <= 0f) continue;
                        if (svc[k].Type == ServiceType.Faith) faith += a; else other += a;
                    }
                FaithOut[i] = faith;
                Value[i] = faith + 0.3f * other;
                bool religious = def.Category == BuildingCategory.Religion || faith > 0f;
                if (!religious || def.IsDwelling) continue;
                if (def.IsWonder || def.UniquePerCivilization) { if (faith > 0f) { IsFaithWonder[i] = true; _wonders.Add(t); } }
                else if (faith > 0f || def.Category == BuildingCategory.Religion) { IsWorship[i] = true; _worship.Add(t); }
                if (Value[i] <= 0f) Value[i] = 0.25f; // religion building without declared faith still has some worth
            }
        }

        /// <summary>
        /// Soft cap on copies per settlement: MaxPerSettlement when set, otherwise 1 + pop / (30 * faith) so small
        /// shrines may multiply in a big town while big sanctuaries stay rare.
        /// </summary>
        public static int SoftCap(BuildingDefinition def, float faith, int population)
        {
            if (def == null) return 0;
            if (def.MaxPerSettlement >= 0) return def.MaxPerSettlement;
            float per = 30f * (faith < 0.5f ? 0.5f : faith);
            return 1 + (int)(population / per);
        }

        /// <summary>True if the settlement may add one more of this type now (availability, tier, population, caps).</summary>
        public bool CanAdd(GameDatabase db, Civilization civ, Settlement s, BuildingType type, int existing)
        {
            if (db == null || civ == null || s == null) return false;
            if (!db.IsBuildingAvailable(civ, type)) return false;
            if (!db.TryGetBuilding(type, out var def)) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            return existing < SoftCap(def, FaithOut[(int)type], s.Population);
        }

        /// <summary>Utility of adding one more of a worship type: value x age freshness x race favour / duplicates.</summary>
        public float Score(BuildingDefinition def, int existing, RaceDefinition race)
        {
            if (def == null) return 0f;
            int i = (int)def.Type;
            float v = Value[i] * (1f + 0.1f * (int)def.MinAge);
            if (race != null && race.FavoredBuildings != null)
                for (int k = 0; k < race.FavoredBuildings.Length; k++)
                    if (race.FavoredBuildings[k] == def.Type) { v *= 1.25f; break; }
            return v / (1f + 1.25f * (existing < 0 ? 0 : existing));
        }

        /// <summary>
        /// Picks the best worship building to add (Faith output at least <paramref name="minFaith"/>).
        /// <paramref name="counts"/> holds existing + queued copies per BuildingType. Iterates in enum order (deterministic).
        /// </summary>
        public bool TryPick(GameDatabase db, Civilization civ, Settlement s, int[] counts, RaceDefinition race, float minFaith, out BuildingType best)
        {
            best = BuildingType.TownCenter;
            float bestScore = 0f;
            bool found = false;
            for (int k = 0; k < _worship.Count; k++)
            {
                var t = _worship[k];
                int i = (int)t;
                if (FaithOut[i] < minFaith) continue;
                int c = counts != null && i < counts.Length ? counts[i] : 0;
                if (!CanAdd(db, civ, s, t, c)) continue;
                float sc = Score(db.Building(t), c, race);
                if (!found || sc > bestScore) { best = t; bestScore = sc; found = true; }
            }
            return found;
        }

        /// <summary>Picks the most valuable faith wonder this settlement may start (caller checks civ-wide uniqueness).</summary>
        public bool TryPickWonder(GameDatabase db, Civilization civ, Settlement s, int[] counts, out BuildingType best)
        {
            best = BuildingType.TownCenter;
            float bestValue = 0f;
            bool found = false;
            for (int k = 0; k < _wonders.Count; k++)
            {
                var t = _wonders[k];
                int i = (int)t;
                int c = counts != null && i < counts.Length ? counts[i] : 0;
                if (c > 0 || !CanAdd(db, civ, s, t, c)) continue;
                if (!found || Value[i] > bestValue) { best = t; bestValue = Value[i]; found = true; }
            }
            return found;
        }
    }
}
