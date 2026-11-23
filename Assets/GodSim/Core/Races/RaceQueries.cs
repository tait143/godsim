using System;

namespace GodSim.Core.Races
{
    /// <summary>
    /// Stateless read helpers over race data: preferred biomes, per-tile habitability and the symmetric
    /// inter-race affinity. Every method is total: null definitions/maps/databases, out-of-range ids or
    /// tiles, malformed arrays and NaN/Infinity all yield a neutral value instead of throwing, so the
    /// helpers are safe inside a system Tick. Only <see cref="PreferredBiomes(RaceDefinition, float)"/>
    /// allocates (call it at initialization, not per tick).
    /// </summary>
    public static class RaceQueries
    {
        /// <summary>Habitability at or above which a biome counts as a "good" home for a race.</summary>
        public const float GoodHabitability = 0.7f;

        /// <summary>Bonus on tiles with a river (fresh water) for every race.</summary>
        public const float RiverBonus = 0.05f;
        /// <summary>Bonus on tiles with fertile soil for every race.</summary>
        public const float FertileSoilBonus = 0.05f;
        /// <summary>Extra bonus on wooded tiles for races that build in trees (<see cref="ArchitectureStyle.BuildsInTrees"/>).</summary>
        public const float CanopyBonus = 0.1f;
        /// <summary>Extra bonus on ore/stone deposits for races that build underground (<see cref="ArchitectureStyle.BuildsUnderground"/>).</summary>
        public const float DeepVeinBonus = 0.1f;
        /// <summary>Extra bonus on coastal/river tiles for races that build on stilts (<see cref="ArchitectureStyle.BuildsOnStilts"/>).</summary>
        public const float StiltWaterBonus = 0.05f;
        /// <summary>Penalty on recently burned tiles.</summary>
        public const float ScorchedPenalty = 0.15f;

        /// <summary>
        /// The race's base habitability for a biome, clamped to [0, 1]. Water biomes, an unknown biome, a
        /// null definition, a missing/wrong-length table or a non-finite entry all give 0.
        /// </summary>
        public static float BiomeValue(RaceDefinition def, Biome biome)
        {
            if (def == null || biome.IsWater()) return 0f;
            var table = def.BiomeHabitability;
            int i = (int)biome;
            if (table == null || table.Length != Counts.Biomes || i >= table.Length) return 0f;
            return MathUtil.Clamp01(MathUtil.Sanitize(table[i]));
        }

        /// <summary>
        /// Habitability of one map tile (index = y * Width + x) for a race, in [0, 1]: the biome value plus
        /// small tile-flag modifiers (river, fertile soil, scorched, and the race's tree/underground/stilt
        /// building habits). A biome the race cannot live in (value 0) stays 0 whatever the flags. Returns 0
        /// for a null definition or map, an out-of-range tile or a water tile. Allocation-free.
        /// </summary>
        public static float Habitability(RaceDefinition def, WorldMap map, int tile)
        {
            if (def == null || map == null || tile < 0 || tile >= map.TileCount) return 0f;
            float v = BiomeValue(def, map.Biomes[tile]);
            if (v <= 0f) return 0f;

            var flags = map.Flags[tile];
            if ((flags & TileFlags.River) != 0) v += RiverBonus;
            if ((flags & TileFlags.FertileSoil) != 0) v += FertileSoilBonus;
            var arch = def.Architecture;
            if (arch != null)
            {
                if (arch.BuildsInTrees && (flags & TileFlags.Trees) != 0) v += CanopyBonus;
                if (arch.BuildsUnderground && (flags & (TileFlags.OreDeposit | TileFlags.StoneDeposit)) != 0) v += DeepVeinBonus;
                if (arch.BuildsOnStilts && (flags & (TileFlags.Coastal | TileFlags.River)) != 0) v += StiltWaterBonus;
            }
            if ((flags & TileFlags.Scorched) != 0) v -= ScorchedPenalty;
            return MathUtil.Clamp01(MathUtil.Sanitize(v));
        }

        /// <summary>Habitability of the tile at <paramref name="tile"/>; 0 when it is out of bounds (or <see cref="Vec2i.Invalid"/>).</summary>
        public static float Habitability(RaceDefinition def, WorldMap map, Vec2i tile)
        {
            if (map == null || !map.InBounds(tile)) return 0f;
            return Habitability(def, map, map.Index(tile));
        }

        /// <summary>
        /// Land biomes the race can live in (habitability &gt; 0), best first; ties are broken by ascending
        /// <see cref="Biome"/> value so the order is deterministic. Null definition: empty array. Allocates.
        /// </summary>
        public static Biome[] PreferredBiomes(RaceDefinition def) => PreferredBiomes(def, 0f);

        /// <summary>
        /// Like <see cref="PreferredBiomes(RaceDefinition)"/> but only biomes whose habitability is at least
        /// <paramref name="minHabitability"/> (e.g. <see cref="GoodHabitability"/>). Never includes water or 0-valued biomes.
        /// </summary>
        public static Biome[] PreferredBiomes(RaceDefinition def, float minHabitability)
        {
            if (def == null) return new Biome[0];
            float min = MathUtil.Sanitize(minHabitability);
            int count = Counts.Biomes;
            var biomes = new Biome[count];
            var values = new float[count];
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                var b = (Biome)i;
                float v = BiomeValue(def, b);
                if (v <= 0f || v < min) continue;
                // Insertion sort, descending by value; strict '<' keeps earlier (lower) enum values first on ties.
                int j = n - 1;
                while (j >= 0 && values[j] < v)
                {
                    biomes[j + 1] = biomes[j];
                    values[j + 1] = values[j];
                    j--;
                }
                biomes[j + 1] = b;
                values[j + 1] = v;
                n++;
            }
            var result = new Biome[n];
            Array.Copy(biomes, result, n);
            return result;
        }

        /// <summary>
        /// One-directional affinity of <paramref name="from"/> toward race <paramref name="to"/>, sanitized and
        /// clamped to [-1, 1]. 0 for a null definition, a missing/short table or an out-of-range id.
        /// </summary>
        public static float DirectedAffinity(RaceDefinition from, RaceId to)
        {
            if (from == null || from.RaceAffinity == null) return 0f;
            int i = (int)to;
            if (i >= from.RaceAffinity.Length) return 0f;
            return MathUtil.Clamp(MathUtil.Sanitize(from.RaceAffinity[i]), -1f, 1f);
        }

        /// <summary>
        /// Symmetric affinity between two race definitions: the average of a-&gt;b and b-&gt;a, in [-1, 1].
        /// The same race (equal ids) is always 1; a null definition gives 0.
        /// </summary>
        public static float AffinityBetween(RaceDefinition a, RaceDefinition b)
        {
            if (a == null || b == null) return 0f;
            if (a.Id == b.Id) return 1f;
            float avg = (DirectedAffinity(a, b.Id) + DirectedAffinity(b, a.Id)) * 0.5f;
            return MathUtil.Clamp(MathUtil.Sanitize(avg), -1f, 1f);
        }

        /// <summary>
        /// Symmetric affinity between two races registered in <paramref name="db"/> (AffinityBetween(db, a, b) ==
        /// AffinityBetween(db, b, a)), in [-1, 1]. Never throws: an id outside <see cref="RaceId"/> gives 0; the
        /// same valid id gives 1; a null database or an unregistered race gives 0. Reads <see cref="GameDatabase.Races"/>
        /// directly (not <see cref="GameDatabase.Race"/>, which throws for empty slots).
        /// </summary>
        public static float AffinityBetween(GameDatabase db, RaceId a, RaceId b)
        {
            int ia = (int)a, ib = (int)b;
            if (ia >= Counts.Races || ib >= Counts.Races) return 0f;
            if (ia == ib) return 1f;
            if (db == null) return 0f;
            var races = db.Races;
            if (races == null || ia >= races.Count || ib >= races.Count) return 0f;
            return AffinityBetween(races[ia], races[ib]);
        }
    }
}
