using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// Shared, stateless rules of the living world (seasons, climate, fire behavior, disaster damage).
    /// Used by <see cref="SeasonWeatherSystem"/>, <see cref="DisasterSystem"/> and <see cref="FireSystem"/>;
    /// other modules may call these helpers too (they only touch state passed in explicitly and route
    /// entity destruction through <see cref="WorldOps"/>). All randomness comes from the caller's
    /// <see cref="Rng"/>, so results are deterministic.
    /// </summary>
    public static class NaturalEffects
    {
        // ------------------------------------------------------------------ blackboard keys
        /// <summary>WorldState.Memory: current climate era (-1 cold, 0 temperate, +1 warm).</summary>
        public const string KeyClimateEra = "nature.climateEra";
        /// <summary>WorldState.Memory: tile index of the latest natural ignition (consumed by FireSystem).</summary>
        public const string KeyFireHint = "nature.fireHint";
        /// <summary>Civilization.Memory: tick of the last natural disaster that struck the civ.</summary>
        public const string KeyLastDisasterTick = "nature.lastDisasterTick";
        /// <summary>Civilization.Memory: number of natural disasters that struck the civ.</summary>
        public const string KeyDisasterCount = "nature.disasterCount";

        // ------------------------------------------------------------------ tuning
        /// <summary>Lower clamp of <see cref="WeatherState.FoodYieldMultiplier"/>.</summary>
        public const float MinFoodYield = 0.1f;
        /// <summary>Upper clamp of <see cref="WeatherState.FoodYieldMultiplier"/>.</summary>
        public const float MaxFoodYield = 2f;
        /// <summary>Lower bound of the rainfall random walk.</summary>
        public const float MinRainfall = 0.5f;
        /// <summary>Upper bound of the rainfall random walk.</summary>
        public const float MaxRainfall = 1.5f;
        /// <summary>Amplitude of the deterministic multi-century climate oscillation.</summary>
        public const float ClimateAmplitude = 0.15f;
        /// <summary>Food multiplier while a drought is active.</summary>
        public const float DroughtYieldFactor = 0.5f;
        /// <summary>Food multiplier while (divine) rain is active.</summary>
        public const float RainYieldFactor = 1.1f;
        /// <summary>TemperatureOffset below which every settlement counts as cold for winter exposure.</summary>
        public const float ColdClimateThreshold = -0.1f;
        /// <summary>Base chance per fire run that a burning tile ignites one flammable orthogonal neighbour.</summary>
        public const float BaseFireSpreadChance = 0.15f;

        // ------------------------------------------------------------------ seasons & weather
        /// <summary>Base food yield of a season: Spring 1.05, Summer 1.25, Autumn 1.1, Winter 0.4.</summary>
        public static float SeasonalBaseYield(Season season)
        {
            switch (season)
            {
                case Season.Spring: return 1.05f;
                case Season.Summer: return 1.25f;
                case Season.Autumn: return 1.1f;
                case Season.Winter: return 0.4f;
                default: return 1f;
            }
        }

        /// <summary>Food factor from rainfall: a dampened linear response, 0.8 at the driest to 1.2 at the wettest.</summary>
        public static float RainfallYieldFactor(float rainfall)
        {
            float r = MathUtil.Clamp(MathUtil.Sanitize(rainfall, 1f), MinRainfall, MaxRainfall);
            return 1f + (r - 1f) * 0.4f;
        }

        /// <summary>
        /// Aggregated food multiplier: season base x rainfall factor x 0.5 (drought) x 1.1 (rain),
        /// clamped to [<see cref="MinFoodYield"/>, <see cref="MaxFoodYield"/>].
        /// </summary>
        public static float ComputeFoodYield(Season season, float rainfall, bool drought, bool raining)
        {
            float y = SeasonalBaseYield(season) * RainfallYieldFactor(rainfall);
            if (drought) y *= DroughtYieldFactor;
            if (raining) y *= RainYieldFactor;
            return MathUtil.Clamp(MathUtil.Sanitize(y, 1f), MinFoodYield, MaxFoodYield);
        }

        /// <summary>
        /// Deterministic long-term climate swing for a world seed: two superposed sines (a 240-480 year
        /// cycle and a 60-120 year cycle, phases from the seed). Range [-ClimateAmplitude, +ClimateAmplitude].
        /// </summary>
        public static float ClimateOscillation(ulong seed, long tick)
        {
            ulong h1 = Hashing.Mix64(seed ^ 0xC1A7E0A11CE5EEDUL);
            ulong h2 = Hashing.Mix64(h1 ^ 0x5EA5025UL);
            double years = tick / (double)SimTime.TicksPerYear;
            double p1 = 240.0 + (h1 % 2401UL) / 10.0;
            double p2 = 60.0 + (h2 % 601UL) / 10.0;
            double ph1 = ((h1 >> 24) % 10000UL) / 10000.0 * 2.0 * Math.PI;
            double ph2 = ((h2 >> 24) % 10000UL) / 10000.0 * 2.0 * Math.PI;
            double v = 0.7 * Math.Sin(2.0 * Math.PI * years / p1 + ph1) + 0.3 * Math.Sin(2.0 * Math.PI * years / p2 + ph2);
            return MathUtil.Clamp(MathUtil.Sanitize((float)(ClimateAmplitude * v)), -ClimateAmplitude, ClimateAmplitude);
        }

        /// <summary>Biomes whose winters are lethal for the homeless (Tundra, Taiga, Snow).</summary>
        public static bool IsColdBiome(Biome biome) => biome == Biome.Tundra || biome == Biome.Taiga || biome == Biome.Snow;

        /// <summary>True if winter exposure applies to a settlement on this biome under the given climate offset.</summary>
        public static bool IsExposureCold(Biome centerBiome, float temperatureOffset) =>
            IsColdBiome(centerBiome) || MathUtil.Sanitize(temperatureOffset) < ColdClimateThreshold;

        /// <summary>Natural wet spell (high rainfall or a strong storm): fires spread less and die sooner.</summary>
        public static bool IsWetWeather(WeatherState w) => w != null && (w.Rainfall >= 1.25f || w.StormIntensity >= 0.5f);

        // ------------------------------------------------------------------ fire
        /// <summary>
        /// Weather multiplier on fire spread: x1.8 in Summer or drought, x0.2 while it rains (divine rain),
        /// x0.5 in Winter, x0.5 during a natural wet spell.
        /// </summary>
        public static float FireSpreadMultiplier(WeatherState w, Season season)
        {
            float m = 1f;
            bool drought = w != null && w.DroughtTicksRemaining > 0;
            if (season == Season.Summer || drought) m *= 1.8f;
            if (season == Season.Winter) m *= 0.5f;
            if (w != null && w.RainTicksRemaining > 0) m *= 0.2f;
            else if (IsWetWeather(w)) m *= 0.5f;
            return m;
        }

        /// <summary>Fuel dryness factor from tile moisture: 1 at 0.5 moisture, 1.2 when parched, 0.3 when soaked.</summary>
        public static float FuelMoistureFactor(float moisture) => MathUtil.Clamp(1.5f - MathUtil.Sanitize(moisture, 0.5f), 0.3f, 1.2f);

        /// <summary>True if the tile is land and carries fuel (Trees or a building).</summary>
        public static bool IsFlammable(WorldMap map, int index)
        {
            if (map == null || index < 0 || index >= map.TileCount || map.Biomes[index].IsWater()) return false;
            return (map.Flags[index] & TileFlags.Trees) != 0 || map.BuildingAt[index] >= 0;
        }

        /// <summary>Sets Burning on a land tile. Returns true if the tile was not burning before.</summary>
        public static bool Ignite(WorldMap map, int index)
        {
            if (map == null || index < 0 || index >= map.TileCount || map.Biomes[index].IsWater()) return false;
            if ((map.Flags[index] & TileFlags.Burning) != 0) return false;
            map.Flags[index] |= TileFlags.Burning;
            return true;
        }

        /// <summary>
        /// Ignites every land tile in a disc (Euclidean radius) and leaves an ignition hint so the fire
        /// system picks the new fire up on its next run. Returns the number of newly burning tiles.
        /// </summary>
        public static int IgniteDisc(WorldState state, Vec2i center, int radius, ref NatureDirtyRect dirty)
        {
            var map = state?.Map;
            if (map == null || !map.InBounds(center)) return 0;
            radius = MathUtil.Clamp(radius, 0, 32);
            int r2 = radius * radius, n = 0;
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                for (int x = center.X - radius; x <= center.X + radius; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - center.X, dy = y - center.Y;
                    if (dx * dx + dy * dy > r2) continue;
                    if (Ignite(map, map.Index(x, y))) { n++; dirty.Add(x, y); }
                }
            if (n > 0) SetFireHint(state, map.Index(center));
            return n;
        }

        /// <summary>Records the latest ignition site for the fire system (exact for maps up to 2^24 tiles).</summary>
        public static void SetFireHint(WorldState state, int tileIndex)
        {
            if (state == null || state.Map == null || tileIndex < 0 || tileIndex >= state.Map.TileCount) return;
            state.Memory[KeyFireHint] = tileIndex;
        }

        // ------------------------------------------------------------------ damage
        /// <summary>
        /// Reduces a building's Condition (never below 0) and destroys it through WorldOps at &lt;= 0.
        /// Returns true if the building was destroyed by this call.
        /// </summary>
        public static bool DamageBuilding(WorldOps ops, Building b, float amount, DestroyCause cause)
        {
            if (b == null || b.IsDestroyed) return false;
            amount = MathUtil.Sanitize(amount);
            if (amount <= 0f) return false;
            float c = MathUtil.Sanitize(b.Condition, 0f) - amount;
            b.Condition = c > 0f ? c : 0f;
            if (b.Condition <= 0f && ops != null)
            {
                ops.DestroyBuilding(b, cause);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Kills each living person whose position lies within the Euclidean radius with the given chance
        /// (deaths go through WorldOps). Iterates the people store by index. Returns the number killed.
        /// </summary>
        public static int KillPeopleNear(SimContext ctx, Vec2i center, int radius, float chance, DeathCause cause)
        {
            if (ctx?.State == null || ctx.Ops == null || radius < 0) return 0;
            chance = MathUtil.Clamp01(MathUtil.Sanitize(chance));
            if (chance <= 0f) return 0;
            var people = ctx.State.People.Items;
            int r2 = radius * radius, killed = 0;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p == null || !p.IsAlive) continue;
                if (p.Position.DistanceSqTo(center) > r2) continue;
                if (chance < 1f && !ctx.Rng.Chance(chance)) continue;
                ctx.Ops.KillPerson(p, cause);
                killed++;
            }
            return killed;
        }

        /// <summary>
        /// Kills each living resident of a settlement with the given chance. The id list is copied into
        /// <paramref name="buffer"/> first because deaths mutate Settlement.ResidentIds.
        /// </summary>
        public static int KillResidents(SimContext ctx, Settlement s, float chance, DeathCause cause, List<int> buffer)
        {
            if (ctx?.State == null || ctx.Ops == null || s == null) return 0;
            chance = MathUtil.Clamp01(MathUtil.Sanitize(chance));
            if (chance <= 0f) return 0;
            var ids = buffer ?? new List<int>();
            ids.Clear();
            ids.AddRange(s.ResidentIds);
            int killed = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = ctx.State.People.Get(ids[i]);
                if (p == null || !p.IsAlive) continue;
                if (chance < 1f && !ctx.Rng.Chance(chance)) continue;
                ctx.Ops.KillPerson(p, cause);
                killed++;
            }
            ids.Clear();
            return killed;
        }

        /// <summary>Nearest living settlement whose center is within maxDistance tiles (Euclidean), or null.</summary>
        public static Settlement NearestSettlement(WorldState state, Vec2i p, int maxDistance)
        {
            if (state == null) return null;
            var all = state.Settlements.Items;
            Settlement best = null;
            long bestD = (long)maxDistance * maxDistance;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.IsAlive) continue;
                long d = s.Center.DistanceSqTo(p);
                if (d <= bestD && (best == null || d < bestD)) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>True if any footprint tile of the building is burning.</summary>
        public static bool HasBurningTile(WorldMap map, Building b)
        {
            if (map == null || b == null) return false;
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    if (map.InBounds(x, y) && (map.Flags[map.Index(x, y)] & TileFlags.Burning) != 0) return true;
            return false;
        }

        /// <summary>Marks the map as changed and publishes one TerrainChangedEvent for the rectangle (if any).</summary>
        public static void PublishTerrainChanged(SimContext ctx, NatureDirtyRect dirty)
        {
            if (!dirty.Any || ctx?.State?.Map == null) return;
            ctx.State.Map.MarkTerrainChanged();
            ctx.Events?.Publish(dirty.ToEvent());
        }

        /// <summary>"1 building" / "3 buildings".</summary>
        public static string Count(int n, string singular, string plural) => n + " " + (n == 1 ? singular : plural);
    }
}
