using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Population;
using NUnit.Framework;
using Kit = GodSim.Tests.Population.SocietyTestKit;

namespace GodSim.Tests.Population
{
    public class WellbeingSystemTests
    {
        private static void AssertUnit(float v, string what)
        {
            Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), what + " is not finite");
            Assert.IsTrue(v >= 0f && v <= 1f, what + " out of [0,1]: " + v);
        }

        [Test]
        public void ValuesStayInUnitRangeWithHostileInputs()
        {
            var ctx = Kit.CreateContext();
            ctx.Config.EnableDisasters = false;
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 12, out var civ);
            civ.Modifiers.HappinessBonus = 5f;
            civ.Modifiers.HealthBonus = float.NaN;
            civ.Modifiers.LifespanBonusYears = float.NegativeInfinity;
            civ.Devotion = 7f;
            for (int i = 0; i < s.ServiceCoverage.Length; i++) s.ServiceCoverage[i] = i % 2 == 0 ? float.NaN : 1e9f;
            var people = Kit.Residents(ctx, s);
            people[0].Satiation = float.NaN;
            people[1].Happiness = float.PositiveInfinity;
            people[2].Health = -3f;
            people[3].Faith = float.NaN;
            people[4].AgeYears = 500f;
            people[5].Traits = PersonTraits.Sickly | PersonTraits.Devout | PersonTraits.Greedy | PersonTraits.Lazy;
            s.Unrest = float.NaN;

            var sys = new WellbeingSystem();
            for (int r = 0; r < 40; r++)
            {
                Kit.RunOnce(sys, ctx);
                foreach (var p in Kit.Residents(ctx, s))
                {
                    AssertUnit(p.Happiness, "happiness");
                    AssertUnit(p.Health, "health");
                    AssertUnit(p.Faith, "faith");
                }
                AssertUnit(s.Happiness, "settlement happiness");
                AssertUnit(s.Health, "settlement health");
                AssertUnit(s.Faith, "settlement faith");
                AssertUnit(s.Unrest, "unrest");
            }
            civ.Modifiers.HappinessBonus = -5f;
            for (int r = 0; r < 40; r++) Kit.RunOnce(sys, ctx);
            foreach (var p in Kit.Residents(ctx, s)) AssertUnit(p.Happiness, "happiness");
        }

        [Test]
        public void WellbeingRespondsToFoodAndHousing()
        {
            var ctx = Kit.CreateContext();
            ctx.Config.EnableDisasters = false;
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out _);
            var house = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            var fed = new List<Person>();
            var starving = new List<Person>();
            for (int i = 0; i < 4; i++)
            {
                var a = Kit.AddPerson(ctx, s, Sex.Female, 25f);
                a.Satiation = 1f;
                Assert.IsTrue(ctx.Ops.AssignHome(a, house));
                fed.Add(a);
                var b = Kit.AddPerson(ctx, s, Sex.Male, 25f);
                b.Satiation = 0f;
                starving.Add(b);
            }
            var sys = new WellbeingSystem();
            for (int r = 0; r < 60; r++) Kit.RunOnce(sys, ctx);

            float hf = 0f, hs = 0f, lf = 0f, ls = 0f;
            foreach (var p in fed) { hf += p.Happiness; lf += p.Health; }
            foreach (var p in starving) { hs += p.Happiness; ls += p.Health; }
            Assert.IsTrue(hf / 4f > hs / 4f + 0.3f, "fed+housed happier: " + hf / 4f + " vs " + hs / 4f);
            Assert.IsTrue(lf / 4f > ls / 4f + 0.2f, "fed+housed healthier: " + lf / 4f + " vs " + ls / 4f);
            Assert.AreEqual(0.35f + 0.2f + 0.15f, fed[0].Happiness, 0.02f);
            Assert.AreEqual(0.35f - 0.2f, starving[0].Happiness, 0.02f);
        }

        [Test]
        public void ServicesWarAndModifiersShiftHappinessTarget()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 1, out var civ);
            var p = Kit.Residents(ctx, s)[0];
            p.Satiation = 1f;
            float baseT = WellbeingSystem.HappinessTarget(p, s, civ, false);
            s.ServiceCoverage[(int)ServiceType.Entertainment] = 2f;
            s.ServiceCoverage[(int)ServiceType.Faith] = 1f;
            s.ServiceCoverage[(int)ServiceType.Health] = 1f;
            float served = WellbeingSystem.HappinessTarget(p, s, civ, false);
            Assert.AreEqual(baseT + 0.1f + 0.08f + 0.07f, served, 1e-4f);
            Assert.AreEqual(served - 0.08f, WellbeingSystem.HappinessTarget(p, s, civ, true), 1e-4f);
            civ.Modifiers.HappinessBonus = 0.05f;
            Assert.AreEqual(System.Math.Min(1f, served + 0.05f), WellbeingSystem.HappinessTarget(p, s, civ, false), 1e-4f);
        }

        [Test]
        public void FaithFollowsCoverageTimesDevotion()
        {
            var ctx = Kit.CreateContext();
            ctx.Config.EnableDisasters = false;
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 3, out var civ);
            civ.Devotion = 0.8f;
            s.ServiceCoverage[(int)ServiceType.Faith] = 0.5f;
            var sys = new WellbeingSystem();
            for (int r = 0; r < 200; r++) Kit.RunOnce(sys, ctx);
            Assert.AreEqual(0.4f, s.Faith, 0.02f);
        }

        [Test]
        public void HealthDeclinesWithAge()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 0, out var civ);
            var young = Kit.AddPerson(ctx, s, Sex.Female, 25f);
            var old = Kit.AddPerson(ctx, s, Sex.Female, 75f);
            var race = ctx.Db.Race(RaceId.Human);
            Assert.IsTrue(WellbeingSystem.HealthTarget(young, s, civ, race) > WellbeingSystem.HealthTarget(old, s, civ, race) + 0.2f);
        }

        [Test]
        public void DeltaTicksScaleSmoothing()
        {
            float once = RunWithDelta(6, 1);
            float sixTimes = RunWithDelta(1, 6);
            Assert.AreEqual(once, sixTimes, 1e-4f);
        }

        private static float RunWithDelta(int delta, int runs)
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 1, out _);
            var p = Kit.Residents(ctx, s)[0];
            p.Happiness = 1f;
            p.Satiation = 0f;
            var sys = new WellbeingSystem();
            for (int i = 0; i < runs; i++) sys.UpdateSettlement(ctx, s, delta);
            return p.Happiness;
        }

        [Test]
        public void UnrestRisesWhenUnhappyAndDecaysOtherwise()
        {
            var ctx = Kit.CreateContext();
            ctx.Config.EnableDisasters = false;
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 6, out _);
            foreach (var p in Kit.Residents(ctx, s)) { p.Satiation = 0f; p.Happiness = 0.1f; }
            var sys = new WellbeingSystem();
            for (int r = 0; r < 30; r++) Kit.RunOnce(sys, ctx);
            Assert.IsTrue(s.Happiness < WellbeingSystem.UnrestThreshold);
            float high = s.Unrest;
            Assert.IsTrue(high > 0.2f, "unrest should build up: " + high);

            var house = Kit.AddHouse(ctx, s, new Vec2i(10, 10), 5);
            var house2 = Kit.AddHouse(ctx, s, new Vec2i(12, 10), 5);
            foreach (var p in Kit.Residents(ctx, s)) { p.Satiation = 1f; if (!ctx.Ops.AssignHome(p, house)) ctx.Ops.AssignHome(p, house2); }
            for (int r = 0; r < 60; r++) Kit.RunOnce(sys, ctx);
            Assert.IsTrue(s.Unrest < high * 0.25f, "unrest should decay: " + s.Unrest);
        }

        [Test]
        public void OutbreakKillsOnlyBoundedFractionAndReports()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 100, out _);
            var disasters = new List<DisasterEvent>();
            ctx.Events.Subscribe<DisasterEvent>(e => disasters.Add(e));
            int chronicleBefore = ctx.State.Chronicle.Entries.Count;

            int deaths = new WellbeingSystem().TriggerOutbreak(ctx, s, 0.5f); // asks for 50%, clamped to 8%
            ctx.Ops.FlushRemovals();

            Assert.IsTrue(deaths >= 1 && deaths <= 8, "deaths " + deaths);
            Assert.AreEqual(100 - deaths, s.Population);
            Assert.AreEqual(deaths, (int)ctx.State.Stats.DeathsByCause[(int)DeathCause.Disease]);
            Assert.AreEqual(1, disasters.Count);
            Assert.AreEqual(DisasterType.Plague, disasters[0].Type);
            Assert.AreEqual(s.Center, disasters[0].Position);
            Assert.AreEqual(4, disasters[0].Radius);
            Assert.IsTrue(disasters[0].Severity > 0f && disasters[0].Severity <= 1f);
            Assert.AreEqual(chronicleBefore + 1, ctx.State.Chronicle.Entries.Count);
            var entry = ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1];
            Assert.AreEqual(1, entry.Importance);
            Assert.AreEqual(ChronicleCategory.Disaster, entry.Category);
            Assert.IsTrue(entry.Text.Contains("plague"));
        }

        [Test]
        public void OutbreakFavorsTheSick()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 200, out _);
            var people = Kit.Residents(ctx, s);
            var weak = new HashSet<int>();
            for (int i = 0; i < people.Count; i++)
            {
                people[i].Health = i % 2 == 0 ? 0.05f : 1f;
                if (i % 2 == 0) weak.Add(people[i].Id);
            }
            var deadWeak = 0;
            var dead = 0;
            ctx.Events.Subscribe<PersonDiedEvent>(e => { dead++; if (weak.Contains(e.PersonId)) deadWeak++; });
            new WellbeingSystem().TriggerOutbreak(ctx, s, 0.08f);
            Assert.AreEqual(16, dead);
            Assert.IsTrue(deadWeak >= 14, "victims should mostly be the sick: " + deadWeak + "/" + dead);
        }

        [Test]
        public void NaturalEpidemicsAreRareAndBounded()
        {
            var ctx = Kit.CreateContext(64, 3);
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 80, out _); // crowded: 80 people, one building, no healers
            int outbreaks = 0, worst = 0, popAtOutbreak = 0;
            ctx.Events.Subscribe<DisasterEvent>(e => outbreaks++);
            int deathsThisRun = 0;
            ctx.Events.Subscribe<PersonDiedEvent>(e => deathsThisRun++);
            var sys = new WellbeingSystem();
            int years = 60;
            for (int r = 0; r < years * SimTime.TicksPerYear / sys.TickInterval; r++)
            {
                deathsThisRun = 0;
                int popBefore = s.Population;
                Kit.RunOnce(sys, ctx);
                if (deathsThisRun > worst) { worst = deathsThisRun; popAtOutbreak = popBefore; }
            }
            Assert.IsTrue(outbreaks >= 1, "a crowded, unserved town should see an epidemic in 60 years");
            Assert.IsTrue(outbreaks <= years / (int)WellbeingSystem.EpidemicCooldownYears + 1, "cooldown bounds frequency: " + outbreaks);
            Assert.IsTrue(worst <= (int)System.Math.Ceiling(popAtOutbreak * 0.08f), "worst outbreak " + worst + " of " + popAtOutbreak);
        }

        [Test]
        public void TinySettlementsHaveNoEpidemics()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), WellbeingSystem.EpidemicMinPopulation - 1, out _);
            int outbreaks = 0;
            ctx.Events.Subscribe<DisasterEvent>(e => outbreaks++);
            var sys = new WellbeingSystem();
            for (int r = 0; r < 100 * SimTime.TicksPerYear / sys.TickInterval; r++) Kit.RunOnce(sys, ctx);
            Assert.AreEqual(0, outbreaks);
            Assert.AreEqual(WellbeingSystem.EpidemicMinPopulation - 1, s.Population);
        }

        [Test]
        public void DisasterFrequencyZeroDisablesEpidemics()
        {
            var ctx = Kit.CreateContext();
            ctx.Config.DisasterFrequency = 0f;
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 80, out _);
            var sys = new WellbeingSystem();
            for (int r = 0; r < 50 * SimTime.TicksPerYear / sys.TickInterval; r++) Kit.RunOnce(sys, ctx);
            Assert.AreEqual(80, s.Population);
        }

        [Test]
        public void DensityFactorGrowsWithCrowdingAndLackOfHealers()
        {
            var ctx = Kit.CreateContext();
            var s = Kit.Settlement(ctx, new Vec2i(20, 20), 40, out _);
            float crowded = WellbeingSystem.DensityFactor(s);
            s.ServiceCoverage[(int)ServiceType.Health] = 1f;
            s.ServiceCoverage[(int)ServiceType.Water] = 1f;
            float served = WellbeingSystem.DensityFactor(s);
            Assert.IsTrue(crowded > served && served > 0f);
            Assert.IsTrue(WellbeingSystem.EpidemicChancePerYear(s) >= 0.02f && WellbeingSystem.EpidemicChancePerYear(s) <= 0.08f);
        }
    }
}
