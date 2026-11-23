using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Tests.Scenarios
{
    /// <summary>Logger that keeps the last warnings/errors so failing scenarios can print them.</summary>
    public sealed class CollectingLogger : ISimLogger
    {
        private const int MaxKept = 200;
        private readonly List<string> _lines = new List<string>();

        public CollectingLogger(LogLevel min = LogLevel.Warning) { MinLevel = min; }

        public LogLevel MinLevel { get; }
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public IReadOnlyList<string> Lines => _lines;

        public void Log(LogLevel level, string message)
        {
            if (level < MinLevel) return;
            if (level == LogLevel.Error) ErrorCount++;
            else if (level == LogLevel.Warning) WarningCount++;
            _lines.Add("[" + level + "] " + (message ?? ""));
            if (_lines.Count > MaxKept) _lines.RemoveAt(0);
        }

        public string Tail(int n)
        {
            var sb = new StringBuilder();
            for (int i = Math.Max(0, _lines.Count - n); i < _lines.Count; i++)
            {
                string l = _lines[i];
                sb.AppendLine(l.Length > 400 ? l.Substring(0, 400) + "..." : l);
            }
            return sb.ToString();
        }
    }

    /// <summary>World/config factories and state queries shared by the scenario fixtures.</summary>
    public static class ScenarioWorlds
    {
        public const int SmallMap = 128;

        /// <summary>Small, fault-isolating (StrictMode=false) world. Callers tweak the returned config.</summary>
        public static SimConfig SmallWorld(ulong seed, int size = SmallMap)
        {
            return new SimConfig
            {
                Seed = seed,
                MapWidth = size,
                MapHeight = size,
                StrictMode = false,
            };
        }

        public static GodSimulation Create(SimConfig config, CollectingLogger log = null)
        {
            var cfg = config.Clone().Sanitize();
            return GodSimulation.CreateNew(cfg, log ?? new CollectingLogger());
        }

        public static IEnumerable<Settlement> AliveSettlements(WorldState s)
        {
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) yield return items[i];
        }

        /// <summary>Most populous living settlement (ties: lowest id), optionally of one civ. Null if none.</summary>
        public static Settlement LargestSettlement(WorldState s, int civId = -1)
        {
            Settlement best = null;
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var st = items[i];
                if (!st.IsAlive || (civId >= 0 && st.CivilizationId != civId)) continue;
                if (best == null || st.Population > best.Population || (st.Population == best.Population && st.Id < best.Id)) best = st;
            }
            return best;
        }

        public static int AliveCivCount(WorldState s)
        {
            int n = 0;
            var items = s.Civilizations.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) n++;
            return n;
        }

        public static int ActiveWarCount(WorldState s)
        {
            int n = 0;
            foreach (var w in s.Diplomacy.Wars) if (w.IsActive) n++;
            return n;
        }

        public static bool IsFoodProducer(GameDatabase db, BuildingType type)
        {
            if (!db.TryGetBuilding(type, out var d)) return false;
            if (d.Category == BuildingCategory.Food) return true;
            if (d.OutputsPerWorker != null)
                for (int i = 0; i < d.OutputsPerWorker.Length; i++)
                    if (d.OutputsPerWorker[i].Type == ResourceType.Food && d.OutputsPerWorker[i].Amount > 0f) return true;
            return false;
        }

        public static bool IsFoodJob(JobType j) =>
            j == JobType.Gatherer || j == JobType.Hunter || j == JobType.Fisher || j == JobType.Farmer || j == JobType.Herder;

        /// <summary>Living person count per civilization id (computed from People, not from cached civ stats).</summary>
        public static int CountCivPopulation(WorldState s, int civId)
        {
            int n = 0;
            var p = s.People.Items;
            for (int i = 0; i < p.Count; i++) if (p[i].IsAlive && p[i].CivilizationId == civId) n++;
            return n;
        }

        /// <summary>Complete, non-destroyed buildings of a settlement within radius of a point.</summary>
        public static int CountBuildingsNear(WorldState s, Settlement st, Vec2i at, int radius)
        {
            int n = 0, r2 = radius * radius;
            foreach (int bid in st.BuildingIds)
            {
                var b = s.Buildings.Get(bid);
                if (b == null || b.IsDestroyed) continue;
                if (b.Center.DistanceSqTo(at) <= r2) n++;
            }
            return n;
        }

        /// <summary>
        /// Finds an unclaimed, buildable land tile far from every living settlement, with a mostly free
        /// neighborhood, and the race best suited to its biome. Deterministic scan order.
        /// </summary>
        public static bool TryFindEmptyLand(WorldState s, GameDatabase db, int minDistance, out Vec2i tile, out RaceId race)
        {
            tile = Vec2i.Invalid;
            race = RaceId.Human;
            var map = s.Map;
            float bestScore = float.MinValue;
            var centers = new List<Vec2i>();
            foreach (var st in AliveSettlements(s)) centers.Add(st.Center);
            int margin = 6;
            for (int y = margin; y < map.Height - margin; y += 2)
                for (int x = margin; x < map.Width - margin; x += 2)
                {
                    int i = map.Index(x, y);
                    if (!map.IsFreeBuildable(i) || map.Owner[i] >= 0) continue;
                    var p = new Vec2i(x, y);
                    int dMin2 = int.MaxValue;
                    for (int c = 0; c < centers.Count; c++) { int d = centers[c].DistanceSqTo(p); if (d < dMin2) dMin2 = d; }
                    float dMin = centers.Count == 0 ? 999f : (float)Math.Sqrt(dMin2);
                    if (dMin < minDistance) continue;
                    int free = 0;
                    for (int yy = y - 3; yy <= y + 3; yy++)
                        for (int xx = x - 3; xx <= x + 3; xx++)
                        {
                            int j = map.Index(xx, yy);
                            if (map.IsFreeBuildable(j) && map.Owner[j] < 0) free++;
                        }
                    if (free < 36) continue; // of 49
                    float bestHab = -1f;
                    RaceId bestRace = RaceId.Human;
                    for (int r = 0; r < Counts.Races; r++)
                    {
                        var def = db.Races[r];
                        if (def == null || def.BiomeHabitability == null) continue;
                        float h = def.BiomeHabitability[(int)map.Biomes[i]];
                        if (h > bestHab) { bestHab = h; bestRace = (RaceId)r; }
                    }
                    if (bestHab < 0.3f) continue;
                    float score = Math.Min(dMin, 40f) + 30f * bestHab + free * 0.25f;
                    if (score > bestScore) { bestScore = score; tile = p; race = bestRace; }
                }
            return tile.IsValid;
        }

        /// <summary>One-line description of the engine's system profiles (faults/disabled first).</summary>
        public static string DescribeProfiles(SimulationEngine engine)
        {
            if (engine == null) return "(no engine)";
            var sb = new StringBuilder();
            foreach (var p in engine.Profiles)
            {
                sb.Append(p.Name).Append(": avg=").Append(p.AvgMs.ToString("0.000")).Append("ms");
                if (p.TotalFaults > 0) sb.Append(" faults=").Append(p.TotalFaults);
                if (p.Disabled) sb.Append(" DISABLED");
                if (!string.IsNullOrEmpty(p.LastError)) sb.Append(" lastError='").Append(p.LastError).Append('\'');
                sb.Append("; ");
            }
            return sb.ToString();
        }

        public static string DescribeTopSystems(SimulationEngine engine, int n)
        {
            var list = new List<SystemProfile>(engine.Profiles);
            list.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(n, list.Count); i++)
                sb.Append(list[i].Name).Append('=').Append(list[i].AvgMs.ToString("0.000")).Append("ms ");
            return sb.ToString();
        }

        public static string Join(IList<string> lines, int max = 25)
        {
            if (lines == null || lines.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(max, lines.Count); i++) sb.AppendLine("  - " + lines[i]);
            if (lines.Count > max) sb.AppendLine("  ... and " + (lines.Count - max) + " more");
            return sb.ToString();
        }
    }
}
