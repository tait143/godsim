using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Assigns residents to dwellings. Per alive settlement it collects complete, standing dwellings
    /// (<see cref="BuildingDefinition.IsDwelling"/>; a House holds 3-5), repairs inconsistent links (a home
    /// that was destroyed, is unfinished, or belongs to another settlement; over-capacity houses), then
    /// houses the homeless: families first (spouses and their children together when capacity allows,
    /// children following their mother), then singles. Each household goes to the house with the most
    /// free space, nearest to the person. Writes <see cref="Settlement.Homeless"/> and the
    /// <see cref="SocietyKeys.FreeHousing"/> blackboard key (total free capacity) that the AI reads to
    /// keep building one house per 3-5 people. All links go through <see cref="WorldOps"/>.
    /// </summary>
    public sealed class HousingSystem : ISimSystem
    {
        /// <summary>Run interval in ticks.</summary>
        public const int Interval = 4;

        private readonly List<Building> _houses = new List<Building>();
        private readonly SocietyHouseholdBuffer _households = new SocietyHouseholdBuffer();
        private static readonly GroupComparer FamiliesFirst = new GroupComparer();

        /// <summary>Creates the system (no state is kept between runs except reusable buffers).</summary>
        public HousingSystem() { }

        /// <inheritdoc />
        public string Name => "Population.Housing";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                UpdateSettlement(ctx, s);
            }
        }

        /// <summary>Runs the full housing pass for a single settlement (also used by tests).</summary>
        public void UpdateSettlement(SimContext ctx, Settlement s)
        {
            var state = ctx.State;
            var db = ctx.Db;
            var ops = ctx.Ops;
            var people = state.People;

            // 1) Collect usable dwellings and repair their resident lists.
            _houses.Clear();
            int capacity = 0;
            var bids = s.BuildingIds;
            for (int i = 0; i < bids.Count; i++)
            {
                var b = state.Buildings.Get(bids[i]);
                if (!SocietyUtil.IsUsableDwelling(db, b, s.Id))
                {
                    // Unusable (unfinished/destroyed) dwelling that still lists residents: evict them.
                    if (b != null && b.Residents.Count > 0) EvictAll(ops, people, b);
                    continue;
                }
                RepairResidents(ops, people, b);
                _houses.Add(b);
                capacity += b.HousingCapacity;
            }

            // 2) Validate every resident's home; gather the homeless into households.
            _households.Clear();
            var residents = s.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive || p.SettlementId != s.Id) continue;
                if (p.HomeId >= 0)
                {
                    var h = state.Buildings.Get(p.HomeId);
                    if (!SocietyUtil.IsUsableDwelling(db, h, s.Id) || !h.Residents.Contains(p.Id)) ops.ClearHome(p);
                }
                if (p.HomeId < 0) _households.Add(p, SocietyUtil.HouseholdAnchor(p, people));
            }

            int free = 0;
            for (int i = 0; i < _houses.Count; i++) free += _houses[i].FreeHousing;

            // 3) House the homeless: largest households (families) first, then singles.
            if (free > 0 && _households.Members.Count > 0)
            {
                _households.BuildGroups(-1);
                var groups = _households.Groups;
                if (groups.Count > 1) groups.Sort(FamiliesFirst);
                for (int g = 0; g < groups.Count && free > 0; g++)
                    free -= HouseGroup(ops, people, groups[g]);
            }

            // 4) Statistics + blackboard.
            int homeless = 0;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p != null && p.IsAlive && p.HomeId < 0) homeless++;
            }
            free = 0;
            for (int i = 0; i < _houses.Count; i++) free += _houses[i].FreeHousing;
            s.Homeless = homeless;
            s.Memory[SocietyKeys.FreeHousing] = free;
            s.Memory[SocietyKeys.HousingCapacity] = capacity;
            s.Memory[SocietyKeys.Dwellings] = _houses.Count;
            _houses.Clear();
            _households.Clear();
        }

        /// <summary>Places one homeless household; returns how many members were housed.</summary>
        private int HouseGroup(WorldOps ops, EntityStore<Person> people, SocietyHousehold g)
        {
            var members = _households.Members;
            int next = g.Start, end = g.Start + g.Count, housed = 0;

            // Join the family home first: the anchor's (mother/wife) or the anchor's spouse's dwelling.
            var familyHome = FamilyHome(people, g.Anchor);
            if (familyHome != null)
            {
                while (next < end && familyHome.FreeHousing > 0)
                {
                    if (ops.AssignHome(members[next].Person, familyHome)) housed++;
                    next++;
                }
            }

            // Remaining members: keep them together in the roomiest nearby house; split only if nothing fits.
            int guard = 0;
            while (next < end && guard++ < 64)
            {
                int remaining = end - next;
                var pos = members[next].Person.Position;
                var house = PickHouse(remaining, pos);
                if (house == null) break;
                int placedHere = 0;
                while (next < end && house.FreeHousing > 0)
                {
                    if (ops.AssignHome(members[next].Person, house)) { housed++; placedHere++; }
                    next++;
                }
                if (placedHere == 0) break;
            }
            return housed;
        }

        private Building FamilyHome(EntityStore<Person> people, int anchorId)
        {
            var anchor = people.Get(anchorId);
            if (anchor == null || !anchor.IsAlive) return null;
            var home = HomeInList(anchor.HomeId);
            if (home != null && home.FreeHousing > 0) return home;
            if (anchor.SpouseId >= 0)
            {
                var sp = people.Get(anchor.SpouseId);
                if (sp != null && sp.IsAlive && sp.SpouseId == anchor.Id)
                {
                    home = HomeInList(sp.HomeId);
                    if (home != null && home.FreeHousing > 0) return home;
                }
            }
            return null;
        }

        private Building HomeInList(int homeId)
        {
            if (homeId < 0) return null;
            for (int i = 0; i < _houses.Count; i++) if (_houses[i].Id == homeId) return _houses[i];
            return null;
        }

        /// <summary>
        /// Picks the house for a household of <paramref name="needed"/> people: a house where everyone fits
        /// beats one where they do not; then most free space; then nearest to <paramref name="pos"/>; then lowest id.
        /// </summary>
        private Building PickHouse(int needed, Vec2i pos)
        {
            Building best = null;
            bool bestFits = false;
            int bestFree = 0, bestDist = 0;
            for (int i = 0; i < _houses.Count; i++)
            {
                var h = _houses[i];
                int f = h.FreeHousing;
                if (f <= 0) continue;
                bool fits = f >= needed;
                int d = pos.IsValid ? pos.DistanceSqTo(h.Center) : 0;
                if (d < 0) d = int.MaxValue; // overflow guard for far/invalid positions
                bool better;
                if (best == null) better = true;
                else if (fits != bestFits) better = fits;
                else if (f != bestFree) better = f > bestFree;
                else if (d != bestDist) better = d < bestDist;
                else better = h.Id < best.Id;
                if (better) { best = h; bestFits = fits; bestFree = f; bestDist = d; }
            }
            return best;
        }

        /// <summary>Removes dead, foreign, duplicated or over-capacity entries from a usable dwelling.</summary>
        private static void RepairResidents(WorldOps ops, EntityStore<Person> people, Building b)
        {
            var list = b.Residents;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                int pid = list[i];
                bool duplicate = false;
                for (int j = 0; j < i; j++) if (list[j] == pid) { duplicate = true; break; }
                var p = people.Get(pid);
                if (duplicate || p == null || !p.IsAlive || p.HomeId != b.Id)
                {
                    // Dangling entry with no consistent owner: WorldOps has no handle for it, drop it directly.
                    list.RemoveAt(i);
                    continue;
                }
                if (p.SettlementId != b.SettlementId) ops.ClearHome(p);
            }
            int cap = Math.Max(0, b.HousingCapacity);
            int guard = 0;
            while (list.Count > cap && guard++ < 1024)
            {
                int pid = list[list.Count - 1];
                var p = people.Get(pid);
                if (p != null && p.HomeId == b.Id) ops.ClearHome(p);
                else list.RemoveAt(list.Count - 1);
            }
        }

        private static void EvictAll(WorldOps ops, EntityStore<Person> people, Building b)
        {
            var list = b.Residents;
            int guard = 0;
            while (list.Count > 0 && guard++ < 1024)
            {
                int pid = list[list.Count - 1];
                var p = people.Get(pid);
                if (p != null && p.HomeId == b.Id) ops.ClearHome(p);
                else list.RemoveAt(list.Count - 1);
            }
        }

        private sealed class GroupComparer : IComparer<SocietyHousehold>
        {
            public int Compare(SocietyHousehold a, SocietyHousehold b)
            {
                if (a.Count != b.Count) return a.Count > b.Count ? -1 : 1;
                return a.Anchor.CompareTo(b.Anchor);
            }
        }
    }
}
