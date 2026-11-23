using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.Tests.Progression.Content
{
    /// <summary>
    /// The building economy as the design states it, for testing the tech tree without the Buildings module
    /// (modules never reference each other). MinAge, races, inputs and outputs are transcribed from the
    /// comments of the BuildingType enum. The cost materials of unlock targets are those of the Buildings
    /// catalog when the unlocks were last reviewed: only which resources are needed matters, not how much.
    /// The composed game can run TechTreeReport.FindUnsuppliedUnlocks on the real catalog.
    /// </summary>
    internal static class DesignEconomy
    {
        private static readonly ResourceType[] Nothing = new ResourceType[0];

        private sealed class Row
        {
            public BuildingType Type;
            public ResourceType[] Cost = Nothing;
            public ResourceType[] Inputs = Nothing;
            public ResourceType[] Outputs = Nothing;
        }

        private static readonly Row[] Rows = BuildRows();

        /// <summary>Intended MinAge of every building, from the comments of the BuildingType enum.</summary>
        public static AgeId IntendedMinAge(BuildingType b)
        {
            int i = (int)b;
            if (i <= 10) return AgeId.Dawn;
            if (i <= 19) return AgeId.Bronze;
            if (i <= 27) return AgeId.Iron;
            if (i <= 36) return AgeId.Classical;
            if (i <= 42) return AgeId.Medieval;
            if (i <= 47) return AgeId.Renaissance;
            if (i <= 52) return AgeId.Industrial;
            if (i <= 58) return AgeId.Modern;
            if (i <= 62) return AgeId.Information;
            if (i <= 66) return AgeId.Ascension;
            switch (b)
            {
                case BuildingType.GrandCathedral: return AgeId.Medieval;
                case BuildingType.WorldTree: return AgeId.Iron;
                case BuildingType.DeepForge: return AgeId.Bronze;
                case BuildingType.WarIdol: return AgeId.Bronze;
                case BuildingType.SunPyramid: return AgeId.Iron;
                case BuildingType.GreatLibrary: return AgeId.Classical;
                case BuildingType.Colossus: return AgeId.Classical;
                default: return AgeId.Medieval; // GrandBazaar
            }
        }

        /// <summary>Races allowed to build each building, from the comments of the BuildingType enum.</summary>
        public static RaceMask IntendedRaces(BuildingType b)
        {
            switch (b)
            {
                case BuildingType.GrandCathedral: return RaceMask.Human;
                case BuildingType.WorldTree: return RaceMask.Elf;
                case BuildingType.DeepForge: return RaceMask.Dwarf;
                case BuildingType.WarIdol: return RaceMask.Orc;
                case BuildingType.SunPyramid: return RaceMask.Saurian;
                default: return RaceMask.All;
            }
        }

        /// <summary>True if the table states the needs (cost materials and inputs) of the building.</summary>
        public static bool Describes(BuildingType b)
        {
            for (int i = 0; i < Rows.Length; i++) if (Rows[i].Type == b) return true;
            return false;
        }

        /// <summary>Resources the building outputs, according to the table (empty if it produces nothing).</summary>
        public static ResourceType[] OutputsOf(BuildingType b)
        {
            for (int i = 0; i < Rows.Length; i++) if (Rows[i].Type == b) return Rows[i].Outputs;
            return Nothing;
        }

        /// <summary>Registers one definition per table row (other slots stay empty for TestDb.FillMissing).</summary>
        public static void Register(GameDatabase db)
        {
            foreach (var row in Rows)
            {
                db.Register(new BuildingDefinition
                {
                    Type = row.Type,
                    Name = row.Type.ToString(),
                    Category = BuildingCategory.Resource,
                    MinAge = IntendedMinAge(row.Type),
                    AllowedRaces = IntendedRaces(row.Type),
                    BuildWork = 10f,
                    Cost = Amounts(row.Cost, 10f),
                    InputsPerWorker = Amounts(row.Inputs, 0.05f),
                    OutputsPerWorker = Amounts(row.Outputs, 0.05f),
                });
            }
        }

        private static ResourceAmount[] Amounts(ResourceType[] types, float amount)
        {
            var a = new ResourceAmount[types.Length];
            for (int i = 0; i < types.Length; i++) a[i] = new ResourceAmount(types[i], amount);
            return a;
        }

        private static Row[] BuildRows()
        {
            const ResourceType Food = ResourceType.Food, Wood = ResourceType.Wood, Stone = ResourceType.Stone,
                Ore = ResourceType.Ore, Metal = ResourceType.Metal, Gold = ResourceType.Gold, Tools = ResourceType.Tools,
                Cloth = ResourceType.Cloth, Goods = ResourceType.Goods, Weapons = ResourceType.Weapons, Fuel = ResourceType.Fuel,
                Machinery = ResourceType.Machinery, Electronics = ResourceType.Electronics;
            var rows = new List<Row>
            {
                // ---- producers (enum comments)
                new Row { Type = BuildingType.GathererHut, Outputs = new[] { Food } },
                new Row { Type = BuildingType.HuntingLodge, Outputs = new[] { Food } },
                new Row { Type = BuildingType.FishingHut, Outputs = new[] { Food } },
                new Row { Type = BuildingType.WoodcutterCamp, Outputs = new[] { Wood } },
                new Row { Type = BuildingType.StoneQuarry, Outputs = new[] { Stone } },
                new Row { Type = BuildingType.Farm, Cost = new[] { Wood, Stone }, Outputs = new[] { Food } },
                new Row { Type = BuildingType.Pasture, Outputs = new[] { Food, Cloth } },
                new Row { Type = BuildingType.Mine, Cost = new[] { Wood, Stone }, Outputs = new[] { Ore } },
                new Row { Type = BuildingType.Smelter, Inputs = new[] { Ore }, Outputs = new[] { Metal } },
                new Row { Type = BuildingType.Weaver, Outputs = new[] { Cloth } },
                new Row { Type = BuildingType.Blacksmith, Cost = new[] { Wood, Stone, Metal }, Inputs = new[] { Metal, Wood }, Outputs = new[] { Tools } },
                new Row { Type = BuildingType.Armory, Inputs = new[] { Metal, Wood }, Outputs = new[] { Weapons } },
                new Row { Type = BuildingType.GoldMine, Outputs = new[] { Gold } },
                new Row { Type = BuildingType.Workshop, Inputs = new[] { Wood, Cloth }, Outputs = new[] { Goods } },
                new Row { Type = BuildingType.Mill, Outputs = new[] { Food } },
                new Row { Type = BuildingType.Bank, Cost = new[] { Stone, Wood, Metal, Gold }, Outputs = new[] { Gold } },
                new Row { Type = BuildingType.Printworks, Inputs = new[] { Wood }, Outputs = new[] { Goods } },
                new Row { Type = BuildingType.Factory, Cost = new[] { Wood, Stone, Metal, Tools }, Inputs = new[] { Metal, Fuel }, Outputs = new[] { Machinery, Goods } },
                new Row { Type = BuildingType.CoalMine, Cost = new[] { Wood, Stone, Metal, Tools }, Outputs = new[] { Fuel } },
                new Row { Type = BuildingType.Sawmill, Outputs = new[] { Wood } },
                new Row { Type = BuildingType.Foundry, Inputs = new[] { Ore, Fuel }, Outputs = new[] { Metal } },
                new Row { Type = BuildingType.OilWell, Outputs = new[] { Fuel } },
                new Row { Type = BuildingType.IndustrialFarm, Outputs = new[] { Food } },
                new Row { Type = BuildingType.ElectronicsPlant, Cost = new[] { Stone, Metal, Gold, Tools, Machinery }, Inputs = new[] { Metal, Gold }, Outputs = new[] { Electronics } },
                new Row { Type = BuildingType.HydroponicsTower, Cost = new[] { Stone, Metal, Gold, Machinery, Electronics }, Outputs = new[] { Food } },
                new Row { Type = BuildingType.DeepForge, Cost = new[] { Wood, Stone }, Inputs = new[] { Ore }, Outputs = new[] { Metal, Tools } },
                // ---- other unlock targets (current and former), needs only
                new Row { Type = BuildingType.Herbalist, Cost = new[] { Wood, Stone } },
                new Row { Type = BuildingType.Temple, Cost = new[] { Stone, Wood, Metal, Tools } },
                new Row { Type = BuildingType.WarIdol, Cost = new[] { Wood, Stone } },
                new Row { Type = BuildingType.WorldTree, Cost = new[] { Wood, Stone, Cloth } },
                new Row { Type = BuildingType.SunPyramid, Cost = new[] { Wood, Stone, Metal } },
                new Row { Type = BuildingType.Library, Cost = new[] { Wood, Stone, Cloth, Gold } },
                new Row { Type = BuildingType.Aqueduct, Cost = new[] { Stone, Wood, Tools } },
                new Row { Type = BuildingType.Monastery, Cost = new[] { Wood, Stone, Tools } },
                new Row { Type = BuildingType.GrandCathedral, Cost = new[] { Wood, Stone, Gold, Tools, Goods } },
                new Row { Type = BuildingType.University, Cost = new[] { Stone, Wood, Gold, Goods, Cloth } },
                new Row { Type = BuildingType.PowerPlant, Cost = new[] { Stone, Metal, Tools, Fuel, Machinery }, Inputs = new[] { Fuel } },
                new Row { Type = BuildingType.DataCenter, Cost = new[] { Stone, Metal, Gold, Machinery, Electronics }, Inputs = new[] { Electronics } },
                new Row { Type = BuildingType.FusionReactor, Cost = new[] { Stone, Metal, Gold, Machinery, Electronics } },
            };
            return rows.ToArray();
        }
    }
}
