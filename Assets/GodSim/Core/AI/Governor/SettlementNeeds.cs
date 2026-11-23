using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The need vector of one settlement (each entry 0..1) plus the diagnostics that explain it.
    /// Layout of <see cref="Values"/>: <see cref="Housing"/>, <see cref="Storage"/>, <see cref="Defense"/>,
    /// then one slot per <see cref="ResourceType"/> (from <see cref="FirstResource"/>), then one slot per
    /// <see cref="ServiceType"/> (from <see cref="FirstService"/>). Gold need covers commerce: buildings
    /// contribute to it through Gold output and the Commerce service. The Defense and Commerce service
    /// slots are always 0 (they are folded into <see cref="Defense"/> and Gold). Reused between settlements.
    /// </summary>
    public sealed class SettlementNeeds
    {
        /// <summary>Slot index: housing.</summary>
        public const int Housing = 0;
        /// <summary>Slot index: storage capacity.</summary>
        public const int Storage = 1;
        /// <summary>Slot index: defense / military.</summary>
        public const int Defense = 2;
        /// <summary>First resource slot (slot = FirstResource + (int)ResourceType).</summary>
        public const int FirstResource = 3;
        /// <summary>First service slot (slot = FirstService + (int)ServiceType).</summary>
        public static readonly int FirstService = FirstResource + Counts.Resources;
        /// <summary>Total number of need slots.</summary>
        public static readonly int Count = FirstService + Counts.Services;

        /// <summary>Slot of a resource need.</summary>
        public static int ResourceSlot(ResourceType r) => FirstResource + (int)r;
        /// <summary>Slot of a service need.</summary>
        public static int ServiceSlot(ServiceType s) => FirstService + (int)s;

        /// <summary>Need per slot, each 0..1.</summary>
        public readonly float[] Values = new float[Count];

        // ------------------------------------------------------------------ diagnostics (for logs/UI)
        /// <summary>Population used for the evaluation.</summary>
        public int Population;
        /// <summary>Homeless residents (max of the housing module's count and capacity shortfall).</summary>
        public int Homeless;
        /// <summary>Free housing in complete dwellings.</summary>
        public float FreeHousing;
        /// <summary>Expected capacity of queued and under-construction dwellings.</summary>
        public float PendingHousing;
        /// <summary>Free housing the governor wants to keep: max(MinFreeHousing, fraction * population).</summary>
        public float TargetFreeHousing;
        /// <summary>Residents expected to arrive during the housing lead time.</summary>
        public float ProjectedGrowth;
        /// <summary>Housing shortfall after counting pending dwellings (&gt; 0 means build).</summary>
        public float HousingDeficit;
        /// <summary>Days of food reserve.</summary>
        public float FoodDays;
        /// <summary>Food net flow per tick (measured, or estimated from buildings before statistics exist).</summary>
        public float FoodNet;
        /// <summary>True when <see cref="FoodNet"/> comes from stockpile statistics.</summary>
        public bool FoodNetMeasured;
        /// <summary>Food eaten per tick by the population.</summary>
        public float FoodConsumption;
        /// <summary>Upcoming demand per resource (reserved costs + a working reserve).</summary>
        public readonly float[] ResourceDemand = new float[Counts.Resources];
        /// <summary>Building type that caused a production-chain need for the resource, -1 if none.</summary>
        public readonly int[] ChainSource = new int[Counts.Resources];
        /// <summary>Highest stockpile fill ratio (amount / capacity).</summary>
        public float StorageFill;
        /// <summary>Resource with the highest fill ratio.</summary>
        public ResourceType StorageResource;
        /// <summary>Threat level 0..1 used for the defense need.</summary>
        public float Threat;
        /// <summary>True when the civilization is at war with any civilization.</summary>
        public bool AtWar;
        /// <summary>True when a contacted hostile civilization has a settlement within the threat radius.</summary>
        public bool HostileNear;
        /// <summary>Strategy posture (0 peaceful, 1 defensive, 2 militarist).</summary>
        public int Posture;
        /// <summary>Current defense (soldier slots, defense service).</summary>
        public float DefenseCurrent;
        /// <summary>Defense the governor wants for the current threat.</summary>
        public float DefenseDesired;
        /// <summary>Gold upkeep per tick of complete buildings.</summary>
        public float GoldUpkeep;
        /// <summary>Average gold income per tick.</summary>
        public float GoldIncome;

        /// <summary>Food need (shortcut).</summary>
        public float Food => Values[FirstResource + (int)ResourceType.Food];
        /// <summary>Gold / commerce need (shortcut).</summary>
        public float Gold => Values[FirstResource + (int)ResourceType.Gold];
        /// <summary>Need of a resource.</summary>
        public float Resource(ResourceType r) => Values[FirstResource + (int)r];
        /// <summary>Need of a service.</summary>
        public float Service(ServiceType s) => Values[FirstService + (int)s];

        /// <summary>Resets all needs and diagnostics.</summary>
        public void Clear()
        {
            Array.Clear(Values, 0, Values.Length);
            Array.Clear(ResourceDemand, 0, ResourceDemand.Length);
            for (int i = 0; i < ChainSource.Length; i++) ChainSource[i] = -1;
            Population = Homeless = Posture = 0;
            FreeHousing = PendingHousing = TargetFreeHousing = ProjectedGrowth = HousingDeficit = 0f;
            FoodDays = FoodNet = FoodConsumption = 0f;
            FoodNetMeasured = false;
            StorageFill = 0f;
            StorageResource = ResourceType.Food;
            Threat = DefenseCurrent = DefenseDesired = GoldUpkeep = GoldIncome = 0f;
            AtWar = HostileNear = false;
        }

        /// <summary>Copies every value and diagnostic from <paramref name="other"/>.</summary>
        public void CopyFrom(SettlementNeeds other)
        {
            if (other == null || ReferenceEquals(other, this)) return;
            Array.Copy(other.Values, Values, Values.Length);
            Array.Copy(other.ResourceDemand, ResourceDemand, ResourceDemand.Length);
            Array.Copy(other.ChainSource, ChainSource, ChainSource.Length);
            Population = other.Population; Homeless = other.Homeless; Posture = other.Posture;
            FreeHousing = other.FreeHousing; PendingHousing = other.PendingHousing; TargetFreeHousing = other.TargetFreeHousing;
            ProjectedGrowth = other.ProjectedGrowth; HousingDeficit = other.HousingDeficit;
            FoodDays = other.FoodDays; FoodNet = other.FoodNet; FoodConsumption = other.FoodConsumption; FoodNetMeasured = other.FoodNetMeasured;
            StorageFill = other.StorageFill; StorageResource = other.StorageResource;
            Threat = other.Threat; DefenseCurrent = other.DefenseCurrent; DefenseDesired = other.DefenseDesired;
            GoldUpkeep = other.GoldUpkeep; GoldIncome = other.GoldIncome;
            AtWar = other.AtWar; HostileNear = other.HostileNear;
        }

        /// <summary>Sets a slot, clamped to [0, 1] and sanitized.</summary>
        public void Set(int slot, float value)
        {
            if (slot < 0 || slot >= Values.Length) return;
            Values[slot] = MathUtil.Clamp01(MathUtil.Sanitize(value));
        }

        /// <summary>Raises a slot to at least <paramref name="value"/>.</summary>
        public void Raise(int slot, float value)
        {
            if (slot < 0 || slot >= Values.Length) return;
            float v = MathUtil.Clamp01(MathUtil.Sanitize(value));
            if (v > Values[slot]) Values[slot] = v;
        }

        /// <summary>Human-readable slot name.</summary>
        public static string SlotName(int slot)
        {
            if (slot == Housing) return "housing";
            if (slot == Storage) return "storage";
            if (slot == Defense) return "defense";
            if (slot >= FirstResource && slot < FirstService) return ((ResourceType)(slot - FirstResource)).ToString().ToLowerInvariant();
            if (slot >= FirstService && slot < Count) return ((ServiceType)(slot - FirstService)).ToString().ToLowerInvariant();
            return "none";
        }
    }
}
