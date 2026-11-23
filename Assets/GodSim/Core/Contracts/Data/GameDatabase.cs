using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    /// <summary>
    /// Registry of all static definitions. Content modules register into it at startup
    /// (RaceCatalog, AgeCatalog, TechCatalog, BuildingCatalog, GodPowerCatalog), then the
    /// composition root calls <see cref="Freeze"/>. After freezing, registration throws, so game
    /// data cannot be mutated at runtime (e.g. by a malicious mod or a buggy system).
    /// </summary>
    public sealed class GameDatabase
    {
        private readonly RaceDefinition[] _races = new RaceDefinition[Counts.Races];
        private readonly AgeDefinition[] _ages = new AgeDefinition[Counts.Ages];
        private readonly BuildingDefinition[] _buildings = new BuildingDefinition[Counts.Buildings];
        private readonly GodPowerDefinition[] _powers = new GodPowerDefinition[Counts.GodPowers];
        private readonly List<TechDefinition> _techs = new List<TechDefinition>();
        private readonly Dictionary<string, int> _techByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        private List<int>[] _techsByAge;

        public bool IsFrozen { get; private set; }

        public IReadOnlyList<RaceDefinition> Races => _races;
        public IReadOnlyList<AgeDefinition> Ages => _ages;
        public IReadOnlyList<BuildingDefinition> Buildings => _buildings;
        public IReadOnlyList<GodPowerDefinition> GodPowers => _powers;
        public IReadOnlyList<TechDefinition> Techs => _techs;

        // ------------------------------------------------------------------ registration
        public void Register(RaceDefinition def) { EnsureMutable(); Guard.NotNull(def, nameof(def)); _races[(int)def.Id] = def; }
        public void Register(AgeDefinition def) { EnsureMutable(); Guard.NotNull(def, nameof(def)); _ages[(int)def.Id] = def; }
        public void Register(BuildingDefinition def) { EnsureMutable(); Guard.NotNull(def, nameof(def)); _buildings[(int)def.Type] = def; }
        public void Register(GodPowerDefinition def) { EnsureMutable(); Guard.NotNull(def, nameof(def)); _powers[(int)def.Type] = def; }

        public int Register(TechDefinition def)
        {
            EnsureMutable();
            Guard.NotNull(def, nameof(def));
            if (string.IsNullOrEmpty(def.Key)) throw new ArgumentException("Tech key required.");
            if (_techByKey.ContainsKey(def.Key)) throw new ArgumentException("Duplicate tech key: " + def.Key);
            def.Id = _techs.Count;
            _techs.Add(def);
            _techByKey[def.Key] = def.Id;
            return def.Id;
        }

        // ------------------------------------------------------------------ lookups
        public RaceDefinition Race(RaceId id) => _races[(int)id] ?? throw new InvalidOperationException("Race not registered: " + id);
        public AgeDefinition Age(AgeId id) => _ages[(int)id] ?? throw new InvalidOperationException("Age not registered: " + id);
        public BuildingDefinition Building(BuildingType t) => _buildings[(int)t] ?? throw new InvalidOperationException("Building not registered: " + t);
        public GodPowerDefinition GodPower(GodPowerType t) => _powers[(int)t] ?? throw new InvalidOperationException("God power not registered: " + t);
        public bool TryGetBuilding(BuildingType t, out BuildingDefinition def) { int i = (int)t; def = i >= 0 && i < _buildings.Length ? _buildings[i] : null; return def != null; }
        public TechDefinition Tech(int id) => id >= 0 && id < _techs.Count ? _techs[id] : null;
        public TechDefinition TechByKey(string key) => key != null && _techByKey.TryGetValue(key, out int id) ? _techs[id] : null;
        public int TechIdOf(string key) => key != null && _techByKey.TryGetValue(key, out int id) ? id : -1;

        /// <summary>Tech ids belonging to an age (valid after Freeze).</summary>
        public IReadOnlyList<int> TechsOfAge(AgeId age)
        {
            if (_techsByAge == null) BuildAgeIndex();
            return _techsByAge[(int)age];
        }

        // ------------------------------------------------------------------ rules shared by every module
        /// <summary>Single source of truth for "can this civ build this building type right now" (ignoring placement/cost).</summary>
        public bool IsBuildingAvailable(Civilization civ, BuildingType type)
        {
            if (civ == null || !TryGetBuilding(type, out var def)) return false;
            if (!def.AllowedRaces.Allows(civ.Race)) return false;
            if (civ.Age > def.MaxAge) return false;
            return civ.Age >= def.MinAge || civ.UnlockedBuildings[(int)type];
        }

        /// <summary>True if the civ knows all prerequisites of the tech and does not know it yet.</summary>
        public bool CanResearch(Civilization civ, TechDefinition tech)
        {
            if (civ == null || tech == null) return false;
            if (civ.KnownTechs.Contains(tech.Id)) return false;
            if (!tech.AllowedRaces.Allows(civ.Race)) return false;
            if (tech.Age > civ.Age) return false;
            for (int i = 0; i < tech.Prerequisites.Length; i++)
                if (!civ.KnownTechs.Contains(tech.Prerequisites[i])) return false;
            return true;
        }

        // ------------------------------------------------------------------ lifecycle
        /// <summary>Resolves tech prerequisite keys, validates, and makes the database read-only.</summary>
        public void Freeze()
        {
            if (IsFrozen) return;
            foreach (var t in _techs)
            {
                var ids = new List<int>();
                foreach (var k in t.PrerequisiteKeys ?? new string[0])
                {
                    int id = TechIdOf(k);
                    if (id < 0) throw new SimValidationException("Tech '" + t.Key + "' has unknown prerequisite '" + k + "'.");
                    ids.Add(id);
                }
                t.Prerequisites = ids.ToArray();
            }
            var errors = Validate();
            if (errors.Count > 0) throw new SimValidationException("GameDatabase invalid:\n - " + string.Join("\n - ", errors));
            BuildAgeIndex();
            IsFrozen = true;
        }

        /// <summary>Structural validation of all content. Returns a list of human-readable problems.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            for (int i = 0; i < _races.Length; i++)
            {
                var r = _races[i];
                if (r == null) { errors.Add("Missing race " + (RaceId)i); continue; }
                if (r.BiomeHabitability == null || r.BiomeHabitability.Length != Counts.Biomes) errors.Add("Race " + r.Id + " BiomeHabitability length");
                if (r.ProductionMultiplier == null || r.ProductionMultiplier.Length != Counts.Resources) errors.Add("Race " + r.Id + " ProductionMultiplier length");
                if (r.RaceAffinity == null || r.RaceAffinity.Length != Counts.Races) errors.Add("Race " + r.Id + " RaceAffinity length");
                if (r.LifeExpectancy <= r.AdulthoodAge) errors.Add("Race " + r.Id + " life expectancy must exceed adulthood");
                if (r.Names == null) errors.Add("Race " + r.Id + " has no NameParts");
            }
            for (int i = 0; i < _ages.Length; i++) if (_ages[i] == null) errors.Add("Missing age " + (AgeId)i);
            for (int i = 1; i < _ages.Length; i++)
                if (_ages[i] != null && _ages[i - 1] != null && _ages[i].ResearchThreshold < _ages[i - 1].ResearchThreshold)
                    errors.Add("Age thresholds must be non-decreasing at " + (AgeId)i);
            for (int i = 0; i < _buildings.Length; i++)
            {
                var b = _buildings[i];
                if (b == null) { errors.Add("Missing building " + (BuildingType)i); continue; }
                if (b.Width < 1 || b.Height < 1 || b.Width > 8 || b.Height > 8) errors.Add("Building " + b.Type + " footprint out of range");
                if (b.HousingMax < b.HousingMin) errors.Add("Building " + b.Type + " HousingMax < HousingMin");
                if (b.BuildWork <= 0f) errors.Add("Building " + b.Type + " BuildWork must be > 0");
                if (b.MinAge > b.MaxAge) errors.Add("Building " + b.Type + " MinAge > MaxAge");
                if (b.Job != JobType.None && b.JobSlots <= 0) errors.Add("Building " + b.Type + " has a job but no slots");
            }
            if (_buildings[(int)BuildingType.House] != null && (_buildings[(int)BuildingType.House].HousingMin != 3 || _buildings[(int)BuildingType.House].HousingMax != 5))
                errors.Add("House must hold 3-5 residents (core design rule).");
            for (int i = 0; i < _powers.Length; i++)
            {
                var p = _powers[i];
                if (p == null) { errors.Add("Missing god power " + (GodPowerType)i); continue; }
                if (p.ManaCost < 0 || p.CooldownTicks < 0 || p.MinRadius < 0 || p.MaxRadius < p.MinRadius) errors.Add("God power " + p.Type + " invalid numbers");
            }
            if (_techs.Count == 0) errors.Add("No techs registered");
            foreach (var t in _techs)
            {
                if (t.Cost <= 0) errors.Add("Tech " + t.Key + " cost must be > 0");
                foreach (int pre in t.Prerequisites)
                {
                    var p = Tech(pre);
                    if (p != null && p.Age > t.Age) errors.Add("Tech " + t.Key + " requires later-age tech " + p.Key);
                }
            }
            DetectTechCycles(errors);
            return errors;
        }

        private void DetectTechCycles(List<string> errors)
        {
            var state = new byte[_techs.Count]; // 0 = unvisited, 1 = visiting, 2 = done
            for (int i = 0; i < _techs.Count; i++) if (state[i] == 0 && HasCycle(i, state)) { errors.Add("Tech prerequisite cycle involving " + _techs[i].Key); return; }
        }

        private bool HasCycle(int id, byte[] state)
        {
            state[id] = 1;
            foreach (int p in _techs[id].Prerequisites)
            {
                if (p < 0 || p >= state.Length) continue;
                if (state[p] == 1) return true;
                if (state[p] == 0 && HasCycle(p, state)) return true;
            }
            state[id] = 2;
            return false;
        }

        private void BuildAgeIndex()
        {
            _techsByAge = new List<int>[Counts.Ages];
            for (int i = 0; i < Counts.Ages; i++) _techsByAge[i] = new List<int>();
            foreach (var t in _techs) _techsByAge[(int)t.Age].Add(t.Id);
        }

        private void EnsureMutable() { if (IsFrozen) throw new InvalidOperationException("GameDatabase is frozen; definitions are read-only at runtime."); }
    }
}
