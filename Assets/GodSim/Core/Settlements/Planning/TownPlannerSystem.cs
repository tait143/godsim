using System;
using System.Collections.Generic;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Town-planning AI. Every 2 ticks, for each living settlement, it takes the AI-written
    /// <see cref="Settlement.BuildQueue"/> (Priority desc, then RequestedTick asc, stable), drops invalid
    /// orders (unavailable, MaxPerSettlement, UniquePerCivilization, MinTier/MinPopulation, building cap),
    /// respects the concurrent construction-site cap (1 + population/12, max 6; Houses may exceed by 1 while
    /// people are homeless) and places at most 2 orders per settlement. Sites are sampled in deterministic
    /// rings inside the territory, filtered by hard rules (<see cref="PlacementRules"/>) and by road
    /// reachability (<see cref="AccessField"/>: the entrance must be able to reach the town's road network),
    /// then ranked by <see cref="SiteScorer"/> with zoning from <see cref="ZoningMap"/>. The best few sites are
    /// validated by planning the access road with the footprint blocked; the first that connects is built,
    /// its road is laid and farms get fields. After 4 failed attempts an order is dropped, and if space was
    /// the problem the territory grows by one tile (radius cap 24). When a Farm is destroyed, its orphaned
    /// fields are cleared. Work per run is capped by <see cref="WorkBudgetPerRun"/>; settlements are served
    /// round-robin, so a deferred settlement goes first in the next run.
    /// </summary>
    public sealed class TownPlannerSystem : ISimSystem
    {
        /// <summary>Orders attempted per settlement per run.</summary>
        public const int MaxOrdersPerSettlementPerRun = 2;
        /// <summary>Failed attempts before an order is dropped.</summary>
        public const int MaxAttemptsPerOrder = 4;
        /// <summary>Most candidate sites sampled per order (small territories).</summary>
        public const int MaxCandidatesPerOrder = 300;
        /// <summary>Fewest candidate sites sampled per order (large territories).</summary>
        public const int MinCandidatesPerOrder = 96;
        /// <summary>Per-order candidate cap = this / planning radius, clamped to [Min, Max]CandidatesPerOrder.</summary>
        public const int CandidateRadiusScale = 2400;
        /// <summary>Territory radius cap for space-driven growth.</summary>
        public const int MaxTerritoryRadius = 24;
        /// <summary>Absolute cap of concurrent construction sites.</summary>
        public const int MaxConcurrentSites = 6;
        /// <summary>Population per additional construction site.</summary>
        public const int PeoplePerExtraSite = 12;
        /// <summary>
        /// Work units per run across all settlements; the rest continues next run. One unit is about one
        /// full candidate evaluation of a small footprint (CanPlace, claimability, entrance, probe, score: about
        /// 1 microsecond in Release), so a run is bounded at roughly 1.5 ms even when every sample is a full
        /// evaluation. Zoning and reachability rebuilds and access-road searches are charged too (see the
        /// PerWorkUnit constants, calibrated to the same microsecond).
        /// </summary>
        public const int WorkBudgetPerRun = 1500;
        /// <summary>Zoning-map tiles cleared/stamped per work unit (about 1.6 ns per tile).</summary>
        public const int ZoningTilesPerWorkUnit = 512;
        /// <summary>Reachability-field window tiles per work unit (about 3 ns per tile).</summary>
        public const int FieldTilesPerWorkUnit = 256;
        /// <summary>Access-road A* expansions per work unit.</summary>
        public const int PathExpansionsPerWorkUnit = 16;
        /// <summary>Units reserved per order for entrance checks (TopCandidates x MaxAccessTriesPerSite), the zoning stamp and A* rounding.</summary>
        public const int ValidationReserve = TopCandidates * MaxAccessTriesPerSite + 2;
        /// <summary>Best-scoring sites kept per order and validated (access road planned) in score order.</summary>
        public const int TopCandidates = 4;
        /// <summary>Entrances tried per validated site.</summary>
        public const int MaxAccessTriesPerSite = 4;
        /// <summary>Maximum deterministic jitter added to site scores.</summary>
        public const float JitterAmount = 0.05f;
        /// <summary>Score bonus for the order's preferred site.</summary>
        public const float PreferredSiteBonus = 0.25f;
        /// <summary>World memory key of the round-robin settlement cursor.</summary>
        public const string CursorMemoryKey = "plan.cursor";

        private enum PlaceResult { Placed, NoSpace, NoSuitableSite }

        private static readonly RaceDefinition FallbackRace = new RaceDefinition();

        private readonly List<BuildOrder> _sorted = new List<BuildOrder>(32);
        private readonly PlannerRoads _roads = new PlannerRoads();
        private readonly ZoningMap _zoning = new ZoningMap();
        private readonly AccessField _field = new AccessField();
        private readonly int[] _typeCounts = new int[Counts.Buildings];
        private readonly float[] _topScore = new float[TopCandidates];
        private readonly Vec2i[] _topOrigin = new Vec2i[TopCandidates];
        private readonly AccessCandidate[] _access = new AccessCandidate[PlacementRules.MaxAccessTiles];
        private SiteProbe _probe;
        private int _topCount;
        private int _geometryOk;

        // Farm-destruction subscription (orphaned fields are cleared as soon as the farm is gone).
        private EventBus _bus;
        private WorldState _eventState;
        private Action<BuildingDestroyedEvent> _onDestroyed;
        private TerrainDirtyRect _eventDirty;

        /// <inheritdoc />
        public string Name => "TownPlanner";

        /// <inheritdoc />
        public int TickInterval => 2;

        /// <summary>Work units spent in the last run (see <see cref="WorkBudgetPerRun"/>).</summary>
        public int LastRunWork { get; private set; }

        /// <summary>Candidate sites evaluated in the last run.</summary>
        public int LastRunCandidates { get; private set; }

        /// <summary>Subscribes to <see cref="BuildingDestroyedEvent"/> so destroyed farms release their fields.</summary>
        public void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            _eventState = ctx.State;
            if (_onDestroyed == null) _onDestroyed = OnBuildingDestroyed;
            if (_bus == ctx.Events) return;
            if (_bus != null) _bus.Unsubscribe(_onDestroyed);
            _bus = ctx.Events;
            if (_bus != null) _bus.Subscribe(_onDestroyed);
        }

        private void OnBuildingDestroyed(BuildingDestroyedEvent e)
        {
            if (e.Type != BuildingType.Farm) return;
            var state = _eventState;
            if (state == null || state.Map == null) return;
            var b = state.Buildings.Get(e.BuildingId);
            int w = b != null ? MathUtil.Clamp(b.Width, 1, 8) : 3, h = b != null ? MathUtil.Clamp(b.Height, 1, 8) : 3;
            PlannerRoads.ClearOrphanFields(state, e.Origin.X - 1, e.Origin.Y - 1, e.Origin.X + w, e.Origin.Y + h, ref _eventDirty);
        }

        /// <summary>Concurrent construction-site cap for a population: min(6, 1 + population / 12).</summary>
        public static int SiteCapFor(int population) => Math.Min(MaxConcurrentSites, 1 + Math.Max(0, population) / PeoplePerExtraSite);

        /// <summary>Planning radius of a settlement: territory radius + 2 (clamped to [2, MaxTerritoryRadius + 2]).</summary>
        public static int PlanningRadiusOf(Settlement s) => MathUtil.Clamp(s.TerritoryRadius + 2, 2, MaxTerritoryRadius + 2);

        /// <summary>Candidate sites sampled per order at a planning radius: CandidateRadiusScale / radius, clamped to [96, 300].</summary>
        public static int CandidateCapFor(int radius) => MathUtil.Clamp(CandidateRadiusScale / Math.Max(1, radius), MinCandidatesPerOrder, MaxCandidatesPerOrder);

        /// <summary>Work units charged per evaluated candidate: 1, plus 1 per 32 footprint tiles (large footprints probe more).</summary>
        public static int EvaluationCost(int width, int height) => 1 + MathUtil.Clamp(width, 1, 8) * MathUtil.Clamp(height, 1, 8) / 32;

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            LastRunWork = 0;
            LastRunCandidates = 0;
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null || ctx.State.Map == null) return;
            if (_bus != ctx.Events || _eventState != ctx.State) Initialize(ctx);
            PlannerRoads.Commit(ctx, ref _eventDirty);
            var state = ctx.State;
            var list = state.Settlements.Items;
            int n = list.Count;
            if (n == 0) return;
            int start = (int)MathUtil.Clamp(MathUtil.Sanitize(state.Memory.TryGetValue(CursorMemoryKey, out float c) ? c : 0f), 0f, 1e9f);
            start = ((start % n) + n) % n;
            int next = (start + 1) % n;
            int remaining = WorkBudgetPerRun;
            for (int k = 0; k < n; k++)
            {
                int idx = (start + k) % n;
                var s = list[idx];
                if (s == null || !s.IsAlive) continue;
                if (!PlanSettlement(ctx, s, ref remaining)) { next = idx; break; }
            }
            LastRunWork = WorkBudgetPerRun - remaining;
            state.Memory[CursorMemoryKey] = next;
        }

        /// <summary>Plans one settlement. Returns false if it stopped early because the run's budget ran out.</summary>
        private bool PlanSettlement(SimContext ctx, Settlement s, ref int remaining)
        {
            var state = ctx.State;
            var civ = state.Civilizations.Get(s.CivilizationId);
            if (civ == null || !civ.IsAlive || !state.Map.InBounds(s.Center)) return true;
            var dirty = default(TerrainDirtyRect);
            _roads.EnsurePlaza(state, s, ref dirty);
            bool completed = true;
            var queue = s.BuildQueue;
            if (queue.Count > 0)
            {
                SortQueue(queue);
                int sites = CountBuildings(state, ctx.Db, s, _typeCounts, out int stampTiles);
                int attempted = 0;
                bool zoningReady = false, fieldReady = false;
                int siteCap = SiteCapFor(s.Population);
                int maxBuildings = ctx.Config != null ? ctx.Config.MaxBuildingsPerSettlement : 600;
                int radius = PlanningRadiusOf(s);
                int candidateCap = CandidateCapFor(radius);
                int zoningSide = 2 * (radius + ZoningMap.Margin) + 1;
                int zoningCharge = CeilDiv(zoningSide * zoningSide + stampTiles, ZoningTilesPerWorkUnit);
                int fieldCharge = CeilDiv(zoningSide * zoningSide, FieldTilesPerWorkUnit);
                var race = RaceOf(ctx.Db, s.Race);
                for (int k = 0; k < _sorted.Count && attempted < MaxOrdersPerSettlementPerRun; k++)
                {
                    var order = _sorted[k];
                    string reason = ValidateOrder(ctx.Db, state, civ, s, order.Type, maxBuildings, _typeCounts, out var def);
                    if (reason != null)
                    {
                        queue.Remove(order);
                        s.LogAi("Dropped " + order.Type + " order: " + reason);
                        continue;
                    }
                    int cap = siteCap + (order.Type == BuildingType.House && s.Homeless > 0 ? 1 : 0);
                    if (sites >= cap) continue;

                    int evalCost = EvaluationCost(def.Width, def.Height);
                    int need = (candidateCap + 1) * evalCost + ValidationReserve
                        + (zoningReady ? 0 : zoningCharge)
                        + (fieldReady ? 0 : fieldCharge);
                    if (need > remaining && remaining < WorkBudgetPerRun) { completed = false; break; }

                    attempted++;
                    if (!zoningReady)
                    {
                        _zoning.Build(state, ctx.Db, s, radius);
                        remaining -= zoningCharge;
                        zoningReady = true;
                    }
                    if (!fieldReady)
                    {
                        _field.Build(state, s, radius + ZoningMap.Margin);
                        remaining -= fieldCharge;
                        fieldReady = true;
                    }
                    var result = TryPlace(ctx, s, race, order, def, radius, candidateCap, evalCost, remaining, ref dirty, out int evaluated, out int pathWork);
                    remaining -= evaluated * evalCost + pathWork;
                    LastRunCandidates += evaluated;
                    if (result == PlaceResult.Placed)
                    {
                        queue.Remove(order);
                        _typeCounts[(int)order.Type]++;
                        sites++;
                        fieldReady = false; // the new footprint, road and fields changed reachability
                        continue;
                    }

                    order.Attempts++;
                    if (order.Attempts < MaxAttemptsPerOrder) continue;
                    queue.Remove(order);
                    bool space = result == PlaceResult.NoSpace;
                    s.LogAi("Gave up on " + order.Type + " after " + order.Attempts + " attempts (" + (space ? "no space" : "no suitable site") + ")");
                    if (space && s.TerritoryRadius < MaxTerritoryRadius)
                        ctx.Ops.ClaimTerritory(s, Math.Min(MaxTerritoryRadius, s.TerritoryRadius + 1));
                }
                _sorted.Clear();
            }
            PlannerRoads.Commit(ctx, ref dirty);
            return completed;
        }

        private static int CeilDiv(int a, int b) => a <= 0 ? 0 : (a + b - 1) / b;

        /// <summary>
        /// Returns null if the order may be planned now, otherwise a short drop reason.
        /// </summary>
        public static string ValidateOrder(GameDatabase db, WorldState state, Civilization civ, Settlement s, BuildingType type, int maxBuildings, out BuildingDefinition def)
        {
            return ValidateOrder(db, state, civ, s, type, maxBuildings, null, out def);
        }

        private static string ValidateOrder(GameDatabase db, WorldState state, Civilization civ, Settlement s, BuildingType type, int maxBuildings, int[] typeCounts, out BuildingDefinition def)
        {
            def = null;
            if (db == null || state == null || civ == null || s == null) return "invalid context";
            if (!db.TryGetBuilding(type, out def)) return "unknown building";
            if (!db.IsBuildingAvailable(civ, type)) return "unavailable";
            if (s.Tier < def.MinTier) return "tier too low";
            if (s.Population < def.MinPopulation) return "population too low";
            if (s.BuildingIds.Count >= maxBuildings) return "building limit reached";
            if (def.MaxPerSettlement >= 0)
            {
                int have = typeCounts != null ? typeCounts[(int)type] : CountOfType(state, s, type);
                if (have >= def.MaxPerSettlement) return "max per settlement reached";
            }
            if (def.UniquePerCivilization && CivilizationHas(state, civ, type)) return "unique per civilization";
            return null;
        }

        /// <summary>Existing (incl. incomplete, excl. destroyed) buildings of a type in the settlement.</summary>
        public static int CountOfType(WorldState state, Settlement s, BuildingType type)
        {
            int n = 0;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b != null && !b.IsDestroyed && b.Type == type) n++;
            }
            return n;
        }

        /// <summary>Incomplete, non-destroyed buildings (active construction sites) of the settlement.</summary>
        public static int CountConstructionSites(WorldState state, Settlement s)
        {
            int n = 0;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b != null && !b.IsDestroyed && !b.IsComplete) n++;
            }
            return n;
        }

        /// <summary>
        /// One pass over the settlement's buildings: per-type counts into <paramref name="counts"/>, and an upper
        /// bound of the tiles a zoning rebuild will stamp; returns the number of construction sites.
        /// </summary>
        private static int CountBuildings(WorldState state, GameDatabase db, Settlement s, int[] counts, out int stampTiles)
        {
            Array.Clear(counts, 0, counts.Length);
            int sites = 0;
            stampTiles = 0;
            const int reach = 2 * ZoningMap.InfluenceRadius;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t >= 0 && t < counts.Length) counts[t]++;
                if (!b.IsComplete) sites++;
                if (db.TryGetBuilding(b.Type, out var def) && (def.Desirability != 0f || ZoningMap.IsResidentialAnchor(def)))
                    stampTiles += (Math.Max(1, b.Width) + reach) * (Math.Max(1, b.Height) + reach);
            }
            return sites;
        }

        private static bool CivilizationHas(WorldState state, Civilization civ, BuildingType type)
        {
            var sids = civ.SettlementIds;
            for (int k = 0; k < sids.Count; k++)
            {
                var other = state.Settlements.Get(sids[k]);
                if (other != null && other.IsAlive && CountOfType(state, other, type) > 0) return true;
            }
            return false;
        }

        private static RaceDefinition RaceOf(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            var races = db.Races;
            return i >= 0 && i < races.Count && races[i] != null ? races[i] : FallbackRace;
        }

        private void SortQueue(List<BuildOrder> queue)
        {
            for (int i = queue.Count - 1; i >= 0; i--) if (queue[i] == null) queue.RemoveAt(i);
            _sorted.Clear();
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                int j = _sorted.Count;
                _sorted.Add(o);
                while (j > 0 && Before(o, _sorted[j - 1])) { _sorted[j] = _sorted[j - 1]; j--; }
                _sorted[j] = o;
            }
        }

        private static bool Before(BuildOrder a, BuildOrder b)
        {
            float pa = MathUtil.Sanitize(a.Priority), pb = MathUtil.Sanitize(b.Priority);
            if (pa != pb) return pa > pb;
            return a.RequestedTick < b.RequestedTick;
        }

        private PlaceResult TryPlace(SimContext ctx, Settlement s, RaceDefinition race, BuildOrder order, BuildingDefinition def, int radius, int candidateCap,
            int evalCost, int budgetLeft, ref TerrainDirtyRect dirty, out int evaluated, out int pathWork)
        {
            pathWork = 0;
            var map = ctx.State.Map;
            int w = MathUtil.Clamp(def.Width, 1, 8), h = MathUtil.Clamp(def.Height, 1, 8);
            bool rugged = PlacementRules.UsesRugged(s.Race, def.Type);
            _topCount = 0;
            _geometryOk = 0;
            evaluated = 0;

            Vec2i preferred = Vec2i.Invalid;
            if (order.HasPreferredSite && map.InBounds(order.PreferredSite))
            {
                preferred = order.PreferredSite;
                evaluated++;
                Evaluate(ctx, s, race, def, preferred, w, h, rugged, radius, PreferredSiteBonus);
            }

            // Deterministic stride sampling over the rings; only sampled offsets are visited.
            int side = 2 * radius + 1;
            int stride = Math.Max(1, (side * side + candidateCap - 1) / candidateCap);
            int phase = stride > 1 ? ctx.Rng.Range(0, stride) : 0;
            int before = 0, r2 = radius * radius;
            var c = s.Center;
            for (int r = 0; r <= radius && evaluated < candidateCap; r++)
            {
                int count = PlacementRules.RingCount(r);
                int first = ((phase - before) % stride + stride) % stride;
                for (int t = first; t < count && evaluated < candidateCap; t += stride)
                {
                    PlacementRules.RingOffset(r, t, out int dx, out int dy);
                    if (dx * dx + dy * dy > r2) continue;
                    var origin = new Vec2i(c.X + dx - w / 2, c.Y + dy - h / 2);
                    if (origin == preferred) continue;
                    evaluated++;
                    Evaluate(ctx, s, race, def, origin, w, h, rugged, radius, 0f);
                }
                before += count;
            }

            // A* only polishes a route the field already proved, so its expansions may be capped by the budget left.
            int pathUnits = budgetLeft - evaluated * evalCost - ValidationReserve;
            int maxExpansions = MathUtil.Clamp(pathUnits, 0, PlannerRoads.AccessMaxExpansions / PathExpansionsPerWorkUnit) * PathExpansionsPerWorkUnit;
            for (int k = 0; k < _topCount; k++)
            {
                Vec2i origin = _topOrigin[k];
                bool planned = PlanEntrance(map, origin, w, h, s.Center, maxExpansions, out byte rotation, out int tries);
                pathWork += tries + CeilDiv(_roads.LastAccessExpansions, PathExpansionsPerWorkUnit);
                if (!planned) continue;
                var b = ctx.Ops.CreateBuilding(s, def.Type, origin, rotation, false, rugged);
                if (b == null) continue;
                if (def.Type == BuildingType.Farm) PlannerRoads.ClearFarmlandUnder(map, origin, w, h, ref dirty);
                _roads.LayLastPath(map, ref dirty);
                if (def.Type == BuildingType.Farm) _roads.MarkFarmland(map, s, b, PlannerRoads.MaxFieldTiles, ref dirty);
                pathWork += CeilDiv(_zoning.Stamp(b, def), ZoningTilesPerWorkUnit);
                s.LogAi("Placed " + def.Type + " at " + origin + " score " + FormatScore(_topScore[k]));
                return PlaceResult.Placed;
            }
            return _geometryOk == 0 ? PlaceResult.NoSpace : PlaceResult.NoSuitableSite;
        }

        /// <summary>Plans the access road of a site (footprint blocked), trying its best entrances in order. Leaves the path in the road helper.</summary>
        private bool PlanEntrance(WorldMap map, Vec2i origin, int w, int h, Vec2i toward, int maxExpansions, out byte rotation, out int tries)
        {
            rotation = 0;
            tries = 0;
            int n = PlacementRules.CollectAccessTiles(map, origin, w, h, _field, toward, _access);
            for (int i = 0; i < n && i < MaxAccessTriesPerSite; i++)
            {
                tries++;
                if (!_roads.PlanAccessPath(map, _field, _access[i].Tile, origin, w, h, maxExpansions)) continue;
                rotation = _access[i].Rotation;
                return true;
            }
            return false;
        }

        private void Evaluate(SimContext ctx, Settlement s, RaceDefinition race, BuildingDefinition def, Vec2i origin, int w, int h, bool rugged, int radius, float bonus)
        {
            var map = ctx.State.Map;
            int cx = origin.X + w / 2 - s.Center.X, cy = origin.Y + h / 2 - s.Center.Y;
            if (cx * cx + cy * cy > radius * radius) return;
            if (!ctx.Ops.CanPlace(origin, w, h, rugged)) return;
            if (!PlacementRules.FootprintClaimable(map, s.Id, origin, w, h, def.Type == BuildingType.Farm)) return;
            if (!PlacementRules.FindAccessTile(map, origin, w, h, _field, s.Center, out _).IsValid) return;
            _geometryOk++;
            PlacementRules.Probe(map, origin, w, h, ref _probe);
            if (!PlacementRules.MeetsRequirements(def.Placement, in _probe)) return;
            float jitter = ctx.Rng.NextFloat() * JitterAmount;
            float score = SiteScorer.Score(map, s, race, def, origin, in _probe, _zoning, radius, jitter) + bonus;
            InsertTop(score, origin);
        }

        /// <summary>Keeps the <see cref="TopCandidates"/> best sites, best first; on equal score the earlier site wins.</summary>
        private void InsertTop(float score, Vec2i origin)
        {
            if (_topCount == TopCandidates && score <= _topScore[_topCount - 1]) return;
            int j = _topCount < TopCandidates ? _topCount++ : _topCount - 1;
            while (j > 0 && _topScore[j - 1] < score)
            {
                _topScore[j] = _topScore[j - 1];
                _topOrigin[j] = _topOrigin[j - 1];
                j--;
            }
            _topScore[j] = score;
            _topOrigin[j] = origin;
        }

        /// <summary>Culture-independent two-decimal formatting used in AI log lines (e.g. "0.83").</summary>
        public static string FormatScore(float v)
        {
            int c = MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(v), -1000f, 1000f) * 100f);
            bool neg = c < 0;
            if (neg) c = -c;
            int frac = c % 100;
            return (neg ? "-" : "") + (c / 100) + "." + (frac < 10 ? "0" : "") + frac;
        }
    }
}
