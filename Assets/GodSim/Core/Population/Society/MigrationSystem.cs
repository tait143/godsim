using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Moves people between settlements.
    /// <para>
    /// Internal migration: in a settlement with more than 3 homeless, homeless households (then unhappy ones)
    /// move to the settlement of the same civilization with the most free housing
    /// (<see cref="SocietyKeys.FreeHousing"/>) within 80 tiles. At most 5% of the population (at least one
    /// person) leaves per run, and families move together.
    /// </para>
    /// <para>
    /// Defection: when a settlement's unrest exceeds 0.8, a few unhappy households may move to a neighbouring
    /// settlement of another civilization whose happiness is much higher.
    /// </para>
    /// All moves go through <see cref="WorldOps.MovePersonToSettlement"/>.
    /// </summary>
    public sealed class MigrationSystem : ISimSystem
    {
        /// <summary>Run interval in ticks.</summary>
        public const int Interval = 48;
        /// <summary>Maximum distance (tiles, between settlement centers) for internal migration.</summary>
        public const int MaxRange = 80;
        /// <summary>A settlement emigrates only when it has more homeless than this.</summary>
        public const int HomelessThreshold = 3;
        /// <summary>Largest share of the population that may leave in one run.</summary>
        public const float MaxFractionPerRun = 0.05f;
        /// <summary>Residents below this happiness are willing to move.</summary>
        public const float UnhappyThreshold = 0.3f;
        /// <summary>Unrest above which defection to another civilization becomes possible.</summary>
        public const float DefectionUnrest = 0.8f;
        /// <summary>The target settlement must be at least this much happier.</summary>
        public const float DefectionHappinessGap = 0.25f;
        /// <summary>Maximum distance (tiles) for a defection target.</summary>
        public const int DefectionRange = 60;
        /// <summary>Most people defecting from one settlement in one run.</summary>
        public const int MaxDefectorsPerRun = 3;
        /// <summary>Chance per run that defection happens at all in an eligible settlement.</summary>
        public const float DefectionChancePerRun = 0.5f;

        private readonly SocietyHouseholdBuffer _households = new SocietyHouseholdBuffer();
        private readonly List<int> _order = new List<int>();
        private readonly HouseholdOrder _byNeed;
        private readonly List<Person> _moving = new List<Person>();
        private readonly List<Settlement> _sources = new List<Settlement>();
        private readonly List<Settlement> _restless = new List<Settlement>();

        /// <summary>Creates the system.</summary>
        public MigrationSystem()
        {
            _byNeed = new HouseholdOrder(_households.Groups);
        }

        /// <inheritdoc />
        public string Name => "Population.Migration";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            var state = ctx.State;
            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null) continue;
                if (s.Memory.ContainsKey(SocietyKeys.Emigrated)) s.Memory[SocietyKeys.Emigrated] = 0f;
                if (s.Memory.ContainsKey(SocietyKeys.Immigrated)) s.Memory[SocietyKeys.Immigrated] = 0f;
            }
            // Snapshot the sources first so arrivals in this run never turn their new home into a source.
            _sources.Clear();
            _restless.Clear();
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;
                if (s.Homeless > HomelessThreshold) _sources.Add(s);
                if (MathUtil.Sanitize(s.Unrest) > DefectionUnrest) _restless.Add(s);
            }
            for (int i = 0; i < _sources.Count; i++)
            {
                var s = _sources[i];
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (s.IsAlive && civ != null && civ.IsAlive) MigrateWithinCiv(ctx, s, civ);
            }
            for (int i = 0; i < _restless.Count; i++)
            {
                var s = _restless[i];
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (s.IsAlive && civ != null && civ.IsAlive) TryDefect(ctx, s, civ);
            }
            _sources.Clear();
            _restless.Clear();
        }

        // ------------------------------------------------------------------ internal migration

        /// <summary>Sends homeless/unhappy households of <paramref name="s"/> to the same-civ settlement with the most free housing. Returns people moved.</summary>
        public int MigrateWithinCiv(SimContext ctx, Settlement s, Civilization civ)
        {
            var state = ctx.State;
            int budget = Math.Max(1, (int)Math.Floor(s.Population * MaxFractionPerRun));
            BuildHouseholds(state, s, civ.LeaderId);
            var groups = _households.Groups;
            _order.Clear();
            for (int g = 0; g < groups.Count; g++)
            {
                var h = groups[g];
                if (h.Locked || !h.HasAdult) continue;
                if (h.Homeless > 0 || h.AvgHappiness < UnhappyThreshold) _order.Add(g);
            }
            if (_order.Count > 1) _order.Sort(_byNeed);

            int moved = 0, homelessMoved = 0;
            for (int k = 0; k < _order.Count && moved < budget; k++)
            {
                var h = groups[_order[k]];
                if (moved + h.Count > budget) continue;
                var dest = BestSameCivDestination(state, ctx.Db, s, civ, h.Count);
                if (dest == null) continue;
                int homeless;
                int n = MoveHousehold(ctx, h, dest, out homeless);
                if (n <= 0) continue;
                moved += n;
                homelessMoved += homeless;
                int free = SocietyUtil.FreeHousingOf(state, ctx.Db, dest);
                dest.Memory[SocietyKeys.FreeHousing] = Math.Max(0, free - n);
                dest.Homeless += n;
                dest.Memory[SocietyKeys.Immigrated] = dest.Mem(SocietyKeys.Immigrated) + n;
            }
            if (moved > 0)
            {
                s.Homeless = Math.Max(0, s.Homeless - homelessMoved);
                s.Memory[SocietyKeys.Emigrated] = s.Mem(SocietyKeys.Emigrated) + moved;
            }
            _households.Clear();
            _order.Clear();
            return moved;
        }

        private static Settlement BestSameCivDestination(WorldState state, GameDatabase db, Settlement from, Civilization civ, int needed)
        {
            Settlement best = null;
            int bestFree = 0, bestDist = 0;
            int maxD2 = MaxRange * MaxRange;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == from.Id) continue;
                var d = state.Settlements.Get(ids[i]);
                if (d == null || !d.IsAlive || d.CivilizationId != civ.Id) continue;
                int dist = from.Center.DistanceSqTo(d.Center);
                if (dist < 0 || dist > maxD2) continue;
                int free = SocietyUtil.FreeHousingOf(state, db, d);
                if (free < needed) continue;
                bool better = best == null || free > bestFree || (free == bestFree && (dist < bestDist || (dist == bestDist && d.Id < best.Id)));
                if (better) { best = d; bestFree = free; bestDist = dist; }
            }
            return best;
        }

        // ------------------------------------------------------------------ defection

        /// <summary>Lets a few unhappy households of a restless settlement defect to a much happier neighbour of another civ. Returns people moved.</summary>
        public int TryDefect(SimContext ctx, Settlement s, Civilization civ)
        {
            var state = ctx.State;
            if (!ctx.Rng.Chance(DefectionChancePerRun)) return 0;
            var dest = BestForeignDestination(state, s);
            if (dest == null) return 0;

            int budget = Math.Min(MaxDefectorsPerRun, Math.Max(1, (int)Math.Floor(s.Population * 0.02f)));
            BuildHouseholds(state, s, civ.LeaderId);
            var groups = _households.Groups;
            _order.Clear();
            for (int g = 0; g < groups.Count; g++)
            {
                var h = groups[g];
                if (h.Locked || !h.HasAdult) continue;
                if (h.AvgHappiness < WellbeingSystem.UnrestThreshold) _order.Add(g);
            }
            if (_order.Count > 1) _order.Sort(_byNeed);

            int moved = 0;
            for (int k = 0; k < _order.Count && moved < budget; k++)
            {
                var h = groups[_order[k]];
                if (moved + h.Count > budget) continue;
                int homeless;
                moved += MoveHousehold(ctx, h, dest, out homeless);
                if (homeless > 0) s.Homeless = Math.Max(0, s.Homeless - homeless);
            }
            if (moved > 0)
            {
                dest.Homeless += moved;
                int free = SocietyUtil.FreeHousingOf(state, ctx.Db, dest);
                dest.Memory[SocietyKeys.FreeHousing] = Math.Max(0, free - moved);
                dest.Memory[SocietyKeys.Immigrated] = dest.Mem(SocietyKeys.Immigrated) + moved;
                s.Memory[SocietyKeys.Emigrated] = s.Mem(SocietyKeys.Emigrated) + moved;
                var destCiv = state.Civilizations.Get(dest.CivilizationId);
                ctx.Ops.AddChronicle(ChronicleCategory.Settlement,
                    moved + (moved == 1 ? " restless soul fled " : " restless souls fled ") + s.Name + " for " + dest.Name +
                    (destCiv != null ? " of the " + destCiv.Name : "") + ".",
                    s.CivilizationId, s.Id, s.Center, 0);
            }
            _households.Clear();
            _order.Clear();
            return moved;
        }

        private static Settlement BestForeignDestination(WorldState state, Settlement from)
        {
            Settlement best = null;
            int bestDist = 0;
            float bestHappy = 0f;
            int maxD2 = DefectionRange * DefectionRange;
            float minHappy = SocietyUtil.Unit(from.Happiness, 0.5f) + DefectionHappinessGap;
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d == null || !d.IsAlive || d.Id == from.Id || d.CivilizationId == from.CivilizationId || d.Population <= 0) continue;
                var dc = state.Civilizations.Get(d.CivilizationId);
                if (dc == null || !dc.IsAlive) continue;
                float happy = SocietyUtil.Unit(d.Happiness, 0.5f);
                if (happy < minHappy) continue;
                int dist = from.Center.DistanceSqTo(d.Center);
                if (dist < 0 || dist > maxD2) continue;
                bool better = best == null || happy > bestHappy || (happy == bestHappy && (dist < bestDist || (dist == bestDist && d.Id < best.Id)));
                if (better) { best = d; bestHappy = happy; bestDist = dist; }
            }
            return best;
        }

        // ------------------------------------------------------------------ shared

        private void BuildHouseholds(WorldState state, Settlement s, int leaderId)
        {
            _households.Clear();
            var people = state.People;
            var residents = s.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive || p.SettlementId != s.Id) continue;
                _households.Add(p, SocietyUtil.HouseholdAnchor(p, people));
            }
            _households.BuildGroups(leaderId);
        }

        private int MoveHousehold(SimContext ctx, SocietyHousehold h, Settlement dest, out int homelessMoved)
        {
            homelessMoved = 0;
            _moving.Clear();
            var members = _households.Members;
            for (int i = h.Start; i < h.Start + h.Count; i++) _moving.Add(members[i].Person);
            int moved = 0;
            for (int i = 0; i < _moving.Count; i++)
            {
                var p = _moving[i];
                if (p == null || !p.IsAlive || p.SettlementId == dest.Id) continue;
                bool wasHomeless = p.HomeId < 0;
                ctx.Ops.MovePersonToSettlement(p, dest);
                if (p.SettlementId == dest.Id)
                {
                    moved++;
                    if (wasHomeless) homelessMoved++;
                }
            }
            _moving.Clear();
            return moved;
        }

        /// <summary>Homeless households first (more homeless first), then the unhappiest; ties by anchor id.</summary>
        private sealed class HouseholdOrder : IComparer<int>
        {
            private readonly List<SocietyHousehold> _groups;
            public HouseholdOrder(List<SocietyHousehold> groups) { _groups = groups; }
            public int Compare(int x, int y)
            {
                var a = _groups[x];
                var b = _groups[y];
                bool ha = a.Homeless > 0, hb = b.Homeless > 0;
                if (ha != hb) return ha ? -1 : 1;
                if (a.Homeless != b.Homeless) return a.Homeless > b.Homeless ? -1 : 1;
                float qa = a.AvgHappiness, qb = b.AvgHappiness;
                if (qa != qb) return qa < qb ? -1 : 1;
                return a.Anchor.CompareTo(b.Anchor);
            }
        }
    }
}
