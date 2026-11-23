using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Tests.Integration
{
    /// <summary>Shared helpers for full-simulation integration tests (they drive the real composition root).</summary>
    internal static class IntegrationTestUtil
    {
        public const ulong DefaultSeed = 20261119UL;
        public const int SmallMap = 128;

        /// <summary>A fresh config for a small, fast world. Each sim gets its own instance (CreateNew may sanitize in place).</summary>
        public static SimConfig SmallConfig(ulong seed = DefaultSeed, int size = SmallMap)
        {
            return new SimConfig { Seed = seed, MapWidth = size, MapHeight = size };
        }

        public static int Ticks(int years) => years * SimTime.TicksPerYear;

        public static GodSimulation CreateAndRun(SimConfig config, int years)
        {
            var sim = GodSimulation.CreateNew(config);
            if (years > 0) sim.StepMany(Ticks(years));
            return sim;
        }

        /// <summary>Living people per civilization id (ground truth from the person store).</summary>
        public static Dictionary<int, int> LivingByCiv(WorldState s)
        {
            var map = new Dictionary<int, int>();
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!p.IsAlive) continue;
                map.TryGetValue(p.CivilizationId, out int n);
                map[p.CivilizationId] = n + 1;
            }
            return map;
        }

        public static int CivsWithPopulation(WorldState s)
        {
            var by = LivingByCiv(s);
            int n = 0;
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (by.TryGetValue(civs[i].Id, out int pop) && pop > 0) n++;
            return n;
        }

        public static int CompleteBuildings(WorldState s)
        {
            int n = 0;
            var items = s.Buildings.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsComplete && !items[i].IsDestroyed) n++;
            return n;
        }

        public static int CompleteHouseCapacity(WorldState s)
        {
            int n = 0;
            var items = s.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b.Type == BuildingType.House && b.IsComplete && !b.IsDestroyed) n += b.HousingCapacity;
            }
            return n;
        }

        public static bool AnyCivAtLeast(WorldState s, AgeId age)
        {
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i].Age >= age) return true;
            return false;
        }

        public static Settlement FirstCapital(WorldState s)
        {
            Settlement best = null;
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var st = items[i];
                if (!st.IsAlive) continue;
                if (best == null || (st.IsCapital && !best.IsCapital) || (st.IsCapital == best.IsCapital && st.Id < best.Id)) best = st;
            }
            return best;
        }

        /// <summary>"" when no system faulted; otherwise one line per faulted/disabled system.</summary>
        public static string FaultReport(GodSimulation sim)
        {
            var sb = new StringBuilder();
            var profiles = sim.Engine.Profiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p.TotalFaults > 0 || p.Disabled)
                    sb.Append(p.Name).Append(": faults=").Append(p.TotalFaults).Append(p.Disabled ? " DISABLED" : "")
                      .Append(" last=").Append(p.LastError).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Lists NaN/Infinity values found in the main persistent floats ("" when clean).</summary>
        public static string NonFiniteReport(WorldState s)
        {
            var sb = new StringBuilder();
            Check(sb, "god.mana", s.God.Mana);
            Check(sb, "god.alignment", s.God.Alignment);
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                Check(sb, "civ" + c.Id + ".totalResearch", c.TotalResearch);
                Check(sb, "civ" + c.Id + ".researchPool", c.ResearchPool);
                Check(sb, "civ" + c.Id + ".devotion", c.Devotion);
                Check(sb, "civ" + c.Id + ".godFavor", c.GodFavor);
                Check(sb, "civ" + c.Id + ".militaryPower", c.MilitaryPower);
            }
            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i];
                Check(sb, "settlement" + st.Id + ".happiness", st.Happiness);
                Check(sb, "settlement" + st.Id + ".health", st.Health);
                Check(sb, "settlement" + st.Id + ".faith", st.Faith);
                Check(sb, "settlement" + st.Id + ".unrest", st.Unrest);
                foreach (var r in EnumUtil.AllResources) Check(sb, "settlement" + st.Id + ".stock." + r, st.Stockpile.Get(r));
            }
            var people = s.People.Items;
            int reported = 0;
            for (int i = 0; i < people.Count && reported < 10; i++)
            {
                var p = people[i];
                int before = sb.Length;
                Check(sb, "person" + p.Id + ".health", p.Health);
                Check(sb, "person" + p.Id + ".happiness", p.Happiness);
                Check(sb, "person" + p.Id + ".ageYears", p.AgeYears);
                if (sb.Length != before) reported++;
            }
            var map = s.Map;
            for (int i = 0; i < map.TileCount; i++)
            {
                if (float.IsNaN(map.Elevation[i]) || float.IsInfinity(map.Elevation[i]) || float.IsNaN(map.Fertility[i]) || float.IsNaN(map.Resource[i]))
                {
                    sb.Append("tile ").Append(i).Append(" has non-finite terrain values\n");
                    break;
                }
            }
            return sb.ToString();
        }

        private static void Check(StringBuilder sb, string what, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) sb.Append(what).Append('=').Append(v).Append('\n');
        }

        /// <summary>Compact per-civ description for assertion messages.</summary>
        public static string Describe(WorldState s)
        {
            var sb = new StringBuilder();
            var by = LivingByCiv(s);
            sb.Append("tick ").Append(s.Tick).Append(", pop ").Append(s.LivingPopulation).Append(", buildings ").Append(CompleteBuildings(s)).Append('\n');
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                by.TryGetValue(c.Id, out int pop);
                sb.Append("  civ ").Append(c.Id).Append(' ').Append(c.Race).Append(' ').Append(c.Age)
                  .Append(c.IsAlive ? "" : " (fallen)").Append(" pop=").Append(pop)
                  .Append(" settlements=").Append(c.SettlementIds.Count)
                  .Append(" research=").Append(c.TotalResearch.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(" techs=").Append(c.KnownTechs.Count).Append('\n');
            }
            return sb.ToString();
        }
    }
}
