using GodSim.Core;
using GodSim.Core.Population;
using NUnit.Framework;
using Kit = GodSim.Tests.Population.SocietyTestKit;

namespace GodSim.Tests.Population
{
    public class JobAssignmentSystemTests
    {
        private static void PlentyOfFood(Settlement s) => s.Stockpile.SetRaw(ResourceType.Food, 1000f);

        [Test]
        public void JobsFillSlotsWithoutExceedingThem()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 10, out _);
            PlentyOfFood(s);
            var hut = Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            var camp = Kit.AddBuilding(ctx, s, BuildingType.WoodcutterCamp, new Vec2i(12, 10));

            Kit.RunOnce(new JobAssignmentSystem(), ctx);

            Assert.AreEqual(3, hut.Workers.Count);
            Assert.AreEqual(2, camp.Workers.Count);
            Assert.AreEqual(3, Kit.CountJob(ctx, s, JobType.Gatherer));
            Assert.AreEqual(2, Kit.CountJob(ctx, s, JobType.Woodcutter));
            Assert.AreEqual(5, s.Unemployed);
            foreach (var p in Kit.Residents(ctx, s))
                if (p.WorkplaceId >= 0) Assert.IsTrue(ctx.State.Buildings.Get(p.WorkplaceId).Workers.Contains(p.Id));
        }

        [Test]
        public void PrioritiesAreRespected()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 3, out _);
            PlentyOfFood(s);
            Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            Kit.AddBuilding(ctx, s, BuildingType.WoodcutterCamp, new Vec2i(12, 10));
            Kit.AddBuilding(ctx, s, BuildingType.WoodcutterCamp, new Vec2i(14, 10));
            s.JobPriority[(int)JobType.Woodcutter] = 5f;

            Kit.RunOnce(new JobAssignmentSystem(), ctx);

            Assert.AreEqual(3, Kit.CountJob(ctx, s, JobType.Woodcutter));
            Assert.AreEqual(0, Kit.CountJob(ctx, s, JobType.Gatherer));
            Assert.AreEqual(0, s.Unemployed);
        }

        [Test]
        public void FoodEmergencyBoostsFoodJobs()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 3, out _);
            s.Stockpile.SetRaw(ResourceType.Food, 0f);
            Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            Kit.AddBuilding(ctx, s, BuildingType.WoodcutterCamp, new Vec2i(12, 10));
            Kit.AddBuilding(ctx, s, BuildingType.WoodcutterCamp, new Vec2i(14, 10));
            s.JobPriority[(int)JobType.Woodcutter] = 2f; // beats the default 1, loses to 1 x 3 during the emergency

            Assert.IsTrue(JobAssignmentSystem.IsFoodEmergency(s));
            Kit.RunOnce(new JobAssignmentSystem(), ctx);

            Assert.AreEqual(3, Kit.CountJob(ctx, s, JobType.Gatherer));
            Assert.AreEqual(0, Kit.CountJob(ctx, s, JobType.Woodcutter));
            Assert.AreEqual(1f, s.Mem(SocietyKeys.FoodEmergency));
        }

        [Test]
        public void FoodDaysFormulaMatchesSpec()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 10, out _);
            s.Stockpile.SetRaw(ResourceType.Food, 7f); // 7 / (10 * 0.05) = 14 days
            Assert.IsTrue(JobAssignmentSystem.IsFoodEmergency(s));
            s.Stockpile.SetRaw(ResourceType.Food, 8f); // 16 days
            Assert.IsFalse(JobAssignmentSystem.IsFoodEmergency(s));
            Assert.AreEqual(3f, JobAssignmentSystem.JobWeight(s, JobType.Farmer, true));
            Assert.AreEqual(1f, JobAssignmentSystem.JobWeight(s, JobType.Smith, true));
        }

        [Test]
        public void BuildersFollowDesiredBuilders()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 10, out _);
            PlentyOfFood(s);
            var sys = new JobAssignmentSystem();
            s.DesiredBuilders = 3;
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(3, Kit.CountJob(ctx, s, JobType.Builder));
            foreach (var p in Kit.Residents(ctx, s)) if (p.Job == JobType.Builder) Assert.AreEqual(-1, p.WorkplaceId);
            Assert.AreEqual(7, s.Unemployed);

            s.DesiredBuilders = 1;
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(1, Kit.CountJob(ctx, s, JobType.Builder));
            Assert.AreEqual(9, s.Unemployed);

            s.DesiredBuilders = 50; // more than the workforce
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(10, Kit.CountJob(ctx, s, JobType.Builder));
            Assert.AreEqual(0, s.Unemployed);
        }

        [Test]
        public void SoldiersAreCountedAndEldersNeverServe()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 2, out _);
            PlentyOfFood(s);
            var barracks = Kit.AddBuilding(ctx, s, BuildingType.Barracks, new Vec2i(10, 10));
            var oldSoldier = Kit.AddPerson(ctx, s, Sex.Male, 62f);
            Assert.AreEqual(LifeStage.Elder, oldSoldier.Stage);
            Assert.IsTrue(ctx.Ops.AssignJob(oldSoldier, barracks, JobType.Soldier));
            for (int i = 0; i < 3; i++) Kit.AddPerson(ctx, s, Sex.Female, 60f + i);

            Kit.RunOnce(new JobAssignmentSystem(), ctx);

            Assert.AreEqual(2, s.Soldiers);
            Assert.AreEqual(2, barracks.Workers.Count);
            foreach (var p in Kit.Residents(ctx, s))
                if (p.Stage == LifeStage.Elder) Assert.AreNotEqual(JobType.Soldier, p.Job);
        }

        [Test]
        public void EldersMayTakeCivilianJobs()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out _);
            PlentyOfFood(s);
            Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            var elder = Kit.AddPerson(ctx, s, Sex.Female, 65f);
            Kit.RunOnce(new JobAssignmentSystem(), ctx);
            Assert.AreEqual(JobType.Gatherer, elder.Job);
        }

        [Test]
        public void ChildrenNeverWork()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out _);
            PlentyOfFood(s);
            var hut = Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            for (int i = 0; i < 3; i++) Kit.AddPerson(ctx, s, Sex.Male, 8f);
            s.DesiredBuilders = 2;
            Kit.RunOnce(new JobAssignmentSystem(), ctx);
            Assert.AreEqual(0, hut.Workers.Count);
            Assert.AreEqual(0, Kit.CountJob(ctx, s, JobType.Builder));
            Assert.AreEqual(0, s.Unemployed);
        }

        [Test]
        public void DestroyedWorkplaceIsHandled()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 3, out _);
            PlentyOfFood(s);
            var hut = Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            var sys = new JobAssignmentSystem();
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(3, hut.Workers.Count);

            ctx.Ops.DestroyBuilding(hut, DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            var spare = Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(14, 14));
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(3, spare.Workers.Count);
            foreach (var p in Kit.Residents(ctx, s)) Assert.AreEqual(spare.Id, p.WorkplaceId);

            // Corruption mid-tick: flagged destroyed without WorldOps -> workers released, no dangling ids.
            spare.IsDestroyed = true;
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(0, spare.Workers.Count);
            foreach (var p in Kit.Residents(ctx, s)) { Assert.AreEqual(-1, p.WorkplaceId); Assert.AreEqual(JobType.None, p.Job); }
            Assert.AreEqual(3, s.Unemployed);
        }

        [Test]
        public void ChurnIsLimitedToFifteenPercent()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 20, out _);
            PlentyOfFood(s);
            for (int i = 0; i < 4; i++) Kit.AddBuilding(ctx, s, BuildingType.StoneQuarry, new Vec2i(6 + 2 * i, 6)); // 4 x 5 slots
            var sys = new JobAssignmentSystem();
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(20, Kit.CountJob(ctx, s, JobType.Quarrier));

            Kit.AddBuilding(ctx, s, BuildingType.Farm, new Vec2i(6, 12));
            s.JobPriority[(int)JobType.Farmer] = 5f;
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(3, Kit.CountJob(ctx, s, JobType.Farmer), "floor(20 * 0.15) = 3 reassignments per run");
            Assert.AreEqual(17, Kit.CountJob(ctx, s, JobType.Quarrier));

            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(4, Kit.CountJob(ctx, s, JobType.Farmer));
            Assert.AreEqual(16, Kit.CountJob(ctx, s, JobType.Quarrier));
        }

        [Test]
        public void ExistingWorkersKeepTheirPostsAndGainExperience()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 4, out _);
            PlentyOfFood(s);
            var hut = Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            var sys = new JobAssignmentSystem();
            Kit.RunOnce(sys, ctx);
            var first = hut.Workers.ToArray();
            for (int i = 0; i < 10; i++) Kit.RunOnce(sys, ctx);
            Assert.AreEqual(first.Length, hut.Workers.Count);
            foreach (int id in first) Assert.IsTrue(hut.Workers.Contains(id));
            var worker = ctx.State.People.Get(first[0]);
            Assert.IsTrue(worker.Experience > 0f && worker.Experience <= 1f);
        }

        [Test]
        public void ExperiencedWorkerKeepsExperienceWhenBuildingLosesSlots()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out _);
            PlentyOfFood(s);
            var hut = Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            var other = Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(14, 10));
            var veteran = Kit.AddPerson(ctx, s, Sex.Female, 30f);
            Assert.IsTrue(ctx.Ops.AssignJob(veteran, hut, JobType.Gatherer));
            veteran.Experience = 0.8f;
            // Simulate a building whose worker list says it is overstaffed: put veteran into a full hut via direct state.
            var a = Kit.AddPerson(ctx, s, Sex.Male, 30f);
            var b = Kit.AddPerson(ctx, s, Sex.Male, 31f);
            var c = Kit.AddPerson(ctx, s, Sex.Male, 32f);
            Assert.IsTrue(ctx.Ops.AssignJob(a, hut, JobType.Gatherer));
            Assert.IsTrue(ctx.Ops.AssignJob(b, hut, JobType.Gatherer));
            // c is forced in beyond the 3 slots (e.g. slots shrank) with zero experience.
            hut.Workers.Add(c.Id); c.WorkplaceId = hut.Id; c.Job = JobType.Gatherer; c.Experience = 0f;

            Kit.RunOnce(new JobAssignmentSystem(), ctx);

            Assert.AreEqual(3, hut.Workers.Count);
            Assert.IsTrue(hut.Workers.Contains(veteran.Id), "the most experienced keep their posts");
            Assert.AreEqual(JobType.Gatherer, c.Job, "the released gatherer is preferred for the open gatherer slot");
            Assert.AreEqual(other.Id, c.WorkplaceId);
        }

        [Test]
        public void SoldiersAwayWithArmyKeepTheirPost()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 2, out var civ);
            PlentyOfFood(s);
            var barracks = Kit.AddBuilding(ctx, s, BuildingType.Barracks, new Vec2i(10, 10));
            var sys = new JobAssignmentSystem();
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(2, s.Soldiers);
            var army = ctx.Ops.CreateArmy(civ, s, Kit.Residents(ctx, s));
            s.JobPriority[(int)JobType.Gatherer] = 9f;
            Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(14, 14));
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(2, s.Soldiers);
            Assert.AreEqual(2, barracks.Workers.Count);
            Assert.AreEqual(2, army.SoldierIds.Count);
        }

        [Test]
        public void BadPriorityValuesDoNotBreakAssignment()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 4, out _);
            PlentyOfFood(s);
            Kit.AddBuilding(ctx, s, BuildingType.GathererHut, new Vec2i(10, 10));
            s.JobPriority[(int)JobType.Gatherer] = float.NaN;
            s.JobPriority[(int)JobType.Builder] = float.PositiveInfinity;
            s.DesiredBuilders = -5;
            Kit.RunOnce(new JobAssignmentSystem(), ctx);
            Assert.AreEqual(3, Kit.CountJob(ctx, s, JobType.Gatherer));
            Assert.AreEqual(0, Kit.CountJob(ctx, s, JobType.Builder));
        }
    }
}
