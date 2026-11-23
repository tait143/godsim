using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// Fire simulation for tiles flagged <see cref="TileFlags.Burning"/> (set by disasters, god powers or
    /// anything else). Each run (every 2 ticks):
    /// <list type="number">
    /// <item>Discovery: Burning flags without a timer (e.g. after loading a save, or set by another module)
    /// get a deterministic 6-20 tick timer. Only a rolling window of the map is scanned per run, plus the
    /// latest ignition hint and the neighbours of known fires.</item>
    /// <item>Burning, in ascending tile-index order, at most <see cref="MaxBurningPerRun"/> tiles per run:
    /// buildings catch fire (IsOnFire, -0.08 Condition per run, destroyed with DestroyCause.Fire at &lt;= 0);
    /// Trees consume their Resource and turn Scorched when exhausted; while a tile still holds fuel, fire
    /// spreads to orthogonal neighbours with Trees or a building (0.15 per run, x1.8 in Summer/drought,
    /// x0.2 under rain, scaled by tile moisture, halved in Winter and wet spells). Fuel-less embers only
    /// smoulder until their timer ends, so ordinary fires stay local and summer fires die with the season.</item>
    /// <item>Extinguishing when the timer ends (3x faster under divine rain, 2x in a natural wet spell);
    /// IsOnFire is cleared on buildings with no burning tile left.</item>
    /// <item>Ecological recovery: Scorched tiles visited by the rolling scan recover after a few years, and
    /// forest biomes regrow young Trees.</item>
    /// </list>
    /// Fire is guaranteed to burn out: every burn consumes fuel (wood or building condition) and timers only
    /// count down. One TerrainChangedEvent with the bounding box of all changes is published per run.
    /// <para>Persistence: the timers and the scheduling cursors change behaviour (burn duration, which window is scanned,
    /// when the stray-OnFire sweep runs), so they are mirrored into the world Memory blackboard at the end of every run
    /// (<see cref="KeyScanCursor"/>, <see cref="KeyProcessCursor"/>, <see cref="KeyRunPhase"/> and packed timer blocks under
    /// <see cref="KeyTimerPrefix"/>) and restored by <see cref="Initialize"/>, so save -> load -> continue matches an
    /// uninterrupted run. At most <see cref="MaxPersistedTimerKeys"/> timer blocks are written (4 tiles each); tiles beyond
    /// that cap, and worlds without persisted fire state (fresh worlds, older saves, a swapped-in WorldState), fall back to
    /// recreating timers from the Burning flags.</para>
    /// </summary>
    public sealed class FireSystem : ISimSystem
    {
        /// <summary>Hard cap of burning tiles processed per run (performance safety).</summary>
        public const int MaxBurningPerRun = 4000;
        /// <summary>Maximum number of fires tracked at once; spread pauses while the cap is reached.</summary>
        public const int MaxTrackedFires = 16000;
        /// <summary>Minimum burn duration of a newly tracked tile, in ticks.</summary>
        public const int MinBurnTicks = 6;
        /// <summary>Maximum burn duration of a newly tracked tile, in ticks.</summary>
        public const int MaxBurnTicks = 20;
        /// <summary>Building condition lost per tick while any of its tiles burns (0.08 per 2-tick run).</summary>
        public const float BuildingDamagePerTick = 0.04f;
        /// <summary>Fraction of a tree tile's remaining wood consumed per tick of burning (a typical 60-160 wood tile is gone in 3-4 runs).</summary>
        public const float TreeBurnFractionPerTick = 0.25f;
        /// <summary>Flat wood consumed per tick of burning.</summary>
        public const float TreeBurnFlatPerTick = 4f;
        /// <summary>Per-year chance that a Scorched tile recovers.</summary>
        public const float ScorchedRecoveryPerYear = 0.15f;
        /// <summary>Wood of young trees regrown on recovered forest tiles.</summary>
        public const float RegrownTreeWood = 40f;
        /// <summary>Minimum number of tiles examined per run by the rolling discovery scan.</summary>
        public const int MinScanWindow = 8192;

        /// <summary>World Memory key: rolling discovery-scan cursor (tile index).</summary>
        public const string KeyScanCursor = "nature.fire.scan";
        /// <summary>World Memory key: processing cursor used when more than <see cref="MaxBurningPerRun"/> tiles burn.</summary>
        public const string KeyProcessCursor = "nature.fire.proc";
        /// <summary>World Memory key: run counter modulo <see cref="SweepEveryRuns"/> (phase of the stray-OnFire sweep).</summary>
        public const string KeyRunPhase = "nature.fire.run";
        /// <summary>World Memory key prefix of packed timer blocks: key + (tileIndex / 4), value = 4 x 5-bit remaining ticks.</summary>
        public const string KeyTimerPrefix = "nature.fire.t.";
        /// <summary>Tiles packed into one timer key (5 bits each, exact in a float's 24-bit mantissa).</summary>
        public const int TilesPerTimerKey = 4;
        /// <summary>Upper bound on persisted timer keys (keeps the world blackboard well under the save format's entry limit).</summary>
        public const int MaxPersistedTimerKeys = 4096;
        /// <summary>The stray-OnFire sweep runs every this many runs.</summary>
        public const int SweepEveryRuns = 8;
        private const int TimerBits = 5; // remaining ticks never exceed MaxBurnTicks (20) < 32; larger values would be clamped
        private const int TimerMask = (1 << TimerBits) - 1;

        private readonly Dictionary<int, int> _timers = new Dictionary<int, int>();
        private readonly List<int> _persistedBlocks = new List<int>();
        private readonly Dictionary<int, string> _blockKeys = new Dictionary<int, string>();
        private readonly List<string> _keyScratch = new List<string>();
        private readonly List<int> _order = new List<int>();
        private readonly List<int> _burningBuildings = new List<int>();
        private readonly HashSet<int> _burningBuildingSet = new HashSet<int>();
        private readonly HashSet<int> _damagedThisRun = new HashSet<int>();
        private WorldState _state;
        private WorldMap _map;
        private int _scanCursor;
        private int _processCursor;
        private long _runs;

        /// <inheritdoc />
        public string Name => "Divine.Fire";

        /// <inheritdoc />
        public int TickInterval => 2;

        /// <summary>Number of burning tiles that currently have a timer.</summary>
        public int TrackedFireCount => _timers.Count;

        /// <summary>Number of burning tiles processed by the last run (never above <see cref="MaxBurningPerRun"/>).</summary>
        public int LastProcessedCount { get; private set; }

        /// <summary>Remaining burn ticks of a tracked tile, or -1 when the tile has no timer.</summary>
        public int RemainingTicks(int tileIndex) => _timers.TryGetValue(tileIndex, out int t) ? t : -1;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            if (ctx?.State?.Map == null) return;
            Reset(ctx);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            var state = ctx?.State;
            var map = state?.Map;
            if (map == null) return;
            if (!ReferenceEquals(state, _state) || !ReferenceEquals(map, _map)) Reset(ctx);
            int dt = Math.Max(1, ctx.DeltaTicks);
            _runs++;
            _damagedThisRun.Clear();
            var dirty = new NatureDirtyRect();

            ConsumeHint(ctx, map, ref dirty);
            ScanWindow(ctx, map, dt, ref dirty);
            if (_timers.Count > 0) ProcessFires(ctx, map, dt, ref dirty);
            else LastProcessedCount = 0;
            ReconcileBuildings(state, map);
            if (_runs % SweepEveryRuns == 0) SweepStrayOnFire(state, map);

            Persist(state);
            NaturalEffects.PublishTerrainChanged(ctx, dirty);
        }

        // ------------------------------------------------------------------ tracking
        private void Reset(SimContext ctx)
        {
            _timers.Clear();
            _order.Clear();
            _burningBuildings.Clear();
            _burningBuildingSet.Clear();
            _damagedThisRun.Clear();
            _persistedBlocks.Clear();
            _state = ctx.State;
            _map = ctx.State.Map;
            _scanCursor = 0;
            _processCursor = 0;
            if (TryRestore(ctx.State)) return;
            var map = _map;
            int n = map.TileCount;
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                if ((map.Flags[i] & TileFlags.Burning) == 0) continue;
                if (map.Biomes[i].IsWater()) { map.Flags[i] &= ~TileFlags.Burning; changed = true; continue; }
                if (_timers.Count >= MaxTrackedFires) break;
                Track(ctx, i);
            }
            if (changed) map.MarkTerrainChanged();
        }

        // ------------------------------------------------------------------ persistence (world Memory blackboard)
        /// <summary>
        /// Restores timers, cursors and the run phase written by <see cref="Persist"/>. Returns false (nothing restored) when
        /// the world carries no persisted fire state. Never draws from the RNG and never edits the map: a loaded world must
        /// hash exactly like the saved one, and untracked Burning tiles are left to the hint/rolling scan, as without a reload.
        /// </summary>
        private bool TryRestore(WorldState state)
        {
            var mem = state.Memory;
            if (!mem.TryGetValue(KeyScanCursor, out float scan)) return false;
            var map = state.Map;
            int n = map.TileCount;
            _scanCursor = ToIndex(scan, n);
            _processCursor = mem.TryGetValue(KeyProcessCursor, out float proc) ? ToIndex(proc, n) : 0;
            _runs = mem.TryGetValue(KeyRunPhase, out float phase) ? ToIndex(phase, SweepEveryRuns) : 0;

            _keyScratch.Clear();
            foreach (var kv in mem) if (kv.Key.StartsWith(KeyTimerPrefix, StringComparison.Ordinal)) _keyScratch.Add(kv.Key);
            for (int k = 0; k < _keyScratch.Count; k++)
            {
                string key = _keyScratch[k];
                if (!int.TryParse(key.Substring(KeyTimerPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int block)
                    || block < 0 || (long)block * TilesPerTimerKey >= n)
                    continue;
                _persistedBlocks.Add(block);
                int packed = (int)MathUtil.Clamp(MathUtil.Sanitize(mem[key]), 0f, (float)((1 << (TimerBits * TilesPerTimerKey)) - 1));
                for (int j = 0; j < TilesPerTimerKey; j++)
                {
                    int t = (packed >> (TimerBits * j)) & TimerMask;
                    int idx = block * TilesPerTimerKey + j;
                    if (t <= 0 || idx >= n || _timers.Count >= MaxTrackedFires) continue;
                    if ((map.Flags[idx] & TileFlags.Burning) == 0 || map.Biomes[idx].IsWater()) continue;
                    _timers[idx] = t;
                }
            }

            // Buildings that burned on a tracked tile were registered by the last run; rebuild that set.
            var items = state.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b == null || b.IsDestroyed || !b.IsOnFire || !HasTrackedTile(map, b)) continue;
                if (_burningBuildingSet.Add(b.Id)) _burningBuildings.Add(b.Id);
            }
            return true;
        }

        /// <summary>Mirrors timers, cursors and the run phase into the world Memory (end of every run).</summary>
        private void Persist(WorldState state)
        {
            var mem = state.Memory;
            mem[KeyScanCursor] = _scanCursor;
            mem[KeyProcessCursor] = _processCursor;
            mem[KeyRunPhase] = (float)(_runs % SweepEveryRuns);
            for (int i = 0; i < _persistedBlocks.Count; i++) mem.Remove(BlockKey(_persistedBlocks[i]));
            _persistedBlocks.Clear();
            if (_timers.Count == 0) return;

            _order.Clear();
            foreach (var kv in _timers) _order.Add(kv.Key);
            _order.Sort();
            int block = -1, packed = 0;
            for (int k = 0; k < _order.Count; k++)
            {
                int idx = _order[k];
                int b = idx / TilesPerTimerKey;
                if (b != block)
                {
                    if (block >= 0) WriteBlock(mem, block, packed);
                    if (_persistedBlocks.Count >= MaxPersistedTimerKeys) { block = -1; break; }
                    block = b;
                    packed = 0;
                }
                int t = Math.Max(1, Math.Min(TimerMask, _timers[idx]));
                packed |= t << (TimerBits * (idx % TilesPerTimerKey));
            }
            if (block >= 0) WriteBlock(mem, block, packed);
            if (_blockKeys.Count > 2 * MaxPersistedTimerKeys) _blockKeys.Clear(); // bounded key cache
        }

        private void WriteBlock(Dictionary<string, float> mem, int block, int packed)
        {
            mem[BlockKey(block)] = packed;
            _persistedBlocks.Add(block);
        }

        private string BlockKey(int block)
        {
            if (!_blockKeys.TryGetValue(block, out string key))
            {
                key = KeyTimerPrefix + block.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _blockKeys[block] = key;
            }
            return key;
        }

        private static int ToIndex(float v, int count)
        {
            v = MathUtil.Sanitize(v);
            if (count <= 0 || v < 0f || v >= count) return 0;
            return (int)v;
        }

        private bool HasTrackedTile(WorldMap map, Building b)
        {
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    if (map.InBounds(x, y) && _timers.ContainsKey(map.Index(x, y))) return true;
            return false;
        }

        private bool Track(SimContext ctx, int index)
        {
            if (_timers.Count >= MaxTrackedFires || _timers.ContainsKey(index)) return false;
            _timers[index] = ctx.Rng.RangeInclusive(MinBurnTicks, MaxBurnTicks);
            return true;
        }

        private void ConsumeHint(SimContext ctx, WorldMap map, ref NatureDirtyRect dirty)
        {
            var mem = ctx.State.Memory;
            if (!mem.TryGetValue(NaturalEffects.KeyFireHint, out float hint)) return;
            mem.Remove(NaturalEffects.KeyFireHint);
            hint = MathUtil.Sanitize(hint, -1f);
            if (hint < 0f || hint >= map.TileCount) return;
            int idx = (int)hint;
            int cx = idx % map.Width, cy = idx / map.Width;
            const int r = 4;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if ((map.Flags[i] & TileFlags.Burning) == 0 || _timers.ContainsKey(i)) continue;
                    if (map.Biomes[i].IsWater()) { map.Flags[i] &= ~TileFlags.Burning; dirty.Add(x, y); continue; }
                    Track(ctx, i);
                }
        }

        private void ScanWindow(SimContext ctx, WorldMap map, int dt, ref NatureDirtyRect dirty)
        {
            int n = map.TileCount;
            int window = Math.Min(n, Math.Max(MinScanWindow, n / 16));
            int sweepRuns = (n + window - 1) / window;
            float recoverChance = Rng.PerYearToPerTicks(ScorchedRecoveryPerYear, sweepRuns * dt);
            var flags = map.Flags;
            if (_scanCursor < 0 || _scanCursor >= n) _scanCursor = 0;
            int i = _scanCursor;
            for (int k = 0; k < window; k++, i++)
            {
                if (i >= n) i = 0;
                var f = flags[i];
                if ((f & (TileFlags.Burning | TileFlags.Scorched)) == 0) continue;
                if ((f & TileFlags.Burning) != 0)
                {
                    if (_timers.ContainsKey(i)) continue;
                    if (map.Biomes[i].IsWater()) { flags[i] = f & ~TileFlags.Burning; dirty.AddIndex(map, i); continue; }
                    Track(ctx, i);
                }
                else if (ctx.Rng.Chance(recoverChance))
                {
                    Recover(map, i);
                    dirty.AddIndex(map, i);
                }
            }
            _scanCursor = i >= n ? 0 : i;
        }

        private static void Recover(WorldMap map, int i)
        {
            map.Flags[i] &= ~TileFlags.Scorched;
            var b = map.Biomes[i];
            bool forest = b == Biome.Forest || b == Biome.Jungle || b == Biome.Taiga;
            const TileFlags blockers = TileFlags.Trees | TileFlags.Road | TileFlags.Farmland | TileFlags.Wall | TileFlags.Burning;
            if (!forest || map.BuildingAt[i] >= 0 || (map.Flags[i] & blockers) != 0) return;
            map.Flags[i] |= TileFlags.Trees;
            map.Resource[i] = Math.Max(MathUtil.Sanitize(map.Resource[i]), RegrownTreeWood);
        }

        // ------------------------------------------------------------------ burning
        private void ProcessFires(SimContext ctx, WorldMap map, int dt, ref NatureDirtyRect dirty)
        {
            var state = ctx.State;
            var w = state.Weather;
            var season = SimTime.SeasonOf(state.Tick);
            float spread = NaturalEffects.BaseFireSpreadChance * NaturalEffects.FireSpreadMultiplier(w, season);
            if (dt != 2) spread = 1f - (float)Math.Pow(1f - MathUtil.Clamp01(spread), dt / 2.0);
            int timerStep = dt * (w != null && w.RainTicksRemaining > 0 ? 3 : (NaturalEffects.IsWetWeather(w) ? 2 : 1));
            float keepWood = (float)Math.Pow(1f - TreeBurnFractionPerTick, dt);
            float flatWood = TreeBurnFlatPerTick * dt;
            float buildingDamage = BuildingDamagePerTick * dt;

            _order.Clear();
            foreach (var kv in _timers) _order.Add(kv.Key);
            _order.Sort();
            int count = _order.Count;
            int budget = Math.Min(count, MaxBurningPerRun);
            int start = count > MaxBurningPerRun ? LowerBound(_order, _processCursor) : 0;
            if (start >= count) start = 0;
            var dirs = Vec2i.Dirs4;
            int last = -1;

            for (int k = 0; k < budget; k++)
            {
                int idx = _order[(start + k) % count];
                last = idx;
                if (!_timers.TryGetValue(idx, out int timer)) continue;
                var f = map.Flags[idx];
                if ((f & TileFlags.Burning) == 0) { _timers.Remove(idx); continue; } // put out by someone else
                if (map.Biomes[idx].IsWater())
                {
                    map.Flags[idx] = f & ~TileFlags.Burning;
                    _timers.Remove(idx);
                    dirty.AddIndex(map, idx);
                    continue;
                }

                // Only tiles that still hold fuel (wood or a standing building) spread; embers just smoulder.
                // Fuel burns out within a few runs, which keeps ordinary fires subcritical and bounds even
                // summer/drought fires by the length of the season.
                bool fueled = NaturalEffects.IsFlammable(map, idx);

                // Buildings on the tile catch fire (damaged once per run however many tiles burn).
                int bid = map.BuildingAt[idx];
                if (bid >= 0)
                {
                    var b = state.Buildings.Get(bid);
                    if (b != null && !b.IsDestroyed)
                    {
                        b.IsOnFire = true;
                        if (_burningBuildingSet.Add(b.Id)) _burningBuildings.Add(b.Id);
                        if (_damagedThisRun.Add(b.Id)) NaturalEffects.DamageBuilding(ctx.Ops, b, buildingDamage, DestroyCause.Fire);
                    }
                }

                // Trees are fuel; when exhausted the tile is scorched.
                if ((map.Flags[idx] & TileFlags.Trees) != 0)
                {
                    float wood = MathUtil.Sanitize(map.Resource[idx]) * keepWood - flatWood;
                    if (wood <= 0f)
                    {
                        map.Resource[idx] = 0f;
                        map.Flags[idx] = (map.Flags[idx] & ~TileFlags.Trees) | TileFlags.Scorched;
                        dirty.AddIndex(map, idx);
                    }
                    else map.Resource[idx] = wood;
                }

                // Spread to orthogonal neighbours with fuel; adopt untracked neighbouring fires.
                int x = idx % map.Width, y = idx / map.Width;
                for (int d = 0; d < dirs.Length; d++)
                {
                    int nx = x + dirs[d].X, ny = y + dirs[d].Y;
                    if (!map.InBounds(nx, ny)) continue;
                    int ni = map.Index(nx, ny);
                    var nf = map.Flags[ni];
                    if ((nf & TileFlags.Burning) != 0)
                    {
                        if (!_timers.ContainsKey(ni) && !map.Biomes[ni].IsWater()) Track(ctx, ni);
                        continue;
                    }
                    if (!fueled || _timers.Count >= MaxTrackedFires || !NaturalEffects.IsFlammable(map, ni)) continue;
                    float p = spread * NaturalEffects.FuelMoistureFactor(map.Moisture[ni]);
                    if (!ctx.Rng.Chance(p)) continue;
                    map.Flags[ni] = nf | TileFlags.Burning;
                    Track(ctx, ni);
                    dirty.Add(nx, ny);
                }

                timer -= timerStep;
                if (timer > 0) { _timers[idx] = timer; continue; }
                _timers.Remove(idx);
                Extinguish(map, idx);
                dirty.AddIndex(map, idx);
            }

            LastProcessedCount = budget;
            _processCursor = count > MaxBurningPerRun && last >= 0 ? last + 1 : 0;
        }

        private static void Extinguish(WorldMap map, int idx)
        {
            var f = map.Flags[idx] & ~TileFlags.Burning;
            if ((f & TileFlags.Trees) == 0 && map.BuildingAt[idx] < 0) f |= TileFlags.Scorched;
            map.Flags[idx] = f;
        }

        private static int LowerBound(List<int> sorted, int value)
        {
            int lo = 0, hi = sorted.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid] < value) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // ------------------------------------------------------------------ building flags
        private void ReconcileBuildings(WorldState state, WorldMap map)
        {
            for (int i = _burningBuildings.Count - 1; i >= 0; i--)
            {
                int id = _burningBuildings[i];
                var b = state.Buildings.Get(id);
                if (b != null && !b.IsDestroyed)
                {
                    if (NaturalEffects.HasBurningTile(map, b)) continue;
                    b.IsOnFire = false;
                }
                _burningBuildings.RemoveAt(i);
                _burningBuildingSet.Remove(id);
            }
        }

        /// <summary>Fixes IsOnFire on buildings this system never saw burning (loaded saves, other modules).</summary>
        private void SweepStrayOnFire(WorldState state, WorldMap map)
        {
            var items = state.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b == null || b.IsDestroyed || !b.IsOnFire || _burningBuildingSet.Contains(b.Id)) continue;
                if (NaturalEffects.HasBurningTile(map, b))
                {
                    _burningBuildingSet.Add(b.Id);
                    _burningBuildings.Add(b.Id);
                }
                else b.IsOnFire = false;
            }
        }
    }
}
