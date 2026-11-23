using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The economic role a settlement plays inside its civilization's internal trade network
    /// (assigned by <see cref="TradeSpecializationAdvisor"/>, stored in settlement memory <see cref="SpecializeRoles.RoleKey"/>).
    /// Resource roles (Breadbasket..ForgeTown) export a tradable surplus to siblings; service roles serve the whole realm
    /// (research and faith are pooled civ-wide, commerce multiplies trade at the hub).
    /// </summary>
    public enum SpecializeRole : byte
    {
        None = 0,
        Breadbasket = 1,
        TimberTown = 2,
        MiningTown = 3,
        ForgeTown = 4,
        MarketTown = 5,
        TempleCity = 6,
        ScholarCity = 7
    }

    /// <summary>Memory keys, display names and resource/service mapping for <see cref="SpecializeRole"/>.</summary>
    public static class SpecializeRoles
    {
        /// <summary>Number of role slots (including None).</summary>
        public const int Count = 8;

        /// <summary>Settlement: current role (numeric <see cref="SpecializeRole"/>).</summary>
        public const string RoleKey = "adv.spec.role";
        /// <summary>Settlement: tick the current role was assigned (hysteresis / tenure).</summary>
        public const string SinceKey = "adv.spec.since";
        /// <summary>Settlement: confidence in the role, 0..1 (how clear the comparative advantage is).</summary>
        public const string ConfidenceKey = "adv.spec.conf";
        /// <summary>Settlement: number of sibling towns in trade range this specialist supplies.</summary>
        public const string FeedsKey = "adv.spec.feeds";
        /// <summary>Settlement: 1 = primary trade hub of the civ, 0.5 = secondary hub.</summary>
        public const string HubKey = "adv.spec.hub";
        /// <summary>Settlement: population at the previous run (growth anticipation).</summary>
        public const string PopKey = "adv.spec.pop";
        /// <summary>Settlement: producer capacity of the role at the previous run (disaster detection).</summary>
        public const string ProdKey = "adv.spec.prod";
        /// <summary>Civilization: id of the primary trade hub settlement (-1 / absent = none).</summary>
        public const string CivHubKey = "adv.spec.hubId";

        private static readonly string[] s_names =
        {
            "self-sufficient town", "breadbasket", "timber town", "mining town", "forge town", "market town", "temple city", "scholar city"
        };

        /// <summary>Lowercase display name, e.g. "breadbasket".</summary>
        public static string Name(SpecializeRole role)
        {
            int i = (int)role;
            return i >= 0 && i < s_names.Length ? s_names[i] : s_names[0];
        }

        /// <summary>Reads a settlement's current role (None if unassigned or invalid).</summary>
        public static SpecializeRole Of(Settlement s)
        {
            if (s == null) return SpecializeRole.None;
            float v = MathUtil.Sanitize(s.Mem(RoleKey));
            int i = MathUtil.Clamp(MathUtil.RoundToInt(v), 0, Count - 1);
            return (SpecializeRole)i;
        }

        /// <summary>True for roles that export a physical, tradable resource surplus.</summary>
        public static bool IsResourceRole(SpecializeRole r) => r >= SpecializeRole.Breadbasket && r <= SpecializeRole.ForgeTown;

        /// <summary>The role whose producers make this resource (raw extraction vs. manufacturing).</summary>
        public static SpecializeRole RoleForResource(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Food: return SpecializeRole.Breadbasket;
                case ResourceType.Wood: return SpecializeRole.TimberTown;
                case ResourceType.Stone:
                case ResourceType.Ore:
                case ResourceType.Gold:
                case ResourceType.Fuel: return SpecializeRole.MiningTown;
                case ResourceType.Metal:
                case ResourceType.Tools:
                case ResourceType.Cloth:
                case ResourceType.Goods:
                case ResourceType.Weapons:
                case ResourceType.Machinery:
                case ResourceType.Electronics: return SpecializeRole.ForgeTown;
                default: return SpecializeRole.None;
            }
        }

        /// <summary>The service role associated with a service type (None for services no role specializes in).</summary>
        public static SpecializeRole RoleForService(ServiceType t)
        {
            switch (t)
            {
                case ServiceType.Commerce: return SpecializeRole.MarketTown;
                case ServiceType.Faith: return SpecializeRole.TempleCity;
                case ServiceType.Research: return SpecializeRole.ScholarCity;
                default: return SpecializeRole.None;
            }
        }

        /// <summary>The service whose coverage a service role pushes (Commerce for None/resource roles as a harmless default).</summary>
        public static ServiceType ServiceOf(SpecializeRole r)
        {
            switch (r)
            {
                case SpecializeRole.TempleCity: return ServiceType.Faith;
                case SpecializeRole.ScholarCity: return ServiceType.Research;
                default: return ServiceType.Commerce;
            }
        }
    }
}
