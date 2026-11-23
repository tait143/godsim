using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class CapitalAdvisorTests
    {
        private static ServiceAmount[] Svc(ServiceType t, float v) => new[] { new ServiceAmount(t, v) };

        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.TownCenter, Name = "Town Center", Category = BuildingCategory.Civic, MaxPerSettlement = 1, Width = 2, Height = 2, BuildWork = 60f, StorageCapacity = 100f, Services = Svc(ServiceType.Administration, 2f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Forum, Name = "Forum", Category = BuildingCategory.Civic, MinAge = AgeId.Classical, MaxPerSettlement = 1, Width = 3, Height = 2, BuildWork = 180f, Job = JobType.Administrator, JobSlots = 2, Services = Svc(ServiceType.Administration, 6f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Castle, Name = "Castle", Category = BuildingCategory.Military, MinAge = AgeId.Medieval, MaxPerSettlement = 1, Width = 3, Height = 3, BuildWork = 300f, Job = JobType.Soldier, JobSlots = 10, Services = new[] { new ServiceAmount(ServiceType.Defense, 20f), new ServiceAmount(ServiceType.Administration, 3f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.SpiritTotem, Name = "Spirit Totem", Category = BuildingCategory.Religion, Services = Svc(ServiceType.Faith, 1f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Temple, Name = "Temple", Category = BuildingCategory.Religion, MinAge = AgeId.Iron, Job = JobType.Priest, JobSlots = 2, Services = Svc(ServiceType.Faith, 3f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Cathedral, Name = "Cathedral", Category = BuildingCategory.Religion, MinAge = AgeId.Medieval, MaxPerSettlement = 1, Job = JobType.Priest, JobSlots = 4, Services = Svc(ServiceType.Faith, 8f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Library, Name = "Library", Category = BuildingCategory.Education, MinAge = AgeId.Classical, Job = JobType.Scholar, JobSlots = 2, Services = Svc(ServiceType.Research, 4f) });
            db.Register(new BuildingDefinition { Type = BuildingType.University, Name = "University", Category = BuildingCategory.Education, MinAge = AgeId.Renaissance, Job = JobType.Scholar, JobSlots = 4, Services = Svc(ServiceType.Research, 8f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Theater, Name = "Theater", Category = BuildingCategory.Culture, MinAge = AgeId.Classical, Job = JobType.Entertainer, JobSlots = 2, Services = Svc(ServiceType.Entertainment, 4f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage, StorageCapacity = 100f });
            db.Register(new BuildingDefinition { Type = BuildingType.StoneQuarry, Name = "Stone Quarry", Category = BuildingCategory.Resource, Job = JobType.Quarrier, JobSlots = 4, OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Stone, 0.1f) }, Placement = PlacementRule.NearStone });
            db.Register(new BuildingDefinition { Type = BuildingType.GoldMine, Name = "Gold Mine", Category = BuildingCategory.Resource, MinAge = AgeId.Iron, Job = JobType.Miner, JobSlots = 3, OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Gold, 0.05f) }, Placement = PlacementRule.NearGold });
            db.Register(new BuildingDefinition { Type = BuildingType.SunPyramid, Name = "Sun Pyramid", Category = BuildingCategory.Wonder, MinAge = AgeId.Iron, IsWonder = true, UniquePerCivilization = true, MaxPerSettlement = 1, AllowedRaces = RaceMask.Saurian, Width = 3, Height = 3, BuildWork = 500f, Cost = new[] { new ResourceAmount(ResourceType.Stone, 200f), new ResourceAmount(ResourceType.Gold, 50f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, MinAge = AgeId.Classical, IsWonder = true, UniquePerCivilization = true, MaxPerSettlement = 1, Width = 3, Height = 3, BuildWork = 750f, Cost = new[] { new ResourceAmount(ResourceType.Stone, 400f) } });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static void AddPeople(SimContext ctx, Settlement s, int n)
        {
            for (int i = 0; i < n; i++) ctx.Ops.CreatePerson(s, s.Race, i % 2 == 0 ? Sex.Female : Sex.Male, 20f + i % 10);
        }

        private static bool LogHas(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        private static Settlement MakeEmpire(SimContext ctx, RaceId race, AgeId age, int settlements, out Civilization civ)
        {
            var cap = TestWorlds.AddSettlement(ctx, race, new Vec2i(16, 16), 20, out civ);
            civ.Age = age;
            var spots = new[] { new Vec2i(48, 16), new Vec2i(16, 48), new Vec2i(48, 48) };
            for (int i = 0; i < settlements - 1 && i < spots.Length; i++) AddPeople(ctx, ctx.Ops.FoundSettlement(civ, spots[i]), 8);
            return cap;
        }

        [Test]
        public void EmpireOfFourSettlements_VotesForumAtCapital()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var cap = MakeEmpire(ctx, RaceId.Human, AgeId.Classical, 4, out var civ);
            TestWorlds.Run(new CapitalAdvisor(), ctx, 24);

            var ctx1 = TestWorlds.CreateContext(db);
            var lone = MakeEmpire(ctx1, RaceId.Human, AgeId.Classical, 1, out _);
            TestWorlds.Run(new CapitalAdvisor(), ctx1, 24);

            float big = AdvisorBoard.BuildBias(cap, BuildingType.Forum);
            float small = AdvisorBoard.BuildBias(lone, BuildingType.Forum);
            Assert.IsTrue(big > 0.7f, "forum bias " + big);
            Assert.IsTrue(big > small + 0.3f, "big " + big + " vs lone " + small);
            Assert.IsTrue(LogHas(cap, "Empire of 4 settlements needs administration"), string.Join("\n", cap.AiLog));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(cap, BuildingType.Castle), "castle is not available in the Classical age");
            Assert.IsTrue(AdvisorBoard.Urgency(cap, CapitalAdvisor.Id) > 0.5f);
            Assert.AreEqual(cap.Id, (int)civ.Mem(CapitalKeys.LastCapital, -1f));
        }

        [Test]
        public void WonderInQueue_VotesMaterialsStorageAndLaterBuilders()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(20, 20), 20, out var civ);
            civ.Age = AgeId.Iron;
            var map = ctx.State.Map;
            map.Set(map.Index(cap.Center.X + 3, cap.Center.Y + 1), TileFlags.StoneDeposit);
            cap.BuildQueue.Add(new BuildOrder { Type = BuildingType.SunPyramid, Priority = 5f, Reason = "wonder" });

            var adv = new CapitalAdvisor();
            TestWorlds.Run(adv, ctx, 24);
            Assert.IsTrue(AdvisorBoard.BuildBias(cap, BuildingType.StoneQuarry) > 0.5f, "quarry " + AdvisorBoard.BuildBias(cap, BuildingType.StoneQuarry));
            Assert.IsTrue(AdvisorBoard.JobBias(cap, JobType.Quarrier) > 0.3f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(cap, BuildingType.GoldMine), "no gold deposit: gold mine cannot be placed");
            Assert.IsTrue(AdvisorBoard.BuildBias(cap, BuildingType.Storehouse) > 0.5f, "200 stone exceeds 150 capacity");
            Assert.IsTrue(LogHas(cap, "Gathering stone for the Sun Pyramid ("), string.Join("\n", cap.AiLog));
            Assert.AreEqual(0f, AdvisorBoard.JobBias(cap, JobType.Builder));
            Assert.IsTrue(cap.Mem(CapitalKeys.WonderProgress) > 0f && cap.Mem(CapitalKeys.WonderProgress) < 0.5f);

            // The cost is paid and the pyramid rises: builders.
            cap.BuildQueue.Clear();
            var site = ctx.Ops.FindFreeSite(cap.Center, 3, 3, 12);
            var b = ctx.Ops.CreateBuilding(cap, BuildingType.SunPyramid, site);
            Assert.IsNotNull(b);
            b.CostPaid = true;
            b.ConstructionProgress = 0.4f;
            TestWorlds.Run(adv, ctx, 24, false);
            Assert.IsTrue(AdvisorBoard.JobBias(cap, JobType.Builder) > 0.5f, "builders " + AdvisorBoard.JobBias(cap, JobType.Builder));
            Assert.IsTrue(LogHas(cap, "Builders to the Sun Pyramid (40%)"), string.Join("\n", cap.AiLog));
            Assert.AreEqual(0.7f, cap.Mem(CapitalKeys.WonderProgress), 1e-4f);
        }

        [Test]
        public void NonCapitals_GetNoGrandeurVotes()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var cap = MakeEmpire(ctx, RaceId.Human, AgeId.Medieval, 2, out var civ);
            Settlement prov = null;
            for (int i = 0; i < civ.SettlementIds.Count; i++) if (civ.SettlementIds[i] != cap.Id) prov = ctx.State.Settlements.Get(civ.SettlementIds[i]);
            Assert.IsNotNull(prov);
            TestWorlds.Run(new CapitalAdvisor(), ctx, 24);

            Assert.IsTrue(AdvisorBoard.BuildBias(cap, BuildingType.Cathedral) > 0f);
            Assert.IsTrue(AdvisorBoard.BuildBias(cap, BuildingType.Castle) > 0f);
            Assert.IsTrue(AdvisorBoard.BuildBias(cap, BuildingType.Library) > 0f);
            Assert.IsTrue(AdvisorBoard.BuildBias(cap, BuildingType.Theater) > 0f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(cap, BuildingType.University), "University is a Renaissance building");

            BuildingType[] grand = { BuildingType.Cathedral, BuildingType.Temple, BuildingType.Forum, BuildingType.Library, BuildingType.Castle, BuildingType.Theater, BuildingType.GreatLibrary, BuildingType.TownCenter };
            for (int i = 0; i < grand.Length; i++)
                Assert.IsTrue(AdvisorBoard.BuildBias(prov, grand[i]) <= 0f, grand[i] + " voted at province: " + AdvisorBoard.BuildBias(prov, grand[i]));
            Assert.IsTrue(AdvisorBoard.BuildBias(prov, BuildingType.Cathedral) < 0f, "prestige concentrates in the capital");
            Assert.IsTrue(AdvisorBoard.BuildBias(prov, BuildingType.GreatLibrary) < 0f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(prov, BuildingType.SunPyramid), "not available to humans");
            Assert.AreEqual(0f, AdvisorBoard.Urgency(prov, CapitalAdvisor.Id));
        }

        [Test]
        public void CapitalMoved_NewCapitalVotesTownCenterAndAdministration()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var a = MakeEmpire(ctx, RaceId.Human, AgeId.Classical, 2, out var civ);
            Settlement b = null;
            for (int i = 0; i < civ.SettlementIds.Count; i++) if (civ.SettlementIds[i] != a.Id) b = ctx.State.Settlements.Get(civ.SettlementIds[i]);
            var adv = new CapitalAdvisor();
            TestWorlds.Run(adv, ctx, 24);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(b, BuildingType.Forum));
            Assert.AreEqual(0f, civ.Mem(CapitalKeys.Relocations));

            // The old capital falls from grace; the town center of the new seat burned.
            a.IsCapital = false;
            b.IsCapital = true;
            civ.CapitalId = b.Id;
            ctx.Ops.DestroyBuilding(ctx.State.Buildings.Get(b.TownCenterId), DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            TestWorlds.Run(adv, ctx, 24, false);

            Assert.AreEqual(1f, civ.Mem(CapitalKeys.Relocations));
            Assert.AreEqual(b.Id, (int)civ.Mem(CapitalKeys.LastCapital, -1f));
            Assert.IsTrue(b.Memory.ContainsKey(CapitalKeys.SeatSince));
            Assert.IsTrue(a.Memory.ContainsKey(CapitalKeys.LostTick));
            Assert.IsTrue(AdvisorBoard.BuildBias(b, BuildingType.TownCenter) > 0.5f, "tc " + AdvisorBoard.BuildBias(b, BuildingType.TownCenter));
            Assert.IsTrue(AdvisorBoard.BuildBias(b, BuildingType.Forum) > 0.5f, "forum " + AdvisorBoard.BuildBias(b, BuildingType.Forum));
            Assert.IsTrue(AdvisorBoard.JobBias(b, JobType.Builder) > 0f);
            Assert.IsTrue(LogHas(b, "seat of power moved"), string.Join("\n", b.AiLog));
            Assert.IsTrue(AdvisorBoard.Urgency(b, CapitalAdvisor.Id) > 0.8f);
        }

        [Test]
        public void NoCapital_MostPopulousSettlementActsAsSeat()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var a = MakeEmpire(ctx, RaceId.Human, AgeId.Classical, 2, out var civ);
            a.IsCapital = false;
            civ.CapitalId = -1;
            TestWorlds.Run(new CapitalAdvisor(), ctx, 24);
            Assert.IsTrue(AdvisorBoard.BuildBias(a, BuildingType.Forum) > 0.5f);
            Assert.IsTrue(AdvisorBoard.Urgency(a, CapitalAdvisor.Id) >= 0.9f);
        }

        [Test]
        public void Deterministic_SameSeedSameMemory()
        {
            Assert.AreEqual(RunScenario(), RunScenario());
        }

        private static string RunScenario()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db, 64, 7);
            var cap = MakeEmpire(ctx, RaceId.Saurian, AgeId.Iron, 4, out var civ);
            var map = ctx.State.Map;
            map.Set(map.Index(cap.Center.X - 2, cap.Center.Y), TileFlags.StoneDeposit);
            map.Set(map.Index(cap.Center.X, cap.Center.Y + 3), TileFlags.GoldDeposit);
            cap.BuildQueue.Add(new BuildOrder { Type = BuildingType.SunPyramid, Priority = 5f });
            TestWorlds.RunAll(new List<ISimSystem> { new AdvisorCouncilSystem(), new CapitalAdvisor() }, ctx, 240);

            var sb = new StringBuilder();
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var keys = new List<string>(items[i].Memory.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                for (int k = 0; k < keys.Count; k++)
                {
                    float v = items[i].Memory[keys[k]];
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), keys[k]);
                    sb.Append(items[i].Id).Append(':').Append(keys[k]).Append('=').Append(v.ToString("R")).Append('\n');
                }
                for (int l = 0; l < items[i].AiLog.Count; l++) sb.Append(items[i].AiLog[l]).Append('\n');
            }
            Assert.IsTrue(AdvisorBoard.BuildBias(cap, BuildingType.GoldMine) > 0f, "gold deposit makes the gold mine feasible");
            return sb.ToString();
        }
    }
}
