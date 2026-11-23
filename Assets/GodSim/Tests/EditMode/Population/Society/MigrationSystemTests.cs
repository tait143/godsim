using GodSim.Core;
using GodSim.Core.Population;
using NUnit.Framework;
using Kit = GodSim.Tests.Population.SocietyTestKit;

namespace GodSim.Tests.Population
{
    public class MigrationSystemTests
    {
        private static Settlement Destination(SimContext ctx, Civilization civ, Vec2i at, int houses)
        {
            var d = ctx.Ops.FoundSettlement(civ, at);
            for (int i = 0; i < houses; i++) Kit.AddHouse(ctx, d, new Vec2i(at.X - 6 + 2 * i, at.Y - 6), 5);
            return d;
        }

        [Test]
        public void HomelessMoveToSameCivSettlementWithSpace()
        {
            var ctx = Kit.CreateContext(128);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 40, out var civ);
            var small = Destination(ctx, civ, new Vec2i(60, 20), 1);   // 5 free, 40 tiles away
            var roomy = Destination(ctx, civ, new Vec2i(20, 70), 4);   // 20 free, 50 tiles away
            var housing = new HousingSystem();
            Kit.RunOnce(housing, ctx);
            Assert.AreEqual(40, src.Homeless);
            Assert.AreEqual(20f, roomy.Mem(SocietyKeys.FreeHousing));

            Kit.RunOnce(new MigrationSystem(), ctx);

            Assert.AreEqual(2, roomy.Population, "5% of 40 move to the roomiest settlement");
            Assert.AreEqual(0, small.Population);
            Assert.AreEqual(38, src.Population);
            Assert.AreEqual(38, src.Homeless);
            Assert.AreEqual(18f, roomy.Mem(SocietyKeys.FreeHousing));
            foreach (var p in Kit.Residents(ctx, roomy)) { Assert.AreEqual(roomy.Id, p.SettlementId); Assert.AreEqual(civ.Id, p.CivilizationId); }

            Kit.RunOnce(housing, ctx);
            Assert.AreEqual(0, roomy.Homeless);
        }

        [Test]
        public void ArrivalsAreNotSentOnAgainInTheSameRun()
        {
            var ctx = Kit.CreateContext(160);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 100, out var civ);
            var mid = Destination(ctx, civ, new Vec2i(70, 20), 2);   // 10 free
            var next = Destination(ctx, civ, new Vec2i(120, 20), 1); // 5 free, out of range of src, in range of mid
            Kit.RunOnce(new HousingSystem(), ctx);
            Kit.RunOnce(new MigrationSystem(), ctx);
            Assert.AreEqual(5, mid.Population, "5% of 100");
            Assert.AreEqual(0, next.Population);
            Assert.AreEqual(5, mid.Homeless);
        }

        [Test]
        public void NoMigrationBeyondRange()
        {
            var ctx = Kit.CreateContext(160);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 40, out var civ);
            var far = Destination(ctx, civ, new Vec2i(120, 120), 4); // ~141 tiles
            Kit.RunOnce(new HousingSystem(), ctx);
            Kit.RunOnce(new MigrationSystem(), ctx);
            Assert.AreEqual(0, far.Population);
            Assert.AreEqual(40, src.Population);
        }

        [Test]
        public void NoMigrationWithFewHomelessOrOtherCivs()
        {
            var ctx = Kit.CreateContext(128);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 3, out var civ);
            var dest = Destination(ctx, civ, new Vec2i(50, 20), 2);
            var other = Kit.Settlement(ctx, new Vec2i(20, 60), 0, out var otherCiv);
            Kit.AddHouse(ctx, other, new Vec2i(14, 54), 5);
            Kit.RunOnce(new HousingSystem(), ctx);
            Kit.RunOnce(new MigrationSystem(), ctx);
            Assert.AreEqual(3, src.Population, "3 homeless is not more than 3");
            Assert.AreEqual(0, dest.Population);
            Assert.AreEqual(0, other.Population);
        }

        [Test]
        public void FamiliesMoveTogether()
        {
            var ctx = Kit.CreateContext(128);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 80, out var civ);
            var mother = Kit.AddPerson(ctx, src, Sex.Female, 30f);
            var father = Kit.AddPerson(ctx, src, Sex.Male, 31f);
            Kit.Marry(mother, father);
            var child = Kit.AddPerson(ctx, src, Sex.Female, 4f, mother.Id, father.Id);
            var dest = Destination(ctx, civ, new Vec2i(60, 60), 4);
            Kit.RunOnce(new HousingSystem(), ctx);

            int moved = new MigrationSystem().MigrateWithinCiv(ctx, src, civ);

            Assert.AreEqual(4, moved, "floor(83 * 5%) = 4");
            Assert.AreEqual(dest.Id, mother.SettlementId);
            Assert.AreEqual(dest.Id, father.SettlementId);
            Assert.AreEqual(dest.Id, child.SettlementId);
            Kit.RunOnce(new HousingSystem(), ctx);
            Assert.AreEqual(mother.HomeId, father.HomeId);
            Assert.AreEqual(mother.HomeId, child.HomeId);
            Assert.IsTrue(mother.HomeId >= 0);
        }

        [Test]
        public void ChildrenNeverMoveAlone()
        {
            var ctx = Kit.CreateContext(128);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out var civ);
            for (int i = 0; i < 10; i++) Kit.AddPerson(ctx, src, Sex.Male, 6f); // orphans
            var dest = Destination(ctx, civ, new Vec2i(60, 20), 4);
            Kit.RunOnce(new HousingSystem(), ctx);
            Kit.RunOnce(new MigrationSystem(), ctx);
            Assert.AreEqual(0, dest.Population);
        }

        [Test]
        public void LeaderAndSoldiersInArmiesStay()
        {
            var ctx = Kit.CreateContext(128);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out var civ);
            var leader = Kit.AddPerson(ctx, src, Sex.Female, 40f);
            var soldier = Kit.AddPerson(ctx, src, Sex.Male, 25f);
            for (int i = 0; i < 4; i++) Kit.AddPerson(ctx, src, Sex.Male, 70f).Happiness = 0.9f;
            civ.LeaderId = leader.Id;
            ctx.Ops.CreateArmy(civ, src, new[] { soldier });
            leader.Happiness = soldier.Happiness = 0f;
            Destination(ctx, civ, new Vec2i(60, 20), 4);
            Kit.RunOnce(new HousingSystem(), ctx);
            var sys = new MigrationSystem();
            for (int i = 0; i < 10; i++) sys.MigrateWithinCiv(ctx, src, civ);
            Assert.AreEqual(src.Id, leader.SettlementId);
            Assert.AreEqual(src.Id, soldier.SettlementId);
        }

        [Test]
        public void RestlessPeopleDefectToMuchHappierNeighbour()
        {
            var ctx = Kit.CreateContext(128);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 20, out var civ);
            var haven = Kit.Settlement(ctx, new Vec2i(50, 20), 5, out var otherCiv);
            foreach (var p in Kit.Residents(ctx, src)) p.Happiness = 0.1f;
            src.Happiness = 0.1f;
            src.Unrest = 0.95f;
            haven.Happiness = 0.9f;
            var sys = new MigrationSystem();
            int total = 0;
            for (int i = 0; i < 12 && total == 0; i++)
            {
                int n = sys.TryDefect(ctx, src, civ);
                Assert.IsTrue(n <= MigrationSystem.MaxDefectorsPerRun);
                total += n;
            }
            Assert.IsTrue(total > 0, "someone should defect within 12 tries");
            Assert.AreEqual(5 + total, haven.Population);
            foreach (var p in Kit.Residents(ctx, haven)) Assert.AreEqual(otherCiv.Id, p.CivilizationId);
        }

        [Test]
        public void NoDefectionWithoutHighUnrestOrHappierNeighbour()
        {
            var ctx = Kit.CreateContext(128);
            var src = Kit.Settlement(ctx, new Vec2i(20, 20), 20, out _);
            var other = Kit.Settlement(ctx, new Vec2i(50, 20), 5, out _);
            foreach (var p in Kit.Residents(ctx, src)) p.Happiness = 0.1f;
            src.Happiness = 0.3f;
            other.Happiness = 0.4f; // not much happier
            src.Unrest = 0.95f;
            var sys = new MigrationSystem();
            for (int i = 0; i < 20; i++) Kit.RunOnce(sys, ctx);
            Assert.AreEqual(20, src.Population);
            other.Happiness = 0.95f;
            src.Unrest = 0.5f; // calm
            for (int i = 0; i < 20; i++) Kit.RunOnce(sys, ctx);
            Assert.AreEqual(20, src.Population);
        }
    }
}
