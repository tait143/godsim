using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    public static partial class GodPowerEffects
    {
        /// <summary>
        /// Lightning: kills 1-2 of the people closest to the strike (within radius + 1) by divine wrath, and sets the
        /// building on the target tile on fire (tile flagged Burning). Fire spread is handled by the nature module.
        /// </summary>
        public static GodPowerOutcome Lightning(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var map = state.Map;
            var people = new List<Person>();
            GodPowerArea.PeopleInRange(state, target, radius + 1, people);
            people.Sort((a, b) => { int c = a.Position.DistanceSqTo(target).CompareTo(b.Position.DistanceSqTo(target)); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            int toKill = Math.Min(people.Count, ctx.Rng.RangeInclusive(1, 2));
            for (int i = 0; i < toKill; i++) ctx.Ops.KillPerson(people[i], DeathCause.DivineWrath);

            int ti = map.Index(target);
            bool ignited = false;
            if (!map.IsWater(ti))
            {
                map.Set(ti, TileFlags.Burning);
                ignited = true;
                int bid = map.BuildingAt[ti];
                var b = bid >= 0 ? state.Buildings.Get(bid) : null;
                if (b != null && !b.IsDestroyed) b.IsOnFire = true;
                GodPowerTerrain.PublishChanged(ctx, target, 0);
            }
            PublishDisaster(ctx, DisasterType.Lightning, target, radius, 0.2f);
            var o = GodPowerOutcome.Ok(toKill > 0 ? "Lightning strikes down " + Plural(toKill, "mortal", "mortals") + "." : (ignited ? "Lightning scorches the ground." : "Lightning hisses into the water."));
            o.Affected = toKill;
            o.Killed = toKill;
            return o;
        }

        /// <summary>Wildfire: Trees and flammable land in range catch fire (Burning); every building in range is set on fire.</summary>
        public static GodPowerOutcome Wildfire(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var map = state.Map;
            var rng = ctx.Rng;
            int burning = 0;
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    var b = map.Biomes[i];
                    if (b.IsWater()) continue;
                    bool fuel = map.Has(i, TileFlags.Trees) || map.BuildingAt[i] >= 0;
                    bool flammable = b != Biome.Snow && b != Biome.Mountain && b != Biome.Desert && b != Biome.Beach && b != Biome.Volcanic;
                    if (fuel || (flammable && rng.Chance(0.6f)))
                    {
                        map.Set(i, TileFlags.Burning);
                        burning++;
                    }
                }
            var buildings = new List<Building>();
            GodPowerArea.BuildingsInRange(state, target, radius, buildings);
            for (int i = 0; i < buildings.Count; i++) buildings[i].IsOnFire = true;
            if (burning > 0) GodPowerTerrain.PublishChanged(ctx, target, radius);
            PublishDisaster(ctx, DisasterType.Wildfire, target, radius, 0.6f);
            var o = GodPowerOutcome.Ok("Flames engulf " + Plural(burning, "tile", "tiles") + (buildings.Count > 0 ? " and " + Plural(buildings.Count, "building", "buildings") : "") + ".");
            o.Affected = burning + buildings.Count;
            o.TilesChanged = burning;
            return o;
        }

        /// <summary>
        /// Earthquake: buildings in range lose 0.3-0.8 condition (less toward the rim) and are destroyed at 0;
        /// each person in range dies with up to 3% chance (Disaster). Chronicled.
        /// </summary>
        public static GodPowerOutcome Earthquake(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var rng = ctx.Rng;
            var buildings = new List<Building>();
            GodPowerArea.BuildingsInRange(state, target, radius, buildings);
            int destroyed = 0;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                var c = b.Center;
                float falloff = 1f - 0.5f * GodPowerArea.NormalizedDistance(target, c.X, c.Y, radius);
                if (DamageBuilding(ctx, b, rng.Range(0.3f, 0.8f) * falloff)) destroyed++;
            }
            var people = new List<Person>();
            GodPowerArea.PeopleInRange(state, target, radius, people);
            int killed = 0;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!p.IsAlive) continue;
                float falloff = 1f - 0.5f * GodPowerArea.NormalizedDistance(target, p.Position.X, p.Position.Y, radius);
                if (rng.Chance(0.03f * falloff)) { ctx.Ops.KillPerson(p, DeathCause.Disaster); killed++; }
            }
            PublishDisaster(ctx, DisasterType.Earthquake, target, radius, 0.7f);

            string place = GodPowerArea.PlaceName(state, target, radius + ChronicleNameReach, out var near);
            var o = GodPowerOutcome.Ok("The earth heaves: " + Plural(destroyed, "building falls", "buildings fall") + ", " + Plural(killed, "life is", "lives are") + " lost.");
            o.Affected = buildings.Count;
            o.BuildingsDestroyed = destroyed;
            o.Killed = killed;
            FillPlace(o, near);
            o.ChronicleText = "The earth shook beneath " + place + (destroyed > 0 ? ", toppling " + Plural(destroyed, "building", "buildings") : "") + ".";
            o.ChronicleImportance = 2;
            return o;
        }

        /// <summary>Crater depth at the impact point.</summary>
        public const float MeteorCraterDepth = 0.15f;

        /// <summary>
        /// Meteor: a crater (elevation lowered in the core of radius/2, may fill as a lake), Scorched land in range,
        /// buildings in the core destroyed and the rest damaged and burning, everyone in range killed (DivineWrath),
        /// and a 50% chance of an Ore or Gold deposit in the crater. Chronicled.
        /// </summary>
        public static GodPowerOutcome Meteor(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var map = state.Map;
            var rng = ctx.Rng;
            int core = Math.Max(1, radius / 2);
            radius = Math.Max(radius, core);
            string place = GodPowerArea.PlaceName(state, target, radius + ChronicleNameReach, out var near);

            int killed = 0;
            var people = new List<Person>();
            GodPowerArea.PeopleInRange(state, target, radius, people);
            for (int i = 0; i < people.Count; i++)
                if (people[i].IsAlive) { ctx.Ops.KillPerson(people[i], DeathCause.DivineWrath); killed++; }

            int destroyed = 0;
            var buildings = new List<Building>();
            GodPowerArea.BuildingsInRange(state, target, radius, buildings);
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b.IsDestroyed) continue;
                if (BuildingTouchesCircle(b, target, core)) { ctx.Ops.DestroyBuilding(b, DestroyCause.DivinePower); destroyed++; }
                else
                {
                    b.IsOnFire = true;
                    if (DamageBuilding(ctx, b, rng.Range(0.2f, 0.5f))) destroyed++;
                }
            }

            var shape = GodPowerTerrain.Shape(ctx, target, core, -MeteorCraterDepth, Biome.Lake);
            destroyed += shape.BuildingsDestroyed;

            int scorched = 0;
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    if (map.IsWater(i)) continue;
                    map.Set(i, TileFlags.Scorched);
                    scorched++;
                    if (GodPowerArea.InCircle(target, x, y, core))
                    {
                        if (map.Has(i, TileFlags.Trees)) { map.Clear(i, TileFlags.Trees); map.Resource[i] = 0f; }
                        map.Clear(i, TileFlags.Farmland | TileFlags.FertileSoil);
                    }
                    else if (map.Has(i, TileFlags.Trees) && rng.Chance(0.5f)) map.Set(i, TileFlags.Burning);
                }

            bool deposit = false;
            if (rng.Chance(0.5f))
            {
                int placed = 0, wanted = rng.RangeInclusive(1, 3);
                GodPowerArea.Bounds(map, target, core, out int cx0, out int cy0, out int cx1, out int cy1);
                bool gold = rng.Chance(0.35f);
                for (int y = cy0; y <= cy1 && placed < wanted; y++)
                    for (int x = cx0; x <= cx1 && placed < wanted; x++)
                    {
                        if (!GodPowerArea.InCircle(target, x, y, core)) continue;
                        int i = map.Index(x, y);
                        if (map.IsWater(i) || map.BuildingAt[i] >= 0 || (map.Flags[i] & GodPowerTerrain.DepositFlags) != 0) continue;
                        map.Clear(i, TileFlags.Trees);
                        map.Set(i, gold ? TileFlags.GoldDeposit : TileFlags.OreDeposit);
                        map.Resource[i] = gold ? rng.Range(80f, 160f) : rng.Range(160f, 300f);
                        placed++;
                    }
                deposit = placed > 0;
            }

            GodPowerTerrain.PublishChanged(ctx, target, radius + 1);
            PublishDisaster(ctx, DisasterType.Meteor, target, radius, 1f);
            var o = GodPowerOutcome.Ok("A meteor strikes " + place + ": " + Plural(destroyed, "building", "buildings") + " destroyed, " + Plural(killed, "life", "lives") + " lost" + (deposit ? "; rare metal glints in the crater." : "."));
            o.Affected = killed + destroyed;
            o.Killed = killed;
            o.BuildingsDestroyed = destroyed;
            o.TilesChanged = shape.TilesChanged + scorched;
            FillPlace(o, near);
            o.ChronicleText = "A fiery star fell upon " + place + ".";
            o.ChronicleImportance = 3;
            return o;
        }

        private static bool BuildingTouchesCircle(Building b, Vec2i c, int r)
        {
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    if (GodPowerArea.InCircle(c, x, y, r)) return true;
            return false;
        }

        /// <summary>
        /// Plague: everyone in settlements in range loses 0.4 health; 10-25% of them (the frailest first, weighted by
        /// age, traits and racial disease resistance) die of disease immediately. Chronicled.
        /// </summary>
        public static GodPowerOutcome Plague(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var rng = ctx.Rng;
            var settlements = new List<Settlement>();
            GodPowerArea.SettlementsInRange(state, target, radius, GodPowerArea.SettlementReach, settlements);
            var people = new List<Person>();
            GodPowerArea.ResidentsOf(state, settlements, people);
            for (int i = 0; i < settlements.Count; i++)
            {
                settlements[i].Health = GodPowerArea.Add01(settlements[i].Health, -0.3f);
                settlements[i].Happiness = GodPowerArea.Add01(settlements[i].Happiness, -0.1f);
            }

            var frailty = new Dictionary<int, float>(people.Count);
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                p.Health = GodPowerArea.Add01(p.Health, -0.4f);
                p.Happiness = GodPowerArea.Add01(p.Happiness, -0.1f);
                var race = ctx.Db.Races[(int)p.Race];
                float resist = race != null ? MathUtil.Clamp(MathUtil.Sanitize(race.DiseaseResistance, 1f), 0.25f, 4f) : 1f;
                float score = rng.NextFloat() + (1f - p.Health);
                if (p.Stage == LifeStage.Elder) score += 0.3f;
                if (p.Stage == LifeStage.Child) score += 0.15f;
                if (p.HasTrait(PersonTraits.Sickly)) score += 0.4f;
                if (p.HasTrait(PersonTraits.Hardy)) score -= 0.4f;
                frailty[p.Id] = score / resist;
            }
            people.Sort((a, b) => { int c = frailty[b.Id].CompareTo(frailty[a.Id]); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            int deaths = MathUtil.Clamp(MathUtil.RoundToInt(people.Count * rng.Range(0.10f, 0.25f)), 0, people.Count);
            for (int i = 0; i < deaths; i++) ctx.Ops.KillPerson(people[i], DeathCause.Disease);
            PublishDisaster(ctx, DisasterType.Plague, target, radius, 0.8f);

            string place = GodPowerArea.PlaceName(state, target, radius + ChronicleNameReach, out var near);
            var o = GodPowerOutcome.Ok(settlements.Count > 0 ? "Pestilence strikes " + Plural(people.Count, "mortal", "mortals") + "; " + deaths + " perish." : "The plague found no one to afflict.");
            o.Affected = people.Count;
            o.Killed = deaths;
            FillPlace(o, near);
            o.ChronicleText = "A divine pestilence swept through " + place + (deaths > 0 ? ", claiming " + Plural(deaths, "soul", "souls") : "") + ".";
            o.ChronicleImportance = 2;
            return o;
        }

        /// <summary>Flooded land: no higher than this above sea level.</summary>
        public const float FloodMaxHeightAboveSea = 0.25f;

        /// <summary>
        /// Flood: low land (within 0.25 of sea level) in range that lies near a river, coast or water body is inundated:
        /// farmland washed away, fires doused, buildings damaged (destroyed at 0), and 1-2% of the people living
        /// there drown (Disaster). Chronicled.
        /// </summary>
        public static GodPowerOutcome Flood(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var map = state.Map;
            var rng = ctx.Rng;
            float sea = MathUtil.Sanitize(map.SeaLevel);
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            var flooded = new bool[bw * bh];
            int floodedCount = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    if (map.IsWater(i) || MathUtil.Sanitize(map.Elevation[i], 1f) > sea + FloodMaxHeightAboveSea) continue;
                    if (!NearWaterOrRiver(map, x, y, 2)) continue;
                    flooded[(y - y0) * bw + (x - x0)] = true;
                    floodedCount++;
                    map.Clear(i, TileFlags.Farmland | TileFlags.Burning);
                    map.Moisture[i] = 1f;
                    map.Fertility[i] = GodPowerArea.Add01(map.Fertility[i], 0.05f);
                }

            int damaged = 0, destroyed = 0, drowned = 0;
            if (floodedCount > 0)
            {
                var buildings = new List<Building>();
                GodPowerArea.BuildingsInRange(state, target, radius, buildings);
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if (!Overlaps(b, flooded, x0, y0, bw, bh)) continue;
                    b.IsOnFire = false;
                    damaged++;
                    if (DamageBuilding(ctx, b, rng.Range(0.2f, 0.5f))) destroyed++;
                }

                float drownChance = rng.Range(0.01f, 0.02f);
                var people = new List<Person>();
                GodPowerArea.PeopleInRange(state, target, radius, people);
                for (int i = 0; i < people.Count; i++)
                {
                    var p = people[i];
                    int lx = p.Position.X - x0, ly = p.Position.Y - y0;
                    if (!p.IsAlive || lx < 0 || ly < 0 || lx >= bw || ly >= bh || !flooded[ly * bw + lx]) continue;
                    if (rng.Chance(drownChance)) { ctx.Ops.KillPerson(p, DeathCause.Disaster); drowned++; }
                }
                GodPowerTerrain.PublishChanged(ctx, target, radius);
            }
            PublishDisaster(ctx, DisasterType.Flood, target, radius, floodedCount > 0 ? 0.6f : 0.1f);

            string place = GodPowerArea.PlaceName(state, target, radius + ChronicleNameReach, out var near);
            var o = GodPowerOutcome.Ok(floodedCount > 0
                ? "Waters drown " + Plural(floodedCount, "tile", "tiles") + ", damaging " + Plural(damaged, "building", "buildings") + "; " + drowned + " drowned."
                : "The waters rose, but found no lowland to claim.");
            o.Affected = floodedCount;
            o.TilesChanged = floodedCount;
            o.BuildingsDestroyed = destroyed;
            o.Killed = drowned;
            FillPlace(o, near);
            if (floodedCount > 0)
            {
                o.ChronicleText = "The waters rose and drowned the lowlands near " + place + ".";
                o.ChronicleImportance = 2;
            }
            return o;
        }

        private static bool NearWaterOrRiver(WorldMap map, int x, int y, int reach)
        {
            int i = map.Index(x, y);
            if ((map.Flags[i] & (TileFlags.River | TileFlags.Coastal)) != 0) return true;
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!map.InBounds(nx, ny)) continue;
                    int n = map.Index(nx, ny);
                    if (map.IsWater(n) || map.Has(n, TileFlags.River)) return true;
                }
            return false;
        }

        private static bool Overlaps(Building b, bool[] mask, int x0, int y0, int bw, int bh)
        {
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                {
                    int lx = x - x0, ly = y - y0;
                    if (lx >= 0 && ly >= 0 && lx < bw && ly < bh && mask[ly * bw + lx]) return true;
                }
            return false;
        }

        /// <summary>Drought: 96 ticks of drought (rain ends) and -0.2 moisture on land in range.</summary>
        public static GodPowerOutcome Drought(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var map = state.Map;
            var w = state.Weather;
            w.DroughtTicksRemaining = Math.Max(96, w.DroughtTicksRemaining);
            w.RainTicksRemaining = 0;
            int tiles = 0;
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    if (map.IsWater(i)) continue;
                    map.Moisture[i] = GodPowerArea.Add01(map.Moisture[i], -0.2f);
                    tiles++;
                }
            if (tiles > 0) GodPowerTerrain.PublishChanged(ctx, target, radius);
            PublishDisaster(ctx, DisasterType.Drought, target, radius, 0.5f);
            var o = GodPowerOutcome.Ok("The sky withholds its rain; " + Plural(tiles, "tile parches", "tiles parch") + ".");
            o.Affected = tiles;
            o.TilesChanged = tiles;
            return o;
        }

        private static void FillPlace(GodPowerOutcome o, Settlement near)
        {
            if (near == null) return;
            o.SettlementId = near.Id;
            o.CivilizationId = near.CivilizationId;
        }
    }
}
