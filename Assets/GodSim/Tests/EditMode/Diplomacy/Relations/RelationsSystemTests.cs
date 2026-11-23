using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Diplomacy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Relations
{
    public class RelationsSystemTests
    {
        [Test]
        public void Opinion_DriftsPositiveForFriendlyRaces_AndNegativeForHostileRaces()
        {
            var db = RelationsTestKit.Db(r =>
            {
                RelationsTestKit.Like(r, RaceId.Human, RaceId.Elf);
                RelationsTestKit.Hate(r, RaceId.Dwarf, RaceId.Orc);
            });
            var ctx = RelationsTestKit.Ctx(db);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var elf = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(50, 15));
            var dwarf = RelationsTestKit.Civ(ctx, RaceId.Dwarf, new Vec2i(15, 100));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(50, 100));
            var friends = RelationsTestKit.Contact(ctx, human, elf, 0f);
            var foes = RelationsTestKit.Contact(ctx, dwarf, orc, 0f);

            TestWorlds.Run(new RelationsSystem(), ctx, 24 * 3);
            Assert.IsTrue(friends.Opinion > 0f && friends.Opinion < 20f, "drifts gradually: " + friends.Opinion);
            Assert.IsTrue(foes.Opinion < 0f && foes.Opinion > -20f, "drifts gradually: " + foes.Opinion);

            TestWorlds.Run(new RelationsSystem(), ctx, 24 * 60, false);
            Assert.IsTrue(friends.Opinion > 30f, "friendly opinion " + friends.Opinion);
            Assert.IsTrue(foes.Opinion < -40f, "hostile opinion " + foes.Opinion);
            Assert.IsTrue(friends.Opinion <= 100f && foes.Opinion >= -100f);
        }

        [Test]
        public void Opinion_FirstRunClosesTenPercentOfTheGap()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            var r = RelationsTestKit.Contact(ctx, a, b, 0f);
            // same race: 30*1 affinity + 10*0.5 diplomacy = 35 (no trade yet, far apart, neutral god favor)
            float target = RelationsSystem.ComputeTargetOpinion(ctx.State, ctx.Db, r);
            Assert.AreEqual(35f, target, 1e-3f);

            TestWorlds.Run(new RelationsSystem(), ctx, 24);

            Assert.AreEqual(3.5f, r.Opinion, 1e-3f);
        }

        [Test]
        public void TargetOpinion_IncludesEveryTerm()
        {
            var db = RelationsTestKit.Db(r =>
            {
                r[(int)RaceId.Human].RaceAffinity[(int)RaceId.Elf] = 0.5f;
                r[(int)RaceId.Elf].RaceAffinity[(int)RaceId.Human] = -0.1f; // avg 0.2 -> +6
                r[(int)RaceId.Human].Xenophobia = 0.2f;
                r[(int)RaceId.Elf].Xenophobia = 0.6f;                         // avg 0.4 -> -8
                r[(int)RaceId.Human].Diplomacy = 0.6f;
                r[(int)RaceId.Elf].Diplomacy = 0.2f;                          // avg 0.4 -> +4
            });
            var ctx = RelationsTestKit.Ctx(db);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var elf = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(35, 20));   // 15 tiles: one border pair -> -10
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(100, 100));
            var r = RelationsTestKit.Contact(ctx, human, elf, 0f);
            float baseTarget = 6f - 8f + 4f - 10f;
            Assert.AreEqual(baseTarget, RelationsSystem.ComputeTargetOpinion(ctx.State, ctx.Db, r), 1e-3f);

            r.TradeAgreement = true;
            human.GodFavor = 0.5f; elf.GodFavor = 0.3f;
            Assert.AreEqual(baseTarget + 15f + 8f, RelationsSystem.ComputeTargetOpinion(ctx.State, ctx.Db, r), 1e-3f);

            RelationsTestKit.StartWar(ctx, human, orc);
            RelationsTestKit.StartWar(ctx, elf, orc);
            Assert.AreEqual(baseTarget + 15f + 8f + 10f, RelationsSystem.ComputeTargetOpinion(ctx.State, ctx.Db, r), 1e-3f, "shared enemy");

            ctx.State.Memory[RelationsMemoryKeys.LastWar(human.Id, elf.Id)] = 0f; // fought recently
            Assert.AreEqual(baseTarget + 15f + 8f + 10f - 25f, RelationsSystem.ComputeTargetOpinion(ctx.State, ctx.Db, r), 1e-3f, "recent war");
            ctx.State.Tick = SimTime.YearsToTicks(21f);
            Assert.AreEqual(baseTarget + 15f + 8f + 10f, RelationsSystem.ComputeTargetOpinion(ctx.State, ctx.Db, r), 1e-3f, "old war forgotten");
        }

        [Test]
        public void BorderFriction_ScalesWithClosePairs_UpToThirty()
        {
            Assert.AreEqual(0f, RelationsSystem.BorderFriction(0));
            Assert.AreEqual(10f, RelationsSystem.BorderFriction(1));
            Assert.AreEqual(20f, RelationsSystem.BorderFriction(2));
            Assert.AreEqual(30f, RelationsSystem.BorderFriction(3));
            Assert.AreEqual(30f, RelationsSystem.BorderFriction(12));
        }

        [Test]
        public void Opinion_IsLowerForCrowdedBorders()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 20));
            var close = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(32, 20));
            var far = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 70));
            var rc = RelationsTestKit.Contact(ctx, a, close, 0f);
            var rf = RelationsTestKit.Contact(ctx, a, far, 0f);
            TestWorlds.Run(new RelationsSystem(), ctx, 24 * 10);
            Assert.IsTrue(rc.Opinion < rf.Opinion, rc.Opinion + " vs " + rf.Opinion);
        }

        [TestCase(DiplomaticStance.Neutral, 59f, true, DiplomaticStance.Friendly)]
        [TestCase(DiplomaticStance.Neutral, 60f, true, DiplomaticStance.Allied)]
        [TestCase(DiplomaticStance.Neutral, 80f, false, DiplomaticStance.Friendly)]
        [TestCase(DiplomaticStance.Allied, 50f, true, DiplomaticStance.Allied)]
        [TestCase(DiplomaticStance.Allied, 45f, true, DiplomaticStance.Allied)]
        [TestCase(DiplomaticStance.Allied, 44.9f, true, DiplomaticStance.Friendly)]
        [TestCase(DiplomaticStance.Allied, 14f, true, DiplomaticStance.Neutral)]
        [TestCase(DiplomaticStance.Friendly, 20f, true, DiplomaticStance.Friendly)]
        [TestCase(DiplomaticStance.Friendly, 15f, true, DiplomaticStance.Friendly)]
        [TestCase(DiplomaticStance.Friendly, 14.9f, true, DiplomaticStance.Neutral)]
        [TestCase(DiplomaticStance.Neutral, 20f, true, DiplomaticStance.Neutral)]
        [TestCase(DiplomaticStance.Neutral, 25f, true, DiplomaticStance.Friendly)]
        [TestCase(DiplomaticStance.Neutral, -24.9f, true, DiplomaticStance.Neutral)]
        [TestCase(DiplomaticStance.Neutral, -25f, true, DiplomaticStance.Hostile)]
        [TestCase(DiplomaticStance.Hostile, -20f, true, DiplomaticStance.Neutral)]
        [TestCase(DiplomaticStance.War, 0f, true, DiplomaticStance.Neutral)]
        [TestCase(DiplomaticStance.Hostile, 70f, true, DiplomaticStance.Allied)]
        public void NextStance_AppliesThresholdsWithHysteresis(DiplomaticStance current, float opinion, bool canAlly, DiplomaticStance expected)
        {
            Assert.AreEqual(expected, RelationsSystem.NextStance(current, opinion, canAlly));
        }

        [Test]
        public void Stances_ChangeWithHysteresis_PublishEvents_AndChronicleAlliances()
        {
            var db = RelationsTestKit.Db(r => r[(int)RaceId.Human].Diplomacy = 0.9f);
            var ctx = RelationsTestKit.Ctx(db);
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            a.GodFavor = 0.5f; b.GodFavor = 0.5f; // target = 30 + 9 + 8 (+15 trade) = 62
            var rel = RelationsTestKit.Contact(ctx, a, b, 0f);
            var changes = new List<StanceChangedEvent>();
            ctx.Events.Subscribe<StanceChangedEvent>(e => changes.Add(e));
            var sys = new RelationsSystem();

            TestWorlds.Run(sys, ctx, 24 * 80);

            Assert.AreEqual(DiplomaticStance.Allied, rel.Stance, "opinion " + rel.Opinion);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(DiplomaticStance.Neutral, changes[0].OldStance);
            Assert.AreEqual(DiplomaticStance.Friendly, changes[0].NewStance);
            Assert.AreEqual(DiplomaticStance.Friendly, changes[1].OldStance);
            Assert.AreEqual(DiplomaticStance.Allied, changes[1].NewStance);
            Assert.AreEqual(a.Id, changes[1].CivA);
            Assert.AreEqual(b.Id, changes[1].CivB);
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "forged an alliance"));

            // A dip that stays above 45 keeps the alliance (hysteresis) ...
            rel.Opinion = 47f;
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.AreEqual(DiplomaticStance.Allied, rel.Stance);
            Assert.AreEqual(2, changes.Count);

            // ... a real drop breaks it, but only to Friendly.
            rel.Opinion = 30f;
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.AreEqual(DiplomaticStance.Friendly, rel.Stance);
            Assert.AreEqual(3, changes.Count);
            Assert.AreEqual(DiplomaticStance.Allied, changes[2].OldStance);
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "dissolved"));
        }

        [Test]
        public void Alliance_RequiresDiplomaticRaces()
        {
            var db = RelationsTestKit.Db(r => r[(int)RaceId.Human].Diplomacy = 0.3f);
            var ctx = RelationsTestKit.Ctx(db);
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            var rel = RelationsTestKit.Contact(ctx, a, b, 95f, DiplomaticStance.Friendly);
            TestWorlds.Run(new RelationsSystem(), ctx, 24);
            Assert.AreEqual(DiplomaticStance.Friendly, rel.Stance);
        }

        [Test]
        public void TradePact_TogglesWithOpinionAndMercantilism()
        {
            var db = RelationsTestKit.Db(r => r[(int)RaceId.Human].Mercantilism = 0.6f);
            var ctx = RelationsTestKit.Ctx(db);
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            var rel = RelationsTestKit.Contact(ctx, a, b, 0f);
            var sys = new RelationsSystem();

            TestWorlds.Run(sys, ctx, 24 * 3);
            Assert.IsFalse(rel.TradeAgreement, "opinion below 15: " + rel.Opinion);
            TestWorlds.Run(sys, ctx, 24 * 10, false);
            Assert.IsTrue(rel.TradeAgreement, "opinion " + rel.Opinion);
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "signed a trade pact"));

            rel.Opinion = 5f; // between off (0) and on (15): kept
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.IsTrue(rel.TradeAgreement);

            rel.Opinion = -10f;
            TestWorlds.Run(sys, ctx, 24, false);
            Assert.IsFalse(rel.TradeAgreement);
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "lapsed"));
        }

        [Test]
        public void TradePact_NeverSignedByUnmercantileRaces()
        {
            var db = RelationsTestKit.Db(r => r[(int)RaceId.Human].Mercantilism = 0.2f);
            var ctx = RelationsTestKit.Ctx(db);
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            var rel = RelationsTestKit.Contact(ctx, a, b, 90f);
            TestWorlds.Run(new RelationsSystem(), ctx, 24 * 10);
            Assert.IsFalse(rel.TradeAgreement);
        }

        [Test]
        public void War_ForcesWarStance_AndCancelsTrade()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            var rel = RelationsTestKit.Contact(ctx, a, b, 50f, DiplomaticStance.Friendly);
            rel.TradeAgreement = true;
            var w = RelationsTestKit.StartWar(ctx, a, b);
            rel.Stance = DiplomaticStance.Friendly; // inconsistent on purpose

            TestWorlds.Run(new RelationsSystem(), ctx, 24 * 10);

            Assert.AreEqual(DiplomaticStance.War, rel.Stance);
            Assert.IsFalse(rel.TradeAgreement);
            Assert.IsTrue(w.IsActive, "relations never end wars");
        }

        [Test]
        public void Relations_WithDeadCivOrWithoutContact_AreNotUpdated()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            var c = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 60));
            var withDead = RelationsTestKit.Contact(ctx, a, b, -10f);
            b.IsAlive = false;
            var noContact = ctx.State.Diplomacy.Get(a.Id, c.Id);
            noContact.Opinion = 7f;

            TestWorlds.Run(new RelationsSystem(), ctx, 24 * 5);

            Assert.AreEqual(-10f, withDead.Opinion);
            Assert.AreEqual(7f, noContact.Opinion);
        }

        [Test]
        public void Opinion_IsClampedAndSanitized()
        {
            var ctx = RelationsTestKit.Ctx(RelationsTestKit.Db());
            var a = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(15, 15));
            var b = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 15));
            var rel = RelationsTestKit.Contact(ctx, a, b, float.NaN);
            a.GodFavor = float.PositiveInfinity;
            TestWorlds.Run(new RelationsSystem(), ctx, 24);
            Assert.IsFalse(float.IsNaN(rel.Opinion) || float.IsInfinity(rel.Opinion));
            rel.Opinion = 500f;
            TestWorlds.Run(new RelationsSystem(), ctx, 24, false);
            Assert.IsTrue(rel.Opinion <= 100f && rel.Opinion >= -100f);
        }
    }
}
