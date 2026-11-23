using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Marriages (every 12 ticks). Pairs unmarried adults (elders included, so widows and widowers
    /// remarry) of opposite sex living in the same settlement, deterministically and without RNG:
    /// women are visited in id order and each takes the unmarried man closest in age, preferring
    /// her own race strongly (a cross-race partner is chosen only when no same-race partner fits and
    /// both races' affinity toward each other exceeds 0.3). The age gap may not exceed 0.25 x the
    /// smaller life expectancy, and parents, children and (half-)siblings never marry. Each run pairs
    /// at most 2 + population/20 couples per settlement and 256 overall, starting from a settlement
    /// that rotates with the tick. Spouse links are written directly on both people; stale links
    /// (dead, removed or one-sided spouses) are cleared.
    /// </summary>
    public sealed class FamilySystem : ISimSystem
    {
        private readonly List<Person> _women = new List<Person>(64);
        private readonly List<Person> _men = new List<Person>(64);
        private readonly RaceDefinition[] _races = new RaceDefinition[Counts.Races];

        private static readonly IComparer<Person> ById = new IdComparer();
        private static readonly IComparer<Person> ByAgeThenId = new AgeComparer();

        /// <summary>Creates the system.</summary>
        public FamilySystem() { }

        /// <inheritdoc />
        public string Name => "Population.Families";

        /// <inheritdoc />
        public int TickInterval => 12;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            for (int r = 0; r < _races.Length; r++) _races[r] = LifecycleRules.RaceOf(ctx.Db, (RaceId)r);

            var settlements = ctx.State.Settlements.Items;
            int n = settlements.Count;
            if (n == 0) return;
            long round = ctx.Tick / dt;
            int start = (int)(((round % n) + n) % n);
            int budget = LifecycleBalance.MaxPairingsPerRun;
            for (int k = 0; k < n && budget > 0; k++)
            {
                var s = settlements[(start + k) % n];
                if (s == null || !s.IsAlive) continue;
                budget -= PairSettlement(ctx, s, budget);
            }
            _women.Clear();
            _men.Clear();
        }

        private int PairSettlement(SimContext ctx, Settlement s, int budget)
        {
            var people = ctx.State.People;
            var ids = s.ResidentIds;
            _women.Clear();
            _men.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                var p = people.Get(ids[i]);
                if (p == null || !p.IsAlive) continue;
                if (p.SpouseId >= 0)
                {
                    var sp = people.Get(p.SpouseId);
                    if (sp != null && sp.IsAlive && sp.SpouseId == p.Id) continue; // happily married
                    p.SpouseId = -1; // widowed or stale link
                }
                if (!p.IsAdult) continue;
                int r = (int)p.Race;
                if (r < 0 || r >= _races.Length || _races[r] == null) continue;
                if (p.Sex == Sex.Female) _women.Add(p);
                else if (p.Sex == Sex.Male) _men.Add(p);
            }
            if (_women.Count == 0 || _men.Count == 0) return 0;

            _women.Sort(ById);
            _men.Sort(ByAgeThenId);
            int cap = Math.Min(budget, LifecycleBalance.PairingsPerSettlementBase + ids.Count / LifecycleBalance.ResidentsPerExtraPairing);
            int made = 0;
            for (int i = 0; i < _women.Count && made < cap; i++)
            {
                var w = _women[i];
                if (w.SpouseId >= 0) continue;
                var m = FindPartner(ctx.Db, w);
                if (m == null) continue;
                w.SpouseId = m.Id;
                m.SpouseId = w.Id;
                made++;
            }
            return made;
        }

        /// <summary>Nearest-age compatible unmarried man for the woman (same race first), or null.</summary>
        private Person FindPartner(GameDatabase db, Person w)
        {
            var wRace = _races[(int)w.Race];
            float wAge = MathUtil.Sanitize(w.AgeYears);
            float window = LifecycleBalance.MaxAgeGapFraction * Math.Max(0f, MathUtil.Sanitize(wRace.LifeExpectancy));

            // Lower bound: first man with age >= wAge.
            int lo = 0, hi = _men.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (MathUtil.Sanitize(_men[mid].AgeYears) < wAge) lo = mid + 1; else hi = mid;
            }
            int down = lo - 1, up = lo;
            Person cross = null;
            while (down >= 0 || up < _men.Count)
            {
                float dDown = down >= 0 ? wAge - MathUtil.Sanitize(_men[down].AgeYears) : float.MaxValue;
                float dUp = up < _men.Count ? MathUtil.Sanitize(_men[up].AgeYears) - wAge : float.MaxValue;
                Person c;
                float gap;
                if (dUp <= dDown) { c = _men[up++]; gap = dUp; }
                else { c = _men[down--]; gap = dDown; }
                if (gap > window) break; // candidates come in increasing gap order

                if (c.SpouseId >= 0 || !c.IsAlive || LifecycleRules.AreCloseKin(w, c)) continue;
                if (c.Race == w.Race) return c; // nearest same-race partner wins outright
                if (cross != null) continue;
                var cRace = _races[(int)c.Race];
                if (gap > LifecycleRules.MaxAgeGap(wRace, cRace)) continue;
                if (LifecycleRules.AreCompatibleRaces(db, w.Race, c.Race)) cross = c;
            }
            return cross;
        }

        private sealed class IdComparer : IComparer<Person>
        {
            public int Compare(Person a, Person b) => a.Id.CompareTo(b.Id);
        }

        private sealed class AgeComparer : IComparer<Person>
        {
            public int Compare(Person a, Person b)
            {
                int c = MathUtil.Sanitize(a.AgeYears).CompareTo(MathUtil.Sanitize(b.AgeYears));
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            }
        }
    }
}
