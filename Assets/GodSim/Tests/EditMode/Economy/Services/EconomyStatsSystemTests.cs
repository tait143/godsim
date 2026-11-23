using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy
{
    public class EconomyStatsSystemTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void SystemMetadata_MatchesContract()
        {
            Assert.AreEqual(1, new EconomyStatsSystem().TickInterval);
        }

        [Test]
        public void Tick_RollsStatisticsAndWritesFoodKeys()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            var sys = new EconomyStatsSystem();
            EconServicesTestKit.TickOnce(sys, ctx); // folds the founding deposit
            float incomeBefore = s.Stockpile.AvgIncome[(int)ResourceType.Food];

            s.Stockpile.SetRaw(ResourceType.Food, 50f);
            s.Stockpile.ConsumeUpTo(ResourceType.Food, 10f);
            EconServicesTestKit.TickOnce(sys, ctx);

            float expense = 10f * 0.05f;
            float income = incomeBefore * 0.95f;
            Assert.AreEqual(expense, s.Stockpile.AvgExpense[(int)ResourceType.Food], 1e-5f);
            Assert.AreEqual(40f / expense, s.Mem(EconomyServiceKeys.FoodDays), Eps);
            Assert.AreEqual(income - expense, s.Mem(EconomyServiceKeys.NetFood), Eps);
        }

        [Test]
        public void FoodDays_CappedWhenNoConsumption()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            EconServicesTestKit.TickOnce(new EconomyStatsSystem(), ctx);
            Assert.AreEqual(9999f, s.Mem(EconomyServiceKeys.FoodDays), Eps);
        }

        [Test]
        public void FoodDays_ZeroFood_IsZero()
        {
            Assert.AreEqual(0f, EconomyStatsSystem.FoodDays(0f, 0f), Eps);
            Assert.AreEqual(0f, EconomyStatsSystem.FoodDays(float.NaN, 1f), Eps);
            Assert.AreEqual(9999f, EconomyStatsSystem.FoodDays(100f, float.NaN), Eps);
            Assert.AreEqual(20f, EconomyStatsSystem.FoodDays(10f, 0.5f), Eps);
        }

        [Test]
        public void Tick_SkipsDeadSettlements()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out _);
            s.IsAlive = false;
            EconServicesTestKit.TickOnce(new EconomyStatsSystem(), ctx);
            Assert.IsFalse(s.Memory.ContainsKey(EconomyServiceKeys.FoodDays));
        }

        [Test]
        public void Module_CreatesAllFiveSystemsWithContractIntervals()
        {
            var list = new System.Collections.Generic.List<ISimSystem>(EconomyServicesModule.CreateSystems());
            Assert.AreEqual(5, list.Count);
            Assert.IsTrue(list[0] is StorageSystem);
            Assert.IsTrue(list[1] is ServiceSystem);
            Assert.IsTrue(list[2] is UpkeepSystem);
            Assert.IsTrue(list[3] is TradeSystem);
            Assert.IsTrue(list[4] is EconomyStatsSystem);
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    Assert.IsTrue(list[i].Name != list[j].Name, "duplicate system name " + list[i].Name);
        }
    }
}
