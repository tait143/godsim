using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy
{
    public class UpkeepSystemTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void SystemMetadata_MatchesContract()
        {
            Assert.AreEqual(24, new UpkeepSystem().TickInterval);
        }

        [Test]
        public void Upkeep_FullyPaid_NoShortfallNoDecay()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            var store = EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            var temple = EconServicesTestKit.Place(ctx, s, BuildingType.Temple);
            s.Stockpile.SetRaw(ResourceType.Gold, 100f);

            EconServicesTestKit.TickOnce(new UpkeepSystem(), ctx);

            float due = (EconServicesTestKit.StorehouseUpkeep + EconServicesTestKit.TempleUpkeep) * 24f;
            Assert.AreEqual(100f - due, s.Stockpile.Get(ResourceType.Gold), Eps);
            Assert.AreEqual(0f, s.Mem(EconomyServiceKeys.UpkeepShortfall, -1f), Eps);
            Assert.AreEqual(due, s.Mem(EconomyServiceKeys.UpkeepDue), Eps);
            Assert.AreEqual(1f, store.Condition, Eps);
            Assert.AreEqual(1f, temple.Condition, Eps);
        }

        [Test]
        public void Upkeep_Shortfall_WritesRatioAndDecaysOnlyBuildingsWithUpkeep()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            var store = EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            var temple = EconServicesTestKit.Place(ctx, s, BuildingType.Temple);
            var well = EconServicesTestKit.Place(ctx, s, BuildingType.Well);
            float due = (EconServicesTestKit.StorehouseUpkeep + EconServicesTestKit.TempleUpkeep) * 24f;
            s.Stockpile.SetRaw(ResourceType.Gold, due * 0.5f);

            EconServicesTestKit.TickOnce(new UpkeepSystem(), ctx);

            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Gold), Eps);
            Assert.AreEqual(0.5f, s.Mem(EconomyServiceKeys.UpkeepShortfall), Eps);
            Assert.AreEqual(1f - 0.01f * 0.5f, store.Condition, 1e-5f);
            Assert.AreEqual(1f - 0.01f * 0.5f, temple.Condition, 1e-5f);
            Assert.AreEqual(1f, well.Condition, 1e-6f);
        }

        [Test]
        public void Upkeep_NoGoldAtAll_FullShortfall_ConditionNeverNegative()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            var store = EconServicesTestKit.Place(ctx, s, BuildingType.Storehouse);
            s.Stockpile.SetRaw(ResourceType.Gold, 0f);
            store.Condition = 0.004f;

            EconServicesTestKit.TickOnce(new UpkeepSystem(), ctx);

            Assert.AreEqual(1f, s.Mem(EconomyServiceKeys.UpkeepShortfall), Eps);
            Assert.AreEqual(0f, store.Condition, 1e-6f);
        }

        [Test]
        public void Upkeep_IgnoresIncompleteBuildings_AndWritesZeroWhenNothingIsDue()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            EconServicesTestKit.Place(ctx, s, BuildingType.Market, complete: false);
            s.Stockpile.SetRaw(ResourceType.Gold, 5f);

            EconServicesTestKit.TickOnce(new UpkeepSystem(), ctx);

            Assert.AreEqual(5f, s.Stockpile.Get(ResourceType.Gold), Eps);
            Assert.IsTrue(s.Memory.ContainsKey(EconomyServiceKeys.UpkeepShortfall));
            Assert.AreEqual(0f, s.Mem(EconomyServiceKeys.UpkeepShortfall), Eps);
        }

        [Test]
        public void Upkeep_ScalesWithDeltaTicks()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            EconServicesTestKit.Place(ctx, s, BuildingType.Market);
            s.Stockpile.SetRaw(ResourceType.Gold, 100f);

            EconServicesTestKit.TickOnce(new UpkeepSystem(), ctx, 12);

            Assert.AreEqual(100f - EconServicesTestKit.MarketUpkeep * 12f, s.Stockpile.Get(ResourceType.Gold), Eps);
        }

        [Test]
        public void Upkeep_SkipsDeadSettlements()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            EconServicesTestKit.Place(ctx, s, BuildingType.Market);
            s.Stockpile.SetRaw(ResourceType.Gold, 50f);
            s.IsAlive = false;

            EconServicesTestKit.TickOnce(new UpkeepSystem(), ctx);

            Assert.AreEqual(50f, s.Stockpile.Get(ResourceType.Gold), Eps);
            Assert.IsFalse(s.Memory.ContainsKey(EconomyServiceKeys.UpkeepShortfall));
        }
    }
}
