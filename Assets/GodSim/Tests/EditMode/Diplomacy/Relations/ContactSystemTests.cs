using GodSim.Core;
using GodSim.Core.Diplomacy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Relations
{
    public class ContactSystemTests
    {
        [Test]
        public void Contact_IsMadeOnlyWithinRange_AndChronicledOnce()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(60, 20));
            var c = RelationsTestKit.Civ(ctx, RaceId.Dwarf, new Vec2i(110, 110));
            var sys = new ContactSystem();

            TestWorlds.Run(sys, ctx, 24);

            Assert.IsTrue(ctx.State.Diplomacy.TryGet(a.Id, b.Id, out var ab));
            Assert.IsTrue(ab.HasContact);
            Assert.AreEqual(24, ab.LastContactTick);
            Assert.IsFalse(ctx.State.Diplomacy.TryGet(a.Id, c.Id, out var ac) && ac.HasContact);
            Assert.IsFalse(ctx.State.Diplomacy.TryGet(b.Id, c.Id, out var bc) && bc.HasContact);
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "first contact"));

            TestWorlds.Run(sys, ctx, 24, false);
            Assert.AreEqual(48, ab.LastContactTick, "LastContactTick refreshes while in range");
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "first contact"), "first contact chronicled once");
        }

        [Test]
        public void Contact_RangeBoundaryIs45TilesChebyshev()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var near = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(65, 30));
            var far = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 66));
            var ca = RelationsTestKit.Capital(ctx, a).Center;
            Assert.AreEqual(45, ca.ChebyshevTo(RelationsTestKit.Capital(ctx, near).Center));
            Assert.AreEqual(46, ca.ChebyshevTo(RelationsTestKit.Capital(ctx, far).Center));

            TestWorlds.Run(new ContactSystem(), ctx, 24);

            Assert.IsTrue(ctx.State.Diplomacy.TryGet(a.Id, near.Id, out var rn) && rn.HasContact);
            Assert.IsFalse(ctx.State.Diplomacy.TryGet(a.Id, far.Id, out var rf) && rf.HasContact);
        }

        [Test]
        public void Contact_UsesAnySettlementOfTheCiv()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(10, 10));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(110, 110));
            ctx.Ops.FoundSettlement(a, new Vec2i(80, 80)); // colony close to b

            TestWorlds.Run(new ContactSystem(), ctx, 24);

            Assert.IsTrue(ctx.State.Diplomacy.TryGet(a.Id, b.Id, out var r) && r.HasContact);
        }

        [Test]
        public void Contact_IgnoresDeadCivsAndDeadSettlements()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var dead = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(40, 20));
            var ghostTown = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 40));
            dead.IsAlive = false;
            RelationsTestKit.Capital(ctx, ghostTown).IsAlive = false;

            TestWorlds.Run(new ContactSystem(), ctx, 48);

            Assert.IsFalse(ctx.State.Diplomacy.TryGet(a.Id, dead.Id, out var r1) && r1.HasContact);
            Assert.IsFalse(ctx.State.Diplomacy.TryGet(a.Id, ghostTown.Id, out var r2) && r2.HasContact);
            Assert.AreEqual(0, RelationsTestKit.CountChronicle(ctx, "first contact"));
        }

        [Test]
        public void Contact_IsKeptAfterMovingOutOfRange()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(50, 20));
            var sys = new ContactSystem();
            TestWorlds.Run(sys, ctx, 24);
            var r = ctx.State.Diplomacy.Get(a.Id, b.Id);
            Assert.IsTrue(r.HasContact);

            RelationsTestKit.Capital(ctx, b).Center = new Vec2i(120, 120);
            TestWorlds.Run(sys, ctx, 24, false);

            Assert.IsTrue(r.HasContact);
            Assert.AreEqual(24, r.LastContactTick, "not refreshed while out of range");
        }

        [Test]
        public void Contact_HandlesEmptyWorld()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            Assert.DoesNotThrow(() => TestWorlds.Run(new ContactSystem(), ctx, 48));
        }
    }
}
