using System.Collections.Generic;

namespace GodSim.Core.Heroes
{
    /// <summary>
    /// Read-only queries over the hero registry (<see cref="WorldState.Memory"/> keys <c>hero.&lt;id&gt;.*</c>).
    /// Safe to call from UI at any time; never mutates state and never throws on bad data.
    /// </summary>
    public static class HeroQueries
    {
        /// <summary>All living heroes, ordered by person id (deterministic).</summary>
        public static IEnumerable<HeroInfo> Living(WorldState s)
        {
            var result = new List<HeroInfo>();
            if (s == null) return result;
            var ids = new List<int>();
            HeroKeys.CollectHeroIds(s.Memory, ids);
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                if (!HeroKeys.TryGetRole(s.Memory, id, out var role)) continue;
                var p = s.People.Get(id);
                if (p == null || !p.IsAlive) continue;
                result.Add(Describe(s, p, role));
            }
            return result;
        }

        /// <summary>True if the person is alive and registered as a hero.</summary>
        public static bool IsHero(WorldState s, int personId)
        {
            if (s == null || personId < 0) return false;
            var p = s.People.Get(personId);
            if (p == null || !p.IsAlive) return false;
            return HeroKeys.TryGetRole(s.Memory, personId, out _);
        }

        /// <summary>The registered role of a person, or null if they are not (or no longer) a hero.</summary>
        public static HeroRole? RoleOf(WorldState s, int personId)
        {
            if (s == null || personId < 0) return null;
            if (!HeroKeys.TryGetRole(s.Memory, personId, out var role)) return null;
            return role;
        }

        /// <summary>Accumulated deeds of a hero (0 if not a hero).</summary>
        public static int DeedsOf(WorldState s, int personId)
        {
            if (s == null || personId < 0) return 0;
            float d = HeroKeys.Read(s.Memory, HeroKeys.Deeds(personId), 0f);
            return MathUtil.FloorToInt(MathUtil.Clamp(d, 0f, HeroRules.MaxDeeds));
        }

        /// <summary>Snapshot of one hero, or false if the person is not a living hero.</summary>
        public static bool TryGet(WorldState s, int personId, out HeroInfo info)
        {
            info = default(HeroInfo);
            if (s == null || personId < 0) return false;
            if (!HeroKeys.TryGetRole(s.Memory, personId, out var role)) return false;
            var p = s.People.Get(personId);
            if (p == null || !p.IsAlive) return false;
            info = Describe(s, p, role);
            return true;
        }

        /// <summary>
        /// Epithet for UI labels, e.g. "the Wise" or "Breaker of Sieges". Empty string for non-heroes.
        /// <paramref name="db"/> is optional and only used for race names.
        /// </summary>
        public static string Title(WorldState s, GameDatabase db, Person p)
        {
            if (s == null || p == null) return "";
            if (!HeroKeys.TryGetRole(s.Memory, p.Id, out var role)) return "";
            string plural = null;
            if (db != null)
            {
                int ri = (int)p.Race;
                var races = db.Races;
                if (races != null && ri >= 0 && ri < races.Count && races[ri] != null) plural = races[ri].PluralName;
            }
            return HeroTitles.Epithet(role, p.Race, DeedsOf(s, p.Id), (ulong)p.Id, plural);
        }

        private static HeroInfo Describe(WorldState s, Person p, HeroRole role)
        {
            int deeds = DeedsOf(s, p.Id);
            float since = HeroKeys.Read(s.Memory, HeroKeys.Since(p.Id), -1f);
            return new HeroInfo
            {
                PersonId = p.Id,
                Name = p.Name,
                Role = role,
                Deeds = deeds,
                SinceTick = since < 0f ? -1L : (long)since,
                HomeSettlementId = HeroKeys.ReadId(s.Memory, HeroKeys.Home(p.Id)),
                SettlementId = p.SettlementId,
                CivilizationId = p.CivilizationId,
                IsLegendary = HeroRules.IsLegendary(deeds),
            };
        }
    }
}
