using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Bounded map scan around a settlement (circle of TerritoryRadius + <see cref="Margin"/>, clamped to
    /// [<see cref="MinRadius"/>, <see cref="MaxRadius"/>]) that measures the geography the frontier advisor needs:
    /// foreign territory and settlements nearby, water/coast share, unclaimed land, deposits and trees. Results are
    /// public fields on a reusable instance (no allocations per scan). <see cref="Classify"/> turns them into a role.
    /// </summary>
    public sealed class FrontierScanner
    {
        public const int Margin = 4;
        public const int MinRadius = 4;
        public const int MaxRadius = 24;
        public const int MaxNeighbours = 4;
        private const int MaxCandidates = 16;

        // ------------------------------------------------------------------ results of the last scan
        public int Radius, Scanned, Land, Water, Coast, Own, Unclaimed, Foreign, Sibling;
        /// <summary>Usable (own or unclaimed) tiles per deposit kind (FrontierCatalog kinds).</summary>
        public readonly int[] Deposits = new int[FrontierCatalog.KindCount];
        /// <summary>0..1 closeness of the nearest foreign settlement (0 = none in reach).</summary>
        public float Proximity;
        /// <summary>Distance (tiles) to the nearest other settlement of the same civ; float.MaxValue when none.</summary>
        public float NearestOwnDistance;
        /// <summary>Most relevant neighbouring civs (by shared border + proximity), best first.</summary>
        public readonly int[] NeighbourCiv = new int[MaxNeighbours];
        public readonly float[] NeighbourScore = new float[MaxNeighbours];
        public int NeighbourCount;
        /// <summary>Role scores (0..1+) of the last classification, indexed by FrontierRoles.</summary>
        public readonly float[] Scores = new float[FrontierRoles.Count];

        private readonly int[] _candCiv = new int[MaxCandidates];
        private readonly float[] _candScore = new float[MaxCandidates];
        private int _candCount;

        // ------------------------------------------------------------------ derived ratios
        public float ForeignShare => Scanned > 0 ? Foreign / (float)Scanned : 0f;
        public float WaterShare => Scanned > 0 ? Water / (float)Scanned : 0f;
        public float WildShare => Land > 0 ? Unclaimed / (float)Land : 0f;
        public float SiblingShare => Scanned > 0 ? Sibling / (float)Scanned : 0f;

        /// <summary>Bit mask of deposit kinds with at least one usable tile.</summary>
        public int DepositMask
        {
            get
            {
                int m = 0;
                for (int k = 0; k < FrontierCatalog.KindCount; k++) if (Deposits[k] > 0) m |= 1 << k;
                return m;
            }
        }

        /// <summary>Weighted deposit richness (trees excluded).</summary>
        public float DepositWealth =>
            Deposits[FrontierCatalog.KindOre] * 1.5f + Deposits[FrontierCatalog.KindStone] * 1f + Deposits[FrontierCatalog.KindGold] * 2.5f +
            Deposits[FrontierCatalog.KindCoal] * 1.5f + Deposits[FrontierCatalog.KindOil] * 2f;

        private void Reset()
        {
            Radius = Scanned = Land = Water = Coast = Own = Unclaimed = Foreign = Sibling = 0;
            for (int k = 0; k < Deposits.Length; k++) Deposits[k] = 0;
            Proximity = 0f;
            NearestOwnDistance = float.MaxValue;
            NeighbourCount = 0;
            for (int i = 0; i < MaxNeighbours; i++) { NeighbourCiv[i] = -1; NeighbourScore[i] = 0f; }
            for (int i = 0; i < Scores.Length; i++) Scores[i] = 0f;
            _candCount = 0;
        }

        /// <summary>Scans the neighbourhood of a settlement. Null-safe (leaves an empty result).</summary>
        public void Scan(WorldState state, Settlement s)
        {
            Reset();
            if (state == null || state.Map == null || s == null) return;
            var map = state.Map;
            int radius = MathUtil.Clamp(s.TerritoryRadius + Margin, MinRadius, MaxRadius);
            Radius = radius;
            var c = map.ClampToBounds(s.Center.IsValid ? s.Center : Vec2i.Zero);
            int r2 = radius * radius;
            int lastOwner = int.MinValue, lastCiv = -1;

            int y0 = Math.Max(0, c.Y - radius), y1 = Math.Min(map.Height - 1, c.Y + radius);
            int x0 = Math.Max(0, c.X - radius), x1 = Math.Min(map.Width - 1, c.X + radius);
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - c.Y;
                for (int x = x0; x <= x1; x++)
                {
                    int dx = x - c.X;
                    if (dx * dx + dy * dy > r2) continue;
                    Scanned++;
                    int i = map.Index(x, y);
                    if (map.Biomes[i].IsWater()) { Water++; continue; }
                    Land++;
                    int owner = map.Owner[i];
                    bool usable;
                    if (owner == s.Id) { Own++; usable = true; }
                    else if (owner < 0) { Unclaimed++; usable = true; }
                    else
                    {
                        if (owner != lastOwner)
                        {
                            lastOwner = owner;
                            var os = state.Settlements.Get(owner);
                            lastCiv = os != null && os.IsAlive ? os.CivilizationId : -1;
                        }
                        usable = false;
                        if (lastCiv >= 0 && lastCiv != s.CivilizationId) { Foreign++; AddCandidate(lastCiv, 1f); }
                        else if (lastCiv == s.CivilizationId) Sibling++;
                    }
                    if (!usable) continue;
                    var flags = map.Flags[i];
                    if ((flags & TileFlags.Coastal) != 0 || TouchesWater(map, x, y)) Coast++;
                    for (int k = 0; k < FrontierCatalog.KindCount; k++)
                        if ((flags & FrontierCatalog.KindFlags[k]) != 0) Deposits[k]++;
                }
            }

            // Settlements in reach (also catches neighbours whose territory does not touch ours yet).
            var items = state.Settlements.Items;
            for (int n = 0; n < items.Count; n++)
            {
                var o = items[n];
                if (o == null || !o.IsAlive || o.Id == s.Id) continue;
                float ddx = o.Center.X - c.X, ddy = o.Center.Y - c.Y;
                float d = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                if (o.CivilizationId == s.CivilizationId)
                {
                    if (d < NearestOwnDistance) NearestOwnDistance = d;
                    continue;
                }
                var oc = state.Civilizations.Get(o.CivilizationId);
                if (oc == null || !oc.IsAlive) continue;
                float limit = s.TerritoryRadius + o.TerritoryRadius + 8f;
                if (d >= limit) continue;
                float p = MathUtil.Clamp01(1f - d / limit);
                if (p > Proximity) Proximity = p;
                AddCandidate(o.CivilizationId, p * 40f);
            }

            // Top neighbours by score (ties: lower civ id), deterministic selection.
            for (int slot = 0; slot < MaxNeighbours; slot++)
            {
                int best = -1;
                for (int k = 0; k < _candCount; k++)
                {
                    if (_candScore[k] <= 0f) continue;
                    if (best < 0 || _candScore[k] > _candScore[best] || (_candScore[k] == _candScore[best] && _candCiv[k] < _candCiv[best])) best = k;
                }
                if (best < 0) break;
                NeighbourCiv[slot] = _candCiv[best];
                NeighbourScore[slot] = _candScore[best];
                _candScore[best] = -1f;
                NeighbourCount++;
            }
        }

        private void AddCandidate(int civId, float score)
        {
            for (int k = 0; k < _candCount; k++)
                if (_candCiv[k] == civId) { _candScore[k] += score; return; }
            if (_candCount >= MaxCandidates) return;
            _candCiv[_candCount] = civId;
            _candScore[_candCount] = score;
            _candCount++;
        }

        private static bool TouchesWater(WorldMap map, int x, int y)
        {
            if (x > 0 && map.Biomes[map.Index(x - 1, y)].IsWater()) return true;
            if (x < map.Width - 1 && map.Biomes[map.Index(x + 1, y)].IsWater()) return true;
            if (y > 0 && map.Biomes[map.Index(x, y - 1)].IsWater()) return true;
            if (y < map.Height - 1 && map.Biomes[map.Index(x, y + 1)].IsWater()) return true;
            return false;
        }

        /// <summary>
        /// Scores every role from the last scan and returns the primary role. The current role gets a small
        /// hysteresis bonus so towns do not flip-flop; ties resolve frontier &gt; coastal &gt; resource &gt; wilderness
        /// &gt; heartland. <paramref name="secondary"/> is the runner-up non-heartland role when it is strong (else -1).
        /// </summary>
        public int Classify(Settlement s, int currentRole, out int secondary)
        {
            secondary = -1;
            if (s == null) return FrontierRoles.Heartland;

            float frontier = NeighbourCount == 0 ? 0f : MathUtil.Clamp01(ForeignShare * 5f + Proximity * 0.9f);
            float ws = WaterShare;
            float coastal = ws < 0.06f || Coast < 4 ? 0f : MathUtil.Clamp01((ws - 0.06f) * 3f + 0.25f + Coast / 40f);
            float resource = MathUtil.Clamp01(DepositWealth / 12f);
            float isolation;
            if (NearestOwnDistance == float.MaxValue) isolation = s.IsCapital ? 0f : 1f;
            else isolation = MathUtil.Clamp01(NearestOwnDistance / (3f * Math.Max(1, Radius)));
            float tierFactor = s.Tier <= SettlementTier.Hamlet ? 1f : s.Tier == SettlementTier.Village ? 0.8f : 0.5f;
            float wilderness = MathUtil.Clamp01((WildShare - 0.3f) * 1.4f + isolation * 0.35f) * tierFactor * (s.IsCapital ? 0.35f : 1f);
            float heartland = 0.35f + (s.IsCapital ? 0.1f : 0f) + MathUtil.Clamp01(SiblingShare * 2f) * 0.3f;

            Scores[FrontierRoles.Heartland] = heartland;
            Scores[FrontierRoles.Frontier] = frontier;
            Scores[FrontierRoles.CoastalGateway] = coastal;
            Scores[FrontierRoles.WildernessOutpost] = wilderness;
            Scores[FrontierRoles.ResourceTown] = resource;
            if (currentRole >= 0 && currentRole < FrontierRoles.Count) Scores[currentRole] += 0.1f;

            int best = -1;
            for (int n = 0; n < s_priority.Length; n++)
            {
                int r = s_priority[n];
                if (best < 0 || Scores[r] > Scores[best]) best = r;
            }
            int second = -1;
            for (int n = 0; n < s_priority.Length; n++)
            {
                int r = s_priority[n];
                if (r == best || r == FrontierRoles.Heartland) continue;
                if (second < 0 || Scores[r] > Scores[second]) second = r;
            }
            if (second >= 0 && Scores[second] >= 0.45f) secondary = second;
            return best;
        }

        private static readonly int[] s_priority =
        {
            FrontierRoles.Frontier, FrontierRoles.CoastalGateway, FrontierRoles.ResourceTown, FrontierRoles.WildernessOutpost, FrontierRoles.Heartland
        };
    }
}
