using GodSim.Core;
using GodSim.Core.Population;
using NUnit.Framework;
using Kit = GodSim.Tests.Population.SocietyTestKit;

namespace GodSim.Tests.Population
{
    public class LeadershipSystemTests
    {
        private static int CountLeaderEntries(SimContext ctx)
        {
            int n = 0;
            foreach (var e in ctx.State.Chronicle.Entries) if (e.Text.Contains(" rose to lead the ")) n++;
            return n;
        }

        [Test]
        public void BestAdultOfTheCapitalIsChosen()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 6, out var civ);
            var sage = Kit.AddPerson(ctx, s, Sex.Female, 45f);
            sage.Traits = PersonTraits.Wise | PersonTraits.Charismatic;
            Assert.AreEqual(-1, civ.LeaderId);

            Kit.RunOnce(new LeadershipSystem(), ctx);

            Assert.AreEqual(sage.Id, civ.LeaderId);
            var last = ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1];
            Assert.AreEqual(sage.Name + " rose to lead the " + civ.Name + ".", last.Text);
            Assert.AreEqual(1, last.Importance);
            Assert.AreEqual(civ.Id, last.CivilizationId);
        }

        [Test]
        public void LeaderIsReplacedOnDeath()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 6, out var civ);
            var sys = new LeadershipSystem();
            Kit.RunOnce(sys, ctx);
            var first = ctx.State.People.Get(civ.LeaderId);
            Assert.IsNotNull(first);

            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(first.Id, civ.LeaderId, "a living leader is kept");
            Assert.AreEqual(1, CountLeaderEntries(ctx));

            ctx.Ops.KillPerson(first, DeathCause.OldAge);
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(-1, civ.LeaderId);
            Kit.RunOnce(sys, ctx);

            var second = ctx.State.People.Get(civ.LeaderId);
            Assert.IsNotNull(second);
            Assert.IsTrue(second.IsAlive);
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.AreEqual(2, CountLeaderEntries(ctx));
            Assert.AreEqual(2f, civ.Mem(SocietyKeys.LeaderCount));
        }

        [Test]
        public void StaleOrForeignLeaderIdIsReplaced()
        {
            var ctx = Kit.CreateContext(96);
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 3, out var civ);
            var other = Kit.Settlement(ctx, new Vec2i(60, 60), 3, out var otherCiv);
            civ.LeaderId = 99999;
            var sys = new LeadershipSystem();
            Kit.RunOnce(sys, ctx);
            var leader = ctx.State.People.Get(civ.LeaderId);
            Assert.IsNotNull(leader);
            Assert.AreEqual(civ.Id, leader.CivilizationId);

            ctx.Ops.MovePersonToSettlement(leader, other); // defected
            Kit.RunOnce(sys, ctx);
            Assert.AreNotEqual(leader.Id, civ.LeaderId);
            Assert.AreEqual(civ.Id, ctx.State.People.Get(civ.LeaderId).CivilizationId);
        }

        [Test]
        public void ChildrenNeverLead()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out var civ);
            for (int i = 0; i < 3; i++) Kit.AddPerson(ctx, s, Sex.Male, 9f).Traits = PersonTraits.Hero | PersonTraits.Wise;
            var sys = new LeadershipSystem();
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(-1, civ.LeaderId);
            var adult = Kit.AddPerson(ctx, s, Sex.Female, 30f);
            Kit.RunOnce(sys, ctx);
            Assert.AreEqual(adult.Id, civ.LeaderId);
        }

        [Test]
        public void FallsBackToAnotherSettlementWhenCapitalIsEmpty()
        {
            var ctx = Kit.CreateContext(96);
            var capital = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out var civ);
            var town = ctx.Ops.FoundSettlement(civ, new Vec2i(60, 60));
            var p = Kit.AddPerson(ctx, town, Sex.Male, 35f);
            Assert.AreEqual(capital.Id, civ.CapitalId);
            Kit.RunOnce(new LeadershipSystem(), ctx);
            Assert.AreEqual(p.Id, civ.LeaderId);
        }

        [Test]
        public void ScoreFavorsTraits()
        {
            var race = TestDbHuman();
            var plain = new Person { Id = 1, AgeYears = 40f, Stage = LifeStage.Adult, Skill = 1f };
            var wise = new Person { Id = 2, AgeYears = 40f, Stage = LifeStage.Adult, Skill = 1f, Traits = PersonTraits.Wise };
            var hero = new Person { Id = 3, AgeYears = 40f, Stage = LifeStage.Adult, Skill = 1f, Traits = PersonTraits.Hero };
            var lazy = new Person { Id = 4, AgeYears = 40f, Stage = LifeStage.Adult, Skill = 1f, Traits = PersonTraits.Lazy };
            Assert.IsTrue(LeadershipSystem.Score(hero, race, -1) > LeadershipSystem.Score(wise, race, -1));
            Assert.IsTrue(LeadershipSystem.Score(wise, race, -1) > LeadershipSystem.Score(plain, race, -1));
            Assert.IsTrue(LeadershipSystem.Score(plain, race, -1) > LeadershipSystem.Score(lazy, race, -1));
        }

        private static RaceDefinition TestDbHuman() => Kit.CreateDb().Race(RaceId.Human);
    }
}
