using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Diplomacy;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Relations
{
    public class DiplomacyQueriesTests
    {
        [Test]
        public void Opinion_AndStance_HandleSelfMissingAndExisting()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(60, 20));
            Assert.AreEqual(100f, DiplomacyQueries.Opinion(ctx.State, a.Id, a.Id));
            Assert.AreEqual(0f, DiplomacyQueries.Opinion(ctx.State, a.Id, b.Id));
            Assert.AreEqual(0f, DiplomacyQueries.Opinion(null, a.Id, b.Id));
            Assert.AreEqual(DiplomaticStance.Neutral, DiplomacyQueries.Stance(ctx.State, a.Id, b.Id));
            Assert.IsFalse(ctx.State.Diplomacy.TryGet(a.Id, b.Id, out _), "queries never create relations");

            RelationsTestKit.Contact(ctx, a, b, -33f, DiplomaticStance.Hostile);
            Assert.AreEqual(-33f, DiplomacyQueries.Opinion(ctx.State, b.Id, a.Id));
            Assert.AreEqual(DiplomaticStance.Hostile, DiplomacyQueries.Stance(ctx.State, b.Id, a.Id));
        }

        [Test]
        public void IsHostile_TrueForHostileStanceOrWar()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(60, 20));
            var c = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 60));
            Assert.IsFalse(DiplomacyQueries.IsHostile(ctx.State, a.Id, b.Id));
            var r = RelationsTestKit.Contact(ctx, a, b, 0f, DiplomaticStance.Neutral);
            Assert.IsFalse(DiplomacyQueries.IsHostile(ctx.State, a.Id, b.Id));
            r.Stance = DiplomaticStance.Hostile;
            Assert.IsTrue(DiplomacyQueries.IsHostile(ctx.State, b.Id, a.Id));
            RelationsTestKit.StartWar(ctx, a, c);
            Assert.IsTrue(DiplomacyQueries.IsHostile(ctx.State, a.Id, c.Id));
            Assert.IsFalse(DiplomacyQueries.IsHostile(ctx.State, a.Id, a.Id));
        }

        [Test]
        public void StrengthRatio_UsesMaxOneDenominator()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(60, 20), AgeId.Bronze, 20f);
            var c = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 60), AgeId.Bronze, 0f);
            Assert.AreEqual(5f, DiplomacyQueries.StrengthRatio(ctx.State, a.Id, b.Id), 1e-4f);
            Assert.AreEqual(0.2f, DiplomacyQueries.StrengthRatio(ctx.State, b.Id, a.Id), 1e-4f);
            Assert.AreEqual(100f, DiplomacyQueries.StrengthRatio(ctx.State, a.Id, c.Id), 1e-4f);
            Assert.AreEqual(0f, DiplomacyQueries.StrengthRatio(ctx.State, 999, a.Id));
            Assert.AreEqual(100f, DiplomacyQueries.StrengthRatio(ctx.State, a.Id, 999), 1e-4f);
            a.MilitaryPower = float.NaN;
            Assert.AreEqual(0f, DiplomacyQueries.StrengthRatio(ctx.State, a.Id, b.Id));
        }

        [Test]
        public void Neighbours_ListsAliveContactsAscending_WithOptionalDistance()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 60));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(70, 60));
            var c = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(100, 60));
            var d = RelationsTestKit.Civ(ctx, RaceId.Dwarf, new Vec2i(20, 20));
            var e = RelationsTestKit.Civ(ctx, RaceId.Saurian, new Vec2i(60, 20));
            // a has id between others? relations created out of order on purpose
            RelationsTestKit.Contact(ctx, c, a, 0f);
            RelationsTestKit.Contact(ctx, a, b, 0f);
            RelationsTestKit.Contact(ctx, d, a, 0f);
            ctx.State.Diplomacy.Get(a.Id, e.Id); // no contact
            RelationsTestKit.Contact(ctx, b, c, 0f); // not a's relation
            d.IsAlive = false;

            var list = new List<int> { 42 };
            int n = DiplomacyQueries.Neighbours(ctx.State, a.Id, list);
            Assert.AreEqual(2, n);
            Assert.AreEqual(b.Id, list[0]);
            Assert.AreEqual(c.Id, list[1]);

            n = DiplomacyQueries.Neighbours(ctx.State, a.Id, list, 20);
            Assert.AreEqual(1, n);
            Assert.AreEqual(b.Id, list[0]);

            Assert.AreEqual(0, DiplomacyQueries.Neighbours(ctx.State, d.Id, list), "dead civs have no neighbours");
            Assert.AreEqual(0, DiplomacyQueries.Neighbours(ctx.State, a.Id, null));
        }

        [Test]
        public void Neighbours_AreAscendingForAMiddleId()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var c1 = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var c2 = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(40, 20));
            var c3 = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(60, 20));
            var c4 = RelationsTestKit.Civ(ctx, RaceId.Dwarf, new Vec2i(80, 20));
            RelationsTestKit.Contact(ctx, c3, c4, 0f);
            RelationsTestKit.Contact(ctx, c3, c1, 0f);
            RelationsTestKit.Contact(ctx, c2, c3, 0f);
            var list = new List<int>();
            DiplomacyQueries.Neighbours(ctx.State, c3.Id, list);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(c1.Id, list[0]);
            Assert.AreEqual(c2.Id, list[1]);
            Assert.AreEqual(c4.Id, list[2]);
        }

        [Test]
        public void WarHelpers_CountWarsAndSharedEnemies()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(60, 20));
            var c = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 60));
            Assert.IsFalse(DiplomacyQueries.ShareEnemy(ctx.State, a.Id, b.Id));
            RelationsTestKit.StartWar(ctx, a, c);
            Assert.IsFalse(DiplomacyQueries.ShareEnemy(ctx.State, a.Id, b.Id));
            var w = RelationsTestKit.StartWar(ctx, c, b);
            Assert.IsTrue(DiplomacyQueries.ShareEnemy(ctx.State, a.Id, b.Id));
            Assert.IsFalse(DiplomacyQueries.ShareEnemy(ctx.State, a.Id, c.Id), "an enemy is not a shared enemy");
            Assert.AreEqual(2, DiplomacyQueries.ActiveWarCount(ctx.State, c.Id));
            w.IsActive = false;
            Assert.AreEqual(1, DiplomacyQueries.ActiveWarCount(ctx.State, c.Id));
            Assert.IsFalse(DiplomacyQueries.ShareEnemy(ctx.State, a.Id, b.Id));
        }

        [Test]
        public void Distances_AndBorderPairs()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var b = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(35, 20));
            ctx.Ops.FoundSettlement(b, new Vec2i(20, 38));
            Assert.AreEqual(15, DiplomacyQueries.MinSettlementDistance(ctx.State, a.Id, b.Id));
            Assert.AreEqual(2, DiplomacyQueries.BorderPairs(ctx.State, a.Id, b.Id));
            Assert.AreEqual(1, DiplomacyQueries.BorderPairs(ctx.State, a.Id, b.Id, 15));
            Assert.AreEqual(int.MaxValue, DiplomacyQueries.MinSettlementDistance(ctx.State, a.Id, 999));
        }

        [Test]
        public void FavorSigns_UseADeadZone()
        {
            Assert.IsTrue(DiplomacyQueries.FavorSignsMatch(0.5f, 0.1f));
            Assert.IsTrue(DiplomacyQueries.FavorSignsMatch(-0.5f, -0.1f));
            Assert.IsFalse(DiplomacyQueries.FavorSignsMatch(0f, 0f));
            Assert.IsFalse(DiplomacyQueries.FavorSignsMatch(0.5f, -0.5f));
            Assert.IsTrue(DiplomacyQueries.FavorSignsDiffer(0.5f, -0.5f));
            Assert.IsFalse(DiplomacyQueries.FavorSignsDiffer(0.5f, 0.01f));
            Assert.IsFalse(DiplomacyQueries.FavorSignsDiffer(float.NaN, -1f));
        }

        [Test]
        public void MemoryKeys_AreSymmetricAndNamespaced()
        {
            Assert.AreEqual("dipl.lastWar.3.7", RelationsMemoryKeys.LastWar(7, 3));
            Assert.AreEqual(RelationsMemoryKeys.LastWar(3, 7), RelationsMemoryKeys.LastWar(7, 3));
            Assert.AreEqual("dipl.war.12.initialPower", RelationsMemoryKeys.WarInitialPower(12));
        }
    }
}
