using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy
{
    public class TradeSystemTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void SystemMetadata_MatchesContract()
        {
            Assert.AreEqual(24, new TradeSystem().TickInterval);
        }

        private static void SameCivPair(SimContext ctx, Vec2i a, Vec2i b, out Settlement sa, out Settlement sb, out Civilization civ)
        {
            sa = TestWorlds.AddSettlement(ctx, RaceId.Human, a, 4, out civ);
            sb = ctx.Ops.FoundSettlement(civ, b);
            EconServicesTestKit.Empty(sa.Stockpile);
            EconServicesTestKit.Empty(sb.Stockpile);
        }

        [Test]
        public void IntraCiv_MovesSurplusToDeficit_LimitedByThroughput()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            SameCivPair(ctx, new Vec2i(20, 20), new Vec2i(60, 20), out var a, out var b, out _);
            a.Stockpile.SetRaw(ResourceType.Stone, 120f); // 80% of 150
            b.Stockpile.SetRaw(ResourceType.Food, 140f);  // donor in the other direction

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(115f, a.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(5f, b.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(5f, a.Stockpile.Get(ResourceType.Food), Eps);
            Assert.AreEqual(135f, b.Stockpile.Get(ResourceType.Food), Eps);
            Assert.AreEqual(10f, a.Mem(EconomyServiceKeys.TradeExported) + b.Mem(EconomyServiceKeys.TradeExported), Eps);
        }

        [Test]
        public void IntraCiv_CommerceCoverageRaisesThroughput_AndDeltaScales()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            SameCivPair(ctx, new Vec2i(20, 20), new Vec2i(60, 20), out var a, out var b, out _);
            a.Stockpile.SetRaw(ResourceType.Wood, 140f);
            a.ServiceCoverage[(int)ServiceType.Commerce] = 3f; // capped at 2 -> 5 + 6 = 11

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);
            Assert.AreEqual(11f, b.Stockpile.Get(ResourceType.Wood), Eps);

            b.Stockpile.SetRaw(ResourceType.Wood, 0f);
            a.ServiceCoverage[(int)ServiceType.Commerce] = 1f; // 8 per 24 ticks, 4 per 12
            EconServicesTestKit.TickOnce(new TradeSystem(), ctx, 12);
            Assert.AreEqual(4f, b.Stockpile.Get(ResourceType.Wood), Eps);
        }

        [Test]
        public void IntraCiv_NoTransferWhenNotSurplusOrNotDeficit()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            SameCivPair(ctx, new Vec2i(20, 20), new Vec2i(60, 20), out var a, out var b, out _);
            a.Stockpile.SetRaw(ResourceType.Stone, 80f); // 53%: not a donor
            a.Stockpile.SetRaw(ResourceType.Wood, 120f); // donor, but receiver has 40 (26%)
            b.Stockpile.SetRaw(ResourceType.Wood, 40f);

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(80f, a.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(120f, a.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(40f, b.Stockpile.Get(ResourceType.Wood), Eps);
        }

        [Test]
        public void IntraCiv_RequiresRange()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            SameCivPair(ctx, new Vec2i(10, 10), new Vec2i(110, 10), out var a, out var b, out _);
            a.Stockpile.SetRaw(ResourceType.Stone, 120f);

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(120f, a.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Stone), Eps);
        }

        [Test]
        public void IntraCiv_SkipsDeadSettlementsAndOtherCivs()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            SameCivPair(ctx, new Vec2i(20, 20), new Vec2i(60, 20), out var a, out var b, out _);
            var foreign = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(40, 50), 2, out _);
            EconServicesTestKit.Empty(foreign.Stockpile);
            a.Stockpile.SetRaw(ResourceType.Stone, 120f);
            b.IsAlive = false;

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(120f, a.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(0f, foreign.Stockpile.Get(ResourceType.Stone), Eps);
        }

        [Test]
        public void IntraCiv_ReceiverNeverPushedAboveDonorThreshold()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            SameCivPair(ctx, new Vec2i(20, 20), new Vec2i(60, 20), out var a, out var b, out _);
            a.Stockpile.Capacity = 10000f;
            a.Stockpile.SetRaw(ResourceType.Metal, 9000f);
            b.Stockpile.Capacity = 10f;
            b.Stockpile.SetRaw(ResourceType.Metal, 1f);

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(5.5f, b.Stockpile.Get(ResourceType.Metal), Eps);
            Assert.AreEqual(9000f - 4.5f, a.Stockpile.Get(ResourceType.Metal), Eps);
        }

        private static void TwoCivs(SimContext ctx, out Settlement a, out Settlement b, out Civilization civA, out Civilization civB, out Relation rel)
        {
            a = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 4, out civA);
            b = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(100, 100), 4, out civB);
            EconServicesTestKit.Empty(a.Stockpile);
            EconServicesTestKit.Empty(b.Stockpile);
            a.Stockpile.SetRaw(ResourceType.Wood, 140f);
            b.Stockpile.SetRaw(ResourceType.Stone, 140f);
            rel = ctx.State.Diplomacy.Get(civA.Id, civB.Id);
            rel.HasContact = true;
        }

        [Test]
        public void InterCiv_RequiresTradeAgreement()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            TwoCivs(ctx, out var a, out var b, out _, out _, out var rel);
            rel.TradeAgreement = false;

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(140f, a.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(0f, a.Stockpile.Get(ResourceType.Gold), Eps);
        }

        [Test]
        public void InterCiv_RequiresContact()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            TwoCivs(ctx, out var a, out var b, out _, out _, out var rel);
            rel.TradeAgreement = true;
            rel.HasContact = false;

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Wood), Eps);
        }

        [Test]
        public void InterCiv_BlockedByWar()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            TwoCivs(ctx, out var a, out var b, out _, out _, out var rel);
            rel.TradeAgreement = true;
            rel.WarId = 1;

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(140f, a.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Wood), Eps);
        }

        [Test]
        public void InterCiv_BlockedWhenPartnerIsDead()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            TwoCivs(ctx, out var a, out var b, out _, out var civB, out var rel);
            rel.TradeAgreement = true;
            civB.IsAlive = false;

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Wood), Eps);
        }

        [Test]
        public void InterCiv_ExchangesSurplusesEarnsGoldAndChroniclesOnce()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            TwoCivs(ctx, out var a, out var b, out var civA, out var civB, out var rel);
            rel.TradeAgreement = true;
            civA.Modifiers.Trade = 2f;
            int chronicleBefore = ctx.State.Chronicle.Entries.Count;
            var sys = new TradeSystem();

            EconServicesTestKit.TickOnce(sys, ctx);

            Assert.AreEqual(130f, a.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(10f, b.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(130f, b.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(10f, a.Stockpile.Get(ResourceType.Stone), Eps);
            Assert.AreEqual(20f * 0.1f * 2f, a.Stockpile.Get(ResourceType.Gold), Eps);
            Assert.AreEqual(20f * 0.1f * 1f, b.Stockpile.Get(ResourceType.Gold), Eps);
            Assert.AreEqual(4f, civA.Mem(EconomyServiceKeys.TradeIncome), Eps);

            string key = "trade.first." + System.Math.Min(civA.Id, civB.Id) + "." + System.Math.Max(civA.Id, civB.Id);
            Assert.AreEqual(key, EconomyServiceKeys.FirstTradeKey(civB.Id, civA.Id));
            Assert.AreEqual(1f, ctx.State.Memory[key], Eps);
            Assert.AreEqual(chronicleBefore + 1, ctx.State.Chronicle.Entries.Count);

            EconServicesTestKit.TickOnce(sys, ctx);
            Assert.AreEqual(120f, a.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(chronicleBefore + 1, ctx.State.Chronicle.Entries.Count, "first trade chronicled only once");
        }

        [Test]
        public void InterCiv_NoCapital_NoTrade()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            TwoCivs(ctx, out var a, out var b, out var civA, out _, out var rel);
            rel.TradeAgreement = true;
            civA.CapitalId = -1;

            EconServicesTestKit.TickOnce(new TradeSystem(), ctx);

            Assert.AreEqual(0f, b.Stockpile.Get(ResourceType.Wood), Eps);
            Assert.AreEqual(140f, b.Stockpile.Get(ResourceType.Stone), Eps);
        }

        [Test]
        public void PickExport_ExcludesGoldAndRequiresAdvantage()
        {
            var ctx = EconServicesTestKit.CreateContext(128);
            TwoCivs(ctx, out var a, out var b, out _, out _, out _);
            EconServicesTestKit.Empty(a.Stockpile);
            EconServicesTestKit.Empty(b.Stockpile);
            a.Stockpile.SetRaw(ResourceType.Gold, 150f);
            Assert.AreEqual(-1, TradeSystem.PickExport(a, b));

            a.Stockpile.SetRaw(ResourceType.Food, 100f);
            b.Stockpile.SetRaw(ResourceType.Food, 95f);
            Assert.AreEqual(-1, TradeSystem.PickExport(a, b), "advantage too small");

            a.Stockpile.SetRaw(ResourceType.Tools, 90f);
            Assert.AreEqual((int)ResourceType.Tools, TradeSystem.PickExport(a, b));
        }
    }
}
