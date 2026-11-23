using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;
using Kit = GodSim.Tests.Economy.Production.ProductionTestKit;

namespace GodSim.Tests.Economy.Production
{
    public class ProductionSystemTests
    {
        private const float Tol = 1e-4f;
        // Farm: 0.22 food * 4 slots * efficiency 0.8 (skill 1, no experience) * fertility factor 1.0 * 2 ticks.
        private const float FarmRunOutput = 0.22f * 4f * 0.8f * 2f;

        private static SimContext FarmWorld(int adults, int staffed, out Settlement s, out Building farm, out Civilization civ)
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), adults, out s, out civ);
            farm = Kit.Place(ctx, s, BuildingType.Farm, new Vec2i(40, 40));
            if (staffed > 0) Kit.Staff(ctx, s, farm, JobType.Farmer, staffed);
            return ctx;
        }

        // ------------------------------------------------------------------ basics

        [Test]
        public void SystemShape_MatchesContract()
        {
            var p = new ProductionSystem();
            Assert.AreEqual(2, p.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(p.Name));
            var r = new ResourceRegrowthSystem();
            Assert.AreEqual(48, r.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(r.Name));
        }

        [Test]
        public void Farm_FullyStaffed_ProducesExpectedFood()
        {
            var ctx = FarmWorld(4, 4, out var s, out var farm, out _);
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(FarmRunOutput, s.Stockpile.Get(ResourceType.Food), Tol);
            Assert.AreEqual(0.8f, farm.Efficiency, Tol);
        }

        [Test]
        public void Farm_FourWorkers_FeedFifteenToEighteenPeople()
        {
            var ctx = FarmWorld(4, 4, out var s, out var farm, out _);
            foreach (int pid in farm.Workers) ctx.State.People.Get(pid).Experience = 0.5f; // a seasoned crew
            Kit.Step(new ProductionSystem(), ctx);
            float perTick = s.Stockpile.Get(ResourceType.Food) / 2f;
            float fed = perTick / ctx.Db.Race(RaceId.Human).FoodPerTick;
            Assert.That(fed, Is.InRange(15f, 18f));
        }

        [Test]
        public void Farm_OutputScalesWithFertilityAndFarmland()
        {
            var ctx = FarmWorld(4, 4, out var s, out var farm, out _);
            var map = ctx.State.Map;
            map.Fertility[map.Index(40, 40)] = 1f;                  // factor 0.4 + 1.0 = 1.4
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(FarmRunOutput * 1.4f, s.Stockpile.Get(ResourceType.Food), 1e-3f);

            var ctx2 = FarmWorld(4, 4, out var s2, out _, out _);
            var map2 = ctx2.State.Map;
            map2.Fertility[map2.Index(40, 40)] = 0.2f;              // 0.6
            for (int x = 38; x <= 41; x++) map2.Set(map2.Index(x, 42), TileFlags.Farmland); // +4 * 0.025
            Kit.Step(new ProductionSystem(), ctx2);
            Assert.AreEqual(FarmRunOutput * 0.7f, s2.Stockpile.Get(ResourceType.Food), 1e-3f);

            var ctx3 = FarmWorld(4, 4, out var s3, out _, out _);
            ctx3.State.Map.Fertility[ctx3.State.Map.Index(40, 40)] = 0f; // floor 0.4
            Kit.Step(new ProductionSystem(), ctx3);
            Assert.AreEqual(FarmRunOutput * 0.4f, s3.Stockpile.Get(ResourceType.Food), 1e-3f);
        }

        [Test]
        public void Farm_OutputScalesWithSeasonalFoodYield()
        {
            var ctx = FarmWorld(4, 4, out var s, out _, out _);
            ctx.State.Weather.FoodYieldMultiplier = 0.5f;
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(FarmRunOutput * 0.5f, s.Stockpile.Get(ResourceType.Food), Tol);
        }

        [Test]
        public void NoWorkers_ProducesNothing()
        {
            var ctx = FarmWorld(0, 0, out var s, out var farm, out _);
            farm.Efficiency = 0.7f;
            Kit.Step(new ProductionSystem(), ctx, 5);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Food));
            Assert.AreEqual(0f, farm.Efficiency);
        }

        [Test]
        public void PartialStaffing_IsLinearInWorkers()
        {
            var ctx = FarmWorld(2, 2, out var s, out var farm, out _);
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(FarmRunOutput * 0.5f, s.Stockpile.Get(ResourceType.Food), Tol);
            Assert.AreEqual(0.4f, farm.Efficiency, Tol);
        }

        [Test]
        public void SkillAndConditionScaleOutput()
        {
            var ctx = FarmWorld(4, 4, out var s, out var farm, out _);
            foreach (int pid in farm.Workers) { var p = ctx.State.People.Get(pid); p.Skill = 1.2f; p.Experience = 1f; }
            farm.Condition = 0.5f;
            Kit.Step(new ProductionSystem(), ctx);
            // per worker 1.2 * 1.2 = 1.44; efficiency 1.44 * 0.5 = 0.72
            Assert.AreEqual(0.22f * 4f * 0.72f * 2f, s.Stockpile.Get(ResourceType.Food), Tol);
            Assert.AreEqual(0.72f, farm.Efficiency, Tol);
        }

        [Test]
        public void DestroyedAndUnfinishedBuildings_ProduceNothing()
        {
            var ctx = FarmWorld(8, 4, out var s, out var farm, out _);
            farm.IsDestroyed = true; // flagged, not yet flushed: must be skipped while iterating

            var site = Kit.Place(ctx, s, BuildingType.Farm, new Vec2i(44, 44), complete: false);
            int extra = 0;
            foreach (int pid in s.ResidentIds)
            {
                var p = ctx.State.People.Get(pid);
                if (p.Job != JobType.None || extra >= 4) continue;
                site.Workers.Add(p.Id); p.WorkplaceId = site.Id; p.Job = JobType.Farmer; extra++;
            }
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Food));
            Assert.AreEqual(0f, site.Efficiency);
        }

        [Test]
        public void MissingCivilization_UsesNeutralModifiers()
        {
            var ctx = FarmWorld(4, 4, out var s, out _, out var civ);
            ctx.State.Civilizations.Remove(civ.Id);
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(FarmRunOutput, s.Stockpile.Get(ResourceType.Food), Tol);
        }

        [Test]
        public void WorkersInArmies_DoNotWork()
        {
            var ctx = FarmWorld(4, 4, out var s, out var farm, out _);
            ctx.State.People.Get(farm.Workers[0]).ArmyId = 99;
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(FarmRunOutput * 0.75f, s.Stockpile.Get(ResourceType.Food), Tol);
        }

        [Test]
        public void Modifiers_MultiplyOutput()
        {
            var ctx = FarmWorld(4, 4, out var s, out _, out var civ);
            civ.Modifiers.Production[(int)ResourceType.Food] = 1.5f;
            ctx.Config.ProductionMultiplier = 2f;
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(FarmRunOutput * 3f, s.Stockpile.Get(ResourceType.Food), 1e-3f);
        }

        [Test]
        public void Experience_GrowsWithWorkAndCaps()
        {
            var ctx = FarmWorld(4, 4, out _, out var farm, out _);
            var a = ctx.State.People.Get(farm.Workers[0]);
            var b = ctx.State.People.Get(farm.Workers[1]);
            b.Experience = 0.999f;
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(ProductionSystem.ExperienceGainPerTick * 2f, a.Experience, 1e-6f);
            Assert.AreEqual(1f, b.Experience, 1e-6f);
        }

        // ------------------------------------------------------------------ input chains

        private static SimContext SmelterWorld(float ore, out Settlement s, out Building smelter)
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 2, out s, out _);
            smelter = Kit.Place(ctx, s, BuildingType.Smelter, new Vec2i(40, 40));
            Kit.Staff(ctx, s, smelter, JobType.Smith, 2);
            s.Stockpile.SetRaw(ResourceType.Ore, ore);
            return ctx;
        }

        [Test]
        public void Smelter_ConsumesInputsAndProducesMetal()
        {
            var ctx = SmelterWorld(10f, out var s, out var smelter);
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(10f - 0.06f * 2f * 2f, s.Stockpile.Get(ResourceType.Ore), Tol);
            Assert.AreEqual(0.05f * 2f * 0.8f * 2f, s.Stockpile.Get(ResourceType.Metal), Tol);
            Assert.AreEqual(0.8f, smelter.Efficiency, Tol);
        }

        [Test]
        public void Smelter_MissingInputs_GiveProportionalOutput()
        {
            var ctx = SmelterWorld(0.12f, out var s, out var smelter); // half of the 0.24 required
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Ore), Tol);
            Assert.AreEqual(0.05f * 2f * 0.8f * 2f * 0.5f, s.Stockpile.Get(ResourceType.Metal), Tol);
            Assert.AreEqual(0.4f, smelter.Efficiency, Tol);

            Kit.Step(new ProductionSystem(), ctx); // nothing left
            Assert.AreEqual(0.08f, s.Stockpile.Get(ResourceType.Metal), Tol);
            Assert.AreEqual(0f, smelter.Efficiency, Tol);
        }

        [Test]
        public void Smelter_PoweredIndustryGetsBonus()
        {
            var ctx = SmelterWorld(10f, out var s, out var smelter);
            s.ServiceCoverage[(int)ServiceType.Power] = 1f;
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(0.05f * 2f * 0.8f * 1.25f * 2f, s.Stockpile.Get(ResourceType.Metal), Tol);
            Assert.AreEqual(1f, smelter.Efficiency, Tol);
        }

        [Test]
        public void Smelter_FullOutputStorage_DoesNotWasteInputs()
        {
            var ctx = SmelterWorld(10f, out var s, out _);
            s.Stockpile.SetRaw(ResourceType.Metal, s.Stockpile.Capacity);
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(10f, s.Stockpile.Get(ResourceType.Ore), Tol);
            Assert.AreEqual(s.Stockpile.Capacity, s.Stockpile.Get(ResourceType.Metal), Tol);
        }

        [Test]
        public void ServiceBuilding_ConsumesInputsAndReportsEfficiency()
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 2, out var s, out _);
            var plant = Kit.Place(ctx, s, BuildingType.PowerPlant, new Vec2i(40, 40));
            Kit.Staff(ctx, s, plant, JobType.Engineer, 2);
            s.Stockpile.SetRaw(ResourceType.Fuel, 0.1f); // half of 0.05 * 2 * 2
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Fuel), Tol);
            Assert.AreEqual(0.4f, plant.Efficiency, Tol);
        }

        // ------------------------------------------------------------------ extraction

        private static SimContext MineWorld(out Settlement s, out Building mine)
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 3, out s, out _);
            mine = Kit.Place(ctx, s, BuildingType.Mine, new Vec2i(45, 45));
            Kit.Staff(ctx, s, mine, JobType.Miner, 3);
            return ctx;
        }

        [Test]
        public void Mine_DepletesDepositsAndStopsWhenExhausted()
        {
            var ctx = MineWorld(out var s, out var mine);
            Kit.SetFlag(ctx, 46, 45, TileFlags.OreDeposit, 1f);
            Kit.SetFlag(ctx, 44, 45, TileFlags.OreDeposit, 1f);
            Kit.SetFlag(ctx, 45, 46, TileFlags.OreDeposit, 1f);
            int terrainEvents = 0;
            ctx.Events.Subscribe<TerrainChangedEvent>(e => terrainEvents++);
            int version = ctx.State.Map.TerrainVersion;

            var sys = new ProductionSystem();
            Kit.Step(sys, ctx);
            Assert.AreEqual(0.07f * 3f * 0.8f * 2f, s.Stockpile.Get(ResourceType.Ore), Tol); // 3 deposits -> factor 1.0
            Assert.AreEqual(3f - s.Stockpile.Get(ResourceType.Ore), Kit.SumResource(ctx.State.Map, TileFlags.OreDeposit), Tol);

            Kit.Step(sys, ctx, 40);
            Assert.AreEqual(3f, s.Stockpile.Get(ResourceType.Ore), 5e-3f, "extraction conserves the deposit amount");
            Assert.AreEqual(0, Kit.CountFlag(ctx.State.Map, TileFlags.OreDeposit), "exhausted deposits lose their flag");
            Assert.AreEqual(0f, mine.Efficiency);
            Assert.IsTrue(terrainEvents > 0);
            Assert.AreNotEqual(version, ctx.State.Map.TerrainVersion);
            int logs = 0;
            foreach (var line in s.AiLog) if (line == "Mine exhausted") logs++;
            Assert.AreEqual(1, logs, "exhaustion is logged once");
        }

        [Test]
        public void Mine_DepletesNearestDepositFirst()
        {
            var ctx = MineWorld(out var s, out _);
            Kit.SetFlag(ctx, 48, 45, TileFlags.OreDeposit, 100f); // distance 3
            Kit.SetFlag(ctx, 46, 46, TileFlags.OreDeposit, 100f); // distance 1
            Kit.SetFlag(ctx, 49, 45, TileFlags.OreDeposit, 100f); // distance 4: out of range
            Kit.Step(new ProductionSystem(), ctx);
            var map = ctx.State.Map;
            float produced = s.Stockpile.Get(ResourceType.Ore);
            Assert.AreEqual(0.07f * 3f * 0.8f * 0.9f * 2f, produced, Tol); // 2 deposits in range -> factor 0.9
            Assert.AreEqual(100f - produced, map.Resource[map.Index(46, 46)], Tol);
            Assert.AreEqual(100f, map.Resource[map.Index(48, 45)], Tol);
            Assert.AreEqual(100f, map.Resource[map.Index(49, 45)], Tol);
        }

        [Test]
        public void Mine_WithoutDeposits_ProducesNothingAndReportsExhaustion()
        {
            var ctx = MineWorld(out var s, out var mine);
            Kit.Step(new ProductionSystem(), ctx, 3);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Ore));
            Assert.AreEqual(0f, mine.Efficiency);
            Assert.IsTrue(s.AiLog.Contains("Mine exhausted"));
        }

        [Test]
        public void Mine_WithFullStorage_DoesNotDepleteDeposits()
        {
            var ctx = MineWorld(out var s, out _);
            Kit.SetFlag(ctx, 46, 45, TileFlags.OreDeposit, 50f);
            s.Stockpile.SetRaw(ResourceType.Ore, s.Stockpile.Capacity);
            Kit.Step(new ProductionSystem(), ctx, 3);
            Assert.AreEqual(50f, ctx.State.Map.Resource[ctx.State.Map.Index(46, 45)], Tol);
        }

        [Test]
        public void ByProducts_DrawOnTheSameDeposit()
        {
            var mine = Kit.Mine();
            mine.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.07f), new ResourceAmount(ResourceType.Gold, 0.01f) };
            var ctx = Kit.CreateWorld(Kit.CreateDb(true, mine), 3, out var s, out _);
            var b = Kit.Place(ctx, s, BuildingType.Mine, new Vec2i(45, 45));
            Kit.Staff(ctx, s, b, JobType.Miner, 3);
            Kit.SetFlag(ctx, 46, 45, TileFlags.OreDeposit, 100f);
            Kit.Step(new ProductionSystem(), ctx);
            float ore = s.Stockpile.Get(ResourceType.Ore), gold = s.Stockpile.Get(ResourceType.Gold);
            Assert.AreEqual(0.07f * 3f * 0.8f * 0.8f * 2f, ore, Tol); // 1 deposit -> factor 0.8
            Assert.AreEqual(0.01f * 3f * 0.8f * 0.8f * 2f, gold, Tol);
            Assert.AreEqual(100f - ore - gold, ctx.State.Map.Resource[ctx.State.Map.Index(46, 45)], Tol);
        }

        [Test]
        public void Woodcutter_NeedsTreesAndFellsThem()
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 2, out var s, out _);
            var camp = Kit.Place(ctx, s, BuildingType.WoodcutterCamp, new Vec2i(50, 20));
            Kit.Staff(ctx, s, camp, JobType.Woodcutter, 2);
            var sys = new ProductionSystem();
            Kit.Step(sys, ctx);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Wood), "no trees -> no wood");
            Assert.AreEqual(0f, camp.Efficiency);

            for (int x = 52; x <= 53; x++)
                for (int y = 17; y <= 24; y++) Kit.SetFlag(ctx, x, y, TileFlags.Trees, 100f); // 16 trees within 4
            float before = Kit.SumResource(ctx.State.Map, TileFlags.Trees);
            float exp = ctx.State.People.Get(camp.Workers[0]).Experience;
            Kit.Step(sys, ctx);
            float wood = s.Stockpile.Get(ResourceType.Wood);
            Assert.AreEqual(0.12f * 2f * (0.8f + 0.4f * exp) * 2f, wood, Tol);
            Assert.AreEqual(before - wood, Kit.SumResource(ctx.State.Map, TileFlags.Trees), 1e-3f);
            Assert.IsTrue(camp.Efficiency > 0f);
        }

        [Test]
        public void FishingHut_NeedsWater()
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 2, out var s, out _);
            var hut = Kit.Place(ctx, s, BuildingType.FishingHut, new Vec2i(20, 50));
            Kit.Staff(ctx, s, hut, JobType.Fisher, 2);
            var sys = new ProductionSystem();
            Kit.Step(sys, ctx);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Food));

            var map = ctx.State.Map;
            for (int x = 21; x <= 23; x++)
                for (int y = 49; y <= 52; y++) map.Biomes[map.Index(x, y)] = Biome.Lake; // 12 water tiles -> factor 1.0
            foreach (int pid in hut.Workers) ctx.State.People.Get(pid).Experience = 0f;
            Kit.Step(sys, ctx);
            Assert.AreEqual(0.15f * 2f * 0.8f * 2f, s.Stockpile.Get(ResourceType.Food), Tol);
        }

        [Test]
        public void HuntingLodge_NeedsWoodlandButDoesNotFellIt()
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 2, out var s, out _);
            var lodge = Kit.Place(ctx, s, BuildingType.HuntingLodge, new Vec2i(20, 50));
            Kit.Staff(ctx, s, lodge, JobType.Hunter, 2);
            var sys = new ProductionSystem();
            Kit.Step(sys, ctx);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Food));

            for (int x = 22; x <= 24; x++)
                for (int y = 46; y <= 55; y++) Kit.SetFlag(ctx, x, y, TileFlags.Trees, 50f); // 30 trees within 5 -> factor 1.0
            foreach (int pid in lodge.Workers) ctx.State.People.Get(pid).Experience = 0f;
            Kit.Step(sys, ctx);
            Assert.AreEqual(0.12f * 2f * 0.8f * 2f, s.Stockpile.Get(ResourceType.Food), Tol);
            Assert.AreEqual(30f * 50f, Kit.SumResource(ctx.State.Map, TileFlags.Trees), Tol);
        }

        // ------------------------------------------------------------------ foraging

        [Test]
        public void IdleAdults_Forage_ByAge()
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 6, out var s, out var civ);
            ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Female, 5f); // child: does not forage
            var sys = new ProductionSystem();
            Kit.Step(sys, ctx);
            Assert.AreEqual(6 * ProductionSystem.EarlyForagePerTick * 2f, s.Stockpile.Get(ResourceType.Food), Tol);

            s.Stockpile.SetRaw(ResourceType.Food, 0f);
            civ.Age = AgeId.Iron;
            Kit.Step(sys, ctx);
            Assert.AreEqual(6 * ProductionSystem.LateForagePerTick * 2f, s.Stockpile.Get(ResourceType.Food), Tol);

            s.Stockpile.SetRaw(ResourceType.Food, 0f);
            ctx.State.Weather.FoodYieldMultiplier = 0.5f;
            Kit.Step(sys, ctx);
            Assert.AreEqual(6 * ProductionSystem.LateForagePerTick * 2f * 0.5f, s.Stockpile.Get(ResourceType.Food), Tol);
        }

        [Test]
        public void EmployedAndSoldiers_DoNotForage()
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 6, out var s, out _);
            var smelter = Kit.Place(ctx, s, BuildingType.Smelter, new Vec2i(40, 40));
            Kit.Staff(ctx, s, smelter, JobType.Smith, 2);       // 4 idle left
            foreach (int pid in s.ResidentIds)
            {
                var p = ctx.State.People.Get(pid);
                if (p.Job == JobType.None) { p.ArmyId = 7; break; } // 3 idle left
            }
            Kit.Step(new ProductionSystem(), ctx);
            Assert.AreEqual(3 * ProductionSystem.EarlyForagePerTick * 2f, s.Stockpile.Get(ResourceType.Food), Tol);
        }

        // ------------------------------------------------------------------ robustness & determinism

        [Test]
        public void ZeroSlotsAndBadData_NeverProduceNaN()
        {
            var badFarm = Kit.Farm();
            badFarm.JobSlots = 0; // invalid content (would fail Freeze validation)
            var db = Kit.CreateDb(false, badFarm);
            var ctx = Kit.CreateWorld(db, 4, out var s, out var civ);
            var farm = Kit.Place(ctx, s, BuildingType.Farm, new Vec2i(40, 40));
            var p0 = ctx.State.People.Get(s.ResidentIds[0]);
            farm.Workers.Add(p0.Id); p0.WorkplaceId = farm.Id; p0.Job = JobType.Farmer;

            var smelter = Kit.Place(ctx, s, BuildingType.Smelter, new Vec2i(44, 44));
            Kit.Staff(ctx, s, smelter, JobType.Smith, 2);
            foreach (int pid in smelter.Workers) { var p = ctx.State.People.Get(pid); p.Skill = float.NaN; p.Experience = float.PositiveInfinity; }
            smelter.Condition = float.NaN;
            s.Stockpile.SetRaw(ResourceType.Ore, 5f);

            var mine = Kit.Place(ctx, s, BuildingType.Mine, new Vec2i(20, 20));
            Kit.Staff(ctx, s, mine, JobType.Miner, 1);
            Kit.SetFlag(ctx, 21, 20, TileFlags.OreDeposit, float.NaN);

            civ.Modifiers.Production[(int)ResourceType.Ore] = float.NaN;
            ctx.State.Weather.FoodYieldMultiplier = float.NaN;
            ctx.State.Map.Fertility[ctx.State.Map.Index(40, 40)] = float.NaN;

            Kit.Step(new ProductionSystem(), ctx, 10);

            for (int i = 0; i < Counts.Resources; i++)
            {
                float v = s.Stockpile.Get((ResourceType)i);
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), ((ResourceType)i) + " is not finite");
            }
            foreach (var b in ctx.State.Buildings.Items) Assert.IsFalse(float.IsNaN(b.Efficiency) || b.Efficiency < 0f || b.Efficiency > ProductionSystem.MaxEfficiency);
            foreach (var p in ctx.State.People.Items) Assert.IsFalse(float.IsNaN(p.Experience) || float.IsInfinity(p.Experience));
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++) Assert.IsFalse(float.IsNaN(map.Resource[i]) || float.IsInfinity(map.Resource[i]));
            Assert.IsTrue(s.Stockpile.Get(ResourceType.Metal) > 0f, "NaN skill/condition sanitized rather than zeroing everything");
        }

        [Test]
        public void ZeroDeltaTicks_DoesNothing()
        {
            var ctx = FarmWorld(4, 4, out var s, out _, out _);
            ctx.DeltaTicks = 0;
            new ProductionSystem().Tick(ctx);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Food));
        }

        [Test]
        public void SameSeed_GivesIdenticalResults()
        {
            Snapshot a = RunMixedWorld(77), b = RunMixedWorld(77);
            Assert.AreEqual(a.Stock, b.Stock);
            Assert.AreEqual(a.Resource, b.Resource);
            Assert.AreEqual(a.Flags, b.Flags);
            Assert.AreEqual(a.Fertility, b.Fertility);
            Assert.AreEqual(a.Efficiency, b.Efficiency);
            Assert.AreEqual(a.Rng, b.Rng);
            Assert.IsTrue(a.Stock[(int)ResourceType.Food] > 0f);
        }

        private struct Snapshot
        {
            public float[] Stock, Resource, Fertility, Efficiency;
            public TileFlags[] Flags;
            public ulong Rng;
        }

        private static Snapshot RunMixedWorld(ulong seed)
        {
            var ctx = Kit.CreateWorld(Kit.CreateDb(), 14, out var s, out _, Biome.Forest, seed);
            var map = ctx.State.Map;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    if ((x * 7 + y * 3) % 5 != 0 && (x < 26 || x > 38)) Kit.SetFlag(ctx, x, y, TileFlags.Trees, 40f + (x + y) % 60);
            for (int x = 46; x <= 48; x++) Kit.SetFlag(ctx, x, 30, TileFlags.OreDeposit, 30f);
            var farm = Kit.Place(ctx, s, BuildingType.Farm, new Vec2i(30, 40));
            var mine = Kit.Place(ctx, s, BuildingType.Mine, new Vec2i(47, 31));
            var camp = Kit.Place(ctx, s, BuildingType.WoodcutterCamp, new Vec2i(24, 30));
            var smelter = Kit.Place(ctx, s, BuildingType.Smelter, new Vec2i(34, 26));
            // keep the generated (seeded) skills so randomness flows through the comparison
            AssignRandomSkillStaff(ctx, s, farm, JobType.Farmer, 4);
            AssignRandomSkillStaff(ctx, s, mine, JobType.Miner, 3);
            AssignRandomSkillStaff(ctx, s, camp, JobType.Woodcutter, 2);
            AssignRandomSkillStaff(ctx, s, smelter, JobType.Smith, 2);
            s.Stockpile.Capacity = 100000f;
            TestWorlds.RunAll(new List<ISimSystem> { new ProductionSystem(), new ResourceRegrowthSystem() }, ctx, 3000);
            var snap = new Snapshot
            {
                Stock = new float[Counts.Resources],
                Resource = (float[])map.Resource.Clone(),
                Fertility = (float[])map.Fertility.Clone(),
                Flags = (TileFlags[])map.Flags.Clone(),
                Efficiency = new[] { farm.Efficiency, mine.Efficiency, camp.Efficiency, smelter.Efficiency },
                Rng = ctx.State.Rng.State,
            };
            for (int i = 0; i < Counts.Resources; i++) snap.Stock[i] = s.Stockpile.Get((ResourceType)i);
            return snap;
        }

        private static void AssignRandomSkillStaff(SimContext ctx, Settlement s, Building b, JobType job, int count)
        {
            int n = 0;
            foreach (int pid in s.ResidentIds)
            {
                if (n >= count) break;
                var p = ctx.State.People.Get(pid);
                if (p.Job != JobType.None) continue;
                Assert.IsTrue(ctx.Ops.AssignJob(p, b, job));
                n++;
            }
        }
    }
}
