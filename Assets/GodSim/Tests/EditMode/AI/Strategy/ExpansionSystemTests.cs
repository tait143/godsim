using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Strategy
{
    public class ExpansionSystemTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = StrategyTestSupport.CreateDb(); }

        private static Settlement OtherThan(SimContext ctx, Civilization civ, Settlement parent)
        {
            foreach (var s in StrategyTestSupport.SettlementsOf(ctx, civ)) if (s.Id != parent.Id) return s;
            return null;
        }

        /// <summary>Marries residents pairwise (female, male) and gives every couple <paramref name="kidsPerCouple"/> children.</summary>
        private static List<Person> MakeFamilies(SimContext ctx, Settlement s, int couples, int kidsPerCouple)
        {
            var created = new List<Person>();
            for (int i = 0; i < couples; i++)
            {
                var mom = ctx.Ops.CreatePerson(s, s.Race, Sex.Female, 25f);
                var dad = ctx.Ops.CreatePerson(s, s.Race, Sex.Male, 27f);
                mom.SpouseId = dad.Id;
                dad.SpouseId = mom.Id;
                created.Add(mom);
                created.Add(dad);
                for (int k = 0; k < kidsPerCouple; k++)
                    created.Add(ctx.Ops.CreatePerson(s, s.Race, k % 2 == 0 ? Sex.Female : Sex.Male, 4f + k, mom.Id, dad.Id));
            }
            return created;
        }

        [Test]
        public void SystemMetadata()
        {
            var sys = new ExpansionSystem();
            Assert.AreEqual(48, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
        }

        [Test]
        public void FoundsSettlement_AtValidDistance_WithSettlersAndSupplies()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            int leader = parent.ResidentIds[0];
            civ.LeaderId = leader;
            float parentFood = parent.Stockpile.Get(ResourceType.Food);
            float parentWood = parent.Stockpile.Get(ResourceType.Wood);
            float parentStone = parent.Stockpile.Get(ResourceType.Stone);

            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);

            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            var colony = OtherThan(ctx, civ, parent);
            int spacing = _db.Race(RaceId.Human).MinSettlementSpacing;
            float d = colony.Center.DistanceTo(parent.Center);
            Assert.That(d, Is.InRange(spacing - 2f, spacing * 3 + 2f));
            Assert.AreEqual(parent.Id, colony.ParentSettlementId);
            Assert.AreEqual(civ.Id, colony.CivilizationId);
            Assert.That(colony.Population, Is.InRange(6, 10));
            Assert.GreaterOrEqual(parent.Population, 30, "parent keeps >= 75%");
            Assert.AreEqual(40, parent.Population + colony.Population);
            Assert.IsTrue(parent.ResidentIds.Contains(leader), "the leader never leaves");
            foreach (int pid in colony.ResidentIds)
            {
                var p = ctx.State.People.Get(pid);
                Assert.AreEqual(colony.Id, p.SettlementId);
                Assert.AreEqual(civ.Id, p.CivilizationId);
            }

            // Supplies: Food 40, Wood 30, Stone 10 moved on top of the founding stock (60/40/10).
            Assert.AreEqual(parentFood - 40f, parent.Stockpile.Get(ResourceType.Food), 0.01f);
            Assert.AreEqual(parentWood - 30f, parent.Stockpile.Get(ResourceType.Wood), 0.01f);
            Assert.AreEqual(parentStone - 10f, parent.Stockpile.Get(ResourceType.Stone), 0.01f);
            Assert.AreEqual(100f, colony.Stockpile.Get(ResourceType.Food), 0.01f);
            Assert.AreEqual(70f, colony.Stockpile.Get(ResourceType.Wood), 0.01f);
            Assert.AreEqual(20f, colony.Stockpile.Get(ResourceType.Stone), 0.01f);

            Assert.Greater(civ.Mem(CivStrategyKeys.NextExpandTick), ctx.Tick);
            Assert.IsTrue(parent.AiLog.Exists(l => l.StartsWith("Expansion: founded " + colony.Name)));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        [TestCase(4UL)]
        [TestCase(5UL)]
        public void SiteRespectsSpacing_FromOwnAndForeignSettlements(ulong seed)
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 60, out var civ, out var parent, seed);
            // A second own town and a foreign town close by restrict the candidate area.
            var own2 = ctx.Ops.FoundSettlement(civ, new Vec2i(64, 84), parent.Id);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(84, 64), 6, out var elves);
            var foreign = ctx.State.Settlements.Get(elves.CapitalId);

            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            Assert.AreEqual(3, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Settlement colony = null;
            foreach (var s in StrategyTestSupport.SettlementsOf(ctx, civ)) if (s.Id != parent.Id && s.Id != own2.Id) colony = s;
            int spacing = _db.Race(RaceId.Human).MinSettlementSpacing;
            // Town centers may shift by a tile when the 2x2 footprint is placed.
            Assert.GreaterOrEqual(colony.Center.DistanceTo(parent.Center), spacing - 2f);
            Assert.GreaterOrEqual(colony.Center.DistanceTo(own2.Center), spacing - 2f);
            Assert.GreaterOrEqual(colony.Center.DistanceTo(foreign.Center), 0.8f * spacing - 2f);
            Assert.LessOrEqual(colony.Center.DistanceTo(parent.Center), 3f * spacing + 2f);
        }

        [Test]
        public void CouplesMoveTogether_WithTheirChildren_NeverTheLeader()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 6, out var civ, out var parent);
            var family = MakeFamilies(ctx, parent, 12, 1); // 36 + 6 singles = 42
            var leader = family[0];
            civ.LeaderId = leader.Id;

            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            var colony = OtherThan(ctx, civ, parent);
            Assert.IsNotNull(colony);
            Assert.LessOrEqual(colony.Population, 10);
            Assert.GreaterOrEqual(colony.Population, 6);
            Assert.GreaterOrEqual(parent.Population, (int)Math.Ceiling(42 * 0.75));
            Assert.AreEqual(parent.Id, leader.SettlementId);
            Assert.AreEqual(parent.Id, ctx.State.People.Get(leader.SpouseId).SettlementId, "the leader's spouse stays with the leader");

            int couplesMoved = 0;
            foreach (int pid in colony.ResidentIds)
            {
                var p = ctx.State.People.Get(pid);
                if (p.SpouseId >= 0)
                {
                    Assert.AreEqual(colony.Id, ctx.State.People.Get(p.SpouseId).SettlementId, "couples are never split");
                    couplesMoved++;
                }
                if (p.Stage == LifeStage.Child)
                {
                    Assert.AreEqual(colony.Id, ctx.State.People.Get(p.MotherId).SettlementId, "children travel with their parents");
                    Assert.AreEqual(colony.Id, ctx.State.People.Get(p.FatherId).SettlementId);
                }
            }
            Assert.Greater(couplesMoved, 0, "couples are preferred");
            // Every child left behind still has its parents at home.
            foreach (int pid in parent.ResidentIds)
            {
                var p = ctx.State.People.Get(pid);
                if (p.Stage == LifeStage.Child) Assert.AreEqual(parent.Id, ctx.State.People.Get(p.MotherId).SettlementId);
            }
        }

        [Test]
        public void CooldownRespected_ThenExpandsAgain()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 70, out var civ, out var parent);
            var sys = new ExpansionSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            long founded = ctx.Tick;
            long next = (long)civ.Mem(CivStrategyKeys.NextExpandTick);
            Assert.That(next - founded, Is.InRange(4L * SimTime.TicksPerYear, 14L * SimTime.TicksPerYear));
            float expansionism = _db.Race(RaceId.Human).Expansionism;
            float expectedYears = 14f - 10f * expansionism;
            Assert.That((next - founded) / (float)SimTime.TicksPerYear, Is.InRange(expectedYears * 0.89f, expectedYears * 1.11f));

            // Up to the end of the cooldown: no new settlement, even though the parent still qualifies.
            TestWorlds.Run(sys, ctx, (int)(next - founded) - 1, initialize: false);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);

            TestWorlds.Run(sys, ctx, 100, initialize: false);
            Assert.AreEqual(3, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
        }

        [Test]
        public void ExpansionistRaces_HaveShorterCooldowns()
        {
            var ctxOrc = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Orc, 40, out var orc, out _);
            var ctxElf = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Elf, 40, out var elf, out _);
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctxOrc);
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctxElf);
            float orcCd = orc.Mem(CivStrategyKeys.NextExpandTick) - orc.Mem(CivStrategyKeys.LastExpandTick);
            float elfCd = elf.Mem(CivStrategyKeys.NextExpandTick) - elf.Mem(CivStrategyKeys.LastExpandTick);
            Assert.Less(orcCd, elfCd);
        }

        [Test]
        public void MaxSettlementsPerCiv_Respected()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 80, out var civ, out var parent);
            ctx.Config.MaxSettlementsPerCiv = 1;
            var sys = new ExpansionSystem();
            TestWorlds.Run(sys, ctx, 48 * 4);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);

            ctx.Config.MaxSettlementsPerCiv = 2;
            civ.Memory.Remove(CivStrategyKeys.NextExpandTick);
            TestWorlds.Run(sys, ctx, 48, initialize: false);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            civ.Memory.Remove(CivStrategyKeys.NextExpandTick); // even without a cooldown
            TestWorlds.Run(sys, ctx, 48 * 4, initialize: false);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
        }

        [Test]
        public void NoExpansion_WhenFoodIsLow()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            parent.Memory["econ.foodDays"] = 10f;
            TestWorlds.Run(new ExpansionSystem(), ctx, 48 * 3);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);

            // Fallback estimate from the stockpile: 30 food / (40 * 0.05) = 15 days.
            parent.Memory.Remove("econ.foodDays");
            parent.Stockpile.SetRaw(ResourceType.Food, 30f);
            TestWorlds.Run(new ExpansionSystem(), ctx, 48 * 3);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
        }

        [Test]
        public void NoExpansion_WhenUnhappyOrTooSmall()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            parent.Happiness = 0.3f;
            TestWorlds.Run(new ExpansionSystem(), ctx, 48 * 3);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);

            var small = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 24, out var civ2, out _);
            TestWorlds.Run(new ExpansionSystem(), small, 48 * 3);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(small, civ2).Count);
        }

        [Test]
        public void Orcs_ExpandFromSmallerTowns_WithinTheQuarterLimit()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Orc, 24, out var civ, out var parent);
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            var colony = OtherThan(ctx, civ, parent);
            Assert.AreEqual(6, colony.Population); // floor(24 * 0.25)
            Assert.AreEqual(18, parent.Population);
        }

        [Test]
        public void NoValidSite_NoExpansion_AndParentBlockedForAWhile()
        {
            // Only a small island (15x15) around the parent is land: nothing within [spacing, 3*spacing].
            var ctx = TestWorlds.CreateContext(_db, 128, 7, Biome.ShallowWater);
            var map = ctx.State.Map;
            for (int y = 57; y <= 71; y++)
                for (int x = 57; x <= 71; x++)
                {
                    map.Biomes[map.Index(x, y)] = Biome.Grassland;
                    map.Elevation[map.Index(x, y)] = 0.2f;
                }
            var parent = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(64, 64), 40, out var civ);
            StrategyTestSupport.MakeHealthy(parent);
            Assert.DoesNotThrow(() => TestWorlds.Run(new ExpansionSystem(), ctx, 48 * 2));
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Assert.AreEqual(40, parent.Population);
            Assert.Greater(parent.Mem(CivStrategyKeys.ExpandBlockedUntil), ctx.Tick);
            Assert.IsTrue(parent.AiLog.Exists(l => l.Contains("no valid site")));
        }

        [Test]
        public void OwnedLand_IsNeverSettled()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            var map = ctx.State.Map;
            // Every unclaimed tile is claimed by someone else.
            for (int i = 0; i < map.TileCount; i++) if (map.Owner[i] < 0) map.Owner[i] = 9999;
            TestWorlds.Run(new ExpansionSystem(), ctx, 48 * 2);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
        }

        [Test]
        public void DeadCivsAndInvalidData_AreIgnored()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            civ.IsAlive = false;
            var empty = ctx.Ops.CreateCivilization(RaceId.Elf);
            empty.SettlementIds.Add(4242); // dangling id
            parent.Memory["econ.foodDays"] = float.NaN;
            Assert.DoesNotThrow(() => TestWorlds.Run(new ExpansionSystem(), ctx, 48 * 2));
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
        }

        [Test]
        public void Deterministic_AcrossIdenticalWorlds()
        {
            string Run()
            {
                var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 30, out var civ, out var parent, 42);
                MakeFamilies(ctx, parent, 8, 2);
                TestWorlds.Run(new ExpansionSystem(), ctx, 48 * 3);
                var colony = OtherThan(ctx, civ, parent);
                var ids = new List<int>(colony.ResidentIds);
                ids.Sort();
                return colony.Center + ":" + string.Join(",", ids) + ":" + civ.Mem(CivStrategyKeys.NextExpandTick);
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void LongRun_CivsExpandToSeveralSettlements_WithoutDrainingParents()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 32, out var civ, out var capital, 11);
            var sys = new ExpansionSystem();
            var before = new Dictionary<int, int>();
            int years = 150;
            for (int t = 0; t < years * SimTime.TicksPerYear; t++)
            {
                ctx.State.Tick++;
                if (ctx.Tick % SimTime.TicksPerYear == 0)
                {
                    // Crude growth model: ~6%/year up to 90 people, healthy and fed.
                    foreach (var s in StrategyTestSupport.SettlementsOf(ctx, civ))
                    {
                        int add = s.Population >= 90 ? 0 : Math.Max(1, (int)(s.Population * 0.06f));
                        for (int i = 0; i < add; i++) ctx.Ops.CreatePerson(s, s.Race, i % 2 == 0 ? Sex.Female : Sex.Male, 18f);
                        StrategyTestSupport.MakeHealthy(s);
                    }
                }
                if (ctx.Tick % sys.TickInterval != 0) continue;
                before.Clear();
                foreach (var s in StrategyTestSupport.SettlementsOf(ctx, civ)) before[s.Id] = s.Population;
                ctx.DeltaTicks = sys.TickInterval;
                sys.Tick(ctx);
                ctx.Ops.FlushRemovals();
                foreach (var s in StrategyTestSupport.SettlementsOf(ctx, civ))
                {
                    if (!before.TryGetValue(s.Id, out int old)) continue;
                    Assert.GreaterOrEqual(s.Population, (int)Math.Ceiling(old * 0.75f), "a parent never loses more than 25%");
                    Assert.Greater(s.Stockpile.Get(ResourceType.Food), 0f);
                }
            }
            int count = StrategyTestSupport.SettlementsOf(ctx, civ).Count;
            Assert.That(count, Is.InRange(5, ctx.Config.MaxSettlementsPerCiv));
            foreach (var s in StrategyTestSupport.SettlementsOf(ctx, civ)) Assert.Greater(s.Population, 0);
        }

        [Test]
        public void SpawnedTribe_MidGame_ExpandsLikeAnyCiv()
        {
            var ctx = TestWorlds.CreateContext(_db, 128, 5);
            ctx.State.Tick = 5000; // a god-spawned tribe appears late in the world
            var tribe = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(40, 80), 12, out var civ);
            StrategyTestSupport.MakeHealthy(tribe);
            var sys = new ExpansionSystem();
            TestWorlds.Run(sys, ctx, 48 * 2);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count, "too small to expand yet");
            for (int i = 0; i < 24; i++) ctx.Ops.CreatePerson(tribe, RaceId.Saurian, i % 2 == 0 ? Sex.Female : Sex.Male, 20f);
            TestWorlds.Run(sys, ctx, 48, initialize: false);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Assert.Greater(civ.Mem(CivStrategyKeys.NextExpandTick), 5000f);
        }

        [Test]
        public void AllSystemsTogether_RunWithoutFaults()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Orc, 40, out var orc, out var cap, 3);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 30, out var human);
            StrategyTestSupport.MakeHealthy(ctx.State.Settlements.Get(human.CapitalId));
            StrategyTestSupport.StartWar(ctx, orc, human);
            var systems = new List<ISimSystem> { new CivilizationStrategySystem(), new ResearchDirectorSystem(), new ExpansionSystem() };
            Assert.DoesNotThrow(() => TestWorlds.RunAll(systems, ctx, SimTime.TicksPerYear * 3));
            Assert.AreEqual(CivStrategyKeys.PostureMilitarist, (int)orc.Mem(CivStrategyKeys.Posture));
            Assert.GreaterOrEqual(orc.CurrentResearchTechId, 0);
            Assert.GreaterOrEqual(StrategyTestSupport.SettlementsOf(ctx, orc).Count, 2);
        }
    }
}
