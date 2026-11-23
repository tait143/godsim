using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Persistence.Format
{
    /// <summary>
    /// Hand-built, non-trivial world that sets a non-default value on every persistent field
    /// (verified by SaveFormatTests.Fixture_SetsEveryPersistentFieldToANonDefaultValue), so a round trip
    /// that drops any field is detected by <see cref="DeepComparer"/>.
    /// </summary>
    public static class SaveTestWorld
    {
        public const ulong Seed = 0xC0FFEE1234UL;

        public static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, Job = JobType.Farmer, JobSlots = 4, Width = 2, Height = 2, BuildWork = 20f });
            db.Register(new BuildingDefinition { Type = BuildingType.Barracks, Name = "Barracks", Category = BuildingCategory.Military, Job = JobType.Soldier, JobSlots = 6, Width = 2, Height = 1, BuildWork = 30f });
            db.Register(new TechDefinition { Key = "test.fire", Name = "Fire", Age = AgeId.Dawn, Cost = 10f });
            db.Register(new TechDefinition { Key = "test.wheel", Name = "Wheel", Age = AgeId.Dawn, Cost = 20f, PrerequisiteKeys = new[] { "test.fire" } });
            db.Register(new TechDefinition { Key = "test.bronze", Name = "Bronze", Age = AgeId.Bronze, Cost = 40f, PrerequisiteKeys = new[] { "test.wheel" } });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        /// <summary>Builds the fixture through WorldOps (so links are consistent) and then sets every remaining field.</summary>
        public static SimContext BuildContext(GameDatabase db)
        {
            var ctx = TestWorlds.CreateContext(db, 64, Seed);
            var s = ctx.State;
            var ops = ctx.Ops;

            // ---------------------------------------------------------------- civilizations & settlements
            s.Tick = 120;
            var s1 = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(16, 16), 12, out var civ1);
            civ1.Age = AgeId.Bronze; // buildings placed from now on get StyleAge = Bronze
            civ1.AgeEnteredTick = 130;
            civ1.Color = 0x3366CCFFu;
            s.Tick = 150;
            var s2 = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(46, 46), 10, out var civ2);
            civ2.Color = 0xAA2211FFu;
            s.Tick = 200;
            var s3 = ops.CreateSettlement(civ1, new Vec2i(16, 46), "Northwatch", s1.Id, 5);
            for (int i = 0; i < 3; i++) ops.CreatePerson(s3, RaceId.Human, (Sex)(i % 2), 25f + i);
            var civ3 = ops.CreateCivilization(RaceId.Elf, "Fallen Grove");
            ops.CollapseCivilization(civ3, civ2.Id);
            ops.ClaimTerritory(s1, 9);

            // ---------------------------------------------------------------- buildings, homes, jobs
            s.Tick = 210;
            var houses1 = PlaceHouses(ops, s1, 3, new Vec2i(20, 20));
            var houses2 = PlaceHouses(ops, s2, 2, new Vec2i(50, 50));
            var houses3 = PlaceHouses(ops, s3, 1, new Vec2i(19, 49));
            HouseEveryone(ops, s, s1, houses1);
            HouseEveryone(ops, s, s2, houses2);
            HouseEveryone(ops, s, s3, houses3);
            houses1[0].Level = 2;
            houses1[1].Condition = 0.8f;

            var farm = ops.CreateBuilding(s1, BuildingType.Farm, ops.FindFreeSite(new Vec2i(10, 16), 2, 2, 10), 1, true);
            farm.IsOnFire = true;
            farm.Efficiency = 0.65f;
            for (int i = 0; i < 3; i++) ops.AssignJob(s.People.Get(s1.ResidentIds[i]), farm, JobType.Farmer);
            var builder = s.People.Get(s1.ResidentIds[3]);
            ops.AssignJob(builder, null, JobType.Builder);

            var temple = ops.CreateBuilding(s1, BuildingType.Temple, ops.FindFreeSite(new Vec2i(12, 22), 1, 1, 10), 2, false);
            temple.ConstructionProgress = 0.35f;
            temple.CostPaid = true;
            temple.Condition = 0.9f;

            var barracks = ops.CreateBuilding(s2, BuildingType.Barracks, ops.FindFreeSite(new Vec2i(42, 46), 2, 1, 10), 3, true);
            var soldiers = new List<Person>();
            for (int i = 0; i < 4; i++)
            {
                var p = s.People.Get(s2.ResidentIds[i]);
                ops.AssignJob(p, barracks, JobType.Soldier);
                soldiers.Add(p);
            }

            // ---------------------------------------------------------------- families & individual people
            var mother = s.People.Get(s1.ResidentIds[4]);  // female (even index)
            var father = s.People.Get(s1.ResidentIds[5]);  // male
            mother.SpouseId = father.Id;
            father.SpouseId = mother.Id;
            s.Tick = 220;
            var child = ops.CreatePerson(s1, RaceId.Human, Sex.Male, 4f, mother.Id, father.Id, true);
            child.Health = 0.8f;
            child.Satiation = 0.7f;
            var elder = s.People.Get(s1.ResidentIds[6]);
            elder.AgeYears = 70.5f;
            elder.Stage = LifeStage.Elder;
            elder.Happiness = 0.55f;
            elder.Traits = PersonTraits.Strong | PersonTraits.Wise | PersonTraits.Hero;
            s.People.Get(s1.ResidentIds[0]).Experience = 0.3f;
            var ghost = new Person
            {
                Id = s.Ids.Person(), Name = "Old Bones", Race = RaceId.Human, Sex = Sex.Male, BirthTick = 10, AgeYears = 80f,
                Stage = LifeStage.Elder, IsAlive = false, DeathTick = 199, DeathCause = DeathCause.Disease,
                SettlementId = s1.Id, CivilizationId = civ1.Id, Position = s1.Center,
            };
            s.People.Add(ghost);

            // ---------------------------------------------------------------- diplomacy: wars, relations, army with a path
            s.Tick = 230;
            var war = new War
            {
                Id = s.Ids.War(), AttackerId = civ2.Id, DefenderId = civ1.Id, StartTick = 225, WarScore = 17.5f,
                CasusBelli = "Insult to the War Idol", Battles = 2, AttackerLosses = 5, DefenderLosses = 7, SettlementsCaptured = 1,
            };
            s.Diplomacy.Wars.Add(war);
            var rel12 = s.Diplomacy.Get(civ1.Id, civ2.Id);
            rel12.WarId = war.Id; rel12.Stance = DiplomaticStance.War; rel12.Opinion = -64.25f;
            rel12.HasContact = true; rel12.LastContactTick = 224; rel12.TruceUntilTick = 99;
            var oldWar = new War
            {
                Id = s.Ids.War(), AttackerId = civ1.Id, DefenderId = civ3.Id, StartTick = 50, EndTick = 90, IsActive = false,
                WarScore = -30f, CasusBelli = "", Battles = 1,
            };
            s.Diplomacy.Wars.Add(oldWar);
            var rel13 = s.Diplomacy.Get(civ3.Id, civ1.Id);
            rel13.TradeAgreement = true; rel13.Stance = DiplomaticStance.Friendly; rel13.Opinion = 40f; rel13.HasContact = true;

            var army = ops.CreateArmy(civ2, s2, soldiers);
            army.State = ArmyState.Marching;
            army.TargetSettlementId = s1.Id;
            army.WarId = war.Id;
            army.Destination = s1.Center;
            var pos = army.Position;
            while (pos != s1.Center && army.Path.Count < 100) { pos = pos.StepToward(s1.Center); army.Path.Add(pos); }
            army.PathIndex = 3;
            army.MoveProgress = 0.375f;
            army.Strength = 42.5f;
            army.Morale = 0.85f;

            // ---------------------------------------------------------------- civilization details
            civ1.KnownTechs.Add(0); civ1.KnownTechs.Add(1);
            civ1.CurrentResearchTechId = 2;
            civ1.ResearchPool = 12.5f; civ1.TotalResearch = 1234.5f;
            civ1.UnlockedBuildings[(int)BuildingType.Library] = true;
            civ1.UnlockedBuildings[(int)BuildingType.Temple] = true;
            var md = civ1.Modifiers;
            md.Production[(int)ResourceType.Stone] = 1.25f; md.Service[(int)ServiceType.Research] = 1.5f;
            md.Research = 1.1f; md.Growth = 1.05f; md.BuildSpeed = 0.95f; md.Faith = 1.2f; md.Military = 1.3f;
            md.Storage = 1.4f; md.Trade = 0.9f; md.MoveSpeed = 1.15f; md.HealthBonus = 0.05f; md.HappinessBonus = 0.1f; md.LifespanBonusYears = 2.5f;
            civ1.Devotion = 0.75f; civ1.GodFavor = -0.2f; civ1.Population = 15; civ1.MilitaryPower = 33.3f;
            civ1.LeaderId = elder.Id;
            civ1.Memory["ai.lastExpandTick"] = 140f;
            civ1.Memory["ai.Ökonomie.ключ"] = 0.5f;
            civ2.KnownTechs.Add(0);
            civ2.Memory["ai.aggression"] = 0.9f;

            // ---------------------------------------------------------------- settlement details
            s1.Tier = SettlementTier.Village;
            s1.DesiredBuilders = 2; s1.Homeless = 1; s1.Unemployed = 3; s2.Soldiers = 4;
            s1.Happiness = 0.71f; s1.Health = 0.93f; s1.Unrest = 0.12f; s1.Faith = 0.44f; s1.DefenseStrength = 12.5f;
            s1.LastStarvationTick = 77;
            for (int i = 0; i < Counts.Services; i++) { s1.ServiceOutput[i] = 0.5f * i; s1.ServiceCoverage[i] = 0.1f * i; }
            s1.JobPriority[(int)JobType.Farmer] = 2f;
            s1.JobPriority[(int)JobType.Builder] = 1.5f;
            s1.BuildQueue.Add(new BuildOrder { Type = BuildingType.House, Priority = 3.5f, RequestedTick = 205, Reason = "Homeless residents", Attempts = 1 });
            s1.BuildQueue.Add(new BuildOrder { Type = BuildingType.Well, Priority = 1.25f, RequestedTick = 206, Reason = "Water", HasPreferredSite = true, PreferredSite = new Vec2i(18, 14) });
            s1.Memory["ai.lastBuild"] = 199f;
            s1.Memory["planner.zone.ε"] = -3.5f;
            s2.Memory["ai.lastBuild"] = 180f;
            s1.Stockpile.Capacity = 400f;
            foreach (var st in s.Settlements.Items)
            {
                st.Stockpile.Add(ResourceType.Food, 30f);
                st.Stockpile.TryConsume(ResourceType.Wood, 10f);
                st.Stockpile.RollStatistics(0.5f);
                st.Stockpile.Add(ResourceType.Stone, 5f);
                st.Stockpile.RollStatistics(0.25f);
                st.Stockpile.Add(ResourceType.Wood, 2.5f);      // un-rolled current-period flows (persisted since schema 2)
                st.Stockpile.TryConsume(ResourceType.Food, 1.25f);
            }
            s1.Stockpile.SetRaw(ResourceType.Gold, 17.25f);

            // ---------------------------------------------------------------- god, weather, chronicle, stats, memory
            var g = s.God;
            g.Mana = 321.5f; g.MaxMana = 640f; g.ManaPerTick = 0.75f; g.Alignment = -0.25f;
            for (int i = 0; i < g.CooldownUntil.Length; i++) g.CooldownUntil[i] = 1000 + i * 13;
            g.PowersCast = 9; g.LastCastTick = 190;
            var w = s.Weather;
            w.Season = Season.Autumn; w.Rainfall = 0.6f; w.TemperatureOffset = -0.3f; w.FoodYieldMultiplier = 0.85f;
            w.DroughtTicksRemaining = 5; w.RainTicksRemaining = 2; w.StormIntensity = 0.4f;
            ops.AddChronicle(ChronicleCategory.War, "The Orcs marched on " + s1.Name + ".", civ2.Id, s1.Id, s1.Center, 3);
            s.Chronicle.Add(new ChronicleEntry { Tick = 231, Category = ChronicleCategory.Divine, Text = null, CivilizationId = -1, SettlementId = -1, Position = Vec2i.Invalid, Importance = 0 });
            var stats = s.Stats;
            stats.WarsDeclared = 2; stats.BattlesFought = 3; stats.GodPowersCast = 9; stats.PeakPopulation = 40;
            for (int i = 0; i < stats.DeathsByCause.Length; i++) stats.DeathsByCause[i] = i + 1;
            stats.TotalDeaths = 36; stats.BuildingsDestroyed = 4;
            s.Memory["world.lastDisasterTick"] = 12f;
            s.Memory["divine.prophet"] = 1f;

            // ---------------------------------------------------------------- terrain variety (after placement)
            var map = s.Map;
            var rng = new Rng(99);
            for (int i = 0; i < map.TileCount; i++)
            {
                map.Elevation[i] = rng.Range(-1f, 1f);
                map.Moisture[i] = rng.NextFloat();
                map.Temperature[i] = rng.NextFloat();
                map.Fertility[i] = rng.NextFloat();
                map.Resource[i] = rng.Range(0f, 500f);
                bool used = map.BuildingAt[i] >= 0 || map.Owner[i] >= 0;
                map.Flags[i] = used ? TileFlags.FertileSoil | TileFlags.Farmland : (TileFlags)rng.Range(0, 65536);
                if (!used) map.Biomes[i] = (Biome)rng.Range(0, Counts.Biomes);
            }
            map.SeaLevel = 0.05f;
            map.TerrainVersion = 3;

            s.Tick = 4321;
            return ctx;
        }

        public static WorldState Build(GameDatabase db) => BuildContext(db).State;

        private static List<Building> PlaceHouses(WorldOps ops, Settlement st, int count, Vec2i near)
        {
            var list = new List<Building>();
            for (int i = 0; i < count; i++)
            {
                var site = ops.FindFreeSite(new Vec2i(near.X, near.Y + i * 2), 1, 1, 10);
                list.Add(ops.CreateBuilding(st, BuildingType.House, site, (byte)(i % 4), true));
            }
            return list;
        }

        private static void HouseEveryone(WorldOps ops, WorldState s, Settlement st, List<Building> houses)
        {
            foreach (int pid in st.ResidentIds.ToArray())
            {
                var p = s.People.Get(pid);
                foreach (var h in houses) if (ops.AssignHome(p, h)) break;
            }
        }
    }
}
