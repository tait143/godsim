using System;
using System.Collections.Generic;

namespace GodSim.Core.World
{
    /// <summary>
    /// Chooses one founding tile per race. Each candidate is scored by the race's average biome
    /// habitability within <see cref="Radius"/>, plus bonuses for fresh water (river, lake, coast),
    /// trees, stone deposits and soil fertility, and must have at least <see cref="MinBuildableTiles"/>
    /// free buildable tiles in that window plus a free 2x2 town-center footprint that covers no deposit,
    /// sacred site or river. The first search pass also requires the Dawn-age building materials to be in
    /// reach (a stone deposit within <see cref="StoneRadius"/>, enough trees within <see cref="TreeRadius"/>
    /// and <see cref="Radius"/>) as long as that costs the race at most a bounded amount of habitability;
    /// later passes relax these only when no candidate qualifies. Races are
    /// spread apart greedily (farthest-point weighting with a minimum spacing that relaxes only when
    /// nothing qualifies). All window sums use integral images, so a search is O(tiles) per race.
    /// Deterministic for a given map and Rng state.
    /// </summary>
    public static class StartLocationFinder
    {
        /// <summary>Chebyshev radius of the evaluation window.</summary>
        public const int Radius = 6;
        /// <summary>Free buildable tiles required within <see cref="Radius"/> (13x13 window).</summary>
        public const int MinBuildableTiles = 60;
        /// <summary>Chebyshev radius within which a start should have a StoneDeposit (quarry reach).</summary>
        public const int StoneRadius = 10;
        /// <summary>StoneDeposit tiles required within <see cref="StoneRadius"/> in the first pass.</summary>
        public const int MinStoneDeposits = 1;
        /// <summary>Chebyshev radius of the wider woodland check.</summary>
        public const int TreeRadius = 8;
        /// <summary>Trees tiles required within <see cref="TreeRadius"/> in the first pass.</summary>
        public const int MinTreesInTreeRadius = 12;
        /// <summary>Trees tiles required within <see cref="Radius"/> in the first pass (a woodcutter camp at home).</summary>
        public const int MinTreesInRadius = 3;
        /// <summary>Footprint of the town center that FoundSettlement places with its origin at the start tile.</summary>
        public const int TownCenterSize = 2;

        private const float FreshWaterBonus = 0.15f;
        private const float TreeBonus = 0.15f;
        private const float StoneBonus = 0.2f;
        private const float FertilityBonus = 0.1f;
        private const float SpreadBonus = 0.3f;
        private const float JitterScale = 0.01f;
        /// <summary>Spacing relaxation per retry, and how many retries keep the resource requirement.</summary>
        private const float SpacingRelax = 0.8f;
        private const int ResourcePassSpacingSteps = 2;
        /// <summary>
        /// How much window habitability (0..1) the resource pass may give up versus the most habitable site
        /// available, so stone and wood never drag a race into a biome it cannot live in.
        /// </summary>
        private const float HabitabilityBudget = 0.3f;

        /// <summary>Flags a town center should never be built over (it would bury the deposit for good).</summary>
        private const TileFlags Reserved = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit
            | TileFlags.CoalDeposit | TileFlags.OilDeposit | TileFlags.Sacred;

        private const byte NotCandidate = 0;
        private const byte TileCandidate = 1;      // the start tile itself is usable
        private const byte FootprintCandidate = 2; // ... and so is the whole 2x2 town-center footprint

        private sealed class Grids
        {
            public WorldMap Map;
            public int Stride;
            public int[] BuildI, FreshI, TreeI, StoneI;
            public double[] FertI, HabI;
            public byte[] Class;
            public ulong Salt;
        }

        private struct Pass
        {
            public bool RequireBuildable; // >= MinBuildableTiles in the window and a free 2x2 footprint
            public bool RequireResources; // stone + trees in reach
            public float MinSpacing;
            public float SpreadScale;
            public float MinHabitability; // window-average habitability floor (resource pass only)
        }

        /// <summary>
        /// Returns exactly one start tile per entry of <paramref name="races"/> (same order). Tiles are
        /// distinct, free and buildable whenever the map has enough such tiles; otherwise falls back to
        /// any buildable tile, then the map center. <paramref name="db"/> may be null (uniform habitability).
        /// </summary>
        public static Vec2i[] Find(WorldMap map, GameDatabase db, IReadOnlyList<RaceId> races, Rng rng)
        {
            Guard.NotNull(map, nameof(map));
            if (races == null || races.Count == 0) return new Vec2i[0];

            int w = map.Width, h = map.Height, n = map.TileCount;
            int stride = w + 1;
            int isz = stride * (h + 1);
            var g = new Grids
            {
                Map = map,
                Stride = stride,
                BuildI = new int[isz],
                FreshI = new int[isz],
                TreeI = new int[isz],
                StoneI = new int[isz],
                FertI = new double[isz],
                HabI = new double[isz],
                Class = new byte[n],
                Salt = rng != null ? rng.NextULong() : 0x5EED5EEDUL,
            };
            int landTiles = 0;

            for (int y = 0; y < h; y++)
            {
                int rowB = 0, rowF = 0, rowT = 0, rowS = 0;
                double rowFe = 0;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    Biome b = map.Biomes[i];
                    TileFlags f = map.Flags[i];
                    bool free = map.IsFreeBuildable(i);
                    if (b.IsLand()) landTiles++;
                    rowB += free ? 1 : 0;
                    rowF += ((f & (TileFlags.River | TileFlags.Coastal)) != 0 || b == Biome.Lake) ? 1 : 0;
                    rowT += (f & TileFlags.Trees) != 0 ? 1 : 0;
                    rowS += (f & TileFlags.StoneDeposit) != 0 ? 1 : 0;
                    rowFe += b.IsLand() ? MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i], 0f)) : 0.0;
                    int o = (y + 1) * stride + x + 1, up = y * stride + x + 1;
                    g.BuildI[o] = g.BuildI[up] + rowB;
                    g.FreshI[o] = g.FreshI[up] + rowF;
                    g.TreeI[o] = g.TreeI[up] + rowT;
                    g.StoneI[o] = g.StoneI[up] + rowS;
                    g.FertI[o] = g.FertI[up] + rowFe;
                    g.Class[i] = IsSiteTile(map, i) ? TileCandidate : NotCandidate;
                }
            }
            for (int y = 0; y + TownCenterSize <= h; y++)
                for (int x = 0; x + TownCenterSize <= w; x++)
                {
                    int i = y * w + x;
                    if (g.Class[i] == NotCandidate) continue;
                    bool ok = true;
                    for (int dy = 0; dy < TownCenterSize && ok; dy++)
                        for (int dx = 0; dx < TownCenterSize; dx++)
                            if (g.Class[i + dy * w + dx] == NotCandidate) { ok = false; break; }
                    if (ok) g.Class[i] = FootprintCandidate;
                }

            var result = new Vec2i[races.Count];
            var chosen = new List<Vec2i>(races.Count);
            var hab = new float[Counts.Biomes];
            float spacing = 0.6f * (float)Math.Sqrt(Math.Max(1, landTiles) / (double)races.Count);
            spacing = MathUtil.Clamp(spacing, 4f, Math.Max(w, h));

            for (int k = 0; k < races.Count; k++)
            {
                FillHabitability(db, races[k], hab);
                BuildHabitabilityIntegral(map, hab, g.HabI, stride);

                // Passes 1-2: buildable window + footprint, with stone and trees in reach when the race can
                // have them without leaving its habitat (scoring still prefers resources otherwise).
                int best = SearchBuildable(g, chosen, k, spacing);
                // Pass 3: any usable tile, preferring larger buildable windows.
                if (best < 0)
                    best = SearchBest(g, chosen, k, new Pass { RequireBuildable = false, RequireResources = false, MinSpacing = 1f, SpreadScale = spacing }, out _);
                Vec2i pick;
                if (best >= 0) pick = map.PosOf(best);
                else pick = FallbackTile(map, chosen);
                result[k] = pick;
                chosen.Add(pick);
            }
            return result;
        }

        /// <summary>Free buildable land, not a river, unowned, and not covering a deposit or sacred site.</summary>
        private static bool IsSiteTile(WorldMap map, int i) =>
            map.IsFreeBuildable(i) && (map.Flags[i] & (TileFlags.River | Reserved)) == 0 && map.Owner[i] < 0;

        /// <summary>
        /// Search over sites with a full buildable window and a free town-center footprint, relaxing the
        /// minimum spacing by <see cref="SpacingRelax"/> per retry. For the first
        /// <see cref="ResourcePassSpacingSteps"/> spacings it also tries to satisfy the resource requirement,
        /// accepting only resource-qualified sites whose habitability is within <see cref="HabitabilityBudget"/>
        /// of the best site open at that spacing (so stone and wood never drag a race into a biome it cannot
        /// live in). Otherwise it returns the best open site at the widest spacing that has one.
        /// </summary>
        private static int SearchBuildable(Grids g, List<Vec2i> chosen, int raceIndex, float spacing)
        {
            var pass = new Pass { RequireBuildable = true, MinSpacing = spacing, SpreadScale = spacing };
            int steps = chosen.Count == 0 ? 1 : int.MaxValue; // spacing is irrelevant for the first race
            int fallback = -1;
            for (int step = 0; step < steps && pass.MinSpacing >= 2f; step++, pass.MinSpacing *= SpacingRelax)
            {
                pass.RequireResources = false;
                pass.MinHabitability = float.NegativeInfinity;
                int open = SearchBest(g, chosen, raceIndex, pass, out float maxHab);
                if (open < 0) continue;
                if (fallback < 0) fallback = open;
                if (step < ResourcePassSpacingSteps)
                {
                    pass.RequireResources = true;
                    pass.MinHabitability = maxHab - HabitabilityBudget;
                    int stocked = SearchBest(g, chosen, raceIndex, pass, out _);
                    if (stocked >= 0) return stocked;
                }
                if (step + 1 >= ResourcePassSpacingSteps) return fallback;
            }
            return fallback;
        }

        private static void FillHabitability(GameDatabase db, RaceId race, float[] hab)
        {
            RaceDefinition def = null;
            if (db != null)
            {
                int ri = (int)race;
                if (ri >= 0 && ri < db.Races.Count) def = db.Races[ri];
            }
            for (int b = 0; b < hab.Length; b++)
            {
                float v;
                if (def != null && def.BiomeHabitability != null && b < def.BiomeHabitability.Length) v = def.BiomeHabitability[b];
                else v = ((Biome)b).IsWater() ? 0f : 0.5f;
                hab[b] = MathUtil.Clamp01(MathUtil.Sanitize(v, 0f));
            }
        }

        private static void BuildHabitabilityIntegral(WorldMap map, float[] hab, double[] habI, int stride)
        {
            int w = map.Width, h = map.Height;
            for (int y = 0; y < h; y++)
            {
                double row = 0;
                for (int x = 0; x < w; x++)
                {
                    int bi = (int)map.Biomes[y * w + x];
                    row += bi >= 0 && bi < hab.Length ? hab[bi] : 0f;
                    habI[(y + 1) * stride + x + 1] = habI[y * stride + x + 1] + row;
                }
            }
        }

        private static int BoxI(int[] I, int stride, int x0, int y0, int x1, int y1) =>
            I[(y1 + 1) * stride + x1 + 1] - I[y0 * stride + x1 + 1] - I[(y1 + 1) * stride + x0] + I[y0 * stride + x0];

        private static double BoxD(double[] I, int stride, int x0, int y0, int x1, int y1) =>
            I[(y1 + 1) * stride + x1 + 1] - I[y0 * stride + x1 + 1] - I[(y1 + 1) * stride + x0] + I[y0 * stride + x0];

        /// <summary>Sum of an integral image over the Chebyshev window of radius r around (x, y), clipped to the map.</summary>
        private static int Around(int[] I, Grids g, int x, int y, int r)
        {
            int w = g.Map.Width, h = g.Map.Height;
            return BoxI(I, g.Stride, Math.Max(0, x - r), Math.Max(0, y - r), Math.Min(w - 1, x + r), Math.Min(h - 1, y + r));
        }

        /// <summary>Best-scoring site for the pass, or -1. <paramref name="maxHab"/> = best window habitability among sites passing every filter except the resource ones.</summary>
        private static int SearchBest(Grids g, List<Vec2i> chosen, int raceIndex, Pass pass, out float maxHab)
        {
            maxHab = float.NegativeInfinity;
            WorldMap map = g.Map;
            int w = map.Width, h = map.Height, stride = g.Stride;
            byte minClass = pass.RequireBuildable ? FootprintCandidate : TileCandidate;
            float bestScore = float.NegativeInfinity;
            int best = -1;
            float min2 = pass.MinSpacing * pass.MinSpacing;
            float spreadNorm = 1f / Math.Max(1f, pass.SpreadScale * 2f);
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Max(0, y - Radius), y1 = Math.Min(h - 1, y + Radius);
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (g.Class[i] < minClass) continue;
                    int x0 = Math.Max(0, x - Radius), x1 = Math.Min(w - 1, x + Radius);
                    int buildable = BoxI(g.BuildI, stride, x0, y0, x1, y1);
                    if (pass.RequireBuildable && buildable < MinBuildableTiles) continue;

                    float nearest2 = float.PositiveInfinity;
                    for (int c = 0; c < chosen.Count; c++)
                    {
                        int dx = chosen[c].X - x, dy = chosen[c].Y - y;
                        float d2 = dx * dx + dy * dy;
                        if (d2 < nearest2) nearest2 = d2;
                    }
                    if (nearest2 < 1f || (chosen.Count > 0 && nearest2 < min2)) continue;

                    float cells = (x1 - x0 + 1) * (y1 - y0 + 1);
                    float habAvg = (float)(BoxD(g.HabI, stride, x0, y0, x1, y1) / cells);
                    if (habAvg > maxHab) maxHab = habAvg;

                    int treesNear = BoxI(g.TreeI, stride, x0, y0, x1, y1);
                    int treesWide = Around(g.TreeI, g, x, y, TreeRadius);
                    int stone = Around(g.StoneI, g, x, y, StoneRadius);
                    if (pass.RequireResources && (habAvg < pass.MinHabitability || stone < MinStoneDeposits
                        || treesWide < MinTreesInTreeRadius || treesNear < MinTreesInRadius))
                        continue;

                    float score = habAvg;
                    score += FreshWaterBonus * Math.Min(1f, BoxI(g.FreshI, stride, x0, y0, x1, y1) / 4f);
                    // Resource terms saturate at the first-pass requirement so that, among qualifying sites,
                    // habitability still decides; they matter most when a pass had to drop the requirement.
                    score += TreeBonus * Math.Min(1f, treesWide / (float)MinTreesInTreeRadius);
                    score += StoneBonus * Math.Min(1f, stone / 2f);
                    score += FertilityBonus * (float)(BoxD(g.FertI, stride, x0, y0, x1, y1) / cells);
                    if (!pass.RequireBuildable) score += 0.5f * Math.Min(1f, buildable / (float)MinBuildableTiles);
                    if (chosen.Count > 0) score += SpreadBonus * Math.Min(1f, (float)Math.Sqrt(nearest2) * spreadNorm);
                    ulong hsh = Hashing.Combine(g.Salt, (ulong)i * 31UL + (ulong)raceIndex);
                    score += JitterScale * ((hsh >> 40) * (1f / 16777216f));
                    if (score > bestScore) { bestScore = score; best = i; }
                }
            }
            return best;
        }

        private static Vec2i FallbackTile(WorldMap map, List<Vec2i> chosen)
        {
            Vec2i center = new Vec2i(map.Width / 2, map.Height / 2);
            int best = -1;
            int bestD = int.MaxValue;
            for (int i = 0; i < map.TileCount; i++)
            {
                if (!map.Biomes[i].IsBuildable()) continue;
                Vec2i p = map.PosOf(i);
                bool taken = false;
                for (int c = 0; c < chosen.Count; c++) if (chosen[c] == p) { taken = true; break; }
                if (taken) continue;
                int d = p.DistanceSqTo(center);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best >= 0 ? map.PosOf(best) : center;
        }
    }
}
