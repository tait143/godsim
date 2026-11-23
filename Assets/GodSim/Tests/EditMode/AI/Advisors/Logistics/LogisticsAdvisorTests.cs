using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class LogisticsAdvisorTests
    {
        /// <summary>Votes +1 once per run with the logistics interval: measures what a full-strength vote becomes on the board.</summary>
        private sealed class LogisticsProbeAdvisor : AdvisorSystemBase
        {
            public override string AdvisorId => "logistics_probe";
            public override int TickInterval => 12;
            protected override void Advise(SimContext ctx, Civilization civ, Settlement s) => VoteBuild(s, civ, BuildingType.Well, 1f);
        }

        /// <summary>Board value of one full-strength (1.0) vote from a 12-tick advisor (accounts for cycle normalization).</summary>
        private static float Full()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 2, out _);
            ctx.DeltaTicks = 12;
            new LogisticsProbeAdvisor().Tick(ctx);
            float f = AdvisorBoard.BuildBias(s, BuildingType.Well);
            Assert.That(f, Is.GreaterThan(0f));
            return f;
        }

        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);

        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.WoodcutterCamp, Name = "Woodcutter Camp", Category = BuildingCategory.Resource,
                Job = JobType.Woodcutter, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Wood, 0.12f) },
                Cost = new[] { R(ResourceType.Wood, 15f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.StoneQuarry, Name = "Stone Quarry", Category = BuildingCategory.Resource,
                Job = JobType.Quarrier, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Stone, 0.08f) },
                Cost = new[] { R(ResourceType.Wood, 20f) }, Placement = PlacementRule.NearStone,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource,
                Job = JobType.Miner, JobSlots = 4, OutputsPerWorker = new[] { R(ResourceType.Ore, 0.07f) },
                Cost = new[] { R(ResourceType.Wood, 25f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Smelter, Name = "Smelter", Category = BuildingCategory.Industry,
                Job = JobType.Smith, JobSlots = 2, InputsPerWorker = new[] { R(ResourceType.Ore, 0.1f) },
                OutputsPerWorker = new[] { R(ResourceType.Metal, 0.05f) }, Cost = new[] { R(ResourceType.Wood, 20f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage,
                StorageCapacity = 100f, Cost = new[] { R(ResourceType.Wood, 20f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Castle, Name = "Castle", Category = BuildingCategory.Military, Width = 3, Height = 3,
                MaxPerSettlement = 1, Cost = new[] { R(ResourceType.Stone, 300f), R(ResourceType.Wood, 20f) }, BuildWork = 400f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Temple, Name = "Temple", Category = BuildingCategory.Religion, Width = 2, Height = 2,
                Cost = new[] { R(ResourceType.Stone, 120f), R(ResourceType.Wood, 10f) }, BuildWork = 120f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Cathedral, Name = "Cathedral", Category = BuildingCategory.Religion, Width = 2, Height = 2,
                Cost = new[] { R(ResourceType.Stone, 140f), R(ResourceType.Wood, 40f) }, BuildWork = 300f,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Barracks, Name = "Barracks", Category = BuildingCategory.Military, Width = 2, Height = 2,
                Cost = new[] { R(ResourceType.Metal, 40f) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5,
                Cost = new[] { R(ResourceType.Wood, 10f) },
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static SimContext MakeWorld(out Settlement s, out Civilization civ, bool stoneDeposit = true)
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out civ);
            if (stoneDeposit)
            {
                var map = ctx.State.Map;
                map.Set(map.Index(36, 32), TileFlags.StoneDeposit);
                map.Set(map.Index(37, 32), TileFlags.StoneDeposit);
            }
            return ctx;
        }

        private static Building Place(SimContext ctx, Settlement s, BuildingType type, bool paid = false)
        {
            var def = ctx.Db.Building(type);
            var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 20);
            Assert.IsTrue(site.IsValid, "no free site for " + type);
            var b = ctx.Ops.CreateBuilding(s, type, site);
            Assert.IsNotNull(b);
            b.CostPaid = paid;
            return b;
        }

        private static void Run(LogisticsAdvisor adv, SimContext ctx)
        {
            ctx.DeltaTicks = adv.TickInterval;
            adv.Tick(ctx);
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        [Test]
        public void IdentityAndInterval()
        {
            var adv = new LogisticsAdvisor();
            Assert.AreEqual("logistics", adv.AdvisorId);
            Assert.AreEqual(12, adv.TickInterval);
        }

        [Test]
        public void UnpaidSiteCostExceedingStock_VotesBottleneckProducer()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.SetRaw(ResourceType.Stone, 10f);
            s.Stockpile.SetRaw(ResourceType.Wood, 100f);
            Place(ctx, s, BuildingType.Temple);
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.StoneQuarry), Is.GreaterThanOrEqualTo(0.5f * Full()));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.WoodcutterCamp), 1e-6, "wood is in stock; no wood producer needed");
            Assert.That(AdvisorBoard.JobBias(s, JobType.Quarrier), Is.GreaterThanOrEqualTo(0f));
            Assert.That(AdvisorBoard.Urgency(s, "logistics"), Is.GreaterThan(0.3f));
            Assert.AreEqual((float)(int)ResourceType.Stone, s.Mem(LogisticsKeys.Bottleneck), 1e-6);
            Assert.IsTrue(LogContains(s, "1 site waiting for stone"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void ProducerVote_RespectsPlacementRequirements()
        {
            var ctx = MakeWorld(out var s, out var civ, stoneDeposit: false);
            s.Stockpile.SetRaw(ResourceType.Stone, 0f);
            Place(ctx, s, BuildingType.Temple);
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.StoneQuarry), 1e-6, "no stone deposit: a quarry cannot be placed");
        }

        [Test]
        public void IncomeArrivingSoon_DoesNotVoteProducers()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.Add(ResourceType.Stone, 5f);
            s.Stockpile.RollStatistics(1f); // income 5 stone/day
            s.Stockpile.SetRaw(ResourceType.Stone, 100f);
            Place(ctx, s, BuildingType.Temple); // needs 120: affordable in ~4 days
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.StoneQuarry), Is.LessThan(0.15f * Full()));
        }

        [Test]
        public void CostExceedingCapacity_GivesStrongStorageVote()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.Capacity = 150f;
            s.Stockpile.SetRaw(ResourceType.Stone, 150f);
            s.Stockpile.SetRaw(ResourceType.Wood, 100f);
            Place(ctx, s, BuildingType.Castle);
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Storehouse), Is.GreaterThanOrEqualTo(0.9f * Full()));
            Assert.IsTrue(LogContains(s, "Castle needs 300 stone, storage holds 150"), string.Join("\n", s.AiLog));
            Assert.AreEqual(1f, s.Mem(LogisticsKeys.HardStall), 1e-6);
            Assert.That(AdvisorBoard.Urgency(s, "logistics"), Is.GreaterThanOrEqualTo(0.9f));
        }

        [Test]
        public void QueuedOrderExceedingCapacity_IsAnticipated()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.Capacity = 150f;
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Castle, Priority = 1f });
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Storehouse), Is.GreaterThanOrEqualTo(0.9f * Full()));
        }

        [Test]
        public void ManyStalledSites_VoteAgainstBigProjects()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.SetRaw(ResourceType.Stone, 0f);
            s.Stockpile.SetRaw(ResourceType.Wood, 100f);
            Place(ctx, s, BuildingType.Temple);
            Place(ctx, s, BuildingType.Temple);
            Place(ctx, s, BuildingType.Temple);
            s.Memory[LogisticsKeys.SitesWaiting] = 3f;
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Cathedral), Is.LessThanOrEqualTo(-0.5f * Full()));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.StoneQuarry), Is.GreaterThan(0.5f * Full()), "the cure is never restrained");
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.House), Is.GreaterThanOrEqualTo(0f), "small builds are not restrained");
            Assert.IsTrue(LogContains(s, "3 sites waiting for stone"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void TwoStalledSites_DoNotRestrainBigProjects()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.SetRaw(ResourceType.Stone, 0f);
            Place(ctx, s, BuildingType.Temple);
            Place(ctx, s, BuildingType.Temple);
            s.Memory[LogisticsKeys.SitesWaiting] = 2f;
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Cathedral), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void PaidSites_GiveBuilderVotesProportionalToSites()
        {
            var ctxA = MakeWorld(out var a, out _);
            Place(ctxA, a, BuildingType.House, paid: true);
            var ctxB = MakeWorld(out var b, out _);
            for (int i = 0; i < 4; i++) Place(ctxB, b, BuildingType.House, paid: true);
            Run(new LogisticsAdvisor(), ctxA);
            Run(new LogisticsAdvisor(), ctxB);
            float one = AdvisorBoard.JobBias(a, JobType.Builder);
            float four = AdvisorBoard.JobBias(b, JobType.Builder);
            Assert.That(one, Is.GreaterThan(0f));
            Assert.That(four, Is.GreaterThan(one * 2f));
        }

        [Test]
        public void MaterialChain_VotesUpstreamProducers()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.SetRaw(ResourceType.Metal, 0f);
            s.Stockpile.SetRaw(ResourceType.Ore, 0f);
            s.Stockpile.SetRaw(ResourceType.Wood, 100f);
            Place(ctx, s, BuildingType.Barracks);
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Smelter), Is.GreaterThanOrEqualTo(0.5f * Full()));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Mine), Is.GreaterThan(0.2f * Full()), "smelter needs ore -> mine");
        }

        [Test]
        public void ExistingIdleProducer_IsStaffedBeforeBuildingMore()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.SetRaw(ResourceType.Stone, 0f);
            var q = Place(ctx, s, BuildingType.StoneQuarry);
            ctx.Ops.CompleteBuilding(q); // complete, no workers
            Place(ctx, s, BuildingType.Temple);
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.That(AdvisorBoard.JobBias(s, JobType.Quarrier), Is.GreaterThan(0.5f * Full()));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.StoneQuarry), Is.LessThan(0.5f * Full()));
        }

        [Test]
        public void NoConstruction_NoVotesAndZeroUrgency()
        {
            var ctx = MakeWorld(out var s, out var civ);
            var adv = new LogisticsAdvisor();
            Run(adv, ctx);
            Assert.AreEqual(0f, AdvisorBoard.Urgency(s, "logistics"), 1e-6);
            for (int t = 0; t < Counts.Buildings; t++) Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, (BuildingType)t), 1e-6);
            foreach (var kv in s.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        [Test]
        public void Deterministic_SameWorldSameVotes()
        {
            string a = Simulate(), b = Simulate();
            Assert.AreEqual(a, b);
            StringAssert.Contains("ai.bias", a);
        }

        private static string Simulate()
        {
            var ctx = MakeWorld(out var s, out var civ);
            s.Stockpile.SetRaw(ResourceType.Stone, 5f);
            s.Stockpile.SetRaw(ResourceType.Metal, 0f);
            Place(ctx, s, BuildingType.Temple);
            Place(ctx, s, BuildingType.Barracks);
            Place(ctx, s, BuildingType.Temple);
            Place(ctx, s, BuildingType.House, paid: true);
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Castle, Priority = 1f });
            s.Memory[LogisticsKeys.SitesWaiting] = 3f;
            var adv = new LogisticsAdvisor();
            TestWorlds.Run(adv, ctx, 48);
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                float v = s.Memory[keys[i]];
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), keys[i]);
                sb.Append(keys[i]).Append('=').Append(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            for (int i = 0; i < s.AiLog.Count; i++) sb.Append('|').Append(s.AiLog[i]);
            return sb.ToString();
        }
    }
}
