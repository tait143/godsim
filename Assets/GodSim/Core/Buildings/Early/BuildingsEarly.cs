using System;

namespace GodSim.Core.Buildings
{
    /// <summary>
    /// Building catalog, part 1: every Dawn, Bronze and Iron age building
    /// (<see cref="BuildingType"/> 0..27, <see cref="BuildingType.TownCenter"/> through
    /// <see cref="BuildingType.Scriptorium"/>). Pure data and balancing; no runtime state.
    /// <para>
    /// Rates follow the shared balance table: "PerWorker" inputs/outputs are per staffed worker per
    /// tick (1 tick = 1 day), services are points per tick for a fully staffed building (buildings
    /// without jobs provide them at full rate). A person eats about 0.05 Food per tick.
    /// </para>
    /// <para>
    /// Cost tiers: Dawn buildings cost only Wood and Stone (a fresh camp's founding stock), Bronze
    /// buildings add more Stone and at most a little Metal, and Iron buildings add Metal and Tools.
    /// No building costs a resource that only it can produce (Smelter needs no Metal, Blacksmith
    /// needs no Tools), so every age can bootstrap its own economy.
    /// </para>
    /// The lead's <c>BuildingCatalog.Register</c> calls <see cref="Register"/> together with the
    /// other two catalog parts (28..47 + 72..74 and 48..71).
    /// </summary>
    public static partial class BuildingsEarly
    {
        /// <summary>First building type defined by this catalog part.</summary>
        public const BuildingType FirstType = BuildingType.TownCenter;

        /// <summary>Last building type defined by this catalog part.</summary>
        public const BuildingType LastType = BuildingType.Scriptorium;

        /// <summary>Number of building types defined by this catalog part (28).</summary>
        public const int TypeCount = (int)LastType - (int)FirstType + 1;

        /// <summary>True if <paramref name="type"/> is one of the types this catalog part defines (0..27).</summary>
        public static bool Covers(BuildingType type) => type >= FirstType && type <= LastType;

        /// <summary>
        /// Registers the 28 Dawn/Bronze/Iron building definitions into <paramref name="db"/>.
        /// Every call registers freshly created instances, so separate databases never share
        /// mutable definitions. Registration is all-or-nothing: a frozen database is rejected
        /// before anything is written.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="db"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="db"/> is already frozen.</exception>
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            if (db.IsFrozen) throw new InvalidOperationException("GameDatabase is frozen; BuildingsEarly must register before Freeze().");
            var defs = CreateDefinitions();
            for (int i = 0; i < defs.Length; i++) db.Register(defs[i]);
        }

        /// <summary>
        /// Creates new instances of all 28 definitions, indexed by <c>(int)BuildingType</c>
        /// (index 0 = TownCenter ... 27 = Scriptorium). Each call returns new objects and arrays.
        /// </summary>
        public static BuildingDefinition[] CreateDefinitions()
        {
            var defs = new BuildingDefinition[TypeCount];
            // Dawn (0..10)
            Put(defs, MakeTownCenter());
            Put(defs, MakeHouse());
            Put(defs, MakeWell());
            Put(defs, MakeStorehouse());
            Put(defs, MakeGathererHut());
            Put(defs, MakeHuntingLodge());
            Put(defs, MakeFishingHut());
            Put(defs, MakeWoodcutterCamp());
            Put(defs, MakeStoneQuarry());
            Put(defs, MakeSpiritTotem());
            Put(defs, MakeCampfire());
            // Bronze (11..19)
            Put(defs, MakeFarm());
            Put(defs, MakePasture());
            Put(defs, MakeGranary());
            Put(defs, MakeMine());
            Put(defs, MakeSmelter());
            Put(defs, MakeShrine());
            Put(defs, MakeHerbalist());
            Put(defs, MakeWeaver());
            Put(defs, MakePalisade());
            // Iron (20..27)
            Put(defs, MakeBlacksmith());
            Put(defs, MakeTemple());
            Put(defs, MakeBarracks());
            Put(defs, MakeMarket());
            Put(defs, MakeWatchtower());
            Put(defs, MakeArmory());
            Put(defs, MakeGoldMine());
            Put(defs, MakeScriptorium());

            for (int i = 0; i < defs.Length; i++)
                if (defs[i] == null) throw new InvalidOperationException("BuildingsEarly is missing a definition for " + (BuildingType)i + ".");
            return defs;
        }

        // ------------------------------------------------------------------ helpers
        private static void Put(BuildingDefinition[] defs, BuildingDefinition def)
        {
            int i = (int)def.Type;
            if (i < 0 || i >= defs.Length) throw new InvalidOperationException("BuildingsEarly does not own building type " + def.Type + ".");
            if (defs[i] != null) throw new InvalidOperationException("BuildingsEarly defines " + def.Type + " twice.");
            defs[i] = def;
        }

        private static ResourceAmount R(ResourceType type, float amount) => new ResourceAmount(type, amount);

        private static ServiceAmount S(ServiceType type, float amount) => new ServiceAmount(type, amount);

        /// <summary>New single-entry resource array (never a shared instance).</summary>
        private static ResourceAmount[] One(ResourceType type, float amount) => new[] { R(type, amount) };

        /// <summary>New single-entry service array (never a shared instance).</summary>
        private static ServiceAmount[] One(ServiceType type, float amount) => new[] { S(type, amount) };

        // Tints (0xRRGGBBAA) hinting the dominant building material.
        private static uint TintTimber => MathUtil.Hex(0xA0784Cu);
        private static uint TintThatch => MathUtil.Hex(0xC8A860u);
        private static uint TintFieldStone => MathUtil.Hex(0x9A948Au);
        private static uint TintDressedStone => MathUtil.Hex(0xC9C0AEu);
        private static uint TintSoot => MathUtil.Hex(0x5E5650u);
        private static uint TintBronze => MathUtil.Hex(0xB08D57u);
        private static uint TintIron => MathUtil.Hex(0x7C7F86u);
        private static uint TintGilded => MathUtil.Hex(0xD4AF37u);
        private static uint TintEarth => MathUtil.Hex(0x7A6A48u);
        private static uint TintMeadow => MathUtil.Hex(0x8DB062u);
    }
}
