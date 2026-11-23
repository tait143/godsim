using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// A "town archetype": what a believable settlement of one <see cref="SettlementTier"/> looks like. It holds the
    /// target building-category mix per 100 residents and the cumulative civic-core checklist for the tier.
    /// </summary>
    public sealed class UrbanArchetype
    {
        public SettlementTier Tier;
        public string Name = "";
        /// <summary>Target buildings per 100 residents, indexed by (int)BuildingCategory (Housing ~25 = 3-5 per house).</summary>
        public float[] PerHundred;
        /// <summary>Civic-core checklist (cumulative across tiers, foundations first).</summary>
        public UrbanRole[] CoreRoles;
        /// <summary>Tier at which each core role was introduced (parallel to <see cref="CoreRoles"/>).</summary>
        public SettlementTier[] RoleTier;
        /// <summary>Population at which the next tier usually starts (-1 = top tier). Used to plan ahead.</summary>
        public int NextTierPopulation = -1;
    }

    /// <summary>Data table of <see cref="UrbanArchetype"/>s per tier (built once at type init).</summary>
    public static class UrbanArchetypeCatalog
    {
        public static readonly int CategoryCount = Enum.GetValues(typeof(BuildingCategory)).Length;
        /// <summary>Cached category names (avoid enum ToString allocations in reasons).</summary>
        public static readonly string[] CategoryNames = BuildCategoryNames();
        private static readonly UrbanArchetype[] s_archetypes = Build();

        /// <summary>Archetype for a tier (clamped to the defined range).</summary>
        public static UrbanArchetype Get(SettlementTier tier)
        {
            int i = (int)tier;
            if (i < 0) i = 0;
            if (i >= s_archetypes.Length) i = s_archetypes.Length - 1;
            return s_archetypes[i];
        }

        public static string CategoryName(BuildingCategory c)
        {
            int i = (int)c;
            return i >= 0 && i < CategoryNames.Length ? CategoryNames[i] : "Other";
        }

        private static string[] BuildCategoryNames()
        {
            var a = new string[CategoryCount];
            for (int i = 0; i < a.Length; i++) a[i] = ((BuildingCategory)i).ToString();
            return a;
        }

        private static UrbanArchetype[] Build()
        {
            int tiers = Math.Max(Counts.Tiers, 7);
            var a = new UrbanArchetype[tiers];
            var roles = new List<UrbanRole>();
            var roleTiers = new List<SettlementTier>();

            //                                   Housing Food Res  Ind  Stor Civ  Rel  Edu  Hlth Cult Mil  Com  Infra
            a[0] = Make(SettlementTier.Camp, "Camp", 10, Mix(25f, 10f, 8f, 0f, 3f, 0f, 2f, 0f, 0f, 2f, 0f, 0f, 1f), roles, roleTiers,
                UrbanRole.TownHall, UrbanRole.Storage);
            a[1] = Make(SettlementTier.Hamlet, "Hamlet", 30, Mix(25f, 10f, 7f, 1f, 3f, 0f, 3f, 0f, 1f, 2f, 1f, 0f, 3f), roles, roleTiers,
                UrbanRole.Water, UrbanRole.Shrine);
            a[2] = Make(SettlementTier.Village, "Village", 80, Mix(25f, 9f, 6f, 3f, 3f, 1f, 2.5f, 1f, 1.5f, 1.5f, 2f, 1.5f, 2f), roles, roleTiers,
                UrbanRole.Market, UrbanRole.FoodStore, UrbanRole.Temple);
            a[3] = Make(SettlementTier.Town, "Town", 200, Mix(25f, 7f, 5f, 4f, 2.5f, 1f, 2f, 1.5f, 1.5f, 1.5f, 2f, 2f, 1.5f), roles, roleTiers,
                UrbanRole.Learning, UrbanRole.Defense, UrbanRole.Healing, UrbanRole.Entertainment);
            a[4] = Make(SettlementTier.City, "City", 500, Mix(25f, 5.5f, 4f, 4.5f, 2f, 0.8f, 1.5f, 1.5f, 1.2f, 1.5f, 1.5f, 2f, 1.2f), roles, roleTiers,
                UrbanRole.HigherLearning, UrbanRole.Finance, UrbanRole.Hospital, UrbanRole.GrandFaith, UrbanRole.Administration);
            a[5] = Make(SettlementTier.Metropolis, "Metropolis", 1200, Mix(25f, 4f, 3f, 4.5f, 1.5f, 0.6f, 1f, 1.5f, 1f, 1.2f, 1f, 2f, 1f), roles, roleTiers,
                UrbanRole.Science, UrbanRole.Power, UrbanRole.Transit, UrbanRole.Stadium);
            a[6] = Make(SettlementTier.Megalopolis, "Megalopolis", -1, Mix(25f, 3f, 2.5f, 4f, 1.2f, 0.5f, 0.8f, 1.5f, 1f, 1f, 0.8f, 2f, 1f), roles, roleTiers,
                UrbanRole.DataHub, UrbanRole.Arcology);
            for (int i = 7; i < tiers; i++) a[i] = a[6];
            return a;
        }

        private static float[] Mix(params float[] v)
        {
            var a = new float[CategoryCount];
            for (int i = 0; i < v.Length && i < a.Length; i++) a[i] = v[i];
            return a;
        }

        private static UrbanArchetype Make(SettlementTier tier, string name, int nextPop, float[] mix, List<UrbanRole> roles, List<SettlementTier> roleTiers, params UrbanRole[] added)
        {
            for (int i = 0; i < added.Length; i++) { roles.Add(added[i]); roleTiers.Add(tier); }
            return new UrbanArchetype
            {
                Tier = tier,
                Name = name,
                PerHundred = mix,
                CoreRoles = roles.ToArray(),
                RoleTier = roleTiers.ToArray(),
                NextTierPopulation = nextPop,
            };
        }
    }
}
