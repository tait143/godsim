using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Heroes;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Heroes
{
    public class HeroRaiseTests
    {
        [TestCase(JobType.Soldier, HeroRole.General)]
        [TestCase(JobType.Scholar, HeroRole.Sage)]
        [TestCase(JobType.Priest, HeroRole.Prophet)]
        [TestCase(JobType.Builder, HeroRole.Architect)]
        [TestCase(JobType.Merchant, HeroRole.MerchantPrince)]
        [TestCase(JobType.Healer, HeroRole.Healer)]
        [TestCase(JobType.Entertainer, HeroRole.Bard)]
        [TestCase(JobType.Hunter, HeroRole.Explorer)]
        public void RoleIsMappedFromJob(JobType job, HeroRole expected)
        {
            Assert.AreEqual(expected, HeroRules.RoleForJob(job));

            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            var p = HeroTestKit.MakeEligible(ctx, s, 3, job);
            var sys = new HeroSystem { RaiseChanceScale = 1000f };
            TestWorlds.Run(sys, ctx, HeroSystem.Interval);

            Assert.IsTrue(HeroQueries.IsHero(ctx.State, p.Id));
            Assert.AreEqual(expected, HeroQueries.RoleOf(ctx.State, p.Id));
            Assert.IsTrue(p.HasTrait(PersonTraits.Hero));
            Assert.IsTrue(p.HasTrait(HeroRules.RoleTrait(expected)));
            Assert.IsTrue(ctx.State.Memory.ContainsKey("hero." + p.Id + ".role"));
            Assert.IsTrue(ctx.State.Memory.ContainsKey("hero." + p.Id + ".deeds"));
            Assert.AreEqual(1, HeroTestKit.LivingCount(ctx.State));
        }

        [TestCase(JobType.Farmer)]
        [TestCase(JobType.None)]
        [TestCase(JobType.Woodcutter)]
        public void JobsWithoutRoleProduceNoHeroes(JobType job)
        {
            Assert.IsNull(HeroRules.RoleForJob(job));
        }

        [Test]
        public void RiseIsChronicledWithImportanceOneOrTwo()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 12, out var civ);
            s.Name = "Grimhold";
            var p = HeroTestKit.MakeEligible(ctx, s, 0, JobType.Builder);
            p.Name = "Aldric Stonebrow";
            int before = ctx.State.Chronicle.Entries.Count;
            var sys = new HeroSystem { RaiseChanceScale = 1000f };
            TestWorlds.Run(sys, ctx, HeroSystem.Interval);

            var entries = ctx.State.Chronicle.Entries;
            Assert.AreEqual(before + 1, entries.Count);
            var e = entries[entries.Count - 1];
            Assert.AreEqual("Aldric Stonebrow rose to renown as the Architect of Grimhold.", e.Text);
            Assert.That(e.Importance, Is.InRange(1, 2));
            Assert.AreEqual(2, e.Importance, "first hero of a civ is a milestone");
            Assert.AreEqual(1f, civ.Mem(HeroKeys.CivRaisedCount));
        }

        [Test]
        public void OnlyEligibleAdultsArise()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 10, out var civ);
            // children with perfect stats and hero jobs
            for (int i = 0; i < 4; i++)
            {
                var child = ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Female, 5f);
                child.Traits = PersonTraits.Clever | PersonTraits.Wise;
                child.Skill = 1.4f;
                child.Job = JobType.Builder;
                Assert.IsFalse(child.IsAdult);
            }
            // skilled adults without a hero job
            HeroTestKit.Resident(ctx, s, 0).Skill = 1.4f;
            HeroTestKit.Resident(ctx, s, 1).Skill = 1.4f;
            // adults with a hero job but mediocre and lazy
            var lazy = HeroTestKit.Resident(ctx, s, 2);
            lazy.Job = JobType.Scholar; lazy.Skill = 1.1f; lazy.Traits = PersonTraits.Lazy;
            var plain = HeroTestKit.Resident(ctx, s, 3);
            plain.Job = JobType.Priest; plain.Skill = 1.0f;
            // dead promising adult
            var dead = HeroTestKit.MakeEligible(ctx, s, 4, JobType.Soldier);
            ctx.Ops.KillPerson(dead, DeathCause.Accident);

            var sys = new HeroSystem { RaiseChanceScale = 1000f };
            TestWorlds.Run(sys, ctx, HeroSystem.Interval * 10);
            Assert.AreEqual(0, HeroTestKit.LivingCount(ctx.State));
            Assert.AreEqual(0, HeroTestKit.CountHeroKeys(ctx.State));

            // A clever, decently skilled builder qualifies (1.05 + 0.15 >= 1.15).
            var ok = HeroTestKit.Resident(ctx, s, 5);
            ok.Job = JobType.Builder; ok.Skill = 1.05f; ok.Traits = PersonTraits.Clever;
            HeroTestKit.OneRun(sys, ctx);
            Assert.IsTrue(HeroQueries.IsHero(ctx.State, ok.Id));
            Assert.AreEqual(1, HeroTestKit.LivingCount(ctx.State));
        }

        [Test]
        public void MostPromisingCandidateWins()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            var a = HeroTestKit.MakeEligible(ctx, s, 1, JobType.Healer);
            a.Skill = 1.2f;
            var b = HeroTestKit.MakeEligible(ctx, s, 2, JobType.Entertainer);
            b.Skill = 1.3f; b.Traits = PersonTraits.Charismatic;
            var sys = new HeroSystem { RaiseChanceScale = 1000f };
            TestWorlds.Run(sys, ctx, HeroSystem.Interval);
            Assert.IsTrue(HeroQueries.IsHero(ctx.State, b.Id));
            Assert.IsFalse(HeroQueries.IsHero(ctx.State, a.Id));
            Assert.AreEqual(HeroRole.Bard, HeroQueries.RoleOf(ctx.State, b.Id));
        }

        [Test]
        public void CapOfThreePerCivAndOnePerSettlement()
        {
            var ctx = HeroTestKit.NewContext();
            var s0 = HeroTestKit.PlainSettlement(ctx, RaceId.Elf, new Vec2i(8, 8), 6, out var civ);
            var settlements = new List<Settlement> { s0 };
            var spots = new[] { new Vec2i(8, 40), new Vec2i(40, 8), new Vec2i(40, 40), new Vec2i(24, 24) };
            foreach (var at in spots)
            {
                var s = ctx.Ops.FoundSettlement(civ, at);
                for (int i = 0; i < 6; i++) ctx.Ops.CreatePerson(s, RaceId.Elf, i % 2 == 0 ? Sex.Female : Sex.Male, 30f);
                HeroTestKit.Normalize(ctx, s);
                settlements.Add(s);
            }
            foreach (var s in settlements)
                for (int i = 0; i < s.ResidentIds.Count; i++) HeroTestKit.MakeEligible(ctx, s, i, i % 2 == 0 ? JobType.Scholar : JobType.Builder);

            var sys = new HeroSystem { RaiseChanceScale = 1000f };
            TestWorlds.Run(sys, ctx, HeroSystem.Interval * 20);

            var living = new List<HeroInfo>(HeroQueries.Living(ctx.State));
            Assert.AreEqual(HeroRules.MaxHeroesPerCiv, living.Count);
            var seen = new HashSet<int>();
            foreach (var h in living)
            {
                Assert.AreEqual(civ.Id, h.CivilizationId);
                Assert.IsTrue(seen.Add(h.SettlementId), "at most one hero per settlement");
            }

            // a hero dies -> the slot can be refilled
            ctx.Ops.KillPerson(ctx.State.People.Get(living[0].PersonId), DeathCause.OldAge);
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(2, HeroTestKit.LivingCount(ctx.State));
            TestWorlds.Run(sys, ctx, HeroSystem.Interval * 2, false);
            Assert.AreEqual(3, HeroTestKit.LivingCount(ctx.State));
        }

        [Test]
        public void SingleSettlementGetsOnlyOneHero()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Orc, new Vec2i(32, 32), 20, out var civ);
            for (int i = 0; i < 20; i++) HeroTestKit.MakeEligible(ctx, s, i, JobType.Soldier);
            var sys = new HeroSystem { RaiseChanceScale = 1000f };
            TestWorlds.Run(sys, ctx, HeroSystem.Interval * 10);
            Assert.AreEqual(1, HeroTestKit.LivingCount(ctx.State));
        }

        [Test]
        public void TinyCivsAndZeroScaleRaiseNobody()
        {
            Assert.AreEqual(0f, HeroRules.RaiseChancePerYear(HeroRules.MinCivPopulation - 1, AgeId.Ascension));
            Assert.Greater(HeroRules.RaiseChancePerYear(300, AgeId.Medieval), HeroRules.RaiseChancePerYear(30, AgeId.Medieval));
            Assert.Greater(HeroRules.RaiseChancePerYear(100, AgeId.Modern), HeroRules.RaiseChancePerYear(100, AgeId.Dawn));
            Assert.LessOrEqual(HeroRules.RaiseChancePerYear(100000, AgeId.Ascension), 1f);

            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5, out var civ);
            for (int i = 0; i < 5; i++) HeroTestKit.MakeEligible(ctx, s, i, JobType.Scholar);
            var sys = new HeroSystem { RaiseChanceScale = 1000f };
            TestWorlds.Run(sys, ctx, HeroSystem.Interval * 10);
            Assert.AreEqual(0, HeroTestKit.LivingCount(ctx.State), "below MinCivPopulation");

            var ctx2 = HeroTestKit.NewContext();
            var s2 = HeroTestKit.PlainSettlement(ctx2, RaceId.Human, new Vec2i(32, 32), 20, out var civ2);
            for (int i = 0; i < 20; i++) HeroTestKit.MakeEligible(ctx2, s2, i, JobType.Scholar);
            var off = new HeroSystem { RaiseChanceScale = 0f };
            TestWorlds.Run(off, ctx2, HeroSystem.Interval * 10);
            Assert.AreEqual(0, HeroTestKit.LivingCount(ctx2.State));
        }

        [Test]
        public void DefaultRateIsRareButHappens()
        {
            // 200 people, default scale: over 40 years a hero should appear, but not every run.
            var ctx = HeroTestKit.NewContext(11);
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 200, out var civ);
            for (int i = 0; i < 200; i += 10) HeroTestKit.MakeEligible(ctx, s, i, JobType.Merchant);
            var sys = new HeroSystem();
            TestWorlds.Run(sys, ctx, HeroSystem.Interval);
            int firstRun = HeroTestKit.LivingCount(ctx.State);
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 40, false);
            Assert.LessOrEqual(firstRun, 1);
            Assert.AreEqual(1, HeroTestKit.LivingCount(ctx.State), "one settlement => exactly one hero eventually");
        }
    }
}
