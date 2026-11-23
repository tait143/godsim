using System;
using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.ContentReport
{
    internal enum Severity { Error = 0, Warning = 1, Info = 2 }

    /// <summary>One content/balance problem found in the data.</summary>
    internal sealed class Finding
    {
        public Severity Severity;
        public string Area;
        public string Message;
        public Finding(Severity s, string area, string message) { Severity = s; Area = area; Message = message; }
        public override string ToString() => Severity.ToString().ToUpperInvariant() + " [" + Area + "] " + Message;
    }

    /// <summary>
    /// Derived, read-only view of the database used by the report, the JSON dump and the checks.
    /// Availability is computed with the shared rules (<see cref="GameDatabase.CanResearch"/> and
    /// <see cref="GameDatabase.IsBuildingAvailable"/>) by simulating, per race, a civilization that researches
    /// every tech it can in each age (best case: unlocks apply as early as possible).
    /// </summary>
    internal sealed partial class ContentModel
    {
        public readonly GameDatabase Db;
        public readonly LoadResult Load;
        public readonly List<RaceDefinition> Races = new List<RaceDefinition>();
        public readonly List<AgeDefinition> Ages = new List<AgeDefinition>();
        public readonly List<BuildingDefinition> Buildings = new List<BuildingDefinition>();
        public readonly List<GodPowerDefinition> Powers = new List<GodPowerDefinition>();
        public readonly List<TechDefinition> Techs = new List<TechDefinition>();

        public readonly int NumAges = Counts.Ages;
        public readonly int NumRaces = Counts.Races;

        /// <summary>[race, techId]: first age the race can research the tech, -1 = never.</summary>
        public int[,] TechFirstAge;
        /// <summary>[race, age, building]: available under the shared rule after researching everything possible.</summary>
        public bool[,,] Available;
        /// <summary>[race, building]: first age available, -1 = never.</summary>
        public int[,] BuildingFirstAge;
        /// <summary>[race, resource]: first age a building can produce the resource (Gold also via Commerce, Food via foraging); -1 = never.</summary>
        public int[,] ResourceFirstAge;
        /// <summary>building -> techs whose UnlockBuilding effect targets it.</summary>
        public readonly List<TechDefinition>[] UnlockedBy = NewLists<TechDefinition>(Counts.Buildings);
        /// <summary>resource -> buildings producing / consuming it as worker input / paying it as construction cost.</summary>
        public readonly List<BuildingDefinition>[] Producers = NewLists<BuildingDefinition>(Counts.Resources);
        public readonly List<BuildingDefinition>[] InputUsers = NewLists<BuildingDefinition>(Counts.Resources);
        public readonly List<BuildingDefinition>[] CostUsers = NewLists<BuildingDefinition>(Counts.Resources);
        /// <summary>service -> buildings providing it.</summary>
        public readonly List<BuildingDefinition>[] ServiceProviders = NewLists<BuildingDefinition>(Counts.Services);
        public readonly List<Finding> Findings = new List<Finding>();

        public ContentModel(LoadResult load)
        {
            Load = load;
            Db = load.Db;
            foreach (var r in Db.Races) if (r != null) Races.Add(r);
            foreach (var a in Db.Ages) if (a != null) Ages.Add(a);
            foreach (var b in Db.Buildings) if (b != null) Buildings.Add(b);
            foreach (var p in Db.GodPowers) if (p != null) Powers.Add(p);
            foreach (var t in Db.Techs) if (t != null) Techs.Add(t);
            IndexFlows();
            SimulateRaces();
            ComputeResourceAges();
            RunChecks();
            Findings.Sort((x, y) =>
            {
                int c = x.Severity.CompareTo(y.Severity);
                if (c != 0) return c;
                c = string.CompareOrdinal(x.Area, y.Area);
                return c != 0 ? c : string.CompareOrdinal(x.Message, y.Message);
            });
        }

        public int ErrorCount => Count(Severity.Error);
        public int WarningCount => Count(Severity.Warning);
        public int Count(Severity s) { int n = 0; foreach (var f in Findings) if (f.Severity == s) n++; return n; }

        // ------------------------------------------------------------------ lookups & naming
        public AgeDefinition AgeDef(int age) => age >= 0 && age < Db.Ages.Count ? Db.Ages[age] : null;
        public RaceDefinition RaceDef(int race) => race >= 0 && race < Db.Races.Count ? Db.Races[race] : null;
        public BuildingDefinition BuildingDef(BuildingType t) => Db.TryGetBuilding(t, out var d) ? d : null;

        public string AgeName(int age) => age < 0 ? "never" : ((AgeId)age).ToString();
        public string RaceName(int race) { var r = RaceDef(race); return r != null && r.Name.Length > 0 ? r.Name : ((RaceId)race).ToString(); }
        public string BuildingName(BuildingType t) { var d = BuildingDef(t); return d != null && d.Name.Length > 0 ? d.Name : t.ToString(); }

        public string RacesText(RaceMask mask)
        {
            if ((mask & RaceMask.All) == RaceMask.All) return "All";
            if ((mask & RaceMask.All) == RaceMask.None) return "None";
            var parts = new List<string>();
            for (int r = 0; r < NumRaces; r++) if (mask.Allows((RaceId)r)) parts.Add(RaceName(r));
            return string.Join(", ", parts);
        }

        /// <summary>Races that can ever build a building (registered races only).</summary>
        public RaceMask EffectiveRaces(BuildingDefinition b)
        {
            var m = RaceMask.None;
            for (int r = 0; r < NumRaces; r++) if (BuildingFirstAge[r, (int)b.Type] >= 0) m |= ((RaceId)r).ToMask();
            return m;
        }

        /// <summary>Earliest age any race gets the building (-1 = never).</summary>
        public int FirstAgeAnyRace(BuildingDefinition b)
        {
            int best = -1;
            for (int r = 0; r < NumRaces; r++)
            {
                int a = BuildingFirstAge[r, (int)b.Type];
                if (a >= 0 && (best < 0 || a < best)) best = a;
            }
            return best;
        }

        public static float TotalCost(BuildingDefinition b)
        {
            float s = 0f;
            foreach (var c in b.Cost ?? ResourceAmount.None) s += Math.Max(0f, c.Amount);
            return s;
        }

        public static float ServicePoints(BuildingDefinition b, ServiceType svc)
        {
            float s = 0f;
            foreach (var x in b.Services ?? ServiceAmount.None) if (x.Type == svc) s += x.Amount;
            return s;
        }

        public static float OutputPerWorker(BuildingDefinition b, ResourceType res)
        {
            float s = 0f;
            foreach (var x in b.OutputsPerWorker ?? ResourceAmount.None) if (x.Type == res) s += x.Amount;
            return s;
        }

        public static float InputPerWorker(BuildingDefinition b, ResourceType res)
        {
            float s = 0f;
            foreach (var x in b.InputsPerWorker ?? ResourceAmount.None) if (x.Type == res) s += x.Amount;
            return s;
        }

        /// <summary>Techs of an age in registration order.</summary>
        public List<TechDefinition> TechsOf(int age)
        {
            var l = new List<TechDefinition>();
            foreach (var t in Techs) if ((int)t.Age == age) l.Add(t);
            return l;
        }

        /// <summary>Number of techs of an age a race can research (best case).</summary>
        public int ResearchableCount(int race, int age)
        {
            int n = 0;
            foreach (var t in Techs) if ((int)t.Age == age && TechFirstAge[race, t.Id] >= 0) n++;
            return n;
        }

        // ------------------------------------------------------------------ derivation
        private void IndexFlows()
        {
            foreach (var b in Buildings)
            {
                foreach (var o in b.OutputsPerWorker ?? ResourceAmount.None) if (o.Amount > 0f && !Producers[(int)o.Type].Contains(b)) Producers[(int)o.Type].Add(b);
                foreach (var i in b.InputsPerWorker ?? ResourceAmount.None) if (i.Amount > 0f && !InputUsers[(int)i.Type].Contains(b)) InputUsers[(int)i.Type].Add(b);
                foreach (var c in b.Cost ?? ResourceAmount.None) if (c.Amount > 0f && !CostUsers[(int)c.Type].Contains(b)) CostUsers[(int)c.Type].Add(b);
                foreach (var s in b.Services ?? ServiceAmount.None) if (s.Amount > 0f && !ServiceProviders[(int)s.Type].Contains(b)) ServiceProviders[(int)s.Type].Add(b);
            }
            foreach (var t in Techs)
                foreach (var e in t.Effects ?? new TechEffect[0])
                    if (e.Kind == TechEffectKind.UnlockBuilding && (int)e.Building < Counts.Buildings && !UnlockedBy[(int)e.Building].Contains(t))
                        UnlockedBy[(int)e.Building].Add(t);
        }

        private void SimulateRaces()
        {
            TechFirstAge = new int[NumRaces, Math.Max(1, Techs.Count)];
            Available = new bool[NumRaces, NumAges, Counts.Buildings];
            BuildingFirstAge = new int[NumRaces, Counts.Buildings];
            for (int r = 0; r < NumRaces; r++)
            {
                for (int t = 0; t < Techs.Count; t++) TechFirstAge[r, t] = -1;
                for (int b = 0; b < Counts.Buildings; b++) BuildingFirstAge[r, b] = -1;
                if (RaceDef(r) == null) continue; // unregistered race: nothing is available
                var civ = new Civilization { Race = (RaceId)r };
                for (int a = 0; a < NumAges; a++)
                {
                    civ.Age = (AgeId)a;
                    bool changed = true;
                    while (changed)
                    {
                        changed = false;
                        foreach (var t in Techs)
                        {
                            if (!Db.CanResearch(civ, t)) continue;
                            civ.KnownTechs.Add(t.Id);
                            TechFirstAge[r, t.Id] = a;
                            foreach (var e in t.Effects ?? new TechEffect[0])
                                if (e.Kind == TechEffectKind.UnlockBuilding && (int)e.Building < Counts.Buildings)
                                    civ.UnlockedBuildings[(int)e.Building] = true;
                            changed = true;
                        }
                    }
                    for (int b = 0; b < Counts.Buildings; b++)
                    {
                        bool ok = Db.IsBuildingAvailable(civ, (BuildingType)b);
                        Available[r, a, b] = ok;
                        if (ok && BuildingFirstAge[r, b] < 0) BuildingFirstAge[r, b] = a;
                    }
                }
            }
        }

        private void ComputeResourceAges()
        {
            ResourceFirstAge = new int[NumRaces, Counts.Resources];
            for (int r = 0; r < NumRaces; r++)
            {
                for (int res = 0; res < Counts.Resources; res++)
                {
                    int best = -1;
                    foreach (var b in Producers[res]) best = MinAge(best, BuildingFirstAge[r, (int)b.Type]);
                    if (res == (int)ResourceType.Gold)
                        foreach (var b in ServiceProviders[(int)ServiceType.Commerce]) best = MinAge(best, BuildingFirstAge[r, (int)b.Type]);
                    if (res == (int)ResourceType.Food && RaceDef(r) != null) best = 0; // idle adults forage
                    ResourceFirstAge[r, res] = best;
                }
            }
        }

        private static int MinAge(int best, int a) => a < 0 ? best : (best < 0 || a < best ? a : best);

        private static List<T>[] NewLists<T>(int n)
        {
            var a = new List<T>[n];
            for (int i = 0; i < n; i++) a[i] = new List<T>();
            return a;
        }

        private void Add(Severity s, string area, string msg) => Findings.Add(new Finding(s, area, msg));
    }
}
