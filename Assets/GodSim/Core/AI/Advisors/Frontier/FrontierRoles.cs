namespace GodSim.Core.AI
{
    /// <summary>
    /// Geographic roles the <see cref="FrontierAdvisor"/> assigns to settlements, plus the blackboard keys it
    /// uses. The role is cached in <c>Settlement.Memory["adv.front.role"]</c> so UI, other advisors and saves see
    /// exactly the same classification.
    /// </summary>
    public static class FrontierRoles
    {
        /// <summary>Safe interior settlement: surrounded by its own civ or by nothing in particular.</summary>
        public const int Heartland = 0;
        /// <summary>Faces another civilization's territory or settlements.</summary>
        public const int Frontier = 1;
        /// <summary>Large share of sea/lake around it: fishing, harbors, sea trade.</summary>
        public const int CoastalGateway = 2;
        /// <summary>Remote colony with lots of unclaimed land around it.</summary>
        public const int WildernessOutpost = 3;
        /// <summary>Sits on rich deposits (ore, stone, gold, coal, oil).</summary>
        public const int ResourceTown = 4;
        /// <summary>Number of roles.</summary>
        public const int Count = 5;

        // ------------------------------------------------------------------ blackboard keys (settlement)
        /// <summary>Primary role (0..4), -1/missing = not classified yet.</summary>
        public const string KeyRole = "adv.front.role";
        /// <summary>Secondary role (0..4) or -1.</summary>
        public const string KeyRole2 = "adv.front.role2";
        /// <summary>Tick of the last map scan.</summary>
        public const string KeyScanTick = "adv.front.scan";
        /// <summary>Territory radius at the last scan (rescan when it grows).</summary>
        public const string KeyRadius = "adv.front.rad";
        /// <summary>Foreign-owned share of the scanned tiles.</summary>
        public const string KeyForeign = "adv.front.foreign";
        /// <summary>Water share of the scanned tiles.</summary>
        public const string KeyWater = "adv.front.water";
        /// <summary>Unclaimed share of the scanned land.</summary>
        public const string KeyWild = "adv.front.wild";
        /// <summary>Proximity (0..1) of the closest foreign settlement.</summary>
        public const string KeyProximity = "adv.front.prox";
        /// <summary>Bit mask of deposit kinds present (bit = FrontierCatalog kind index).</summary>
        public const string KeyDepositMask = "adv.front.dep";
        /// <summary>Most hostile neighbour stance seen last run (DiplomaticStance as float, -1 none).</summary>
        public const string KeyStance = "adv.front.stance";
        /// <summary>Hostility (0..1) of the most hostile neighbour last run.</summary>
        public const string KeyHostility = "adv.front.host";
        /// <summary>Threat (0..1) from hostile armies near the settlement last run.</summary>
        public const string KeyThreat = "adv.front.threat";
        /// <summary>Tick when reasons were last written to the AI log.</summary>
        public const string KeySaid = "adv.front.said";

        /// <summary>Neighbour civ ids (most relevant first), -1 = none.</summary>
        public static readonly string[] KeyNeighbour = { "adv.front.n0", "adv.front.n1", "adv.front.n2", "adv.front.n3" };
        /// <summary>Usable deposit tile counts per FrontierCatalog kind (ore, stone, gold, coal, oil, trees).</summary>
        public static readonly string[] KeyDepositTiles = { "adv.front.d0", "adv.front.d1", "adv.front.d2", "adv.front.d3", "adv.front.d4", "adv.front.d5" };

        // ------------------------------------------------------------------ blackboard keys (civilization)
        /// <summary>Civ memory: number of frontier settlements (refreshed every advisor run).</summary>
        public const string CivKeyFrontiers = "adv.front.nfront";
        /// <summary>Civ memory: number of coastal gateways.</summary>
        public const string CivKeyGateways = "adv.front.ncoast";
        /// <summary>Civ memory: number of resource towns.</summary>
        public const string CivKeyResourceTowns = "adv.front.nres";

        private static readonly string[] s_names = { "heartland", "frontier", "coastal gateway", "wilderness outpost", "resource town" };

        /// <summary>Lower-case display name of a role ("unknown" when out of range).</summary>
        public static string Name(int role) => role >= 0 && role < Count ? s_names[role] : "unknown";

        /// <summary>The cached role of a settlement, or -1 if not classified yet.</summary>
        public static int RoleOf(Settlement s)
        {
            if (s == null || !s.Memory.TryGetValue(KeyRole, out float v) || float.IsNaN(v)) return -1;
            int r = (int)v;
            return r >= 0 && r < Count ? r : -1;
        }

        /// <summary>The cached secondary role of a settlement, or -1.</summary>
        public static int SecondaryRoleOf(Settlement s)
        {
            if (s == null || !s.Memory.TryGetValue(KeyRole2, out float v) || float.IsNaN(v)) return -1;
            int r = (int)v;
            return r >= 0 && r < Count ? r : -1;
        }
    }
}
