using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Memory keys and text helpers of the logistics advisor. Keys under "ai.logi." are written by
    /// <see cref="LogisticsAdvisor"/> (UI/debug/learning); "build.*" keys are read from the construction module.
    /// </summary>
    public static class LogisticsKeys
    {
        /// <summary>Settlement key written by construction: unpaid sites waiting for resources (absent when 0).</summary>
        public const string SitesWaiting = "build.sitesWaiting";
        /// <summary>Settlement key prefix written by construction: ticks an unpaid site has waited ("build.wait.{buildingId}").</summary>
        public const string WaitPrefix = "build.wait.";
        /// <summary>Ticks after which construction abandons an unpaid site (three years; mirrors the construction rule).</summary>
        public const int AbandonTicks = 3 * SimTime.TicksPerYear;

        /// <summary>Construction stall risk 0..1 (same value as the advisor urgency).</summary>
        public const string StallRisk = "ai.logi.stallRisk";
        /// <summary>Days until the worst bottleneck material is affordable (0 = none, 9999 = never at current income).</summary>
        public const string EtaDays = "ai.logi.etaDays";
        /// <summary>Bottleneck resource index (ResourceType), -1 when none.</summary>
        public const string Bottleneck = "ai.logi.bottleneck";
        /// <summary>1 while a placed or queued cost can never fit in the stockpile (hard stall).</summary>
        public const string HardStall = "ai.logi.hardStall";
        /// <summary>1 while construction is considered stalled (used to log transitions only once).</summary>
        public const string Stalled = "ai.logi.stalled";
        /// <summary>Tick of the last logged vote explanation (rate limit).</summary>
        public const string ExplainTick = "ai.logi.explainTick";

        private static readonly string[] s_resourceNames = BuildResourceNames();

        /// <summary>Lower-case display name of a resource ("stone"), allocation-free.</summary>
        public static string ResourceName(ResourceType r)
        {
            int i = (int)r;
            return i >= 0 && i < s_resourceNames.Length ? s_resourceNames[i] : "materials";
        }

        /// <summary>Display name of a building type from its definition (falls back to the enum name).</summary>
        public static string BuildingName(GameDatabase db, int type)
        {
            if (type < 0 || type >= Counts.Buildings) return "a project";
            if (db != null && db.TryGetBuilding((BuildingType)type, out var def) && !string.IsNullOrEmpty(def.Name)) return def.Name;
            return ((BuildingType)type).ToString();
        }

        private static string[] BuildResourceNames()
        {
            var a = new string[Counts.Resources];
            for (int i = 0; i < a.Length; i++) a[i] = ((ResourceType)i).ToString().ToLowerInvariant();
            return a;
        }
    }
}
