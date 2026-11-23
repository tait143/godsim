using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Functional civic roles a believable settlement fills as it grows (the "civic core"). Each role resolves,
    /// per civilization, to the best building that is actually available for its age and race
    /// (see <see cref="UrbanRoleResolver"/>).
    /// </summary>
    public enum UrbanRole : byte
    {
        TownHall, Water, Storage, Shrine,                         // Hamlet
        Market, FoodStore, Temple,                                // Village
        Learning, Defense, Healing, Entertainment,                // Town
        HigherLearning, Finance, Hospital, GrandFaith, Administration, // City
        Science, Power, Transit, Stadium,                         // Metropolis
        DataHub, Arcology                                         // Megalopolis
    }

    /// <summary>
    /// Data describing how one <see cref="UrbanRole"/> is filled: an ordered preference list of candidate buildings
    /// (optionally per race), plus a data-driven fallback (any building of a category offering a service, at or above an
    /// age) so modded or rebalanced catalogs still resolve.
    /// </summary>
    public sealed class UrbanRoleSpec
    {
        public UrbanRole Role;
        /// <summary>Short human label, e.g. "a Market" (used when nothing resolves).</summary>
        public string Label = "";
        /// <summary>Candidates in preference order; race-favored candidates are promoted first by the resolver.</summary>
        public BuildingType[] Candidates = new BuildingType[0];
        /// <summary>Optional race-specific candidate lists ("race equivalents"), indexed by (int)RaceId; null = default.</summary>
        public BuildingType[][] RaceCandidates;
        /// <summary>
        /// Upgrade roles are only satisfied by the resolved building or a better-ranked candidate (a Shrine does not
        /// satisfy the Temple role once a Temple can be built). Non-upgrade roles are satisfied by any candidate.
        /// </summary>
        public bool Upgrade;

        public bool HasFallback;
        public BuildingCategory FallbackCategory;
        public bool FallbackNeedsService;
        public ServiceType FallbackService;
        public AgeId FallbackMinAge;

        /// <summary>The candidate list for a race (race equivalent if defined, else the default list).</summary>
        public BuildingType[] CandidatesFor(RaceId race)
        {
            if (RaceCandidates != null)
            {
                int r = (int)race;
                if (r >= 0 && r < RaceCandidates.Length && RaceCandidates[r] != null) return RaceCandidates[r];
            }
            return Candidates;
        }

        /// <summary>True if a building definition matches this role's data-driven fallback criteria.</summary>
        public bool MatchesFallback(BuildingDefinition def)
        {
            if (!HasFallback || def == null) return false;
            if (def.Category != FallbackCategory || def.MinAge < FallbackMinAge) return false;
            if (!FallbackNeedsService) return true;
            var sv = def.Services;
            if (sv == null) return false;
            for (int i = 0; i < sv.Length; i++)
                if (sv[i].Type == FallbackService && sv[i].Amount > 0f) return true;
            return false;
        }
    }

    /// <summary>Static table of every <see cref="UrbanRoleSpec"/> (built once, read-only afterwards).</summary>
    public static class UrbanRoleCatalog
    {
        public static readonly int Count = Enum.GetValues(typeof(UrbanRole)).Length;
        private static readonly UrbanRoleSpec[] s_specs = Build();

        public static UrbanRoleSpec Get(UrbanRole role)
        {
            int i = (int)role;
            return i >= 0 && i < s_specs.Length ? s_specs[i] : s_specs[0];
        }

        private static UrbanRoleSpec[] Build()
        {
            var a = new UrbanRoleSpec[Count];
            a[(int)UrbanRole.TownHall] = Spec(UrbanRole.TownHall, "a Town Center", false, new[] { BuildingType.TownCenter }, BuildingCategory.Civic, true, ServiceType.Administration, AgeId.Dawn);
            a[(int)UrbanRole.Water] = Spec(UrbanRole.Water, "a Well", false, new[] { BuildingType.Well, BuildingType.Aqueduct }, BuildingCategory.Infrastructure, true, ServiceType.Water, AgeId.Dawn);
            a[(int)UrbanRole.Storage] = Spec(UrbanRole.Storage, "a Storehouse", false, new[] { BuildingType.Storehouse, BuildingType.Granary }, BuildingCategory.Storage, false, ServiceType.Faith, AgeId.Dawn);
            a[(int)UrbanRole.Shrine] = Spec(UrbanRole.Shrine, "a shrine", false, new[] { BuildingType.Shrine, BuildingType.SpiritTotem }, BuildingCategory.Religion, true, ServiceType.Faith, AgeId.Dawn);
            a[(int)UrbanRole.Market] = Spec(UrbanRole.Market, "a Market", false, new[] { BuildingType.Market, BuildingType.Guildhall }, BuildingCategory.Commerce, true, ServiceType.Commerce, AgeId.Iron);
            a[(int)UrbanRole.FoodStore] = Spec(UrbanRole.FoodStore, "a Granary", false, new[] { BuildingType.Granary }, BuildingCategory.Storage, false, ServiceType.Faith, AgeId.Bronze);
            a[(int)UrbanRole.Temple] = Spec(UrbanRole.Temple, "a Temple", true, new[] { BuildingType.Temple, BuildingType.Shrine }, BuildingCategory.Religion, true, ServiceType.Faith, AgeId.Iron);
            a[(int)UrbanRole.Learning] = Spec(UrbanRole.Learning, "a Library", true, new[] { BuildingType.Library, BuildingType.Scriptorium, BuildingType.Monastery }, BuildingCategory.Education, true, ServiceType.Research, AgeId.Iron);
            a[(int)UrbanRole.Defense] = Spec(UrbanRole.Defense, "a garrison or walls", false, new[] { BuildingType.Barracks, BuildingType.StoneWall, BuildingType.Palisade, BuildingType.Watchtower, BuildingType.Castle, BuildingType.MilitaryBase }, BuildingCategory.Military, true, ServiceType.Defense, AgeId.Bronze);
            a[(int)UrbanRole.Healing] = Spec(UrbanRole.Healing, "a Bathhouse or Herbalist", false, new[] { BuildingType.Bathhouse, BuildingType.Herbalist, BuildingType.Hospital }, BuildingCategory.Health, true, ServiceType.Health, AgeId.Bronze);
            a[(int)UrbanRole.Entertainment] = Spec(UrbanRole.Entertainment, "a Theater or Tavern", false, new[] { BuildingType.Theater, BuildingType.Tavern, BuildingType.Arena, BuildingType.Campfire }, BuildingCategory.Culture, true, ServiceType.Entertainment, AgeId.Dawn);
            a[(int)UrbanRole.HigherLearning] = Spec(UrbanRole.HigherLearning, "a University", false, new[] { BuildingType.University, BuildingType.Observatory }, BuildingCategory.Education, true, ServiceType.Research, AgeId.Renaissance);
            a[(int)UrbanRole.Finance] = Spec(UrbanRole.Finance, "a Bank", false, new[] { BuildingType.Bank, BuildingType.Guildhall }, BuildingCategory.Commerce, true, ServiceType.Commerce, AgeId.Medieval);
            a[(int)UrbanRole.Hospital] = Spec(UrbanRole.Hospital, "a Hospital", false, new[] { BuildingType.MedicalCenter, BuildingType.Hospital }, BuildingCategory.Health, true, ServiceType.Health, AgeId.Renaissance);
            a[(int)UrbanRole.GrandFaith] = Spec(UrbanRole.GrandFaith, "a Cathedral", false, new[] { BuildingType.Cathedral, BuildingType.Monastery }, BuildingCategory.Religion, true, ServiceType.Faith, AgeId.Medieval);
            a[(int)UrbanRole.Administration] = Spec(UrbanRole.Administration, "a Forum", false, new[] { BuildingType.Forum, BuildingType.Arcology }, BuildingCategory.Civic, true, ServiceType.Administration, AgeId.Classical);
            a[(int)UrbanRole.Science] = Spec(UrbanRole.Science, "a Research Lab", false, new[] { BuildingType.ResearchLab, BuildingType.DataCenter, BuildingType.Observatory }, BuildingCategory.Education, true, ServiceType.Research, AgeId.Modern);
            a[(int)UrbanRole.Power] = Spec(UrbanRole.Power, "a Power Plant", false, new[] { BuildingType.FusionReactor, BuildingType.PowerPlant }, BuildingCategory.Infrastructure, true, ServiceType.Power, AgeId.Modern);
            a[(int)UrbanRole.Transit] = Spec(UrbanRole.Transit, "a Rail Station", false, new[] { BuildingType.Airport, BuildingType.RailStation }, BuildingCategory.Commerce, false, ServiceType.Commerce, AgeId.Industrial);
            a[(int)UrbanRole.Stadium] = Spec(UrbanRole.Stadium, "a Stadium", false, new[] { BuildingType.Stadium, BuildingType.Arena }, BuildingCategory.Culture, true, ServiceType.Entertainment, AgeId.Modern);
            a[(int)UrbanRole.DataHub] = Spec(UrbanRole.DataHub, "a Data Center", false, new[] { BuildingType.DataCenter }, BuildingCategory.Education, true, ServiceType.Research, AgeId.Information);
            a[(int)UrbanRole.Arcology] = Spec(UrbanRole.Arcology, "an Arcology", false, new[] { BuildingType.Arcology }, BuildingCategory.Civic, true, ServiceType.Administration, AgeId.Ascension);

            // Race equivalents: each people expresses the same civic function through its own institutions.
            var faith = a[(int)UrbanRole.GrandFaith];
            faith.RaceCandidates = new BuildingType[Counts.Races][];
            Set(faith, RaceId.Human, BuildingType.Cathedral, BuildingType.GrandCathedral, BuildingType.Monastery);
            Set(faith, RaceId.Elf, BuildingType.WorldTree, BuildingType.Monastery, BuildingType.Cathedral);
            Set(faith, RaceId.Orc, BuildingType.WarIdol, BuildingType.Cathedral, BuildingType.Monastery);
            Set(faith, RaceId.Saurian, BuildingType.SunPyramid, BuildingType.Cathedral, BuildingType.Monastery);

            var fun = a[(int)UrbanRole.Entertainment];
            fun.RaceCandidates = new BuildingType[Counts.Races][];
            Set(fun, RaceId.Dwarf, BuildingType.Tavern, BuildingType.Theater, BuildingType.Arena, BuildingType.Campfire);
            Set(fun, RaceId.Orc, BuildingType.Arena, BuildingType.Tavern, BuildingType.Theater, BuildingType.Campfire);
            Set(fun, RaceId.Elf, BuildingType.Theater, BuildingType.Campfire, BuildingType.Tavern);

            var def = a[(int)UrbanRole.Defense];
            def.RaceCandidates = new BuildingType[Counts.Races][];
            Set(def, RaceId.Dwarf, BuildingType.StoneWall, BuildingType.Barracks, BuildingType.Palisade, BuildingType.Castle, BuildingType.Watchtower, BuildingType.MilitaryBase);
            Set(def, RaceId.Elf, BuildingType.Watchtower, BuildingType.Palisade, BuildingType.Barracks, BuildingType.StoneWall, BuildingType.MilitaryBase);

            var learn = a[(int)UrbanRole.Learning];
            learn.RaceCandidates = new BuildingType[Counts.Races][];
            Set(learn, RaceId.Saurian, BuildingType.Library, BuildingType.Monastery, BuildingType.Scriptorium);
            return a;
        }

        private static void Set(UrbanRoleSpec spec, RaceId race, params BuildingType[] list)
        {
            int r = (int)race;
            if (r >= 0 && r < spec.RaceCandidates.Length) spec.RaceCandidates[r] = list;
        }

        private static UrbanRoleSpec Spec(UrbanRole role, string label, bool upgrade, BuildingType[] cands, BuildingCategory fbCat, bool fbNeedsService, ServiceType fbService, AgeId fbMinAge)
        {
            return new UrbanRoleSpec
            {
                Role = role,
                Label = label,
                Upgrade = upgrade,
                Candidates = cands,
                HasFallback = true,
                FallbackCategory = fbCat,
                FallbackNeedsService = fbNeedsService,
                FallbackService = fbService,
                FallbackMinAge = fbMinAge,
            };
        }
    }
}
