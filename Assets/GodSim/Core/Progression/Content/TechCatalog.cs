using System;
using System.Collections.Generic;

namespace GodSim.Core.Progression
{
    /// <summary>
    /// The full technology tree: 94 techs over ten ages (8-10 per age), including two racial techs per race
    /// and the <see cref="CapstoneKey"/> endgame tech. Keys are "age.name" in lowercase. Prerequisites form a
    /// DAG within the same or earlier ages: each age after Dawn opens with three root techs that depend only on
    /// previous-age techs, and every other tech has one or two prerequisites. General techs never depend on
    /// racial techs, so every race can walk the whole general tree.
    /// <para>
    /// Costs: each age's total cost is 40-80% (about half) of its research window, the gap between its threshold
    /// and the next one; the final age uses the last real gap, Information to Ascension (see
    /// <see cref="TechTreeReport.ResearchWindow"/>). Per-tech costs follow this per-age budget, not the design
    /// brief's literal per-tech ranges (Dawn 60-120 .. Ascension 4000-7000). Those ranges cannot fit under
    /// 0.8 x threshold gap: eight Dawn techs at 60 already cost 480, more than 0.8 x 400. The brief's whole scale is
    /// therefore shrunk by one factor (about 4.3x), which keeps its Dawn-to-Ascension cost ratio (about 60x).
    /// </para>
    /// <para>
    /// Early unlocks: an UnlockBuilding effect grants a building exactly one age before its MinAge. It may only
    /// target a building whose cost and inputs the civilization can already produce in the tech's age (see
    /// <see cref="TechTreeReport.FindUnsuppliedUnlocks"/>).
    /// </para>
    /// <para>
    /// Ids: <see cref="TechDefinition.Id"/> is the registration index, and saves store tech ids
    /// (Civilization.KnownTechs, CurrentResearchTechId). The order of <see cref="CreateDefinitions"/> is therefore
    /// part of the save format. Within a save version the list is append-only. Because it is also kept ordered by
    /// age, the only id-preserving change is appending Ascension techs at the end. Inserting, removing or
    /// reordering entries re-maps every saved tech, so it must ship with a save-version bump; a golden hash in
    /// TechCatalogTests pins the current order.
    /// </para>
    /// Pure data: the research system in Progression/Systems interprets the effects.
    /// </summary>
    public static class TechCatalog
    {
        /// <summary>
        /// Key of the final Ascension-age capstone technology. Nothing in the data gates on it: UnlockBuilding can only
        /// grant access, never withhold it, so the Ascension Gate becomes available through its MinAge alone. A system
        /// that wants the capstone to gate the endgame checks
        /// <c>civ.KnownTechs.Contains(db.TechIdOf(TechCatalog.CapstoneKey))</c>.
        /// </summary>
        public const string CapstoneKey = "ascension.transcendence";

        /// <summary>
        /// Multiplier on every listed tech cost, by age (balance pass 2026-11-19). It matches the growth of each age's
        /// research window in <see cref="AgeCatalog"/>, so an age's techs still cost 40-80% of its window. The listed
        /// costs below are the pre-pass values.
        /// </summary>
        public static readonly float[] CostScale = { 2f, 3f, 5f, 8f, 12f, 16f, 20f, 24f, 28f, 28f };

        private static readonly string[] None = new string[0];
        private static int[] _generalCounts;

        /// <summary>Registers every tech definition (fresh instances) into the database, in a deterministic order.</summary>
        /// <param name="db">A database that has not been frozen yet and has no techs with the same keys.</param>
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            var defs = CreateDefinitions();
            for (int i = 0; i < defs.Count; i++) db.Register(defs[i]);
        }

        /// <summary>Number of techs of an age that every race may research (AllowedRaces == All).</summary>
        public static int GeneralTechCount(AgeId age)
        {
            if (_generalCounts == null)
            {
                var counts = new int[Counts.Ages];
                var defs = CreateDefinitions();
                for (int i = 0; i < defs.Count; i++)
                    if (defs[i].AllowedRaces == RaceMask.All) counts[(int)defs[i].Age]++;
                _generalCounts = counts;
            }
            int a = (int)age;
            return a >= 0 && a < _generalCounts.Length ? _generalCounts[a] : 0;
        }

        /// <summary>The lowercase key prefix used for techs of an age, e.g. "bronze".</summary>
        public static string AgePrefix(AgeId age) => age.ToString().ToLowerInvariant();

        /// <summary>
        /// Builds new definitions for the whole tree, ordered by age (prerequisites always appear before
        /// dependants). Each call returns new objects, so several databases never share mutable definitions.
        /// </summary>
        public static List<TechDefinition> CreateDefinitions()
        {
            var l = new List<TechDefinition>(96);
            AddDawn(l);
            AddBronze(l);
            AddIron(l);
            AddClassical(l);
            AddMedieval(l);
            AddRenaissance(l);
            AddIndustrial(l);
            AddModern(l);
            AddInformation(l);
            AddAscension(l);
            return l;
        }

        // ================================================================================== DAWN (window 400)
        private static void AddDawn(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Dawn;
            Add(l, a, "dawn.fire", "Mastery of Fire", 16f, None,
                "Taming flame brings warmth, cooked food and the first gatherings around the hearth.",
                Of(TechEffectKind.HealthBonus, 0.03f), Of(TechEffectKind.HappinessBonus, 0.02f));
            Add(l, a, "dawn.stone_tools", "Stone Tools", 16f, None,
                "Knapped flint blades and hand axes make felling trees and shaping stone far easier.",
                Prod(ResourceType.Wood, 0.1f), Prod(ResourceType.Stone, 0.1f), Of(TechEffectKind.BuildSpeedMultiplier, 0.1f));
            Add(l, a, "dawn.foraging", "Foraging", 14f, None,
                "Knowing which roots, berries and nuts are safe to eat keeps the band fed through lean seasons.",
                Prod(ResourceType.Food, 0.12f));
            Add(l, a, "dawn.hunting", "Organized Hunting", 20f, Req("dawn.stone_tools"),
                "Spears and coordinated drives bring down large game and train the first warriors.",
                Prod(ResourceType.Food, 0.1f), Of(TechEffectKind.MilitaryMultiplier, 0.1f));
            Add(l, a, "dawn.animism", "Animism", 20f, Req("dawn.fire"),
                "Offerings to the spirits of river, beast and stone give the people their first sense of the divine.",
                Of(TechEffectKind.FaithMultiplier, 0.15f), Svc(ServiceType.Faith, 0.1f));
            Add(l, a, "dawn.agriculture", "Agriculture", 30f, Req("dawn.foraging", "dawn.stone_tools"),
                "Planting the seeds of wild grasses lets a band settle beside its fields and build the first farms.",
                Prod(ResourceType.Food, 0.15f), Unlock(BuildingType.Farm));
            Add(l, a, "dawn.oral_tradition", "Oral Tradition", 24f, Req("dawn.animism"),
                "Elders pass down songs, stories and hard-won knowledge from one generation to the next.",
                Of(TechEffectKind.ResearchMultiplier, 0.12f), Of(TechEffectKind.HappinessBonus, 0.02f));
            Add(l, a, "dawn.herbal_lore", "Herbal Lore", 22f, Req("dawn.foraging"),
                "Poultices and bitter teas from wild plants soothe wounds and fevers.",
                Of(TechEffectKind.HealthBonus, 0.04f), Unlock(BuildingType.Herbalist));
            // Unlocks the Mine, not the Deep Forge: the forge smelts Ore, and nothing produces Ore in the Dawn age.
            Racial(l, RaceId.Dwarf, a, "dawn.deep_delving", "Deep Delving", 24f, Req("dawn.stone_tools"),
                "Dwarven clans follow veins of stone and ore deep beneath the mountains and sink the first true mine shafts.",
                Prod(ResourceType.Stone, 0.2f), Prod(ResourceType.Ore, 0.2f), Unlock(BuildingType.Mine));
            Racial(l, RaceId.Orc, a, "dawn.warbands", "Warbands", 24f, Req("dawn.hunting"),
                "Orc hunters band together under the strongest fighter and raise a crude war idol to their victories.",
                Of(TechEffectKind.MilitaryMultiplier, 0.2f), Of(TechEffectKind.MoveSpeedMultiplier, 0.1f), Unlock(BuildingType.WarIdol));
        }

        // ================================================================================== BRONZE (window 1000)
        private static void AddBronze(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Bronze;
            Add(l, a, "bronze.pottery", "Pottery", 45f, Req("dawn.agriculture"),
                "Fired clay jars keep grain dry and safe from vermin through the winter.",
                Of(TechEffectKind.StorageMultiplier, 0.2f));
            Add(l, a, "bronze.copper_smelting", "Copper Smelting", 50f, Req("dawn.stone_tools"),
                "Charcoal kilns hot enough to melt copper from green ore open the age of metal.",
                Prod(ResourceType.Ore, 0.15f), Prod(ResourceType.Metal, 0.15f));
            Add(l, a, "bronze.animal_husbandry", "Animal Husbandry", 50f, Req("dawn.hunting", "dawn.agriculture"),
                "Penning sheep, goats and cattle provides milk, meat and wool without the risk of the hunt.",
                Prod(ResourceType.Food, 0.1f), Prod(ResourceType.Cloth, 0.15f));
            Add(l, a, "bronze.casting", "Bronze Casting", 60f, Req("bronze.copper_smelting"),
                "Alloying copper with tin and pouring it into molds yields hard, sharp tools.",
                Prod(ResourceType.Metal, 0.2f), Prod(ResourceType.Tools, 0.15f));
            Add(l, a, "bronze.weaving", "Weaving", 50f, Req("bronze.animal_husbandry"),
                "Looms turn wool and flax into warm cloth for clothing, sails and trade.",
                Prod(ResourceType.Cloth, 0.2f), Of(TechEffectKind.HappinessBonus, 0.02f));
            Add(l, a, "bronze.writing", "Writing", 65f, Req("bronze.pottery", "dawn.oral_tradition"),
                "Marks pressed into clay tablets record harvests, debts and the words of the wise.",
                Of(TechEffectKind.ResearchMultiplier, 0.15f));
            Add(l, a, "bronze.iron_working", "Iron Working", 75f, Req("bronze.casting"),
                "Hot bloomery fires wring iron from bog ore, and the first blacksmiths learn to hammer it into shape.",
                Prod(ResourceType.Metal, 0.1f), Of(TechEffectKind.MilitaryMultiplier, 0.1f), Unlock(BuildingType.Blacksmith));
            // No early Temple: it costs Tools, and the first Tools producer (Blacksmith) is not an ancestor of this tech.
            Add(l, a, "bronze.priesthood", "Priesthood", 60f, Req("bronze.writing", "dawn.animism"),
                "A caste of priests keeps the rites, the calendar and the offerings of the village shrines.",
                Of(TechEffectKind.FaithMultiplier, 0.2f), Svc(ServiceType.Faith, 0.15f));
            Racial(l, RaceId.Elf, a, "bronze.sylvan_lore", "Sylvan Lore", 60f, Req("bronze.writing"),
                "Elven loremasters learn the songs of the old forest and coax the first World Tree from a sacred seed.",
                Prod(ResourceType.Wood, 0.25f), Of(TechEffectKind.HealthBonus, 0.05f), Unlock(BuildingType.WorldTree));
            Racial(l, RaceId.Saurian, a, "bronze.star_charts", "Star Charts", 60f, Req("bronze.writing"),
                "Saurian sky-priests map the wandering stars and align a great stepped pyramid to the sun.",
                Of(TechEffectKind.ResearchMultiplier, 0.15f), Of(TechEffectKind.FaithMultiplier, 0.15f), Unlock(BuildingType.SunPyramid));
        }

        // ================================================================================== IRON (window 1800)
        private static void AddIron(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Iron;
            Add(l, a, "iron.bloomery", "Iron Smelting", 90f, Req("bronze.iron_working"),
                "Larger bloomeries and bellows make iron plentiful enough for every household.",
                Prod(ResourceType.Metal, 0.2f), Prod(ResourceType.Ore, 0.1f));
            Add(l, a, "iron.masonry", "Masonry", 85f, Req("bronze.casting"),
                "Iron chisels square fieldstone into blocks that stack into lasting walls.",
                Prod(ResourceType.Stone, 0.2f), Of(TechEffectKind.BuildSpeedMultiplier, 0.1f));
            Add(l, a, "iron.organized_religion", "Organized Religion", 90f, Req("bronze.priesthood"),
                "Shared scripture and a hierarchy of clergy bind many villages into one faith.",
                Of(TechEffectKind.FaithMultiplier, 0.2f), Of(TechEffectKind.HappinessBonus, 0.03f));
            Add(l, a, "iron.fortification", "Fortification", 100f, Req("iron.masonry"),
                "Ditches, ramparts and stone gatehouses make settlements hard to take by force.",
                Svc(ServiceType.Defense, 0.2f), Of(TechEffectKind.MilitaryMultiplier, 0.05f));
            Add(l, a, "iron.coinage", "Coinage", 100f, Req("iron.bloomery", "bronze.writing"),
                "Stamped coins of standard weight replace barter and make trade far simpler.",
                Of(TechEffectKind.TradeMultiplier, 0.2f), Prod(ResourceType.Gold, 0.15f));
            Add(l, a, "iron.philosophy", "Philosophy", 110f, Req("iron.organized_religion"),
                "Thinkers question the nature of the world and gather scrolls into the first libraries.",
                Of(TechEffectKind.ResearchMultiplier, 0.15f), Unlock(BuildingType.Library));
            Add(l, a, "iron.iron_plows", "Iron Plows", 95f, Req("iron.bloomery"),
                "Iron-shod plows cut heavy soils that wooden ards could never turn.",
                Prod(ResourceType.Food, 0.2f));
            Add(l, a, "iron.engineering", "Engineering", 110f, Req("iron.masonry"),
                "Arches, levers and surveying let builders span rivers and carry water across valleys.",
                Of(TechEffectKind.BuildSpeedMultiplier, 0.15f), Unlock(BuildingType.Aqueduct));
            Racial(l, RaceId.Orc, a, "iron.blood_rites", "Blood Rites", 100f, Req("iron.organized_religion"),
                "Orc shamans bind warriors with oaths sealed in blood, and the clans grow fierce and numerous.",
                Of(TechEffectKind.MilitaryMultiplier, 0.2f), Of(TechEffectKind.GrowthMultiplier, 0.1f));
            Racial(l, RaceId.Saurian, a, "iron.clutch_rites", "Clutch Rites", 100f, Req("iron.organized_religion"),
                "Saurian priests bless the warm nesting grounds, and more clutches hatch with every season.",
                Of(TechEffectKind.GrowthMultiplier, 0.2f), Of(TechEffectKind.FaithMultiplier, 0.1f));
        }

        // ================================================================================== CLASSICAL (window 3000)
        private static void AddClassical(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Classical;
            Add(l, a, "classical.mathematics", "Mathematics", 140f, Req("iron.philosophy"),
                "Geometry and arithmetic give scholars and builders a precise language of number and form.",
                Of(TechEffectKind.ResearchMultiplier, 0.15f), Of(TechEffectKind.BuildSpeedMultiplier, 0.05f));
            Add(l, a, "classical.civil_service", "Civil Service", 150f, Req("iron.coinage"),
                "Salaried officials collect taxes, keep records and administer distant towns.",
                Svc(ServiceType.Administration, 0.25f), Of(TechEffectKind.StorageMultiplier, 0.1f));
            Add(l, a, "classical.hydraulics", "Hydraulics", 140f, Req("iron.engineering"),
                "Siphons, cisterns and channels bring clean water to fountains, baths and fields.",
                Svc(ServiceType.Water, 0.25f), Of(TechEffectKind.HealthBonus, 0.04f));
            Add(l, a, "classical.drama", "Drama", 150f, Req("classical.civil_service"),
                "Tragedies and comedies staged in grand theaters entertain and unite the citizenry.",
                Svc(ServiceType.Entertainment, 0.25f), Of(TechEffectKind.HappinessBonus, 0.04f));
            Add(l, a, "classical.astronomy", "Astronomy", 170f, Req("classical.mathematics"),
                "Careful observation of the heavens yields calendars, predictions and new questions.",
                Of(TechEffectKind.ResearchMultiplier, 0.15f), Svc(ServiceType.Research, 0.1f));
            Add(l, a, "classical.seafaring", "Seafaring", 170f, Req("classical.astronomy"),
                "Navigating by the stars, galleys carry fishermen and merchants beyond sight of land.",
                Prod(ResourceType.Food, 0.1f), Of(TechEffectKind.TradeMultiplier, 0.15f), Of(TechEffectKind.MoveSpeedMultiplier, 0.1f));
            Add(l, a, "classical.medicine", "Medicine", 165f, Req("classical.hydraulics"),
                "Physicians catalogue diseases and remedies and teach the virtue of clean living.",
                Svc(ServiceType.Health, 0.2f), Of(TechEffectKind.LifespanBonus, 2f));
            Add(l, a, "classical.monasticism", "Monasticism", 175f, Req("classical.mathematics", "iron.organized_religion"),
                "Devout communities withdraw to pray, copy manuscripts and found the first monasteries.",
                Of(TechEffectKind.FaithMultiplier, 0.15f), Svc(ServiceType.Research, 0.05f), Unlock(BuildingType.Monastery));
            Racial(l, RaceId.Human, a, "classical.chivalric_orders", "Chivalric Orders", 160f, Req("classical.civil_service"),
                "Human knights swear oaths of service and piety, and their orders endow a grand cathedral.",
                Of(TechEffectKind.MilitaryMultiplier, 0.15f), Of(TechEffectKind.HappinessBonus, 0.03f), Unlock(BuildingType.GrandCathedral));
            Racial(l, RaceId.Elf, a, "classical.grove_wardens", "Grove Wardens", 160f, Req("classical.medicine"),
                "Elven wardens tend the sacred groves, where healing waters and old magic restore body and spirit.",
                Of(TechEffectKind.HealthBonus, 0.05f), Prod(ResourceType.Wood, 0.15f), Of(TechEffectKind.FaithMultiplier, 0.1f));
        }

        // ================================================================================== MEDIEVAL (window 4300)
        private static void AddMedieval(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Medieval;
            Add(l, a, "medieval.feudalism", "Feudalism", 200f, Req("classical.civil_service"),
                "Land is granted in return for service, binding lords, knights and peasants in a web of duty.",
                Of(TechEffectKind.MilitaryMultiplier, 0.15f), Prod(ResourceType.Food, 0.1f));
            Add(l, a, "medieval.crop_rotation", "Crop Rotation", 200f, Req("classical.hydraulics"),
                "Rotating grain, legumes and fallow across three fields keeps the soil rich year after year.",
                Prod(ResourceType.Food, 0.25f), Of(TechEffectKind.GrowthMultiplier, 0.05f));
            Add(l, a, "medieval.scholasticism", "Scholasticism", 220f, Req("classical.monasticism"),
                "Cathedral schools reconcile faith and reason and grow into the first universities.",
                Of(TechEffectKind.ResearchMultiplier, 0.15f), Unlock(BuildingType.University));
            Add(l, a, "medieval.castle_building", "Castle Building", 230f, Req("medieval.feudalism"),
                "Concentric walls, keeps and towers turn every lordly seat into a fortress.",
                Svc(ServiceType.Defense, 0.25f), Of(TechEffectKind.BuildSpeedMultiplier, 0.05f));
            Add(l, a, "medieval.gothic_architecture", "Gothic Architecture", 240f, Req("medieval.castle_building"),
                "Pointed arches and flying buttresses lift cathedral vaults toward the heavens.",
                Of(TechEffectKind.FaithMultiplier, 0.15f), Prod(ResourceType.Stone, 0.15f));
            Add(l, a, "medieval.windmills", "Windmills", 220f, Req("medieval.crop_rotation"),
                "Sails catch the wind to grind grain and saw timber without the toil of beasts.",
                Prod(ResourceType.Food, 0.15f), Prod(ResourceType.Goods, 0.1f), Of(TechEffectKind.StorageMultiplier, 0.1f));
            Add(l, a, "medieval.banking", "Banking", 250f, Req("medieval.feudalism", "medieval.scholasticism"),
                "Letters of credit and money-changers let wealth flow safely between distant cities.",
                Prod(ResourceType.Gold, 0.2f), Of(TechEffectKind.TradeMultiplier, 0.1f), Unlock(BuildingType.Bank));
            Add(l, a, "medieval.crossbows", "Crossbows", 230f, Req("medieval.castle_building"),
                "Mechanical bows let a lightly trained levy pierce the armor of a knight.",
                Of(TechEffectKind.MilitaryMultiplier, 0.2f), Prod(ResourceType.Weapons, 0.15f));
            Racial(l, RaceId.Dwarf, a, "medieval.runesmithing", "Runesmithing", 230f, Req("medieval.castle_building"),
                "Dwarven runesmiths etch binding runes into anvil, axe and gate, and their work never fails.",
                Prod(ResourceType.Tools, 0.25f), Prod(ResourceType.Weapons, 0.2f), Svc(ServiceType.Defense, 0.1f));
            Racial(l, RaceId.Human, a, "medieval.great_guilds", "Great Guilds", 230f, Req("medieval.windmills"),
                "Human craft and merchant guilds set standards, train apprentices and dominate the markets of every town.",
                Prod(ResourceType.Goods, 0.2f), Of(TechEffectKind.TradeMultiplier, 0.15f));
        }

        // ================================================================================== RENAISSANCE (window 6000)
        private static void AddRenaissance(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Renaissance;
            Add(l, a, "renaissance.printing_press", "Printing Press", 300f, Req("medieval.scholasticism"),
                "Movable type puts books in the hands of thousands and spreads ideas like wildfire.",
                Of(TechEffectKind.ResearchMultiplier, 0.2f));
            Add(l, a, "renaissance.gunpowder", "Gunpowder", 320f, Req("medieval.crossbows"),
                "Saltpeter, charcoal and sulfur power cannon and arquebus, ending the reign of the castle.",
                Of(TechEffectKind.MilitaryMultiplier, 0.25f), Prod(ResourceType.Weapons, 0.1f));
            Add(l, a, "renaissance.mercantilism", "Mercantilism", 310f, Req("medieval.banking"),
                "Chartered trading companies and double-entry ledgers make commerce a pillar of the state.",
                Of(TechEffectKind.TradeMultiplier, 0.2f), Prod(ResourceType.Gold, 0.1f));
            Add(l, a, "renaissance.optics", "Optics", 320f, Req("renaissance.printing_press"),
                "Ground lenses reveal the very small and the very far, from cells to the moons of other worlds.",
                Of(TechEffectKind.ResearchMultiplier, 0.15f));
            Add(l, a, "renaissance.navigation", "Navigation", 340f, Req("renaissance.optics", "renaissance.mercantilism"),
                "Compass, astrolabe and accurate charts open ocean routes to distant markets.",
                Of(TechEffectKind.TradeMultiplier, 0.15f), Of(TechEffectKind.MoveSpeedMultiplier, 0.1f));
            Add(l, a, "renaissance.anatomy", "Anatomy", 330f, Req("renaissance.printing_press"),
                "Careful dissection and illustrated texts reveal how the body truly works.",
                Svc(ServiceType.Health, 0.2f), Of(TechEffectKind.LifespanBonus, 3f));
            Add(l, a, "renaissance.humanism", "Humanism", 320f, Req("renaissance.printing_press"),
                "A renewed love of art, letters and human potential fills the cities with painters and poets.",
                Of(TechEffectKind.HappinessBonus, 0.05f), Svc(ServiceType.Entertainment, 0.15f));
            Add(l, a, "renaissance.domed_architecture", "Domed Architecture", 330f, Req("renaissance.humanism"),
                "Rediscovered proportion and herringbone brickwork crown cities with soaring domes.",
                Of(TechEffectKind.BuildSpeedMultiplier, 0.15f), Prod(ResourceType.Stone, 0.1f));
            // Unlocks the CoalMine, not the Factory: the Factory burns Fuel, and only the CoalMine (Industrial) makes it.
            Add(l, a, "renaissance.steam_power", "Steam Power", 360f, Req("renaissance.optics", "renaissance.gunpowder"),
                "Pressure vessels and pistons harness boiling water to pump flooded shafts dry, opening the first deep coal mines.",
                Prod(ResourceType.Fuel, 0.15f), Of(TechEffectKind.AllProductionMultiplier, 0.05f), Unlock(BuildingType.CoalMine));
        }

        // ================================================================================== INDUSTRIAL (window 8500)
        private static void AddIndustrial(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Industrial;
            Add(l, a, "industrial.coal_mining", "Coal Mining", 430f, Req("renaissance.steam_power"),
                "Steam pumps drain deep shafts, and coal fuels furnaces and engines across the land.",
                Prod(ResourceType.Fuel, 0.25f), Prod(ResourceType.Ore, 0.1f));
            Add(l, a, "industrial.textile_mills", "Textile Mills", 440f, Req("renaissance.mercantilism"),
                "Spinning jennies and power looms produce cloth by the mile.",
                Prod(ResourceType.Cloth, 0.25f), Prod(ResourceType.Goods, 0.15f));
            Add(l, a, "industrial.scientific_method", "Scientific Method", 450f, Req("renaissance.optics"),
                "Hypothesis, experiment and peer review turn natural philosophy into modern science.",
                Of(TechEffectKind.ResearchMultiplier, 0.2f));
            Add(l, a, "industrial.steel", "Steel", 470f, Req("industrial.coal_mining"),
                "Bessemer converters turn pig iron into strong, cheap steel for rails, bridges and machines.",
                Prod(ResourceType.Metal, 0.25f), Prod(ResourceType.Machinery, 0.15f));
            Add(l, a, "industrial.railways", "Railways", 500f, Req("industrial.steel"),
                "Locomotives on iron rails carry goods and passengers faster than any horse.",
                Of(TechEffectKind.TradeMultiplier, 0.2f), Of(TechEffectKind.MoveSpeedMultiplier, 0.15f));
            Add(l, a, "industrial.sanitation", "Sanitation", 460f, Req("industrial.scientific_method"),
                "Sewers, clean water mains and public health laws tame the diseases of crowded cities.",
                Of(TechEffectKind.HealthBonus, 0.06f), Svc(ServiceType.Water, 0.2f));
            Add(l, a, "industrial.electricity", "Electricity", 520f, Req("industrial.scientific_method", "industrial.steel"),
                "Dynamos and copper wire carry power to lamps and motors, heralding the first power plants.",
                Of(TechEffectKind.AllProductionMultiplier, 0.05f), Svc(ServiceType.Power, 0.2f), Unlock(BuildingType.PowerPlant));
            Add(l, a, "industrial.vaccination", "Vaccination", 480f, Req("industrial.sanitation"),
                "Inoculation against smallpox and other plagues lets far more children reach adulthood.",
                Of(TechEffectKind.LifespanBonus, 4f), Of(TechEffectKind.GrowthMultiplier, 0.1f));
            Add(l, a, "industrial.rifled_firearms", "Rifled Firearms", 470f, Req("industrial.steel"),
                "Mass-produced rifles with spiral-grooved barrels strike accurately at long range.",
                Of(TechEffectKind.MilitaryMultiplier, 0.3f));
        }

        // ================================================================================== MODERN (window 11000)
        private static void AddModern(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Modern;
            Add(l, a, "modern.combustion_engine", "Combustion Engine", 560f, Req("industrial.railways"),
                "Compact engines burning refined fuel power cars, trucks and tractors.",
                Prod(ResourceType.Fuel, 0.2f), Of(TechEffectKind.MoveSpeedMultiplier, 0.15f));
            Add(l, a, "modern.mass_production", "Mass Production", 580f, Req("industrial.electricity"),
                "Assembly lines and interchangeable parts multiply the output of every factory.",
                Of(TechEffectKind.AllProductionMultiplier, 0.1f), Of(TechEffectKind.BuildSpeedMultiplier, 0.1f));
            Add(l, a, "modern.antibiotics", "Antibiotics", 570f, Req("industrial.vaccination"),
                "Penicillin and its successors cure infections that were once a death sentence.",
                Svc(ServiceType.Health, 0.25f), Of(TechEffectKind.LifespanBonus, 4f));
            Add(l, a, "modern.petroleum_refining", "Petroleum Refining", 600f, Req("modern.combustion_engine"),
                "Cracking towers split crude oil into fuels, lubricants and plastics.",
                Prod(ResourceType.Fuel, 0.25f), Prod(ResourceType.Machinery, 0.1f));
            Add(l, a, "modern.mechanized_agriculture", "Mechanized Agriculture", 610f, Req("modern.mass_production", "modern.combustion_engine"),
                "Tractors, combine harvesters and fertilizer let a few farmers feed a whole city.",
                Prod(ResourceType.Food, 0.3f));
            Add(l, a, "modern.radio", "Radio", 600f, Req("modern.mass_production"),
                "Broadcasts carry news, music and voices into every home at once.",
                Of(TechEffectKind.HappinessBonus, 0.04f), Svc(ServiceType.Entertainment, 0.2f));
            Add(l, a, "modern.flight", "Powered Flight", 620f, Req("modern.combustion_engine"),
                "Aircraft shrink continents, carrying mail, cargo and passengers across oceans in hours.",
                Of(TechEffectKind.MoveSpeedMultiplier, 0.15f), Of(TechEffectKind.TradeMultiplier, 0.15f));
            // Unlocks the ElectronicsPlant, not the DataCenter: the DataCenter needs Electronics, which only the plant makes.
            Add(l, a, "modern.computing", "Computing", 680f, Req("modern.radio"),
                "Vacuum tubes and then transistors perform millions of calculations, and the first electronics plants rise to build them.",
                Of(TechEffectKind.ResearchMultiplier, 0.25f), Unlock(BuildingType.ElectronicsPlant));
            Add(l, a, "modern.combined_arms", "Combined Arms", 640f, Req("modern.flight"),
                "Tanks, artillery and aircraft fight as one coordinated force.",
                Of(TechEffectKind.MilitaryMultiplier, 0.3f), Svc(ServiceType.Defense, 0.15f));
        }

        // ================================================================================== INFORMATION (window 14000)
        private static void AddInformation(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Information;
            Add(l, a, "information.microprocessors", "Microprocessors", 720f, Req("modern.computing"),
                "An entire computer etched onto a sliver of silicon puts computation everywhere.",
                Prod(ResourceType.Electronics, 0.25f), Of(TechEffectKind.ResearchMultiplier, 0.1f));
            Add(l, a, "information.globalization", "Globalization", 730f, Req("modern.flight"),
                "Container ships and open markets weave every economy into one.",
                Of(TechEffectKind.TradeMultiplier, 0.25f), Prod(ResourceType.Gold, 0.1f));
            Add(l, a, "information.genetics", "Genetics", 740f, Req("modern.antibiotics"),
                "Reading the genome reveals the roots of inherited disease and how to cure it.",
                Of(TechEffectKind.HealthBonus, 0.05f), Of(TechEffectKind.LifespanBonus, 5f));
            Add(l, a, "information.internet", "The Internet", 800f, Req("information.microprocessors"),
                "A global network links every computer, putting all knowledge a moment away.",
                Of(TechEffectKind.ResearchMultiplier, 0.2f), Svc(ServiceType.Commerce, 0.15f));
            Add(l, a, "information.robotics", "Robotics", 790f, Req("information.microprocessors"),
                "Tireless robotic arms assemble, weld and build with superhuman precision.",
                Of(TechEffectKind.AllProductionMultiplier, 0.1f), Of(TechEffectKind.BuildSpeedMultiplier, 0.15f));
            Add(l, a, "information.renewable_energy", "Renewable Energy", 770f, Req("information.microprocessors"),
                "Smart grids of wind turbines and solar panels harvest clean power from sky and sun.",
                Svc(ServiceType.Power, 0.25f), Prod(ResourceType.Fuel, 0.1f));
            Add(l, a, "information.vertical_farming", "Vertical Farming", 790f, Req("information.genetics", "information.robotics"),
                "Engineered crops grown under artificial light in stacked towers point the way to hydroponic arcologies.",
                Prod(ResourceType.Food, 0.25f), Unlock(BuildingType.HydroponicsTower));
            Add(l, a, "information.fusion_power", "Fusion Power", 850f, Req("information.renewable_energy"),
                "Magnetic bottles finally contain a miniature star, promising nearly limitless energy.",
                Svc(ServiceType.Power, 0.2f), Unlock(BuildingType.FusionReactor));
            Add(l, a, "information.social_networks", "Social Networks", 760f, Req("information.internet"),
                "Everyone can speak to everyone, and governments learn to listen.",
                Of(TechEffectKind.HappinessBonus, 0.05f), Svc(ServiceType.Administration, 0.15f));
        }

        // ================================================================================== ASCENSION (window 14000, the last real gap)
        private static void AddAscension(List<TechDefinition> l)
        {
            const AgeId a = AgeId.Ascension;
            Add(l, a, "ascension.nanotechnology", "Nanotechnology", 1000f, Req("information.robotics"),
                "Swarms of molecular machines assemble matter atom by atom.",
                Of(TechEffectKind.AllProductionMultiplier, 0.15f), Prod(ResourceType.Machinery, 0.2f));
            Add(l, a, "ascension.synthetic_minds", "Synthetic Minds", 1050f, Req("information.internet"),
                "Artificial intellects join their makers as partners in discovery.",
                Of(TechEffectKind.ResearchMultiplier, 0.3f));
            Add(l, a, "ascension.longevity", "Longevity Therapies", 1000f, Req("information.genetics"),
                "Cellular repair halts aging, and lifespans stretch far beyond their natural span.",
                Of(TechEffectKind.LifespanBonus, 8f), Of(TechEffectKind.HealthBonus, 0.06f));
            Add(l, a, "ascension.post_scarcity", "Post-Scarcity Economy", 1150f, Req("ascension.nanotechnology", "information.vertical_farming"),
                "When anything can be made from sunlight and dust, want itself becomes obsolete.",
                Of(TechEffectKind.AllProductionMultiplier, 0.15f), Of(TechEffectKind.HappinessBonus, 0.06f));
            Add(l, a, "ascension.arcology_design", "Arcology Design", 1150f, Req("ascension.post_scarcity"),
                "Self-contained city-structures house, feed and govern millions in perfect balance.",
                Of(TechEffectKind.BuildSpeedMultiplier, 0.2f), Svc(ServiceType.Administration, 0.2f));
            Add(l, a, "ascension.aegis_fields", "Aegis Fields", 1100f, Req("ascension.synthetic_minds"),
                "Shimmering energy barriers shield entire cities from harm.",
                Svc(ServiceType.Defense, 0.3f), Of(TechEffectKind.MilitaryMultiplier, 0.2f));
            Add(l, a, "ascension.collective_consciousness", "Collective Consciousness", 1200f, Req("ascension.synthetic_minds", "ascension.longevity"),
                "Linked minds share thoughts and dreams, and the people feel the presence of their god as never before.",
                Of(TechEffectKind.FaithMultiplier, 0.25f), Of(TechEffectKind.HappinessBonus, 0.05f));
            Add(l, a, CapstoneKey, "Transcendence", 2400f, Req("ascension.collective_consciousness", "ascension.post_scarcity"),
                "The civilization sheds its mortal limits and prepares to transcend the physical world.",
                Of(TechEffectKind.AllProductionMultiplier, 0.1f), Of(TechEffectKind.ResearchMultiplier, 0.2f), Of(TechEffectKind.FaithMultiplier, 0.3f));
        }

        // ================================================================================== builders
        private static string[] Req(params string[] keys) => keys;
        private static TechEffect Prod(ResourceType r, float v) => TechEffect.Produce(r, v);
        private static TechEffect Svc(ServiceType s, float v) => TechEffect.ServiceBoost(s, v);
        private static TechEffect Of(TechEffectKind k, float v) => TechEffect.Of(k, v);
        private static TechEffect Unlock(BuildingType b) => TechEffect.Unlock(b);

        private static TechDefinition Add(List<TechDefinition> list, AgeId age, string key, string name, float cost,
            string[] prereqs, string description, params TechEffect[] effects)
        {
            // Programmer-error guard: the key prefix must name the tech's age ("bronze.casting" -> Bronze).
            string prefix = AgePrefix(age) + ".";
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Tech key '" + key + "' must start with '" + prefix + "'.");
            var def = new TechDefinition
            {
                Key = key,
                Name = name,
                Description = description,
                Age = age,
                Cost = cost * CostScale[(int)age],
                PrerequisiteKeys = (string[])prereqs.Clone(),
                Effects = (TechEffect[])effects.Clone(),
                AllowedRaces = RaceMask.All
            };
            list.Add(def);
            return def;
        }

        private static void Racial(List<TechDefinition> list, RaceId race, AgeId age, string key, string name, float cost,
            string[] prereqs, string description, params TechEffect[] effects)
        {
            Add(list, age, key, name, cost, prereqs, description, effects).AllowedRaces = race.ToMask();
        }
    }
}
