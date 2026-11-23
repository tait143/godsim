using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Heroes;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Heroes
{
    public class HeroLifecycleTests
    {
        [Test]
        public void DeathWritesOneBiographyAndClearsKeys()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 12, out var civ);
            s.Name = "Grimhold";
            var sys = new HeroSystem { RaiseChanceScale = 0f };
            TestWorlds.Run(sys, ctx, 0);
            var p = HeroTestKit.Resident(ctx, s, 1);
            p.Name = "Aldric Stonebrow";
            p.Sex = Sex.Male;
            Assert.IsTrue(sys.Promote(ctx, p, HeroRole.Architect));
            ctx.State.Memory[HeroKeys.Deeds(p.Id)] = 23f;
            p.AgeYears = 81.6f;

            int deathsBefore = HeroTestKit.CountChronicle(ctx, ChronicleCategory.Death);
            ctx.Ops.KillPerson(p, DeathCause.OldAge);

            var bios = HeroTestKit.Entries(ctx, ChronicleCategory.Death);
            Assert.AreEqual(deathsBefore + 1, bios.Count);
            var bio = bios[bios.Count - 1];
            Assert.AreEqual("Aldric Stonebrow, the Architect of Grimhold, died aged 81; he raised 23 buildings.", bio.Text);
            Assert.AreEqual(2, bio.Importance);
            Assert.AreEqual(civ.Id, bio.CivilizationId);
            Assert.AreEqual(0, HeroTestKit.CountHeroKeys(ctx.State));
            Assert.IsFalse(HeroQueries.IsHero(ctx.State, p.Id));
            Assert.IsNull(HeroQueries.RoleOf(ctx.State, p.Id));

            ctx.Ops.FlushRemovals();
            TestWorlds.Run(sys, ctx, HeroSystem.Interval * 3, false);
            Assert.AreEqual(deathsBefore + 1, HeroTestKit.CountChronicle(ctx, ChronicleCategory.Death), "no duplicate biography");
        }

        [Test]
        public void LegendaryHeroDeathIsImportanceThree()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Elf, new Vec2i(32, 32), 12, out var civ);
            var sys = new HeroSystem { RaiseChanceScale = 0f };
            TestWorlds.Run(sys, ctx, 0);
            var p = HeroTestKit.Resident(ctx, s, 0);
            p.Sex = Sex.Female;
            sys.Promote(ctx, p, HeroRole.Sage);
            ctx.State.Memory[HeroKeys.Deeds(p.Id)] = HeroRules.LegendaryDeeds + 1;
            ctx.Ops.KillPerson(p, DeathCause.Violence);
            var bios = HeroTestKit.Entries(ctx, ChronicleCategory.Death);
            Assert.AreEqual(1, bios.Count);
            Assert.AreEqual(3, bios[0].Importance);
            StringAssert.Contains("fell in battle", bios[0].Text);
            StringAssert.Contains("she wrote 26 treatises", bios[0].Text);
        }

        [Test]
        public void MissedDeathIsDetectedByScanAndChronicledOnce()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            s.Name = "Oakford";
            var sys = new HeroSystem { RaiseChanceScale = 0f }; // never initialized => not subscribed
            var p = HeroTestKit.Resident(ctx, s, 2);
            Assert.IsTrue(sys.Promote(ctx, p, HeroRole.Bard));
            ctx.State.Memory[HeroKeys.Deeds(p.Id)] = 4.75f;
            ctx.Ops.KillPerson(p, DeathCause.Disease);
            ctx.Ops.FlushRemovals();
            Assert.IsNull(ctx.State.People.Get(p.Id));
            Assert.AreEqual(0, HeroTestKit.CountChronicle(ctx, ChronicleCategory.Death));

            TestWorlds.Run(sys, ctx, HeroSystem.Interval * 3, false);
            var bios = HeroTestKit.Entries(ctx, ChronicleCategory.Death);
            Assert.AreEqual(1, bios.Count);
            Assert.AreEqual("The Bard of Oakford passed from the world; they composed 4 songs.", bios[0].Text);
            Assert.AreEqual(0, HeroTestKit.CountHeroKeys(ctx.State));
        }

        [Test]
        public void KeysSurviveNewSystemInstance()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Saurian, new Vec2i(32, 32), 12, out var civ);
            var a = new HeroSystem { RaiseChanceScale = 0f };
            TestWorlds.Run(a, ctx, 0);
            var sage = HeroTestKit.Resident(ctx, s, 0);
            var architect = HeroTestKit.Resident(ctx, s, 1);
            Assert.IsTrue(a.Promote(ctx, sage, HeroRole.Sage));
            var s2 = ctx.Ops.FoundSettlement(civ, new Vec2i(10, 10));
            ctx.Ops.MovePersonToSettlement(architect, s2);
            Assert.IsTrue(a.Promote(ctx, architect, HeroRole.Architect));
            TestWorlds.Run(a, ctx, HeroSystem.Interval * 4, false);
            int deeds = HeroQueries.DeedsOf(ctx.State, sage.Id);
            a.Detach();

            // simulated load: same state, fresh bus/ops/system
            var bus = new EventBus { StrictMode = true };
            var ctx2 = new SimContext
            {
                State = ctx.State, Db = ctx.Db, Events = bus, Config = ctx.Config, Log = ctx.Log,
                Ops = new WorldOps(ctx.State, ctx.Db, bus), Commands = ctx.Commands,
            };
            var b = new HeroSystem { RaiseChanceScale = 0f };
            TestWorlds.Run(b, ctx2, 0);
            Assert.IsTrue(HeroQueries.IsHero(ctx2.State, sage.Id));
            Assert.AreEqual(HeroRole.Architect, HeroQueries.RoleOf(ctx2.State, architect.Id));
            Assert.AreEqual(deeds, HeroQueries.DeedsOf(ctx2.State, sage.Id));

            float research = civ.TotalResearch;
            TestWorlds.Run(b, ctx2, HeroSystem.Interval, false);
            Assert.AreEqual(research + HeroRules.SageResearch(civ.Age), civ.TotalResearch, 1e-4f);

            // architect cache rebuilt on Initialize: completions count immediately
            var site = ctx2.Ops.FindFreeSite(s2.Center, 1, 1, 8);
            var house = ctx2.Ops.CreateBuilding(s2, BuildingType.House, site);
            ctx2.Ops.CompleteBuilding(house);
            Assert.AreEqual(1, HeroQueries.DeedsOf(ctx2.State, architect.Id));

            ctx2.Ops.KillPerson(sage, DeathCause.OldAge);
            Assert.AreEqual(1, HeroTestKit.CountChronicle(ctx2, ChronicleCategory.Death));
            Assert.IsFalse(HeroQueries.IsHero(ctx2.State, sage.Id));
        }

        [Test]
        public void DeterministicAcrossIdenticalWorlds()
        {
            string a = RunScenario(42), b = RunScenario(42), c = RunScenario(43);
            Assert.AreEqual(a, b);
            Assert.IsTrue(a.Length > 0);
            Assert.AreNotEqual(a, c, "different seeds should diverge");
        }

        private static string RunScenario(ulong seed)
        {
            var ctx = HeroTestKit.NewContext(seed);
            var jobs = new[] { JobType.Soldier, JobType.Scholar, JobType.Priest, JobType.Builder, JobType.Merchant, JobType.Healer, JobType.Entertainer, JobType.Hunter, JobType.Farmer };
            var spots = new[] { new Vec2i(12, 12), new Vec2i(50, 12), new Vec2i(12, 50), new Vec2i(50, 50), new Vec2i(31, 31) };
            for (int k = 0; k < spots.Length; k++)
            {
                var s = TestWorlds.AddSettlement(ctx, (RaceId)(k % 5), spots[k], 30, out var civ);
                civ.Age = (AgeId)k;
                for (int i = 0; i < s.ResidentIds.Count; i++) HeroTestKit.Resident(ctx, s, i).Job = jobs[i % jobs.Length];
            }
            var sys = new HeroSystem { RaiseChanceScale = 3f, DiscoveryChanceScale = 2f };
            for (int year = 0; year < 30; year++)
            {
                TestWorlds.Run(sys, ctx, SimTime.TicksPerYear, year == 0);
                // a deterministic culling so deaths and refills happen
                var people = ctx.State.People.Items;
                for (int i = 0; i < people.Count; i++)
                    if (people[i].IsAlive && (people[i].Id + year) % 37 == 0) ctx.Ops.KillPerson(people[i], DeathCause.OldAge);
                ctx.Ops.FlushRemovals();
            }
            var sb = new StringBuilder();
            foreach (var e in ctx.State.Chronicle.Entries) sb.Append(e.Tick).Append(':').Append(e.Importance).Append(':').Append(e.Text).Append('\n');
            var keys = new List<string>(ctx.State.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            foreach (var k in keys) sb.Append(k).Append('=').Append(ctx.State.Memory[k].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(ctx.State.Rng.State);
            return sb.ToString();
        }

        [Test]
        public void NoExceptionsWithEmptyWorldsDeadPeopleAndCorruptKeys()
        {
            var empty = HeroTestKit.NewContext();
            var sys = new HeroSystem { RaiseChanceScale = 1000f, DiscoveryChanceScale = 1000f };
            Assert.DoesNotThrow(() => TestWorlds.Run(sys, empty, HeroSystem.Interval * 4));
            Assert.AreEqual(0, HeroTestKit.LivingCount(empty.State));
            Assert.DoesNotThrow(() => sys.Tick(null));
            Assert.DoesNotThrow(() => sys.Initialize(null));

            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Orc, new Vec2i(32, 32), 12, out var civ);
            var sys2 = new HeroSystem { RaiseChanceScale = 0f, DiscoveryChanceScale = 1000f };
            TestWorlds.Run(sys2, ctx, 0);
            var roles = (HeroRole[])System.Enum.GetValues(typeof(HeroRole));
            for (int i = 0; i < roles.Length; i++) Assert.IsTrue(sys2.Promote(ctx, HeroTestKit.Resident(ctx, s, i), roles[i]));
            ctx.State.Memory["hero.abc.role"] = 1f;          // ignored (not ours)
            ctx.State.Memory["hero.99999.role"] = float.NaN;  // corrupt -> cleared
            ctx.State.Memory["hero.88888.role"] = 42f;        // out of range -> cleared
            ctx.State.Memory["hero.77777.role"] = 2f;         // valid but nobody -> generic biography
            ctx.State.Memory[HeroKeys.Deeds(77777)] = float.PositiveInfinity;
            ctx.State.Memory[HeroKeys.Home(77777)] = float.NaN;
            ctx.State.Memory[HeroKeys.Deeds(HeroTestKit.Resident(ctx, s, 0).Id)] = float.NaN;

            Assert.DoesNotThrow(() => TestWorlds.Run(sys2, ctx, HeroSystem.Interval, false));
            Assert.IsFalse(ctx.State.Memory.ContainsKey("hero.99999.role"));
            Assert.IsFalse(ctx.State.Memory.ContainsKey("hero.88888.role"));
            Assert.IsFalse(ctx.State.Memory.ContainsKey("hero.77777.role"));
            Assert.IsTrue(ctx.State.Memory.ContainsKey("hero.abc.role"));
            Assert.AreEqual(1, HeroTestKit.CountChronicle(ctx, ChronicleCategory.Death));

            // everybody dies, settlement razed, civ collapses
            foreach (int id in new List<int>(s.ResidentIds)) ctx.Ops.KillPerson(ctx.State.People.Get(id), DeathCause.Disaster);
            Assert.AreEqual(1 + roles.Length, HeroTestKit.CountChronicle(ctx, ChronicleCategory.Death));
            ctx.Ops.DestroySettlement(s, DestroyCause.Disaster, true);
            ctx.Ops.FlushRemovals();
            Assert.DoesNotThrow(() => TestWorlds.Run(sys2, ctx, HeroSystem.Interval * 4, false));
            Assert.AreEqual(0, HeroTestKit.LivingCount(ctx.State));
            Assert.AreEqual(1 + roles.Length, HeroTestKit.CountChronicle(ctx, ChronicleCategory.Death));
        }

        [Test]
        public void HeroInHomelessOrDeadSettlementDoesNotThrow()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out var civ);
            var sys = new HeroSystem { RaiseChanceScale = 0f };
            TestWorlds.Run(sys, ctx, 0);
            var roles = (HeroRole[])System.Enum.GetValues(typeof(HeroRole));
            for (int i = 0; i < roles.Length; i++) sys.Promote(ctx, HeroTestKit.Resident(ctx, s, i), roles[i]);
            for (int i = 0; i < roles.Length; i++) { var p = HeroTestKit.Resident(ctx, s, i); p.SettlementId = -1; p.CivilizationId = 999; p.ArmyId = 12345; }
            Assert.DoesNotThrow(() => TestWorlds.Run(sys, ctx, HeroSystem.Interval * 2, false));
            Assert.AreEqual(roles.Length, HeroTestKit.LivingCount(ctx.State));
        }
    }
}
