using System;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class SupplyChainAdvisorTests
    {
        private static ResourceAmount[] Res(ResourceType t, float a) => new[] { new ResourceAmount(t, a) };

        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource, MinAge = AgeId.Bronze,
                Job = JobType.Miner, JobSlots = 3, OutputsPerWorker = Res(ResourceType.Ore, 0.07f),
                Placement = PlacementRule.NearOre, Cost = Res(ResourceType.Wood, 10f), BuildWork = 20f
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Smelter, Name = "Smelter", Category = BuildingCategory.Industry, MinAge = AgeId.Bronze,
                Job = JobType.Smith, JobSlots = 2, InputsPerWorker = Res(ResourceType.Ore, 0.06f), OutputsPerWorker = Res(ResourceType.Metal, 0.05f),
                Placement = PlacementRule.Isolated, Cost = Res(ResourceType.Stone, 5f), BuildWork = 20f
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Blacksmith, Name = "Blacksmith", Category = BuildingCategory.Industry, MinAge = AgeId.Iron,
                Job = JobType.Smith, JobSlots = 2,
                InputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.04f), new ResourceAmount(ResourceType.Wood, 0.02f) },
                OutputsPerWorker = Res(ResourceType.Tools, 0.04f), Placement = PlacementRule.Isolated, BuildWork = 20f
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Barracks, Name = "Barracks", Category = BuildingCategory.Military, MinAge = AgeId.Iron,
                Cost = new[] { new ResourceAmount(ResourceType.Tools, 20f), new ResourceAmount(ResourceType.Wood, 10f) }, BuildWork = 30f
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Factory, Name = "Factory", Category = BuildingCategory.Industry, MinAge = AgeId.Industrial,
                Job = JobType.Engineer, JobSlots = 4,
                InputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.05f), new ResourceAmount(ResourceType.Fuel, 0.04f) },
                OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Machinery, 0.05f), new ResourceAmount(ResourceType.Goods, 0.04f) },
                BuildWork = 40f
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.CoalMine, Name = "Coal Mine", Category = BuildingCategory.Resource, MinAge = AgeId.Industrial,
                Job = JobType.Miner, JobSlots = 3, OutputsPerWorker = Res(ResourceType.Fuel, 0.10f), Placement = PlacementRule.NearCoal, BuildWork = 30f
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement Setup(GameDatabase db, AgeId age, bool ore, out SimContext ctx, out Civilization civ, ulong seed = 7)
        {
            ctx = TestWorlds.CreateContext(db, 64, seed);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out civ);
            civ.Age = age;
            if (ore) Deposit(ctx.State.Map, s, TileFlags.OreDeposit);
            return s;
        }

        private static void Deposit(WorldMap map, Settlement s, TileFlags flag)
        {
            for (int k = 0; k < 6; k++)
            {
                int i = map.Index(s.Center.X + 3 + k, s.Center.Y + 1);
                map.Set(i, flag);
                map.Resource[i] = 100f;
            }
            map.MarkTerrainChanged();
        }

        private static Building AddStaffed(SimContext ctx, Settlement s, BuildingType type, JobType job, int workers)
        {
            var def = ctx.Db.Building(type);
            var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 12);
            var b = ctx.Ops.CreateBuilding(s, type, site, 0, true);
            Assert.IsNotNull(b);
            int assigned = 0;
            for (int i = 0; i < s.ResidentIds.Count && assigned < workers; i++)
            {
                var p = ctx.State.People.Get(s.ResidentIds[i]);
                if (p == null || !p.IsAdult || p.WorkplaceId >= 0) continue;
                if (ctx.Ops.AssignJob(p, b, job)) assigned++;
            }
            Assert.AreEqual(workers, assigned);
            return b;
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        /// <summary>
        /// Per-run board value of a per-council-cycle expectation. Votes are normalized per 24-tick council cycle
        /// (AdvisorSystemBase.CycleScale = TickInterval / 24), so one run of this 12-tick advisor contributes half of
        /// what it votes per cycle. Thresholds below are written per cycle and checked against a single run.
        /// </summary>
        private static float PerRun(float perCycle) => perCycle * System.Math.Min(1f, new SupplyChainAdvisor().TickInterval / 24f);

        private static void RunOnce(SimContext ctx) => TestWorlds.Run(new SupplyChainAdvisor(), ctx, 12);

        [Test]
        public void Model_IsBuiltFromDefinitions()
        {
            var adv = new SupplyChainAdvisor();
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db, 32);
            adv.Initialize(ctx);
            var m = adv.Model;
            Assert.AreEqual(1, m.ProducersOf((int)ResourceType.Ore).Length);
            Assert.AreEqual((int)BuildingType.Mine, m.ProducersOf((int)ResourceType.Ore)[0]);
            Assert.AreEqual(0.06f, m.ConsumeRate((int)BuildingType.Smelter, (int)ResourceType.Ore), 1e-6f);
            Assert.AreEqual(2, m.ConsumersOf((int)ResourceType.Metal).Length); // Blacksmith + Factory
            Assert.AreEqual(2, m.ChainDepth((int)ResourceType.Tools));          // Ore -> Metal -> Tools
            Assert.AreEqual(0, m.ChainDepth((int)ResourceType.Ore));
        }

        [Test]
        public void StarvedSmelter_VotesMine_AndHoldsBackConsumers()
        {
            var s = Setup(MakeDb(), AgeId.Bronze, true, out var ctx, out var civ);
            AddStaffed(ctx, s, BuildingType.Smelter, JobType.Smith, 2);
            s.Stockpile.SetRaw(ResourceType.Ore, 0f);
            RunOnce(ctx);

            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Mine), PerRun(0.5f));
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Smelter), 0f, "no more consumers of a starved input");
            Assert.Less(AdvisorBoard.JobBias(s, JobType.Smith), 0f, "idle smiths should be shifted away");
            Assert.Greater(AdvisorBoard.Urgency(s, SupplyChainAdvisor.Id), 0.5f);
            Assert.AreEqual((float)ResourceType.Ore + 1f, s.Mem(SupplyChainKeys.WorstResource));
            Assert.IsTrue(LogContains(s, "Smelter idle: Ore short"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void InfeasibleTerrain_SuppressesMineVote()
        {
            var s = Setup(MakeDb(), AgeId.Bronze, false, out var ctx, out var civ);
            AddStaffed(ctx, s, BuildingType.Smelter, JobType.Smith, 2);
            s.Stockpile.SetRaw(ResourceType.Ore, 0f);
            RunOnce(ctx);

            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.Mine), 0f);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Smelter), 0f);
        }

        [Test]
        public void ToolsDemand_VotesBlacksmithSmelterAndMine()
        {
            var s = Setup(MakeDb(), AgeId.Iron, true, out var ctx, out var civ);
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Barracks, Priority = 1f, Reason = "test" });
            s.Stockpile.SetRaw(ResourceType.Tools, 0f);
            s.Stockpile.SetRaw(ResourceType.Metal, 0f);
            RunOnce(ctx);

            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Blacksmith), PerRun(0.3f));
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Smelter), PerRun(0.1f), "Metal feeds the Tools chain");
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Mine), PerRun(0.05f), "Ore feeds Metal feeds Tools");
            Assert.IsTrue(LogContains(s, "Metal feeds Blacksmith -> Tools"), string.Join("\n", s.AiLog));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Factory), "Industrial building unavailable in Iron age");
        }

        [Test]
        public void UnavailableProducers_NeverReceiveVotes()
        {
            var s = Setup(MakeDb(), AgeId.Bronze, true, out var ctx, out var civ);
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Barracks, Priority = 1f });
            s.Stockpile.SetRaw(ResourceType.Tools, 0f);
            RunOnce(ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Blacksmith));
        }

        [Test]
        public void Overproduction_VotesNegativeOnProducersAndJobs()
        {
            var s = Setup(MakeDb(), AgeId.Bronze, true, out var ctx, out var civ);
            AddStaffed(ctx, s, BuildingType.Mine, JobType.Miner, 3);
            s.Stockpile.SetRaw(ResourceType.Ore, s.Stockpile.Capacity - 5f);
            RunOnce(ctx);

            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Mine), 0f);
            Assert.Less(AdvisorBoard.JobBias(s, JobType.Miner), 0f);
            Assert.IsTrue(LogContains(s, "Ore piling up"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void MercantileCivs_TolerateSurplusMore()
        {
            float Bias(float merc)
            {
                var s = Setup(MakeDb(), AgeId.Bronze, true, out var ctx, out var civ);
                AdvisorBoard.SetTrait(civ, AITrait.Mercantilism, merc);
                AddStaffed(ctx, s, BuildingType.Mine, JobType.Miner, 3);
                s.Stockpile.SetRaw(ResourceType.Ore, s.Stockpile.Capacity - 5f);
                RunOnce(ctx);
                return AdvisorBoard.BuildBias(s, BuildingType.Mine);
            }
            Assert.Less(Bias(0f), Bias(1f));
        }

        [Test]
        public void FactoryStarvedForFuel_VotesCoalMineWhenCoalPresent()
        {
            var s = Setup(MakeDb(), AgeId.Industrial, false, out var ctx, out var civ);
            Deposit(ctx.State.Map, s, TileFlags.CoalDeposit);
            AddStaffed(ctx, s, BuildingType.Factory, JobType.Engineer, 4);
            s.Stockpile.SetRaw(ResourceType.Metal, 100f);
            s.Stockpile.SetRaw(ResourceType.Fuel, 0f);
            RunOnce(ctx);

            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.CoalMine), PerRun(0.5f));
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Factory), 0f);
            Assert.IsTrue(LogContains(s, "Factory idle: Fuel short"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void ExistingProducerWithOpenSlots_PrefersStaffingOverBuilding()
        {
            var s = Setup(MakeDb(), AgeId.Bronze, true, out var ctx, out var civ);
            AddStaffed(ctx, s, BuildingType.Smelter, JobType.Smith, 2);
            AddStaffed(ctx, s, BuildingType.Mine, JobType.Miner, 1); // 2 open slots
            s.Stockpile.SetRaw(ResourceType.Ore, 0f);
            RunOnce(ctx);

            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Miner), 0f);
            var s2 = Setup(MakeDb(), AgeId.Bronze, true, out var ctx2, out var civ2);
            AddStaffed(ctx2, s2, BuildingType.Smelter, JobType.Smith, 2);
            s2.Stockpile.SetRaw(ResourceType.Ore, 0f);
            RunOnce(ctx2);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Mine), AdvisorBoard.BuildBias(s2, BuildingType.Mine));
        }

        [Test]
        public void Deterministic_AndFinite()
        {
            float[] Snapshot()
            {
                var s = Setup(MakeDb(), AgeId.Iron, true, out var ctx, out var civ, 99);
                AddStaffed(ctx, s, BuildingType.Smelter, JobType.Smith, 2);
                s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Barracks, Priority = 1f });
                s.Stockpile.SetRaw(ResourceType.Ore, 0f);
                s.Stockpile.SetRaw(ResourceType.Stone, s.Stockpile.Capacity);
                TestWorlds.Run(new SupplyChainAdvisor(), ctx, 60);
                var a = new float[Counts.Buildings + Counts.Jobs + 1];
                for (int i = 0; i < Counts.Buildings; i++) a[i] = AdvisorBoard.BuildBias(s, (BuildingType)i);
                for (int j = 0; j < Counts.Jobs; j++) a[Counts.Buildings + j] = AdvisorBoard.JobBias(s, (JobType)j);
                a[a.Length - 1] = AdvisorBoard.Urgency(s, SupplyChainAdvisor.Id);
                foreach (var kv in s.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
                return a;
            }
            var x = Snapshot();
            var y = Snapshot();
            for (int i = 0; i < x.Length; i++) Assert.AreEqual(x[i], y[i], "index " + i);
        }
    }
}
