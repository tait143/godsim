using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Population;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Population.Lifecycle
{
    public class FamilySystemTests
    {
        private static void RunFamilies(SimContext ctx, int runs, bool initialize = true)
        {
            TestWorlds.Run(new FamilySystem(), ctx, runs * 12, initialize);
        }

        private static void AssertMarried(Person a, Person b)
        {
            Assert.AreEqual(b.Id, a.SpouseId, a.Name + " should be married to " + b.Name);
            Assert.AreEqual(a.Id, b.SpouseId, b.Name + " should be married to " + a.Name);
        }

        [Test]
        public void CouplesForm_WithSymmetricLinks()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 10, out _);

            RunFamilies(ctx, 4);

            int married = 0;
            foreach (int id in s.ResidentIds)
            {
                var p = ctx.State.People.Get(id);
                if (p.SpouseId < 0) continue;
                married++;
                var sp = ctx.State.People.Get(p.SpouseId);
                Assert.AreEqual(p.Id, sp.SpouseId);
                Assert.AreNotEqual(p.Sex, sp.Sex);
                Assert.AreEqual(p.SettlementId, sp.SettlementId);
            }
            Assert.AreEqual(10, married, "5 women and 5 men of similar age should all pair up");
        }

        [Test]
        public void Pairs_NearestAge_AndRespectsMaxAgeGap()
        {
            var db = TestDb.CreateStub(); // life expectancy 70 -> max gap 17.5 years
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var w40 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 40f);
            var w20 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 20f);
            var m21 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 21f);
            var m41 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 41f);
            var w18 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 18f);
            var m70 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 70f);

            RunFamilies(ctx, 3);

            AssertMarried(w40, m41);
            AssertMarried(w20, m21);
            Assert.AreEqual(-1, w18.SpouseId, "52-year gap is too large");
            Assert.AreEqual(-1, m70.SpouseId);
        }

        [Test]
        public void ChildrenAndSameSex_DoNotPair()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var girl = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 12f);
            var boy = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 13f);
            var w1 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 25f);
            var w2 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 26f);

            RunFamilies(ctx, 3);

            Assert.AreEqual(-1, girl.SpouseId);
            Assert.AreEqual(-1, boy.SpouseId);
            Assert.AreEqual(-1, w1.SpouseId);
            Assert.AreEqual(-1, w2.SpouseId);
        }

        [Test]
        public void SameRace_IsPreferred_CrossRaceNeedsAffinity()
        {
            var db = LifecycleTestSupport.CreateDb(d =>
            {
                d.Race(RaceId.Human).RaceAffinity[(int)RaceId.Elf] = 0.5f;
                d.Race(RaceId.Elf).RaceAffinity[(int)RaceId.Human] = 0.5f;
                // Orcs and humans: humans like orcs, orcs do not like humans -> incompatible.
                d.Race(RaceId.Human).RaceAffinity[(int)RaceId.Orc] = 0.9f;
                d.Race(RaceId.Orc).RaceAffinity[(int)RaceId.Human] = 0.1f;
            });
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var human = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 25f);
            var elf = LifecycleTestSupport.Add(ctx, s, RaceId.Elf, Sex.Male, 25f);
            var humanMan = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 33f);

            RunFamilies(ctx, 1);
            AssertMarried(human, humanMan);
            Assert.AreEqual(-1, elf.SpouseId);

            // A second human woman with only the elf (compatible) and an orc (incompatible) left.
            var human2 = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 26f);
            var orc = LifecycleTestSupport.Add(ctx, s, RaceId.Orc, Sex.Male, 26f);
            RunFamilies(ctx, 1, false);
            AssertMarried(human2, elf);
            Assert.AreEqual(-1, orc.SpouseId);

            // No affinity at all: a dwarf woman never pairs with the orc.
            var dwarf = LifecycleTestSupport.Add(ctx, s, RaceId.Dwarf, Sex.Female, 26f);
            RunFamilies(ctx, 2, false);
            Assert.AreEqual(-1, dwarf.SpouseId);
            Assert.AreEqual(-1, orc.SpouseId);
            Assert.IsFalse(LifecycleRules.AreCompatibleRaces(db, RaceId.Human, RaceId.Orc));
            Assert.IsTrue(LifecycleRules.AreCompatibleRaces(db, RaceId.Elf, RaceId.Human));
        }

        [Test]
        public void CloseKin_NeverMarry()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var mother = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 36f);
            var son = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 19f, mother.Id);   // 17-year gap: within the age window
            var daughter = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 18f, mother.Id);

            RunFamilies(ctx, 3);

            Assert.AreEqual(-1, mother.SpouseId, "a mother does not marry her son");
            Assert.AreEqual(-1, son.SpouseId, "siblings do not marry");
            Assert.AreEqual(-1, daughter.SpouseId);

            var neighbour = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 18f); // 18 years from the mother: too old a gap
            RunFamilies(ctx, 1, false);
            AssertMarried(daughter, neighbour);
        }

        [Test]
        public void Widows_Remarry()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var wife = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 60f);
            var husband = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 62f);
            LifecycleTestSupport.Marry(wife, husband);

            ctx.Ops.KillPerson(husband, DeathCause.OldAge);
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(-1, wife.SpouseId);
            var widower = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 64f);

            RunFamilies(ctx, 1);

            AssertMarried(wife, widower);
        }

        [Test]
        public void StaleSpouseLinks_AreCleared()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            var a = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 30f);
            var b = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 31f);
            a.SpouseId = 424242; // dangling
            b.SpouseId = a.Id;   // one-sided

            RunFamilies(ctx, 1);

            AssertMarried(a, b);
        }

        [Test]
        public void DifferentSettlements_DoNotPair()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s1 = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(16, 16), 100f, out var civ);
            var s2 = ctx.Ops.FoundSettlement(civ, new Vec2i(48, 48));
            var w = LifecycleTestSupport.Add(ctx, s1, RaceId.Human, Sex.Female, 25f);
            var m = LifecycleTestSupport.Add(ctx, s2, RaceId.Human, Sex.Male, 25f);

            RunFamilies(ctx, 2);

            Assert.AreEqual(-1, w.SpouseId);
            Assert.AreEqual(-1, m.SpouseId);
        }

        [Test]
        public void Pairings_AreCappedPerRun()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 100f, out _);
            for (int i = 0; i < 30; i++)
            {
                LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 20f + i % 10);
                LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Male, 20f + i % 10);
            }
            int expectedCap = LifecycleBalance.PairingsPerSettlementBase + 60 / LifecycleBalance.ResidentsPerExtraPairing;

            RunFamilies(ctx, 1);
            Assert.AreEqual(expectedCap, CountCouples(ctx, s));

            RunFamilies(ctx, 10, false);
            Assert.AreEqual(30, CountCouples(ctx, s), "everybody pairs up over several runs");
        }

        [Test]
        public void Pairing_IsDeterministic()
        {
            var db = TestDb.CreateStub();
            var results = new List<string>();
            for (int run = 0; run < 2; run++)
            {
                var ctx = TestWorlds.CreateContext(db, seed: 77);
                var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 40, out _);
                RunFamilies(ctx, 6);
                var sb = new System.Text.StringBuilder();
                foreach (int id in s.ResidentIds) sb.Append(id).Append(':').Append(ctx.State.People.Get(id).SpouseId).Append(';');
                results.Add(sb.ToString());
            }
            Assert.AreEqual(results[0], results[1]);
        }

        private static int CountCouples(SimContext ctx, Settlement s)
        {
            int n = 0;
            foreach (int id in s.ResidentIds)
            {
                var p = ctx.State.People.Get(id);
                if (p.Sex == Sex.Female && p.SpouseId >= 0) n++;
            }
            return n;
        }
    }
}
