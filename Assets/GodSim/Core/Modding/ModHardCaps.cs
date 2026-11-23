using System;
using System.Collections.Generic;

namespace GodSim.Core.Modding
{
    /// <summary>
    /// Absolute bounds every mod-supplied value is checked against (inclusive). Options can never loosen them.
    /// Documented in docs/MODDING.md (whitelist table).
    /// </summary>
    public static class ModHardCaps
    {
        // Costs and amounts
        public const double MaxCost = 100000;              // building cost per resource, tech research cost
        public const double MinTechCost = 1;               // GameDatabase requires tech cost > 0
        public const double MaxRate = 10;                  // per-worker/per-tick rates, upkeep, aptitude multipliers
        public const double MinAptitude = 0.01;            // multipliers never reach 0 (avoids divide-by-zero downstream)
        public const double MinBuildWork = 0.1;            // GameDatabase requires BuildWork > 0
        public const double MaxBuildWork = 100000;
        public const int MaxJobSlots = 100;
        public const double MaxStorage = 100000;
        public const double MinVisualHeight = 0.1;
        public const double MaxVisualHeight = 20;
        public const int MaxMinPopulation = 1000000;
        // Races
        public const double MinLifeYears = 1;
        public const double MaxLifeYears = 2000;
        // Ages
        public const double MaxResearchThreshold = 10000000;
        // God powers
        public const double MaxManaCost = 100000;
        public const int MaxCooldownTicks = 9600;          // 100 years
        public const int MaxRadius = 32;
        // Tech effects (new techs)
        public const double MinEffectMultiplier = -0.9;
        public const double MaxEffectMultiplier = 2;
        public const double MinLifespanBonus = -50;
        public const double MaxLifespanBonus = 100;
        public const int MaxNewTechsPerPack = 256;
        public const int MaxPrerequisites = 16;
        public const int MaxEffects = 8;
        public const int MaxTechKeySuffixLength = 32;
        // Text
        public const int MaxNameLength = 48;
        public const int MaxDescriptionLength = 1024;
        public const int MaxSyllableLength = 24;
        public const int MaxFamilyNameLength = 32;
        public const int MaxPatternLength = 64;
        public const int MaxNameListEntries = 128;
    }

    /// <summary>
    /// Exact, case-sensitive enum-name lookups. Deliberately NOT Enum.Parse/TryParse: those accept numeric
    /// strings ("99") and flag combinations ("House, Well") that would index outside definition arrays.
    /// </summary>
    internal static class ModEnumNames
    {
        public static readonly Dictionary<string, BuildingType> Buildings = Build(EnumUtil.AllBuildings);
        public static readonly Dictionary<string, RaceId> Races = Build(EnumUtil.AllRaces);
        public static readonly Dictionary<string, AgeId> Ages = Build(EnumUtil.AllAges);
        public static readonly Dictionary<string, GodPowerType> Powers = Build(EnumUtil.AllGodPowers);
        public static readonly Dictionary<string, ResourceType> Resources = Build(EnumUtil.AllResources);
        public static readonly Dictionary<string, ServiceType> Services = Build(EnumUtil.AllServices);
        public static readonly Dictionary<string, TechEffectKind> EffectKinds = Build((TechEffectKind[])Enum.GetValues(typeof(TechEffectKind)));

        private static Dictionary<string, T> Build<T>(T[] values)
        {
            var d = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var v in values) d[v.ToString()] = v;
            return d;
        }

        public static bool TryGet<T>(Dictionary<string, T> table, string name, out T value)
        {
            value = default(T);
            return name != null && name.Length <= 64 && table.TryGetValue(name, out value);
        }
    }
}
