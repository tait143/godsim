using System;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// Seasons, weather and long-term climate. Every tick it:
    /// <list type="bullet">
    /// <item>sets <see cref="WeatherState.Season"/> from the calendar;</item>
    /// <item>random-walks <see cref="WeatherState.Rainfall"/> in [0.5, 1.5] (mean-reverting, wetter in spring,
    /// drier under drought, wetter under divine rain);</item>
    /// <item>computes <see cref="WeatherState.FoodYieldMultiplier"/> = season base x rainfall factor
    /// x 0.5 (drought) x 1.1 (rain), clamped to [0.1, 2], then counts the drought/rain timers down;</item>
    /// <item>moves <see cref="WeatherState.TemperatureOffset"/> along a deterministic multi-century climate
    /// oscillation derived from the world seed plus small persistent noise;</item>
    /// <item>rolls occasional storms (autumn/winter more likely) that decay over a few days;</item>
    /// <item>once per winter (midwinter day) homeless people in cold settlements lose 0.05 Health.</item>
    /// </list>
    /// Exposure deaths are left to the population module; this system only lowers Health.
    /// The system keeps no private persistent state: everything lives in WorldState.Weather/Memory.
    /// </summary>
    public sealed class SeasonWeatherSystem : ISimSystem
    {
        /// <summary>Day of the year (0..95) on which winter exposure is applied: the middle of winter.</summary>
        public const int ExposureDayOfYear = 3 * SimTime.TicksPerSeason + SimTime.TicksPerSeason / 2;
        /// <summary>Health lost by an exposed homeless person once per winter.</summary>
        public const float ExposureHealthLoss = 0.05f;
        /// <summary>Per-tick mean reversion strength of rainfall.</summary>
        public const float RainfallReversion = 0.03f;
        /// <summary>Per-tick standard deviation of the rainfall walk.</summary>
        public const float RainfallNoise = 0.025f;
        /// <summary>Maximum magnitude of the short-term temperature noise around the climate curve.</summary>
        public const float TemperatureNoiseMax = 0.04f;
        /// <summary>Per-tick decay factor of storm intensity.</summary>
        public const float StormDecayPerTick = 0.85f;

        /// <inheritdoc />
        public string Name => "Divine.SeasonWeather";

        /// <inheritdoc />
        public int TickInterval => 1;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            var state = ctx?.State;
            if (state == null) return;
            var w = state.Weather;
            SanitizeWeather(w);
            w.Season = SimTime.SeasonOf(state.Tick);
            w.FoodYieldMultiplier = NaturalEffects.ComputeFoodYield(w.Season, w.Rainfall, w.DroughtTicksRemaining > 0, w.RainTicksRemaining > 0);
            if (!state.Memory.ContainsKey(NaturalEffects.KeyClimateEra))
                state.Memory[NaturalEffects.KeyClimateEra] = EraFor(NaturalEffects.ClimateOscillation(state.Seed, state.Tick), 0);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            var state = ctx?.State;
            if (state == null) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            long tick = state.Tick;
            var w = state.Weather;
            var rng = ctx.Rng;
            SanitizeWeather(w);

            var season = SimTime.SeasonOf(tick);
            w.Season = season;
            bool drought = w.DroughtTicksRemaining > 0;
            bool raining = w.RainTicksRemaining > 0;

            UpdateRainfall(w, rng, season, drought, raining, dt);
            UpdateTemperature(w, rng, state.Seed, tick, dt);
            UpdateStorms(w, rng, season, dt);

            w.FoodYieldMultiplier = NaturalEffects.ComputeFoodYield(season, w.Rainfall, drought, raining);
            if (w.DroughtTicksRemaining > 0) w.DroughtTicksRemaining = Math.Max(0, w.DroughtTicksRemaining - dt);
            if (w.RainTicksRemaining > 0) w.RainTicksRemaining = Math.Max(0, w.RainTicksRemaining - dt);

            if (season == Season.Winter && CrossedDay(tick, dt, ExposureDayOfYear)) ApplyWinterExposure(state, w.TemperatureOffset);

            UpdateClimateEra(ctx, tick);
        }

        // ------------------------------------------------------------------ weather
        private static void UpdateRainfall(WeatherState w, Rng rng, Season season, bool drought, bool raining, int dt)
        {
            float target = 1f;
            switch (season)
            {
                case Season.Spring: target += 0.06f; break;
                case Season.Summer: target -= 0.04f; break;
                case Season.Autumn: target += 0.02f; break;
            }
            if (drought) target -= 0.35f;
            if (raining) target += 0.3f;
            float revert = 1f - (float)Math.Pow(1f - RainfallReversion, dt);
            float r = w.Rainfall + (target - w.Rainfall) * revert + rng.Gaussian(0f, RainfallNoise) * (float)Math.Sqrt(dt);
            w.Rainfall = MathUtil.Clamp(MathUtil.Sanitize(r, 1f), NaturalEffects.MinRainfall, NaturalEffects.MaxRainfall);
        }

        private static void UpdateTemperature(WeatherState w, Rng rng, ulong seed, long tick, int dt)
        {
            // The noise component is recovered from the stored offset, so no extra state is needed and
            // save/load keeps the curve continuous.
            float prevClimate = NaturalEffects.ClimateOscillation(seed, tick - dt);
            float noise = MathUtil.Clamp(w.TemperatureOffset - prevClimate, -TemperatureNoiseMax, TemperatureNoiseMax);
            noise = noise * (float)Math.Pow(0.98, dt) + rng.Gaussian(0f, 0.004f) * (float)Math.Sqrt(dt);
            noise = MathUtil.Clamp(MathUtil.Sanitize(noise), -TemperatureNoiseMax, TemperatureNoiseMax);
            float t = NaturalEffects.ClimateOscillation(seed, tick) + noise;
            w.TemperatureOffset = MathUtil.Clamp(MathUtil.Sanitize(t), -1f, 1f);
        }

        private static void UpdateStorms(WeatherState w, Rng rng, Season season, int dt)
        {
            float s = w.StormIntensity * (float)Math.Pow(StormDecayPerTick, dt);
            if (s < 0.01f) s = 0f;
            float perTick;
            switch (season)
            {
                case Season.Autumn: perTick = 0.012f; break;
                case Season.Winter: perTick = 0.010f; break;
                case Season.Summer: perTick = 0.004f; break;
                default: perTick = 0.003f; break;
            }
            float chance = 1f - (float)Math.Pow(1f - perTick, dt);
            if (rng.Chance(chance))
            {
                s = Math.Max(s, rng.Range(0.35f, 0.8f));
                w.Rainfall = MathUtil.Clamp(w.Rainfall + 0.08f, NaturalEffects.MinRainfall, NaturalEffects.MaxRainfall);
            }
            w.StormIntensity = MathUtil.Clamp01(MathUtil.Sanitize(s));
        }

        /// <summary>Repairs out-of-range or non-finite weather values (e.g. from an old or tampered save).</summary>
        public static void SanitizeWeather(WeatherState w)
        {
            if (w == null) return;
            w.Rainfall = MathUtil.Clamp(MathUtil.Sanitize(w.Rainfall, 1f), NaturalEffects.MinRainfall, NaturalEffects.MaxRainfall);
            w.TemperatureOffset = MathUtil.Clamp(MathUtil.Sanitize(w.TemperatureOffset), -1f, 1f);
            w.FoodYieldMultiplier = MathUtil.Clamp(MathUtil.Sanitize(w.FoodYieldMultiplier, 1f), NaturalEffects.MinFoodYield, NaturalEffects.MaxFoodYield);
            w.StormIntensity = MathUtil.Clamp01(MathUtil.Sanitize(w.StormIntensity));
            if (w.DroughtTicksRemaining < 0) w.DroughtTicksRemaining = 0;
            if (w.RainTicksRemaining < 0) w.RainTicksRemaining = 0;
            if (!Enum.IsDefined(typeof(Season), w.Season)) w.Season = Season.Spring;
        }

        /// <summary>True if a tick with the given day-of-year lies in the interval (tick - dt, tick].</summary>
        private static bool CrossedDay(long tick, int dt, int dayOfYear)
        {
            for (long t = tick - dt + 1; t <= tick; t++)
                if (t >= 0 && SimTime.DayOfYear(t) == dayOfYear) return true;
            return false;
        }

        // ------------------------------------------------------------------ exposure
        private static void ApplyWinterExposure(WorldState state, float temperatureOffset)
        {
            var map = state.Map;
            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                Biome biome = map != null && map.InBounds(s.Center) ? map.Biomes[map.Index(s.Center)] : Biome.Grassland;
                if (!NaturalEffects.IsExposureCold(biome, temperatureOffset)) continue;
                var ids = s.ResidentIds;
                for (int k = 0; k < ids.Count; k++)
                {
                    var p = state.People.Get(ids[k]);
                    if (p == null || !p.IsAlive || p.HomeId >= 0) continue;
                    float h = MathUtil.Sanitize(p.Health, 0f) - ExposureHealthLoss;
                    p.Health = h > 0f ? h : 0f;
                }
            }
        }

        // ------------------------------------------------------------------ climate eras (chronicle flavor)
        private static int EraFor(float climate, int current)
        {
            switch (current)
            {
                case -1: return climate > -0.06f ? (climate > 0.09f ? 1 : 0) : -1;
                case 1: return climate < 0.06f ? (climate < -0.09f ? -1 : 0) : 1;
                default: return climate < -0.09f ? -1 : (climate > 0.09f ? 1 : 0);
            }
        }

        private static void UpdateClimateEra(SimContext ctx, long tick)
        {
            var state = ctx.State;
            int current = MathUtil.Clamp(MathUtil.RoundToInt(state.Memory.TryGetValue(NaturalEffects.KeyClimateEra, out float e) ? MathUtil.Sanitize(e) : 0f), -1, 1);
            int next = EraFor(NaturalEffects.ClimateOscillation(state.Seed, tick), current);
            if (next == current) { if (!state.Memory.ContainsKey(NaturalEffects.KeyClimateEra)) state.Memory[NaturalEffects.KeyClimateEra] = current; return; }
            state.Memory[NaturalEffects.KeyClimateEra] = next;
            string text;
            if (next < 0) text = "A long cold settled over the world; winters grew bitter and the harvests thin.";
            else if (next > 0) text = "The world grew warm; summers lingered and the snows retreated to the peaks.";
            else if (current < 0) text = "The great cold loosened its grip, and the rivers ran free of ice once more.";
            else text = "The long warmth faded, and the seasons returned to their old ways.";
            ctx.Ops?.AddChronicle(ChronicleCategory.World, text, -1, -1, Vec2i.Invalid, 1);
        }
    }
}
