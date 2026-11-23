using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy
{
    public class ServiceSystemTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void SystemMetadata_MatchesContract()
        {
            Assert.AreEqual(6, new ServiceSystem().TickInterval);
        }

        [Test]
        public void ServiceNeeds_TableMatchesDesign()
        {
            Assert.AreEqual(Counts.Services, ServiceNeeds.PerCapita.Length);
            Assert.AreEqual(0.05f, ServiceNeeds.PerCapita[(int)ServiceType.Faith], 1e-6f);
            Assert.AreEqual(0.05f, ServiceNeeds.PerCapita[(int)ServiceType.Health], 1e-6f);
            Assert.AreEqual(0.03f, ServiceNeeds.PerCapita[(int)ServiceType.Research], 1e-6f);
            Assert.AreEqual(0.05f, ServiceNeeds.PerCapita[(int)ServiceType.Entertainment], 1e-6f);
            Assert.AreEqual(0.04f, ServiceNeeds.PerCapita[(int)ServiceType.Defense], 1e-6f);
            Assert.AreEqual(0.1f, ServiceNeeds.PerCapita[(int)ServiceType.Water], 1e-6f);
            Assert.AreEqual(0.15f, ServiceNeeds.PerCapita[(int)ServiceType.Power], 1e-6f);
            Assert.AreEqual(0.04f, ServiceNeeds.PerCapita[(int)ServiceType.Commerce], 1e-6f);
            Assert.AreEqual(0.04f, ServiceNeeds.PerCapita[(int)ServiceType.Administration], 1e-6f);
        }

        [Test]
        public void Output_ScalesWithStaffing()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 6, out _);
            var temple = EconServicesTestKit.Place(ctx, s, BuildingType.Temple);
            var sys = new ServiceSystem();

            EconServicesTestKit.TickOnce(sys, ctx);
            Assert.AreEqual(0f, s.ServiceOutput[(int)ServiceType.Faith], Eps, "unstaffed");

            EconServicesTestKit.Staff(ctx, s, temple, 1);
            EconServicesTestKit.TickOnce(sys, ctx);
            Assert.AreEqual(EconServicesTestKit.TempleFaith * 0.5f, s.ServiceOutput[(int)ServiceType.Faith], Eps, "half staffed");
            Assert.AreEqual(0.5f, temple.Efficiency, Eps);

            EconServicesTestKit.Staff(ctx, s, temple, 1);
            EconServicesTestKit.TickOnce(sys, ctx);
            Assert.AreEqual(EconServicesTestKit.TempleFaith, s.ServiceOutput[(int)ServiceType.Faith], Eps, "fully staffed");
        }

        [Test]
        public void Output_ScalesWithCondition_AndJoblessBuildingsRunAtFullRate()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 6, out _);
            var well = EconServicesTestKit.Place(ctx, s, BuildingType.Well);
            EconServicesTestKit.TickOnce(new ServiceSystem(), ctx);
            Assert.AreEqual(EconServicesTestKit.WellWater, s.ServiceOutput[(int)ServiceType.Water], Eps);

            well.Condition = 0.5f;
            EconServicesTestKit.TickOnce(new ServiceSystem(), ctx);
            Assert.AreEqual(EconServicesTestKit.WellWater * 0.5f, s.ServiceOutput[(int)ServiceType.Water], Eps);
        }

        [Test]
        public void Output_IgnoresIncompleteAndDestroyedBuildings()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 6, out _);
            EconServicesTestKit.Place(ctx, s, BuildingType.Well, complete: false);
            var flagged = EconServicesTestKit.Place(ctx, s, BuildingType.Well);
            flagged.IsDestroyed = true;
            ctx.DeltaTicks = 6;
            new ServiceSystem().Tick(ctx);
            Assert.AreEqual(0f, s.ServiceOutput[(int)ServiceType.Water], Eps);
        }

        [Test]
        public void Output_AppliesServiceAndFaithModifiers()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(30, 30), 6, out var civ);
            var temple = EconServicesTestKit.Place(ctx, s, BuildingType.Temple);
            EconServicesTestKit.Staff(ctx, s, temple, 2);
            EconServicesTestKit.Place(ctx, s, BuildingType.Well);
            civ.Modifiers.Service[(int)ServiceType.Faith] = 2f;
            civ.Modifiers.Faith = 1.5f;
            civ.Modifiers.Service[(int)ServiceType.Water] = 1.2f;

            EconServicesTestKit.TickOnce(new ServiceSystem(), ctx);

            Assert.AreEqual(EconServicesTestKit.TempleFaith * 2f * 1.5f, s.ServiceOutput[(int)ServiceType.Faith], Eps);
            Assert.AreEqual(EconServicesTestKit.WellWater * 1.2f, s.ServiceOutput[(int)ServiceType.Water], Eps);
        }

        [Test]
        public void Defense_IncludesSoldiersTimesMilitary()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(30, 30), 6, out var civ);
            EconServicesTestKit.Place(ctx, s, BuildingType.Palisade);
            s.Soldiers = 4;
            civ.Modifiers.Military = 2f;

            EconServicesTestKit.TickOnce(new ServiceSystem(), ctx);

            Assert.AreEqual(EconServicesTestKit.PalisadeDefense + 4f * 1.5f * 2f, s.ServiceOutput[(int)ServiceType.Defense], Eps);
        }

        [Test]
        public void Coverage_IsOutputOverPopulationNeed_Clamped()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 40, out _);
            var temple = EconServicesTestKit.Place(ctx, s, BuildingType.Temple);
            EconServicesTestKit.Staff(ctx, s, temple, 2);
            EconServicesTestKit.Place(ctx, s, BuildingType.Well);
            EconServicesTestKit.Place(ctx, s, BuildingType.Palisade);
            s.Soldiers = 10;

            EconServicesTestKit.TickOnce(new ServiceSystem(), ctx);

            Assert.AreEqual(3f / (40 * 0.1f), s.ServiceCoverage[(int)ServiceType.Water], Eps);
            Assert.AreEqual(4f / (40 * 0.05f), s.ServiceCoverage[(int)ServiceType.Faith], Eps);
            Assert.AreEqual(0f, s.ServiceCoverage[(int)ServiceType.Research], Eps);
            // Defense: 5 + 10*1.5 = 20 over 40*0.04 = 1.6 -> 12.5, clamped to 3.
            Assert.AreEqual(3f, s.ServiceCoverage[(int)ServiceType.Defense], Eps);
        }

        [Test]
        public void Coverage_ZeroPopulation_UsesUnitDemandAndStaysFinite()
        {
            Assert.AreEqual(0.5f, ServiceSystem.CoverageFor(0.5f, 0, ServiceType.Health), Eps);
            Assert.AreEqual(3f, ServiceSystem.CoverageFor(50f, 0, ServiceType.Health), Eps);
            Assert.AreEqual(0f, ServiceSystem.CoverageFor(float.NaN, 10, ServiceType.Health), Eps);
            // Small population: demand below 1 is floored to 1.
            Assert.AreEqual(0.8f, ServiceSystem.CoverageFor(0.8f, 5, ServiceType.Water), Eps);
        }

        [Test]
        public void Commerce_ProducesGoldScaledByDeltaAndTrade()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 6, out var civ);
            var market = EconServicesTestKit.Place(ctx, s, BuildingType.Market);
            EconServicesTestKit.Staff(ctx, s, market, 2);
            civ.Modifiers.Trade = 1.5f;
            s.Stockpile.SetRaw(ResourceType.Gold, 0f);

            EconServicesTestKit.TickOnce(new ServiceSystem(), ctx);

            float expected = EconServicesTestKit.MarketCommerce * 0.02f * 6f * 1.5f;
            Assert.AreEqual(expected, s.Stockpile.Get(ResourceType.Gold), Eps);
            Assert.AreEqual(expected, s.Mem(EconomyServiceKeys.CommerceGold), Eps);
        }

        [Test]
        public void Initialize_ComputesOutputsWithoutGold()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 6, out _);
            var market = EconServicesTestKit.Place(ctx, s, BuildingType.Market);
            EconServicesTestKit.Staff(ctx, s, market, 2);
            s.Stockpile.SetRaw(ResourceType.Gold, 0f);
            ctx.DeltaTicks = 6;
            new ServiceSystem().Initialize(ctx);
            Assert.AreEqual(EconServicesTestKit.MarketCommerce, s.ServiceOutput[(int)ServiceType.Commerce], Eps);
            Assert.AreEqual(0f, s.Stockpile.Get(ResourceType.Gold), Eps);
        }

        [Test]
        public void BadData_NeverProducesNaN()
        {
            var ctx = EconServicesTestKit.CreateContext();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 6, out var civ);
            var well = EconServicesTestKit.Place(ctx, s, BuildingType.Well);
            var market = EconServicesTestKit.Place(ctx, s, BuildingType.Market);
            EconServicesTestKit.Staff(ctx, s, market, 2);
            well.Condition = float.NaN;
            civ.Modifiers.Service[(int)ServiceType.Commerce] = float.PositiveInfinity;
            civ.Modifiers.Trade = float.NaN;
            civ.Modifiers.Military = -3f;
            s.Soldiers = -5;

            EconServicesTestKit.TickOnce(new ServiceSystem(), ctx);

            for (int i = 0; i < Counts.Services; i++)
            {
                Assert.IsTrue(EconServicesTestKit.IsFinite(s.ServiceOutput[i]) && s.ServiceOutput[i] >= 0f, "output " + (ServiceType)i);
                Assert.IsTrue(EconServicesTestKit.IsFinite(s.ServiceCoverage[i]) && s.ServiceCoverage[i] >= 0f && s.ServiceCoverage[i] <= 3f, "coverage " + (ServiceType)i);
            }
            Assert.IsTrue(EconServicesTestKit.IsFinite(s.Stockpile.Get(ResourceType.Gold)));
        }
    }
}
