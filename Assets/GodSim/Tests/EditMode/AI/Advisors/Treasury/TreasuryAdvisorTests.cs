using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class TreasuryAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static BuildingDefinition Def(BuildingType t, BuildingCategory cat, AgeId minAge, float upkeep)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = cat, MinAge = minAge, UpkeepGold = upkeep, BuildWork = 20f,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 40f), new ResourceAmount(ResourceType.Stone, 20f) },
            };
        }

        private static BuildingDefinition Commerce(BuildingType t, AgeId minAge, float commerce, float upkeep, int slots)
        {
            var d = Def(t, BuildingCategory.Commerce, minAge, upkeep);
            d.Job = JobType.Merchant; d.JobSlots = slots;
            d.Services = new[] { new ServiceAmount(ServiceType.Commerce, commerce) };
            d.Placement = PlacementRule.Central;
            return d;
        }

        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            var market = Commerce(BuildingType.Market, AgeId.Iron, 2f, 0.01f, 2); market.MaxPerSettlement = 2; db.Register(market);
            db.Register(Commerce(BuildingType.Guildhall, AgeId.Medieval, 3f, 0.02f, 3));
            db.Register(Commerce(BuildingType.Bank, AgeId.Renaissance, 5f, 0.03f, 3));
            var harbor = Commerce(BuildingType.Harbor, AgeId.Classical, 3f, 0.03f, 3); harbor.Placement = PlacementRule.Coastal; db.Register(harbor);
            var gm = Def(BuildingType.GoldMine, BuildingCategory.Resource, AgeId.Iron, 0.01f);
            gm.Job = JobType.Miner; gm.JobSlots = 3; gm.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Gold, 0.03f) }; gm.Placement = PlacementRule.NearGold;
            db.Register(gm);
            var theater = Def(BuildingType.Theater, BuildingCategory.Culture, AgeId.Classical, 0.08f);
            theater.Services = new[] { new ServiceAmount(ServiceType.Entertainment, 4f) }; theater.Desirability = 0.5f; db.Register(theater);
            var arena = Def(BuildingType.Arena, BuildingCategory.Culture, AgeId.Classical, 0.06f);
            arena.Services = new[] { new ServiceAmount(ServiceType.Entertainment, 5f) }; db.Register(arena);
            var tavern = Def(BuildingType.Tavern, BuildingCategory.Culture, AgeId.Medieval, 0.02f);
            tavern.Services = new[] { new ServiceAmount(ServiceType.Entertainment, 2f) }; db.Register(tavern);
            var barracks = Def(BuildingType.Barracks, BuildingCategory.Military, AgeId.Iron, 0.05f);
            barracks.Job = JobType.Soldier; barracks.JobSlots = 4; barracks.Services = new[] { new ServiceAmount(ServiceType.Defense, 2f) }; db.Register(barracks);
            var palisade = Def(BuildingType.Palisade, BuildingCategory.Military, AgeId.Bronze, 0.01f);
            palisade.Services = new[] { new ServiceAmount(ServiceType.Defense, 1f) }; db.Register(palisade);
            var lib = Def(BuildingType.GreatLibrary, BuildingCategory.Wonder, AgeId.Classical, 0.1f);
            lib.IsWonder = true; lib.UniquePerCivilization = true;
            lib.Cost = new[] { new ResourceAmount(ResourceType.Stone, 400f), new ResourceAmount(ResourceType.Gold, 400f) };
            db.Register(lib);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement MakeTown(SimContext ctx, Vec2i at, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, at, 12, out civ);
            civ.Age = AgeId.Medieval;
            s.Stockpile.Capacity = 10000f;
            return s;
        }

        private static void Place(SimContext ctx, Settlement s, BuildingType t)
        {
            var def = ctx.Db.Building(t);
            var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 12);
            Assert.IsTrue(site.IsValid, "no site for " + t);
            Assert.IsNotNull(ctx.Ops.CreateBuilding(s, t, site, 0, true));
        }

        private static void MakeInsolvent(SimContext ctx, Settlement s)
        {
            Place(ctx, s, BuildingType.Theater);
            Place(ctx, s, BuildingType.Arena);
            Place(ctx, s, BuildingType.Palisade);
            s.Stockpile.SetRaw(ResourceType.Gold, 2f);
            s.Memory[TreasuryKeys.EconUpkeepShortfall] = 0.5f;
        }

        private static float Bias(Settlement s, BuildingType t) => AdvisorBoard.BuildBias(s, t);

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        // ------------------------------------------------------------------ tests
        [Test]
        public void Identity()
        {
            var a = new TreasuryAdvisor();
            Assert.AreEqual("treasury", a.AdvisorId);
            Assert.AreEqual(24, a.TickInterval);
        }

        [Test]
        public void Insolvency_VotesCommerce_AndCutsLuxuries()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            MakeInsolvent(ctx, s);
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);

            Assert.Greater(Bias(s, BuildingType.Guildhall) + Bias(s, BuildingType.Market), 1f, "strong commerce votes");
            Assert.Greater(Bias(s, BuildingType.Market), 0f);
            Assert.AreEqual(0f, Bias(s, BuildingType.Bank), "Bank is Renaissance: never vote unavailable buildings");
            Assert.AreEqual(0f, Bias(s, BuildingType.Harbor), "inland town: no harbor");
            Assert.AreEqual(0f, Bias(s, BuildingType.GoldMine), "no gold deposits: no gold mine");
            Assert.Less(Bias(s, BuildingType.Theater), -0.5f, "upkeep-heavy luxury cut");
            Assert.Less(Bias(s, BuildingType.Tavern), 0f);
            Assert.Less(Bias(s, BuildingType.Barracks), 0f, "extra garrison cut in peacetime");
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Merchant), 0.5f);
            Assert.Greater(AdvisorBoard.Urgency(s, TreasuryAdvisor.Id), 0.8f);
            Assert.AreEqual((float)TreasuryMode.Austerity, s.Mem(TreasuryKeys.Mode));
            Assert.Less(s.Mem(TreasuryKeys.Runway), 1f);
            Assert.IsTrue(LogContains(s, "belt-tightening"), "explainable negative vote");
            Assert.IsTrue(LogContains(s, "need revenue"), "explainable positive vote");
        }

        [Test]
        public void Insolvency_AtWar_SparesMilitary()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            MakeInsolvent(ctx, s);
            ctx.State.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = 999, DefenderId = civ.Id, IsActive = true });
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);
            Assert.GreaterOrEqual(Bias(s, BuildingType.Barracks), 0f);
            Assert.Less(Bias(s, BuildingType.Theater), 0f);
        }

        [Test]
        public void Surplus_NoAusterity_AllowsLuxuries()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            Place(ctx, s, BuildingType.Theater);
            Place(ctx, s, BuildingType.Palisade);
            s.Stockpile.SetRaw(ResourceType.Gold, 5000f);
            s.Stockpile.AvgIncome[(int)ResourceType.Gold] = 1f;
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);

            for (int i = 0; i < Counts.Buildings; i++)
                Assert.GreaterOrEqual(Bias(s, (BuildingType)i), 0f, "no austerity vote for " + (BuildingType)i);
            Assert.Greater(Bias(s, BuildingType.Arena) + Bias(s, BuildingType.Tavern) + Bias(s, BuildingType.Theater), 0f, "patronage of luxuries");
            Assert.AreEqual((float)TreasuryMode.Prosperity, s.Mem(TreasuryKeys.Mode));
            Assert.IsFalse(LogContains(s, "belt-tightening"));
            Assert.Less(AdvisorBoard.Urgency(s, TreasuryAdvisor.Id), 0.2f);
        }

        [Test]
        public void Mercantilism_ScalesPatronage()
        {
            float low = PatronageFor(0.1f), high = PatronageFor(0.9f);
            Assert.Greater(high, low);
        }

        private static float PatronageFor(float merc)
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            AdvisorBoard.SetTrait(civ, AITrait.Mercantilism, merc);
            s.Stockpile.SetRaw(ResourceType.Gold, 5000f);
            s.Stockpile.AvgIncome[(int)ResourceType.Gold] = 1f;
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);
            return Bias(s, BuildingType.Arena) + Bias(s, BuildingType.Tavern) + Bias(s, BuildingType.Theater);
        }

        [Test]
        public void Coastal_PrefersHarbor()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var coast = MakeTown(ctx, new Vec2i(14, 14), out var c1);
            var inland = MakeTown(ctx, new Vec2i(50, 50), out var c2);
            var map = ctx.State.Map;
            for (int x = 10; x < 20; x++) map.Set(map.Index(x, 18), TileFlags.Coastal);
            MakeInsolvent(ctx, coast);
            MakeInsolvent(ctx, inland);
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);

            Assert.Greater(Bias(coast, BuildingType.Harbor), 0.5f);
            Assert.Greater(Bias(coast, BuildingType.Harbor), Bias(coast, BuildingType.Market));
            Assert.GreaterOrEqual(Bias(coast, BuildingType.Harbor), Bias(coast, BuildingType.Guildhall));
            Assert.AreEqual(0f, Bias(inland, BuildingType.Harbor));
            Assert.Greater(Bias(inland, BuildingType.Market), 0f);
        }

        [Test]
        public void GoldDeposits_EnableGoldMine()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            var map = ctx.State.Map;
            map.Set(map.Index(35, 30), TileFlags.GoldDeposit);
            MakeInsolvent(ctx, s);
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);
            Assert.Greater(Bias(s, BuildingType.GoldMine), 0f);
        }

        [Test]
        public void WonderInCapitalQueue_BoostsGoldVotes()
        {
            var a = WonderWorld(false, out var sa);
            var b = WonderWorld(true, out var sb);
            float goldA = Bias(sa, BuildingType.Market) + Bias(sa, BuildingType.Guildhall);
            float goldB = Bias(sb, BuildingType.Market) + Bias(sb, BuildingType.Guildhall);
            Assert.Greater(goldB, goldA + 0.3f);
            Assert.Greater(AdvisorBoard.Urgency(sb, TreasuryAdvisor.Id), AdvisorBoard.Urgency(sa, TreasuryAdvisor.Id));
            Assert.Greater(AdvisorBoard.JobBias(sb, JobType.Merchant), AdvisorBoard.JobBias(sa, JobType.Merchant));
            Assert.LessOrEqual(Bias(sb, BuildingType.Arena), 0f, "luxuries wait for the wonder");
            Assert.AreEqual(0f, Bias(sb, BuildingType.GreatLibrary), "never votes on wonders");
            Assert.IsNotNull(a);
        }

        private static SimContext WonderWorld(bool wonder, out Settlement s)
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            Assert.IsTrue(s.IsCapital);
            Place(ctx, s, BuildingType.Market);
            s.Stockpile.SetRaw(ResourceType.Gold, 200f);
            s.Stockpile.AvgIncome[(int)ResourceType.Gold] = 0.05f;
            if (wonder) s.BuildQueue.Add(new BuildOrder { Type = BuildingType.GreatLibrary, Priority = 5f, Reason = "glory" });
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);
            return ctx;
        }

        [Test]
        public void PendingUpkeep_TriggersCaution_BeforeShortfall()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            s.Stockpile.SetRaw(ResourceType.Gold, 20f);
            s.Stockpile.AvgIncome[(int)ResourceType.Gold] = 0.02f;
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Theater, Priority = 1f });
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Arena, Priority = 1f });
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);
            Assert.Greater(s.Mem(TreasuryKeys.Stress), 0.2f, "anticipates the future upkeep burden");
            Assert.Greater(s.Mem(TreasuryKeys.PendingUpkeep), 0.1f);
            Assert.Greater(Bias(s, BuildingType.Guildhall), 0f);
        }

        [Test]
        public void RichestTown_IsBankingSeat()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var rich = MakeTown(ctx, new Vec2i(16, 16), out var civ);
            civ.Age = AgeId.Renaissance;
            var poor = ctx.Ops.FoundSettlement(civ, new Vec2i(48, 48));
            poor.Stockpile.Capacity = 10000f;
            for (int i = 0; i < 12; i++) ctx.Ops.CreatePerson(poor, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 25f);
            rich.Stockpile.SetRaw(ResourceType.Gold, 3000f);
            rich.Stockpile.AvgIncome[(int)ResourceType.Gold] = 0.8f;
            poor.Stockpile.SetRaw(ResourceType.Gold, 40f);
            poor.Stockpile.AvgIncome[(int)ResourceType.Gold] = 0.01f;
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 24);
            Assert.AreEqual((float)rich.Id, civ.Mem(TreasuryKeys.Richest));
            Assert.Greater(Bias(rich, BuildingType.Bank), Bias(poor, BuildingType.Bank));
        }

        [Test]
        public void BadData_NeverWritesNaN()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = MakeTown(ctx, new Vec2i(32, 32), out var civ);
            Place(ctx, s, BuildingType.Theater);
            s.Stockpile.AvgIncome[(int)ResourceType.Gold] = float.NaN;
            s.Stockpile.AvgExpense[(int)ResourceType.Gold] = float.PositiveInfinity;
            s.Memory[TreasuryKeys.EconUpkeepShortfall] = float.NaN;
            s.ServiceOutput[(int)ServiceType.Commerce] = float.NaN;
            s.BuildQueue.Add(null);
            civ.Modifiers.Trade = float.NaN;
            TestWorlds.Run(new TreasuryAdvisor(), ctx, 48);
            int checkedKeys = 0;
            foreach (var kv in s.Memory)
            {
                if (!kv.Key.StartsWith("ai.", System.StringComparison.Ordinal)) continue; // injected bad input keys are not ours
                checkedKeys++;
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            }
            Assert.Greater(checkedKeys, 5);
            foreach (var kv in civ.Memory)
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        [Test]
        public void Deterministic()
        {
            var a = Snapshot();
            var b = Snapshot();
            Assert.AreEqual(a, b);
        }

        private static string Snapshot()
        {
            var ctx = TestWorlds.CreateContext(MakeDb(), 64, 7);
            var s1 = MakeTown(ctx, new Vec2i(16, 16), out var c1);
            var s2 = MakeTown(ctx, new Vec2i(48, 48), out var c2);
            MakeInsolvent(ctx, s1);
            Place(ctx, s2, BuildingType.Theater);
            s2.Stockpile.SetRaw(ResourceType.Gold, 900f);
            s2.Stockpile.AvgIncome[(int)ResourceType.Gold] = 0.4f;
            var adv = new TreasuryAdvisor();
            TestWorlds.Run(adv, ctx, 24 * 4);
            var keys = new List<string>();
            foreach (var kv in s1.Memory) keys.Add("1:" + kv.Key + "=" + kv.Value.ToString("R"));
            foreach (var kv in s2.Memory) keys.Add("2:" + kv.Key + "=" + kv.Value.ToString("R"));
            keys.Sort(System.StringComparer.Ordinal);
            return string.Join("|", keys) + "#" + string.Join("|", s1.AiLog) + "#" + string.Join("|", s2.AiLog);
        }
    }
}
