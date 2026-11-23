using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Contracts
{
    /// <summary>Shared fixtures and a global referential-integrity checker for WorldState (test-only).</summary>
    public static class WorldInvariants
    {
        /// <summary>Stub DB plus a Farm (Farmer x3, 2x2) and Barracks (Soldier x4, 2x1) so jobs can be exercised.</summary>
        public static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Width = 2, Height = 2, Job = JobType.Farmer, JobSlots = 3 });
            db.Register(new BuildingDefinition { Type = BuildingType.Barracks, Name = "Barracks", Width = 2, Height = 1, Job = JobType.Soldier, JobSlots = 4 });
            db.Register(new BuildingDefinition { Type = BuildingType.Colossus, Name = "Colossus", IsWonder = true, Width = 1, Height = 1 });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        /// <summary>Invariants that the WorldOps contract promises after every FlushRemovals.</summary>
        public static List<string> CheckCore(WorldState s, GameDatabase db)
        {
            var v = new List<string>();
            var map = s.Map;

            // ---------------------------------------------------------------- people
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!p.IsAlive) { v.Add("dead person " + p.Id + " still in store after flush"); continue; }
                var st = s.Settlements.Get(p.SettlementId);
                if (st == null) v.Add("person " + p.Id + " settlement " + p.SettlementId + " missing");
                else
                {
                    if (!st.IsAlive) v.Add("person " + p.Id + " in dead settlement " + st.Id);
                    if (!st.ResidentIds.Contains(p.Id)) v.Add("settlement " + st.Id + " does not list resident " + p.Id);
                    if (p.CivilizationId != st.CivilizationId) v.Add("person " + p.Id + " civ " + p.CivilizationId + " != settlement civ " + st.CivilizationId);
                }
                if (p.HomeId >= 0)
                {
                    var h = s.Buildings.Get(p.HomeId);
                    if (h == null || h.IsDestroyed) v.Add("person " + p.Id + " home " + p.HomeId + " missing/destroyed");
                    else if (!h.Residents.Contains(p.Id)) v.Add("home " + h.Id + " does not list person " + p.Id);
                }
                if (p.WorkplaceId >= 0)
                {
                    var w = s.Buildings.Get(p.WorkplaceId);
                    if (w == null || w.IsDestroyed) v.Add("person " + p.Id + " workplace " + p.WorkplaceId + " missing/destroyed");
                    else if (!w.Workers.Contains(p.Id)) v.Add("workplace " + w.Id + " does not list worker " + p.Id);
                }
                if (p.SpouseId >= 0)
                {
                    var sp = s.People.Get(p.SpouseId);
                    if (sp == null || !sp.IsAlive) v.Add("person " + p.Id + " spouse " + p.SpouseId + " missing/dead");
                    else if (sp.SpouseId != p.Id) v.Add("spouse link not symmetric " + p.Id + " -> " + sp.Id + " -> " + sp.SpouseId);
                }
                if (p.ArmyId >= 0)
                {
                    var a = s.Diplomacy.Armies.Get(p.ArmyId);
                    if (a == null || !a.IsAlive) v.Add("person " + p.Id + " army " + p.ArmyId + " missing/disbanded");
                    else if (!a.SoldierIds.Contains(p.Id)) v.Add("army " + a.Id + " does not list soldier " + p.Id);
                }
            }

            // ---------------------------------------------------------------- buildings
            var buildings = s.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b.IsDestroyed) { v.Add("destroyed building " + b.Id + " still in store after flush"); continue; }
                var st = s.Settlements.Get(b.SettlementId);
                if (st == null || !st.IsAlive) v.Add("building " + b.Id + " settlement " + b.SettlementId + " missing/dead");
                else
                {
                    if (!st.BuildingIds.Contains(b.Id)) v.Add("settlement " + st.Id + " does not list building " + b.Id);
                    if (b.CivilizationId != st.CivilizationId) v.Add("building " + b.Id + " civ " + b.CivilizationId + " != settlement civ " + st.CivilizationId);
                }
                if (b.Residents.Count > b.HousingCapacity) v.Add("building " + b.Id + " over capacity " + b.Residents.Count + "/" + b.HousingCapacity);
                if (db.TryGetBuilding(b.Type, out var def) && b.Workers.Count > def.JobSlots) v.Add("building " + b.Id + " over job slots " + b.Workers.Count + "/" + def.JobSlots);
                CheckIdList(v, "building " + b.Id + " Residents", b.Residents);
                CheckIdList(v, "building " + b.Id + " Workers", b.Workers);
                foreach (int pid in b.Residents)
                {
                    var p = s.People.Get(pid);
                    if (p == null || !p.IsAlive) v.Add("building " + b.Id + " resident " + pid + " missing/dead");
                    else if (p.HomeId != b.Id) v.Add("resident " + pid + " of " + b.Id + " has HomeId " + p.HomeId);
                }
                foreach (int pid in b.Workers)
                {
                    var p = s.People.Get(pid);
                    if (p == null || !p.IsAlive) v.Add("building " + b.Id + " worker " + pid + " missing/dead");
                    else if (p.WorkplaceId != b.Id) v.Add("worker " + pid + " of " + b.Id + " has WorkplaceId " + p.WorkplaceId);
                }
                for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                    for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    {
                        if (!map.InBounds(x, y)) { v.Add("building " + b.Id + " footprint out of bounds"); continue; }
                        if (map.BuildingAt[map.Index(x, y)] != b.Id) v.Add("tile (" + x + "," + y + ") BuildingAt " + map.BuildingAt[map.Index(x, y)] + " != " + b.Id);
                    }
            }
            for (int i = 0; i < map.TileCount; i++)
            {
                int bid = map.BuildingAt[i];
                if (bid < 0) continue;
                var b = s.Buildings.Get(bid);
                if (b == null || b.IsDestroyed) v.Add("tile " + map.PosOf(i) + " references missing building " + bid);
                else if (!b.Occupies(map.PosOf(i))) v.Add("tile " + map.PosOf(i) + " outside footprint of " + bid);
            }

            // ---------------------------------------------------------------- settlements
            var setts = s.Settlements.Items;
            for (int i = 0; i < setts.Count; i++)
            {
                var st = setts[i];
                if (!st.IsAlive) { v.Add("dead settlement " + st.Id + " still in store after flush"); continue; }
                CheckIdList(v, "settlement " + st.Id + " ResidentIds", st.ResidentIds);
                CheckIdList(v, "settlement " + st.Id + " BuildingIds", st.BuildingIds);
                foreach (int pid in st.ResidentIds)
                {
                    var p = s.People.Get(pid);
                    if (p == null || !p.IsAlive) v.Add("settlement " + st.Id + " lists missing/dead resident " + pid);
                    else if (p.SettlementId != st.Id) v.Add("settlement " + st.Id + " lists person " + pid + " of settlement " + p.SettlementId);
                }
                foreach (int bid in st.BuildingIds)
                {
                    var b = s.Buildings.Get(bid);
                    if (b == null || b.IsDestroyed) v.Add("settlement " + st.Id + " lists missing/destroyed building " + bid);
                    else if (b.SettlementId != st.Id) v.Add("settlement " + st.Id + " lists building " + bid + " of settlement " + b.SettlementId);
                }
                if (st.TownCenterId >= 0)
                {
                    var tc = s.Buildings.Get(st.TownCenterId);
                    if (tc == null || tc.IsDestroyed || tc.Type != BuildingType.TownCenter) v.Add("settlement " + st.Id + " TownCenterId stale " + st.TownCenterId);
                }
                var civ = s.Civilizations.Get(st.CivilizationId);
                if (civ == null) v.Add("settlement " + st.Id + " civ " + st.CivilizationId + " missing");
                else
                {
                    int n = 0;
                    foreach (int sid in civ.SettlementIds) if (sid == st.Id) n++;
                    if (n != 1) v.Add("civ " + civ.Id + " lists settlement " + st.Id + " " + n + " times");
                }
            }

            // ---------------------------------------------------------------- civilizations
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                CheckIdList(v, "civ " + c.Id + " SettlementIds", c.SettlementIds);
                foreach (int sid in c.SettlementIds)
                {
                    var st = s.Settlements.Get(sid);
                    if (st == null || !st.IsAlive) v.Add("civ " + c.Id + " lists missing/dead settlement " + sid);
                    else if (st.CivilizationId != c.Id) v.Add("civ " + c.Id + " lists settlement " + sid + " owned by " + st.CivilizationId);
                }
                if (!c.IsAlive && c.SettlementIds.Count > 0) v.Add("collapsed civ " + c.Id + " still has settlements");
            }

            // ---------------------------------------------------------------- armies
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (!a.IsAlive) { v.Add("disbanded army " + a.Id + " still in store after flush"); continue; }
                var civ = s.Civilizations.Get(a.CivilizationId);
                if (civ == null || !civ.IsAlive) v.Add("army " + a.Id + " belongs to missing/collapsed civ " + a.CivilizationId);
                CheckIdList(v, "army " + a.Id + " SoldierIds", a.SoldierIds);
                foreach (int pid in a.SoldierIds)
                {
                    var p = s.People.Get(pid);
                    if (p == null || !p.IsAlive) v.Add("army " + a.Id + " lists missing/dead soldier " + pid);
                    else if (p.ArmyId != a.Id) v.Add("army " + a.Id + " soldier " + pid + " has ArmyId " + p.ArmyId);
                }
            }
            return v;
        }

        /// <summary>Stronger invariants a careful reader of the contract would expect; currently violated (see ignored tests).</summary>
        public static List<string> CheckExtended(WorldState s)
        {
            var v = new List<string>();
            var map = s.Map;
            for (int i = 0; i < map.TileCount; i++)
            {
                int o = map.Owner[i];
                if (o < 0) continue;
                var st = s.Settlements.Get(o);
                if (st == null || !st.IsAlive) { v.Add("[owner] tile " + map.PosOf(i) + " owned by missing settlement " + o); break; }
            }
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c.LeaderId >= 0)
                {
                    var l = s.People.Get(c.LeaderId);
                    if (l == null || !l.IsAlive) v.Add("[leader] civ " + c.Id + " leader " + c.LeaderId + " missing/dead");
                    else if (l.CivilizationId != c.Id) v.Add("[leader] civ " + c.Id + " leader " + l.Id + " belongs to civ " + l.CivilizationId);
                }
                if (c.IsAlive && c.SettlementIds.Count > 0)
                {
                    if (!c.SettlementIds.Contains(c.CapitalId)) v.Add("[capital] civ " + c.Id + " CapitalId " + c.CapitalId + " not among its settlements");
                    int caps = 0;
                    foreach (int sid in c.SettlementIds) { var st = s.Settlements.Get(sid); if (st != null && st.IsCapital) caps++; }
                    if (caps != 1) v.Add("[capital] civ " + c.Id + " has " + caps + " IsCapital settlements");
                }
            }
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                foreach (int pid in a.SoldierIds)
                {
                    var p = s.People.Get(pid);
                    if (p != null && p.CivilizationId != a.CivilizationId) v.Add("[army-civ] army " + a.Id + " (civ " + a.CivilizationId + ") has soldier " + pid + " of civ " + p.CivilizationId);
                }
                var home = s.Settlements.Get(a.HomeSettlementId);
                if (home == null || !home.IsAlive || home.CivilizationId != a.CivilizationId) v.Add("[army-home] army " + a.Id + " home settlement " + a.HomeSettlementId + " missing or foreign");
            }
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p.HomeId < 0) continue;
                var h = s.Buildings.Get(p.HomeId);
                if (h != null && h.SettlementId != p.SettlementId) v.Add("[home-settlement] person " + p.Id + " lives in building " + h.Id + " of another settlement");
            }
            return v;
        }

        private static void CheckIdList(List<string> v, string what, List<int> ids)
        {
            var seen = new HashSet<int>();
            foreach (int id in ids) if (!seen.Add(id)) { v.Add(what + " has duplicate id " + id); return; }
        }
    }
}
