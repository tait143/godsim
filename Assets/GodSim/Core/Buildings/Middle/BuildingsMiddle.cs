using System;

namespace GodSim.Core.Buildings
{
    /// <summary>
    /// Building catalog, part 2: every Classical, Medieval and Renaissance building
    /// (<see cref="BuildingType"/> 28..47) plus the shared wonders Great Library, Colossus and
    /// Grand Bazaar (72..74). Pure data and balancing; no runtime state.
    /// <para>
    /// Rates follow the shared balance table: "PerWorker" inputs/outputs are per staffed worker per
    /// tick (1 tick = 1 day), services are points per tick for a fully staffed building (buildings
    /// without jobs provide them at full rate). A person eats about 0.05 Food per tick.
    /// </para>
    /// </summary>
    public static class BuildingsMiddle
    {
        /// <summary>First building type of the contiguous Classical..Renaissance block owned by this catalog part.</summary>
        public const BuildingType FirstEraType = BuildingType.Library;
        /// <summary>Last building type of the contiguous Classical..Renaissance block owned by this catalog part.</summary>
        public const BuildingType LastEraType = BuildingType.Printworks;
        /// <summary>First shared wonder owned by this catalog part.</summary>
        public const BuildingType FirstWonderType = BuildingType.GreatLibrary;
        /// <summary>Last shared wonder owned by this catalog part.</summary>
        public const BuildingType LastWonderType = BuildingType.GrandBazaar;

        private static readonly BuildingType[] _types = BuildTypeList();

        /// <summary>Number of building types this catalog part defines (23).</summary>
        public static int Count => _types.Length;

        /// <summary>
        /// Registers a freshly created definition for every owned building type into <paramref name="db"/>.
        /// Existing definitions for these types are replaced; other slots are left untouched.
        /// </summary>
        /// <param name="db">Database to fill. Must not be frozen.</param>
        /// <exception cref="ArgumentNullException"><paramref name="db"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="db"/> is already frozen.</exception>
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            if (db.IsFrozen) throw new InvalidOperationException("BuildingsMiddle.Register: GameDatabase is frozen.");
            for (int i = 0; i < _types.Length; i++) db.Register(Create(_types[i]));
        }

        /// <summary>True if <paramref name="type"/> is defined by this catalog part (28..47 or 72..74).</summary>
        public static bool Owns(BuildingType type)
        {
            return (type >= FirstEraType && type <= LastEraType) || (type >= FirstWonderType && type <= LastWonderType);
        }

        /// <summary>Returns a new array with every building type this catalog part defines, in ascending order.</summary>
        public static BuildingType[] GetTypes()
        {
            var copy = new BuildingType[_types.Length];
            Array.Copy(_types, copy, _types.Length);
            return copy;
        }

        /// <summary>
        /// Creates a new, independent definition for an owned building type (callers may mutate it freely).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not owned by this catalog part.</exception>
        public static BuildingDefinition Create(BuildingType type)
        {
            switch (type)
            {
                // Classical
                case BuildingType.Library: return MiddleClassicalBuildings.Library();
                case BuildingType.Aqueduct: return MiddleClassicalBuildings.Aqueduct();
                case BuildingType.Bathhouse: return MiddleClassicalBuildings.Bathhouse();
                case BuildingType.Theater: return MiddleClassicalBuildings.Theater();
                case BuildingType.Harbor: return MiddleClassicalBuildings.Harbor();
                case BuildingType.Workshop: return MiddleClassicalBuildings.Workshop();
                case BuildingType.Forum: return MiddleClassicalBuildings.Forum();
                case BuildingType.Arena: return MiddleClassicalBuildings.Arena();
                case BuildingType.StoneWall: return MiddleClassicalBuildings.StoneWall();
                // Medieval
                case BuildingType.Castle: return MiddleMedievalBuildings.Castle();
                case BuildingType.Cathedral: return MiddleMedievalBuildings.Cathedral();
                case BuildingType.Monastery: return MiddleMedievalBuildings.Monastery();
                case BuildingType.Mill: return MiddleMedievalBuildings.Mill();
                case BuildingType.Guildhall: return MiddleMedievalBuildings.Guildhall();
                case BuildingType.Tavern: return MiddleMedievalBuildings.Tavern();
                // Renaissance
                case BuildingType.University: return MiddleRenaissanceBuildings.University();
                case BuildingType.Bank: return MiddleRenaissanceBuildings.Bank();
                case BuildingType.Observatory: return MiddleRenaissanceBuildings.Observatory();
                case BuildingType.Hospital: return MiddleRenaissanceBuildings.Hospital();
                case BuildingType.Printworks: return MiddleRenaissanceBuildings.Printworks();
                // Shared wonders
                case BuildingType.GreatLibrary: return MiddleWonderBuildings.GreatLibrary();
                case BuildingType.Colossus: return MiddleWonderBuildings.Colossus();
                case BuildingType.GrandBazaar: return MiddleWonderBuildings.GrandBazaar();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "BuildingsMiddle does not define this building type.");
            }
        }

        private static BuildingType[] BuildTypeList()
        {
            int era = LastEraType - FirstEraType + 1;
            int wonders = LastWonderType - FirstWonderType + 1;
            var list = new BuildingType[era + wonders];
            for (int i = 0; i < era; i++) list[i] = (BuildingType)((int)FirstEraType + i);
            for (int i = 0; i < wonders; i++) list[era + i] = (BuildingType)((int)FirstWonderType + i);
            return list;
        }
    }
}
