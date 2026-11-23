using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.AI.Strategy
{
    /// <summary>Database/world builders shared by the AI strategy tests.</summary>
    internal static class StrategyTestSupport
    {
        public const string Lore = "test.lore";
        public const string Tools = "test.tools";
        public const string Spears = "test.spears";
        public const string Rites = "test.rites";
        public const string Farming = "test.farming";
        public const string Barter = "test.barter";
        public const string BronzeWork = "test.bronze";

        /// <summary>
        /// Test database: a handful of techs, a few wonders and race personalities
        /// (Human neutral, Orc aggressive/expansionist, Elf curious/peaceful, Saurian pious), stubs elsewhere.
        /// </summary>
        public static GameDatabase CreateDb(bool extraTwins = false)
        {
            var db = new GameDatabase();
            db.Register(new TechDefinition { Key = Lore, Name = "Lore", Age = AgeId.Dawn, Cost = 50f, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.2f) } });
            db.Register(new TechDefinition { Key = Tools, Name = "Tools", Age = AgeId.Dawn, Cost = 60f, Effects = new[] { TechEffect.Of(TechEffectKind.AllProductionMultiplier, 0.1f) } });
            db.Register(new TechDefinition { Key = Spears, Name = "Spears", Age = AgeId.Dawn, Cost = 60f, PrerequisiteKeys = new[] { Tools }, Effects = new[] { TechEffect.Of(TechEffectKind.MilitaryMultiplier, 0.3f) } });
            db.Register(new TechDefinition { Key = Rites, Name = "Rites", Age = AgeId.Dawn, Cost = 50f, Effects = new[] { TechEffect.Of(TechEffectKind.FaithMultiplier, 0.2f) } });
            db.Register(new TechDefinition { Key = Farming, Name = "Farming", Age = AgeId.Dawn, Cost = 70f, Effects = new[] { TechEffect.Produce(ResourceType.Food, 0.25f), TechEffect.Unlock(BuildingType.Farm) } });
            db.Register(new TechDefinition { Key = Barter, Name = "Barter", Age = AgeId.Dawn, Cost = 50f, Effects = new[] { TechEffect.Of(TechEffectKind.TradeMultiplier, 0.2f) } });
            db.Register(new TechDefinition { Key = BronzeWork, Name = "Bronze Working", Age = AgeId.Bronze, Cost = 120f, PrerequisiteKeys = new[] { Tools }, Effects = new[] { TechEffect.Of(TechEffectKind.AllProductionMultiplier, 0.1f) } });
            if (extraTwins)
            {
                db.Register(new TechDefinition { Key = "test.twinA", Name = "Twin A", Age = AgeId.Dawn, Cost = 80f, Effects = new[] { TechEffect.Of(TechEffectKind.StorageMultiplier, 0.1f) } });
                db.Register(new TechDefinition { Key = "test.twinB", Name = "Twin B", Age = AgeId.Dawn, Cost = 80f, Effects = new[] { TechEffect.Of(TechEffectKind.StorageMultiplier, 0.1f) } });
            }

            db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, MinAge = AgeId.Bronze });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.StoneQuarry, Name = "Stone Quarry", Category = BuildingCategory.Resource, Job = JobType.Quarrier, JobSlots = 2,
                OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Stone, 0.1f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, IsWonder = true,
                UniquePerCivilization = true, MinAge = AgeId.Dawn, Width = 2, Height = 2, BuildWork = 50f,
                Cost = new[] { new ResourceAmount(ResourceType.Stone, 100f) },
                Services = new[] { new ServiceAmount(ServiceType.Research, 5f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.GrandBazaar, Name = "Grand Bazaar", Category = BuildingCategory.Wonder, IsWonder = true,
                UniquePerCivilization = true, MinAge = AgeId.Dawn, Width = 2, Height = 2, BuildWork = 50f,
                Cost = new[] { new ResourceAmount(ResourceType.Stone, 100f) },
                Services = new[] { new ServiceAmount(ServiceType.Commerce, 5f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.WarIdol, Name = "War Idol", Category = BuildingCategory.Wonder, IsWonder = true,
                UniquePerCivilization = true, MinAge = AgeId.Bronze, AllowedRaces = RaceMask.Orc, BuildWork = 40f,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 80f) },
                Services = new[] { new ServiceAmount(ServiceType.Defense, 4f), new ServiceAmount(ServiceType.Faith, 2f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Colossus, Name = "Colossus", Category = BuildingCategory.Wonder, IsWonder = true,
                UniquePerCivilization = true, MinAge = AgeId.Dawn, Placement = PlacementRule.Coastal, BuildWork = 40f,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 10f) },
                Services = new[] { new ServiceAmount(ServiceType.Commerce, 50f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.AscensionGate, Name = "Ascension Gate", Category = BuildingCategory.Wonder, IsWonder = true,
                UniquePerCivilization = true, MinAge = AgeId.Ascension, BuildWork = 500f,
                Cost = new[] { new ResourceAmount(ResourceType.Stone, 100f) },
            });

            TestDb.FillMissing(db);

            var orc = db.Race(RaceId.Orc);
            orc.Aggression = 0.9f; orc.Expansionism = 0.9f; orc.Curiosity = 0.3f; orc.Piety = 0.4f;
            orc.UniqueBuildings = new[] { BuildingType.WarIdol };
            var elf = db.Race(RaceId.Elf);
            elf.Aggression = 0.1f; elf.Curiosity = 0.9f; elf.Piety = 0.6f; elf.Expansionism = 0.3f; elf.Diplomacy = 0.5f;
            var saurian = db.Race(RaceId.Saurian);
            saurian.Piety = 0.95f; saurian.Curiosity = 0.2f; saurian.Mercantilism = 0.2f; saurian.Aggression = 0.3f; saurian.Expansionism = 0.3f;

            db.Freeze();
            return db;
        }

        public const string Fire = "p.fire";
        public const string IronWorking = "p.iron";
        public const string Smelting = "p.smelting";
        public const string Sailing = "p.sailing";
        public const string PlanFarming = "p.farming";
        public const string ElfLore = "p.elflore";
        public const string ElfGate = "p.elfgate";
        public const string BronzeAge = "p.bronze";

        /// <summary>
        /// Planning database: the Bronze age needs <paramref name="required"/> Dawn techs and 100 research.
        /// Fire (40) -> Iron Working (80); Smelting (60, ore boost); Sailing (60, unlocks a coastal Harbor);
        /// Farming (70); Elf Lore (elves only) -> Elf Gate (20, a dead end for everyone else).
        /// </summary>
        public static GameDatabase CreatePlanDb(bool strongIron, int required = 2)
        {
            var db = new GameDatabase();
            db.Register(new TechDefinition { Key = Fire, Name = "Fire", Age = AgeId.Dawn, Cost = 40f, Effects = new[] { TechEffect.Of(TechEffectKind.AllProductionMultiplier, 0.05f) } });
            db.Register(new TechDefinition { Key = IronWorking, Name = "Iron Working", Age = AgeId.Dawn, Cost = 80f, PrerequisiteKeys = new[] { Fire }, Effects = new[] { TechEffect.Of(TechEffectKind.MilitaryMultiplier, strongIron ? 0.5f : 0.1f) } });
            db.Register(new TechDefinition { Key = Smelting, Name = "Smelting", Age = AgeId.Dawn, Cost = 60f, Effects = new[] { TechEffect.Produce(ResourceType.Ore, 0.2f) } });
            db.Register(new TechDefinition { Key = Sailing, Name = "Sailing", Age = AgeId.Dawn, Cost = 60f, Effects = new[] { TechEffect.Unlock(BuildingType.Harbor) } });
            db.Register(new TechDefinition { Key = PlanFarming, Name = "Farming", Age = AgeId.Dawn, Cost = 70f, Effects = new[] { TechEffect.Produce(ResourceType.Food, 0.25f) } });
            db.Register(new TechDefinition { Key = ElfLore, Name = "Elf Lore", Age = AgeId.Dawn, Cost = 10f, AllowedRaces = RaceMask.Elf, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.05f) } });
            db.Register(new TechDefinition { Key = ElfGate, Name = "Elf Gate", Age = AgeId.Dawn, Cost = 20f, PrerequisiteKeys = new[] { ElfLore }, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.3f) } });
            db.Register(new TechDefinition { Key = BronzeAge, Name = "Bronze Casting", Age = AgeId.Bronze, Cost = 100f, PrerequisiteKeys = new[] { IronWorking }, Effects = new[] { TechEffect.Of(TechEffectKind.AllProductionMultiplier, 0.1f) } });
            db.Register(new AgeDefinition { Id = AgeId.Bronze, Name = "Bronze", ResearchThreshold = 100f, RequiredTechsFromPreviousAge = required });
            db.Register(new BuildingDefinition { Type = BuildingType.Harbor, Name = "Harbor", Category = BuildingCategory.Commerce, MinAge = AgeId.Classical, Placement = PlacementRule.Coastal });
            TestDb.FillMissing(db); // later ages get thresholds 2000+, so they stay non-decreasing
            db.Freeze();
            return db;
        }

        /// <summary>Starts an active war (with contact and War stance) between two civs.</summary>
        public static War StartWar(SimContext ctx, Civilization attacker, Civilization defender)
        {
            var war = new War { Id = ctx.State.Ids.War(), AttackerId = attacker.Id, DefenderId = defender.Id, StartTick = ctx.Tick, IsActive = true };
            ctx.State.Diplomacy.Wars.Add(war);
            var rel = ctx.State.Diplomacy.Get(attacker.Id, defender.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.War;
            rel.WarId = war.Id;
            return war;
        }

        /// <summary>A healthy, well-fed, happy parent settlement (large stockpile capacity) on a 128x128 grassland map.</summary>
        public static SimContext CreateExpansionWorld(GameDatabase db, RaceId race, int adults, out Civilization civ, out Settlement parent, ulong seed = 7, Biome fill = Biome.Grassland)
        {
            var ctx = TestWorlds.CreateContext(db, 128, seed, fill);
            parent = TestWorlds.AddSettlement(ctx, race, new Vec2i(64, 64), adults, out civ);
            MakeHealthy(parent);
            return ctx;
        }

        /// <summary>Sets happiness/food so the settlement qualifies as an expansion parent.</summary>
        public static void MakeHealthy(Settlement s)
        {
            s.Happiness = 0.7f;
            s.Stockpile.Capacity = 2000f;
            s.Stockpile.SetRaw(ResourceType.Food, 400f);
            s.Stockpile.SetRaw(ResourceType.Wood, 200f);
            s.Stockpile.SetRaw(ResourceType.Stone, 100f);
            s.Memory["econ.foodDays"] = 60f;
        }

        /// <summary>Alive settlements of the civ, in id order of the civ's list.</summary>
        public static List<Settlement> SettlementsOf(SimContext ctx, Civilization civ)
        {
            var list = new List<Settlement>();
            foreach (int id in civ.SettlementIds)
            {
                var s = ctx.State.Settlements.Get(id);
                if (s != null && s.IsAlive) list.Add(s);
            }
            return list;
        }

        /// <summary>Runs a system exactly once at the next tick that is a multiple of its interval.</summary>
        public static void RunOnce(ISimSystem system, SimContext ctx)
        {
            long next = (ctx.State.Tick / system.TickInterval + 1) * system.TickInterval;
            TestWorlds.Run(system, ctx, (int)(next - ctx.State.Tick), initialize: false);
        }
    }
}
