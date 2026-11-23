using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven candidate lists for <see cref="HappinessAdvisor"/>, derived once per database from
    /// <see cref="BuildingDefinition"/> fields (services, outputs, housing). Wonders and the town center are excluded.
    /// </summary>
    public sealed class HappinessCatalog
    {
        /// <summary>Placement flags that are hard terrain requirements (a vote may be unbuildable on some maps).</summary>
        public const PlacementRule HardPlacement = PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills |
            PlacementRule.NearMountain | PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone |
            PlacementRule.NearGold | PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver;

        /// <summary>The database the lists were built from.</summary>
        public readonly GameDatabase Db;
        /// <summary>Buildings that provide Entertainment.</summary>
        public readonly List<BuildingType> Culture = new List<BuildingType>();
        /// <summary>Buildings that provide Faith.</summary>
        public readonly List<BuildingType> Faith = new List<BuildingType>();
        /// <summary>Staffed buildings with a net food output.</summary>
        public readonly List<BuildingType> Food = new List<BuildingType>();
        /// <summary>Dwellings.</summary>
        public readonly List<BuildingType> Dwellings = new List<BuildingType>();
        /// <summary>Buildings that provide Administration (governance of restless outposts).</summary>
        public readonly List<BuildingType> Admin = new List<BuildingType>();
        /// <summary>Indexed by (int)BuildingType: provides Entertainment.</summary>
        public readonly bool[] IsCulture = new bool[Counts.Buildings];
        /// <summary>Indexed by (int)BuildingType: provides Faith.</summary>
        public readonly bool[] IsFaith = new bool[Counts.Buildings];

        /// <summary>Builds the lists from a (normally frozen) database.</summary>
        public HappinessCatalog(GameDatabase db)
        {
            Db = db;
            if (db == null) return;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                if (!db.TryGetBuilding((BuildingType)i, out var d) || d == null) continue;
                if (d.IsWonder || d.UniquePerCivilization || d.Type == BuildingType.TownCenter) continue;
                if (ServicePoints(d, ServiceType.Entertainment) > 0f) { Culture.Add(d.Type); IsCulture[i] = true; }
                if (ServicePoints(d, ServiceType.Faith) > 0f) { Faith.Add(d.Type); IsFaith[i] = true; }
                if (d.HasJobs && NetFoodPerWorker(d) > 0f) Food.Add(d.Type);
                if (d.IsDwelling) Dwellings.Add(d.Type);
                if (ServicePoints(d, ServiceType.Administration) > 0f) Admin.Add(d.Type);
            }
        }

        /// <summary>Service points per tick a (fully staffed) building provides for a service.</summary>
        public static float ServicePoints(BuildingDefinition d, ServiceType t)
        {
            if (d == null || d.Services == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < d.Services.Length; i++)
                if (d.Services[i].Type == t) sum += MathUtil.Sanitize(d.Services[i].Amount);
            return sum > 0f ? sum : 0f;
        }

        /// <summary>Food produced minus food consumed per staffed worker per tick.</summary>
        public static float NetFoodPerWorker(BuildingDefinition d)
        {
            if (d == null) return 0f;
            float v = 0f;
            if (d.OutputsPerWorker != null)
                for (int i = 0; i < d.OutputsPerWorker.Length; i++)
                    if (d.OutputsPerWorker[i].Type == ResourceType.Food) v += MathUtil.Sanitize(d.OutputsPerWorker[i].Amount);
            if (d.InputsPerWorker != null)
                for (int i = 0; i < d.InputsPerWorker.Length; i++)
                    if (d.InputsPerWorker[i].Type == ResourceType.Food) v -= MathUtil.Sanitize(d.InputsPerWorker[i].Amount);
            return v;
        }

        /// <summary>Rough construction burden: total resource cost plus a quarter of the build work.</summary>
        public static float CostWeight(BuildingDefinition d)
        {
            if (d == null) return 0f;
            float c = 0f;
            if (d.Cost != null)
                for (int i = 0; i < d.Cost.Length; i++) c += Math.Max(0f, MathUtil.Sanitize(d.Cost[i].Amount));
            return c + 0.25f * Math.Max(0f, MathUtil.Sanitize(d.BuildWork));
        }

        /// <summary>True when the building needs specific terrain (it may not be placeable everywhere).</summary>
        public static bool HasHardPlacement(BuildingDefinition d) => d != null && (d.Placement & HardPlacement) != 0;
    }
}
