using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class GrowthAdvisorTests
    {
        private static readonly Vec2i At = new Vec2i(32, 32);

        // ------------------------------------------------------------------ fixtures
        private static GameDatabase MakeDb(float fertility = 0.25f, AgeId houseMinAge = AgeId.Dawn)
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.TownCenter, Name = "Town Center", Category = BuildingCategory.Civic, Width = 2, Height = 2, MaxPerSettlement = 1 });
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5, MinAge = houseMinAge });
            db.Register(new BuildingDefinition { Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure, Services = new[] { new ServiceAmount(ServiceType.Water, 3f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.Aqueduct, Name = "Aqueduct", Category = BuildingCategory.Infrastructure, MinAge = AgeId.Classical, Services = new[] { new ServiceAmount(ServiceType.Water, 10f) } });
            TestDb.FillMissing(db);
            db.Races[(int)RaceId.Human].FertilityPerYear = fertility;
            db.Freeze();
            return db;
        }

        private static Building AddHouse(SimContext ctx, Settlement s, int capacity)
        {
            var site = ctx.Ops.FindFreeSite(s.Center, 1, 1, 20);
            Assert.IsTrue(site.IsValid, "no free site for a test house");
            var b = ctx.Ops.CreateBuilding(s, BuildingType.House, site, 0, true);
            Assert.IsNotNull(b);
            b.HousingCapacity = capacity;
            return b;
        }

        private static void HouseEveryone(SimContext ctx, Settlement s)
        {
            for (int i = 0; i < s.ResidentIds.Count; i++)
            {
                var p = ctx.State.People.Get(s.ResidentIds[i]);
                for (int j = 0; j < s.BuildingIds.Count && p.HomeId < 0; j++)
                {
                    var b = ctx.State.Buildings.Get(s.BuildingIds[j]);
                    if (b.Type == BuildingType.House) ctx.Ops.AssignHome(p, b);
                }
            }
        }

        private static void MarryPairs(SimContext ctx, Settlement s)
        {
            for (int i = 0; i + 1 < s.ResidentIds.Count; i += 2)
            {
                var a = ctx.State.People.Get(s.ResidentIds[i]);
                var b = ctx.State.People.Get(s.ResidentIds[i + 1]);
                a.SpouseId = b.Id;
                b.SpouseId = a.Id;
            }
        }

        /// <summary>12 adults in 6 couples, 15 beds (3 free), fertile race: a deficit is projected within a year.</summary>
        private static Settlement GrowingTown(SimContext ctx, float foodDays)
        {
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 12, out _);
            MarryPairs(ctx, s);
            for (int i = 0; i < 3; i++) AddHouse(ctx, s, 5);
            HouseEveryone(ctx, s);
            s.Memory[GrowthAdvisorKeys.FoodDays] = foodDays;
            return s;
        }

        /// <summary>
        /// Per-run board value of a per-council-cycle expectation. Votes are normalized per 24-tick council cycle
        /// (AdvisorSystemBase.CycleScale = TickInterval / 24), so one run of this 12-tick advisor contributes half of
        /// what it votes per cycle. Thresholds below are written per cycle and checked against a single run.
        /// </summary>
        private static float PerRun(float perCycle) => perCycle * System.Math.Min(1f, new GrowthAdvisor().TickInterval / 24f);

        private static void RunOnce(GrowthAdvisor adv, SimContext ctx, bool initialize = true) => TestWorlds.Run(adv, ctx, adv.TickInterval, initialize);

        // ------------------------------------------------------------------ tests
        [Test]
        public void Identity_IsGrowthEvery12Ticks()
        {
            var adv = new GrowthAdvisor();
            Assert.AreEqual("growth", adv.AdvisorId);
            Assert.AreEqual(12, adv.TickInterval);
        }

        [Test]
        public void Homeless_GiveStrongHouseVote_WithReason()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 10, out _);
            s.Memory[GrowthAdvisorKeys.FoodDays] = 200f;
            var adv = new GrowthAdvisor();
            RunOnce(adv, ctx);

            Assert.GreaterOrEqual(AdvisorBoard.BuildBias(s, BuildingType.House), PerRun(0.6f));
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Builder), 0f);
            Assert.Greater(AdvisorBoard.Urgency(s, "growth"), 0.5f);
            Assert.AreEqual(10, adv.LastForecast.Homeless);
            bool logged = false;
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains("[growth]") && s.AiLog[i].Contains("homeless")) logged = true;
            Assert.IsTrue(logged, "strong vote should carry a reason");
        }

        [Test]
        public void ProjectedDeficit_GivesModerateVote()
        {
            var ctx = TestWorlds.CreateContext(MakeDb(fertility: 0.8f));
            var s = GrowingTown(ctx, 200f);
            var adv = new GrowthAdvisor();
            RunOnce(adv, ctx);

            var f = adv.LastForecast;
            Assert.AreEqual(0, f.Homeless);
            Assert.AreEqual(3, f.FreeBeds);
            Assert.Greater(f.Projected1, 12f);
            Assert.GreaterOrEqual(f.Projected2, f.Projected1);
            Assert.GreaterOrEqual(f.Projected3, f.Projected2);
            Assert.Greater(f.BedDeficit, 0f);
            float bias = AdvisorBoard.BuildBias(s, BuildingType.House);
            Assert.Greater(bias, PerRun(0.1f), "pre-build ahead of growth");
            Assert.Less(bias, PerRun(0.6f), "pre-building must stay moderate, not a homeless-level push");
            Assert.AreEqual(f.Projected1, s.Mem(GrowthAdvisorKeys.Projected1), 1e-4f);
        }

        [Test]
        public void ComfortableSlowTown_DoesNotPreBuild()
        {
            var ctx = TestWorlds.CreateContext(MakeDb(fertility: 0.05f));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 6, out _);
            MarryPairs(ctx, s);
            for (int i = 0; i < 3; i++) AddHouse(ctx, s, 4); // 12 beds, 6 free, 2 empty houses at most
            HouseEveryone(ctx, s);
            s.Memory[GrowthAdvisorKeys.FoodDays] = 200f;
            RunOnce(new GrowthAdvisor(), ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.House), 1e-4f);
        }

        [Test]
        public void PendingDwellingBudget_PausesPreBuilding()
        {
            var ctx = TestWorlds.CreateContext(MakeDb(fertility: 0.8f));
            var s = GrowingTown(ctx, 200f);
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.House, Priority = 1f, Reason = "test" });
            var adv = new GrowthAdvisor();
            RunOnce(adv, ctx);
            Assert.AreEqual(4f, adv.LastForecast.PendingBeds, 1e-4f, "queued dwellings count as ~4 beds");
            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.House), 0f, "a camp builds one dwelling at a time");
        }

        [Test]
        public void EmptyHouseGlut_GivesNegativeVote()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 4, out _);
            MarryPairs(ctx, s);
            AddHouse(ctx, s, 5);
            HouseEveryone(ctx, s);
            for (int i = 0; i < 5; i++) AddHouse(ctx, s, 4);
            s.Memory[GrowthAdvisorKeys.FoodDays] = 200f;
            RunOnce(new GrowthAdvisor(), ctx);
            float bias = AdvisorBoard.BuildBias(s, BuildingType.House);
            Assert.Less(bias, 0f);
            Assert.Greater(bias, PerRun(-0.6f), "only slightly negative");
        }

        [Test]
        public void FreeHousingMemory_IsPreferredOverComputedBeds()
        {
            var ctx = TestWorlds.CreateContext(MakeDb(fertility: 0.8f));
            var s = GrowingTown(ctx, 200f);
            s.Memory[GrowthAdvisorKeys.FreeHousing] = 40f;
            var adv = new GrowthAdvisor();
            RunOnce(adv, ctx);
            Assert.AreEqual(40, adv.LastForecast.FreeBeds);
            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.House), 0f);
        }

        [Test]
        public void MissingTownCenter_GivesVote()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);
            var s = ctx.Ops.CreateSettlement(civ, At);
            for (int i = 0; i < 6; i++) ctx.Ops.CreatePerson(s, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 25f);
            var withTc = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(12, 12), 6, out _);
            RunOnce(new GrowthAdvisor(), ctx);

            Assert.GreaterOrEqual(AdvisorBoard.BuildBias(s, BuildingType.TownCenter), PerRun(0.9f));
            Assert.AreEqual(1f, AdvisorBoard.Urgency(s, "growth"), 1e-4f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(withTc, BuildingType.TownCenter), 1e-4f);
        }

        [Test]
        public void LowFood_DampensGrowthVotes()
        {
            var richCtx = TestWorlds.CreateContext(MakeDb(fertility: 0.8f));
            var rich = GrowingTown(richCtx, 200f);
            var poorCtx = TestWorlds.CreateContext(MakeDb(fertility: 0.8f));
            var poor = GrowingTown(poorCtx, 5f);
            RunOnce(new GrowthAdvisor(), richCtx);
            var poorAdv = new GrowthAdvisor();
            RunOnce(poorAdv, poorCtx);
            float richBias = AdvisorBoard.BuildBias(rich, BuildingType.House);
            float poorBias = AdvisorBoard.BuildBias(poor, BuildingType.House);
            Assert.Greater(richBias, 0f);
            Assert.Less(poorBias, richBias * 0.5f, "do not build housing food cannot support");
            Assert.Less(poorAdv.LastForecast.NetPerYear, 3f);

            // Homeless still get shelter when food is short, but the push is tempered.
            var a = TestWorlds.CreateContext(MakeDb());
            var sa = TestWorlds.AddSettlement(a, RaceId.Human, At, 10, out _);
            sa.Memory[GrowthAdvisorKeys.FoodDays] = 200f;
            var b = TestWorlds.CreateContext(MakeDb());
            var sb = TestWorlds.AddSettlement(b, RaceId.Human, At, 10, out _);
            sb.Memory[GrowthAdvisorKeys.FoodDays] = 3f;
            RunOnce(new GrowthAdvisor(), a);
            RunOnce(new GrowthAdvisor(), b);
            Assert.Greater(AdvisorBoard.BuildBias(sb, BuildingType.House), PerRun(0.3f));
            Assert.Less(AdvisorBoard.BuildBias(sb, BuildingType.House), AdvisorBoard.BuildBias(sa, BuildingType.House));
        }

        [Test]
        public void GrowingTown_VotesAvailableWaterOnly()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 40, out _);
            var site = ctx.Ops.FindFreeSite(s.Center, 1, 1, 20);
            ctx.Ops.CreateBuilding(s, BuildingType.Well, site, 0, true);
            s.ServiceOutput[(int)ServiceType.Water] = 3f;
            s.ServiceCoverage[(int)ServiceType.Water] = 0.6f;
            s.Memory[GrowthAdvisorKeys.FoodDays] = 200f;
            var adv = new GrowthAdvisor();
            RunOnce(adv, ctx);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Well), PerRun(0.2f));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Aqueduct), 1e-4f, "Aqueduct is not available in the Dawn age");
            Assert.AreEqual(0.125f, s.Mem(GrowthAdvisorKeys.WaterPerCapita), 0.01f);
        }

        [Test]
        public void UnavailableHouse_IsNeverVoted()
        {
            var ctx = TestWorlds.CreateContext(MakeDb(houseMinAge: AgeId.Iron));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 10, out _);
            s.Memory[GrowthAdvisorKeys.FoodDays] = 200f;
            RunOnce(new GrowthAdvisor(), ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.House), 1e-4f);
        }

        [Test]
        public void ObservedArrivals_RaiseTrend()
        {
            var ctx = TestWorlds.CreateContext(MakeDb());
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 6, out _);
            s.Memory[GrowthAdvisorKeys.FoodDays] = 200f;
            var adv = new GrowthAdvisor();
            RunOnce(adv, ctx);
            ctx.State.Tick++; // births happen after the sample (engine order: Population runs before AI)
            for (int i = 0; i < 4; i++) ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Female, 0f, -1, -1, true);
            RunOnce(adv, ctx, false);
            Assert.Greater(s.Mem(GrowthAdvisorKeys.TrendPerYear), 0f);
            Assert.Greater(s.Mem(GrowthAdvisorKeys.BirthsPerYear), 0f);
            Assert.AreEqual(1f, s.Mem(GrowthAdvisorKeys.Samples), 1e-4f);
        }

        [Test]
        public void Deterministic_SameInputsSameMemory()
        {
            string a = Simulate(7), b = Simulate(7);
            Assert.AreEqual(a, b);
            StringAssert.Contains("adv.growth.proj1", a);
        }

        private static string Simulate(ulong seed)
        {
            var ctx = TestWorlds.CreateContext(MakeDb(fertility: 0.6f), 64, seed);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, 12, out _);
            MarryPairs(ctx, s);
            for (int i = 0; i < 2; i++) AddHouse(ctx, s, ctx.Rng.RangeInclusive(3, 5));
            HouseEveryone(ctx, s);
            var adv = new GrowthAdvisor();
            TestWorlds.Run(adv, ctx, 120);
            ctx.State.Tick++;
            for (int i = 0; i < 5; i++) ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Male, 0f, -1, -1, true);
            TestWorlds.Run(adv, ctx, 120, false);

            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                float v = s.Memory[keys[i]];
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), keys[i] + " is not finite");
                sb.Append(keys[i]).Append('=').Append(v.ToString("R")).Append(';');
            }
            return sb.ToString();
        }
    }
}
