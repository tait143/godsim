using GodSim.Core;
using GodSim.Core.Population;
using GodSim.Tests.Shared;
using NUnit.Framework;
using Kit = GodSim.Tests.Population.SocietyTestKit;

namespace GodSim.Tests.Population
{
    public class SocietyIntegrationTests
    {
        /// <summary>Two civilizations, three settlements, families, houses and workplaces.</summary>
        private static SimContext BuildWorld(ulong seed)
        {
            var ctx = Kit.CreateContext(128, seed);
            var a = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(24, 24), 30, out var civA);
            var a2 = ctx.Ops.FoundSettlement(civA, new Vec2i(70, 24));
            var b = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(40, 80), 24, out _);
            foreach (var s in new[] { a, b })
            {
                var people = Kit.Residents(ctx, s);
                for (int i = 0; i + 1 < people.Count && i < 10; i += 2)
                {
                    Kit.Marry(people[i], people[i + 1]);
                    var mother = people[i].Sex == Sex.Female ? people[i] : people[i + 1];
                    var father = mother == people[i] ? people[i + 1] : people[i];
                    ctx.Ops.CreatePerson(s, s.Race, Sex.Female, 4f + i, mother.Id, father.Id);
                    ctx.Ops.CreatePerson(s, s.Race, Sex.Male, 1f + i, mother.Id, father.Id);
                }
                s.Stockpile.SetRaw(ResourceType.Food, 100f);
                s.DesiredBuilders = 2;
                s.ServiceCoverage[(int)ServiceType.Faith] = 0.6f;
            }
            for (int i = 0; i < 4; i++) Kit.AddHouse(ctx, a, new Vec2i(14 + 2 * i, 14), 3 + (i % 3));
            for (int i = 0; i < 3; i++) Kit.AddHouse(ctx, b, new Vec2i(30 + 2 * i, 70), 3 + (i % 3));
            for (int i = 0; i < 6; i++) Kit.AddHouse(ctx, a2, new Vec2i(64 + 2 * i, 16), 5);
            Kit.AddBuilding(ctx, a, BuildingType.GathererHut, new Vec2i(30, 14));
            Kit.AddBuilding(ctx, a, BuildingType.Farm, new Vec2i(32, 14));
            Kit.AddBuilding(ctx, a, BuildingType.Barracks, new Vec2i(34, 14));
            Kit.AddBuilding(ctx, b, BuildingType.WoodcutterCamp, new Vec2i(46, 70));
            Kit.AddBuilding(ctx, b, BuildingType.Barracks, new Vec2i(48, 70));
            return ctx;
        }

        private static ulong Hash(SimContext ctx)
        {
            ulong h = 1469598103934665603UL;
            var people = ctx.State.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                h = Hashing.Combine(h, (ulong)p.Id);
                h = Hashing.Combine(h, (ulong)(uint)p.HomeId);
                h = Hashing.Combine(h, (ulong)(uint)p.WorkplaceId);
                h = Hashing.Combine(h, (ulong)p.Job);
                h = Hashing.Combine(h, (ulong)(uint)p.SettlementId);
                h = Hashing.Combine(h, (ulong)System.BitConverter.ToInt32(System.BitConverter.GetBytes(p.Happiness), 0));
                h = Hashing.Combine(h, (ulong)System.BitConverter.ToInt32(System.BitConverter.GetBytes(p.Health), 0));
                h = Hashing.Combine(h, p.IsAlive ? 1UL : 0UL);
            }
            var ss = ctx.State.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                h = Hashing.Combine(h, (ulong)ss[i].Homeless);
                h = Hashing.Combine(h, (ulong)ss[i].Unemployed);
                h = Hashing.Combine(h, (ulong)ss[i].Soldiers);
            }
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) h = Hashing.Combine(h, (ulong)(uint)civs[i].LeaderId);
            return Hashing.Combine(h, ctx.State.Rng.State);
        }

        [Test]
        public void SimulationIsDeterministic()
        {
            var c1 = BuildWorld(42);
            var c2 = BuildWorld(42);
            TestWorlds.RunAll(Kit.AllSystems(), c1, 5 * SimTime.TicksPerYear);
            TestWorlds.RunAll(Kit.AllSystems(), c2, 5 * SimTime.TicksPerYear);
            Assert.AreEqual(Hash(c1), Hash(c2));
            Assert.AreEqual(c1.State.Chronicle.Entries.Count, c2.State.Chronicle.Entries.Count);
        }

        [Test]
        public void TwoHundredYearRunStaysConsistent()
        {
            var ctx = BuildWorld(9);
            var systems = Kit.AllSystems();
            TestWorlds.RunAll(systems, ctx, 200 * SimTime.TicksPerYear);
            var st = ctx.State;
            int alive = 0;
            foreach (var p in st.People.Items)
            {
                if (!p.IsAlive) continue;
                alive++;
                Assert.IsTrue(p.Happiness >= 0f && p.Happiness <= 1f && !float.IsNaN(p.Happiness));
                Assert.IsTrue(p.Health >= 0f && p.Health <= 1f && !float.IsNaN(p.Health));
                Assert.IsTrue(p.Faith >= 0f && p.Faith <= 1f && !float.IsNaN(p.Faith));
                Assert.IsTrue(p.Experience >= 0f && p.Experience <= 1f);
                if (p.HomeId >= 0)
                {
                    var h = st.Buildings.Get(p.HomeId);
                    Assert.IsNotNull(h);
                    Assert.AreEqual(p.SettlementId, h.SettlementId);
                    Assert.IsTrue(h.Residents.Contains(p.Id));
                }
                if (p.WorkplaceId >= 0)
                {
                    var w = st.Buildings.Get(p.WorkplaceId);
                    Assert.IsNotNull(w);
                    Assert.IsTrue(w.Workers.Contains(p.Id));
                    Assert.AreEqual(ctx.Db.Building(w.Type).Job, p.Job);
                }
                if (!p.IsAdult) Assert.AreEqual(JobType.None, p.Job);
                if (p.Stage == LifeStage.Elder) Assert.AreNotEqual(JobType.Soldier, p.Job);
            }
            Assert.IsTrue(alive > 0);
            foreach (var b in st.Buildings.Items)
            {
                Assert.IsTrue(b.Residents.Count <= b.HousingCapacity);
                if (ctx.Db.Building(b.Type).HasJobs) Assert.IsTrue(b.Workers.Count <= ctx.Db.Building(b.Type).JobSlots);
            }
            foreach (var s in st.Settlements.Items)
            {
                if (!s.IsAlive) continue;
                Assert.IsTrue(s.Unrest >= 0f && s.Unrest <= 1f);
                Assert.IsTrue(s.Mem(SocietyKeys.FreeHousing, -1f) >= 0f);
                int homeless = 0;
                foreach (int id in s.ResidentIds) if (st.People.Get(id).HomeId < 0) homeless++;
                Assert.IsTrue(homeless >= 0 && s.Homeless >= 0);
            }
            foreach (var civ in st.Civilizations.Items)
            {
                if (!civ.IsAlive) continue;
                var leader = st.People.Get(civ.LeaderId);
                Assert.IsNotNull(leader, "every civ with adults has a leader");
                Assert.IsTrue(leader.IsAlive && leader.CivilizationId == civ.Id);
            }
        }

        [Test]
        public void SystemsSurviveAnEmptyWorldAndDeadSettlements()
        {
            var ctx = Kit.CreateContext();
            var systems = Kit.AllSystems();
            TestWorlds.RunAll(systems, ctx, 100);
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 5, out var civ);
            Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            ctx.Ops.DestroySettlement(s, DestroyCause.Abandoned, true);
            TestWorlds.RunAll(systems, ctx, 100);
        }

        [Test]
        public void RunsUnderTheSimulationEngine()
        {
            var ctx = BuildWorld(5);
            var engine = new SimulationEngine(ctx);
            engine.AddSystems(GodSim.Core.Population.SocietyModule.CreateSystems());
            engine.Initialize();
            engine.StepMany(3 * SimTime.TicksPerYear);
            foreach (var p in engine.Profiles) { Assert.AreEqual(0, p.TotalFaults, p.Name + ": " + p.LastError); Assert.IsTrue(p.Runs > 1); }
            foreach (var s in ctx.State.Settlements.Items)
            {
                int homeless = 0;
                foreach (int id in s.ResidentIds) if (ctx.State.People.Get(id).HomeId < 0) homeless++;
                Assert.IsTrue(s.Homeless >= 0 && s.Homeless <= s.Population);
                Assert.IsTrue(s.Memory.ContainsKey(SocietyKeys.FreeHousing));
            }
            foreach (var civ in ctx.State.Civilizations.Items) Assert.IsTrue(civ.LeaderId >= 0);
        }

        [Test]
        public void SystemsHaveExpectedIntervalsAndNames()
        {
            Assert.AreEqual(4, new HousingSystem().TickInterval);
            Assert.AreEqual(6, new JobAssignmentSystem().TickInterval);
            Assert.AreEqual(6, new WellbeingSystem().TickInterval);
            Assert.AreEqual(48, new MigrationSystem().TickInterval);
            Assert.AreEqual(48, new LeadershipSystem().TickInterval);
            foreach (var s in Kit.AllSystems()) Assert.IsFalse(string.IsNullOrEmpty(s.Name));
        }
    }
}
