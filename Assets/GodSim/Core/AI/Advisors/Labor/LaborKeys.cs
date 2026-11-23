using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys of the labor advisor (<see cref="LaborAdvisor"/>). Written keys live in
    /// <see cref="Settlement.Memory"/> under the "adv.labor." namespace; read keys come from other modules.
    /// </summary>
    public static class LaborKeys
    {
        // ------------------------------------------------------------------ written (settlement)
        /// <summary>EMA of the signed labor balance rate (-1..1): + idle workers, - unfilled jobs (per adult).</summary>
        public const string Balance = "adv.labor.bal";
        /// <summary>Hysteresis state: -1 labor shortage, 0 balanced, +1 unemployment surplus.</summary>
        public const string Mode = "adv.labor.mode";
        /// <summary>EMA of the soldier share of adults (0..1).</summary>
        public const string SoldierShare = "adv.labor.soldier";
        /// <summary>Slow EMA of the population (disaster detection).</summary>
        public const string PopEma = "adv.labor.pop";
        /// <summary>1 once the EMAs have been seeded.</summary>
        public const string Initialized = "adv.labor.init";
        /// <summary>Last raw count of idle adults (UI/debug).</summary>
        public const string Unemployed = "adv.labor.unemployed";
        /// <summary>Last raw count of open civilian job slots (UI/debug).</summary>
        public const string OpenSlots = "adv.labor.open";
        /// <summary>BuildingType (as float) the advisor pushed hardest at its last run; -1 when none.</summary>
        public const string Top = "adv.labor.top";
        /// <summary>0..1 recovery intensity after a population crash.</summary>
        public const string Recovery = "adv.labor.recovery";
        /// <summary>Tick of the last explained (logged) decision.</summary>
        public const string LogTick = "adv.labor.logTick";
        /// <summary>Signature of the last explained decision (log only when it changes, or yearly).</summary>
        public const string LogSignature = "adv.labor.logSig";

        // ------------------------------------------------------------------ read (other modules)
        /// <summary>Civilization, written by strategy: perceived external threat 0..1.</summary>
        public const string CivThreat = "ai.threat";
        /// <summary>Civilization, written by strategy: number of active wars.</summary>
        public const string CivWars = "ai.wars";
        /// <summary>Settlement, written by economy: days of food left.</summary>
        public const string FoodDays = "econ.foodDays";
    }

    /// <summary>Economic domains the labor advisor values jobs by (mapped from building outputs and services).</summary>
    public enum LaborDomain : byte { Food = 0, Materials = 1, Industry = 2, Wealth = 3, Science = 4, Faith = 5, Health = 6, Defense = 7, Culture = 8 }

    /// <summary>Static, allocation-free mapping tables between resources/services, domains, advisor ids and traits.</summary>
    public static class LaborDomains
    {
        public const int Count = 9;

        // Other advisors' ids that speak for each domain (urgency is the max of those present).
        private static readonly string[][] s_advisorIds =
        {
            new[] { "food" },
            new[] { "supply", "materials", "industry" },
            new[] { "industry", "supply" },
            new[] { "treasury", "trade", "commerce" },
            new[] { "science", "research" },
            new[] { "faith", "religion" },
            new[] { "health" },
            new[] { "defense", "military" },
            new[] { "happiness", "culture" },
        };

        private static readonly string[] s_names = { "food", "materials", "industry", "wealth", "science", "faith", "health", "defense", "culture" };

        /// <summary>Lowercase display name of a domain (for reason strings).</summary>
        public static string Name(LaborDomain d) { int i = (int)d; return i >= 0 && i < s_names.Length ? s_names[i] : "work"; }

        public static LaborDomain OfResource(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Food: return LaborDomain.Food;
                case ResourceType.Wood: case ResourceType.Stone: case ResourceType.Ore: return LaborDomain.Materials;
                case ResourceType.Gold: return LaborDomain.Wealth;
                case ResourceType.Weapons: return LaborDomain.Defense;
                default: return LaborDomain.Industry; // Metal, Tools, Cloth, Goods, Fuel, Machinery, Electronics
            }
        }

        public static LaborDomain OfService(ServiceType s)
        {
            switch (s)
            {
                case ServiceType.Faith: return LaborDomain.Faith;
                case ServiceType.Health: case ServiceType.Water: return LaborDomain.Health;
                case ServiceType.Research: return LaborDomain.Science;
                case ServiceType.Entertainment: return LaborDomain.Culture;
                case ServiceType.Defense: return LaborDomain.Defense;
                case ServiceType.Power: return LaborDomain.Industry;
                default: return LaborDomain.Wealth; // Commerce, Administration
            }
        }

        /// <summary>Reads the highest urgency other advisors reported for a domain. False when none reported.</summary>
        public static bool TryReadUrgency(Settlement s, LaborDomain d, out float urgency)
        {
            urgency = 0f;
            if (s == null) return false;
            var ids = s_advisorIds[(int)d];
            bool found = false;
            for (int i = 0; i < ids.Length; i++)
            {
                if (!s.Memory.TryGetValue(AdvisorBoard.UrgencyKey(ids[i]), out float u)) continue;
                u = MathUtil.Clamp01(MathUtil.Sanitize(u));
                if (!found || u > urgency) urgency = u;
                found = true;
            }
            return found;
        }

        /// <summary>Personality multiplier (0.75..1.25) a civ applies to a domain: industrious, curious, pious... civs value different work.</summary>
        public static float TraitMultiplier(Civilization civ, GameDatabase db, LaborDomain d)
        {
            switch (d)
            {
                case LaborDomain.Materials:
                case LaborDomain.Industry: return 0.75f + 0.5f * AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
                case LaborDomain.Wealth: return 0.75f + 0.5f * AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
                case LaborDomain.Science: return 0.75f + 0.5f * AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
                case LaborDomain.Faith: return 0.75f + 0.5f * AdvisorBoard.Trait(civ, db, AITrait.Piety);
                case LaborDomain.Defense: return 0.75f + 0.5f * AdvisorBoard.Trait(civ, db, AITrait.Aggression);
                default: return 1f;
            }
        }
    }
}
