using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Core.Races;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.SaurianCulture
{
    public class SaurianCultureAdvisorTests
    {
        // Ticks (1 year = 96): Spring day 0 = 192, Summer day 0 = 216, Autumn day 12 = 252 (seasonally neutral).
        private const long SpringTick = 192, SummerTick = 216, NeutralTick = 252;

        private static GameDatabase s_db;
        private static GameDatabase Db => s_db ?? (s_db = CreateDb());

        private static BuildingDefinition Def(BuildingType t, AgeId age, BuildingCategory cat, JobType job = JobType.None, PlacementRule placement = PlacementRule.None)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = cat, MinAge = age, Job = job,
                JobSlots = job == JobType.None ? 0 : 2, Placement = placement, BuildWork = 10f,
            };
        }

        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);
            db.Register(Def(BuildingType.FishingHut, AgeId.Dawn, BuildingCategory.Food, JobType.Fisher, PlacementRule.NearWater));
            db.Register(Def(BuildingType.Harbor, AgeId.Classical, BuildingCategory.Commerce, JobType.Merchant, PlacementRule.Coastal));
            db.Register(Def(BuildingType.Shrine, AgeId.Bronze, BuildingCategory.Religion, JobType.Priest));
            db.Register(Def(BuildingType.Temple, AgeId.Iron, BuildingCategory.Religion, JobType.Priest));
            db.Register(Def(BuildingType.Monastery, AgeId.Medieval, BuildingCategory.Religion, JobType.Priest));
            db.Register(Def(BuildingType.Observatory, AgeId.Renaissance, BuildingCategory.Education, JobType.Scholar));
            db.Register(Def(BuildingType.Bathhouse, AgeId.Classical, BuildingCategory.Health, JobType.Healer));
            db.Register(Def(BuildingType.Barracks, AgeId.Iron, BuildingCategory.Military, JobType.Soldier));
            db.Register(Def(BuildingType.Palisade, AgeId.Bronze, BuildingCategory.Military));
            var pyramid = Def(BuildingType.SunPyramid, AgeId.Iron, BuildingCategory.Wonder);
            pyramid.IsWonder = true;
            pyramid.UniquePerCivilization = true;
            pyramid.AllowedRaces = RaceMask.Saurian;
            pyramid.Cost = new[] { new ResourceAmount(ResourceType.Stone, 300f) };
            db.Register(pyramid);
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5 });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement Make(RaceId race, out SimContext ctx, out Civilization civ, int adults = 6, AgeId age = AgeId.Iron, int waterTiles = 0, ulong seed = 1)
        {
            ctx = TestWorlds.CreateContext(Db, 64, seed);
            var s = TestWorlds.AddSettlement(ctx, race, new Vec2i(32, 32), adults, out civ);
            civ.Age = age;
            PaintWater(ctx.State.Map, s.Center, waterTiles);
            return s;
        }

        /// <summary>Turns up to n tiles of the territory ring (radius 3..5 around the center) into shallow water.</summary>
        private static int PaintWater(WorldMap map, Vec2i c, int n)
        {
            int painted = 0;
            for (int y = c.Y - 5; y <= c.Y + 5 && painted < n; y++)
                for (int x = c.X - 5; x <= c.X + 5 && painted < n; x++)
                {
                    int dx = x - c.X, dy = y - c.Y;
                    if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2) continue;
                    if (dx * dx + dy * dy > 25) continue;
                    int i = map.Index(x, y);
                    if (map.BuildingAt[i] >= 0) continue;
                    map.Biomes[i] = Biome.ShallowWater;
                    painted++;
                }
            return painted;
        }

        private static void RunOnce(SaurianCultureAdvisor adv, SimContext ctx, long tick)
        {
            ctx.State.Tick = tick;
            ctx.DeltaTicks = adv.TickInterval;
            adv.Initialize(ctx);
            adv.Tick(ctx);
        }

        private static void Marry(SimContext ctx, Settlement s)
        {
            var ids = s.ResidentIds;
            for (int i = 0; i + 1 < ids.Count; i += 2)
            {
                var a = ctx.State.People.Get(ids[i]);
                var b = ctx.State.People.Get(ids[i + 1]);
                a.SpouseId = b.Id;
                b.SpouseId = a.Id;
            }
        }

        private static string Dump(Dictionary<string, float> mem)
        {
            var sorted = new SortedDictionary<string, float>(mem, StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var kv in sorted) sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("R")).Append(';');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ tests
        [Test]
        public void Identity_UsesAdvisorIdAndInterval()
        {
            var adv = new SaurianCultureAdvisor();
            Assert.AreEqual("race_saurian", adv.AdvisorId);
            Assert.AreEqual(24, adv.TickInterval);
        }

        [Test]
        public void FishingVotes_ScaleWithWaterTilesInTerritory()
        {
            var dry = Make(RaceId.Saurian, out var c0, out _, waterTiles: 0);
            var few = Make(RaceId.Saurian, out var c1, out _, waterTiles: 6);
            var many = Make(RaceId.Saurian, out var c2, out _, waterTiles: 30);
            RunOnce(new SaurianCultureAdvisor(), c0, NeutralTick);
            RunOnce(new SaurianCultureAdvisor(), c1, NeutralTick);
            RunOnce(new SaurianCultureAdvisor(), c2, NeutralTick);

            float b0 = AdvisorBoard.BuildBias(dry, BuildingType.FishingHut);
            float b1 = AdvisorBoard.BuildBias(few, BuildingType.FishingHut);
            float b2 = AdvisorBoard.BuildBias(many, BuildingType.FishingHut);
            Assert.AreEqual(0f, b0, 1e-6f, "no water, no nets");
            Assert.That(b1, Is.GreaterThan(0f));
            Assert.That(b2, Is.GreaterThan(b1 + 0.3f), "more water in territory -> stronger fishing votes");
            Assert.That(AdvisorBoard.JobBias(many, JobType.Fisher), Is.GreaterThan(AdvisorBoard.JobBias(few, JobType.Fisher)));
            Assert.That(many.Mem(SaurianCultureKeys.Water), Is.GreaterThan(few.Mem(SaurianCultureKeys.Water)));
        }

        [Test]
        public void Harbor_StronglyPreferredOnWaterOnceAvailable()
        {
            var s = Make(RaceId.Saurian, out var ctx, out var civ, age: AgeId.Classical, waterTiles: 30);
            RunOnce(new SaurianCultureAdvisor(), ctx, NeutralTick);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Harbor), Is.GreaterThanOrEqualTo(0.9f));
        }

        [Test]
        public void SmallTown_VotesShrineAndTempleEarly_WithHighPriestPriority()
        {
            var s = Make(RaceId.Saurian, out var ctx, out _, adults: 4, age: AgeId.Iron);
            RunOnce(new SaurianCultureAdvisor(), ctx, NeutralTick);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Shrine), Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Temple), Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Priest), Is.GreaterThanOrEqualTo(0.8f));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Monastery), 1e-6f, "monastery not available in Iron");
        }

        [Test]
        public void Observatory_VotedAsSoonAsAvailable()
        {
            var early = Make(RaceId.Saurian, out var c1, out _, age: AgeId.Classical);
            var ready = Make(RaceId.Saurian, out var c2, out _, age: AgeId.Renaissance);
            RunOnce(new SaurianCultureAdvisor(), c1, NeutralTick);
            RunOnce(new SaurianCultureAdvisor(), c2, NeutralTick);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(early, BuildingType.Observatory), 1e-6f);
            Assert.That(AdvisorBoard.BuildBias(ready, BuildingType.Observatory), Is.GreaterThanOrEqualTo(0.99f), "capital charts the heavens first");
            Assert.That(AdvisorBoard.JobBias(ready, JobType.Scholar), Is.GreaterThan(0.3f));
        }

        [Test]
        public void Summer_AddsSolsticeRitualFaithVotes()
        {
            var spring = Make(RaceId.Saurian, out var c1, out _);
            var summer = Make(RaceId.Saurian, out var c2, out _);
            RunOnce(new SaurianCultureAdvisor(), c1, SpringTick);
            RunOnce(new SaurianCultureAdvisor(), c2, SummerTick);
            Assert.That(AdvisorBoard.BuildBias(summer, BuildingType.Temple), Is.GreaterThan(AdvisorBoard.BuildBias(spring, BuildingType.Temple) + 0.4f));
            Assert.That(AdvisorBoard.JobBias(summer, JobType.Priest), Is.GreaterThan(AdvisorBoard.JobBias(spring, JobType.Priest) + 0.3f));
            Assert.IsTrue(string.Join("\n", summer.AiLog).Contains("The solstice rites approach"));
            Assert.IsFalse(string.Join("\n", spring.AiLog).Contains("solstice"));
            Assert.AreEqual(1f, SaurianCultureAdvisor.RiteIntensity(Season.Summer, 0));
            Assert.AreEqual(0f, SaurianCultureAdvisor.RiteIntensity(Season.Winter, 0));
        }

        [Test]
        public void ClutchHousing_AnticipatesHatchlingsBeforeHousingRunsOut()
        {
            // 96 adults (was 24): Saurian fertility fell from 0.32 to 0.09/yr in the 2026-11-19 balance pass, so it takes
            // ~4x the mothers for the expected hatchlings to outrun one house's free beds.
            var couples = Make(RaceId.Saurian, out var c1, out _, adults: 96);
            var singles = Make(RaceId.Saurian, out var c2, out _, adults: 96);
            Marry(c1, couples);
            foreach (var pair in new[] { new { c = c1, s = couples }, new { c = c2, s = singles } })
            {
                var site = pair.c.Ops.FindFreeSite(pair.s.Center, 1, 1, 8);
                Assert.IsNotNull(pair.c.Ops.CreateBuilding(pair.s, BuildingType.House, site, 0, true));
            }
            RunOnce(new SaurianCultureAdvisor(), c1, NeutralTick);
            RunOnce(new SaurianCultureAdvisor(), c2, NeutralTick);

            float withClutches = AdvisorBoard.BuildBias(couples, BuildingType.House);
            Assert.That(withClutches, Is.GreaterThanOrEqualTo(0.35f), "houses are voted while beds are still free");
            Assert.That(withClutches, Is.GreaterThan(AdvisorBoard.BuildBias(singles, BuildingType.House)));
            Assert.That(couples.Mem(SaurianCultureKeys.ExpectedHatchlings), Is.GreaterThan(5f));
            Assert.That(SaurianCultureAdvisor.ClutchFactor(Db.Race(RaceId.Saurian)), Is.GreaterThan(SaurianCultureAdvisor.ClutchFactor(Db.Race(RaceId.Human))));
        }

        [Test]
        public void SunPyramid_CapitalStockpilesStoneUntilQueued()
        {
            var s = Make(RaceId.Saurian, out var ctx, out var civ, age: AgeId.Iron);
            var adv = new SaurianCultureAdvisor();
            RunOnce(adv, ctx, NeutralTick);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.SunPyramid), Is.GreaterThanOrEqualTo(0.99f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Quarrier), Is.GreaterThan(0.3f));
            Assert.AreEqual(1f, civ.Mem(SaurianCultureKeys.PyramidSaving));

            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.SunPyramid, Priority = 6f, Reason = "wonder" });
            float before = AdvisorBoard.BuildBias(s, BuildingType.SunPyramid);
            RunOnce(adv, ctx, NeutralTick + 96);
            Assert.AreEqual(0f, civ.Mem(SaurianCultureKeys.PyramidSaving));
            Assert.AreEqual(before, AdvisorBoard.BuildBias(s, BuildingType.SunPyramid), 1e-6f, "no further pyramid votes once queued");
            Assert.That(AdvisorBoard.JobBias(s, JobType.Builder), Is.GreaterThan(0.5f));
        }

        [Test]
        public void Vengeance_IsRememberedAndBuildsSteadilyOverYears()
        {
            var ctx = TestWorlds.CreateContext(Db, 64, 3);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(16, 32), 8, out var saur);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(48, 32), 8, out var orc);
            saur.Age = AgeId.Iron;
            var adv = new SaurianCultureAdvisor();
            var systems = new List<ISimSystem> { new AdvisorCouncilSystem(), adv };
            adv.Initialize(ctx);
            ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = orc.Id, DefenderId = saur.Id });
            Assert.IsTrue(saur.Memory.ContainsKey("race.saurian.wronged." + orc.Id));

            var v = new float[4];
            var soldier = new float[4];
            var barracks = new float[4];
            int[] steps = { 48, 96, 144, 144 }; // years 0.5, 1.5, 3, 4.5
            for (int k = 0; k < steps.Length; k++)
            {
                TestWorlds.RunAll(systems, ctx, steps[k]);
                v[k] = saur.Mem(SaurianCultureKeys.Vengeance);
                soldier[k] = AdvisorBoard.JobBias(s, JobType.Soldier);
                barracks[k] = AdvisorBoard.BuildBias(s, BuildingType.Barracks);
            }
            Assert.AreEqual(1, ctx.Events.HandlerCount<WarDeclaredEvent>(), "subscribed once despite re-initialization");
            Assert.That(v[0], Is.LessThan(0.15f), "slow to anger");
            for (int k = 1; k < v.Length; k++) Assert.That(v[k], Is.GreaterThan(v[k - 1]), "vengeance grows steadily");
            Assert.That(v[3], Is.GreaterThanOrEqualTo(0.45f), "relentless once wronged");
            Assert.That(soldier[3], Is.GreaterThan(soldier[0] + 0.2f));
            Assert.That(barracks[3], Is.GreaterThan(barracks[0] + 0.2f));
        }

        [Test]
        public void ColdWinter_VotesBathhouseForColdBloodedResidents()
        {
            var s = Make(RaceId.Saurian, out var ctx, out _, age: AgeId.Classical);
            RunOnce(new SaurianCultureAdvisor(), ctx, 72); // Winter, year 0
            Assert.That(s.Mem(SaurianCultureKeys.Chill), Is.GreaterThan(0.3f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Bathhouse), Is.GreaterThan(0.3f));
        }

        [Test]
        public void NonSaurians_AreUntouched()
        {
            var ctx = TestWorlds.CreateContext(Db, 64, 5);
            var human = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(16, 32), 24, out var hc);
            var saur = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(48, 32), 6, out var sc);
            hc.Age = AgeId.Renaissance;
            sc.Age = AgeId.Iron;
            PaintWater(ctx.State.Map, human.Center, 30);
            Marry(ctx, human);
            var adv = new SaurianCultureAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = sc.Id, DefenderId = hc.Id });
            RunOnce(adv, ctx, SummerTick);

            foreach (var key in human.Memory.Keys)
                Assert.IsFalse(key.StartsWith("ai.bias.", StringComparison.Ordinal) || key.StartsWith("race.saurian", StringComparison.Ordinal) || key.StartsWith("ai.adv.race_saurian", StringComparison.Ordinal), key);
            foreach (var key in hc.Memory.Keys)
                Assert.IsFalse(key.StartsWith("race.saurian", StringComparison.Ordinal), key);
            Assert.AreEqual(0, human.AiLog.Count);
            Assert.That(AdvisorBoard.BuildBias(saur, BuildingType.Shrine), Is.GreaterThan(0.5f), "the Saurian neighbour is advised");
        }

        [Test]
        public void SameSeedAndInputs_ProduceIdenticalMemory()
        {
            string[] runs = new string[2];
            for (int r = 0; r < 2; r++)
            {
                var ctx = TestWorlds.CreateContext(Db, 64, 11);
                var s = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(20, 32), 16, out var saur);
                TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(48, 32), 6, out var elf);
                saur.Age = AgeId.Iron;
                PaintWater(ctx.State.Map, s.Center, 20);
                Marry(ctx, s);
                var adv = new SaurianCultureAdvisor();
                adv.Initialize(ctx);
                ctx.Events.Publish(new BattleEvent { AttackerCivId = elf.Id, DefenderCivId = saur.Id, SettlementId = s.Id });
                TestWorlds.RunAll(new List<ISimSystem> { new AdvisorCouncilSystem(), adv }, ctx, 96 * 3);
                runs[r] = Dump(s.Memory) + "|" + Dump(saur.Memory);
                foreach (var kv in s.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            }
            Assert.AreEqual(runs[0], runs[1]);
        }
    }
}
