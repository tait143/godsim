using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class SimulationEngineTests
    {
        private sealed class Probe : ISimSystem
        {
            public string Name { get; }
            public int TickInterval { get; }
            public readonly List<long> RunTicks = new List<long>();
            public readonly List<int> Deltas = new List<int>();
            public int Inits;
            public Func<SimContext, bool> ShouldThrow;
            public Action<SimContext> OnTick;

            public Probe(string name, int interval) { Name = name; TickInterval = interval; }
            public void Initialize(SimContext ctx) { Inits++; }
            public void Tick(SimContext ctx)
            {
                RunTicks.Add(ctx.Tick);
                Deltas.Add(ctx.DeltaTicks);
                OnTick?.Invoke(ctx);
                if (ShouldThrow != null && ShouldThrow(ctx)) throw new InvalidOperationException("probe fault");
            }
        }

        private sealed class ThrowingInit : ISimSystem
        {
            public string Name => "badInit";
            public int TickInterval => 1;
            public int Ticks;
            public void Initialize(SimContext ctx) { throw new Exception("init fault"); }
            public void Tick(SimContext ctx) { Ticks++; }
        }

        private static SimContext Ctx(bool strict, int maxFaults = 3)
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 64, 1);
            ctx.Config.StrictMode = strict;
            ctx.Config.MaxSystemFaults = maxFaults;
            return ctx;
        }

        [Test]
        public void Constructor_FillsMissingServices()
        {
            var state = new WorldState(1, new WorldMap(16, 16));
            var ctx = new SimContext { State = state, Db = TestDb.CreateStub() };
            var engine = new SimulationEngine(ctx);
            Assert.IsNotNull(ctx.Events);
            Assert.IsNotNull(ctx.Config);
            Assert.IsNotNull(ctx.Log);
            Assert.IsNotNull(ctx.Ops);
            Assert.IsNotNull(ctx.Commands);
            Assert.AreSame(ctx, engine.Context);
            Assert.Throws<ArgumentNullException>(() => new SimulationEngine(null));
            Assert.Throws<ArgumentNullException>(() => new SimulationEngine(new SimContext { Db = TestDb.CreateStub() }));
            Assert.Throws<ArgumentNullException>(() => new SimulationEngine(new SimContext { State = state }));
        }

        [Test]
        public void Constructor_SyncsEventBusStrictModeWithConfig()
        {
            var ctx = Ctx(false);
            Assert.IsTrue(ctx.Events.StrictMode, "fixture starts strict");
            new SimulationEngine(ctx);
            Assert.IsFalse(ctx.Events.StrictMode);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(7)]
        [TestCase(30)]
        public void IntervalSystem_RunsEveryNTicks_WithDeltaN(int n)
        {
            var ctx = Ctx(true);
            var engine = new SimulationEngine(ctx);
            var filler = new Probe("filler", 1);
            var p = new Probe("p", n);
            engine.AddSystem(filler);
            engine.AddSystem(p);
            engine.StepMany(n * 20);
            Assert.AreEqual(20, p.RunTicks.Count);
            for (int i = 1; i < p.RunTicks.Count; i++) Assert.AreEqual(n, p.RunTicks[i] - p.RunTicks[i - 1]);
            foreach (int d in p.Deltas) Assert.AreEqual(n, d);
            Assert.AreEqual(n * 20, filler.RunTicks.Count);
            Assert.AreEqual(1, p.Inits);
        }

        [Test]
        public void SameIntervalSystems_AreStaggered()
        {
            var ctx = Ctx(true);
            var engine = new SimulationEngine(ctx);
            var probes = new List<Probe>();
            for (int i = 0; i < 4; i++) { var p = new Probe("p" + i, 4); probes.Add(p); engine.AddSystem(p); }
            engine.StepMany(40);
            var firstTicks = new HashSet<long>();
            foreach (var p in probes)
            {
                Assert.AreEqual(10, p.RunTicks.Count);
                firstTicks.Add(p.RunTicks[0] % 4);
            }
            Assert.AreEqual(4, firstTicks.Count, "systems with equal interval should run on different ticks");
        }

        [Test]
        public void AddSystem_Validation()
        {
            var engine = new SimulationEngine(Ctx(true));
            Assert.Throws<ArgumentException>(() => engine.AddSystem(new Probe("zero", 0)));
            Assert.Throws<ArgumentException>(() => engine.AddSystem(new Probe("neg", -3)));
            Assert.Throws<ArgumentNullException>(() => engine.AddSystem(null));
            engine.AddSystems(new ISimSystem[] { new Probe("a", 1), new Probe("b", 2) });
            Assert.AreEqual(2, engine.Systems.Count);
            Assert.AreEqual(2, engine.Profiles.Count);
            engine.Initialize();
            Assert.Throws<InvalidOperationException>(() => engine.AddSystem(new Probe("late", 1)));
        }

        [Test]
        public void Initialize_RunsOnce_AndStepInitializesLazily()
        {
            var engine = new SimulationEngine(Ctx(true));
            var p = new Probe("p", 3);
            engine.AddSystem(p);
            engine.Step();
            engine.Initialize();
            engine.Step();
            Assert.AreEqual(1, p.Inits);
            Assert.AreEqual(2, engine.Tick);
        }

        [Test]
        public void NonStrict_FaultingSystem_IsIsolatedThenDisabled()
        {
            var ctx = Ctx(false, 3);
            var engine = new SimulationEngine(ctx);
            var bad = new Probe("bad", 1) { ShouldThrow = c => true };
            var good = new Probe("good", 1);
            engine.AddSystem(bad);
            engine.AddSystem(good);
            Assert.DoesNotThrow(() => engine.StepMany(10));
            Assert.AreEqual(3, bad.RunTicks.Count, "disabled after MaxSystemFaults consecutive faults");
            Assert.AreEqual(10, good.RunTicks.Count);
            var prof = engine.Profiles[0];
            Assert.IsTrue(prof.Disabled);
            Assert.AreEqual(3, prof.TotalFaults);
            Assert.AreEqual(3, prof.ConsecutiveFaults);
            StringAssert.Contains("probe fault", prof.LastError);
            Assert.IsFalse(engine.Profiles[1].Disabled);
        }

        [Test]
        public void NonStrict_IntermittentFaults_ResetConsecutiveCounter()
        {
            var ctx = Ctx(false, 3);
            var engine = new SimulationEngine(ctx);
            var flaky = new Probe("flaky", 1) { ShouldThrow = c => c.Tick % 3 != 0 }; // two faults then a success
            engine.AddSystem(flaky);
            engine.StepMany(30);
            var prof = engine.Profiles[0];
            Assert.IsFalse(prof.Disabled);
            Assert.AreEqual(30, flaky.RunTicks.Count);
            Assert.AreEqual(20, prof.TotalFaults);
            Assert.AreEqual(31, prof.Runs, "30 ticks + Initialize");
        }

        [Test]
        public void Strict_FaultingSystem_Rethrows()
        {
            var ctx = Ctx(true, 3);
            var engine = new SimulationEngine(ctx);
            engine.AddSystem(new Probe("bad", 1) { ShouldThrow = c => c.Tick == 2 });
            engine.Step();
            Assert.Throws<InvalidOperationException>(() => engine.Step());
            Assert.AreEqual(1, engine.Profiles[0].TotalFaults);
            Assert.IsFalse(engine.Profiles[0].Disabled);
        }

        [Test]
        public void InitializeFault_CountsAsFault_NonStrict()
        {
            var ctx = Ctx(false, 1);
            var engine = new SimulationEngine(ctx);
            var sys = new ThrowingInit();
            engine.AddSystem(sys);
            Assert.DoesNotThrow(() => engine.Initialize());
            Assert.IsTrue(engine.Profiles[0].Disabled);
            engine.StepMany(5);
            Assert.AreEqual(0, sys.Ticks);
        }

        [Test]
        public void InitializeFault_Strict_Rethrows()
        {
            var engine = new SimulationEngine(Ctx(true));
            engine.AddSystem(new ThrowingInit());
            Assert.Throws<Exception>(() => engine.Initialize());
        }

        [Test]
        public void CalendarEvents_FireAtSeasonAndYearBoundaries()
        {
            var ctx = Ctx(true);
            var engine = new SimulationEngine(ctx);
            var seasons = new List<long>();
            var seasonValues = new List<Season>();
            var years = new List<long>();
            ctx.Events.Subscribe<SeasonChangedEvent>(e => { seasons.Add(ctx.State.Tick); seasonValues.Add(e.Season); Assert.AreEqual(SimTime.Year(ctx.State.Tick), e.Year); });
            ctx.Events.Subscribe<YearPassedEvent>(e => { years.Add(ctx.State.Tick); Assert.AreEqual(SimTime.Year(ctx.State.Tick), e.Year); });
            engine.StepMany(SimTime.TicksPerYear * 2);
            CollectionAssert.AreEqual(new long[] { 24, 48, 72, 96, 120, 144, 168, 192 }, seasons);
            CollectionAssert.AreEqual(new[] { Season.Summer, Season.Autumn, Season.Winter, Season.Spring, Season.Summer, Season.Autumn, Season.Winter, Season.Spring }, seasonValues);
            CollectionAssert.AreEqual(new long[] { 96, 192 }, years);
        }

        [Test]
        public void CalendarEvents_PublishedBeforeSystemsRun()
        {
            var ctx = Ctx(true);
            var engine = new SimulationEngine(ctx);
            long seenAtYear = -1;
            ctx.Events.Subscribe<YearPassedEvent>(e => seenAtYear = ctx.State.Tick);
            var p = new Probe("p", 1) { OnTick = c => { if (c.Tick == 96) Assert.AreEqual(96, seenAtYear); } };
            engine.AddSystem(p);
            engine.StepMany(96);
        }

        [Test]
        public void Profiles_AreUpdated()
        {
            var ctx = Ctx(true);
            var engine = new SimulationEngine(ctx);
            engine.AddSystem(new Probe("fast", 1));
            engine.AddSystem(new Probe("slow", 5));
            engine.StepMany(20);
            Assert.AreEqual("fast", engine.Profiles[0].Name);
            Assert.AreEqual(21, engine.Profiles[0].Runs, "20 ticks + Initialize");
            Assert.AreEqual(5, engine.Profiles[1].Runs, "4 ticks + Initialize");
            foreach (var p in engine.Profiles)
            {
                Assert.GreaterOrEqual(p.LastMs, 0.0);
                Assert.GreaterOrEqual(p.AvgMs, 0.0);
                Assert.IsFalse(double.IsNaN(p.AvgMs));
                Assert.AreEqual(0, p.TotalFaults);
            }
            Assert.GreaterOrEqual(engine.LastStepMs, 0.0);
        }

        [Test]
        public void Removals_AreFlushedAfterEachSystem()
        {
            var ctx = Ctx(true);
            var st = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 3, out _);
            var engine = new SimulationEngine(ctx);
            int victim = st.ResidentIds[0];
            var killer = new Probe("killer", 1) { OnTick = c => { if (c.Tick == 1) c.Ops.KillPerson(c.State.People.Get(victim), DeathCause.Accident); } };
            bool sawFlushed = false;
            var observer = new Probe("observer", 1) { OnTick = c => { if (c.Tick == 1) sawFlushed = !c.State.People.Contains(victim); } };
            engine.AddSystem(killer);
            engine.AddSystem(observer);
            engine.Step();
            Assert.IsTrue(sawFlushed, "next system must see the removal already flushed");
        }

        [Test]
        public void SystemsRunInRegistrationOrder()
        {
            var ctx = Ctx(true);
            var engine = new SimulationEngine(ctx);
            var order = new List<string>();
            foreach (var n in new[] { "Divine", "Economy", "Population" }) { string name = n; engine.AddSystem(new Probe(name, 1) { OnTick = c => order.Add(name) }); }
            engine.StepMany(2);
            CollectionAssert.AreEqual(new[] { "Divine", "Economy", "Population", "Divine", "Economy", "Population" }, order);
        }

        [Test]
        public void SameSeed_SameSystems_AreDeterministic()
        {
            ulong Run()
            {
                var ctx = Ctx(true);
                TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(20, 20), 6, out _);
                var engine = new SimulationEngine(ctx);
                engine.AddSystem(new Probe("rng", 3) { OnTick = c => c.Rng.NextUInt() });
                engine.AddSystem(new Probe("births", 7) { OnTick = c => { var s = c.State.Settlements[0]; c.Ops.CreatePerson(s, s.Race, (Sex)c.Rng.Range(0, 2), 0f, -1, -1, true); } });
                engine.StepMany(200);
                return ctx.State.Rng.State ^ (ulong)ctx.State.People.Count;
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}
