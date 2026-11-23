using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// Natural disasters. Runs every 24 ticks, only when <see cref="SimConfig.EnableDisasters"/> is set and
    /// <see cref="SimConfig.DisasterFrequency"/> &gt; 0. Each living civilization is a "region": per run it rolls
    /// one chance per disaster kind (annual rates below, scaled by DisasterFrequency, seasonal where it
    /// matters, softened for a few years after the civ was last struck). On a hit, a deterministic site is
    /// searched around the civ's settlements (starting from a random one). With every kind applicable a civ
    /// sees about one notable disaster per ~15 years at frequency 1, a typical civ (2-3 applicable kinds) one
    /// per ~20-30 years. A few rarer wilderness events (volcanoes, forest fires) keep the wider world alive.
    /// <list type="bullet">
    /// <item>Wildfire (Summer or drought): Trees tile near a settlement (or a random forest tile in the
    /// wilds); Burning in radius 1-2, then <see cref="FireSystem"/> takes over.</item>
    /// <item>Earthquake: epicenter on Mountain/Volcanic within 20 tiles of a settlement; buildings lose
    /// 0.2-0.6 Condition with distance falloff (destroyed at &lt;= 0), 0-2% of nearby people die.</item>
    /// <item>Flood (Spring): river tile near a settlement; low tiles lose Farmland, buildings on them are
    /// damaged, fires are doused, silt adds a little fertility; 1% of the settlement drowns.</item>
    /// <item>Volcano: a Volcanic tile; Scorched + Burning around the vent, buildings within 3 destroyed,
    /// people within 4 killed, ash damages buildings within 10, the vent rises slightly.</item>
    /// <item>Storm: a coastal settlement; its buildings lose 0.1-0.3 Condition, StormIntensity = 1.</item>
    /// </list>
    /// Every disaster publishes a <see cref="DisasterEvent"/> and adds an importance-2 Disaster chronicle
    /// entry. Terrain edits publish a <see cref="TerrainChangedEvent"/>. WorldStats is not touched (beyond
    /// what WorldOps does for deaths and destroyed buildings).
    /// </summary>
    public sealed class DisasterSystem : ISimSystem
    {
        /// <summary>Per-civ annual wildfire rate at frequency 1 (rolled only in Summer or drought).</summary>
        public const float WildfirePerYear = 0.016f;
        /// <summary>Per-civ annual earthquake rate at frequency 1.</summary>
        public const float EarthquakePerYear = 0.012f;
        /// <summary>Per-civ annual flood rate at frequency 1 (rolled only in Spring).</summary>
        public const float FloodPerYear = 0.016f;
        /// <summary>Per-civ annual volcano rate at frequency 1.</summary>
        public const float VolcanoPerYear = 0.006f;
        /// <summary>Per-civ annual storm rate at frequency 1 (weighted toward Autumn/Winter).</summary>
        public const float StormPerYear = 0.016f;
        /// <summary>World-wide annual rate of a volcano erupting on a random Volcanic tile.</summary>
        public const float WildVolcanoPerYear = 0.02f;
        /// <summary>World-wide annual rate of a wildfire in a random forest (Summer or drought only).</summary>
        public const float WildWildfirePerYear = 0.03f;
        /// <summary>Years after a disaster during which further disasters in that civ are 4x less likely (at frequency 1).</summary>
        public const float CooldownYears = 3f;
        /// <summary>Earthquake epicenters must lie within this distance of the struck settlement.</summary>
        public const int EarthquakeSearchRadius = 20;
        /// <summary>A volcano counts as "near" a settlement within this distance.</summary>
        public const int VolcanoSearchRadius = 24;
        /// <summary>Buildings within this distance of an erupting vent are destroyed.</summary>
        public const int VolcanoDestroyRadius = 3;
        /// <summary>People within this distance of an erupting vent are killed.</summary>
        public const int VolcanoKillRadius = 4;
        /// <summary>Volcanic ash damages buildings within this distance of the vent.</summary>
        public const int VolcanoAshRadius = 10;

        private enum SiteKind : byte { Trees, Quake, River, Volcanic, Coast }

        private readonly List<int> _candidates = new List<int>();
        private readonly List<int> _volcanic = new List<int>();
        private readonly List<Settlement> _alive = new List<Settlement>();
        private readonly List<int> _idBuffer = new List<int>();
        private readonly HashSet<int> _touched = new HashSet<int>();
        private WorldMap _volcanicMap;
        private int _runsSinceVolcanicScan;

        /// <inheritdoc />
        public string Name => "Divine.Disasters";

        /// <inheritdoc />
        public int TickInterval => 24;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            if (ctx?.State?.Map == null) return;
            RebuildVolcanicCache(ctx.State.Map);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            var state = ctx?.State;
            var cfg = ctx?.Config;
            if (state?.Map == null || cfg == null || ctx.Ops == null || !cfg.EnableDisasters) return;
            float freq = MathUtil.Clamp(MathUtil.Sanitize(cfg.DisasterFrequency), 0f, 10f);
            if (freq <= 0f) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            var rng = ctx.Rng;
            long tick = state.Tick;
            var season = SimTime.SeasonOf(tick);
            bool drought = state.Weather.DroughtTicksRemaining > 0;
            bool fireSeason = season == Season.Summer || drought;

            if (!ReferenceEquals(_volcanicMap, state.Map) || ++_runsSinceVolcanicScan >= 40) RebuildVolcanicCache(state.Map);

            long cooldownTicks = SimTime.YearsToTicks(CooldownYears / freq);
            var civs = state.Civilizations.Items;
            for (int ci = 0; ci < civs.Count; ci++)
            {
                var civ = civs[ci];
                if (civ == null || !civ.IsAlive || civ.SettlementIds.Count == 0) continue;
                float last = civ.Mem(NaturalEffects.KeyLastDisasterTick, float.NaN);
                bool recent = !float.IsNaN(last) && !float.IsInfinity(last) && last <= tick && tick - (long)last < cooldownTicks;
                float f = freq * (recent ? 0.25f : 1f);
                if (!RollKind(rng, season, fireSeason, f, dt, out var kind)) continue;
                float severity = rng.Range(0.35f, 1f);
                TryStrikeCivilization(ctx, civ, kind, severity);
            }

            // Wilderness events: the world lives on beyond the borders.
            if (_volcanic.Count > 0 && rng.Chance(Rng.PerYearToPerTicks(WildVolcanoPerYear * freq, dt)))
            {
                int vent = _volcanic[rng.Range(0, _volcanic.Count)];
                if (state.Map.Biomes[vent] == Biome.Volcanic)
                {
                    var v = state.Map.PosOf(vent);
                    Eruption(ctx, vent, rng.Range(0.35f, 1f), NaturalEffects.NearestSettlement(state, v, VolcanoSearchRadius));
                }
            }
            if (fireSeason && rng.Chance(Rng.PerYearToPerTicks(Math.Min(1f, WildWildfirePerYear * freq * 4f), dt)))
            {
                int site = SampleForestTile(state.Map, rng, 64);
                if (site >= 0)
                {
                    var p = state.Map.PosOf(site);
                    Wildfire(ctx, site, rng.Range(0.35f, 1f), NaturalEffects.NearestSettlement(state, p, 16));
                }
            }
        }

        /// <summary>
        /// Forces a natural disaster of the given kind on a settlement (dev tools, scripted events, tests),
        /// ignoring season, DisasterFrequency and EnableDisasters. Supported kinds: Wildfire, Earthquake,
        /// Flood, Volcano, Storm. With a null settlement only Volcano and Wildfire are possible (random
        /// wilderness site). Returns false when the kind is unsupported or no valid site exists.
        /// </summary>
        public bool Trigger(SimContext ctx, DisasterType type, Settlement settlement)
        {
            var state = ctx?.State;
            if (state?.Map == null || ctx.Ops == null) return false;
            if (!ReferenceEquals(_volcanicMap, state.Map)) RebuildVolcanicCache(state.Map);
            float severity = ctx.Rng.Range(0.35f, 1f);
            if (settlement == null || !settlement.IsAlive)
            {
                if (settlement != null) return false;
                if (type == DisasterType.Volcano && _volcanic.Count > 0)
                {
                    int vent = _volcanic[ctx.Rng.Range(0, _volcanic.Count)];
                    if (state.Map.Biomes[vent] != Biome.Volcanic) return false;
                    Eruption(ctx, vent, severity, NaturalEffects.NearestSettlement(state, state.Map.PosOf(vent), VolcanoSearchRadius));
                    return true;
                }
                if (type == DisasterType.Wildfire)
                {
                    int site = SampleForestTile(state.Map, ctx.Rng, 256);
                    if (site < 0) return false;
                    Wildfire(ctx, site, severity, NaturalEffects.NearestSettlement(state, state.Map.PosOf(site), 16));
                    return true;
                }
                return false;
            }
            bool ok = Strike(ctx, type, settlement, severity);
            if (ok) RecordStrike(state.Civilizations.Get(settlement.CivilizationId), state.Tick);
            return ok;
        }

        // ------------------------------------------------------------------ rolling
        private static bool RollKind(Rng rng, Season season, bool fireSeason, float f, int dt, out DisasterType kind)
        {
            kind = DisasterType.Storm;
            if (rng.Chance(Rng.PerYearToPerTicks(VolcanoPerYear * f, dt))) { kind = DisasterType.Volcano; return true; }
            if (rng.Chance(Rng.PerYearToPerTicks(EarthquakePerYear * f, dt))) { kind = DisasterType.Earthquake; return true; }
            if (season == Season.Spring && rng.Chance(Rng.PerYearToPerTicks(Math.Min(1f, FloodPerYear * f * 4f), dt))) { kind = DisasterType.Flood; return true; }
            if (fireSeason && rng.Chance(Rng.PerYearToPerTicks(Math.Min(1f, WildfirePerYear * f * 4f), dt))) { kind = DisasterType.Wildfire; return true; }
            float stormWeight = season == Season.Autumn ? 1.4f : season == Season.Winter ? 1.2f : season == Season.Summer ? 0.8f : 0.6f;
            if (rng.Chance(Rng.PerYearToPerTicks(Math.Min(1f, StormPerYear * f * stormWeight), dt))) { kind = DisasterType.Storm; return true; }
            return false;
        }

        private void TryStrikeCivilization(SimContext ctx, Civilization civ, DisasterType kind, float severity)
        {
            var state = ctx.State;
            _alive.Clear();
            for (int i = 0; i < civ.SettlementIds.Count; i++)
            {
                var s = state.Settlements.Get(civ.SettlementIds[i]);
                if (s != null && s.IsAlive) _alive.Add(s);
            }
            int count = _alive.Count;
            if (count == 0) return;
            int offset = ctx.Rng.Range(0, count);
            for (int k = 0; k < count; k++)
            {
                var s = _alive[(offset + k) % count];
                if (!Strike(ctx, kind, s, severity)) continue;
                RecordStrike(civ, state.Tick);
                break;
            }
            _alive.Clear();
        }

        private static void RecordStrike(Civilization civ, long tick)
        {
            if (civ == null) return;
            civ.Memory[NaturalEffects.KeyLastDisasterTick] = tick;
            civ.Memory[NaturalEffects.KeyDisasterCount] = civ.Mem(NaturalEffects.KeyDisasterCount) + 1f;
        }

        private bool Strike(SimContext ctx, DisasterType kind, Settlement s, float severity)
        {
            var map = ctx.State.Map;
            int site;
            switch (kind)
            {
                case DisasterType.Wildfire:
                    site = PickSite(ctx, map, s.Center, MathUtil.Clamp(s.TerritoryRadius + 8, 8, 24), SiteKind.Trees);
                    if (site < 0) return false;
                    Wildfire(ctx, site, severity, s);
                    return true;
                case DisasterType.Earthquake:
                    site = PickSite(ctx, map, s.Center, EarthquakeSearchRadius, SiteKind.Quake);
                    if (site < 0) return false;
                    Earthquake(ctx, site, severity, s);
                    return true;
                case DisasterType.Flood:
                    site = PickSite(ctx, map, s.Center, MathUtil.Clamp(s.TerritoryRadius + 4, 6, 16), SiteKind.River);
                    if (site < 0) return false;
                    Flood(ctx, site, severity, s);
                    return true;
                case DisasterType.Volcano:
                    site = PickSite(ctx, map, s.Center, VolcanoSearchRadius, SiteKind.Volcanic);
                    if (site < 0) return false;
                    Eruption(ctx, site, severity, s);
                    return true;
                case DisasterType.Storm:
                    if (!IsCoastal(map, s)) return false;
                    Storm(ctx, severity, s);
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ site selection
        private int PickSite(SimContext ctx, WorldMap map, Vec2i c, int radius, SiteKind kind)
        {
            _candidates.Clear();
            int r2 = radius * radius;
            for (int y = c.Y - radius; y <= c.Y + radius; y++)
                for (int x = c.X - radius; x <= c.X + radius; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - c.X, dy = y - c.Y;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    if (Matches(map, i, kind)) _candidates.Add(i);
                }
            int pick = _candidates.Count == 0 ? -1 : _candidates[ctx.Rng.Range(0, _candidates.Count)];
            _candidates.Clear();
            return pick;
        }

        private static bool Matches(WorldMap map, int i, SiteKind kind)
        {
            var b = map.Biomes[i];
            var f = map.Flags[i];
            switch (kind)
            {
                case SiteKind.Trees: return !b.IsWater() && (f & TileFlags.Trees) != 0 && (f & TileFlags.Burning) == 0;
                case SiteKind.Quake: return b == Biome.Mountain || b == Biome.Volcanic;
                case SiteKind.River: return !b.IsWater() && (f & TileFlags.River) != 0;
                case SiteKind.Volcanic: return b == Biome.Volcanic;
                case SiteKind.Coast: return (f & TileFlags.Coastal) != 0 || b == Biome.DeepOcean || b == Biome.ShallowWater;
                default: return false;
            }
        }

        private static bool IsCoastal(WorldMap map, Settlement s)
        {
            int r = MathUtil.Clamp(s.TerritoryRadius, 3, 10);
            for (int y = s.Center.Y - r; y <= s.Center.Y + r; y++)
                for (int x = s.Center.X - r; x <= s.Center.X + r; x++)
                    if (map.InBounds(x, y) && Matches(map, map.Index(x, y), SiteKind.Coast)) return true;
            return false;
        }

        private static int SampleForestTile(WorldMap map, Rng rng, int tries)
        {
            int n = map.TileCount;
            for (int t = 0; t < tries; t++)
            {
                int i = rng.Range(0, n);
                if (Matches(map, i, SiteKind.Trees)) return i;
            }
            return -1;
        }

        private void RebuildVolcanicCache(WorldMap map)
        {
            _volcanic.Clear();
            _volcanicMap = map;
            _runsSinceVolcanicScan = 0;
            if (map == null) return;
            for (int i = 0; i < map.TileCount; i++) if (map.Biomes[i] == Biome.Volcanic) _volcanic.Add(i);
        }

        // ------------------------------------------------------------------ effects
        private void Wildfire(SimContext ctx, int site, float severity, Settlement near)
        {
            var state = ctx.State;
            var c = state.Map.PosOf(site);
            int radius = severity >= 0.65f ? 2 : 1;
            var dirty = new NatureDirtyRect();
            NaturalEffects.IgniteDisc(state, c, radius, ref dirty);
            Publish(ctx, DisasterType.Wildfire, c, radius, severity);
            NaturalEffects.PublishTerrainChanged(ctx, dirty);
            string text = near != null
                ? Pick(ctx.Rng, "Wildfire roared out of the forests near " + near.Name + ".", "The woods beyond " + near.Name + " went up in flames.")
                : "A wildfire raged through the untamed forests.";
            Chronicle(ctx, text, near, c);
        }

        private void Earthquake(SimContext ctx, int site, float severity, Settlement s)
        {
            var state = ctx.State;
            var epi = state.Map.PosOf(site);
            float dist = epi.DistanceTo(s.Center);
            int radius = MathUtil.Clamp(MathUtil.RoundToInt(dist) + 8, 8, 28);
            float baseLoss = MathUtil.Lerp(0.2f, 0.6f, severity);
            int destroyed = 0, damaged = 0;
            var buildings = state.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null || b.IsDestroyed) continue;
                float d = b.Center.DistanceTo(epi);
                if (d > radius) continue;
                float falloff = 1f - 0.6f * d / radius;
                float loss = MathUtil.Clamp(baseLoss * ctx.Rng.Range(0.85f, 1.15f), 0.2f, 0.6f) * falloff;
                damaged++;
                if (NaturalEffects.DamageBuilding(ctx.Ops, b, loss, DestroyCause.Disaster)) destroyed++;
            }
            int killed = NaturalEffects.KillPeopleNear(ctx, epi, radius, ctx.Rng.Range(0f, 0.02f * severity), DeathCause.Disaster);
            Publish(ctx, DisasterType.Earthquake, epi, radius, severity);
            string text = "The earth split beneath " + s.Name + ".";
            if (destroyed > 0) text += " " + NaturalEffects.Count(destroyed, "building", "buildings") + " fell.";
            if (killed > 0) text += " " + NaturalEffects.Count(killed, "soul", "souls") + " perished in the rubble.";
            if (destroyed == 0 && killed == 0 && damaged > 0) text += " Walls cracked, but the town endured.";
            Chronicle(ctx, text, s, epi);
        }

        private void Flood(SimContext ctx, int site, float severity, Settlement s)
        {
            var state = ctx.State;
            var map = state.Map;
            var c = map.PosOf(site);
            int radius = 3 + MathUtil.RoundToInt(severity * 4f);
            float crest = map.Elevation[site] + 0.06f;
            int r2 = radius * radius, fields = 0, destroyed = 0;
            var dirty = new NatureDirtyRect();
            _touched.Clear();
            for (int y = c.Y - radius; y <= c.Y + radius; y++)
                for (int x = c.X - radius; x <= c.X + radius; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - c.X, dy = y - c.Y;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    if (map.Biomes[i].IsWater()) continue;
                    var f = map.Flags[i];
                    if ((f & TileFlags.River) == 0 && !(map.Elevation[i] <= crest)) continue;
                    if ((f & TileFlags.Farmland) != 0) fields++;
                    map.Flags[i] = f & ~(TileFlags.Farmland | TileFlags.Burning);
                    map.Moisture[i] = MathUtil.Clamp01(MathUtil.Sanitize(map.Moisture[i]) + 0.15f);
                    map.Fertility[i] = MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i]) + 0.03f);
                    dirty.Add(x, y);
                    int bid = map.BuildingAt[i];
                    if (bid < 0 || !_touched.Add(bid)) continue;
                    var b = state.Buildings.Get(bid);
                    if (b == null || b.IsDestroyed) continue;
                    if (NaturalEffects.DamageBuilding(ctx.Ops, b, ctx.Rng.Range(0.1f, 0.35f) * (0.6f + 0.4f * severity), DestroyCause.Disaster)) destroyed++;
                }
            _touched.Clear();
            int drowned = NaturalEffects.KillResidents(ctx, s, 0.01f, DeathCause.Disaster, _idBuffer);
            Publish(ctx, DisasterType.Flood, c, radius, severity);
            NaturalEffects.PublishTerrainChanged(ctx, dirty);
            string text = Pick(ctx.Rng, "The river burst its banks and swallowed the fields of " + s.Name + ".", "Spring floods rose over " + s.Name + ".");
            if (fields > 0) text += " " + NaturalEffects.Count(fields, "field was", "fields were") + " buried in silt.";
            if (destroyed > 0) text += " " + NaturalEffects.Count(destroyed, "building was", "buildings were") + " swept away.";
            if (drowned > 0) text += " " + NaturalEffects.Count(drowned, "soul was", "souls were") + " lost to the waters.";
            Chronicle(ctx, text, s, c);
        }

        private void Eruption(SimContext ctx, int vent, float severity, Settlement near)
        {
            var state = ctx.State;
            var map = state.Map;
            var v = map.PosOf(vent);
            int lava = severity >= 0.7f ? 3 : 2;
            var dirty = new NatureDirtyRect();
            int l2 = lava * lava;
            for (int y = v.Y - lava; y <= v.Y + lava; y++)
                for (int x = v.X - lava; x <= v.X + lava; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - v.X, dy = y - v.Y;
                    if (dx * dx + dy * dy > l2) continue;
                    int i = map.Index(x, y);
                    if (map.Biomes[i].IsWater()) continue;
                    var f = map.Flags[i];
                    if ((f & TileFlags.Trees) != 0) map.Resource[i] = 0f;
                    map.Flags[i] = (f & ~(TileFlags.Trees | TileFlags.Farmland)) | TileFlags.Scorched | TileFlags.Burning;
                    dirty.Add(x, y);
                }
            NaturalEffects.SetFireHint(state, vent);

            // Buildings touching the inner blast radius are destroyed outright; ash damages the rest.
            int destroyed = 0;
            int r = VolcanoDestroyRadius, rr = r * r;
            for (int y = v.Y - r; y <= v.Y + r; y++)
                for (int x = v.X - r; x <= v.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - v.X, dy = y - v.Y;
                    if (dx * dx + dy * dy > rr) continue;
                    int bid = map.BuildingAt[map.Index(x, y)];
                    if (bid < 0) continue;
                    var b = state.Buildings.Get(bid);
                    if (b == null || b.IsDestroyed) continue;
                    ctx.Ops.DestroyBuilding(b, DestroyCause.Disaster);
                    destroyed++;
                }
            var buildings = state.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null || b.IsDestroyed) continue;
                float d = b.Center.DistanceTo(v);
                if (d > VolcanoAshRadius) continue;
                float loss = (0.1f + 0.2f * severity) * (1f - d / (VolcanoAshRadius + 1f));
                if (NaturalEffects.DamageBuilding(ctx.Ops, b, loss, DestroyCause.Disaster)) destroyed++;
            }
            int killed = NaturalEffects.KillPeopleNear(ctx, v, VolcanoKillRadius, 1f, DeathCause.Disaster);

            // The vent builds up.
            float rise = 0.02f + 0.03f * severity;
            for (int y = v.Y - 1; y <= v.Y + 1; y++)
                for (int x = v.X - 1; x <= v.X + 1; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    float add = (x == v.X && y == v.Y) ? rise : rise * 0.4f;
                    map.Elevation[i] = MathUtil.Clamp(MathUtil.Sanitize(map.Elevation[i]) + add, -1f, 1f);
                }
            dirty.AddSquare(map, v, 1);

            Publish(ctx, DisasterType.Volcano, v, VolcanoKillRadius, severity);
            NaturalEffects.PublishTerrainChanged(ctx, dirty);
            string text = near != null
                ? Pick(ctx.Rng, "The mountain above " + near.Name + " awoke, and fire rained from the sky.", "A volcano tore open near " + near.Name + ", burying the land in ash.")
                : "A volcano erupted in the wilds, and ash darkened the sky for a season.";
            if (destroyed > 0) text += " " + NaturalEffects.Count(destroyed, "building was", "buildings were") + " lost.";
            if (killed > 0) text += " " + NaturalEffects.Count(killed, "soul", "souls") + " perished in the fire.";
            Chronicle(ctx, text, near, v);
        }

        private void Storm(SimContext ctx, float severity, Settlement s)
        {
            var state = ctx.State;
            int destroyed = 0;
            var buildings = state.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null || b.IsDestroyed || b.SettlementId != s.Id) continue;
                if (NaturalEffects.DamageBuilding(ctx.Ops, b, ctx.Rng.Range(0.1f, 0.3f), DestroyCause.Disaster)) destroyed++;
            }
            var w = state.Weather;
            w.StormIntensity = 1f;
            w.Rainfall = MathUtil.Clamp(MathUtil.Sanitize(w.Rainfall, 1f) + 0.1f, NaturalEffects.MinRainfall, NaturalEffects.MaxRainfall);
            Publish(ctx, DisasterType.Storm, s.Center, Math.Max(1, s.TerritoryRadius), severity);
            string text = Pick(ctx.Rng, "A great storm battered the coast of " + s.Name + ".", "Howling winds and black seas lashed " + s.Name + ".");
            if (destroyed > 0) text += " " + NaturalEffects.Count(destroyed, "building was", "buildings were") + " torn apart.";
            Chronicle(ctx, text, s, s.Center);
        }

        // ------------------------------------------------------------------ reporting
        private static void Publish(SimContext ctx, DisasterType type, Vec2i at, int radius, float severity)
        {
            ctx.Events?.Publish(new DisasterEvent { Type = type, Position = at, Radius = radius, Severity = MathUtil.Clamp01(MathUtil.Sanitize(severity)) });
        }

        private static void Chronicle(SimContext ctx, string text, Settlement s, Vec2i at)
        {
            ctx.Ops?.AddChronicle(ChronicleCategory.Disaster, text, s != null ? s.CivilizationId : -1, s != null ? s.Id : -1, at, 2);
        }

        private static string Pick(Rng rng, string a, string b) => rng.Chance(0.5f) ? a : b;
    }
}
