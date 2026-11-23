using System;
using System.Collections.Generic;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Runtime invariant checker and conservative repairer for <see cref="WorldState"/>. Detects corruption from bugs,
    /// tampered saves or hostile mods before it spreads (NaN propagation, dangling ids, broken back-links) and fixes the
    /// safe subset.
    /// <para>Checked invariants:</para>
    /// <list type="bullet">
    /// <item><description>no NaN/Infinity in people, buildings, settlements (incl. stockpiles and service arrays), civilizations
    /// (incl. modifiers), armies, diplomacy, god, weather, tile layers and the Memory blackboards;</description></item>
    /// <item><description>enum fields hold defined values (an undefined value would index arrays out of range elsewhere);</description></item>
    /// <item><description>residents &lt;= HousingCapacity and workers &lt;= JobSlots;</description></item>
    /// <item><description>ids valid and links symmetric: Person.HomeId and Building.Residents, Person.WorkplaceId and Building.Workers,
    /// Person.SettlementId and Settlement.ResidentIds, Building.SettlementId and Settlement.BuildingIds,
    /// Settlement.CivilizationId and Civilization.SettlementIds, Person.ArmyId and Army.SoldierIds, plus capital, leader,
    /// town-center, spouse, tile owner and research ids;</description></item>
    /// <item><description>map.BuildingAt consistent with building footprints;</description></item>
    /// <item><description>id allocators ahead of every existing id (otherwise the next Create* would throw);</description></item>
    /// <item><description>living population &lt;= MaxPopulation * 1.05.</description></item>
    /// </list>
    /// <para>
    /// <see cref="Repair(WorldState)"/> fixes: non-finite values to defaults, undefined enums to the nearest valid value,
    /// dangling ids to -1, invalid/duplicate list entries removed, missing back-links added where the forward link is
    /// authoritative (settlement and civilization membership), over-capacity houses and over-staffed workplaces evicted
    /// from the end of the list (residents become homeless / workers unemployed), stale tile references cleared, and id
    /// allocators advanced. It never creates or destroys entities and never changes the population. Everything is
    /// deterministic (index-order iteration; dictionary keys sorted before use).
    /// </para>
    /// Check and Repair never throw; an unexpected exception aborts the scan and is reported as an issue.
    /// </summary>
    public static class IntegrityChecker
    {
        /// <summary>Population cap used when no configuration is supplied (the upper bound of SimConfig.Sanitize).</summary>
        public const int AbsoluteMaxPopulation = 250000;
        /// <summary>Tolerance factor over MaxPopulation before the population counts as an integrity issue.</summary>
        public const float PopulationTolerance = 1.05f;
        /// <summary>Upper bound for the maxIssues argument.</summary>
        public const int MaxIssuesLimit = 10000;

        /// <summary>Checks every invariant and returns up to <paramref name="maxIssues"/> human-readable issues (empty = healthy).
        /// Uses <see cref="AbsoluteMaxPopulation"/> for the population cap.</summary>
        public static List<string> Check(WorldState s, GameDatabase db, int maxIssues = 100) => Check(s, db, maxIssues, AbsoluteMaxPopulation);

        /// <summary>Checks every invariant using the configured population cap. Pure: never mutates state.</summary>
        public static List<string> Check(WorldState s, GameDatabase db, int maxIssues, int maxPopulation)
        {
            var issues = new List<string>();
            if (s == null) { issues.Add("World state is missing."); return issues; }
            var scan = new Scan(s, db, false, MathUtil.Clamp(maxIssues, 1, MaxIssuesLimit), maxPopulation, issues);
            scan.Run();
            return issues;
        }

        /// <summary>Repairs every safe issue that does not need the database. Returns the number of fixes applied.</summary>
        public static int Repair(WorldState s) => Repair(s, null);

        /// <summary>Repairs every safe issue; with a database it also evicts workers beyond a workplace's JobSlots and
        /// drops unknown tech ids. Returns the number of fixes applied.</summary>
        public static int Repair(WorldState s, GameDatabase db)
        {
            if (s == null) return 0;
            var scan = new Scan(s, db, true, int.MaxValue, AbsoluteMaxPopulation, null);
            scan.Run();
            return scan.Fixes;
        }

        // ==================================================================================== scanner

        private sealed class Scan
        {
            private readonly WorldState _s;
            private readonly GameDatabase _db;
            private readonly bool _repair;
            private readonly int _max;
            private readonly int _maxPopulation;
            private readonly List<string> _issues;
            private readonly HashSet<long> _residentPairs = new HashSet<long>();
            private readonly HashSet<long> _buildingPairs = new HashSet<long>();

            public int Fixes;

            public Scan(WorldState s, GameDatabase db, bool repair, int max, int maxPopulation, List<string> issues)
            {
                _s = s; _db = db; _repair = repair; _max = max; _maxPopulation = maxPopulation; _issues = issues;
            }

            private bool Full => !_repair && _issues.Count >= _max;

            /// <summary>Records a fixable issue. Returns true when the caller should apply the fix (repair mode).</summary>
            private bool Issue(string message)
            {
                if (_repair) { Fixes++; return true; }
                if (_issues.Count < _max) _issues.Add(message);
                return false;
            }

            /// <summary>Records an issue that cannot be repaired safely (reported in check mode only).</summary>
            private void Unfixable(string message)
            {
                if (!_repair && _issues.Count < _max) _issues.Add(message);
            }

            private static bool Bad(float v) => float.IsNaN(v) || float.IsInfinity(v);

            private void F(ref float v, float fallback, string kind, int id, string field)
            {
                if (Bad(v) && Issue(kind + " #" + id + ": " + field + " is not a finite number.")) v = fallback;
            }

            private void FArr(float[] arr, float fallback, string kind, int id, string field)
            {
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                    if (Bad(arr[i]) && Issue(kind + " #" + id + ": " + field + "[" + i + "] is not a finite number.")) arr[i] = fallback;
            }

            private void Memory(Dictionary<string, float> memory, string kind, int id)
            {
                if (memory == null || memory.Count == 0) return;
                List<string> bad = null;
                foreach (var kv in memory) if (Bad(kv.Value)) (bad ?? (bad = new List<string>())).Add(kv.Key);
                if (bad == null) return;
                bad.Sort(StringComparer.Ordinal); // deterministic order
                for (int i = 0; i < bad.Count; i++)
                    if (Issue(kind + " #" + id + ": a Memory value is not a finite number.")) memory[bad[i]] = 0f;
            }

            public void Run()
            {
                try
                {
                    CheckGlobals();
                    if (Full) return;
                    CheckSettlements();
                    if (Full) return;
                    CheckBuildings();
                    if (Full) return;
                    CheckPeople();
                    if (Full) return;
                    CheckCivilizations();
                    if (Full) return;
                    CheckArmiesAndDiplomacy();
                    if (Full) return;
                    CheckMap();
                    if (Full) return;
                    CheckIdAllocators();
                }
                catch (Exception ex)
                {
                    if (!_repair && _issues.Count < _max) _issues.Add("Integrity scan aborted (" + ex.GetType().Name + "); state is severely corrupted.");
                }
            }

            // ------------------------------------------------------------------ god, weather, world memory

            private void CheckGlobals()
            {
                var g = _s.God;
                if (g != null)
                {
                    F(ref g.MaxMana, 500f, "God", 0, "MaxMana");
                    if (!Bad(g.MaxMana) && g.MaxMana < 0f && Issue("God: MaxMana is negative.")) g.MaxMana = 0f;
                    F(ref g.Mana, 0f, "God", 0, "Mana");
                    if (!Bad(g.Mana) && g.Mana < 0f && Issue("God: Mana is negative.")) g.Mana = 0f;
                    F(ref g.ManaPerTick, 0f, "God", 0, "ManaPerTick");
                    F(ref g.Alignment, 0f, "God", 0, "Alignment");
                }
                var w = _s.Weather;
                if (w != null)
                {
                    F(ref w.Rainfall, 1f, "Weather", 0, "Rainfall");
                    F(ref w.TemperatureOffset, 0f, "Weather", 0, "TemperatureOffset");
                    F(ref w.FoodYieldMultiplier, 1f, "Weather", 0, "FoodYieldMultiplier");
                    F(ref w.StormIntensity, 0f, "Weather", 0, "StormIntensity");
                    if (w.DroughtTicksRemaining < 0 && Issue("Weather: DroughtTicksRemaining is negative.")) w.DroughtTicksRemaining = 0;
                    if (w.RainTicksRemaining < 0 && Issue("Weather: RainTicksRemaining is negative.")) w.RainTicksRemaining = 0;
                    if (!Enum.IsDefined(typeof(Season), w.Season) && Issue("Weather: Season is not defined.")) w.Season = SimTime.SeasonOf(_s.Tick);
                }
                Memory(_s.Memory, "World", 0);
            }

            // ------------------------------------------------------------------ settlements

            private void CheckSettlements()
            {
                var map = _s.Map;
                var items = _s.Settlements.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    if (Full) return;
                    var st = items[i];
                    if (st == null) continue;
                    int id = st.Id;
                    F(ref st.Happiness, 0.6f, "Settlement", id, "Happiness");
                    F(ref st.Health, 1f, "Settlement", id, "Health");
                    F(ref st.Unrest, 0f, "Settlement", id, "Unrest");
                    F(ref st.Faith, 0.5f, "Settlement", id, "Faith");
                    F(ref st.DefenseStrength, 0f, "Settlement", id, "DefenseStrength");
                    FArr(st.ServiceOutput, 0f, "Settlement", id, "ServiceOutput");
                    FArr(st.ServiceCoverage, 0f, "Settlement", id, "ServiceCoverage");
                    FArr(st.JobPriority, 0f, "Settlement", id, "JobPriority");
                    CheckStockpile(st.Stockpile, id);
                    Memory(st.Memory, "Settlement", id);
                    if ((int)st.Race >= Counts.Races && Issue("Settlement #" + id + ": Race is not defined.")) st.Race = RaceId.Human;
                    if ((int)st.Tier >= Counts.Tiers && Issue("Settlement #" + id + ": Tier is not defined.")) st.Tier = (SettlementTier)(Counts.Tiers - 1);
                    if (!map.InBounds(st.Center) && Issue("Settlement #" + id + ": Center is outside the map.")) st.Center = map.ClampToBounds(st.Center);
                    if (!st.IsAlive) continue;

                    // ResidentIds: entries must be living people that point back; duplicates removed.
                    var res = st.ResidentIds;
                    int wr = 0;
                    for (int r = 0; r < res.Count; r++)
                    {
                        int pid = res[r];
                        var p = _s.People.Get(pid);
                        string problem = null;
                        if (p == null) problem = "lists missing person #" + pid;
                        else if (!p.IsAlive) problem = "lists dead person #" + pid;
                        else if (p.SettlementId != id) problem = "lists person #" + pid + " who belongs elsewhere";
                        else if (!_residentPairs.Add(Pair(id, pid))) problem = "lists person #" + pid + " twice";
                        if (problem != null && Issue("Settlement #" + id + ": ResidentIds " + problem + ".")) continue;
                        res[wr++] = pid;
                    }
                    if (wr < res.Count) res.RemoveRange(wr, res.Count - wr);

                    // BuildingIds: entries must be live buildings that point back; duplicates removed.
                    var bl = st.BuildingIds;
                    wr = 0;
                    for (int r = 0; r < bl.Count; r++)
                    {
                        int bid = bl[r];
                        var b = _s.Buildings.Get(bid);
                        string problem = null;
                        if (b == null || b.IsDestroyed) problem = "lists missing building #" + bid;
                        else if (b.SettlementId != id) problem = "lists building #" + bid + " that belongs elsewhere";
                        else if (!_buildingPairs.Add(Pair(id, bid))) problem = "lists building #" + bid + " twice";
                        if (problem != null && Issue("Settlement #" + id + ": BuildingIds " + problem + ".")) continue;
                        bl[wr++] = bid;
                    }
                    if (wr < bl.Count) bl.RemoveRange(wr, bl.Count - wr);

                    if (st.TownCenterId >= 0)
                    {
                        var tc = _s.Buildings.Get(st.TownCenterId);
                        if ((tc == null || tc.IsDestroyed) && Issue("Settlement #" + id + ": TownCenterId refers to a missing building.")) st.TownCenterId = -1;
                    }
                    else if (st.TownCenterId < -1 && Issue("Settlement #" + id + ": TownCenterId is invalid.")) st.TownCenterId = -1;

                    if (st.CivilizationId < 0 || _s.Civilizations.Get(st.CivilizationId) == null)
                        Unfixable("Settlement #" + id + ": CivilizationId refers to a missing civilization.");
                }
            }

            private void CheckStockpile(Stockpile sp, int id)
            {
                if (sp == null) return;
                if ((Bad(sp.Capacity) || sp.Capacity < 0f) && Issue("Settlement #" + id + ": stockpile capacity is invalid.")) sp.Capacity = Stockpile.DefaultCapacity;
                for (int t = 0; t < Counts.Resources; t++)
                {
                    var type = (ResourceType)t;
                    float v = sp.Get(type);
                    if ((Bad(v) || v < 0f) && Issue("Settlement #" + id + ": stockpile " + type + " is invalid.")) sp.SetRaw(type, 0f);
                }
                FArr(sp.AvgIncome, 0f, "Settlement", id, "Stockpile.AvgIncome");
                FArr(sp.AvgExpense, 0f, "Settlement", id, "Stockpile.AvgExpense");
            }

            // ------------------------------------------------------------------ buildings

            private void CheckBuildings()
            {
                var map = _s.Map;
                var items = _s.Buildings.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    if (Full) return;
                    var b = items[i];
                    if (b == null) continue;
                    int id = b.Id;
                    F(ref b.ConstructionProgress, b.IsComplete ? 1f : 0f, "Building", id, "ConstructionProgress");
                    F(ref b.Condition, 1f, "Building", id, "Condition");
                    F(ref b.Efficiency, 0f, "Building", id, "Efficiency");
                    if (b.IsDestroyed) continue;

                    BuildingDefinition def = null;
                    if ((int)b.Type >= Counts.Buildings) Unfixable("Building #" + id + ": Type is not defined.");
                    else if (_db != null && !_db.TryGetBuilding(b.Type, out def)) Unfixable("Building #" + id + ": Type " + b.Type + " is not registered.");
                    if (b.Width < 1 || b.Height < 1 || b.Width > 8 || b.Height > 8) Unfixable("Building #" + id + ": footprint size is invalid.");
                    else if (!map.InBounds(b.Origin) || !map.InBounds(b.Origin.X + b.Width - 1, b.Origin.Y + b.Height - 1))
                        Unfixable("Building #" + id + ": footprint lies outside the map.");
                    if ((int)b.StyleRace >= Counts.Races && Issue("Building #" + id + ": StyleRace is not defined.")) b.StyleRace = RaceId.Human;
                    if ((int)b.StyleAge >= Counts.Ages && Issue("Building #" + id + ": StyleAge is not defined.")) b.StyleAge = AgeId.Dawn;
                    if (b.HousingCapacity < 0 && Issue("Building #" + id + ": HousingCapacity is negative.")) b.HousingCapacity = 0;

                    // Residents: living people whose HomeId points back; no duplicates; within capacity.
                    var list = b.Residents;
                    HashSet<int> seen = null;
                    int wr = 0;
                    for (int r = 0; r < list.Count; r++)
                    {
                        int pid = list[r];
                        var p = _s.People.Get(pid);
                        string problem = null;
                        if (p == null || !p.IsAlive) problem = "lists missing or dead person #" + pid;
                        else if (p.HomeId != id) problem = "lists person #" + pid + " who lives elsewhere";
                        else if (SeenBefore(list, r, pid, ref seen)) problem = "lists person #" + pid + " twice";
                        if (problem != null && Issue("Building #" + id + ": Residents " + problem + ".")) continue;
                        list[wr++] = pid;
                    }
                    if (wr < list.Count) list.RemoveRange(wr, list.Count - wr);
                    if (list.Count > Math.Max(0, b.HousingCapacity) &&
                        Issue("Building #" + id + ": " + list.Count + " residents exceed its capacity of " + b.HousingCapacity + "."))
                    {
                        int cap = Math.Max(0, b.HousingCapacity);
                        for (int r = list.Count - 1; r >= cap; r--)
                        {
                            var p = _s.People.Get(list[r]);
                            if (p != null && p.HomeId == id) p.HomeId = -1;
                            list.RemoveAt(r);
                        }
                    }

                    // Workers: living people whose WorkplaceId points back; no duplicates; within JobSlots (needs db).
                    list = b.Workers;
                    seen = null;
                    wr = 0;
                    for (int r = 0; r < list.Count; r++)
                    {
                        int pid = list[r];
                        var p = _s.People.Get(pid);
                        string problem = null;
                        if (p == null || !p.IsAlive) problem = "lists missing or dead person #" + pid;
                        else if (p.WorkplaceId != id) problem = "lists person #" + pid + " who works elsewhere";
                        else if (SeenBefore(list, r, pid, ref seen)) problem = "lists person #" + pid + " twice";
                        if (problem != null && Issue("Building #" + id + ": Workers " + problem + ".")) continue;
                        list[wr++] = pid;
                    }
                    if (wr < list.Count) list.RemoveRange(wr, list.Count - wr);
                    if (def != null && list.Count > Math.Max(0, def.JobSlots) &&
                        Issue("Building #" + id + ": " + list.Count + " workers exceed its " + def.JobSlots + " job slots."))
                    {
                        int cap = Math.Max(0, def.JobSlots);
                        for (int r = list.Count - 1; r >= cap; r--)
                        {
                            var p = _s.People.Get(list[r]);
                            if (p != null && p.WorkplaceId == id) { p.WorkplaceId = -1; p.Job = JobType.None; }
                            list.RemoveAt(r);
                        }
                    }

                    // Settlement membership (BuildingIds pairs were collected from valid entries above).
                    var st = b.SettlementId >= 0 ? _s.Settlements.Get(b.SettlementId) : null;
                    if (st == null || !st.IsAlive) Unfixable("Building #" + id + ": SettlementId refers to a missing settlement.");
                    else if (!_buildingPairs.Contains(Pair(st.Id, id)) && Issue("Building #" + id + ": missing from its settlement's BuildingIds."))
                    {
                        st.BuildingIds.Add(id);
                        _buildingPairs.Add(Pair(st.Id, id));
                    }
                }
            }

            /// <summary>
            /// Duplicate test for list[r] among the already-kept entries. Small lists use a linear scan (no allocation);
            /// large (possibly tampered) lists use a per-list set so the scan stays O(n). Works during in-place
            /// compaction because the first occurrence of a valid id is always copied into the kept prefix.
            /// </summary>
            private static bool SeenBefore(List<int> list, int r, int id, ref HashSet<int> seen)
            {
                if (list.Count <= 32)
                {
                    for (int i = 0; i < r; i++) if (list[i] == id) return true;
                    return false;
                }
                if (seen == null) seen = new HashSet<int>();
                return !seen.Add(id);
            }

            // ------------------------------------------------------------------ people

            private void CheckPeople()
            {
                var map = _s.Map;
                var items = _s.People.Items;
                int alive = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (Full) return;
                    var p = items[i];
                    if (p == null) continue;
                    int id = p.Id;
                    F(ref p.Health, 1f, "Person", id, "Health");
                    F(ref p.Happiness, 0.6f, "Person", id, "Happiness");
                    F(ref p.Satiation, 1f, "Person", id, "Satiation");
                    F(ref p.Faith, 0.5f, "Person", id, "Faith");
                    F(ref p.Skill, 1f, "Person", id, "Skill");
                    F(ref p.Experience, 0f, "Person", id, "Experience");
                    if ((Bad(p.AgeYears) || p.AgeYears < 0f) && Issue("Person #" + id + ": AgeYears is invalid."))
                        p.AgeYears = Math.Max(0f, MathUtil.Sanitize(SimTime.TicksToYears(_s.Tick - p.BirthTick), 0f));
                    if (!p.IsAlive) continue;
                    alive++;

                    if ((int)p.Race >= Counts.Races && Issue("Person #" + id + ": Race is not defined.")) p.Race = RaceId.Human;
                    if ((int)p.Job >= Counts.Jobs && Issue("Person #" + id + ": Job is not defined.")) p.Job = JobType.None;
                    if (!Enum.IsDefined(typeof(LifeStage), p.Stage) && Issue("Person #" + id + ": Stage is not defined.")) p.Stage = LifeStage.Adult;
                    if (!Enum.IsDefined(typeof(Sex), p.Sex) && Issue("Person #" + id + ": Sex is not defined.")) p.Sex = Sex.Female;
                    if (!map.InBounds(p.Position) && Issue("Person #" + id + ": Position is outside the map.")) p.Position = map.ClampToBounds(p.Position);

                    // Home
                    if (p.HomeId >= 0)
                    {
                        var h = _s.Buildings.Get(p.HomeId);
                        if (h == null || h.IsDestroyed) { if (Issue("Person #" + id + ": HomeId refers to a missing building.")) p.HomeId = -1; }
                        else if (!h.Residents.Contains(id)) { if (Issue("Person #" + id + ": not listed among its home's residents.")) p.HomeId = -1; }
                    }
                    else if (p.HomeId < -1 && Issue("Person #" + id + ": HomeId is invalid.")) p.HomeId = -1;

                    // Workplace
                    if (p.WorkplaceId >= 0)
                    {
                        var w = _s.Buildings.Get(p.WorkplaceId);
                        bool bad = false;
                        if (w == null || w.IsDestroyed) bad = Issue("Person #" + id + ": WorkplaceId refers to a missing building.");
                        else if (!w.Workers.Contains(id)) bad = Issue("Person #" + id + ": not listed among its workplace's workers.");
                        if (bad) { p.WorkplaceId = -1; p.Job = JobType.None; }
                    }
                    else if (p.WorkplaceId < -1 && Issue("Person #" + id + ": WorkplaceId is invalid.")) p.WorkplaceId = -1;

                    // Settlement membership (forward link is authoritative: add the missing back-link).
                    if (p.SettlementId >= 0)
                    {
                        var st = _s.Settlements.Get(p.SettlementId);
                        if (st == null || !st.IsAlive) { if (Issue("Person #" + id + ": SettlementId refers to a missing settlement.")) p.SettlementId = -1; }
                        else if (!_residentPairs.Contains(Pair(st.Id, id)) && Issue("Person #" + id + ": missing from its settlement's ResidentIds."))
                        {
                            st.ResidentIds.Add(id);
                            _residentPairs.Add(Pair(st.Id, id));
                        }
                    }
                    else if (p.SettlementId < -1 && Issue("Person #" + id + ": SettlementId is invalid.")) p.SettlementId = -1;

                    if (p.CivilizationId >= 0 && _s.Civilizations.Get(p.CivilizationId) == null &&
                        Issue("Person #" + id + ": CivilizationId refers to a missing civilization."))
                    {
                        var st = _s.Settlements.Get(p.SettlementId);
                        p.CivilizationId = st != null && _s.Civilizations.Get(st.CivilizationId) != null ? st.CivilizationId : -1;
                    }
                    if (p.SpouseId >= 0 && _s.People.Get(p.SpouseId) == null && Issue("Person #" + id + ": SpouseId refers to a missing person.")) p.SpouseId = -1;
                    if (p.ArmyId >= 0)
                    {
                        var a = _s.Diplomacy.Armies.Get(p.ArmyId);
                        if ((a == null || !a.IsAlive) && Issue("Person #" + id + ": ArmyId refers to a missing army.")) p.ArmyId = -1;
                        else if (a != null && a.IsAlive && !a.SoldierIds.Contains(id) && Issue("Person #" + id + ": not listed among its army's soldiers.")) p.ArmyId = -1;
                    }
                }
                long cap = (long)Math.Ceiling(Math.Max(0, _maxPopulation) * (double)PopulationTolerance);
                if (alive > cap) Unfixable("Population " + alive + " exceeds the cap of " + _maxPopulation + " by more than 5%.");
            }

            // ------------------------------------------------------------------ civilizations

            private void CheckCivilizations()
            {
                var civs = _s.Civilizations.Items;
                for (int i = 0; i < civs.Count; i++)
                {
                    if (Full) return;
                    var c = civs[i];
                    if (c == null) continue;
                    int id = c.Id;
                    F(ref c.ResearchPool, 0f, "Civilization", id, "ResearchPool");
                    F(ref c.TotalResearch, 0f, "Civilization", id, "TotalResearch");
                    F(ref c.Devotion, 0.5f, "Civilization", id, "Devotion");
                    F(ref c.GodFavor, 0f, "Civilization", id, "GodFavor");
                    F(ref c.MilitaryPower, 0f, "Civilization", id, "MilitaryPower");
                    var m = c.Modifiers;
                    if (m != null)
                    {
                        FArr(m.Production, 1f, "Civilization", id, "Modifiers.Production");
                        FArr(m.Service, 1f, "Civilization", id, "Modifiers.Service");
                        F(ref m.Research, 1f, "Civilization", id, "Modifiers.Research");
                        F(ref m.Growth, 1f, "Civilization", id, "Modifiers.Growth");
                        F(ref m.BuildSpeed, 1f, "Civilization", id, "Modifiers.BuildSpeed");
                        F(ref m.Faith, 1f, "Civilization", id, "Modifiers.Faith");
                        F(ref m.Military, 1f, "Civilization", id, "Modifiers.Military");
                        F(ref m.Storage, 1f, "Civilization", id, "Modifiers.Storage");
                        F(ref m.Trade, 1f, "Civilization", id, "Modifiers.Trade");
                        F(ref m.MoveSpeed, 1f, "Civilization", id, "Modifiers.MoveSpeed");
                        F(ref m.HealthBonus, 0f, "Civilization", id, "Modifiers.HealthBonus");
                        F(ref m.HappinessBonus, 0f, "Civilization", id, "Modifiers.HappinessBonus");
                        F(ref m.LifespanBonusYears, 0f, "Civilization", id, "Modifiers.LifespanBonusYears");
                    }
                    Memory(c.Memory, "Civilization", id);
                    if ((int)c.Race >= Counts.Races && Issue("Civilization #" + id + ": Race is not defined.")) c.Race = RaceId.Human;
                    if ((int)c.Age >= Counts.Ages && Issue("Civilization #" + id + ": Age is not defined.")) c.Age = (AgeId)(Counts.Ages - 1);

                    if (_db != null)
                    {
                        if (c.CurrentResearchTechId >= 0 && _db.Tech(c.CurrentResearchTechId) == null &&
                            Issue("Civilization #" + id + ": CurrentResearchTechId refers to an unknown tech.")) c.CurrentResearchTechId = -1;
                        List<int> unknown = null;
                        foreach (int t in c.KnownTechs) if (_db.Tech(t) == null) (unknown ?? (unknown = new List<int>())).Add(t);
                        if (unknown != null)
                            for (int k = 0; k < unknown.Count; k++)
                                if (Issue("Civilization #" + id + ": KnownTechs contains unknown tech #" + unknown[k] + ".")) c.KnownTechs.Remove(unknown[k]);
                    }
                    if (!c.IsAlive) continue;

                    var list = c.SettlementIds;
                    HashSet<int> seen = null;
                    int wr = 0;
                    for (int r = 0; r < list.Count; r++)
                    {
                        int sid = list[r];
                        var st = _s.Settlements.Get(sid);
                        string problem = null;
                        if (st == null || !st.IsAlive) problem = "lists missing settlement #" + sid;
                        else if (st.CivilizationId != id) problem = "lists settlement #" + sid + " owned by another civilization";
                        else if (SeenBefore(list, r, sid, ref seen)) problem = "lists settlement #" + sid + " twice";
                        if (problem != null && Issue("Civilization #" + id + ": SettlementIds " + problem + ".")) continue;
                        list[wr++] = sid;
                    }
                    if (wr < list.Count) list.RemoveRange(wr, list.Count - wr);

                    if (c.CapitalId >= 0)
                    {
                        var cap = _s.Settlements.Get(c.CapitalId);
                        if ((cap == null || !cap.IsAlive || cap.CivilizationId != id) && Issue("Civilization #" + id + ": CapitalId refers to a missing or foreign settlement."))
                            c.CapitalId = -1;
                    }
                    if (c.LeaderId >= 0)
                    {
                        var leader = _s.People.Get(c.LeaderId);
                        if ((leader == null || !leader.IsAlive) && Issue("Civilization #" + id + ": LeaderId refers to a missing person.")) c.LeaderId = -1;
                    }
                }

                // Settlement -> civilization back-links (forward link authoritative).
                var sts = _s.Settlements.Items;
                for (int i = 0; i < sts.Count; i++)
                {
                    if (Full) return;
                    var st = sts[i];
                    if (st == null || !st.IsAlive) continue;
                    var c = _s.Civilizations.Get(st.CivilizationId);
                    if (c == null) continue; // reported in CheckSettlements
                    if (!c.IsAlive) { Unfixable("Settlement #" + st.Id + ": belongs to a collapsed civilization."); continue; }
                    if (!c.SettlementIds.Contains(st.Id) && Issue("Settlement #" + st.Id + ": missing from its civilization's SettlementIds.")) c.SettlementIds.Add(st.Id);
                }
            }

            // ------------------------------------------------------------------ armies & diplomacy

            private void CheckArmiesAndDiplomacy()
            {
                var armies = _s.Diplomacy.Armies.Items;
                for (int i = 0; i < armies.Count; i++)
                {
                    if (Full) return;
                    var a = armies[i];
                    if (a == null) continue;
                    F(ref a.MoveProgress, 0f, "Army", a.Id, "MoveProgress");
                    F(ref a.Strength, 0f, "Army", a.Id, "Strength");
                    F(ref a.Morale, 1f, "Army", a.Id, "Morale");
                    if (!a.IsAlive) continue;
                    var list = a.SoldierIds;
                    HashSet<int> seen = null;
                    int wr = 0;
                    for (int r = 0; r < list.Count; r++)
                    {
                        int pid = list[r];
                        var p = _s.People.Get(pid);
                        string problem = null;
                        if (p == null || !p.IsAlive) problem = "lists missing or dead person #" + pid;
                        else if (p.ArmyId != a.Id) problem = "lists person #" + pid + " who serves elsewhere";
                        else if (SeenBefore(list, r, pid, ref seen)) problem = "lists person #" + pid + " twice";
                        if (problem != null && Issue("Army #" + a.Id + ": SoldierIds " + problem + ".")) continue;
                        list[wr++] = pid;
                    }
                    if (wr < list.Count) list.RemoveRange(wr, list.Count - wr);
                }
                foreach (var rel in _s.Diplomacy.AllRelations) // SortedDictionary: deterministic order
                {
                    if (rel == null) continue;
                    if (Bad(rel.Opinion) && Issue("Relation " + rel.CivA + "/" + rel.CivB + ": Opinion is not a finite number.")) rel.Opinion = 0f;
                    if (!Enum.IsDefined(typeof(DiplomaticStance), rel.Stance) && Issue("Relation " + rel.CivA + "/" + rel.CivB + ": Stance is not defined."))
                        rel.Stance = DiplomaticStance.Neutral;
                }
                var wars = _s.Diplomacy.Wars;
                for (int i = 0; i < wars.Count; i++)
                {
                    var w = wars[i];
                    if (w != null && Bad(w.WarScore) && Issue("War #" + w.Id + ": WarScore is not a finite number.")) w.WarScore = 0f;
                }
            }

            // ------------------------------------------------------------------ map

            private void CheckMap()
            {
                var map = _s.Map;
                if (Bad(map.SeaLevel) && Issue("Map: SeaLevel is not a finite number.")) map.SeaLevel = 0f;
                int n = map.TileCount;
                int lastOwner = int.MinValue;
                bool lastOwnerOk = false;
                for (int i = 0; i < n; i++)
                {
                    if (Full) return;
                    if (Bad(map.Elevation[i]) && Issue("Tile " + map.PosOf(i) + ": Elevation is not a finite number.")) map.Elevation[i] = 0f;
                    if (Bad(map.Moisture[i]) && Issue("Tile " + map.PosOf(i) + ": Moisture is not a finite number.")) map.Moisture[i] = 0.5f;
                    if (Bad(map.Temperature[i]) && Issue("Tile " + map.PosOf(i) + ": Temperature is not a finite number.")) map.Temperature[i] = 0.5f;
                    if (Bad(map.Fertility[i]) && Issue("Tile " + map.PosOf(i) + ": Fertility is not a finite number.")) map.Fertility[i] = 0f;
                    if (Bad(map.Resource[i]) && Issue("Tile " + map.PosOf(i) + ": Resource is not a finite number.")) map.Resource[i] = 0f;
                    if ((int)map.Biomes[i] >= Counts.Biomes && Issue("Tile " + map.PosOf(i) + ": Biome is not defined."))
                        map.Biomes[i] = map.Elevation[i] >= map.SeaLevel ? Biome.Grassland : Biome.ShallowWater;

                    int owner = map.Owner[i];
                    if (owner != -1)
                    {
                        if (owner != lastOwner)
                        {
                            lastOwner = owner;
                            var st = owner >= 0 ? _s.Settlements.Get(owner) : null;
                            lastOwnerOk = st != null && st.IsAlive;
                        }
                        if (!lastOwnerOk && Issue("Tile " + map.PosOf(i) + ": owned by a missing settlement.")) map.Owner[i] = -1;
                    }

                    int bid = map.BuildingAt[i];
                    if (bid != -1)
                    {
                        var b = bid >= 0 ? _s.Buildings.Get(bid) : null;
                        if ((b == null || b.IsDestroyed || !b.Occupies(map.PosOf(i))) &&
                            Issue("Tile " + map.PosOf(i) + ": BuildingAt refers to a building that does not occupy it.")) map.BuildingAt[i] = -1;
                    }
                }

                // Footprints must be marked on the map.
                var items = _s.Buildings.Items;
                for (int k = 0; k < items.Count; k++)
                {
                    if (Full) return;
                    var b = items[k];
                    if (b == null || b.IsDestroyed || b.Width < 1 || b.Height < 1 || b.Width > 8 || b.Height > 8) continue;
                    for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                        for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                        {
                            if (!map.InBounds(x, y)) continue; // reported in CheckBuildings
                            int i = map.Index(x, y);
                            int at = map.BuildingAt[i];
                            if (at == b.Id) continue;
                            if (at == -1) { if (Issue("Building #" + b.Id + ": footprint tile (" + x + "," + y + ") is not marked on the map.")) map.BuildingAt[i] = b.Id; }
                            else Unfixable("Building #" + b.Id + ": footprint overlaps building #" + at + " at (" + x + "," + y + ").");
                        }
                }
            }

            // ------------------------------------------------------------------ id allocators

            private void CheckIdAllocators()
            {
                var ids = _s.Ids;
                int maxPerson = 0, maxBuilding = 0, maxSettlement = 0, maxCiv = 0, maxArmy = 0, maxWar = 0;
                var people = _s.People.Items; for (int i = 0; i < people.Count; i++) if (people[i].Id > maxPerson) maxPerson = people[i].Id;
                var bs = _s.Buildings.Items; for (int i = 0; i < bs.Count; i++) if (bs[i].Id > maxBuilding) maxBuilding = bs[i].Id;
                var sts = _s.Settlements.Items; for (int i = 0; i < sts.Count; i++) if (sts[i].Id > maxSettlement) maxSettlement = sts[i].Id;
                var civs = _s.Civilizations.Items; for (int i = 0; i < civs.Count; i++) if (civs[i].Id > maxCiv) maxCiv = civs[i].Id;
                var armies = _s.Diplomacy.Armies.Items; for (int i = 0; i < armies.Count; i++) if (armies[i].Id > maxArmy) maxArmy = armies[i].Id;
                var wars = _s.Diplomacy.Wars; for (int i = 0; i < wars.Count; i++) if (wars[i] != null && wars[i].Id > maxWar) maxWar = wars[i].Id;
                if (ids.NextPersonId <= maxPerson && Issue("Ids: NextPersonId is not ahead of existing people.")) ids.NextPersonId = Next(maxPerson);
                if (ids.NextBuildingId <= maxBuilding && Issue("Ids: NextBuildingId is not ahead of existing buildings.")) ids.NextBuildingId = Next(maxBuilding);
                if (ids.NextSettlementId <= maxSettlement && Issue("Ids: NextSettlementId is not ahead of existing settlements.")) ids.NextSettlementId = Next(maxSettlement);
                if (ids.NextCivilizationId <= maxCiv && Issue("Ids: NextCivilizationId is not ahead of existing civilizations.")) ids.NextCivilizationId = Next(maxCiv);
                if (ids.NextArmyId <= maxArmy && Issue("Ids: NextArmyId is not ahead of existing armies.")) ids.NextArmyId = Next(maxArmy);
                if (ids.NextWarId <= maxWar && Issue("Ids: NextWarId is not ahead of existing wars.")) ids.NextWarId = Next(maxWar);
            }

            private static int Next(int max) => max == int.MaxValue ? int.MaxValue : max + 1;

            private static long Pair(int owner, int member) => ((long)owner << 32) | (uint)member;
        }
    }
}
