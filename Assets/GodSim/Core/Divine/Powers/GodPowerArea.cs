using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// Area queries shared by the divine power effects. Every query is bounded by the cast radius and the
    /// map bounds (tile scans) or by the settlement list plus the residents of nearby settlements (people),
    /// so no effect ever walks the whole map or the whole population. Iteration is in deterministic order.
    /// </summary>
    internal static class GodPowerArea
    {
        /// <summary>Extra tiles beyond the cast radius at which a settlement center still counts as "in range".</summary>
        public const int SettlementReach = 3;
        /// <summary>Absolute safety cap on any effect radius, whatever the definition says.</summary>
        public const int HardMaxRadius = 32;

        public static int ClampRadius(int radius) => MathUtil.Clamp(radius, 0, HardMaxRadius);

        /// <summary>Square bounds of a circle, clipped to the map.</summary>
        public static void Bounds(WorldMap map, Vec2i c, int r, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = MathUtil.Clamp(c.X - r, 0, map.Width - 1);
            y0 = MathUtil.Clamp(c.Y - r, 0, map.Height - 1);
            x1 = MathUtil.Clamp(c.X + r, 0, map.Width - 1);
            y1 = MathUtil.Clamp(c.Y + r, 0, map.Height - 1);
        }

        public static bool InCircle(Vec2i c, int x, int y, int r)
        {
            int dx = x - c.X, dy = y - c.Y;
            return dx * dx + dy * dy <= r * r;
        }

        public static bool InCircle(Vec2i c, Vec2i p, int r) => InCircle(c, p.X, p.Y, r);

        /// <summary>0 at the center .. 1 at distance r (clamped). r = 0 yields 0 everywhere.</summary>
        public static float NormalizedDistance(Vec2i c, int x, int y, int r)
        {
            if (r <= 0) return 0f;
            int dx = x - c.X, dy = y - c.Y;
            return MathUtil.Clamp01((float)System.Math.Sqrt(dx * dx + dy * dy) / r);
        }

        /// <summary>Alive settlements whose center lies within radius + reach of c (Euclidean).</summary>
        public static void SettlementsInRange(WorldState s, Vec2i c, int radius, int reach, List<Settlement> into)
        {
            into.Clear();
            int r = radius + reach;
            long r2 = (long)r * r;
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (st == null || !st.IsAlive) continue;
                if (st.Center.DistanceSqTo(c) <= r2) into.Add(st);
            }
        }

        /// <summary>True if any alive settlement center lies within radius + reach of c.</summary>
        public static bool AnySettlementInRange(WorldState s, Vec2i c, int radius, int reach)
        {
            int r = radius + reach;
            long r2 = (long)r * r;
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (st != null && st.IsAlive && st.Center.DistanceSqTo(c) <= r2) return true;
            }
            return false;
        }

        /// <summary>Nearest alive settlement within maxDistance of c, or null. Ties resolve to the earlier settlement.</summary>
        public static Settlement NearestSettlement(WorldState s, Vec2i c, int maxDistance)
        {
            Settlement best = null;
            long bestD = (long)maxDistance * maxDistance;
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (st == null || !st.IsAlive) continue;
                long d = st.Center.DistanceSqTo(c);
                if (d < bestD || (best == null && d == bestD)) { bestD = d; best = st; }
            }
            return best;
        }

        /// <summary>Name of the nearest settlement within maxDistance, or "the wilds".</summary>
        public static string PlaceName(WorldState s, Vec2i c, int maxDistance, out Settlement nearest)
        {
            nearest = NearestSettlement(s, c, maxDistance);
            return nearest != null && !string.IsNullOrEmpty(nearest.Name) ? nearest.Name : "the wilds";
        }

        /// <summary>
        /// Alive people whose position lies within radius of c. Only residents of settlements whose territory could
        /// reach the circle are examined, so cost is bounded by the area, not the world population.
        /// </summary>
        public static void PeopleInRange(WorldState s, Vec2i c, int radius, List<Person> into)
        {
            into.Clear();
            var all = s.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (st == null || !st.IsAlive) continue;
                int reach = radius + MathUtil.Clamp(st.TerritoryRadius, 0, 64) + 8;
                if (st.Center.DistanceSqTo(c) > (long)reach * reach) continue;
                var ids = st.ResidentIds;
                for (int k = 0; k < ids.Count; k++)
                {
                    var p = s.People.Get(ids[k]);
                    if (p == null || !p.IsAlive || !s.Map.InBounds(p.Position)) continue;
                    if (InCircle(c, p.Position, radius)) into.Add(p);
                }
            }
        }

        /// <summary>Alive residents of the given settlements (snapshot; safe to kill while iterating the result).</summary>
        public static void ResidentsOf(WorldState s, List<Settlement> settlements, List<Person> into)
        {
            into.Clear();
            for (int i = 0; i < settlements.Count; i++)
            {
                var ids = settlements[i].ResidentIds;
                for (int k = 0; k < ids.Count; k++)
                {
                    var p = s.People.Get(ids[k]);
                    if (p != null && p.IsAlive) into.Add(p);
                }
            }
        }

        /// <summary>Non-destroyed buildings with at least one footprint tile inside the circle (scan order, deduplicated).</summary>
        public static void BuildingsInRange(WorldState s, Vec2i c, int radius, List<Building> into)
        {
            into.Clear();
            var map = s.Map;
            Bounds(map, c, radius, out int x0, out int y0, out int x1, out int y1);
            HashSet<int> seen = null;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!InCircle(c, x, y, radius)) continue;
                    int id = map.BuildingAt[map.Index(x, y)];
                    if (id < 0) continue;
                    if (seen == null) seen = new HashSet<int>();
                    if (!seen.Add(id)) continue;
                    var b = s.Buildings.Get(id);
                    if (b != null && !b.IsDestroyed) into.Add(b);
                }
        }

        /// <summary>Distinct ids of alive civilizations owning the given settlements, in first-seen order.</summary>
        public static void CivsOf(WorldState s, List<Settlement> settlements, List<int> into)
        {
            into.Clear();
            for (int i = 0; i < settlements.Count; i++)
            {
                int id = settlements[i].CivilizationId;
                if (id < 0 || into.Contains(id)) continue;
                var civ = s.Civilizations.Get(id);
                if (civ != null && civ.IsAlive) into.Add(id);
            }
        }

        /// <summary>True if any 4-neighbor of (x, y) is water.</summary>
        public static bool HasWaterNeighbor4(WorldMap map, int x, int y)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + Vec2i.Dirs4[d].X, ny = y + Vec2i.Dirs4[d].Y;
                if (map.InBounds(nx, ny) && map.IsWater(map.Index(nx, ny))) return true;
            }
            return false;
        }

        /// <summary>Adds delta to a 0..1 wellbeing value, sanitizing NaN and clamping the result.</summary>
        public static float Add01(float value, float delta) => MathUtil.Clamp01(MathUtil.Sanitize(value, 0.5f) + delta);
    }
}
