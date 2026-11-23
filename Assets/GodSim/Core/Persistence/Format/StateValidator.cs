using System;
using System.Collections.Generic;

namespace GodSim.Core.Persistence
{
    /// <summary>Outcome of <see cref="StateValidator.ValidateAndRepair"/>.</summary>
    public sealed class StateValidationResult
    {
        /// <summary>Problems that make the state unusable (the loader rejects the save).</summary>
        public readonly List<string> Fatal = new List<string>();
        /// <summary>Benign inconsistencies (dangling optional references, unlisted members...) that were fixed (or would be).</summary>
        public readonly List<string> Repaired = new List<string>();
        /// <summary>True if no fatal problem was found.</summary>
        public bool IsUsable => Fatal.Count == 0;
    }

    /// <summary>
    /// Post-load referential-integrity validation of a <see cref="WorldState"/> graph. Fatal problems
    /// (non-finite floats, ids outside the IdAllocator ranges, out-of-bounds or overlapping building
    /// footprints, people not listed by their settlement, settlements/wars/armies of unknown civs...) reject
    /// the save. Benign ones (dangling home/job/army/spouse/leader/capital references, stale membership lists,
    /// stale map ownership/occupancy, out-of-bounds positions...) are repaired the same way WorldOps would
    /// have cleaned them. Runs in O(total size); never throws.
    /// </summary>
    public static class StateValidator
    {
        /// <summary>Largest accepted building footprint side (definitions use 1..8; slack for future content).</summary>
        public const int MaxBuildingFootprint = 16;

        private const int MaxMessages = 100;

        /// <summary>
        /// Reports every problem without modifying the state. Entries start with "fatal: " or "repairable: ".
        /// An empty list means the state is fully consistent. <paramref name="db"/> may be null (content checks skipped).
        /// </summary>
        public static List<string> Validate(WorldState s, GameDatabase db)
        {
            var r = Run(s, db, false);
            var all = new List<string>(r.Fatal.Count + r.Repaired.Count);
            foreach (var m in r.Fatal) all.Add("fatal: " + m);
            foreach (var m in r.Repaired) all.Add("repairable: " + m);
            return all;
        }

        /// <summary>Validates and repairs benign problems in place. The state must not be used if the result has fatal problems.</summary>
        public static StateValidationResult ValidateAndRepair(WorldState s, GameDatabase db) => Run(s, db, true);

        private sealed class Ctx
        {
            public readonly StateValidationResult Result = new StateValidationResult();
            public readonly bool Repair;
            private int _fatalDropped, _fixDropped;
            public Ctx(bool repair) { Repair = repair; }
            public void Fatal(string m) { if (Result.Fatal.Count < MaxMessages) Result.Fatal.Add(m); else _fatalDropped++; }
            public void Fix(string m) { if (Result.Repaired.Count < MaxMessages) Result.Repaired.Add(m); else _fixDropped++; }
            public void Finish()
            {
                if (_fatalDropped > 0) Result.Fatal.Add("(" + _fatalDropped + " more fatal problems)");
                if (_fixDropped > 0) Result.Repaired.Add("(" + _fixDropped + " more repairs)");
            }
            public void Finite(float v, string what) { if (float.IsNaN(v) || float.IsInfinity(v)) Fatal(what + " is not finite"); }
            // The overloads below build their message only on failure (validation of millions of entities must not allocate per entity).
            public void Finite(float[] a, string kind, int id, string what)
            {
                if (a == null) { Fatal(kind + " " + id + " " + what + " is null"); return; }
                for (int i = 0; i < a.Length; i++) if (float.IsNaN(a[i]) || float.IsInfinity(a[i])) { Fatal(kind + " " + id + " " + what + "[" + i + "] is not finite"); return; }
            }
            public void Memory(Dictionary<string, float> m, string kind, int id)
            {
                foreach (var kv in m) if (float.IsNaN(kv.Value) || float.IsInfinity(kv.Value)) Fatal(kind + " " + id + " memory['" + kv.Key + "'] is not finite");
            }
        }

        private static StateValidationResult Run(WorldState s, GameDatabase db, bool repair)
        {
            var c = new Ctx(repair);
            try { RunCore(s, db, c); }
            catch (Exception ex) { c.Fatal("validator failed on malformed state: " + ex.GetType().Name + ": " + ex.Message); }
            c.Finish();
            return c.Result;
        }

        private static void RunCore(WorldState s, GameDatabase db, Ctx c)
        {
            if (s == null) { c.Fatal("state is null"); return; }
            var map = s.Map;
            if (map == null) { c.Fatal("map is null"); return; }
            int n = map.TileCount;
            if (map.Elevation == null || map.Elevation.Length != n || map.Moisture == null || map.Moisture.Length != n ||
                map.Temperature == null || map.Temperature.Length != n || map.Fertility == null || map.Fertility.Length != n ||
                map.Biomes == null || map.Biomes.Length != n || map.Flags == null || map.Flags.Length != n ||
                map.Resource == null || map.Resource.Length != n || map.Owner == null || map.Owner.Length != n ||
                map.BuildingAt == null || map.BuildingAt.Length != n)
            { c.Fatal("map arrays do not match " + map.Width + "x" + map.Height); return; }
            if (s.Rng == null) c.Fatal("rng is null");
            if (s.Tick < 0) c.Fatal("tick is negative");

            var ids = s.Ids;
            if (ids.NextPersonId < 1 || ids.NextBuildingId < 1 || ids.NextSettlementId < 1 || ids.NextCivilizationId < 1 || ids.NextArmyId < 1 || ids.NextWarId < 1)
                c.Fatal("id allocator counters must be >= 1");

            CheckFloats(s, c);
            CheckIdRanges(s, c);
            if (c.Result.Fatal.Count > 0) return; // later passes assume sane ids and numbers

            CheckCivReferences(s, c);
            CheckBuildings(s, c);
            CheckMap(s, c);
            CheckSettlementFields(s, c);
            CheckResidency(s, c);
            CheckSettlementBuildings(s, c);
            CheckCivilizations(s, db, c);
            CheckHomesAndJobs(s, c);
            CheckArmies(s, c);
            CheckPeopleMisc(s, c);
            CheckChronicle(s, c);
        }

        // ------------------------------------------------------------------ fatal: numbers and ids
        private static void CheckFloats(WorldState s, Ctx c)
        {
            var m = s.Map;
            c.Finite(m.Elevation, "map", 0, "Elevation"); c.Finite(m.Moisture, "map", 0, "Moisture"); c.Finite(m.Temperature, "map", 0, "Temperature");
            c.Finite(m.Fertility, "map", 0, "Fertility"); c.Finite(m.Resource, "map", 0, "Resource"); c.Finite(m.SeaLevel, "map.SeaLevel");
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (Bad(p.AgeYears) || Bad(p.Health) || Bad(p.Happiness) || Bad(p.Satiation) || Bad(p.Faith) || Bad(p.Skill) || Bad(p.Experience))
                    c.Fatal("person " + p.Id + " has a non-finite float");
            }
            var bs = s.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                if (Bad(b.ConstructionProgress) || Bad(b.Condition) || Bad(b.Efficiency)) c.Fatal("building " + b.Id + " has a non-finite float");
            }
            var ss = s.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var st = ss[i];
                var sp = st.Stockpile;
                bool bad = Bad(sp.Capacity) || Bad(st.Happiness) || Bad(st.Health) || Bad(st.Unrest) || Bad(st.Faith) || Bad(st.DefenseStrength);
                for (int r = 0; r < Counts.Resources; r++) bad |= Bad(sp.Get((ResourceType)r));
                for (int q = 0; q < st.BuildQueue.Count; q++) bad |= st.BuildQueue[q] == null || Bad(st.BuildQueue[q].Priority);
                if (bad) c.Fatal("settlement " + st.Id + " has a non-finite float or null build order");
                c.Finite(sp.AvgIncome, "settlement", st.Id, "AvgIncome"); c.Finite(sp.AvgExpense, "settlement", st.Id, "AvgExpense");
                c.Finite(st.ServiceOutput, "settlement", st.Id, "ServiceOutput"); c.Finite(st.ServiceCoverage, "settlement", st.Id, "ServiceCoverage");
                c.Finite(st.JobPriority, "settlement", st.Id, "JobPriority");
                c.Memory(st.Memory, "settlement", st.Id);
            }
            var cs = s.Civilizations.Items;
            for (int i = 0; i < cs.Count; i++)
            {
                var civ = cs[i];
                var md = civ.Modifiers;
                c.Finite(md.Production, "civilization", civ.Id, "Production"); c.Finite(md.Service, "civilization", civ.Id, "Service");
                if (Bad(md.Research) || Bad(md.Growth) || Bad(md.BuildSpeed) || Bad(md.Faith) || Bad(md.Military) || Bad(md.Storage) || Bad(md.Trade) ||
                    Bad(md.MoveSpeed) || Bad(md.HealthBonus) || Bad(md.HappinessBonus) || Bad(md.LifespanBonusYears) ||
                    Bad(civ.ResearchPool) || Bad(civ.TotalResearch) || Bad(civ.Devotion) || Bad(civ.GodFavor) || Bad(civ.MilitaryPower))
                    c.Fatal("civilization " + civ.Id + " has a non-finite float");
                c.Memory(civ.Memory, "civilization", civ.Id);
            }
            foreach (var r in s.Diplomacy.AllRelations) if (Bad(r.Opinion)) c.Fatal("relation " + r.CivA + "-" + r.CivB + " opinion is not finite");
            foreach (var war in s.Diplomacy.Wars)
            {
                if (war == null) c.Fatal("null war");
                else if (Bad(war.WarScore)) c.Fatal("war " + war.Id + " score is not finite");
            }
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (Bad(a.MoveProgress) || Bad(a.Strength) || Bad(a.Morale)) c.Fatal("army " + a.Id + " has a non-finite float");
            }
            var g = s.God;
            if (Bad(g.Mana) || Bad(g.MaxMana) || Bad(g.ManaPerTick) || Bad(g.Alignment)) c.Fatal("god state has a non-finite float");
            var x = s.Weather;
            if (Bad(x.Rainfall) || Bad(x.TemperatureOffset) || Bad(x.FoodYieldMultiplier) || Bad(x.StormIntensity)) c.Fatal("weather has a non-finite float");
            c.Memory(s.Memory, "world", 0);
        }

        private static bool Bad(float v) => float.IsNaN(v) || float.IsInfinity(v);

        private static void CheckIdRanges(WorldState s, Ctx c)
        {
            var ids = s.Ids;
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++) if (people[i].Id < 1 || people[i].Id >= ids.NextPersonId) c.Fatal("person id " + people[i].Id + " outside allocator range");
            var bs = s.Buildings.Items;
            for (int i = 0; i < bs.Count; i++) if (bs[i].Id < 1 || bs[i].Id >= ids.NextBuildingId) c.Fatal("building id " + bs[i].Id + " outside allocator range");
            var ss = s.Settlements.Items;
            for (int i = 0; i < ss.Count; i++) if (ss[i].Id < 1 || ss[i].Id >= ids.NextSettlementId) c.Fatal("settlement id " + ss[i].Id + " outside allocator range");
            var cs = s.Civilizations.Items;
            for (int i = 0; i < cs.Count; i++) if (cs[i].Id < 1 || cs[i].Id >= ids.NextCivilizationId) c.Fatal("civilization id " + cs[i].Id + " outside allocator range");
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++) if (armies[i].Id < 1 || armies[i].Id >= ids.NextArmyId) c.Fatal("army id " + armies[i].Id + " outside allocator range");
            var warIds = new HashSet<int>();
            foreach (var w in s.Diplomacy.Wars)
            {
                if (w == null) continue;
                if (w.Id < 1 || w.Id >= ids.NextWarId) c.Fatal("war id " + w.Id + " outside allocator range");
                if (!warIds.Add(w.Id)) c.Fatal("duplicate war id " + w.Id);
            }
        }

        // ------------------------------------------------------------------ civ ownership of settlements / wars / armies / relations
        private static void CheckCivReferences(WorldState s, Ctx c)
        {
            var ss = s.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
                if (!s.Civilizations.Contains(ss[i].CivilizationId)) c.Fatal("settlement " + ss[i].Id + " belongs to unknown civilization " + ss[i].CivilizationId);
            foreach (var w in s.Diplomacy.Wars)
            {
                if (!s.Civilizations.Contains(w.AttackerId) || !s.Civilizations.Contains(w.DefenderId) || w.AttackerId == w.DefenderId)
                    c.Fatal("war " + w.Id + " has invalid belligerents " + w.AttackerId + "/" + w.DefenderId);
            }
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
                if (!s.Civilizations.Contains(armies[i].CivilizationId)) c.Fatal("army " + armies[i].Id + " belongs to unknown civilization " + armies[i].CivilizationId);

            var warIds = WarIds(s);
            foreach (var r in s.Diplomacy.AllRelations)
            {
                // Civilizations are never removed from the store, so a relation to an unknown civ is structural corruption.
                if (r.CivA >= r.CivB) { c.Fatal("relation " + r.CivA + "-" + r.CivB + " is not normalized"); continue; }
                if (!s.Civilizations.Contains(r.CivA) || !s.Civilizations.Contains(r.CivB)) { c.Fatal("relation " + r.CivA + "-" + r.CivB + " references an unknown civilization"); continue; }
                if (r.WarId != -1 && !warIds.Contains(r.WarId))
                {
                    c.Fix("relation " + r.CivA + "-" + r.CivB + " references missing war " + r.WarId);
                    if (c.Repair) r.WarId = -1;
                }
            }
        }

        private static HashSet<int> WarIds(WorldState s)
        {
            var set = new HashSet<int>();
            foreach (var w in s.Diplomacy.Wars) set.Add(w.Id);
            return set;
        }

        // ------------------------------------------------------------------ buildings: geometry and ownership
        private static bool FootprintOk(WorldMap m, Building b) =>
            b.Width >= 1 && b.Height >= 1 && b.Width <= MaxBuildingFootprint && b.Height <= MaxBuildingFootprint &&
            b.Origin.X >= 0 && b.Origin.Y >= 0 && (long)b.Origin.X + b.Width <= m.Width && (long)b.Origin.Y + b.Height <= m.Height;

        private static void CheckBuildings(WorldState s, Ctx c)
        {
            var bs = s.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                if (!FootprintOk(s.Map, b)) { c.Fatal("building " + b.Id + " footprint " + b.Origin + " " + b.Width + "x" + b.Height + " is invalid or out of bounds"); continue; }
                if (b.Rotation > 3) { c.Fix("building " + b.Id + " rotation " + b.Rotation + " normalized"); if (c.Repair) b.Rotation &= 3; }
                if (b.SettlementId != -1 && !s.Settlements.Contains(b.SettlementId))
                {
                    c.Fix("building " + b.Id + " references missing settlement " + b.SettlementId);
                    if (c.Repair) b.SettlementId = -1;
                }
                var st = s.Settlements.Get(b.SettlementId);
                int civ = st != null ? st.CivilizationId : (s.Civilizations.Contains(b.CivilizationId) ? b.CivilizationId : -1);
                if (b.CivilizationId != civ)
                {
                    c.Fix("building " + b.Id + " civilization " + b.CivilizationId + " corrected to " + civ);
                    if (c.Repair) b.CivilizationId = civ;
                }
            }
        }

        // ------------------------------------------------------------------ map occupancy / ownership
        private static void CheckMap(WorldState s, Ctx c)
        {
            var m = s.Map;
            int n = m.TileCount;
            // Footprint overlap among live buildings (independent of the map array). Ids are >= 1 here, so 0 = free.
            // Stops at the first overlap: the save is rejected anyway, and this bounds the work to ~one map's worth of tiles.
            var claimed = new int[n];
            var bs = s.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                if (b.IsDestroyed || !FootprintOk(m, b)) continue;
                for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                    for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    {
                        int t = m.Index(x, y);
                        if (claimed[t] != 0) { c.Fatal("buildings " + claimed[t] + " and " + b.Id + " overlap at " + new Vec2i(x, y)); return; }
                        claimed[t] = b.Id;
                    }
            }
            int cleared = 0, set = 0, owners = 0;
            for (int t = 0; t < n; t++)
            {
                int at = m.BuildingAt[t];
                int want = claimed[t] != 0 ? claimed[t] : -1;
                if (at != want)
                {
                    if (want == -1) cleared++; else set++;
                    if (c.Repair) m.BuildingAt[t] = want;
                }
                int o = m.Owner[t];
                if (o != -1 && !s.Settlements.Contains(o)) { owners++; if (c.Repair) m.Owner[t] = -1; }
            }
            if (cleared > 0) c.Fix("map: " + cleared + " stale BuildingAt tiles cleared");
            if (set > 0) c.Fix("map: " + set + " BuildingAt tiles restored from building footprints");
            if (owners > 0) c.Fix("map: " + owners + " tiles owned by missing settlements released");
        }

        // ------------------------------------------------------------------ settlements
        private static void CheckSettlementFields(WorldState s, Ctx c)
        {
            var m = s.Map;
            var ss = s.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var st = ss[i];
                if (!m.InBounds(st.Center)) { c.Fix("settlement " + st.Id + " center clamped"); if (c.Repair) st.Center = m.ClampToBounds(st.Center); }
                if (st.TownCenterId != -1)
                {
                    var tc = s.Buildings.Get(st.TownCenterId);
                    if (tc == null || tc.SettlementId != st.Id) { c.Fix("settlement " + st.Id + " town center " + st.TownCenterId + " cleared"); if (c.Repair) st.TownCenterId = -1; }
                }
                if (st.ParentSettlementId < -1 || st.ParentSettlementId >= s.Ids.NextSettlementId)
                { c.Fix("settlement " + st.Id + " parent id cleared"); if (c.Repair) st.ParentSettlementId = -1; }
                for (int q = 0; q < st.BuildQueue.Count; q++)
                {
                    var o = st.BuildQueue[q];
                    if (o.HasPreferredSite && !m.InBounds(o.PreferredSite)) { c.Fix("settlement " + st.Id + " build order site dropped"); if (c.Repair) o.HasPreferredSite = false; }
                }
            }
        }

        private static void CheckResidency(WorldState s, Ctx c)
        {
            var listedIn = new Dictionary<int, int>();
            var ss = s.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var st = ss[i];
                var list = st.ResidentIds;
                int keep = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    int pid = list[k];
                    var p = s.People.Get(pid);
                    bool ok;
                    if (p == null || !p.IsAlive) { c.Fix("settlement " + st.Id + " lists missing/dead resident " + pid); ok = false; }
                    else if (p.SettlementId != st.Id) { c.Fatal("settlement " + st.Id + " lists person " + pid + " who belongs to settlement " + p.SettlementId); ok = false; }
                    else if (listedIn.ContainsKey(pid)) { c.Fix("settlement " + st.Id + " lists resident " + pid + " twice"); ok = false; }
                    else { listedIn[pid] = st.Id; ok = true; }
                    if (ok || !c.Repair) list[keep++] = pid;
                }
                if (c.Repair && keep < list.Count) list.RemoveRange(keep, list.Count - keep);
            }
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!p.IsAlive) continue;
                if (p.SettlementId != -1)
                {
                    var st = s.Settlements.Get(p.SettlementId);
                    if (st == null) { c.Fatal("person " + p.Id + " references missing settlement " + p.SettlementId); continue; }
                    if (!listedIn.ContainsKey(p.Id)) { c.Fatal("person " + p.Id + " is not listed by settlement " + p.SettlementId); continue; }
                    if (p.CivilizationId != st.CivilizationId) { c.Fix("person " + p.Id + " civilization corrected"); if (c.Repair) p.CivilizationId = st.CivilizationId; }
                }
                else if (p.CivilizationId != -1 && !s.Civilizations.Contains(p.CivilizationId))
                { c.Fix("person " + p.Id + " references missing civilization"); if (c.Repair) p.CivilizationId = -1; }
            }
        }

        private static void CheckSettlementBuildings(WorldState s, Ctx c)
        {
            var kept = new HashSet<int>();
            var ss = s.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var st = ss[i];
                var list = st.BuildingIds;
                int keep = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    int bid = list[k];
                    var b = s.Buildings.Get(bid);
                    bool ok = b != null && !b.IsDestroyed && b.SettlementId == st.Id && kept.Add(bid);
                    if (!ok) c.Fix("settlement " + st.Id + " building list entry " + bid + " dropped");
                    if (ok || !c.Repair) list[keep++] = bid;
                }
                if (c.Repair && keep < list.Count) list.RemoveRange(keep, list.Count - keep);
            }
            var bs = s.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                if (b.IsDestroyed || b.SettlementId == -1 || kept.Contains(b.Id)) continue;
                var st = s.Settlements.Get(b.SettlementId);
                if (st == null) continue;
                c.Fix("building " + b.Id + " added to settlement " + st.Id + " building list");
                if (c.Repair) st.BuildingIds.Add(b.Id);
            }
        }

        // ------------------------------------------------------------------ civilizations
        private static void CheckCivilizations(WorldState s, GameDatabase db, Ctx c)
        {
            var kept = new HashSet<int>();
            var cs = s.Civilizations.Items;
            for (int i = 0; i < cs.Count; i++)
            {
                var civ = cs[i];
                var list = civ.SettlementIds;
                int keep = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    int sid = list[k];
                    var st = s.Settlements.Get(sid);
                    bool ok = st != null && st.CivilizationId == civ.Id && kept.Add(sid);
                    if (!ok) c.Fix("civilization " + civ.Id + " settlement list entry " + sid + " dropped");
                    if (ok || !c.Repair) list[keep++] = sid;
                }
                if (c.Repair && keep < list.Count) list.RemoveRange(keep, list.Count - keep);

                if (civ.CapitalId != -1)
                {
                    var cap = s.Settlements.Get(civ.CapitalId);
                    if (cap == null || cap.CivilizationId != civ.Id) { c.Fix("civilization " + civ.Id + " capital cleared"); if (c.Repair) civ.CapitalId = -1; }
                }
                if (civ.LeaderId != -1)
                {
                    var leader = s.People.Get(civ.LeaderId);
                    if (leader == null || !leader.IsAlive) { c.Fix("civilization " + civ.Id + " leader cleared"); if (c.Repair) civ.LeaderId = -1; }
                }
                if (civ.CurrentResearchTechId != -1 && (civ.CurrentResearchTechId < -1 || (db != null && db.Tech(civ.CurrentResearchTechId) == null)))
                { c.Fix("civilization " + civ.Id + " research target cleared"); if (c.Repair) civ.CurrentResearchTechId = -1; }
                if (db != null)
                {
                    List<int> unknown = null;
                    foreach (int t in civ.KnownTechs) if (db.Tech(t) == null) (unknown ?? (unknown = new List<int>())).Add(t);
                    if (unknown != null)
                    {
                        c.Fix("civilization " + civ.Id + " forgets " + unknown.Count + " unknown techs");
                        if (c.Repair) foreach (int t in unknown) civ.KnownTechs.Remove(t);
                    }
                }
            }
            var ss = s.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var st = ss[i];
                if (!st.IsAlive || kept.Contains(st.Id)) continue;
                var civ = s.Civilizations.Get(st.CivilizationId);
                if (civ == null) continue;
                c.Fix("settlement " + st.Id + " added to civilization " + civ.Id + " settlement list");
                if (c.Repair) civ.SettlementIds.Add(st.Id);
            }
        }

        // ------------------------------------------------------------------ homes, jobs, spouses
        private static void CheckHomesAndJobs(WorldState s, Ctx c)
        {
            var housed = new HashSet<int>();
            var employed = new HashSet<int>();
            var bs = s.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                CompactLinks(s, c, b, b.Residents, housed, true);
                CompactLinks(s, c, b, b.Workers, employed, false);
            }
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p.HomeId != -1 && !housed.Contains(p.Id)) { c.Fix("person " + p.Id + " home " + p.HomeId + " cleared"); if (c.Repair) p.HomeId = -1; }
                if (p.WorkplaceId != -1 && !employed.Contains(p.Id))
                {
                    c.Fix("person " + p.Id + " workplace " + p.WorkplaceId + " cleared");
                    if (c.Repair) { p.WorkplaceId = -1; p.Job = JobType.None; }
                }
                if (p.SpouseId != -1)
                {
                    var sp = s.People.Get(p.SpouseId);
                    if (!p.IsAlive || sp == null || !sp.IsAlive || sp.SpouseId != p.Id || sp.Id == p.Id)
                    { c.Fix("person " + p.Id + " spouse " + p.SpouseId + " cleared"); if (c.Repair) p.SpouseId = -1; }
                }
            }
        }

        private static void CompactLinks(WorldState s, Ctx c, Building b, List<int> list, HashSet<int> kept, bool homes)
        {
            int keep = 0;
            for (int k = 0; k < list.Count; k++)
            {
                int pid = list[k];
                var p = s.People.Get(pid);
                bool ok = !b.IsDestroyed && p != null && p.IsAlive && (homes ? p.HomeId : p.WorkplaceId) == b.Id && kept.Add(pid);
                if (!ok) c.Fix("building " + b.Id + (homes ? " resident " : " worker ") + pid + " dropped");
                if (ok || !c.Repair) list[keep++] = pid;
            }
            if (c.Repair && keep < list.Count) list.RemoveRange(keep, list.Count - keep);
        }

        // ------------------------------------------------------------------ armies
        private static void CheckArmies(WorldState s, Ctx c)
        {
            var m = s.Map;
            var warIds = WarIds(s);
            var soldiers = new HashSet<int>();
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                string w = "army " + a.Id; // armies are few (<= MaxListCount); a per-army label is acceptable
                if (a.HomeSettlementId != -1 && !s.Settlements.Contains(a.HomeSettlementId)) { c.Fix(w + " home settlement cleared"); if (c.Repair) a.HomeSettlementId = -1; }
                if (a.TargetSettlementId != -1 && !s.Settlements.Contains(a.TargetSettlementId)) { c.Fix(w + " target cleared"); if (c.Repair) a.TargetSettlementId = -1; }
                if (a.WarId != -1 && !warIds.Contains(a.WarId)) { c.Fix(w + " war cleared"); if (c.Repair) a.WarId = -1; }
                if (!m.InBounds(a.Position)) { c.Fix(w + " position clamped"); if (c.Repair) a.Position = m.ClampToBounds(a.Position); }
                if (!m.InBounds(a.Destination)) { c.Fix(w + " destination clamped"); if (c.Repair) a.Destination = m.ClampToBounds(a.Destination); }
                bool pathOk = true;
                for (int k = 0; k < a.Path.Count && pathOk; k++) pathOk = m.InBounds(a.Path[k]);
                if (!pathOk) { c.Fix(w + " path left the map; cleared"); if (c.Repair) { a.Path.Clear(); a.PathIndex = 0; } }
                int count = c.Repair && !pathOk ? 0 : a.Path.Count;
                if (a.PathIndex < 0 || a.PathIndex > count) { c.Fix(w + " path index clamped"); if (c.Repair) a.PathIndex = MathUtil.Clamp(a.PathIndex, 0, count); }

                var list = a.SoldierIds;
                int keep = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    int pid = list[k];
                    var p = s.People.Get(pid);
                    bool ok = p != null && p.IsAlive && p.ArmyId == a.Id && soldiers.Add(pid);
                    if (!ok) c.Fix(w + " soldier " + pid + " dropped");
                    if (ok || !c.Repair) list[keep++] = pid;
                }
                if (c.Repair && keep < list.Count) list.RemoveRange(keep, list.Count - keep);
            }
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p.ArmyId != -1 && !soldiers.Contains(p.Id)) { c.Fix("person " + p.Id + " army " + p.ArmyId + " cleared"); if (c.Repair) p.ArmyId = -1; }
            }
        }

        // ------------------------------------------------------------------ misc
        private static void CheckPeopleMisc(WorldState s, Ctx c)
        {
            var m = s.Map;
            int next = s.Ids.NextPersonId;
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!m.InBounds(p.Position)) { c.Fix("person " + p.Id + " position clamped"); if (c.Repair) p.Position = m.ClampToBounds(p.Position); }
                if (p.MotherId < -1 || p.MotherId >= next || p.MotherId == p.Id) { c.Fix("person " + p.Id + " mother id cleared"); if (c.Repair) p.MotherId = -1; }
                if (p.FatherId < -1 || p.FatherId >= next || p.FatherId == p.Id) { c.Fix("person " + p.Id + " father id cleared"); if (c.Repair) p.FatherId = -1; }
            }
        }

        private static void CheckChronicle(WorldState s, Ctx c)
        {
            var e = s.Chronicle.Entries;
            for (int i = 0; i < e.Count; i++)
            {
                if (e[i].Importance >= 0 && e[i].Importance <= 3) continue;
                c.Fix("chronicle entry " + i + " importance clamped");
                if (c.Repair) { var x = e[i]; x.Importance = MathUtil.Clamp(x.Importance, 0, 3); e[i] = x; }
            }
        }
    }
}
