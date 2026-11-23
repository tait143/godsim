using System;

namespace GodSim.Core.Buildings
{
    /// <summary>
    /// Building catalog, part 3: every <see cref="BuildingDefinition"/> from the Industrial age to the
    /// Ascension age plus the five racial wonders (<see cref="BuildingType"/> 48..71, i.e.
    /// <see cref="FirstType"/>..<see cref="LastType"/>).
    /// <para>
    /// Pure data + balancing. The composition root (via <c>BuildingCatalog</c>) calls
    /// <see cref="Register"/> once on a mutable <see cref="GameDatabase"/> before it is frozen.
    /// Every call creates fresh definition instances, so no mutable state is shared between databases.
    /// </para>
    /// <para>
    /// Balance conventions: a person eats ~0.05 Food per tick. Production numbers are per staffed worker
    /// per tick (<see cref="BuildingDefinition.InputsPerWorker"/>/<see cref="BuildingDefinition.OutputsPerWorker"/>);
    /// services are points per tick for a fully staffed building (jobless buildings provide them at full rate).
    /// Production chains bootstrap transitively from the pre-Industrial resources (Food..Weapons): Fuel, Machinery and
    /// Electronics become reachable in a settlement with coal but no oil (CoalMine) and with oil but no coal (OilWell,
    /// which therefore costs no Machinery), and every producer of those resources has a MinTier no higher than its
    /// consumers (stockpiles are per settlement). Placement uses at most one hard requirement flag per building, because
    /// hard flags are all required at once.
    /// </para>
    /// <para>
    /// Storage: <see cref="Stockpile.TryPay"/> needs the whole cost in stock at once and capacity is per resource, so no
    /// single cost entry exceeds <see cref="MaxSingleCost"/> (<see cref="MaxSingleCostPreIndustrial"/> for wonders
    /// available before the Industrial age). Costs also only use resources that exist in the age where a building can
    /// first be unlocked; techs may unlock a building one age before its MinAge.
    /// </para>
    /// </summary>
    public static partial class BuildingsLate
    {
        /// <summary>
        /// Largest amount of any single resource in one cost of this catalog part. Per-resource capacity is
        /// <see cref="Stockpile.DefaultCapacity"/> (150) plus storage buildings: a late town with its Town Center (+100),
        /// a Granary (+150), four Storehouses (+120 each) and a Rail Station (+200) holds 1,080, about 1.5x this cap,
        /// before any Arcology (+400) or storage tech modifiers. The Ascension Gate's largest entries sit at this cap.
        /// Balance pass 2026-11-19: was 900 against a 940 reference town (no margin; the Gate was never placed).
        /// </summary>
        public const float MaxSingleCost = 700f;

        /// <summary>
        /// Largest single cost entry of a building available before the Industrial age (the racial wonders): a village
        /// with base capacity (150), a Town Center (+100) and one Storehouse (+120) holds 370, and two Storehouses give
        /// 490, so these wonders never wait on storage that a small settlement cannot reach.
        /// </summary>
        public const float MaxSingleCostPreIndustrial = 400f;

        /// <summary>First building type owned by this catalog part (inclusive).</summary>
        public const BuildingType FirstType = BuildingType.Factory;

        /// <summary>Last building type owned by this catalog part (inclusive).</summary>
        public const BuildingType LastType = BuildingType.SunPyramid;

        /// <summary>Number of building types registered by <see cref="Register"/> (24).</summary>
        public const int Count = (int)LastType - (int)FirstType + 1;

        /// <summary>True if <paramref name="type"/> belongs to this catalog part (48..71).</summary>
        public static bool Owns(BuildingType type) => type >= FirstType && type <= LastType;

        /// <summary>Returns a fresh array with every building type owned by this catalog part, in enum order.</summary>
        public static BuildingType[] GetTypes()
        {
            var types = new BuildingType[Count];
            for (int i = 0; i < Count; i++) types[i] = (BuildingType)((int)FirstType + i);
            return types;
        }

        /// <summary>
        /// Registers the definitions of BuildingType 48..71 into <paramref name="db"/>. Slots outside that
        /// range are never touched. Re-registering replaces this part's previous definitions.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="db"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The database is already frozen (nothing is registered).</exception>
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            if (db.IsFrozen) throw new InvalidOperationException("GameDatabase is frozen; BuildingsLate must register before Freeze().");
            var defs = CreateAll();
            for (int i = 0; i < defs.Length; i++) db.Register(defs[i]);
        }

        /// <summary>Creates fresh definitions for every type in this catalog part, in enum order.</summary>
        public static BuildingDefinition[] CreateAll()
        {
            var defs = new BuildingDefinition[Count];
            for (int i = 0; i < Count; i++) defs[i] = Create((BuildingType)((int)FirstType + i));
            return defs;
        }

        /// <summary>Creates a fresh definition for one building type of this catalog part.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not in 48..71.</exception>
        public static BuildingDefinition Create(BuildingType type)
        {
            BuildingDefinition d;
            switch (type)
            {
                // Industrial
                case BuildingType.Factory: d = MakeFactory(); break;
                case BuildingType.CoalMine: d = MakeCoalMine(); break;
                case BuildingType.Sawmill: d = MakeSawmill(); break;
                case BuildingType.RailStation: d = MakeRailStation(); break;
                case BuildingType.Foundry: d = MakeFoundry(); break;
                // Modern
                case BuildingType.PowerPlant: d = MakePowerPlant(); break;
                case BuildingType.OilWell: d = MakeOilWell(); break;
                case BuildingType.IndustrialFarm: d = MakeIndustrialFarm(); break;
                case BuildingType.ResearchLab: d = MakeResearchLab(); break;
                case BuildingType.Stadium: d = MakeStadium(); break;
                case BuildingType.MilitaryBase: d = MakeMilitaryBase(); break;
                // Information
                case BuildingType.DataCenter: d = MakeDataCenter(); break;
                case BuildingType.ElectronicsPlant: d = MakeElectronicsPlant(); break;
                case BuildingType.Airport: d = MakeAirport(); break;
                case BuildingType.MedicalCenter: d = MakeMedicalCenter(); break;
                // Ascension
                case BuildingType.HydroponicsTower: d = MakeHydroponicsTower(); break;
                case BuildingType.FusionReactor: d = MakeFusionReactor(); break;
                case BuildingType.Arcology: d = MakeArcology(); break;
                case BuildingType.AscensionGate: d = MakeAscensionGate(); break;
                // Racial wonders
                case BuildingType.GrandCathedral: d = MakeGrandCathedral(); break;
                case BuildingType.WorldTree: d = MakeWorldTree(); break;
                case BuildingType.DeepForge: d = MakeDeepForge(); break;
                case BuildingType.WarIdol: d = MakeWarIdol(); break;
                case BuildingType.SunPyramid: d = MakeSunPyramid(); break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "BuildingsLate only defines BuildingType " + (int)FirstType + ".." + (int)LastType + ".");
            }
            d.Type = type;
            return d;
        }

        // ------------------------------------------------------------------ data-building helpers

        /// <summary>Construction cost; zero entries are dropped and the rest kept in ResourceType order (deterministic).</summary>
        private static ResourceAmount[] Cost(float food = 0f, float wood = 0f, float stone = 0f, float ore = 0f, float metal = 0f,
            float gold = 0f, float tools = 0f, float cloth = 0f, float goods = 0f, float weapons = 0f, float fuel = 0f,
            float machinery = 0f, float electronics = 0f)
        {
            var values = new[] { food, wood, stone, ore, metal, gold, tools, cloth, goods, weapons, fuel, machinery, electronics };
            int n = 0;
            for (int i = 0; i < values.Length; i++) if (values[i] > 0f) n++;
            var result = new ResourceAmount[n];
            int k = 0;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > 0f) result[k++] = new ResourceAmount((ResourceType)i, values[i]);
            return result;
        }

        /// <summary>A single per-worker resource flow.</summary>
        private static ResourceAmount[] Flow(ResourceType type, float amount) => new[] { new ResourceAmount(type, amount) };

        /// <summary>Two per-worker resource flows.</summary>
        private static ResourceAmount[] Flow(ResourceType a, float amountA, ResourceType b, float amountB) =>
            new[] { new ResourceAmount(a, amountA), new ResourceAmount(b, amountB) };

        /// <summary>One service.</summary>
        private static ServiceAmount[] Svc(ServiceType type, float amount) => new[] { new ServiceAmount(type, amount) };

        /// <summary>Two services.</summary>
        private static ServiceAmount[] Svc(ServiceType a, float amountA, ServiceType b, float amountB) =>
            new[] { new ServiceAmount(a, amountA), new ServiceAmount(b, amountB) };

        /// <summary>Three services.</summary>
        private static ServiceAmount[] Svc(ServiceType a, float amountA, ServiceType b, float amountB, ServiceType c, float amountC) =>
            new[] { new ServiceAmount(a, amountA), new ServiceAmount(b, amountB), new ServiceAmount(c, amountC) };

        /// <summary>Applies the shared wonder flags (one per civilization, one per settlement).</summary>
        private static BuildingDefinition AsWonder(BuildingDefinition d)
        {
            d.IsWonder = true;
            d.UniquePerCivilization = true;
            d.MaxPerSettlement = 1;
            d.Category = BuildingCategory.Wonder;
            return d;
        }

        /// <summary>Applies the racial-unique flags: a wonder that only <paramref name="race"/> may build.</summary>
        private static BuildingDefinition AsRacialWonder(BuildingDefinition d, RaceId race)
        {
            AsWonder(d);
            d.AllowedRaces = race.ToMask();
            return d;
        }
    }
}
