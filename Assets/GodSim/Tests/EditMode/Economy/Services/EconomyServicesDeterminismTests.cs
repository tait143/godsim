using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy
{
    public class EconomyServicesDeterminismTests
    {
        /// <summary>Builds a small multi-civ world exercising every economy service system.</summary>
        private static SimContext BuildWorld(ulong seed)
        {
            var ctx = EconServicesTestKit.CreateContext(128, seed);
            var a1 = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 12, out var civA);
            var a2 = ctx.Ops.FoundSettlement(civA, new Vec2i(60, 25));
            var a3 = ctx.Ops.FoundSettlement(civA, new Vec2i(30, 70));
            var b1 = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(100, 100), 10, out var civB);
            var c1 = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(100, 20), 8, out var civC);

            EconServicesTestKit.Place(ctx, a1, BuildingType.Storehouse);
            var market = EconServicesTestKit.Place(ctx, a1, BuildingType.Market);
            EconServicesTestKit.Staff(ctx, a1, market, 2);
            var temple = EconServicesTestKit.Place(ctx, a1, BuildingType.Temple);
            EconServicesTestKit.Staff(ctx, a1, temple, 1);
            EconServicesTestKit.Place(ctx, a2, BuildingType.Granary);
            EconServicesTestKit.Place(ctx, a3, BuildingType.Well);
            EconServicesTestKit.Place(ctx, b1, BuildingType.Storehouse);
            EconServicesTestKit.Place(ctx, b1, BuildingType.Palisade);
            var bm = EconServicesTestKit.Place(ctx, b1, BuildingType.Market);
            EconServicesTestKit.Staff(ctx, b1, bm, 1);
            EconServicesTestKit.Place(ctx, c1, BuildingType.Temple);
            c1.Soldiers = 3;

            a1.Stockpile.SetRaw(ResourceType.Food, 200f);
            a1.Stockpile.SetRaw(ResourceType.Stone, 180f);
            a2.Stockpile.SetRaw(ResourceType.Wood, 150f);
            b1.Stockpile.SetRaw(ResourceType.Metal, 200f);
            b1.Stockpile.SetRaw(ResourceType.Gold, 3f);

            var rel = ctx.State.Diplomacy.Get(civA.Id, civB.Id);
            rel.HasContact = true;
            rel.TradeAgreement = true;
            var war = ctx.State.Diplomacy.Get(civA.Id, civC.Id);
            war.HasContact = true;
            war.TradeAgreement = true;
            war.WarId = 1;
            return ctx;
        }

        private static List<ISimSystem> Systems() => new List<ISimSystem>(EconomyServicesModule.CreateSystems());

        private static string Snapshot(SimContext ctx)
        {
            var sb = new StringBuilder();
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                sb.Append(s.Id).Append(':').Append(s.Stockpile.Capacity.ToString("R")).Append('|');
                for (int r = 0; r < Counts.Resources; r++) sb.Append(s.Stockpile.Get((ResourceType)r).ToString("R")).Append(',');
                for (int v = 0; v < Counts.Services; v++) sb.Append(s.ServiceOutput[v].ToString("R")).Append('/').Append(s.ServiceCoverage[v].ToString("R")).Append(',');
                var keys = new List<string>(s.Memory.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                foreach (var k in keys) sb.Append(k).Append('=').Append(s.Memory[k].ToString("R")).Append(';');
                sb.Append('\n');
            }
            var bs = ctx.State.Buildings.Items;
            for (int i = 0; i < bs.Count; i++) sb.Append(bs[i].Id).Append('c').Append(bs[i].Condition.ToString("R")).Append(';');
            sb.Append(ctx.State.Chronicle.Entries.Count);
            return sb.ToString();
        }

        [Test]
        public void SameSeed_SameResult()
        {
            var ctx1 = BuildWorld(42);
            var ctx2 = BuildWorld(42);
            TestWorlds.RunAll(Systems(), ctx1, 300);
            TestWorlds.RunAll(Systems(), ctx2, 300);
            Assert.AreEqual(Snapshot(ctx1), Snapshot(ctx2));
        }

        [Test]
        public void LongRun_NoNaNAndStockpilesWithinCapacity()
        {
            var ctx = BuildWorld(9);
            TestWorlds.RunAll(Systems(), ctx, 500);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                Assert.IsTrue(EconServicesTestKit.IsFinite(s.Stockpile.Capacity) && s.Stockpile.Capacity >= 1f);
                for (int r = 0; r < Counts.Resources; r++)
                {
                    float v = s.Stockpile.Get((ResourceType)r);
                    Assert.IsTrue(EconServicesTestKit.IsFinite(v) && v >= 0f && v <= s.Stockpile.Capacity + 1e-3f, s.Name + " " + (ResourceType)r + "=" + v);
                    Assert.IsTrue(EconServicesTestKit.IsFinite(s.Stockpile.AvgIncome[r]) && EconServicesTestKit.IsFinite(s.Stockpile.AvgExpense[r]));
                }
                for (int v = 0; v < Counts.Services; v++)
                    Assert.IsTrue(EconServicesTestKit.IsFinite(s.ServiceOutput[v]) && EconServicesTestKit.IsFinite(s.ServiceCoverage[v]));
                foreach (var kv in s.Memory) Assert.IsTrue(EconServicesTestKit.IsFinite(kv.Value), kv.Key);
                Assert.IsTrue(s.Memory.ContainsKey(EconomyServiceKeys.FoodDays));
                Assert.IsTrue(s.Memory.ContainsKey(EconomyServiceKeys.NetFood));
                Assert.IsTrue(s.Memory.ContainsKey(EconomyServiceKeys.UpkeepShortfall));
            }
            var bs = ctx.State.Buildings.Items;
            for (int i = 0; i < bs.Count; i++) Assert.IsTrue(bs[i].Condition >= 0f && bs[i].Condition <= 1f);
        }

        [Test]
        public void LongRun_OnlyTradingPairIsChronicled()
        {
            var ctx = BuildWorld(3);
            TestWorlds.RunAll(Systems(), ctx, 200);
            int firstTradeKeys = 0;
            var keys = new List<string>(ctx.State.Memory.Keys);
            foreach (var k in keys) if (k.StartsWith(EconomyServiceKeys.FirstTradePrefix, System.StringComparison.Ordinal)) firstTradeKeys++;
            Assert.AreEqual(1, firstTradeKeys, "only the A-B pair (agreement, no war) should trade");
        }

        [Test]
        public void EngineIntegration_RunsWithoutFaults()
        {
            var ctx = BuildWorld(5);
            var engine = new SimulationEngine(ctx);
            engine.AddSystems(EconomyServicesModule.CreateSystems());
            engine.Initialize();
            engine.StepMany(120);
            foreach (var p in engine.Profiles) Assert.AreEqual(0, p.TotalFaults, p.Name + ": " + p.LastError);
        }
    }
}
