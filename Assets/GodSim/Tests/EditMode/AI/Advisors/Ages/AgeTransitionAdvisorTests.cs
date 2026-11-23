using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class AgeTransitionAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static ResourceAmount[] R(ResourceType a, float x) => new[] { new ResourceAmount(a, x) };
        private static ResourceAmount[] R(ResourceType a, float x, ResourceType b, float y) => new[] { new ResourceAmount(a, x), new ResourceAmount(b, y) };
        private static ResourceAmount[] R(ResourceType a, float x, ResourceType b, float y, ResourceType c, float z) => new[] { new ResourceAmount(a, x), new ResourceAmount(b, y), new ResourceAmount(c, z) };

        private static GameDatabase BuildDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.GathererHut, Name = "Gatherer's Hut", Category = BuildingCategory.Food, MinAge = AgeId.Dawn, MaxAge = AgeId.Iron, Job = JobType.Gatherer, JobSlots = 3, OutputsPerWorker = R(ResourceType.Food, 0.09f), Cost = R(ResourceType.Wood, 8f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage, MinAge = AgeId.Dawn, StorageCapacity = 120f, Cost = R(ResourceType.Wood, 20f) });
            db.Register(new BuildingDefinition { Type = BuildingType.StoneQuarry, Name = "Stone Quarry", Category = BuildingCategory.Resource, MinAge = AgeId.Dawn, Job = JobType.Quarrier, JobSlots = 3, OutputsPerWorker = R(ResourceType.Stone, 0.08f), Cost = R(ResourceType.Wood, 10f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, MinAge = AgeId.Bronze, Job = JobType.Farmer, JobSlots = 4, OutputsPerWorker = R(ResourceType.Food, 0.15f), Placement = PlacementRule.Fertile, Cost = R(ResourceType.Wood, 20f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Smelter, Name = "Smelter", Category = BuildingCategory.Industry, MinAge = AgeId.Bronze, Job = JobType.Smith, JobSlots = 2, InputsPerWorker = R(ResourceType.Ore, 0.05f), OutputsPerWorker = R(ResourceType.Metal, 0.04f), Cost = R(ResourceType.Stone, 20f, ResourceType.Wood, 10f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Shrine, Name = "Shrine", Category = BuildingCategory.Religion, MinAge = AgeId.Bronze, Services = new[] { new ServiceAmount(ServiceType.Faith, 2f) }, Cost = R(ResourceType.Wood, 15f, ResourceType.Stone, 10f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource, MinAge = AgeId.Bronze, Job = JobType.Miner, JobSlots = 3, OutputsPerWorker = R(ResourceType.Ore, 0.1f), Placement = PlacementRule.NearOre, Cost = R(ResourceType.Wood, 15f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Weaver, Name = "Weaver", Category = BuildingCategory.Industry, MinAge = AgeId.Bronze, AllowedRaces = RaceMask.Elf, Job = JobType.Weaver, JobSlots = 2, OutputsPerWorker = R(ResourceType.Cloth, 0.05f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Blacksmith, Name = "Blacksmith", Category = BuildingCategory.Industry, MinAge = AgeId.Iron, Job = JobType.Smith, JobSlots = 2, InputsPerWorker = R(ResourceType.Metal, 0.03f, ResourceType.Wood, 0.02f), OutputsPerWorker = R(ResourceType.Tools, 0.03f), Cost = R(ResourceType.Metal, 30f, ResourceType.Wood, 20f, ResourceType.Stone, 10f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Temple, Name = "Temple", Category = BuildingCategory.Religion, MinAge = AgeId.Iron, Services = new[] { new ServiceAmount(ServiceType.Faith, 5f) }, Cost = R(ResourceType.Stone, 60f, ResourceType.Metal, 10f) });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private sealed class World
        {
            public SimContext Ctx;
            public Civilization Civ;
            public Settlement Capital;
            public Settlement Second;
            public AgeTransitionAdvisor Advisor;
        }

        private static World MakeWorld(ulong seed = 7)
        {
            var w = new World();
            w.Ctx = TestWorlds.CreateContext(BuildDb(), 64, seed);
            w.Capital = TestWorlds.AddSettlement(w.Ctx, RaceId.Human, new Vec2i(16, 16), 14, out w.Civ);
            w.Second = w.Ctx.Ops.FoundSettlement(w.Civ, new Vec2i(46, 46));
            for (int i = 0; i < 6; i++) w.Ctx.Ops.CreatePerson(w.Second, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 22f);
            w.Advisor = new AgeTransitionAdvisor();
            w.Ctx.DeltaTicks = w.Advisor.TickInterval;
            w.Advisor.Initialize(w.Ctx);
            return w;
        }

        private static void Advance(World w, AgeId to, long tick)
        {
            w.Ctx.State.Tick = tick;
            var old = w.Civ.Age;
            w.Civ.Age = to;
            w.Civ.AgeEnteredTick = tick;
            w.Ctx.Events.Publish(new AgeAdvancedEvent { CivilizationId = w.Civ.Id, OldAge = old, NewAge = to });
        }

        /// <summary>Puts the civ in an age it entered long ago (outside the adoption wave).</summary>
        private static void SettleInAge(World w, AgeId age, long tick)
        {
            w.Ctx.State.Tick = tick;
            w.Civ.Age = age;
            w.Civ.AgeEnteredTick = tick - 30 * SimTime.TicksPerYear;
            w.Civ.TotalResearch = (int)age * 1000f; // stub thresholds: age * 1000
        }

        private static void RunOnce(World w)
        {
            w.Ctx.DeltaTicks = w.Advisor.TickInterval;
            w.Advisor.Tick(w.Ctx);
            w.Ctx.Ops.FlushRemovals();
        }

        private static void Place(World w, Settlement s, BuildingType t, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var site = w.Ctx.Ops.FindFreeSite(s.Center, 1, 1, 10);
                Assert.IsTrue(site.IsValid, "no site for " + t);
                Assert.IsNotNull(w.Ctx.Ops.CreateBuilding(s, t, site, 0, true));
            }
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        private static float Bias(Settlement s, BuildingType t) => AdvisorBoard.BuildBias(s, t);

        // ------------------------------------------------------------------ adoption wave
        [Test]
        public void AgeAdvance_RecordsEnteredTick_InCivMemory()
        {
            var w = MakeWorld();
            Advance(w, AgeId.Bronze, 480);
            Assert.AreEqual(480f, w.Civ.Mem(AgesKeys.EnteredTick, -1f));
            Assert.AreEqual((float)(int)AgeId.Bronze, w.Civ.Mem(AgesKeys.EnteredAge, -1f));
        }

        [Test]
        public void AdoptionWave_VotesNewAgeSignatures_CapitalFirst()
        {
            var w = MakeWorld();
            Advance(w, AgeId.Bronze, 480);
            w.Civ.TotalResearch = 1000f;
            RunOnce(w);

            Assert.Greater(Bias(w.Capital, BuildingType.Farm), 0.5f);
            Assert.Greater(Bias(w.Capital, BuildingType.Smelter), 0.5f);
            Assert.Greater(Bias(w.Capital, BuildingType.Shrine), 0f);
            Assert.Greater(Bias(w.Second, BuildingType.Smelter), 0f);
            Assert.Greater(Bias(w.Capital, BuildingType.Smelter), Bias(w.Second, BuildingType.Smelter), "capital pilots the new age");
            Assert.Greater(Bias(w.Capital, BuildingType.Farm), Bias(w.Second, BuildingType.Farm));

            // Data-driven gating: no ore nearby -> no Mine; Elf-only Weaver -> never voted for Humans; old-age stubs untouched.
            Assert.AreEqual(0f, Bias(w.Capital, BuildingType.Mine));
            Assert.AreEqual(0f, Bias(w.Capital, BuildingType.Weaver));
            Assert.AreEqual(0f, Bias(w.Capital, BuildingType.Well));

            Assert.IsTrue(LogContains(w.Capital, "Bronze Age dawns"), "explainable reason in the AI log");
            Assert.Greater(AdvisorBoard.Urgency(w.Capital, AgeTransitionAdvisor.Id), 0f);
            Assert.AreEqual((float)(int)AgesPhase.Adoption, w.Capital.Mem(AgesKeys.Phase));
        }

        [Test]
        public void AdoptionWave_SkipsAdoptedBuildings_AndDiffusesFromCapital()
        {
            var plain = MakeWorld();
            Advance(plain, AgeId.Bronze, 480);
            RunOnce(plain);

            var piloted = MakeWorld();
            Place(piloted, piloted.Capital, BuildingType.Smelter, 1);
            Advance(piloted, AgeId.Bronze, 480);
            RunOnce(piloted);

            Assert.AreEqual(0f, Bias(piloted.Capital, BuildingType.Smelter), "capital already has one");
            Assert.Greater(Bias(piloted.Second, BuildingType.Smelter), Bias(plain.Second, BuildingType.Smelter), "ideas spread once the capital shows the way");
        }

        [Test]
        public void AdoptionWave_WorksWithoutEvent_AndEndsAfterTwentyYears()
        {
            var w = MakeWorld();
            w.Ctx.State.Tick = 960;
            w.Civ.Age = AgeId.Bronze;           // e.g. loaded save: no event seen
            w.Civ.AgeEnteredTick = 960;
            RunOnce(w);
            Assert.Greater(Bias(w.Capital, BuildingType.Farm), 0f);

            var late = MakeWorld();
            late.Ctx.State.Tick = 960 + AgesTuning.WaveTicks + 24;
            late.Civ.Age = AgeId.Bronze;
            late.Civ.AgeEnteredTick = 960;
            late.Civ.TotalResearch = 1000f;
            RunOnce(late);
            Assert.AreEqual(0f, Bias(late.Capital, BuildingType.Farm));
        }

        [Test]
        public void AdoptionWave_ScalesWithCuriosityAndIndustriousness()
        {
            var keen = MakeWorld();
            AdvisorBoard.SetTrait(keen.Civ, AITrait.Curiosity, 1f);
            AdvisorBoard.SetTrait(keen.Civ, AITrait.Industriousness, 1f);
            Advance(keen, AgeId.Bronze, 480);
            RunOnce(keen);

            var dull = MakeWorld();
            AdvisorBoard.SetTrait(dull.Civ, AITrait.Curiosity, 0f);
            AdvisorBoard.SetTrait(dull.Civ, AITrait.Industriousness, 0f);
            Advance(dull, AgeId.Bronze, 480);
            RunOnce(dull);

            Assert.Greater(Bias(keen.Second, BuildingType.Shrine), Bias(dull.Second, BuildingType.Shrine));
            Assert.Greater(Bias(keen.Capital, BuildingType.Smelter), Bias(dull.Capital, BuildingType.Smelter));
        }

        // ------------------------------------------------------------------ replacement planning
        [Test]
        public void ObsoleteTypes_GetNegativeVotes_AndEquivalentsPositive()
        {
            var w = MakeWorld();
            SettleInAge(w, AgeId.Classical, 10000);
            Place(w, w.Capital, BuildingType.GathererHut, 3);
            RunOnce(w);

            Assert.Less(Bias(w.Capital, BuildingType.GathererHut), 0f);
            Assert.Greater(Bias(w.Capital, BuildingType.Farm), 0.5f);
            Assert.AreEqual(0f, Bias(w.Capital, BuildingType.Shrine), "only functional equivalents");
            Assert.IsTrue(LogContains(w.Capital, "obsolete"));
            Assert.AreEqual((float)(int)AgesPhase.Modernizing, w.Capital.Mem(AgesKeys.Phase));
            Assert.Greater(AdvisorBoard.Urgency(w.Capital, AgeTransitionAdvisor.Id), 0.5f);
        }

        [Test]
        public void ReplacementPressure_FallsWhenModernCapacityExists()
        {
            var bare = MakeWorld();
            SettleInAge(bare, AgeId.Classical, 10000);
            Place(bare, bare.Capital, BuildingType.GathererHut, 2);
            RunOnce(bare);

            var covered = MakeWorld();
            SettleInAge(covered, AgeId.Classical, 10000);
            Place(covered, covered.Capital, BuildingType.GathererHut, 2);
            Place(covered, covered.Capital, BuildingType.Farm, 3);
            RunOnce(covered);

            Assert.Greater(Bias(bare.Capital, BuildingType.Farm), Bias(covered.Capital, BuildingType.Farm));
        }

        [Test]
        public void SunsetTypes_AreRetiredAheadOfTheTransition()
        {
            var w = MakeWorld();
            SettleInAge(w, AgeId.Iron, 10000);
            w.Civ.TotalResearch = 2950f; // 95% of the way to Classical
            Place(w, w.Capital, BuildingType.GathererHut, 2);
            RunOnce(w);

            Assert.IsTrue(w.Ctx.Db.IsBuildingAvailable(w.Civ, BuildingType.GathererHut), "still buildable today");
            Assert.Less(Bias(w.Capital, BuildingType.GathererHut), 0f, "stop investing in soon-obsolete huts");
            Assert.Greater(Bias(w.Capital, BuildingType.Farm), 0f, "prepare the successor");
        }

        [Test]
        public void Index_FindsFunctionalEquivalents_FromData()
        {
            var db = BuildDb();
            var ix = AgesBuildingIndex.Build(db);
            var eq = ix.Equivalents(BuildingType.GathererHut);
            Assert.Greater(eq.Count, 0);
            Assert.AreEqual(BuildingType.Farm, eq[0]);
            Assert.AreEqual(0f, AgesBuildingIndex.EquivalenceScore(db.Building(BuildingType.GathererHut), db.Building(BuildingType.Shrine)));
            Assert.IsTrue(ContainsType(ix.Signatures(AgeId.Bronze), BuildingType.Smelter));
            Assert.IsFalse(ContainsType(ix.Signatures(AgeId.Bronze), BuildingType.House));
            Assert.IsTrue(ContainsType(ix.Sunset(AgeId.Iron), BuildingType.GathererHut));
            Assert.AreEqual(BuildingType.Smelter, ix.Producers(ResourceType.Metal)[0]);
        }

        private static bool ContainsType(IReadOnlyList<BuildingType> list, BuildingType t)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == t) return true;
            return false;
        }

        // ------------------------------------------------------------------ pre-transition
        [Test]
        public void PreTransition_StockpilesNextAgeMaterials_AndStorage()
        {
            var w = MakeWorld();
            SettleInAge(w, AgeId.Bronze, 10000);
            w.Civ.TotalResearch = 1900f; // 90% of the way to Iron
            w.Capital.Stockpile.Capacity = 60f;
            RunOnce(w);

            Assert.Greater(w.Civ.Mem(AgesKeys.Prep), 0.5f);
            Assert.Greater(Bias(w.Capital, BuildingType.Smelter), 0.3f, "Iron Age buildings need metal");
            Assert.Greater(Bias(w.Capital, BuildingType.StoneQuarry), 0f, "and stone");
            Assert.Greater(Bias(w.Capital, BuildingType.Storehouse), 0.3f, "and somewhere to keep it");
            Assert.AreEqual(0f, Bias(w.Capital, BuildingType.Mine), "no ore deposit: no mine");
            Assert.IsTrue(LogContains(w.Capital, "Iron Age nears"));
            Assert.AreEqual((float)(int)AgesPhase.Preparing, w.Capital.Mem(AgesKeys.Phase));
        }

        [Test]
        public void PreTransition_IsQuiet_FarFromTheThreshold()
        {
            var w = MakeWorld();
            SettleInAge(w, AgeId.Bronze, 10000);
            w.Civ.TotalResearch = 1200f;
            w.Capital.Stockpile.Capacity = 60f;
            RunOnce(w);

            Assert.AreEqual(0f, w.Civ.Mem(AgesKeys.Prep));
            Assert.AreEqual(0f, Bias(w.Capital, BuildingType.Smelter));
            Assert.AreEqual(0f, Bias(w.Capital, BuildingType.Storehouse));
            Assert.AreEqual(0f, AdvisorBoard.Urgency(w.Capital, AgeTransitionAdvisor.Id));
        }

        [Test]
        public void NextAgeProgress_UsesSpanOfCurrentAge()
        {
            var db = BuildDb();
            var civ = new Civilization { Age = AgeId.Bronze, TotalResearch = 1500f };
            float p = AgeTransitionAdvisor.NextAgeProgress(civ, db, 10, out float remaining);
            Assert.AreEqual(0.5f, p, 1e-4f);
            Assert.AreEqual(500f, remaining, 1e-3f);
            civ.Age = AgeId.Ascension;
            Assert.AreEqual(0f, AgeTransitionAdvisor.NextAgeProgress(civ, db, 10, out remaining));
        }

        // ------------------------------------------------------------------ determinism & safety
        [Test]
        public void Deterministic_AcrossIdenticalWorlds_AndFinite()
        {
            var a = Simulate();
            var b = Simulate();
            for (int i = 0; i < a.Ctx.State.Settlements.Items.Count; i++)
            {
                var sa = a.Ctx.State.Settlements.Items[i];
                var sb = b.Ctx.State.Settlements.Items[i];
                Assert.AreEqual(sa.Memory.Count, sb.Memory.Count);
                foreach (var kv in sa.Memory)
                {
                    Assert.IsTrue(sb.Memory.TryGetValue(kv.Key, out float v), kv.Key);
                    Assert.AreEqual(kv.Value, v, kv.Key);
                    Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
                }
                Assert.AreEqual(sa.AiLog.Count, sb.AiLog.Count);
                for (int k = 0; k < sa.AiLog.Count; k++) Assert.AreEqual(sa.AiLog[k], sb.AiLog[k]);
            }
            foreach (var kv in a.Civ.Memory)
            {
                Assert.AreEqual(kv.Value, b.Civ.Mem(kv.Key, float.NaN), kv.Key);
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            }
        }

        private static World Simulate()
        {
            var w = MakeWorld(42);
            Place(w, w.Capital, BuildingType.GathererHut, 2);
            Advance(w, AgeId.Bronze, 96);
            for (int step = 0; step < 40; step++)
            {
                w.Civ.TotalResearch += 23f;
                TestWorlds.Run(w.Advisor, w.Ctx, 24, false);
                if (step == 25) Advance(w, AgeId.Iron, w.Ctx.State.Tick);
            }
            return w;
        }

        [Test]
        public void SurvivesEmptyAndDegenerateWorlds()
        {
            var db = BuildDb();
            var ctx = TestWorlds.CreateContext(db);
            var adv = new AgeTransitionAdvisor();
            TestWorlds.Run(adv, ctx, 48);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(10, 10), 0, out var civ);
            civ.Age = AgeId.Ascension;
            civ.TotalResearch = float.NaN;
            s.Stockpile.Capacity = 0f;
            TestWorlds.Run(adv, ctx, 48, false);
            Assert.IsFalse(float.IsNaN(AdvisorBoard.Urgency(s, AgeTransitionAdvisor.Id)));
        }
    }
}
