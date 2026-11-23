using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    public static partial class GodPowerEffects
    {
        /// <summary>Elevation change at the center of a Raise/Lower Land cast (smooth radial falloff).</summary>
        public const float LandShapeAmount = 0.12f;

        /// <summary>Raise Land: +0.12 elevation with radial falloff; water may become Beach/Grassland, high land Hills/Mountain/Snow.</summary>
        public static GodPowerOutcome RaiseLand(SimContext ctx, Vec2i target, int radius) => ShapeLand(ctx, target, radius, LandShapeAmount);

        /// <summary>Lower Land: -0.12 elevation with radial falloff; land sinking below sea level floods and destroys what stood on it.</summary>
        public static GodPowerOutcome LowerLand(SimContext ctx, Vec2i target, int radius) => ShapeLand(ctx, target, radius, -LandShapeAmount);

        private static GodPowerOutcome ShapeLand(SimContext ctx, Vec2i target, int radius, float amount)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var r = GodPowerTerrain.Shape(ctx, target, radius, amount, Biome.ShallowWater);
            GodPowerTerrain.PublishChanged(ctx, target, radius + 1);
            string msg;
            if (amount > 0f) msg = r.NewLand > 0 ? Plural(r.NewLand, "tile of new land rises", "tiles of new land rise") + " from the waters." : "The earth swells upward.";
            else msg = r.NewWater > 0 ? "The sea claims " + Plural(r.NewWater, "tile", "tiles") + (r.BuildingsDestroyed > 0 ? " and " + Plural(r.BuildingsDestroyed, "building", "buildings") : "") + "." : "The earth sinks.";
            var o = GodPowerOutcome.Ok(msg);
            o.Affected = r.TilesChanged;
            o.TilesChanged = r.TilesChanged;
            o.BuildingsDestroyed = r.BuildingsDestroyed;
            return o;
        }

        /// <summary>Plant Forest: Trees and wood on free land in range; Grassland/Savanna become Forest, Tundra becomes Taiga.</summary>
        public static GodPowerOutcome PlantForest(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var map = ctx.State.Map;
            var rng = ctx.Rng;
            int planted = 0;
            const TileFlags blocked = TileFlags.Road | TileFlags.Wall | TileFlags.Farmland | TileFlags.Burning | GodPowerTerrain.DepositFlags;
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    var b = map.Biomes[i];
                    if (!b.IsBuildable() || b == Biome.Beach || b == Biome.Desert) continue;
                    if (map.BuildingAt[i] >= 0 || (map.Flags[i] & blocked) != 0) continue;
                    map.Set(i, TileFlags.Trees);
                    map.Clear(i, TileFlags.Scorched);
                    map.Resource[i] = Math.Max(MathUtil.Sanitize(map.Resource[i]), rng.Range(60f, 100f));
                    if (b == Biome.Grassland || b == Biome.Savanna) map.Biomes[i] = Biome.Forest;
                    else if (b == Biome.Tundra) map.Biomes[i] = Biome.Taiga;
                    planted++;
                }
            if (planted > 0) GodPowerTerrain.PublishChanged(ctx, target, radius);
            var o = GodPowerOutcome.Ok(planted > 0 ? "A forest of " + Plural(planted, "tile", "tiles") + " springs up." : "No open ground would take the seeds.");
            o.Affected = planted;
            o.TilesChanged = planted;
            return o;
        }

        /// <summary>Reveal Ore: 3-8 free land tiles in range (hills and mountains preferred) gain Ore, Stone or Gold deposits.</summary>
        public static GodPowerOutcome RevealOre(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var map = ctx.State.Map;
            var rng = ctx.Rng;
            const TileFlags blocked = TileFlags.Road | TileFlags.Wall | TileFlags.Farmland | GodPowerTerrain.DepositFlags;
            var candidates = new List<int>();
            var scores = new List<float>();
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    var b = map.Biomes[i];
                    if (b.IsWater() || b == Biome.Snow || map.BuildingAt[i] >= 0 || (map.Flags[i] & blocked) != 0) continue;
                    float score = (b == Biome.Hills || b == Biome.Mountain ? 2f : 0f) + MathUtil.Clamp(MathUtil.Sanitize(map.Elevation[i]), -1f, 1f);
                    if (map.Has(i, TileFlags.Trees)) score -= 1f;
                    score += rng.NextFloat() * 0.75f;
                    candidates.Add(i);
                    scores.Add(score);
                }
            if (candidates.Count == 0) return GodPowerOutcome.Ok("The earth here holds no secrets to reveal.");

            var order = new List<int>(candidates.Count);
            for (int k = 0; k < candidates.Count; k++) order.Add(k);
            order.Sort((a, b) => { int c = scores[b].CompareTo(scores[a]); return c != 0 ? c : candidates[a].CompareTo(candidates[b]); });

            int count = Math.Min(candidates.Count, rng.RangeInclusive(3, 8));
            int ore = 0, stone = 0, gold = 0;
            for (int k = 0; k < count; k++)
            {
                int i = candidates[order[k]];
                bool high = map.Biomes[i] == Biome.Hills || map.Biomes[i] == Biome.Mountain;
                float roll = rng.NextFloat();
                map.Clear(i, TileFlags.Trees);
                if (roll < (high ? 0.25f : 0.15f)) { map.Set(i, TileFlags.GoldDeposit); map.Resource[i] = rng.Range(60f, 120f); gold++; }
                else if (roll < (high ? 0.70f : 0.50f)) { map.Set(i, TileFlags.OreDeposit); map.Resource[i] = rng.Range(120f, 240f); ore++; }
                else { map.Set(i, TileFlags.StoneDeposit); map.Resource[i] = rng.Range(160f, 320f); stone++; }
            }
            GodPowerTerrain.PublishChanged(ctx, target, radius);
            var o = GodPowerOutcome.Ok("The hills open to reveal " + ore + " ore, " + stone + " stone and " + gold + " gold deposits.");
            o.Affected = count;
            o.TilesChanged = count;
            return o;
        }

        /// <summary>
        /// Spawn Tribe: a new civilization of <paramref name="race"/> with a founded settlement at target and
        /// Config.InitialPopulationPerRace people of alternating sex aged 14-40 (scaled for slow-maturing races).
        /// The new people start devout (Devotion 0.9, GodFavor 0.5). Fails without changes when
        /// <see cref="CanSpawnTribe"/> rejects the site.
        /// </summary>
        public static GodPowerOutcome SpawnTribe(SimContext ctx, Vec2i target, RaceId race)
        {
            int radius = 0;
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            if (!CanSpawnTribe(ctx, target, (int)race, out string reason)) return GodPowerOutcome.Fail(reason);

            var state = ctx.State;
            var def = ctx.Db.Race(race);
            var civ = ctx.Ops.CreateCivilization(race);
            var s = ctx.Ops.FoundSettlement(civ, target);
            civ.Devotion = 0.9f;
            civ.GodFavor = 0.5f;
            civ.Memory["divine.spawnedTick"] = state.Tick;

            int n = ctx.Config != null ? MathUtil.Clamp(ctx.Config.InitialPopulationPerRace, 2, 200) : 14;
            float scale = Math.Max(1f, MathUtil.Sanitize(def.AdulthoodAge, 16f) / 16f);
            float minAge = 14f * scale, maxAge = 40f * scale;
            float elder = MathUtil.Sanitize(def.ElderAge, 55f);
            if (elder > minAge + 1f) maxAge = Math.Min(maxAge, elder - 0.5f);
            int created = 0;
            for (int i = 0; i < n; i++)
            {
                var sex = (i % 2 == 0) ? Sex.Female : Sex.Male;
                var p = ctx.Ops.CreatePerson(s, race, sex, ctx.Rng.Range(minAge, maxAge));
                p.Faith = Math.Max(p.Faith, 0.7f);
                created++;
            }
            var map = state.Map;
            map.Set(map.Index(target), TileFlags.Sacred);
            GodPowerTerrain.PublishChanged(ctx, target, 0);

            var o = GodPowerOutcome.Ok("The " + civ.Name + " awaken at " + s.Name + ".");
            o.Affected = created;
            o.Created = created;
            o.CivilizationId = civ.Id;
            o.SettlementId = s.Id;
            o.ChronicleText = "By divine will, the " + civ.Name + " of the " + def.PluralName + " were shaped from the earth at " + s.Name + ".";
            o.ChronicleImportance = 3;
            return o;
        }
    }
}
