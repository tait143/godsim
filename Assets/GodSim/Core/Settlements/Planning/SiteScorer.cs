using System;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Soft (preference) scoring of a candidate site that already passed the hard rules. Higher is better;
    /// typical range 0..2. Combines: location within the settlement (Central / Outskirts / Isolated),
    /// zoning (dwellings cluster near dwellings and civic buildings with positive desirability, away from
    /// negative-desirability industry), road adjacency, race style (compactness, Elves near trees, Saurians
    /// near water, Dwarves near hills, Humans on a road grid), resource proximity for extractors, and a small
    /// caller-supplied deterministic jitter.
    /// </summary>
    public static class SiteScorer
    {
        /// <summary>Spacing of the planned road grid (Humans / high RoadAffinity).</summary>
        public const int GridSpacing = 4;

        /// <summary>Score penalty for a farm footprint fully covering existing fields (scaled by the covered share).</summary>
        public const float FieldReusePenalty = 0.3f;

        /// <summary>Scores a site. <paramref name="radius"/> is the planning radius; <paramref name="jitter"/> is added verbatim.</summary>
        public static float Score(WorldMap map, Settlement s, RaceDefinition race, BuildingDefinition def, Vec2i origin,
            in SiteProbe probe, ZoningMap zoning, int radius, float jitter)
        {
            if (s == null || def == null) return 0f;
            int w = MathUtil.Clamp(def.Width, 1, 8), h = MathUtil.Clamp(def.Height, 1, 8);
            float compact = race != null ? MathUtil.Clamp01(MathUtil.Sanitize(race.Compactness, 0.5f)) : 0.5f;
            float roadAffinity = race != null ? MathUtil.Clamp01(MathUtil.Sanitize(race.RoadAffinity, 0.5f)) : 0.5f;
            var rule = def.Placement;

            float fx = origin.X + (w - 1) * 0.5f - s.Center.X;
            float fy = origin.Y + (h - 1) * 0.5f - s.Center.Y;
            float norm = MathUtil.Clamp01((float)Math.Sqrt(fx * fx + fy * fy) / Math.Max(1f, radius));

            float residential = zoning != null ? zoning.AverageResidential(origin, w, h) : 0f;
            float desirability = zoning != null ? zoning.AverageDesirability(origin, w, h) : 0f;
            float density = MathUtil.Clamp01(residential / 1.5f);

            float score = 0f;

            // 1) location within the settlement
            if ((rule & PlacementRule.Central) != 0) score += 1f - norm;
            else if ((rule & PlacementRule.Outskirts) != 0) score += 1f - MathUtil.Clamp01(Math.Abs(norm - 0.7f) / 0.7f);
            else if ((rule & PlacementRule.Isolated) != 0) score += 0.7f * (1f - density) + 0.3f * norm;
            else score += (0.35f + 0.4f * compact) * (1f - norm);

            // 2) zoning
            if (ZoningMap.IsResidentialAnchor(def))
                score += 0.3f * MathUtil.Clamp(desirability, -1f, 1f) + 0.2f * density;
            else if (def.Desirability < -0.05f && (rule & PlacementRule.Isolated) == 0)
                score -= 0.35f * density;

            // 3) roads
            if (probe.RoadAdjacent > 0) score += 0.15f;
            score += 0.05f * MathUtil.Clamp01(probe.RoadNear2 / 4f);

            // 4) compactness: dense races like neighbors nearby, sprawling races keep gaps
            score += 0.12f * compact * MathUtil.Clamp01(probe.OccupiedNear2 / 6f);
            score -= 0.15f * (1f - compact) * MathUtil.Clamp01(probe.OccupiedAdjacent / 3f);

            // 5) racial taste
            switch (s.Race)
            {
                case RaceId.Elf:
                    score += 0.15f * MathUtil.Clamp01(probe.TreesNear4 / 8f);
                    break;
                case RaceId.Saurian:
                    score += 0.15f * MathUtil.Clamp01(probe.WaterOrRiverNear3 / 6f);
                    break;
                case RaceId.Dwarf:
                    score += 0.15f * MathUtil.Clamp01((probe.HillsNear3 + probe.MountainNear3) / 8f);
                    break;
            }

            // 6) grid alignment (Humans, or any race with high road affinity): sit between lanes, keep lanes free
            float grid = s.Race == RaceId.Human ? roadAffinity : roadAffinity * 0.5f;
            if (grid >= 0.5f && w < GridSpacing && h < GridSpacing)
            {
                int lanes = CountLaneTiles(origin, w, h, s.Center);
                if (lanes == 0) score += 0.2f * grid;
                else score -= 0.25f * grid * MathUtil.Clamp01(lanes * 2f / (w * h));
            }

            // 7) resource proximity for extractors / water users
            float res = 0f;
            if ((rule & PlacementRule.NearOre) != 0) res = Math.Max(res, probe.OreNear3 / 6f);
            if ((rule & PlacementRule.NearStone) != 0) res = Math.Max(res, probe.StoneNear3 / 6f);
            if ((rule & PlacementRule.NearGold) != 0) res = Math.Max(res, probe.GoldNear3 / 6f);
            if ((rule & PlacementRule.NearCoal) != 0) res = Math.Max(res, probe.CoalNear3 / 6f);
            if ((rule & PlacementRule.NearOil) != 0) res = Math.Max(res, probe.OilNear3 / 6f);
            if ((rule & PlacementRule.NearForest) != 0) res = Math.Max(res, probe.TreesNear4 / 12f);
            if ((rule & PlacementRule.NearWater) != 0) res = Math.Max(res, probe.WaterOrRiverNear3 / 8f);
            if ((rule & PlacementRule.NearRiver) != 0) res = Math.Max(res, probe.RiverNear3 / 6f);
            if ((rule & PlacementRule.NearMountain) != 0) res = Math.Max(res, probe.MountainNear3 / 8f);
            if ((rule & PlacementRule.Coastal) != 0) res = Math.Max(res, probe.AdjacentWater / 3f);
            score += 0.3f * MathUtil.Clamp01(res);
            if ((rule & PlacementRule.Fertile) != 0) score += 0.3f * MathUtil.Clamp01(probe.AverageFertility);

            // 8) farms may reuse fields, but should not swallow a neighbouring live farm's fields when land is free
            if (def.Type == BuildingType.Farm && probe.FarmlandInFootprint > 0 && probe.Area > 0)
                score -= FieldReusePenalty * MathUtil.Clamp01(probe.FarmlandInFootprint / (float)probe.Area);

            score += MathUtil.Sanitize(jitter);
            return MathUtil.Sanitize(score, 0f);
        }

        /// <summary>True if the tile lies on a road-grid lane (spacing <see cref="GridSpacing"/>, anchored on the center).</summary>
        public static bool IsGridLane(int x, int y, Vec2i center) =>
            Mod(x - center.X, GridSpacing) == 0 || Mod(y - center.Y, GridSpacing) == 0;

        /// <summary>Counts footprint tiles that lie on grid lanes.</summary>
        public static int CountLaneTiles(Vec2i origin, int w, int h, Vec2i center)
        {
            int c = 0;
            for (int y = origin.Y; y < origin.Y + h; y++)
                for (int x = origin.X; x < origin.X + w; x++)
                    if (IsGridLane(x, y, center)) c++;
            return c;
        }

        private static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
    }
}
