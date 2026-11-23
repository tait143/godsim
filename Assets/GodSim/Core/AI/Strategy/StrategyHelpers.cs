using System;

namespace GodSim.Core.AI
{
    /// <summary>Shared, allocation-free helpers for the civilization-level AI systems.</summary>
    internal static class StrategyHelpers
    {
        /// <summary>Food per person per tick assumed when the race definition gives none.</summary>
        public const float DefaultFoodPerTick = 0.05f;
        /// <summary>Food days reported for an empty settlement (no consumption).</summary>
        public const float NoConsumptionFoodDays = 999f;

        /// <summary>Race definition or null when unregistered/out of range (never throws).</summary>
        public static RaceDefinition RaceOf(GameDatabase db, RaceId id)
        {
            int i = (int)id;
            if (db == null || i < 0 || i >= db.Races.Count) return null;
            return db.Races[i];
        }

        /// <summary>Habitability of a biome for a race (0 when data is missing).</summary>
        public static float Habitability(RaceDefinition race, Biome biome)
        {
            var arr = race?.BiomeHabitability;
            int i = (int)biome;
            if (arr == null || i < 0 || i >= arr.Length) return 0f;
            return MathUtil.Clamp01(MathUtil.Sanitize(arr[i]));
        }

        /// <summary>Food per tick per person for the race (sanitized).</summary>
        public static float FoodPerTick(RaceDefinition race)
        {
            float f = race != null ? MathUtil.Sanitize(race.FoodPerTick) : 0f;
            return f > 0f ? f : DefaultFoodPerTick;
        }

        /// <summary>
        /// Days of food left: the economy's <c>econ.foodDays</c> memory when present, otherwise
        /// stockpiled food / (population * food per tick).
        /// </summary>
        public static float FoodDays(Settlement s, RaceDefinition race)
        {
            if (s == null) return 0f;
            if (s.Memory.TryGetValue(CivStrategyKeys.FoodDays, out float v) && !float.IsNaN(v) && !float.IsInfinity(v))
                return Math.Max(0f, v);
            int pop = s.Population;
            if (pop <= 0) return NoConsumptionFoodDays;
            return MathUtil.Sanitize(s.Stockpile.Get(ResourceType.Food) / (pop * FoodPerTick(race)));
        }

        /// <summary>True when the settlement exists, is alive and belongs to the civ.</summary>
        public static bool IsOwnAlive(Settlement s, int civId) => s != null && s.IsAlive && s.CivilizationId == civId;

        /// <summary>The civ's capital, or (if the capital id is stale) its most populous alive settlement. Null when none.</summary>
        public static Settlement Capital(WorldState state, Civilization civ)
        {
            if (state == null || civ == null) return null;
            var cap = civ.CapitalId >= 0 ? state.Settlements.Get(civ.CapitalId) : null;
            if (IsOwnAlive(cap, civ.Id)) return cap;
            Settlement best = null;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (!IsOwnAlive(s, civ.Id)) continue;
                if (best == null || s.Population > best.Population || (s.Population == best.Population && s.Id < best.Id)) best = s;
            }
            return best;
        }

        /// <summary>Number of alive settlements owned by the civ.</summary>
        public static int CountAliveSettlements(WorldState state, Civilization civ)
        {
            int n = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++) if (IsOwnAlive(state.Settlements.Get(ids[i]), civ.Id)) n++;
            return n;
        }

        /// <summary>True while an active war exists between the two civs.</summary>
        public static bool WarBetween(WorldState state, int a, int b)
        {
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                if ((w.AttackerId == a && w.DefenderId == b) || (w.AttackerId == b && w.DefenderId == a)) return true;
            }
            return false;
        }

        /// <summary>Counts active wars of a civ against alive opponents; reports whether it defends in any of them.</summary>
        public static int ActiveWars(WorldState state, int civId, out bool defending)
        {
            defending = false;
            int n = 0;
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                int other;
                if (w.AttackerId == civId) other = w.DefenderId;
                else if (w.DefenderId == civId) { other = w.AttackerId; }
                else continue;
                var oc = state.Civilizations.Get(other);
                if (oc == null || !oc.IsAlive) continue;
                n++;
                if (w.DefenderId == civId) defending = true;
            }
            return n;
        }

        /// <summary>
        /// Military power of a civ: the cached <c>Civilization.MilitaryPower</c> when the military module
        /// computed one, otherwise an estimate from soldiers, defenses and population.
        /// </summary>
        public static float EffectivePower(WorldState state, GameDatabase db, Civilization civ)
        {
            if (civ == null) return 0f;
            float cached = MathUtil.Sanitize(civ.MilitaryPower);
            if (cached > 0f) return cached;
            var race = RaceOf(db, civ.Race);
            float strength = race != null ? Math.Max(0.1f, MathUtil.Sanitize(race.MilitaryStrength, 1f)) : 1f;
            float mod = Math.Max(0.1f, MathUtil.Sanitize(civ.Modifiers.Military, 1f));
            float power = 0f;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (!IsOwnAlive(s, civ.Id)) continue;
                power += Math.Max(0, s.Soldiers) * strength * mod + Math.Max(0f, MathUtil.Sanitize(s.DefenseStrength)) * 0.5f + s.Population * 0.05f;
            }
            return MathUtil.Sanitize(power);
        }

        /// <summary>Squared distance between the closest pair of alive settlements of two civs (int.MaxValue if none).</summary>
        public static int MinSettlementDistanceSq(WorldState state, Civilization a, Civilization b)
        {
            int best = int.MaxValue;
            for (int i = 0; i < a.SettlementIds.Count; i++)
            {
                var sa = state.Settlements.Get(a.SettlementIds[i]);
                if (!IsOwnAlive(sa, a.Id)) continue;
                for (int j = 0; j < b.SettlementIds.Count; j++)
                {
                    var sb = state.Settlements.Get(b.SettlementIds[j]);
                    if (!IsOwnAlive(sb, b.Id)) continue;
                    int d = sa.Center.DistanceSqTo(sb.Center);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>Culture-invariant one-decimal formatting for log lines (no System.Globalization needed).</summary>
        public static string Fmt1(float v)
        {
            v = MathUtil.Sanitize(v);
            if (v > 1e7f) v = 1e7f;
            if (v < -1e7f) v = -1e7f;
            int tenths = MathUtil.RoundToInt(v * 10f);
            string sign = tenths < 0 ? "-" : "";
            if (tenths < 0) tenths = -tenths;
            return sign + (tenths / 10).ToString() + "." + (tenths % 10).ToString();
        }

        /// <summary>Display name of a building definition.</summary>
        public static string NameOf(BuildingDefinition def) => def == null ? "?" : (string.IsNullOrEmpty(def.Name) ? def.Type.ToString() : def.Name);

        /// <summary>Display name of a tech definition.</summary>
        public static string NameOf(TechDefinition def) => def == null ? "?" : (string.IsNullOrEmpty(def.Name) ? def.Key : def.Name);

        /// <summary>Signed whole percent, e.g. "+12%" / "-5%".</summary>
        public static string Pct(float fraction)
        {
            int p = MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(fraction), -99.99f, 99.99f) * 100f);
            return (p >= 0 ? "+" : "") + p.ToString() + "%";
        }

        // ================================================================== personality & mood
        /// <summary>Dynamic personality trait (Advisor Council), falling back to the race default.</summary>
        public static float Trait(Civilization civ, GameDatabase db, AITrait trait) => AdvisorBoard.Trait(civ, db, trait);

        /// <summary>War weariness 0..1 published by diplomacy (<c>war.weariness</c>), 0 when absent.</summary>
        public static float WarWeariness(Civilization civ) => civ == null ? 0f : MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.WarWeariness)));

        /// <summary>Stored economy trend -1..1 (0 when unknown).</summary>
        public static float EconomyTrend(Civilization civ) => civ == null ? 0f : MathUtil.Clamp(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.EconomyTrend)), -1f, 1f);

        /// <summary>True for Orcs and for civs whose dynamic Expansionism is at least <paramref name="threshold"/>.</summary>
        public static bool IsExpansionDriven(Civilization civ, GameDatabase db, float threshold = 0.75f)
            => civ != null && (civ.Race == RaceId.Orc || Trait(civ, db, AITrait.Expansionism) >= threshold);

        // ================================================================== hostility
        /// <summary>At war, or in contact with a Hostile/War stance.</summary>
        public static bool IsHostile(WorldState state, int civId, int otherId)
        {
            if (civId == otherId || civId < 0 || otherId < 0) return false;
            if (WarBetween(state, civId, otherId)) return true;
            return state.Diplomacy.TryGet(civId, otherId, out var rel) && (rel.AtWar || rel.Stance <= DiplomaticStance.Hostile);
        }

        /// <summary>
        /// Distance (tiles) from <paramref name="p"/> to the nearest alive army of a civ hostile to <paramref name="civId"/>,
        /// or float.MaxValue when there is none. Allocation-free.
        /// </summary>
        public static float NearestHostileArmy(WorldState state, int civId, Vec2i p)
        {
            float best = float.MaxValue;
            var armies = state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (a == null || !a.IsAlive || a.CivilizationId == civId || a.State == ArmyState.Disbanding) continue;
                if (!IsHostile(state, civId, a.CivilizationId)) continue;
                float d = a.Position.DistanceTo(p);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Distance from p to the segment [a, b] (tiles).</summary>
        public static float DistanceToSegment(Vec2i p, Vec2i a, Vec2i b)
        {
            float vx = b.X - a.X, vy = b.Y - a.Y;
            float wx = p.X - a.X, wy = p.Y - a.Y;
            float len2 = vx * vx + vy * vy;
            float t = len2 > 0f ? MathUtil.Clamp01((wx * vx + wy * vy) / len2) : 0f;
            float dx = wx - t * vx, dy = wy - t * vy;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // ================================================================== crises
        /// <summary>Days below which a settlement is in a food crisis.</summary>
        public const float FoodCrisisDays = 20f;
        /// <summary>A reported famine keeps the civ in "famine" for this many ticks.</summary>
        public const int FamineMemoryTicks = SimTime.TicksPerYear / 2;

        /// <summary>Any populated settlement below <see cref="FoodCrisisDays"/>, or a famine reported recently.</summary>
        public static bool InFoodCrisis(WorldState state, Civilization civ, RaceDefinition race, long tick, out float worstDays)
        {
            worstDays = NoConsumptionFoodDays;
            bool crisis = false;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (!IsOwnAlive(s, civ.Id) || s.Population <= 0) continue;
                float d = FoodDays(s, race);
                if (d < worstDays) worstDays = d;
                if (d < FoodCrisisDays) crisis = true;
            }
            if (civ.Memory.TryGetValue(CivStrategyKeys.FamineTick, out float ft) && tick - (long)MathUtil.Sanitize(ft, -1e7f) <= FamineMemoryTicks) crisis = true;
            return crisis;
        }

        /// <summary>Records a disaster on the civ (keeps the stronger of a still-fresh previous one).</summary>
        public static void RecordDisaster(Civilization civ, long tick, float severity)
        {
            if (civ == null) return;
            severity = MathUtil.Clamp01(MathUtil.Sanitize(severity));
            if (severity <= 0f) return;
            float prev = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.DisasterSeverity)));
            long prevTick = (long)MathUtil.Sanitize(civ.Mem(CivStrategyKeys.DisasterTick, -1e7f), -1e7f);
            if (tick - prevTick < SimTime.TicksPerYear && prev > severity) severity = prev;
            civ.Memory[CivStrategyKeys.DisasterTick] = tick;
            civ.Memory[CivStrategyKeys.DisasterSeverity] = severity;
        }

        // ================================================================== terrain features
        /// <summary>Placement rules that depend on terrain (hard requirements).</summary>
        public const PlacementRule HardPlacementRules = PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills
            | PlacementRule.NearMountain | PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone
            | PlacementRule.NearGold | PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.NearRiver;

        /// <summary>Placement features present within Chebyshev radius r of c, as PlacementRule bits (Fertile = fertile soil).</summary>
        public static PlacementRule ScanFeatures(WorldMap map, Vec2i c, int r)
        {
            PlacementRule present = PlacementRule.None;
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    present |= FeaturesOf(map.Flags[i], map.Biomes[i]);
                }
            return present;
        }

        /// <summary>PlacementRule bits a single tile contributes.</summary>
        public static PlacementRule FeaturesOf(TileFlags f, Biome b)
        {
            PlacementRule present = PlacementRule.None;
            if ((f & TileFlags.River) != 0) present |= PlacementRule.NearRiver | PlacementRule.NearWater;
            if ((f & TileFlags.Coastal) != 0) present |= PlacementRule.Coastal | PlacementRule.NearWater;
            if (b == Biome.Lake) present |= PlacementRule.NearWater;
            if (b == Biome.Hills) present |= PlacementRule.OnHills;
            if (b == Biome.Mountain) present |= PlacementRule.NearMountain;
            if ((f & TileFlags.Trees) != 0 || b == Biome.Forest || b == Biome.Jungle || b == Biome.Taiga) present |= PlacementRule.NearForest;
            if ((f & TileFlags.OreDeposit) != 0) present |= PlacementRule.NearOre;
            if ((f & TileFlags.StoneDeposit) != 0) present |= PlacementRule.NearStone;
            if ((f & TileFlags.GoldDeposit) != 0) present |= PlacementRule.NearGold;
            if ((f & TileFlags.CoalDeposit) != 0) present |= PlacementRule.NearCoal;
            if ((f & TileFlags.OilDeposit) != 0) present |= PlacementRule.NearOil;
            if ((f & TileFlags.FertileSoil) != 0) present |= PlacementRule.Fertile;
            return present;
        }

        /// <summary>Ticks between two recomputations of <see cref="CivFeatures"/> (also refreshed when the settlement count changes).</summary>
        public const int FeatureCacheTicks = SimTime.TicksPerYear * 2;

        /// <summary>Union of the terrain features around all of the civ's settlements (territory + 2), cached in civ memory.</summary>
        public static PlacementRule CivFeatures(WorldState state, Civilization civ, long tick)
        {
            if (state == null || civ == null) return PlacementRule.None;
            int count = CountAliveSettlements(state, civ);
            if (civ.Memory.TryGetValue(CivStrategyKeys.Features, out float cached)
                && MathUtil.RoundToInt(civ.Mem(CivStrategyKeys.FeaturesCount, -1f)) == count
                && tick - (long)MathUtil.Sanitize(civ.Mem(CivStrategyKeys.FeaturesTick, -1e7f), -1e7f) < FeatureCacheTicks
                && tick >= (long)MathUtil.Sanitize(civ.Mem(CivStrategyKeys.FeaturesTick)))
                return (PlacementRule)(ushort)MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(cached)), 0, 65535);
            PlacementRule f = PlacementRule.None;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (!IsOwnAlive(s, civ.Id)) continue;
                f |= ScanFeatures(state.Map, s.Center, MathUtil.Clamp(s.TerritoryRadius + 2, 6, 24));
            }
            civ.Memory[CivStrategyKeys.Features] = (int)f;
            civ.Memory[CivStrategyKeys.FeaturesTick] = tick;
            civ.Memory[CivStrategyKeys.FeaturesCount] = count;
            return f;
        }

        /// <summary>True when the features satisfy every hard placement requirement of the rule.</summary>
        public static bool PlacementSatisfied(PlacementRule features, PlacementRule rule)
        {
            PlacementRule needed = rule & HardPlacementRules;
            return (features & needed) == needed;
        }

        /// <summary>Whether the civ's terrain lets it produce a raw resource at all (deposit / forest present).</summary>
        public static bool CanSource(PlacementRule features, ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Ore: case ResourceType.Metal: return (features & (PlacementRule.NearOre | PlacementRule.OnHills | PlacementRule.NearMountain)) != 0;
                case ResourceType.Stone: return (features & (PlacementRule.NearStone | PlacementRule.OnHills | PlacementRule.NearMountain)) != 0;
                case ResourceType.Gold: return (features & PlacementRule.NearGold) != 0;
                case ResourceType.Fuel: return (features & (PlacementRule.NearCoal | PlacementRule.NearOil)) != 0;
                case ResourceType.Wood: return (features & PlacementRule.NearForest) != 0;
                default: return true;
            }
        }
    }
}
