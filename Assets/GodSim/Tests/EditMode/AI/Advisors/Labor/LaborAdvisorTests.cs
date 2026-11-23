using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Advisors.Labor
{
    public class LaborAdvisorTests
    {
        /// <summary>Per-run scale the advisor board applies (votes are per council cycle).</summary>
        private static readonly float K = new LaborAdvisor().VoteScale;

        // ------------------------------------------------------------------ fixtures
        private static BuildingDefinition JobDef(BuildingType t, BuildingCategory cat, JobType job, int slots, ResourceType res, float perWorker)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = cat, Job = job, JobSlots = slots, BuildWork = 10f,
                OutputsPerWorker = new[] { new ResourceAmount(res, perWorker) },
            };
        }

        private static BuildingDefinition Farm(int slots = 3) => JobDef(BuildingType.Farm, BuildingCategory.Food, JobType.Farmer, slots, ResourceType.Food, 0.1f);
        private static BuildingDefinition Workshop(int slots = 6) => JobDef(BuildingType.Workshop, BuildingCategory.Industry, JobType.Craftsman, slots, ResourceType.Goods, 0.05f);
        private static BuildingDefinition Woodcutter(int slots = 3) => JobDef(BuildingType.WoodcutterCamp, BuildingCategory.Resource, JobType.Woodcutter, slots, ResourceType.Wood, 0.12f);

        private static BuildingDefinition Scriptorium()
        {
            return new BuildingDefinition
            {
                Type = BuildingType.Scriptorium, Name = "Scriptorium", Category = BuildingCategory.Education, Job = JobType.Scholar, JobSlots = 2,
                BuildWork = 10f, Services = new[] { new ServiceAmount(ServiceType.Research, 1.5f) },
            };
        }

        private static BuildingDefinition Barracks()
        {
            return new BuildingDefinition
            {
                Type = BuildingType.Barracks, Name = "Barracks", Category = BuildingCategory.Military, Job = JobType.Soldier, JobSlots = 8,
                BuildWork = 10f, Services = new[] { new ServiceAmount(ServiceType.Defense, 2f) },
            };
        }

        private static GameDatabase Db(params BuildingDefinition[] defs)
        {
            var db = new GameDatabase();
            for (int i = 0; i < defs.Length; i++) db.Register(defs[i]);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Building Place(SimContext ctx, Settlement s, BuildingType t, bool complete)
        {
            var def = ctx.Db.Building(t);
            var site = ctx.Ops.FindFreeSite(new Vec2i(s.Center.X + 3, s.Center.Y + 3), def.Width, def.Height, 24);
            var b = ctx.Ops.CreateBuilding(s, t, site, 0, complete);
            Assert.IsNotNull(b, "could not place " + t);
            return b;
        }

        private static int Employ(SimContext ctx, Settlement s, Building b, int count)
        {
            var def = ctx.Db.Building(b.Type);
            int done = 0;
            for (int i = 0; i < s.ResidentIds.Count && done < count; i++)
            {
                var p = ctx.State.People.Get(s.ResidentIds[i]);
                if (p == null || !p.IsAdult || p.Job != JobType.None) continue;
                if (ctx.Ops.AssignJob(p, b, def.Job)) done++;
            }
            return done;
        }

        private static void RunOnce(LaborAdvisor advisor, SimContext ctx)
        {
            ctx.State.Tick += advisor.TickInterval;
            ctx.DeltaTicks = advisor.TickInterval;
            advisor.Tick(ctx);
        }

        /// <summary>
        /// Runs council + labor like the engine (council first on shared ticks). After each labor run, reports the
        /// labor run's own contribution (bias after minus before) and the resulting bias.
        /// </summary>
        private static void RunCouncil(SimContext ctx, LaborAdvisor labor, int runs, Func<float> read, Action<int, float, float> sample)
        {
            var council = new AdvisorCouncilSystem();
            int r = 0;
            while (r < runs)
            {
                ctx.State.Tick++;
                if (ctx.State.Tick % council.TickInterval == 0) { ctx.DeltaTicks = council.TickInterval; council.Tick(ctx); }
                if (ctx.State.Tick % labor.TickInterval != 0) continue;
                float before = read();
                ctx.DeltaTicks = labor.TickInterval;
                labor.Tick(ctx);
                float after = read();
                sample(r, after - before, after);
                r++;
            }
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].IndexOf(text, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static SimContext Unemployed(float foodUrgency, float scienceUrgency, out Settlement s)
        {
            var ctx = TestWorlds.CreateContext(Db(Farm(), Scriptorium()));
            s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 12, out _);
            AdvisorBoard.ReportUrgency(s, "food", foodUrgency);
            AdvisorBoard.ReportUrgency(s, "science", scienceUrgency);
            return ctx;
        }

        // ------------------------------------------------------------------ tests
        [Test]
        public void Identity()
        {
            var a = new LaborAdvisor();
            Assert.AreEqual("labor", a.AdvisorId);
            Assert.AreEqual(12, a.TickInterval);
        }

        [Test]
        public void Unemployment_VotesJobBuildings_WeightedByUrgencies()
        {
            var ctx = Unemployed(0.9f, 0.1f, out var s);
            RunOnce(new LaborAdvisor(), ctx);
            float farm = AdvisorBoard.BuildBias(s, BuildingType.Farm), scr = AdvisorBoard.BuildBias(s, BuildingType.Scriptorium);
            Assert.IsTrue(farm > 0.5f * K, "hungry + idle -> strong farm vote, got " + farm);
            Assert.IsTrue(scr > 0f && farm > scr, "farm " + farm + " should beat scriptorium " + scr);
            Assert.AreEqual(1f, s.Mem(LaborKeys.Mode));
            Assert.IsTrue(AdvisorBoard.Urgency(s, LaborAdvisor.Id) > 0.5f);
            Assert.IsTrue(LogContains(s, "[labor]") && LogContains(s, "idle"), "strong vote must be explained");

            var ctx2 = Unemployed(0.1f, 0.9f, out var s2);
            RunOnce(new LaborAdvisor(), ctx2);
            float farm2 = AdvisorBoard.BuildBias(s2, BuildingType.Farm), scr2 = AdvisorBoard.BuildBias(s2, BuildingType.Scriptorium);
            Assert.IsTrue(scr2 > farm2 && farm2 > 0f, "science urgency flips the ranking: scr " + scr2 + " farm " + farm2);
        }

        [Test]
        public void Unemployment_WithConstruction_VotesBuilders()
        {
            var ctx = Unemployed(0.5f, 0.5f, out var s);
            Place(ctx, s, BuildingType.Farm, false);
            s.DesiredBuilders = 3;
            RunOnce(new LaborAdvisor(), ctx);
            Assert.IsTrue(AdvisorBoard.JobBias(s, JobType.Builder) > 0.5f * K, "builders wanted while idle and building");
        }

        [Test]
        public void Shortage_VotesAgainstJobBuildings_AndForHouses()
        {
            var ctx = TestWorlds.CreateContext(Db(Workshop(6), Farm()));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 4, out _);
            var w1 = Place(ctx, s, BuildingType.Workshop, true);
            Place(ctx, s, BuildingType.Workshop, true);
            Place(ctx, s, BuildingType.Workshop, true);
            Assert.AreEqual(4, Employ(ctx, s, w1, 4));
            RunOnce(new LaborAdvisor(), ctx);

            float house = AdvisorBoard.BuildBias(s, BuildingType.House);
            float workshop = AdvisorBoard.BuildBias(s, BuildingType.Workshop);
            float farm = AdvisorBoard.BuildBias(s, BuildingType.Farm);
            Assert.AreEqual(-1f, s.Mem(LaborKeys.Mode));
            Assert.IsTrue(house > 0.5f * K, "homes for growth, got " + house);
            Assert.IsTrue(workshop < -0.5f * K, "no more workshops, got " + workshop);
            Assert.IsTrue(farm <= 0f && workshop < farm, "unstaffed type is braked hardest");
            Assert.IsTrue(LogContains(s, "14 jobs unfilled: build homes, not workshops"), "explained: " + string.Join(" | ", s.AiLog));
            Assert.IsTrue(AdvisorBoard.Urgency(s, LaborAdvisor.Id) > 0.5f);
        }

        [Test]
        public void Shortage_CriticalFoodSlots_GetFoodJobVotesFirst()
        {
            var ctx = TestWorlds.CreateContext(Db(Farm(4), Workshop(4), Woodcutter(3)));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 3, out _);
            Place(ctx, s, BuildingType.Farm, true);
            Place(ctx, s, BuildingType.Workshop, true);
            var wood = Place(ctx, s, BuildingType.WoodcutterCamp, true);
            Employ(ctx, s, wood, 3);
            AdvisorBoard.ReportUrgency(s, "food", 1f);
            RunOnce(new LaborAdvisor(), ctx);

            float farmer = AdvisorBoard.JobBias(s, JobType.Farmer), crafts = AdvisorBoard.JobBias(s, JobType.Craftsman);
            Assert.IsTrue(farmer > 0.8f * K, "starving town staffs farms first, got " + farmer);
            Assert.IsTrue(farmer > crafts, "farmer " + farmer + " > craftsman " + crafts);
            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.Farm) <= 0f, "empty farms: staff them, do not build more");
        }

        [Test]
        public void PeacetimeOverMilitarization_VotesSoldiersDown_WarDoesNot()
        {
            var ctx = TestWorlds.CreateContext(Db(Barracks(), Farm()));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(32, 32), 10, out var civ);
            var bar = Place(ctx, s, BuildingType.Barracks, true);
            Employ(ctx, s, bar, 6);
            RunOnce(new LaborAdvisor(), ctx);
            Assert.IsTrue(AdvisorBoard.JobBias(s, JobType.Soldier) < -0.5f * K, "60% soldiers in peace -> demobilize");
            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.Barracks) < 0f, "no new barracks in peace");
            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.Farm) > 0f, "idle hands get farms, not barracks");
            Assert.IsTrue(LogContains(s, "peacetime"));

            var ctx2 = TestWorlds.CreateContext(Db(Barracks(), Farm()));
            var s2 = TestWorlds.AddSettlement(ctx2, RaceId.Orc, new Vec2i(32, 32), 10, out var civ2);
            var bar2 = Place(ctx2, s2, BuildingType.Barracks, true);
            Employ(ctx2, s2, bar2, 1);
            ctx2.State.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = 99, DefenderId = civ2.Id, IsActive = true });
            RunOnce(new LaborAdvisor(), ctx2);
            Assert.IsTrue(AdvisorBoard.JobBias(s2, JobType.Soldier) > 0.3f * K, "at war with idle hands -> mobilize, got " + AdvisorBoard.JobBias(s2, JobType.Soldier));
        }

        [Test]
        public void Personality_IndustriousCivReactsToModestUnemployment()
        {
            float industrious = ModestUnemploymentFarmBias(1f);
            float lazy = ModestUnemploymentFarmBias(0f);
            Assert.IsTrue(industrious > 0f, "industrious civs hate idleness, got " + industrious);
            Assert.AreEqual(0f, lazy, "relaxed civs tolerate 12% idleness");
        }

        private static float ModestUnemploymentFarmBias(float industriousness)
        {
            var ctx = TestWorlds.CreateContext(Db(Farm(), Workshop(22)));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 25, out var civ);
            AdvisorBoard.SetTrait(civ, AITrait.Industriousness, industriousness);
            var w = Place(ctx, s, BuildingType.Workshop, true);
            Assert.AreEqual(22, Employ(ctx, s, w, 22));
            RunOnce(new LaborAdvisor(), ctx);
            return AdvisorBoard.BuildBias(s, BuildingType.Farm);
        }

        [Test]
        public void PopulationCrash_TriggersRecovery()
        {
            var ctx = TestWorlds.CreateContext(Db(Farm(6)));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 20, out _);
            var farm = Place(ctx, s, BuildingType.Farm, true);
            Employ(ctx, s, farm, 6);
            var labor = new LaborAdvisor();
            RunOnce(labor, ctx);
            for (int i = s.ResidentIds.Count - 1; i >= 8; i--) ctx.Ops.KillPerson(ctx.State.People.Get(s.ResidentIds[i]), DeathCause.Disease);
            ctx.Ops.FlushRemovals();
            for (int i = 0; i < s.ResidentIds.Count; i++) { var p = ctx.State.People.Get(s.ResidentIds[i]); if (p.Job == JobType.Farmer && i % 2 == 0) ctx.Ops.ClearJob(p); }
            RunOnce(labor, ctx);
            Assert.IsTrue(s.Mem(LaborKeys.Recovery) > 0.3f, "crash detected");
            Assert.IsTrue(AdvisorBoard.JobBias(s, JobType.Farmer) > 0.5f * K, "food jobs restaffed first");
            Assert.IsTrue(AdvisorBoard.Urgency(s, LaborAdvisor.Id) >= 0.5f);
        }

        [Test]
        public void Balanced_AnticipatesPipelineJobs_WithHomes()
        {
            var ctx = TestWorlds.CreateContext(Db(Workshop(6), Farm()));
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 30, out _);
            var w = Place(ctx, s, BuildingType.Workshop, true);
            Employ(ctx, s, w, 6);
            for (int i = 0; i < 4; i++) { var f = Place(ctx, s, BuildingType.Farm, true); Employ(ctx, s, f, 3); }
            for (int i = 0; i < 3; i++) Place(ctx, s, BuildingType.Workshop, false); // 18 slots coming; 12 idle now
            RunOnce(new LaborAdvisor(), ctx);
            Assert.AreEqual(0f, s.Mem(LaborKeys.Mode), "pipeline absorbs the idle: no new job buildings");
            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.Workshop) <= 0f);
            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.Farm) <= 0f);
            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.House) > 0f, "6 more jobs than idle hands are coming: homes first");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StaticSettlement_IsDeterministic_AndDoesNotOscillate(bool surplus)
        {
            const int Runs = 20;
            float[] da = new float[Runs], db = new float[Runs], bias = new float[Runs], mode = new float[Runs];
            Settlement sa = null, sb = null;
            for (int pass = 0; pass < 2; pass++)
            {
                SimContext ctx;
                Settlement s;
                if (surplus) ctx = Unemployed(0.6f, 0.3f, out s);
                else
                {
                    ctx = TestWorlds.CreateContext(Db(Workshop(6), Farm()));
                    s = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(32, 32), 4, out _);
                    var w = Place(ctx, s, BuildingType.Workshop, true);
                    Place(ctx, s, BuildingType.Workshop, true);
                    Employ(ctx, s, w, 4);
                }
                var key = surplus ? BuildingType.Farm : BuildingType.House;
                var deltas = pass == 0 ? da : db;
                bool first = pass == 0;
                var cs = s;
                RunCouncil(ctx, new LaborAdvisor(), Runs, () => AdvisorBoard.BuildBias(cs, key), (r, delta, after) =>
                {
                    deltas[r] = delta;
                    if (first) { bias[r] = after; mode[r] = cs.Mem(LaborKeys.Mode); }
                });
                if (pass == 0) sa = s; else sb = s;
            }

            for (int r = 0; r < Runs; r++)
            {
                Assert.AreEqual(da[r], db[r], "deterministic at run " + r);
                Assert.IsTrue(da[r] > 0f, "vote never flips sign, run " + r);
                Assert.IsTrue(Math.Abs(da[r] - da[0]) < 1e-5f, "constant advice on a static town, run " + r + ": " + da[r] + " vs " + da[0]);
                Assert.AreEqual(mode[0], mode[r], "mode is stable");
                Assert.IsTrue(bias[r] > 0f && !float.IsNaN(bias[r]));
            }
            // Council-cycle aligned samples (every 2nd run) converge monotonically.
            for (int r = 3; r < Runs; r += 2) Assert.IsTrue(bias[r] >= bias[r - 2] - 1e-5f, "monotone at run " + r);
            Assert.IsTrue(Math.Abs(bias[Runs - 1] - bias[Runs - 3]) < 1e-2f, "converged");
            AssertSameMemory(sa, sb);
        }

        private static void AssertSameMemory(Settlement x, Settlement y)
        {
            var kx = new List<string>(x.Memory.Keys);
            var ky = new List<string>(y.Memory.Keys);
            kx.Sort(StringComparer.Ordinal);
            ky.Sort(StringComparer.Ordinal);
            Assert.AreEqual(kx.Count, ky.Count);
            for (int i = 0; i < kx.Count; i++)
            {
                Assert.AreEqual(kx[i], ky[i]);
                Assert.AreEqual(x.Memory[kx[i]], y.Memory[ky[i]], kx[i]);
                Assert.IsFalse(float.IsNaN(x.Memory[kx[i]]) || float.IsInfinity(x.Memory[kx[i]]), kx[i]);
            }
            Assert.AreEqual(x.AiLog.Count, y.AiLog.Count);
            for (int i = 0; i < x.AiLog.Count; i++) Assert.AreEqual(x.AiLog[i], y.AiLog[i]);
        }

        [Test]
        public void EmptyAndBadData_DoNotThrow_AndNeverTouchQueue()
        {
            var ctx = TestWorlds.CreateContext(Db(Farm()));
            var empty = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(16, 16), 0, out _);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(44, 44), 3, out _);
            s.ResidentIds.Add(424242);
            s.BuildingIds.Add(777777);
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Farm });
            s.Unemployed = -5;
            s.DesiredBuilders = -2;
            s.Memory[LaborKeys.Balance] = float.NaN;
            s.Memory[LaborKeys.Initialized] = 1f;
            var labor = new LaborAdvisor();
            for (int i = 0; i < 3; i++) RunOnce(labor, ctx);
            Assert.AreEqual(0f, AdvisorBoard.Urgency(empty, LaborAdvisor.Id));
            Assert.AreEqual(1, s.BuildQueue.Count, "never edits the queue");
            foreach (var kv in s.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }
    }
}
