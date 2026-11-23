using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// The concrete effects of every divine power: one public method per <see cref="GodPowerType"/>, plus a
    /// dispatcher (<see cref="Apply"/>) and the power-specific feasibility checks (<see cref="CheckPreconditions"/>).
    /// Effects never charge mana, set cooldowns or move alignment; that bookkeeping is done by
    /// <see cref="GodCommandSystem"/>. Each method re-checks its inputs (bounds, radius) and returns a failed
    /// outcome instead of throwing. Every loop is bounded by the radius and the map bounds; entity creation and
    /// removal goes through <c>ctx.Ops</c>; randomness through <c>ctx.Rng</c>.
    /// </summary>
    public static partial class GodPowerEffects
    {
        /// <summary>Minimum free buildable tiles within <see cref="SpawnTribeLandRadius"/> to spawn a tribe.</summary>
        public const int SpawnTribeMinBuildableTiles = 30;
        /// <summary>Radius scanned for buildable land when spawning a tribe.</summary>
        public const int SpawnTribeLandRadius = 5;
        /// <summary>No settlement may lie within this distance of a newly spawned tribe.</summary>
        public const int SpawnTribeMinSettlementDistance = 10;
        /// <summary>How far from the target the chronicle looks for a settlement name.</summary>
        public const int ChronicleNameReach = 8;

        /// <summary>Runs the effect of <paramref name="power"/>. Parameter is only used by SpawnTribe (a RaceId).</summary>
        public static GodPowerOutcome Apply(SimContext ctx, GodPowerType power, Vec2i target, int radius, int parameter)
        {
            switch (power)
            {
                case GodPowerType.Bless: return Bless(ctx, target, radius);
                case GodPowerType.Rain: return Rain(ctx, target, radius);
                case GodPowerType.Fertility: return Fertility(ctx, target, radius);
                case GodPowerType.Inspire: return Inspire(ctx, target, radius);
                case GodPowerType.Heal: return Heal(ctx, target, radius);
                case GodPowerType.BountifulHarvest: return BountifulHarvest(ctx, target, radius);
                case GodPowerType.Prophet: return Prophet(ctx, target, radius);
                case GodPowerType.RaiseLand: return RaiseLand(ctx, target, radius);
                case GodPowerType.LowerLand: return LowerLand(ctx, target, radius);
                case GodPowerType.PlantForest: return PlantForest(ctx, target, radius);
                case GodPowerType.RevealOre: return RevealOre(ctx, target, radius);
                case GodPowerType.SpawnTribe:
                    if (parameter < 0 || parameter >= Counts.Races) return GodPowerOutcome.Fail("invalid race parameter");
                    return SpawnTribe(ctx, target, (RaceId)parameter);
                case GodPowerType.Lightning: return Lightning(ctx, target, radius);
                case GodPowerType.Wildfire: return Wildfire(ctx, target, radius);
                case GodPowerType.Earthquake: return Earthquake(ctx, target, radius);
                case GodPowerType.Meteor: return Meteor(ctx, target, radius);
                case GodPowerType.Plague: return Plague(ctx, target, radius);
                case GodPowerType.Flood: return Flood(ctx, target, radius);
                case GodPowerType.Drought: return Drought(ctx, target, radius);
                default: return GodPowerOutcome.Fail("unknown power");
            }
        }

        /// <summary>
        /// Power-specific feasibility checks that cannot be expressed in <see cref="GodPowerDefinition"/>
        /// (e.g. SpawnTribe needs open land, Prophet needs an adult to anoint). Run before mana is charged.
        /// </summary>
        public static bool CheckPreconditions(SimContext ctx, GodPowerType power, Vec2i target, int radius, int parameter, out string reason)
        {
            reason = null;
            if (ctx == null || ctx.State == null || ctx.State.Map == null) { reason = "no world"; return false; }
            if (!ctx.State.Map.InBounds(target)) { reason = "target out of bounds"; return false; }
            switch (power)
            {
                case GodPowerType.SpawnTribe:
                    return CanSpawnTribe(ctx, target, parameter, out reason);
                case GodPowerType.Prophet:
                    if (FindProphetCandidate(ctx.State, target, GodPowerArea.ClampRadius(radius), out _) == null)
                    {
                        reason = "no worthy mortal within range";
                        return false;
                    }
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// True if a tribe of race <paramref name="raceParameter"/> may be spawned at target: valid registered race,
        /// buildable unclaimed land, at least <see cref="SpawnTribeMinBuildableTiles"/> free buildable tiles within
        /// <see cref="SpawnTribeLandRadius"/>, no settlement within <see cref="SpawnTribeMinSettlementDistance"/>,
        /// and room under the world population cap.
        /// </summary>
        public static bool CanSpawnTribe(SimContext ctx, Vec2i target, int raceParameter, out string reason)
        {
            reason = null;
            if (ctx == null || ctx.State == null || ctx.Db == null) { reason = "no world"; return false; }
            var state = ctx.State;
            var map = state.Map;
            if (raceParameter < 0 || raceParameter >= Counts.Races || !Enum.IsDefined(typeof(RaceId), (RaceId)raceParameter) || ctx.Db.Races[raceParameter] == null)
            { reason = "invalid race parameter"; return false; }
            if (!map.InBounds(target)) { reason = "target out of bounds"; return false; }
            int ti = map.Index(target);
            if (!map.Biomes[ti].IsBuildable()) { reason = "target must be habitable land"; return false; }
            int owner = map.Owner[ti];
            if (owner >= 0) { var os = state.Settlements.Get(owner); if (os != null && os.IsAlive) { reason = "land already claimed by " + os.Name; return false; } }
            if (GodPowerArea.AnySettlementInRange(state, target, SpawnTribeMinSettlementDistance, 0)) { reason = "too close to an existing settlement"; return false; }

            int free = 0;
            GodPowerArea.Bounds(map, target, SpawnTribeLandRadius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (GodPowerArea.InCircle(target, x, y, SpawnTribeLandRadius) && map.IsFreeBuildable(map.Index(x, y))) free++;
            if (free < SpawnTribeMinBuildableTiles) { reason = "not enough open land (" + free + "/" + SpawnTribeMinBuildableTiles + " buildable tiles)"; return false; }

            var cfg = ctx.Config;
            if (cfg != null && state.People.Count + MathUtil.Clamp(cfg.InitialPopulationPerRace, 2, 200) > cfg.MaxPopulation)
            { reason = "world population limit reached"; return false; }
            return true;
        }

        // ------------------------------------------------------------------ shared helpers

        /// <summary>Validates the context/target and clamps the radius. On failure returns false with a failed outcome.</summary>
        private static bool Prepare(SimContext ctx, Vec2i target, ref int radius, out GodPowerOutcome fail)
        {
            fail = null;
            if (ctx == null || ctx.State == null || ctx.State.Map == null || ctx.Ops == null || ctx.Db == null)
            { fail = GodPowerOutcome.Fail("no world"); return false; }
            if (!ctx.State.Map.InBounds(target)) { fail = GodPowerOutcome.Fail("target out of bounds"); return false; }
            radius = GodPowerArea.ClampRadius(radius);
            return true;
        }

        private static void Publish<T>(SimContext ctx, T evt) where T : struct
        {
            if (ctx.Events != null) ctx.Events.Publish(evt);
        }

        private static void PublishDisaster(SimContext ctx, DisasterType type, Vec2i target, int radius, float severity)
        {
            Publish(ctx, new DisasterEvent { Type = type, Position = target, Radius = radius, Severity = MathUtil.Clamp01(severity) });
        }

        private static string Plural(int n, string one, string many) => n == 1 ? "1 " + one : n + " " + many;

        /// <summary>Damages a building's condition; destroys it (DivinePower) at 0. Returns true if destroyed.</summary>
        private static bool DamageBuilding(SimContext ctx, Building b, float damage)
        {
            if (b == null || b.IsDestroyed) return false;
            b.Condition = MathUtil.Clamp01(MathUtil.Sanitize(b.Condition, 1f) - Math.Max(0f, MathUtil.Sanitize(damage)));
            if (b.Condition > 0f) return false;
            ctx.Ops.DestroyBuilding(b, DestroyCause.DivinePower);
            return true;
        }
    }
}
