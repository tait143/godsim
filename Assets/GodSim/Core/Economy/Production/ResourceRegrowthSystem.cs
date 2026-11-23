using System;

namespace GodSim.Core.Economy
{
    /// <summary>
    /// Slow natural recovery of the land (every 48 ticks). Each run processes one contiguous window of about
    /// 1/<see cref="WindowsPerCycle"/> of the map (tile-index order), so a full sweep takes
    /// <c>WindowsPerCycle · DeltaTicks</c> ticks and every rate is scaled by that elapsed time per tile.
    /// The window is derived from the tick counter, so the sweep needs no persisted cursor and stays
    /// deterministic across save/load.
    /// <list type="bullet">
    /// <item>Trees tiles regain <see cref="WoodRegrowthPerYear"/> wood per year, up to <see cref="MaxTreeWood"/>.</item>
    /// <item>Forest/Jungle/Taiga tiles without Trees, buildings (on or next to the tile), roads, walls, farmland,
    /// deposits, fire or scorching may regrow Trees (per-year chance <see cref="BaseTreeSpreadPerYear"/> + <see cref="NeighbourTreeSpreadPerYear"/>
    /// per neighbouring Trees tile), starting with <see cref="SaplingWood"/> wood.</item>
    /// <item>Fertility moves toward the tile's biome/climate baseline (up faster than down).</item>
    /// <item>Scorched clears with chance <see cref="ScorchedClearPerYear"/> per year.</item>
    /// </list>
    /// Publishes at most one <see cref="TerrainChangedEvent"/> per run, bounding the tiles whose flags changed.
    /// </summary>
    public sealed class ResourceRegrowthSystem : ISimSystem
    {
        /// <summary>Number of windows the map is split into; one window is processed per run.</summary>
        public const int WindowsPerCycle = 48;
        /// <summary>Wood regained per year on a Trees tile.</summary>
        public const float WoodRegrowthPerYear = 2f;
        /// <summary>Maximum wood a Trees tile regrows to.</summary>
        public const float MaxTreeWood = 160f;
        /// <summary>Wood on a freshly regrown Trees tile.</summary>
        public const float SaplingWood = 30f;
        /// <summary>Per-year chance that an eligible woodland tile with no wooded neighbours regrows Trees.</summary>
        public const float BaseTreeSpreadPerYear = 0.01f;
        /// <summary>Extra per-year regrowth chance per neighbouring (8-way) Trees tile.</summary>
        public const float NeighbourTreeSpreadPerYear = 0.03f;
        /// <summary>Fertility regained per year while below the baseline.</summary>
        public const float FertilityRecoveryPerYear = 0.02f;
        /// <summary>Fertility lost per year while above the baseline.</summary>
        public const float FertilityDecayPerYear = 0.01f;
        /// <summary>Per-year chance that a Scorched tile heals.</summary>
        public const float ScorchedClearPerYear = 0.3f;

        private const TileFlags BlocksRegrowth = TileFlags.Road | TileFlags.Wall | TileFlags.Farmland | TileFlags.Burning | TileFlags.Scorched | ProductionTerrain.DepositFlags;

        // Biome fertility baselines (indexed by (int)Biome), adjusted per tile by moisture, temperature, river and fertile soil.
        private static readonly float[] BaseFertility =
        {
            0f, 0f, 0f, 0.2f, 0.72f, 0.5f, 0.58f, 0.6f, 0.07f, 0.5f, 0.32f, 0.14f, 0.4f, 0.08f, 0f, 0.35f
        };

        private readonly float[] _spreadChance = new float[9];
        private int _chanceCycleTicks = -1;
        private float _scorchedChance;
        private TileBounds _changed;

        /// <inheritdoc />
        public string Name => "Economy.ResourceRegrowth";

        /// <inheritdoc />
        public int TickInterval => 48;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            _chanceCycleTicks = -1;
            _changed.Reset();
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.State.Map == null) return;
            int dt = ctx.DeltaTicks;
            if (dt <= 0) return;
            var state = ctx.State;
            var map = state.Map;
            int n = map.TileCount;
            if (n <= 0) return;

            int cycleTicks = (int)Math.Min(int.MaxValue / 2, (long)WindowsPerCycle * dt);
            EnsureChances(cycleTicks);
            float years = cycleTicks / (float)SimTime.TicksPerYear;
            float woodGain = WoodRegrowthPerYear * years;
            float fertUp = FertilityRecoveryPerYear * years;
            float fertDown = FertilityDecayPerYear * years;

            int window = (n + WindowsPerCycle - 1) / WindowsPerCycle;
            long run = Math.Max(0L, state.Tick) / dt;
            int slot = (int)(run % WindowsPerCycle);
            int start = slot * window;
            int end = Math.Min(n, start + window);
            if (start >= end) return;

            _changed.Reset();
            var rng = ctx.Rng;
            int width = map.Width, height = map.Height;
            var flags = map.Flags;
            for (int i = start; i < end; i++)
            {
                Biome biome = map.Biomes[i];
                if (biome.IsWater()) continue;
                TileFlags f = flags[i];

                // ---- woodland
                if ((f & TileFlags.Trees) != 0)
                {
                    if ((f & TileFlags.Burning) == 0)
                    {
                        float r = Math.Max(0f, MathUtil.Sanitize(map.Resource[i]));
                        if (r < MaxTreeWood) map.Resource[i] = Math.Min(MaxTreeWood, r + woodGain);
                        else map.Resource[i] = r;
                    }
                }
                else if (IsWoodlandBiome(biome) && map.BuildingAt[i] < 0 && (f & BlocksRegrowth) == 0)
                {
                    int x = i % width, y = i / width;
                    int neighbours = CountTreeNeighbours(map, x, y, width, height, out bool nextToBuilding);
                    if (!nextToBuilding && rng.Chance(_spreadChance[neighbours]))
                    {
                        flags[i] = f | TileFlags.Trees;
                        f = flags[i];
                        map.Resource[i] = SaplingWood;
                        _changed.Include(x, y);
                    }
                }

                // ---- scorched earth heals
                if ((f & TileFlags.Scorched) != 0 && rng.Chance(_scorchedChance))
                {
                    flags[i] = f & ~TileFlags.Scorched;
                    _changed.Include(i % width, i / width);
                }

                // ---- soil recovers toward its baseline
                float fert = MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i]));
                float baseline = Baseline(map, i);
                if (fert < baseline) fert = Math.Min(baseline, fert + fertUp);
                else if (fert > baseline) fert = Math.Max(baseline, fert - fertDown);
                map.Fertility[i] = MathUtil.Clamp01(fert);
            }

            if (_changed.Any)
            {
                map.MarkTerrainChanged();
                if (ctx.Events != null) ctx.Events.Publish(_changed.ToEvent());
            }
        }

        /// <summary>
        /// Natural fertility of a land tile: biome base, +moisture, +river, +fertile soil, +temperate climate
        /// (the same shape world generation uses, so recovery does not erase generated variation).
        /// </summary>
        public static float Baseline(WorldMap map, int index)
        {
            if (map == null || index < 0 || index >= map.TileCount) return 0f;
            int b = (int)map.Biomes[index];
            if (b < 0 || b >= BaseFertility.Length) return 0f;
            float f = BaseFertility[b];
            if (f <= 0f) return 0f;
            float moisture = MathUtil.Clamp01(MathUtil.Sanitize(map.Moisture[index], 0.5f));
            float temperature = MathUtil.Clamp01(MathUtil.Sanitize(map.Temperature[index], 0.5f));
            f += (moisture - 0.5f) * 0.3f;
            TileFlags fl = map.Flags[index];
            if ((fl & TileFlags.River) != 0) f += 0.15f;
            if ((fl & TileFlags.FertileSoil) != 0) f += 0.15f;
            f += 0.12f * (1f - Math.Abs(temperature - 0.58f) * 2f);
            return MathUtil.Clamp01(f);
        }

        private static bool IsWoodlandBiome(Biome b) => b == Biome.Forest || b == Biome.Jungle || b == Biome.Taiga;

        /// <summary>Counts 8-way Trees neighbours; also reports whether any neighbour is a building (kept clear: yards, paths).</summary>
        private static int CountTreeNeighbours(WorldMap map, int x, int y, int width, int height, out bool nextToBuilding)
        {
            int c = 0;
            nextToBuilding = false;
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    if (nx < 0 || nx >= width) continue;
                    int n = ny * width + nx;
                    if ((map.Flags[n] & TileFlags.Trees) != 0) c++;
                    if (map.BuildingAt[n] >= 0) nextToBuilding = true;
                }
            }
            return c;
        }

        private void EnsureChances(int cycleTicks)
        {
            if (cycleTicks == _chanceCycleTicks) return;
            _chanceCycleTicks = cycleTicks;
            for (int k = 0; k < _spreadChance.Length; k++)
                _spreadChance[k] = Rng.PerYearToPerTicks(BaseTreeSpreadPerYear + NeighbourTreeSpreadPerYear * k, cycleTicks);
            _scorchedChance = Rng.PerYearToPerTicks(ScorchedClearPerYear, cycleTicks);
        }
    }
}
