using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Population;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Population.Lifecycle
{
    public class AgingSystemTests
    {
        [Test]
        public void PeopleAge_AndMoveThroughLifeStages()
        {
            var db = TestDb.CreateStub(); // stub race: adult at 16, elder at 55, life expectancy 70
            var ctx = TestWorlds.CreateContext(db, seed: 3);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var child = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 15.5f);
            var adult = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 54.5f);
            child.Traits = PersonTraits.None;
            adult.Traits = PersonTraits.None;
            Assert.AreEqual(LifeStage.Child, child.Stage);
            Assert.AreEqual(LifeStage.Adult, adult.Stage);

            TestWorlds.Run(new AgingSystem(), ctx, SimTime.TicksPerYear);

            Assert.IsTrue(child.IsAlive && adult.IsAlive);
            Assert.AreEqual(16.5f, child.AgeYears, 1e-3f);
            Assert.AreEqual(55.5f, adult.AgeYears, 1e-3f);
            Assert.AreEqual(LifeStage.Adult, child.Stage);
            Assert.AreEqual(LifeStage.Elder, adult.Stage);
        }

        [Test]
        public void Soldiers_RetireOnBecomingElders()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, seed: 5);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var veteran = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 54.95f);
            var young = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 30f);
            var farmer = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 54.95f);
            veteran.Traits = young.Traits = farmer.Traits = PersonTraits.None;
            ctx.Ops.AssignJob(veteran, null, JobType.Soldier);
            ctx.Ops.AssignJob(young, null, JobType.Soldier);
            ctx.Ops.AssignJob(farmer, null, JobType.Farmer);

            TestWorlds.Run(new AgingSystem(), ctx, 16);

            Assert.AreEqual(LifeStage.Elder, veteran.Stage);
            Assert.AreEqual(JobType.None, veteran.Job);
            Assert.AreEqual(JobType.Soldier, young.Job);
            Assert.AreEqual(JobType.Farmer, farmer.Job, "only soldiers retire");
        }

        [Test]
        public void TinySettlement_400Years_EldersDie_NobodyExceedsMaxAge()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, seed: 11);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            for (int i = 0; i < 6; i++) LifecycleTestSupport.Add(ctx, s, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 20f + i);
            LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 2f);
            float maxAge = 70f * LifecycleBalance.MaxAgeFactor;

            float oldest = 0f;
            int oldAge = 0, elderDeaths = 0, deaths = 0;
            ctx.Events.Subscribe<PersonDiedEvent>(e =>
            {
                deaths++;
                if (e.AgeYears > oldest) oldest = e.AgeYears;
                if (e.Cause == DeathCause.OldAge) oldAge++;
                if (e.AgeYears >= 55f) elderDeaths++;
            });

            var aging = new AgingSystem();
            TestWorlds.Run(aging, ctx, 0);
            for (int year = 0; year < 400; year++)
            {
                TestWorlds.Run(aging, ctx, SimTime.TicksPerYear, false);
                foreach (var p in ctx.State.People.Items)
                    Assert.IsTrue(p.AgeYears <= maxAge + 1e-3f, "age " + p.AgeYears + " exceeds 1.5 x life expectancy");
            }

            Assert.AreEqual(7, deaths);
            Assert.AreEqual(0, ctx.State.LivingPopulation, "everybody dies of old age eventually");
            Assert.IsTrue(oldest <= maxAge + 1e-3f, "oldest death at " + oldest);
            Assert.IsTrue(oldAge >= 1, "some deaths should be from old age");
            Assert.IsTrue(elderDeaths >= 1, "some people should live to become elders");
        }

        [Test]
        public void HardCap_KillsAtExactlyOnePointFiveLifeExpectancy()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var ancient = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 104.99f);
            DeathCause cause = DeathCause.Accident;
            float age = 0f;
            ctx.Events.Subscribe<PersonDiedEvent>(e => { if (e.PersonId == ancient.Id) { cause = e.Cause; age = e.AgeYears; } });

            TestWorlds.Run(new AgingSystem(), ctx, 8);

            Assert.IsFalse(ancient.IsAlive);
            Assert.AreEqual(DeathCause.OldAge, cause);
            Assert.AreEqual(105f, age, 1e-4f);
        }

        [Test]
        public void LifespanBonus_RaisesTheCap()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out var civ);
            civ.Modifiers.LifespanBonusYears = 30f;
            Assert.AreEqual(100f, LifecycleRules.EffectiveLifeExpectancy(db.Race(RaceId.Human), civ), 1e-4f);

            var oldTimer = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 106f);
            oldTimer.Traits = PersonTraits.None;
            float maxSeen = 0f;
            ctx.Events.Subscribe<PersonDiedEvent>(e => { if (e.AgeYears > maxSeen) maxSeen = e.AgeYears; });
            TestWorlds.Run(new AgingSystem(), ctx, 60 * SimTime.TicksPerYear);

            Assert.IsFalse(oldTimer.IsAlive);
            Assert.IsTrue(maxSeen > 106f && maxSeen <= 150f + 1e-3f, "died at " + maxSeen);
        }

        [Test]
        public void MortalityCurve_MatchesDesignPoints()
        {
            Assert.AreEqual(0.003f, LifecycleRules.SenescenceHazard(0.3f), 1e-6f);
            Assert.AreEqual(0.003f, LifecycleRules.SenescenceHazard(0.6f), 1e-5f);
            Assert.AreEqual(0.5f, LifecycleRules.SenescenceHazard(1.0f), 0.01f);
            Assert.AreEqual(0.95f, LifecycleRules.SenescenceHazard(1.3f), 0.01f);
            float prev = 0f;
            for (float x = 0.6f; x <= 1.5f; x += 0.01f)
            {
                float h = LifecycleRules.SenescenceHazard(x);
                Assert.IsTrue(h >= prev - 1e-6f, "hazard must rise with age at " + x);
                prev = h;
            }

            // Health and traits.
            float healthy = LifecycleRules.MortalityPerYear(LifeStage.Adult, 30f, 70f, 1f, PersonTraits.None, 0f);
            float frail = LifecycleRules.MortalityPerYear(LifeStage.Adult, 30f, 70f, 0f, PersonTraits.None, 0f);
            float sickly = LifecycleRules.MortalityPerYear(LifeStage.Adult, 30f, 70f, 1f, PersonTraits.Sickly, 0f);
            float hardy = LifecycleRules.MortalityPerYear(LifeStage.Adult, 30f, 70f, 1f, PersonTraits.Hardy, 0f);
            Assert.AreEqual(0.003f * 0.5f, healthy, 1e-6f);
            Assert.AreEqual(0.003f * 1.5f, frail, 1e-6f);
            Assert.AreEqual(healthy * 1.5f, sickly, 1e-6f);
            Assert.AreEqual(healthy * 0.7f, hardy, 1e-6f);

            // Children: small, reduced by health care.
            float child = LifecycleRules.MortalityPerYear(LifeStage.Child, 5f, 70f, 1f, PersonTraits.None, 0f);
            float childCared = LifecycleRules.MortalityPerYear(LifeStage.Child, 5f, 70f, 1f, PersonTraits.None, 2f);
            Assert.AreEqual(0.02f * 0.2f, child, 1e-6f);
            Assert.AreEqual(0.02f * 0.2f * 0.5f, childCared, 1e-6f);

            Assert.AreEqual(DeathCause.Disease, LifecycleRules.NaturalDeathCause(41f, 70f));
            Assert.AreEqual(DeathCause.OldAge, LifecycleRules.NaturalDeathCause(42f, 70f));
            Assert.IsFalse(float.IsNaN(LifecycleRules.MortalityPerYear(LifeStage.Adult, float.NaN, float.NaN, float.NaN, PersonTraits.None, float.NaN)));
        }

        [Test]
        public void Tick_SkipsDeadPeopleAndInvalidRaces()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var dead = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 30f);
            var weird = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 30f);
            var nan = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 30f);
            ctx.Ops.KillPerson(dead, DeathCause.Accident); // not flushed: still in the store
            weird.Race = (RaceId)200;
            nan.AgeYears = float.NaN;
            nan.CivilizationId = -1;

            var aging = new AgingSystem();
            ctx.State.Tick = 8;
            ctx.DeltaTicks = 8;
            Assert.DoesNotThrow(() => aging.Tick(ctx));
            Assert.AreEqual(30f, dead.AgeYears, 1e-6f);
            Assert.AreEqual(30f, weird.AgeYears, 1e-6f);
            Assert.IsFalse(float.IsNaN(nan.AgeYears));
        }

        [Test]
        public void LongLivedRace_UsesItsOwnLifeExpectancy()
        {
            var db = LifecycleTestSupport.CreateDb(d =>
            {
                var elf = d.Race(RaceId.Elf);
                elf.LifeExpectancy = 350f;
                elf.AdulthoodAge = 30f;
                elf.ElderAge = 280f;
            });
            var ctx = TestWorlds.CreateContext(db, seed: 21);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Elf, new Vec2i(32, 32), 100f, out _);
            var elves = new List<Person>();
            for (int i = 0; i < 10; i++)
            {
                var e = LifecycleTestSupport.Add(ctx, s, RaceId.Elf, i % 2 == 0 ? Sex.Female : Sex.Male, 100f);
                e.Traits = PersonTraits.None;
                elves.Add(e);
            }

            // 70 years: a human would be near the end of life; an elf at 170 is in its prime.
            TestWorlds.Run(new AgingSystem(), ctx, 70 * SimTime.TicksPerYear);

            int alive = 0;
            foreach (var e in elves) if (e.IsAlive) { alive++; Assert.AreEqual(LifeStage.Adult, e.Stage); }
            Assert.IsTrue(alive >= 7, "elves should mostly survive 70 years of adulthood, alive=" + alive);
        }
    }
}
