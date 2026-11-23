using GodSim.Core;
using GodSim.Core.Population;
using NUnit.Framework;
using Kit = GodSim.Tests.Population.SocietyTestKit;

namespace GodSim.Tests.Population
{
    public class HousingSystemTests
    {
        [Test]
        public void HomelessAreHousedUpToCapacityAndNeverOver()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 10, out _);
            var a = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 3);
            var b = Kit.AddHouse(ctx, s, new Vec2i(12, 10), 4);
            var sys = new HousingSystem();

            Kit.RunOnce(sys, ctx);

            Assert.AreEqual(3, a.Residents.Count);
            Assert.AreEqual(4, b.Residents.Count);
            Assert.AreEqual(3, s.Homeless);
            Assert.AreEqual(0f, s.Mem(SocietyKeys.FreeHousing));
            foreach (var p in Kit.Residents(ctx, s))
            {
                if (p.HomeId < 0) continue;
                var h = ctx.State.Buildings.Get(p.HomeId);
                Assert.IsTrue(h.Residents.Contains(p.Id));
                Assert.AreEqual(h.Center, p.Position);
            }

            // A second run is stable and still never exceeds capacity.
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(3, a.Residents.Count);
            Assert.AreEqual(4, b.Residents.Count);
            Assert.AreEqual(3, s.Homeless);
        }

        [Test]
        public void FreeHousingKeyReportsFreeCapacity()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 2, out _);
            Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            Kit.RunOnce(new HousingSystem(), ctx);

            Assert.AreEqual(3f, s.Mem(SocietyKeys.FreeHousing, -1f));
            Assert.AreEqual(5f, s.Mem(SocietyKeys.HousingCapacity, -1f));
            Assert.AreEqual(1f, s.Mem(SocietyKeys.Dwellings, -1f));
            Assert.AreEqual(0, s.Homeless);
        }

        [Test]
        public void FreeHousingIsZeroWithoutDwellings()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 4, out _);
            Kit.RunOnce(new HousingSystem(), ctx);
            Assert.AreEqual(0f, s.Mem(SocietyKeys.FreeHousing, -1f));
            Assert.AreEqual(4, s.Homeless);
        }

        [Test]
        public void FamiliesAreHousedTogether()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 3, out _);
            var mother = Kit.AddPerson(ctx, s, Sex.Female, 30f);
            var father = Kit.AddPerson(ctx, s, Sex.Male, 32f);
            Kit.Marry(mother, father);
            var c1 = Kit.AddPerson(ctx, s, Sex.Female, 6f, mother.Id, father.Id);
            var c2 = Kit.AddPerson(ctx, s, Sex.Male, 3f, mother.Id, father.Id);
            var big = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 4);
            var small = Kit.AddHouse(ctx, s, new Vec2i(30, 30), 3);

            Kit.RunOnce(new HousingSystem(), ctx);

            Assert.AreEqual(big.Id, mother.HomeId);
            Assert.AreEqual(big.Id, father.HomeId);
            Assert.AreEqual(big.Id, c1.HomeId);
            Assert.AreEqual(big.Id, c2.HomeId);
            Assert.AreEqual(3, small.Residents.Count);
            Assert.AreEqual(0, s.Homeless);
        }

        [Test]
        public void ChildrenFollowTheirMother()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out _);
            var mother = Kit.AddPerson(ctx, s, Sex.Female, 30f);
            var lodger = Kit.AddPerson(ctx, s, Sex.Male, 40f);
            var home = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            var empty = Kit.AddHouse(ctx, s, new Vec2i(40, 40), 5);
            Assert.IsTrue(ctx.Ops.AssignHome(mother, home));
            Assert.IsTrue(ctx.Ops.AssignHome(lodger, home));
            var child = Kit.AddPerson(ctx, s, Sex.Male, 2f, mother.Id);

            Kit.RunOnce(new HousingSystem(), ctx);

            Assert.AreEqual(home.Id, child.HomeId, "child should join the mother even though another house is emptier");
            Assert.AreEqual(0, empty.Residents.Count);
        }

        [Test]
        public void HomeInAnotherSettlementIsCleared()
        {
            var ctx = Kit.CreateContext(96);
            var s1 = Kit.Settlement(ctx, new Vec2i(20, 20), 1, out var civ);
            var s2 = ctx.Ops.FoundSettlement(civ, new Vec2i(60, 60));
            var foreignHouse = Kit.AddHouse(ctx, s2, new Vec2i(62, 64), 5);
            var p = Kit.Residents(ctx, s1)[0];
            Assert.IsTrue(ctx.Ops.AssignHome(p, foreignHouse));

            Kit.RunOnce(new HousingSystem(), ctx);

            Assert.AreEqual(-1, p.HomeId);
            Assert.IsFalse(foreignHouse.Residents.Contains(p.Id));
            Assert.AreEqual(1, s1.Homeless);
            Assert.AreEqual(5f, s2.Mem(SocietyKeys.FreeHousing));
        }

        [Test]
        public void DestroyedOrUnfinishedHomesAreNotUsed()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 4, out _);
            var house = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            var unfinished = Kit.AddBuilding(ctx, s, BuildingType.House, new Vec2i(30, 30), false);
            var sys = new HousingSystem();
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(4, house.Residents.Count);
            Assert.AreEqual(0, unfinished.Residents.Count);

            // Mid-tick corruption: flagged destroyed without going through WorldOps.
            house.IsDestroyed = true;
            Kit.RunOnce(sys, ctx);
            foreach (var p in Kit.Residents(ctx, s)) Assert.AreEqual(-1, p.HomeId);
            Assert.AreEqual(4, s.Homeless);
            Assert.AreEqual(0f, s.Mem(SocietyKeys.FreeHousing));

            // Proper destruction through WorldOps, then a replacement house.
            house.IsDestroyed = false;
            ctx.Ops.DestroyBuilding(house, DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            var fresh = Kit.AddHouse(ctx, s, new Vec2i(12, 12), 4);
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(4, fresh.Residents.Count);
            Assert.AreEqual(0, s.Homeless);
        }

        [Test]
        public void OverCapacityHouseEvictsExtras()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 5, out _);
            var house = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            var sys = new HousingSystem();
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(5, house.Residents.Count);

            house.HousingCapacity = 3;
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(3, house.Residents.Count);
            Assert.AreEqual(2, s.Homeless);
        }

        [Test]
        public void EqualHousesPreferTheNearestOne()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 1, out _);
            var far = Kit.AddHouse(ctx, s, new Vec2i(50, 50), 4);
            var near = Kit.AddHouse(ctx, s, new Vec2i(24, 24), 4);
            var p = Kit.Residents(ctx, s)[0];
            Kit.RunOnce(new HousingSystem(), ctx);
            Assert.AreEqual(near.Id, p.HomeId);
            Assert.AreEqual(0, far.Residents.Count);
        }

        [Test]
        public void RoomiestHouseIsFilledFirst()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out _);
            var near = Kit.AddHouse(ctx, s, new Vec2i(24, 24), 3);
            var roomy = Kit.AddHouse(ctx, s, new Vec2i(40, 40), 5);
            var p = Kit.AddPerson(ctx, s, Sex.Male, 30f);
            Kit.RunOnce(new HousingSystem(), ctx);
            Assert.AreEqual(roomy.Id, p.HomeId);
            Assert.AreEqual(0, near.Residents.Count);
        }

        [Test]
        public void LargeFamilySplitsOnlyWhenNoHouseFits()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out _);
            var mother = Kit.AddPerson(ctx, s, Sex.Female, 35f);
            var father = Kit.AddPerson(ctx, s, Sex.Male, 36f);
            Kit.Marry(mother, father);
            for (int i = 0; i < 4; i++) Kit.AddPerson(ctx, s, Sex.Male, 2f + i, mother.Id, father.Id);
            var a = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            var b = Kit.AddHouse(ctx, s, new Vec2i(12, 10), 5);
            Kit.RunOnce(new HousingSystem(), ctx);
            Assert.AreEqual(0, s.Homeless);
            Assert.AreEqual(mother.HomeId, father.HomeId, "the couple stays together");
            Assert.AreEqual(6, a.Residents.Count + b.Residents.Count);
            Assert.IsTrue(a.Residents.Count <= 5 && b.Residents.Count <= 5);
        }

        [Test]
        public void DeadResidentsAreIgnored()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 4, out _);
            var victim = Kit.Residents(ctx, s)[0];
            ctx.Ops.KillPerson(victim, DeathCause.Accident); // not flushed yet: still in the store, flagged dead
            var house = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            new HousingSystem().UpdateSettlement(ctx, s);
            Assert.IsFalse(house.Residents.Contains(victim.Id));
            Assert.AreEqual(3, house.Residents.Count);
            Assert.AreEqual(0, s.Homeless);
        }
    }
}
