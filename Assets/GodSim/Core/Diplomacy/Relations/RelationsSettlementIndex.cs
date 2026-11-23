using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Per-run snapshot of alive settlement centers grouped by alive civilization, with bounding boxes
    /// for cheap rejection. Rebuilt by each diplomacy system at the start of its run; storage is pooled
    /// so steady-state rebuilds do not allocate. Civ order follows <see cref="WorldState.Civilizations"/>.
    /// </summary>
    internal sealed class RelationsSettlementIndex
    {
        private sealed class Slot
        {
            public int CivId;
            public readonly List<Vec2i> Centers = new List<Vec2i>();
            public int MinX, MinY, MaxX, MaxY;
        }

        private static readonly List<Vec2i> Empty = new List<Vec2i>();

        private readonly Dictionary<int, Slot> _byCiv = new Dictionary<int, Slot>();
        private readonly List<Slot> _pool = new List<Slot>();
        private int _used;

        /// <summary>Alive civs owning at least one alive settlement, in civilization store order.</summary>
        public readonly List<int> CivIds = new List<int>();

        public void Rebuild(WorldState state)
        {
            _byCiv.Clear();
            CivIds.Clear();
            _used = 0;
            if (state == null) return;
            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;
                if (!_byCiv.TryGetValue(civ.Id, out var slot))
                {
                    slot = Rent(civ.Id);
                    _byCiv[civ.Id] = slot;
                    slot.MinX = slot.MaxX = s.Center.X;
                    slot.MinY = slot.MaxY = s.Center.Y;
                }
                slot.Centers.Add(s.Center);
                if (s.Center.X < slot.MinX) slot.MinX = s.Center.X;
                if (s.Center.X > slot.MaxX) slot.MaxX = s.Center.X;
                if (s.Center.Y < slot.MinY) slot.MinY = s.Center.Y;
                if (s.Center.Y > slot.MaxY) slot.MaxY = s.Center.Y;
            }
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (civs[i] != null && _byCiv.ContainsKey(civs[i].Id)) CivIds.Add(civs[i].Id);
        }

        /// <summary>Alive settlement centers of a civ (empty if none or dead).</summary>
        public IReadOnlyList<Vec2i> Centers(int civId) => _byCiv.TryGetValue(civId, out var s) ? s.Centers : Empty;

        /// <summary>Number of alive settlements of an alive civ.</summary>
        public int SettlementCount(int civId) => _byCiv.TryGetValue(civId, out var s) ? s.Centers.Count : 0;

        /// <summary>True if any settlement of a is within range (Chebyshev) of any settlement of b.</summary>
        public bool AnyWithin(int a, int b, int range)
        {
            if (!_byCiv.TryGetValue(a, out var sa) || !_byCiv.TryGetValue(b, out var sb)) return false;
            if (BoxDistance(sa, sb) > range) return false;
            var ca = sa.Centers; var cb = sb.Centers;
            for (int i = 0; i < ca.Count; i++)
                for (int j = 0; j < cb.Count; j++)
                    if (ca[i].ChebyshevTo(cb[j]) <= range) return true;
            return false;
        }

        /// <summary>Number of (settlement of a, settlement of b) pairs within range (Chebyshev).</summary>
        public int CountPairsWithin(int a, int b, int range)
        {
            if (!_byCiv.TryGetValue(a, out var sa) || !_byCiv.TryGetValue(b, out var sb)) return 0;
            if (BoxDistance(sa, sb) > range) return 0;
            int n = 0;
            var ca = sa.Centers; var cb = sb.Centers;
            for (int i = 0; i < ca.Count; i++)
                for (int j = 0; j < cb.Count; j++)
                    if (ca[i].ChebyshevTo(cb[j]) <= range) n++;
            return n;
        }

        /// <summary>Midpoint of the closest settlement pair of two civs, or Vec2i.Invalid.</summary>
        public Vec2i ClosestMidpoint(int a, int b)
        {
            if (!_byCiv.TryGetValue(a, out var sa) || !_byCiv.TryGetValue(b, out var sb)) return Vec2i.Invalid;
            int best = int.MaxValue;
            Vec2i mid = Vec2i.Invalid;
            var ca = sa.Centers; var cb = sb.Centers;
            for (int i = 0; i < ca.Count; i++)
                for (int j = 0; j < cb.Count; j++)
                {
                    int d = ca[i].ChebyshevTo(cb[j]);
                    if (d < best) { best = d; mid = new Vec2i((ca[i].X + cb[j].X) / 2, (ca[i].Y + cb[j].Y) / 2); }
                }
            return mid;
        }

        private static int BoxDistance(Slot a, Slot b)
        {
            int dx = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            int dy = Math.Max(0, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
            return Math.Max(dx, dy);
        }

        private Slot Rent(int civId)
        {
            Slot s;
            if (_used < _pool.Count) { s = _pool[_used]; s.Centers.Clear(); }
            else { s = new Slot(); _pool.Add(s); }
            _used++;
            s.CivId = civId;
            return s;
        }
    }
}
