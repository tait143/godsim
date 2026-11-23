using System;
using System.Collections.Generic;
using GodSim.Core.AI;
using GodSim.Core.Buildings;
using GodSim.Core.Diplomacy;
using GodSim.Core.Divine;
using GodSim.Core.Economy;
using GodSim.Core.Heroes;
using GodSim.Core.History;
using GodSim.Core.Objectives;
using GodSim.Core.Population;
using GodSim.Core.Progression;
using GodSim.Core.Races;
using GodSim.Core.Religion;
using GodSim.Core.Security;
using GodSim.Core.Settlements;
using GodSim.Core.Storytelling;
using GodSim.Core.World;

namespace GodSim.Core.Composition
{
    /// <summary>
    /// The composition root: the only place that knows every module. Builds the frozen content database,
    /// generates and seeds a new world, and wires all systems in their canonical tick order.
    /// </summary>
    public static class DefaultComposition
    {
        /// <summary>Registers all content catalogs and freezes the database (read-only from here on).</summary>
        public static GameDatabase BuildDatabase()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);
            AgeCatalog.Register(db);
            TechCatalog.Register(db);
            BuildingCatalog.Register(db);
            GodPowerCatalog.Register(db);
            db.Freeze();
            return db;
        }

        /// <summary>
        /// Canonical system order: Divine -> Economy -> Population -> Progression -> AI -> Settlements -> Diplomacy -> meta.
        /// Order is part of determinism: changing it changes every seed's history.
        /// </summary>
        public static List<ISimSystem> CreateSystems()
        {
            return new List<ISimSystem>
            {
                // Divine & nature: weather first so yields/fire use this tick's conditions; player commands next.
                new SeasonWeatherSystem(),
                new GodCommandSystem(),
                new FaithSystem(),
                new DisasterSystem(),
                new FireSystem(),

                // Economy: capacity & services before production; stats roll last.
                new StorageSystem(),
                new ServiceSystem(),
                new ProductionSystem(),
                new UpkeepSystem(),
                new TradeSystem(),
                new ResourceRegrowthSystem(),
                new EconomyStatsSystem(),

                // Population
                new NeedsSystem(),
                new HousingSystem(),
                new JobAssignmentSystem(),
                new AgingSystem(),
                new BirthSystem(),
                new FamilySystem(),
                new WellbeingSystem(),
                new MigrationSystem(),
                new LeadershipSystem(),

                // Progression
                new ModifierSystem(),
                new ResearchSystem(),
                new AgeProgressionSystem(),

                // AI — civ minds & institutions (personality and god perception first: everything else reads them)
                new PersonalityDriftSystem(),
                new GodPerceptionSystem(),
                new GovernanceSystem(),
                new CivilizationStrategySystem(),
                new ResearchDirectorSystem(),
                new EnvoySystem(),
                new EspionageSystem(),
                new WarPlannerSystem(),

                // AI — Advisor Council: decay, then specialist votes, then learning, then the governor acts on them
                new AdvisorCouncilSystem(),
                new FoodSecurityAdvisor(),
                new GrowthAdvisor(),
                new SupplyChainAdvisor(),
                new LogisticsAdvisor(),
                new LaborAdvisor(),
                new SeasonalAdvisor(),
                new DefenseAdvisor(),
                new FaithAdvisor(),
                new ScienceAdvisor(),
                new TreasuryAdvisor(),
                new HealthAdvisor(),
                new HappinessAdvisor(),
                new InfrastructureAdvisor(),
                new AgeTransitionAdvisor(),
                new DisasterRecoveryAdvisor(),
                new UrbanDesignAdvisor(),
                new FrontierAdvisor(),
                new ProspectorAdvisor(),
                new TradeSpecializationAdvisor(),
                new CapitalAdvisor(),
                new HumanCultureAdvisor(),
                new ElfCultureAdvisor(),
                new DwarfCultureAdvisor(),
                new OrcCultureAdvisor(),
                new SaurianCultureAdvisor(),
                new AdvisorUrgencySampler(),
                new AdvisorLearningSystem(),
                new SettlementGovernorSystem(),
                new ExpansionSystem(),

                // Settlements
                new TownPlannerSystem(),
                new RoadNetworkSystem(),
                new ConstructionSystem(),
                new SettlementGrowthSystem(),
                new MaintenanceSystem(),

                // Diplomacy & war
                new ContactSystem(),
                new RelationsSystem(),
                new WarDecisionSystem(),
                new MilitarySystem(),
                new ArmySystem(),

                // Emergent depth: religions, heroes, world events (rebellions, golden ages, raids...)
                new ReligionSystem(),
                new HeroSystem(),
                new WorldEventSystem(),

                // Meta: history, objectives, integrity monitoring (last, observes the settled tick)
                new HistoryRecorder(),
                new ObjectiveSystem(),
                new IntegritySystem(),
            };
        }

        /// <summary>Generates terrain, places one civilization per starting race and seeds its first settlement.</summary>
        public static WorldState CreateWorld(SimConfig config, GameDatabase db, ISimLogger log = null)
        {
            Guard.NotNull(config, nameof(config));
            Guard.NotNull(db, nameof(db));
            log = log ?? NullLogger.Instance;

            var seedRng = new Rng(config.Seed);
            var map = WorldGenerator.Generate(WorldGenSettings.FromConfig(config), seedRng.Fork(0x57A71D));
            var state = new WorldState(config.Seed, map);
            state.God.Mana = config.StartingMana;
            state.God.MaxMana = Math.Max(state.God.MaxMana, config.StartingMana);

            // Bootstrap mutations publish into a throwaway bus: no subscriber exists before the engine is built.
            var ops = new WorldOps(state, db, new EventBus(log));
            var races = config.StartingRaces;
            var starts = StartLocationFinder.Find(map, db, races, seedRng.Fork(0x5747));
            for (int i = 0; i < races.Length && i < starts.Length; i++)
                SeedCivilization(state, db, ops, races[i], starts[i], config.InitialPopulationPerRace);

            ops.AddChronicle(ChronicleCategory.World, "The world was shaped, and " + races.Length + " peoples awoke beneath the heavens.", -1, -1, Vec2i.Invalid, 3);
            ops.FlushRemovals();
            log.Info("World created: seed " + config.Seed + ", " + map.Width + "x" + map.Height + ", " + state.Civilizations.Count + " civilizations, " + state.People.Count + " people.");
            return state;
        }

        /// <summary>Founds a civilization at a start tile with a first generation of couples, children and starter huts.</summary>
        public static Civilization SeedCivilization(WorldState state, GameDatabase db, WorldOps ops, RaceId race, Vec2i start, int population)
        {
            var def = db.Race(race);
            var rng = state.Rng;
            var civ = ops.CreateCivilization(race);
            var s = ops.FoundSettlement(civ, start);

            int adults = Math.Max(2, (int)Math.Round(population * 0.75));
            int children = Math.Max(0, population - adults);
            var people = new List<Person>(population);
            for (int i = 0; i < adults; i++)
            {
                float age = def.AdulthoodAge + rng.Range(0f, Math.Max(1f, (def.ElderAge - def.AdulthoodAge) * 0.45f));
                people.Add(ops.CreatePerson(s, race, i % 2 == 0 ? Sex.Female : Sex.Male, age));
            }
            // First couples
            for (int i = 0; i + 1 < people.Count; i += 2)
            {
                people[i].SpouseId = people[i + 1].Id;
                people[i + 1].SpouseId = people[i].Id;
            }
            for (int i = 0; i < children; i++)
            {
                var mother = people[(i * 2) % Math.Max(1, people.Count - 1)];
                var father = mother.SpouseId >= 0 ? state.People.Get(mother.SpouseId) : null;
                ops.CreatePerson(s, race, rng.Chance(0.5f) ? Sex.Female : Sex.Male, rng.Range(0f, def.AdulthoodAge * 0.8f),
                    mother.Sex == Sex.Female ? mother.Id : -1, father?.Id ?? -1);
            }

            // Starter huts: one dwelling per ~4 founders so the first families have shelter (the AI builds the rest).
            if (db.TryGetBuilding(BuildingType.House, out var house))
            {
                int huts = Math.Max(1, population / 4);
                for (int i = 0; i < huts; i++)
                {
                    var site = ops.FindFreeSite(new Vec2i(s.Center.X + (i % 3) * 2 - 2, s.Center.Y + (i / 3) * 2 + 2), house.Width, house.Height, 6);
                    if (site.IsValid) ops.CreateBuilding(s, BuildingType.House, site, (byte)(i & 3), true);
                }
            }
            civ.Devotion = MathUtil.Clamp01(0.4f + 0.4f * def.Piety);
            return civ;
        }
    }
}
