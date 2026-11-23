using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Advisors.Specialize
{
    /// <summary>Minimal, realistic building definitions and terrain painting for the specialization advisor tests.</summary>
    internal static class SpecializeTestKit
    {
        public static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);
        public static ServiceAmount S(ServiceType t, float a) => new ServiceAmount(t, a);

        public static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, MinAge = AgeId.Bronze, Width = 3, Height = 3,
                Job = JobType.Farmer, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Food, 0.8f) }, Placement = PlacementRule.Fertile | PlacementRule.Outskirts,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource, MinAge = AgeId.Bronze, Width = 2, Height = 2,
                Job = JobType.Miner, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Ore, 0.5f) }, Placement = PlacementRule.NearOre,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.WoodcutterCamp, Name = "Woodcutter Camp", Category = BuildingCategory.Resource, MinAge = AgeId.Dawn,
                Job = JobType.Woodcutter, JobSlots = 2, OutputsPerWorker = new[] { R(ResourceType.Wood, 0.6f) }, Placement = PlacementRule.NearForest,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Market, Name = "Market", Category = BuildingCategory.Commerce, MinAge = AgeId.Iron, Width = 2, Height = 2,
                Job = JobType.Merchant, JobSlots = 2, Services = new[] { S(ServiceType.Commerce, 3f) }, Placement = PlacementRule.Central,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Harbor, Name = "Harbor", Category = BuildingCategory.Commerce, MinAge = AgeId.Classical,
                Job = JobType.Merchant, JobSlots = 2, Services = new[] { S(ServiceType.Commerce, 4f) }, Placement = PlacementRule.Coastal,
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        /// <summary>Paints a square of terrain around a center: fertility, optional fertile soil, ore every k-th tile, biome.</summary>
        public static void Paint(SimContext ctx, Vec2i c, float fert, bool soil, int oreEvery, Biome biome = Biome.Grassland, int r = 12)
        {
            var map = ctx.State.Map;
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    map.Fertility[i] = fert;
                    map.Biomes[i] = biome;
                    map.Clear(i, TileFlags.FertileSoil | TileFlags.OreDeposit);
                    if (soil) map.Set(i, TileFlags.FertileSoil);
                    if (oreEvery > 0 && (x + y * 3) % oreEvery == 0) map.Set(i, TileFlags.OreDeposit);
                }
        }

        public static Settlement Found(SimContext ctx, Civilization civ, Vec2i at, int adults)
        {
            var s = ctx.Ops.FoundSettlement(civ, at);
            for (int i = 0; i < adults; i++) ctx.Ops.CreatePerson(s, civ.Race, i % 2 == 0 ? Sex.Female : Sex.Male, 20f + i % 10);
            return s;
        }

        /// <summary>Fertile capital A (x=20) and an ore-rich, poor-soil town B (x=60), 40 tiles apart (in trade range).</summary>
        public static SimContext TwoTowns(out Civilization civ, out Settlement a, out Settlement b, AgeId age = AgeId.Bronze)
        {
            var ctx = TestWorlds.CreateContext(CreateDb(), 128, 7);
            Paint(ctx, new Vec2i(20, 40), 0.95f, true, 0);
            Paint(ctx, new Vec2i(60, 40), 0.5f, false, 3, Biome.Hills);
            a = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 40), 10, out civ);
            civ.Age = age;
            b = Found(ctx, civ, new Vec2i(60, 40), 10);
            return ctx;
        }

        public static string Dump(SimContext ctx)
        {
            var sb = new StringBuilder();
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                var keys = new List<string>(s.Memory.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                sb.Append(s.Id).Append(':');
                foreach (var k in keys) sb.Append(k).Append('=').Append(s.Memory[k].ToString("R")).Append(';');
                foreach (var l in s.AiLog) sb.Append('|').Append(l);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static void AssertNoNaN(SimContext ctx)
        {
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
                foreach (var kv in items[i].Memory)
                    Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key + " is not finite");
        }
    }

    public class TradeSpecializationAdvisorTests
    {
        private static void RunOnce(TradeSpecializationAdvisor adv, SimContext ctx, bool init = false) => TestWorlds.Run(adv, ctx, adv.TickInterval, init);

        [Test]
        public void Identity()
        {
            var adv = new TradeSpecializationAdvisor();
            Assert.AreEqual("specialize", adv.AdvisorId);
            Assert.AreEqual(48, adv.TickInterval);
            Assert.AreEqual("AI.Advisor.specialize", adv.Name);
        }

        [Test]
        public void Catalog_IsDataDriven()
        {
            var cat = new SpecializeBuildingCatalog();
            cat.Build(SpecializeTestKit.CreateDb());
            Assert.AreEqual(SpecializeRole.Breadbasket, cat.RoleOf[(int)BuildingType.Farm]);
            Assert.AreEqual(SpecializeRole.MiningTown, cat.RoleOf[(int)BuildingType.Mine]);
            Assert.AreEqual(SpecializeRole.TimberTown, cat.RoleOf[(int)BuildingType.WoodcutterCamp]);
            Assert.AreEqual(SpecializeRole.MarketTown, cat.RoleOf[(int)BuildingType.Market]);
            Assert.AreEqual(SpecializeRole.None, cat.RoleOf[(int)BuildingType.TownCenter]);
            Assert.AreEqual(SpecializeRole.None, cat.RoleOf[(int)BuildingType.Library], "stub with no outputs/services serves no role");
            Assert.AreEqual(BuildingType.Harbor, cat.At(SpecializeRole.MarketTown, 0), "higher commerce output ranks first");
        }

        [Test]
        public void DifferentTerrains_GetComplementaryRoles()
        {
            var ctx = SpecializeTestKit.TwoTowns(out var civ, out var a, out var b);
            var adv = new TradeSpecializationAdvisor();
            RunOnce(adv, ctx, true);

            Assert.AreEqual(SpecializeRole.Breadbasket, SpecializeRoles.Of(a));
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(b));
            Assert.IsTrue(a.Mem(SpecializeRoles.ConfidenceKey) > 0.5f);
            Assert.AreEqual(1f, a.Mem(SpecializeRoles.FeedsKey));
            Assert.IsTrue(AdvisorBoard.Urgency(a, "specialize") > 0f);
            SpecializeTestKit.AssertNoNaN(ctx);
        }

        [Test]
        public void SpecialtyVotes_AndSiblingNegatives()
        {
            var ctx = SpecializeTestKit.TwoTowns(out var civ, out var a, out var b);
            var adv = new TradeSpecializationAdvisor();
            RunOnce(adv, ctx, true);

            Assert.IsTrue(AdvisorBoard.BuildBias(a, BuildingType.Farm) >= 0.5f, "breadbasket pushes farms");
            Assert.IsTrue(AdvisorBoard.BuildBias(b, BuildingType.Mine) >= 0.5f, "mining town pushes mines");
            Assert.IsTrue(AdvisorBoard.BuildBias(b, BuildingType.Farm) < 0f, "mining town gently avoids farms: the breadbasket is far better");
            Assert.IsTrue(AdvisorBoard.BuildBias(b, BuildingType.Farm) > -0.5f, "negatives are gentle");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(a, BuildingType.Mine), "no ore near A: never vote an unplaceable building");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(a, BuildingType.Market), "Market is not available in the Bronze age");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(a, BuildingType.WoodcutterCamp), "no forest: timber is not viable");

            bool reason = false;
            foreach (var line in a.AiLog) if (line.Contains("Specialty: breadbasket of the ") && line.Contains("feeds 1 town")) reason = true;
            Assert.IsTrue(reason, "explainable reason logged: " + string.Join(" / ", a.AiLog));
        }

        [Test]
        public void CriticalLocalFoodNeed_SuppressesNegative()
        {
            var ctx = SpecializeTestKit.TwoTowns(out var civ, out var a, out var b);
            AdvisorBoard.ReportUrgency(b, "food", 0.9f);
            var adv = new TradeSpecializationAdvisor();
            RunOnce(adv, ctx, true);
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(b));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(b, BuildingType.Farm), "a hungry town is never told to avoid farms");
        }

        [Test]
        public void Roles_AreStable_WithHysteresis_ButAdaptAfterTenure()
        {
            var ctx = TestWorlds.CreateContext(SpecializeTestKit.CreateDb(), 128, 3);
            var pa = new Vec2i(20, 40); var pb = new Vec2i(60, 40);
            SpecializeTestKit.Paint(ctx, pa, 0.9f, true, 6);
            SpecializeTestKit.Paint(ctx, pb, 0.55f, false, 2);
            var a = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, pa, 10, out var civ);
            civ.Age = AgeId.Bronze;
            var b = SpecializeTestKit.Found(ctx, civ, pb, 10);
            var adv = new TradeSpecializationAdvisor();

            RunOnce(adv, ctx, true);
            Assert.AreEqual(SpecializeRole.Breadbasket, SpecializeRoles.Of(a));
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(b));
            float since = a.Mem(SpecializeRoles.SinceKey);

            TestWorlds.Run(adv, ctx, adv.TickInterval * 20, false);
            Assert.AreEqual(SpecializeRole.Breadbasket, SpecializeRoles.Of(a));
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(b));
            Assert.AreEqual(since, a.Mem(SpecializeRoles.SinceKey), "role was not reassigned");

            // swap the terrains: a fresh assignment would swap roles, but tenure keeps them
            SpecializeTestKit.Paint(ctx, pa, 0.55f, false, 2);
            SpecializeTestKit.Paint(ctx, pb, 0.9f, true, 6);
            RunOnce(adv, ctx);
            Assert.AreEqual(SpecializeRole.Breadbasket, SpecializeRoles.Of(a), "hysteresis within tenure");
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(b), "hysteresis within tenure");

            // decades later the persistent, clear advantage wins: the economy adapts
            ctx.State.Tick += TradeSpecializationAdvisor.MinTenureTicks;
            RunOnce(adv, ctx);
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(a));
            Assert.AreEqual(SpecializeRole.Breadbasket, SpecializeRoles.Of(b));
            SpecializeTestKit.AssertNoNaN(ctx);
        }

        [Test]
        public void UnviableRole_IsDroppedImmediately()
        {
            var ctx = SpecializeTestKit.TwoTowns(out var civ, out var a, out var b);
            var adv = new TradeSpecializationAdvisor();
            RunOnce(adv, ctx, true);
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(b));
            SpecializeTestKit.Paint(ctx, new Vec2i(60, 40), 0.5f, false, 0, Biome.Hills); // deposits exhausted
            AdvisorBoard.Decay(b, 0f); // the council would have faded the old votes
            RunOnce(adv, ctx);
            Assert.IsTrue(SpecializeRoles.Of(b) != SpecializeRole.MiningTown);
            Assert.IsTrue(AdvisorBoard.BuildBias(b, BuildingType.Mine) <= 0f, "no more mine votes once the ore is gone");
        }

        [Test]
        public void TradeHub_GetsCommerce()
        {
            var ctx = TestWorlds.CreateContext(SpecializeTestKit.CreateDb(), 128, 11);
            var pa = new Vec2i(10, 40); var pc = new Vec2i(45, 40); var pb = new Vec2i(80, 40);
            SpecializeTestKit.Paint(ctx, pa, 0.95f, true, 0);
            SpecializeTestKit.Paint(ctx, pb, 0.5f, false, 3, Biome.Hills);
            var a = TestWorlds.AddSettlement(ctx, RaceId.Human, pa, 10, out var civ);
            civ.Age = AgeId.Iron;
            var c = SpecializeTestKit.Found(ctx, civ, pc, 10);
            var b = SpecializeTestKit.Found(ctx, civ, pb, 10);
            var adv = new TradeSpecializationAdvisor();
            RunOnce(adv, ctx, true);

            Assert.AreEqual((float)c.Id, civ.Mem(SpecializeRoles.CivHubKey), "the central town is the trade hub");
            Assert.AreEqual(1f, c.Mem(SpecializeRoles.HubKey));
            Assert.IsTrue(AdvisorBoard.BuildBias(c, BuildingType.Market) > 0.5f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(c, BuildingType.Harbor), "Harbor is not available (and not coastal)");
            Assert.AreEqual(SpecializeRole.Breadbasket, SpecializeRoles.Of(a));
            Assert.AreEqual(SpecializeRole.MiningTown, SpecializeRoles.Of(b));
            Assert.AreEqual(2f, a.Mem(SpecializeRoles.FeedsKey));
            SpecializeTestKit.AssertNoNaN(ctx);
        }

        [Test]
        public void SingleSettlementCiv_IsUnchanged()
        {
            var ctx = TestWorlds.CreateContext(SpecializeTestKit.CreateDb(), 64, 5);
            SpecializeTestKit.Paint(ctx, new Vec2i(30, 30), 0.95f, true, 3);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(30, 30), 12, out var civ);
            civ.Age = AgeId.Iron;
            string before = SpecializeTestKit.Dump(ctx);
            int civMem = civ.Memory.Count, queue = s.BuildQueue.Count;
            var adv = new TradeSpecializationAdvisor();
            TestWorlds.Run(adv, ctx, adv.TickInterval * 5, true);
            Assert.AreEqual(before, SpecializeTestKit.Dump(ctx));
            Assert.AreEqual(civMem, civ.Memory.Count);
            Assert.AreEqual(queue, s.BuildQueue.Count);
        }

        [Test]
        public void OutOfTradeRange_NoSpecialization()
        {
            var ctx = TestWorlds.CreateContext(SpecializeTestKit.CreateDb(), 160, 5);
            SpecializeTestKit.Paint(ctx, new Vec2i(15, 15), 0.95f, true, 0);
            SpecializeTestKit.Paint(ctx, new Vec2i(140, 140), 0.5f, false, 3, Biome.Hills);
            var a = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(15, 15), 10, out var civ);
            civ.Age = AgeId.Bronze;
            var b = SpecializeTestKit.Found(ctx, civ, new Vec2i(140, 140), 10);
            var adv = new TradeSpecializationAdvisor();
            RunOnce(adv, ctx, true);
            Assert.AreEqual(SpecializeRole.None, SpecializeRoles.Of(a));
            Assert.AreEqual(SpecializeRole.None, SpecializeRoles.Of(b));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(a, BuildingType.Farm));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(b, BuildingType.Farm));
        }

        [Test]
        public void SettlementLoss_ClearsRoles()
        {
            var ctx = SpecializeTestKit.TwoTowns(out var civ, out var a, out var b);
            var adv = new TradeSpecializationAdvisor();
            RunOnce(adv, ctx, true);
            Assert.AreEqual(SpecializeRole.Breadbasket, SpecializeRoles.Of(a));
            ctx.Ops.DestroySettlement(b, DestroyCause.War, true);
            ctx.Ops.FlushRemovals();
            RunOnce(adv, ctx);
            Assert.AreEqual(SpecializeRole.None, SpecializeRoles.Of(a));
            Assert.IsFalse(a.Memory.ContainsKey(SpecializeRoles.RoleKey));
        }

        [Test]
        public void Deterministic()
        {
            string Run()
            {
                var ctx = TestWorlds.CreateContext(SpecializeTestKit.CreateDb(), 128, 21);
                SpecializeTestKit.Paint(ctx, new Vec2i(10, 40), 0.95f, true, 0);
                SpecializeTestKit.Paint(ctx, new Vec2i(80, 40), 0.5f, false, 3, Biome.Hills);
                TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(10, 40), 9, out var civ);
                civ.Age = AgeId.Iron;
                SpecializeTestKit.Found(ctx, civ, new Vec2i(45, 40), 14);
                SpecializeTestKit.Found(ctx, civ, new Vec2i(80, 40), 7);
                var adv = new TradeSpecializationAdvisor();
                TestWorlds.Run(adv, ctx, adv.TickInterval * 6, true);
                SpecializeTestKit.AssertNoNaN(ctx);
                return SpecializeTestKit.Dump(ctx) + civ.Mem(SpecializeRoles.CivHubKey);
            }
            string r1 = Run(), r2 = Run();
            Assert.AreEqual(r1, r2);
            Assert.IsTrue(r1.Contains("adv.spec.role"));
        }
    }
}
