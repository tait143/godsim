using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Tests.Scenarios
{
    /// <summary>Result of a whole-world consistency sweep: hard errors fail scenarios, warnings are reported.</summary>
    public sealed class InvariantReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public int ErrorCount;
        public int WarningCount;

        public bool Ok => ErrorCount == 0;

        internal void Error(string m) { ErrorCount++; if (Errors.Count < 100) Errors.Add(m); }
        internal void Warn(string m) { WarningCount++; if (Warnings.Count < 100) Warnings.Add(m); }

        public override string ToString() =>
            ErrorCount + " errors, " + WarningCount + " warnings\nerrors:\n" + ScenarioWorlds.Join(Errors) + "warnings:\n" + ScenarioWorlds.Join(Warnings, 10);
    }

    /// <summary>
    /// Whole-world invariants that every module must preserve (see WorldOps: "no dangling ids"). Intended to be
    /// called between ticks (after the engine flushed removals), when stores hold only living/standing entities.
    /// </summary>
    public static class ScenarioInvariants
    {
        // ------------------------------------------------------------------ references
        public static InvariantReport CheckReferences(WorldState s, GameDatabase db)
        {
            var r = new InvariantReport();
            var residentSets = new Dictionary<int, HashSet<int>>();
            var buildingSets = new Dictionary<int, HashSet<int>>();

            // settlements
            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i];
                string tag = "settlement #" + st.Id + " '" + st.Name + "'";
                if (!st.IsAlive) { r.Error(tag + " is dead but still in the store after flush"); continue; }
                var civ = s.Civilizations.Get(st.CivilizationId);
                if (civ == null) r.Error(tag + " references missing civ " + st.CivilizationId);
                else
                {
                    if (!civ.IsAlive) r.Error(tag + " belongs to collapsed civ " + civ.Id);
                    if (!civ.SettlementIds.Contains(st.Id)) r.Error(tag + " missing from civ " + civ.Id + " SettlementIds");
                }
                var rs = new HashSet<int>();
                foreach (int pid in st.ResidentIds)
                {
                    if (!rs.Add(pid)) { r.Error(tag + " lists resident " + pid + " twice"); continue; }
                    var p = s.People.Get(pid);
                    if (p == null) r.Error(tag + " lists missing resident " + pid);
                    else if (p.SettlementId != st.Id) r.Error(tag + " lists resident " + pid + " whose SettlementId=" + p.SettlementId);
                }
                residentSets[st.Id] = rs;
                var bs = new HashSet<int>();
                foreach (int bid in st.BuildingIds)
                {
                    if (!bs.Add(bid)) { r.Error(tag + " lists building " + bid + " twice"); continue; }
                    var b = s.Buildings.Get(bid);
                    if (b == null) r.Error(tag + " lists missing building " + bid);
                    else if (b.SettlementId != st.Id) r.Error(tag + " lists building " + bid + " whose SettlementId=" + b.SettlementId);
                }
                buildingSets[st.Id] = bs;
                if (st.TownCenterId >= 0)
                {
                    var tc = s.Buildings.Get(st.TownCenterId);
                    if (tc == null || tc.IsDestroyed) r.Error(tag + " TownCenterId " + st.TownCenterId + " is gone");
                    else if (tc.Type != BuildingType.TownCenter) r.Error(tag + " TownCenterId points at a " + tc.Type);
                    else if (!bs.Contains(tc.Id)) r.Error(tag + " TownCenter not in BuildingIds");
                }
            }

            // people
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                string tag = "person #" + p.Id;
                if (!p.IsAlive) { r.Error(tag + " is dead but still in the store after flush"); continue; }
                var st = s.Settlements.Get(p.SettlementId);
                if (st == null) r.Error(tag + " references missing settlement " + p.SettlementId);
                else
                {
                    if (!st.IsAlive) r.Error(tag + " lives in dead settlement " + st.Id);
                    if (residentSets.TryGetValue(st.Id, out var rs) && !rs.Contains(p.Id)) r.Error(tag + " not in ResidentIds of settlement " + st.Id);
                    if (p.CivilizationId != st.CivilizationId) r.Error(tag + " CivilizationId " + p.CivilizationId + " != settlement civ " + st.CivilizationId);
                }
                if (p.HomeId >= 0)
                {
                    var h = s.Buildings.Get(p.HomeId);
                    if (h == null || h.IsDestroyed) r.Error(tag + " HomeId " + p.HomeId + " is gone");
                    else
                    {
                        if (!h.Residents.Contains(p.Id)) r.Error(tag + " not in Residents of home " + h.Id);
                        if (h.SettlementId != p.SettlementId) r.Warn(tag + " lives in a building of another settlement (" + h.SettlementId + " vs " + p.SettlementId + ")");
                    }
                }
                if (p.WorkplaceId >= 0)
                {
                    var w = s.Buildings.Get(p.WorkplaceId);
                    if (w == null || w.IsDestroyed) r.Error(tag + " WorkplaceId " + p.WorkplaceId + " is gone");
                    else
                    {
                        if (!w.Workers.Contains(p.Id)) r.Error(tag + " not in Workers of " + w.Id);
                        if (w.SettlementId != p.SettlementId) r.Warn(tag + " works in another settlement's building " + w.Id);
                    }
                }
                if (p.ArmyId >= 0)
                {
                    var a = s.Diplomacy.Armies.Get(p.ArmyId);
                    if (a == null || !a.IsAlive) r.Error(tag + " ArmyId " + p.ArmyId + " is gone");
                    else if (!a.SoldierIds.Contains(p.Id)) r.Error(tag + " not in SoldierIds of army " + a.Id);
                }
                if (p.SpouseId >= 0)
                {
                    var sp = s.People.Get(p.SpouseId);
                    if (sp == null || !sp.IsAlive) r.Error(tag + " SpouseId " + p.SpouseId + " is gone");
                    else if (sp.SpouseId != p.Id) r.Error(tag + " spouse " + sp.Id + " is married to " + sp.SpouseId);
                }
            }

            // buildings
            var map = s.Map;
            var buildings = s.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                string tag = b.Type + " #" + b.Id;
                if (b.IsDestroyed) { r.Error(tag + " is destroyed but still in the store after flush"); continue; }
                var st = s.Settlements.Get(b.SettlementId);
                if (st == null) r.Error(tag + " references missing settlement " + b.SettlementId);
                else
                {
                    if (buildingSets.TryGetValue(st.Id, out var bs) && !bs.Contains(b.Id)) r.Error(tag + " not in BuildingIds of settlement " + st.Id);
                    if (b.CivilizationId != st.CivilizationId) r.Error(tag + " CivilizationId " + b.CivilizationId + " != settlement civ " + st.CivilizationId);
                }
                var seen = new HashSet<int>();
                foreach (int pid in b.Residents)
                {
                    if (!seen.Add(pid)) { r.Error(tag + " lists resident " + pid + " twice"); continue; }
                    var p = s.People.Get(pid);
                    if (p == null) r.Error(tag + " lists missing resident " + pid);
                    else if (p.HomeId != b.Id) r.Error(tag + " lists resident " + pid + " whose HomeId=" + p.HomeId);
                }
                if (b.Residents.Count > b.HousingCapacity) r.Error(tag + " over capacity " + b.Residents.Count + "/" + b.HousingCapacity);
                if (!b.IsComplete && b.Residents.Count > 0) r.Error(tag + " under construction but inhabited");
                seen.Clear();
                foreach (int pid in b.Workers)
                {
                    if (!seen.Add(pid)) { r.Error(tag + " lists worker " + pid + " twice"); continue; }
                    var p = s.People.Get(pid);
                    if (p == null) r.Error(tag + " lists missing worker " + pid);
                    else if (p.WorkplaceId != b.Id) r.Error(tag + " lists worker " + pid + " whose WorkplaceId=" + p.WorkplaceId);
                }
                if (db != null && db.TryGetBuilding(b.Type, out var def) && b.Workers.Count > Math.Max(def.JobSlots, 0))
                    r.Error(tag + " has " + b.Workers.Count + " workers > JobSlots " + def.JobSlots);
                for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                    for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    {
                        if (!map.InBounds(x, y)) { r.Error(tag + " footprint out of bounds at " + x + "," + y); continue; }
                        int at = map.BuildingAt[map.Index(x, y)];
                        if (at != b.Id) r.Error(tag + " footprint tile " + x + "," + y + " has BuildingAt=" + at);
                    }
            }

            // civilizations
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (!c.IsAlive) continue;
                string tag = "civ #" + c.Id + " '" + c.Name + "'";
                var seen = new HashSet<int>();
                foreach (int sid in c.SettlementIds)
                {
                    if (!seen.Add(sid)) { r.Error(tag + " lists settlement " + sid + " twice"); continue; }
                    var st = s.Settlements.Get(sid);
                    if (st == null || !st.IsAlive) r.Error(tag + " lists missing/dead settlement " + sid);
                    else if (st.CivilizationId != c.Id) r.Error(tag + " lists settlement " + sid + " owned by " + st.CivilizationId);
                }
                if (c.SettlementIds.Count == 0) r.Warn(tag + " is alive with no settlements");
                else if (!c.SettlementIds.Contains(c.CapitalId)) r.Error(tag + " CapitalId " + c.CapitalId + " not among its settlements");
                if (c.LeaderId >= 0)
                {
                    var l = s.People.Get(c.LeaderId);
                    if (l == null || !l.IsAlive) r.Error(tag + " LeaderId " + c.LeaderId + " is gone");
                    else if (l.CivilizationId != c.Id) r.Warn(tag + " leader " + l.Id + " now belongs to civ " + l.CivilizationId);
                }
            }

            // armies
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                string tag = "army #" + a.Id;
                if (!a.IsAlive) { r.Error(tag + " is disbanded but still in the store after flush"); continue; }
                var c = s.Civilizations.Get(a.CivilizationId);
                if (c == null || !c.IsAlive) r.Error(tag + " belongs to missing/collapsed civ " + a.CivilizationId);
                var seen = new HashSet<int>();
                foreach (int pid in a.SoldierIds)
                {
                    if (!seen.Add(pid)) { r.Error(tag + " lists soldier " + pid + " twice"); continue; }
                    var p = s.People.Get(pid);
                    if (p == null) r.Error(tag + " lists missing soldier " + pid);
                    else if (p.ArmyId != a.Id) r.Error(tag + " lists soldier " + pid + " whose ArmyId=" + p.ArmyId);
                }
                if (a.HomeSettlementId >= 0 && s.Settlements.Get(a.HomeSettlementId) == null) r.Warn(tag + " home settlement " + a.HomeSettlementId + " is gone");
            }

            // wars and relations
            var warIds = new HashSet<int>();
            foreach (var w in s.Diplomacy.Wars)
            {
                if (!warIds.Add(w.Id)) r.Error("war id " + w.Id + " duplicated");
                if (!w.IsActive) { if (w.EndTick >= 0 && w.EndTick < w.StartTick) r.Error("war " + w.Id + " ended before it started"); continue; }
                var a = s.Civilizations.Get(w.AttackerId);
                var d = s.Civilizations.Get(w.DefenderId);
                if (a == null || !a.IsAlive || d == null || !d.IsAlive) r.Error("active war " + w.Id + " involves a missing/collapsed civ");
                if (w.AttackerId == w.DefenderId) r.Error("war " + w.Id + " is a civ against itself");
                else if (!s.Diplomacy.TryGet(w.AttackerId, w.DefenderId, out var rel) || rel.WarId != w.Id)
                    r.Error("active war " + w.Id + " not referenced by the relation of its belligerents");
            }
            foreach (var rel in s.Diplomacy.AllRelations)
            {
                if (rel.WarId < 0) continue;
                var w = s.Diplomacy.GetWar(rel.WarId);
                if (w == null) r.Error("relation " + rel.CivA + "-" + rel.CivB + " references missing war " + rel.WarId);
                else if (!w.IsActive) r.Error("relation " + rel.CivA + "-" + rel.CivB + " still references ended war " + rel.WarId);
            }

            // map
            for (int i = 0; i < map.TileCount; i++)
            {
                int bid = map.BuildingAt[i];
                if (bid >= 0)
                {
                    var b = s.Buildings.Get(bid);
                    if (b == null) r.Error("tile " + map.PosOf(i) + " references missing building " + bid);
                    else if (!b.Occupies(map.PosOf(i))) r.Error("tile " + map.PosOf(i) + " references building " + bid + " outside its footprint");
                }
                int owner = map.Owner[i];
                if (owner >= 0 && s.Settlements.Get(owner) == null) r.Warn("tile " + map.PosOf(i) + " owned by missing settlement " + owner);
            }
            return r;
        }

        // ------------------------------------------------------------------ NaN / Infinity
        public static List<string> CheckFinite(WorldState s)
        {
            var bad = new List<string>();
            void F(float v, string what) { if ((float.IsNaN(v) || float.IsInfinity(v)) && bad.Count < 100) bad.Add(what + " = " + v); }
            void A(float[] arr, string what) { if (arr == null) return; for (int i = 0; i < arr.Length; i++) F(arr[i], what + "[" + i + "]"); }
            void M(Dictionary<string, float> mem, string what) { if (mem == null) return; foreach (var kv in mem) F(kv.Value, what + ".Memory['" + kv.Key + "']"); }

            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i]; string t = "person#" + p.Id;
                F(p.AgeYears, t + ".AgeYears"); F(p.Health, t + ".Health"); F(p.Happiness, t + ".Happiness");
                F(p.Satiation, t + ".Satiation"); F(p.Faith, t + ".Faith"); F(p.Skill, t + ".Skill"); F(p.Experience, t + ".Experience");
            }
            var buildings = s.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i]; string t = "building#" + b.Id;
                F(b.ConstructionProgress, t + ".ConstructionProgress"); F(b.Condition, t + ".Condition"); F(b.Efficiency, t + ".Efficiency");
            }
            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i]; string t = "settlement#" + st.Id;
                F(st.Happiness, t + ".Happiness"); F(st.Health, t + ".Health"); F(st.Unrest, t + ".Unrest"); F(st.Faith, t + ".Faith");
                F(st.DefenseStrength, t + ".DefenseStrength");
                A(st.ServiceOutput, t + ".ServiceOutput"); A(st.ServiceCoverage, t + ".ServiceCoverage"); A(st.JobPriority, t + ".JobPriority");
                F(st.Stockpile.Capacity, t + ".Stockpile.Capacity");
                for (int r = 0; r < Counts.Resources; r++)
                {
                    float v = st.Stockpile.Get((ResourceType)r);
                    F(v, t + ".Stockpile[" + (ResourceType)r + "]");
                    if (v < -1e-3f && bad.Count < 100) bad.Add(t + ".Stockpile[" + (ResourceType)r + "] negative " + v);
                }
                A(st.Stockpile.AvgIncome, t + ".AvgIncome"); A(st.Stockpile.AvgExpense, t + ".AvgExpense");
                M(st.Memory, t);
            }
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i]; string t = "civ#" + c.Id;
                F(c.ResearchPool, t + ".ResearchPool"); F(c.TotalResearch, t + ".TotalResearch"); F(c.Devotion, t + ".Devotion");
                F(c.GodFavor, t + ".GodFavor"); F(c.MilitaryPower, t + ".MilitaryPower");
                var m = c.Modifiers;
                A(m.Production, t + ".Modifiers.Production"); A(m.Service, t + ".Modifiers.Service");
                F(m.Research, t + ".Modifiers.Research"); F(m.Growth, t + ".Modifiers.Growth"); F(m.BuildSpeed, t + ".Modifiers.BuildSpeed");
                F(m.Faith, t + ".Modifiers.Faith"); F(m.Military, t + ".Modifiers.Military"); F(m.Storage, t + ".Modifiers.Storage");
                F(m.Trade, t + ".Modifiers.Trade"); F(m.MoveSpeed, t + ".Modifiers.MoveSpeed"); F(m.HealthBonus, t + ".Modifiers.HealthBonus");
                F(m.HappinessBonus, t + ".Modifiers.HappinessBonus"); F(m.LifespanBonusYears, t + ".Modifiers.LifespanBonusYears");
                M(c.Memory, t);
            }
            var armies = s.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i]; string t = "army#" + a.Id;
                F(a.Strength, t + ".Strength"); F(a.Morale, t + ".Morale"); F(a.MoveProgress, t + ".MoveProgress");
            }
            foreach (var w in s.Diplomacy.Wars) F(w.WarScore, "war#" + w.Id + ".WarScore");
            foreach (var rel in s.Diplomacy.AllRelations) F(rel.Opinion, "relation " + rel.CivA + "-" + rel.CivB + ".Opinion");
            F(s.God.Mana, "God.Mana"); F(s.God.MaxMana, "God.MaxMana"); F(s.God.ManaPerTick, "God.ManaPerTick"); F(s.God.Alignment, "God.Alignment");
            if (s.God.Mana < -1e-3f) bad.Add("God.Mana negative " + s.God.Mana);
            F(s.Weather.Rainfall, "Weather.Rainfall"); F(s.Weather.TemperatureOffset, "Weather.TemperatureOffset");
            F(s.Weather.FoodYieldMultiplier, "Weather.FoodYieldMultiplier"); F(s.Weather.StormIntensity, "Weather.StormIntensity");
            M(s.Memory, "World");
            var map = s.Map;
            A(map.Elevation, "Map.Elevation"); A(map.Moisture, "Map.Moisture"); A(map.Temperature, "Map.Temperature");
            A(map.Fertility, "Map.Fertility"); A(map.Resource, "Map.Resource");
            return bad;
        }

        // ------------------------------------------------------------------ configured caps
        public static List<string> CheckCaps(WorldState s, SimConfig cfg)
        {
            var bad = new List<string>();
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (civs[i].IsAlive && civs[i].SettlementIds.Count > cfg.MaxSettlementsPerCiv)
                    bad.Add("civ " + civs[i].Id + " has " + civs[i].SettlementIds.Count + " settlements > MaxSettlementsPerCiv " + cfg.MaxSettlementsPerCiv);
            var st = s.Settlements.Items;
            for (int i = 0; i < st.Count; i++)
                if (st[i].IsAlive && st[i].BuildingIds.Count > cfg.MaxBuildingsPerSettlement)
                    bad.Add("settlement " + st[i].Id + " has " + st[i].BuildingIds.Count + " buildings > MaxBuildingsPerSettlement " + cfg.MaxBuildingsPerSettlement);
            int armies = 0;
            var a = s.Diplomacy.Armies.Items;
            for (int i = 0; i < a.Count; i++) if (a[i].IsAlive) armies++;
            if (armies > cfg.MaxArmies) bad.Add(armies + " armies > MaxArmies " + cfg.MaxArmies);
            return bad;
        }

        // ------------------------------------------------------------------ Security.IntegrityChecker (optional)
        /// <summary>
        /// Runs GodSim.Core.Security.IntegrityChecker.Check if that type exists (found by reflection so this file
        /// compiles whether or not the security module ships it). Returns false when absent; issues receives problems.
        /// </summary>
        public static bool TryRunIntegrityChecker(GodSimulation sim, List<string> issues, out string note)
        {
            Type t = FindType("GodSim.Core.Security.IntegrityChecker", typeof(GodSimulation).Assembly, typeof(WorldState).Assembly);
            if (t == null) { note = "GodSim.Core.Security.IntegrityChecker not present; own checks only"; return false; }
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "Check" || m.ContainsGenericParameters) continue;
                var ps = m.GetParameters();
                var args = new object[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    var pt = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType;
                    if (pt == typeof(WorldState)) args[i] = sim.State;
                    else if (pt == typeof(GameDatabase)) args[i] = sim.Db;
                    else if (pt == typeof(SimConfig)) args[i] = sim.Config;
                    else if (ps[i].IsOut) args[i] = null;
                    else if (pt == typeof(List<string>) || pt == typeof(IList<string>) || pt == typeof(ICollection<string>)) args[i] = new List<string>();
                    else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                    else ok = false;
                }
                if (!ok) continue;
                object result;
                try { result = m.Invoke(null, args); }
                catch (TargetInvocationException ex) { issues.Add("IntegrityChecker.Check threw " + ex.InnerException); note = "IntegrityChecker threw"; return true; }
                Interpret(result, issues, "result");
                for (int i = 0; i < ps.Length; i++)
                    if (ps[i].IsOut || (args[i] is List<string>)) Interpret(args[i], issues, "argument '" + ps[i].Name + "'");
                note = "IntegrityChecker." + m.Name + "(" + ps.Length + " args) ran";
                return true;
            }
            note = "IntegrityChecker found but no usable static Check overload";
            return false;
        }

        private static Type FindType(string fullName, params Assembly[] preferred)
        {
            foreach (var a in preferred) { var t = a.GetType(fullName, false); if (t != null) return t; }
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType(fullName, false); } catch (Exception) { }
                if (t != null) return t;
            }
            return null;
        }

        private static void Interpret(object value, List<string> issues, string what)
        {
            if (value == null) return;
            if (value is bool b) { if (!b) issues.Add("IntegrityChecker " + what + " = false"); return; }
            if (value is int n) { if (n > 0) issues.Add("IntegrityChecker " + what + " reported " + n + " problems"); return; }
            if (value is string s) { if (s.Length > 0) issues.Add(s); return; }
            if (value is IEnumerable e)
            {
                foreach (var x in e) if (x != null) issues.Add(x.ToString());
                return;
            }
            var type = value.GetType();
            foreach (var name in new[] { "Errors", "Issues", "Problems", "Violations" })
            {
                var pr = type.GetProperty(name);
                object v = pr != null ? pr.GetValue(value) : type.GetField(name)?.GetValue(value);
                if (v is IEnumerable list && !(v is string)) { foreach (var x in list) if (x != null) issues.Add(x.ToString()); return; }
            }
            foreach (var name in new[] { "IsValid", "Ok", "IsOk", "Success" })
            {
                var pr = type.GetProperty(name);
                object v = pr != null ? pr.GetValue(value) : type.GetField(name)?.GetValue(value);
                if (v is bool ok) { if (!ok) issues.Add("IntegrityChecker " + what + "." + name + " = false"); return; }
            }
        }
    }
}
