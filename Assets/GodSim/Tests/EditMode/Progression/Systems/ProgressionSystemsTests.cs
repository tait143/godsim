using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Progression;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Progression
{
    public class ProgressionSystemsTests
    {
        private const float Eps = 1e-4f;

        private GameDatabase _db;
        private int _fire, _tools, _hunting, _elfMagic, _casting, _writing;

        // ------------------------------------------------------------------ fixtures

        /// <summary>Dawn: fire(50), tools(80, needs fire, unlocks Farm), hunting(60), elf-only magic(10). Bronze: casting(200, needs tools), writing(150).</summary>
        private static GameDatabase BuildDb()
        {
            var db = new GameDatabase();
            db.Register(new AgeDefinition { Id = AgeId.Dawn, Name = "Dawn", ResearchThreshold = 0f, ProductionBonus = 0.1f });
            db.Register(new AgeDefinition
            {
                Id = AgeId.Bronze, Name = "Bronze", ResearchThreshold = 100f, RequiredTechsFromPreviousAge = 2, MinPopulation = 5, MinSettlements = 1,
                ResearchBonus = 0.5f, LifespanBonusYears = 2f, HealthBonus = 0.05f
            });
            db.Register(new AgeDefinition { Id = AgeId.Iron, Name = "Iron Age", ResearchThreshold = 400f, RequiredTechsFromPreviousAge = 10, MinPopulation = 5, MinSettlements = 1 });
            db.Register(new TechDefinition { Key = "dawn.fire", Name = "Fire", Age = AgeId.Dawn, Cost = 50f, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.1f) } });
            db.Register(new TechDefinition
            {
                Key = "dawn.tools", Name = "Tools", Age = AgeId.Dawn, Cost = 80f, PrerequisiteKeys = new[] { "dawn.fire" },
                Effects = new[] { TechEffect.Produce(ResourceType.Wood, 0.2f), TechEffect.Unlock(BuildingType.Farm), TechEffect.Produce(ResourceType.Food, 0.25f) }
            });
            db.Register(new TechDefinition { Key = "dawn.hunting", Name = "Hunting", Age = AgeId.Dawn, Cost = 60f, Effects = new[] { TechEffect.Produce(ResourceType.Food, 0.25f) } });
            db.Register(new TechDefinition { Key = "dawn.elfmagic", Name = "Elf Magic", Age = AgeId.Dawn, Cost = 10f, AllowedRaces = RaceId.Elf.ToMask(), Effects = new[] { TechEffect.Of(TechEffectKind.FaithMultiplier, 1f) } });
            db.Register(new TechDefinition { Key = "bronze.casting", Name = "Casting", Age = AgeId.Bronze, Cost = 200f, PrerequisiteKeys = new[] { "dawn.tools" }, Effects = new[] { TechEffect.Of(TechEffectKind.MilitaryMultiplier, 0.3f) } });
            db.Register(new TechDefinition { Key = "bronze.writing", Name = "Writing", Age = AgeId.Bronze, Cost = 150f, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.2f), TechEffect.ServiceBoost(ServiceType.Health, 0.5f) } });
            TestDb.FillMissing(db);
            var human = db.Race(RaceId.Human);
            human.ResearchRate = 1.5f;
            human.ProductionMultiplier[(int)ResourceType.Food] = 1.2f;
            human.BuildSpeed = 0.9f;
            human.MilitaryStrength = 1.1f;
            db.Freeze();
            return db;
        }

        private static GameDatabase BuildCustomDb(Action<GameDatabase> registerTechs, Action<GameDatabase> afterFill = null)
        {
            var db = new GameDatabase();
            registerTechs(db);
            TestDb.FillMissing(db);
            afterFill?.Invoke(db);
            db.Freeze();
            return db;
        }

        [SetUp]
        public void SetUp()
        {
            _db = BuildDb();
            _fire = _db.TechIdOf("dawn.fire");
            _tools = _db.TechIdOf("dawn.tools");
            _hunting = _db.TechIdOf("dawn.hunting");
            _elfMagic = _db.TechIdOf("dawn.elfmagic");
            _casting = _db.TechIdOf("bronze.casting");
            _writing = _db.TechIdOf("bronze.writing");
        }

        private SimContext NewWorld(int adults, out Civilization civ, out Settlement s, RaceId race = RaceId.Human, GameDatabase db = null)
        {
            var ctx = TestWorlds.CreateContext(db ?? _db);
            s = TestWorlds.AddSettlement(ctx, race, new Vec2i(32, 32), adults, out civ);
            return ctx;
        }

        private static void TickAges(AgeProgressionSystem sys, SimContext ctx)
        {
            ctx.State.Tick += sys.TickInterval;
            ctx.DeltaTicks = sys.TickInterval;
            sys.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        private static void TickResearch(ResearchSystem sys, SimContext ctx)
        {
            ctx.State.Tick += sys.TickInterval;
            ctx.DeltaTicks = sys.TickInterval;
            sys.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        private static void AssertFinite(float v, string what)
        {
            Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), what + " is not finite: " + v);
        }

        // ------------------------------------------------------------------ modifiers

        [Test]
        public void Intervals_MatchDesign()
        {
            Assert.AreEqual(1, new ModifierSystem().TickInterval);
            Assert.AreEqual(12, ModifierSystem.RecomputeInterval);
            Assert.AreEqual(4, new ResearchSystem().TickInterval);
            Assert.AreEqual(24, new AgeProgressionSystem().TickInterval);
        }

        [Test]
        public void Modifiers_CombineRaceAgeAndTechs()
        {
            var ctx = NewWorld(4, out var civ, out _);
            civ.KnownTechs.Add(_fire);
            civ.KnownTechs.Add(_tools);
            civ.KnownTechs.Add(_hunting);
            new ModifierSystem().Initialize(ctx);

            var m = civ.Modifiers;
            Assert.AreEqual(1.5f * 1.1f, m.Research, Eps, "race 1.5 x fire +10%");
            Assert.AreEqual(1.2f * 1.1f * 1.5f, m.Production[(int)ResourceType.Food], Eps, "race x age x (1 + 0.25 + 0.25)");
            Assert.AreEqual(1.1f * 1.2f, m.Production[(int)ResourceType.Wood], Eps);
            Assert.AreEqual(1.1f, m.Production[(int)ResourceType.Stone], Eps);
            Assert.AreEqual(0.9f, m.BuildSpeed, Eps);
            Assert.AreEqual(1.1f, m.Military, Eps);
            Assert.AreEqual(1f, m.Faith, Eps);
            Assert.AreEqual(1f, m.Service[(int)ServiceType.Health], Eps);
            Assert.IsTrue(civ.UnlockedBuildings[(int)BuildingType.Farm]);
            Assert.AreEqual(4, civ.Population);
        }

        [Test]
        public void Modifiers_AgeBonusesAndServiceEffects()
        {
            var ctx = NewWorld(4, out var civ, out _);
            civ.Age = AgeId.Bronze;
            civ.KnownTechs.Add(_writing);
            var sys = new ModifierSystem();
            sys.Recompute(civ, _db);
            Assert.AreEqual(1.5f * 1.5f * 1.2f, civ.Modifiers.Research, Eps);
            Assert.AreEqual(2f, civ.Modifiers.LifespanBonusYears, Eps);
            Assert.AreEqual(0.05f, civ.Modifiers.HealthBonus, Eps);
            Assert.AreEqual(1.5f, civ.Modifiers.Service[(int)ServiceType.Health], Eps);
            Assert.AreEqual(1.2f, civ.Modifiers.Production[(int)ResourceType.Food], Eps, "Bronze has no production bonus");
        }

        [Test]
        public void Modifiers_RecomputeFromScratch_DoesNotCompound_AndResets()
        {
            var ctx = NewWorld(4, out var civ, out _);
            civ.KnownTechs.Add(_fire);
            var sys = new ModifierSystem();
            sys.Initialize(ctx);
            for (int i = 0; i < 5; i++) sys.Recompute(civ, _db);
            Assert.AreEqual(1.65f, civ.Modifiers.Research, Eps);

            civ.KnownTechs.Remove(_fire);
            civ.Memory[ProgressionKeys.Dirty] = 1f;
            ctx.State.Tick = 1; // not a periodic slot for civ 1
            sys.Tick(ctx);
            Assert.AreEqual(1.5f, civ.Modifiers.Research, Eps);
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty), "dirty flag cleared");
        }

        [Test]
        public void Modifiers_DirtyRecomputesImmediately_OtherwiseStaggeredEvery12Ticks()
        {
            var ctx = NewWorld(4, out var civ, out _);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(10, 10), 4, out var civ2);
            Assert.AreEqual(civ.Id + 1, civ2.Id);
            var sys = new ModifierSystem();
            sys.Initialize(ctx);
            Assert.AreEqual(1.5f, civ.Modifiers.Research, Eps);
            Assert.AreEqual(1.5f, civ2.Modifiers.Research, Eps);

            civ.Age = AgeId.Bronze; // no dirty flag: only the periodic recompute can pick this up
            civ2.Age = AgeId.Bronze;
            long slot1 = -1, slot2 = -1;
            for (long t = 1; t <= 24; t++)
            {
                ctx.State.Tick = t;
                sys.Tick(ctx);
                if (slot1 < 0)
                {
                    if (Math.Abs(civ.Modifiers.Research - 2.25f) < Eps) slot1 = t;
                    else Assert.AreEqual(1.5f, civ.Modifiers.Research, Eps, "civ 1 must not recompute off-slot at tick " + t);
                }
                if (slot2 < 0)
                {
                    if (Math.Abs(civ2.Modifiers.Research - 2.25f) < Eps) slot2 = t;
                    else Assert.AreEqual(1.5f, civ2.Modifiers.Research, Eps, "civ 2 must not recompute off-slot at tick " + t);
                }
            }
            Assert.IsTrue(slot1 > 0, "periodic recompute never happened for civ 1");
            Assert.IsTrue(slot2 > 0, "periodic recompute never happened for civ 2");
            Assert.LessOrEqual(slot1, (long)ModifierSystem.RecomputeInterval, "within one interval");
            Assert.LessOrEqual(slot2, (long)ModifierSystem.RecomputeInterval, "within one interval");
            Assert.AreEqual(0, (slot1 + civ.Id) % ModifierSystem.RecomputeInterval, "civ 1 recomputes on its stagger slot");
            Assert.AreEqual(0, (slot2 + civ2.Id) % ModifierSystem.RecomputeInterval, "civ 2 recomputes on its stagger slot");
            Assert.AreNotEqual(slot1, slot2, "slots are staggered by civ id");

            civ.Age = AgeId.Dawn;
            civ2.Age = AgeId.Dawn; // no request for civ 2
            civ.Memory[ProgressionKeys.Dirty] = 1f;
            ctx.State.Tick = slot1 + 1; // off-slot for both civs
            Assert.AreNotEqual(0, (ctx.State.Tick + civ2.Id) % ModifierSystem.RecomputeInterval);
            sys.Tick(ctx);
            Assert.AreEqual(1.5f, civ.Modifiers.Research, Eps, "dirty forces an out-of-interval recompute");
            Assert.AreEqual(2.25f, civ2.Modifiers.Research, Eps, "the request is per civ");
        }

        [Test]
        public void Modifiers_EveryTechEffectKindAggregates_RaceTimesAgeTimesOnePlusSum()
        {
            var db = BuildCustomDb(d =>
            {
                d.Register(new AgeDefinition { Id = AgeId.Dawn, Name = "Dawn", ResearchThreshold = 0f, ProductionBonus = 0.1f, ResearchBonus = 0.2f, LifespanBonusYears = 3f, HealthBonus = 0.6f });
                d.Register(new AgeDefinition { Id = AgeId.Bronze, Name = "Bronze", ResearchThreshold = 100f });
                d.Register(new TechDefinition { Key = "t.allprod", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.AllProductionMultiplier, 0.5f) } });
                d.Register(new TechDefinition { Key = "t.food", Age = AgeId.Dawn, Effects = new[] { TechEffect.Produce(ResourceType.Food, 0.25f) } });
                d.Register(new TechDefinition { Key = "t.foodore", Age = AgeId.Dawn, Effects = new[] { TechEffect.Produce(ResourceType.Food, 0.25f), TechEffect.Produce(ResourceType.Ore, 0.4f) } });
                d.Register(new TechDefinition { Key = "t.research1", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.1f) } });
                d.Register(new TechDefinition { Key = "t.research2", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.3f) } });
                d.Register(new TechDefinition { Key = "t.growth", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.GrowthMultiplier, 0.2f) } });
                d.Register(new TechDefinition { Key = "t.lifespan", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.LifespanBonus, 5f) } });
                d.Register(new TechDefinition { Key = "t.health", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.HealthBonus, 0.7f) } });
                d.Register(new TechDefinition { Key = "t.happy", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.HappinessBonus, 0.15f) } });
                d.Register(new TechDefinition { Key = "t.military", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.MilitaryMultiplier, 0.4f) } });
                d.Register(new TechDefinition { Key = "t.build", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.BuildSpeedMultiplier, 0.5f) } });
                d.Register(new TechDefinition { Key = "t.faith", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.FaithMultiplier, 0.25f) } });
                d.Register(new TechDefinition { Key = "t.storage", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.StorageMultiplier, 0.6f) } });
                d.Register(new TechDefinition { Key = "t.trade", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.TradeMultiplier, 0.2f) } });
                d.Register(new TechDefinition { Key = "t.service", Age = AgeId.Dawn, Effects = new[] { TechEffect.ServiceBoost(ServiceType.Commerce, 0.35f) } });
                d.Register(new TechDefinition { Key = "t.move", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.MoveSpeedMultiplier, 0.125f) } });
                d.Register(new TechDefinition { Key = "t.unlock", Age = AgeId.Dawn, Effects = new[] { TechEffect.Unlock(BuildingType.Mine) } });
                d.Register(new TechDefinition { Key = "t.unknown", Age = AgeId.Dawn, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 5f), TechEffect.Of(TechEffectKind.StorageMultiplier, 5f) } });
            }, d =>
            {
                var h = d.Race(RaceId.Human);
                h.ResearchRate = 1.5f; h.FaithRate = 1.2f; h.MilitaryStrength = 1.1f; h.BuildSpeed = 0.9f; h.TradeAptitude = 1.3f; h.MoveSpeed = 0.8f;
                h.ProductionMultiplier[(int)ResourceType.Food] = 1.2f;
                h.ProductionMultiplier[(int)ResourceType.Ore] = 0.5f;
            });
            var ctx = NewWorld(2, out var civ, out _, RaceId.Human, db);
            for (int i = 0; i < db.Techs.Count; i++) if (db.Techs[i].Key.StartsWith("t.", StringComparison.Ordinal) && db.Techs[i].Key != "t.unknown") civ.KnownTechs.Add(i);
            ModifierSystem.RecomputeFor(civ, db);

            var m = civ.Modifiers;
            Assert.AreEqual(1.2f * 1.1f * (1f + 0.25f + 0.25f) * 1.5f, m.Production[(int)ResourceType.Food], Eps, "race x age x (1 + resSum) x (1 + allSum)");
            Assert.AreEqual(0.5f * 1.1f * 1.4f * 1.5f, m.Production[(int)ResourceType.Ore], Eps);
            Assert.AreEqual(1.1f * 1.5f, m.Production[(int)ResourceType.Wood], Eps, "AllProduction reaches every resource");
            Assert.AreEqual(1.5f * 1.2f * (1f + 0.1f + 0.3f), m.Research, Eps, "two research techs sum additively (not 1.1 x 1.3)");
            Assert.AreEqual(1.2f, m.Growth, Eps);
            Assert.AreEqual(3f + 5f, m.LifespanBonusYears, Eps, "age + tech years");
            Assert.AreEqual(1f, m.HealthBonus, Eps, "age 0.6 + tech 0.7 clamped to 1");
            Assert.AreEqual(0.15f, m.HappinessBonus, Eps);
            Assert.AreEqual(1.1f * 1.4f, m.Military, Eps);
            Assert.AreEqual(0.9f * 1.5f, m.BuildSpeed, Eps);
            Assert.AreEqual(1.2f * 1.25f, m.Faith, Eps);
            Assert.AreEqual(1.6f, m.Storage, Eps, "unknown tech's +500% storage ignored");
            Assert.AreEqual(1.3f * 1.2f, m.Trade, Eps);
            Assert.AreEqual(0.8f * 1.125f, m.MoveSpeed, Eps);
            Assert.AreEqual(1.35f, m.Service[(int)ServiceType.Commerce], Eps);
            for (int sv = 0; sv < m.Service.Length; sv++)
                if (sv != (int)ServiceType.Commerce) Assert.AreEqual(1f, m.Service[sv], Eps, "service " + (ServiceType)sv);
            Assert.IsTrue(civ.UnlockedBuildings[(int)BuildingType.Mine]);

            // Recompute resets: with no techs only race x age remain; unlock flags are never cleared.
            civ.KnownTechs.Clear();
            ModifierSystem.RecomputeFor(civ, db);
            Assert.AreEqual(1.2f * 1.1f, m.Production[(int)ResourceType.Food], Eps);
            Assert.AreEqual(1.5f * 1.2f, m.Research, Eps);
            Assert.AreEqual(1f, m.Growth, Eps);
            Assert.AreEqual(1f, m.Storage, Eps);
            Assert.AreEqual(1.1f, m.Military, Eps);
            Assert.AreEqual(3f, m.LifespanBonusYears, Eps);
            Assert.AreEqual(0.6f, m.HealthBonus, Eps);
            Assert.AreEqual(0f, m.HappinessBonus, Eps);
            Assert.AreEqual(1f, m.Service[(int)ServiceType.Commerce], Eps);
            Assert.IsTrue(civ.UnlockedBuildings[(int)BuildingType.Mine]);
        }

        [Test]
        public void Modifiers_CorruptDirtyValuesAreClearedAfterOneTick()
        {
            var ctx = NewWorld(4, out var civ, out _);
            var sys = new ModifierSystem();
            sys.Initialize(ctx);
            Assert.AreEqual(1.5f, civ.Modifiers.Research, Eps);

            civ.Age = AgeId.Bronze;
            civ.Memory[ProgressionKeys.Dirty] = float.NaN;
            ctx.State.Tick = 1; // off-slot for civ 1 (ticks 1..5 all are)
            sys.Tick(ctx);
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty), "NaN removed from persisted Memory");
            Assert.AreEqual(2.25f, civ.Modifiers.Research, Eps, "NaN is treated as a request");

            civ.Age = AgeId.Dawn;
            civ.Memory[ProgressionKeys.Dirty] = -3f;
            ctx.State.Tick = 2;
            sys.Tick(ctx);
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty), "negative value removed");
            Assert.AreEqual(2.25f, civ.Modifiers.Research, Eps, "a negative value is not a request");

            foreach (float v in new[] { 0f, float.NegativeInfinity })
            {
                civ.Memory[ProgressionKeys.Dirty] = v;
                ctx.State.Tick++;
                sys.Tick(ctx);
                Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty), "removed: " + v);
            }

            civ.Memory[ProgressionKeys.Dirty] = float.PositiveInfinity;
            ctx.State.Tick = 5;
            sys.Tick(ctx);
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty));
            Assert.AreEqual(1.5f, civ.Modifiers.Research, Eps, "+Infinity is a request");
            foreach (var kv in civ.Memory) AssertFinite(kv.Value, "Memory[" + kv.Key + "]");
        }
        [Test]
        public void Modifiers_RefreshPopulationEveryTick()
        {
            var ctx = NewWorld(6, out var civ, out var s);
            var sys = new ModifierSystem();
            sys.Initialize(ctx);
            civ.Population = 0;
            ctx.State.Tick = 1;
            sys.Tick(ctx);
            Assert.AreEqual(6, civ.Population);
            ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Male, 30f);
            ctx.State.Tick = 2;
            sys.Tick(ctx);
            Assert.AreEqual(7, civ.Population);
        }

        // ------------------------------------------------------------------ research

        [Test]
        public void Research_AccumulatesFromServiceOutputAndPopulation()
        {
            var ctx = NewWorld(10, out var civ, out var s);
            new ModifierSystem().Initialize(ctx); // Research = 1.5 (race)
            s.ServiceOutput[(int)ServiceType.Research] = 2f;
            // Service output counts TechQueries.ResearchPerServicePoint (0.4 since the 2026-11-19 balance pass, was 1).
            float expectedPerTick = (TechQueries.ResearchPerServicePoint * 2f + 0.004f * 10) * 1.5f;
            Assert.AreEqual(expectedPerTick, TechQueries.ResearchPerTick(ctx, civ), Eps);

            TestWorlds.Run(new ResearchSystem(), ctx, 4);
            Assert.AreEqual(expectedPerTick * 4f, civ.TotalResearch, Eps);
            Assert.AreEqual(expectedPerTick * 4f, civ.ResearchPool, Eps);
            Assert.AreEqual(0, civ.KnownTechs.Count);

            ctx.Config.ResearchMultiplier = 2f;
            Assert.AreEqual(expectedPerTick * 2f, TechQueries.ResearchPerTick(ctx.State, ctx.Db, civ, ctx.Config), Eps);
        }

        [Test]
        public void Research_LearnsInPrerequisiteOrder_AndCapsIdlePool()
        {
            var ctx = NewWorld(6, out var civ, out var s);
            s.ServiceOutput[(int)ServiceType.Research] = 5f;
            var learned = new List<int>();
            ctx.Events.Subscribe<TechResearchedEvent>(e => { if (e.CivilizationId == civ.Id) learned.Add(e.TechId); });

            TestWorlds.RunAll(new ISimSystem[] { new ModifierSystem(), new ResearchSystem() }, ctx, 400);

            Assert.AreEqual(new[] { _fire, _hunting, _tools }, learned.ToArray(), "cheapest-first fallback honoring prerequisites");
            Assert.IsFalse(civ.KnownTechs.Contains(_elfMagic), "race-restricted tech skipped");
            Assert.IsFalse(civ.KnownTechs.Contains(_casting), "next-age techs locked while in Dawn");
            Assert.LessOrEqual(civ.ResearchPool, 2f * 80f + Eps, "idle pool capped at 2x the age's most expensive tech");
            Assert.Greater(civ.TotalResearch, 1000f, "total research keeps accumulating");
            Assert.AreEqual(-1f, civ.Mem(ProgressionKeys.ResearchTarget, 99f));
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty), "modifier system consumed the dirty flag");
            Assert.AreEqual(1.65f, civ.Modifiers.Research, Eps, "learned fire applied to modifiers");
        }

        [Test]
        public void Research_RespectsAiChoice()
        {
            var ctx = NewWorld(2, out var civ, out _);
            civ.ResearchPool = 65f;
            civ.CurrentResearchTechId = _hunting; // fallback would be fire (cheaper)
            TickResearch(new ResearchSystem(), ctx);
            Assert.IsTrue(civ.KnownTechs.Contains(_hunting));
            Assert.IsFalse(civ.KnownTechs.Contains(_fire));
            Assert.AreEqual(-1, civ.CurrentResearchTechId, "AI target cleared after learning");
            Assert.Less(civ.ResearchPool, 10f);
        }

        [Test]
        public void Research_InvalidAiTargetFallsBackAndIsCleared()
        {
            var ctx = NewWorld(2, out var civ, out _);
            var sys = new ResearchSystem();
            civ.ResearchPool = 55f;
            civ.CurrentResearchTechId = _casting; // later age: not researchable
            TickResearch(sys, ctx);
            Assert.AreEqual(-1, civ.CurrentResearchTechId);
            Assert.IsTrue(civ.KnownTechs.Contains(_fire), "fallback = cheapest researchable");
            Assert.AreEqual(1, civ.KnownTechs.Count);

            civ.CurrentResearchTechId = 9999; // unknown id
            civ.ResearchPool = 0f;
            TickResearch(sys, ctx);
            Assert.AreEqual(-1, civ.CurrentResearchTechId);

            civ.CurrentResearchTechId = _fire; // already known
            TickResearch(sys, ctx);
            Assert.AreEqual(-1, civ.CurrentResearchTechId);
        }

        [Test]
        public void Research_FallbackTieBreaksByLowestId()
        {
            var db = BuildCustomDb(d =>
            {
                d.Register(new TechDefinition { Key = "z.first", Name = "First", Age = AgeId.Dawn, Cost = 40f });
                d.Register(new TechDefinition { Key = "a.second", Name = "Second", Age = AgeId.Dawn, Cost = 40f });
                d.Register(new TechDefinition { Key = "m.pricey", Name = "Pricey", Age = AgeId.Dawn, Cost = 90f });
            });
            var ctx = NewWorld(2, out var civ, out _, RaceId.Human, db);
            int first = db.TechIdOf("z.first");
            Assert.AreEqual(first, TechQueries.CheapestResearchable(civ, db));
            civ.ResearchPool = 45f;
            TickResearch(new ResearchSystem(), ctx);
            Assert.IsTrue(civ.KnownTechs.Contains(first));
            Assert.AreEqual(1, civ.KnownTechs.Count);
            Assert.AreEqual(db.TechIdOf("a.second"), TechQueries.CheapestResearchable(civ, db));
        }

        [Test]
        public void Research_UnlockPublishesEventAndChronicle()
        {
            var ctx = NewWorld(2, out var civ, out var s);
            civ.KnownTechs.Add(_fire);
            civ.CurrentResearchTechId = _tools;
            civ.ResearchPool = 85f;
            int events = 0;
            ctx.Events.Subscribe<TechResearchedEvent>(e => { if (e.TechId == _tools && e.CivilizationId == civ.Id) events++; });

            TickResearch(new ResearchSystem(), ctx);

            Assert.IsTrue(civ.KnownTechs.Contains(_tools));
            Assert.IsTrue(civ.UnlockedBuildings[(int)BuildingType.Farm], "UnlockBuilding applied immediately");
            Assert.AreEqual(1, events);
            Assert.Greater(civ.Mem(ProgressionKeys.Dirty), 0f);
            var entry = ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1];
            Assert.AreEqual(ChronicleCategory.Technology, entry.Category);
            Assert.AreEqual(1, entry.Importance);
            Assert.AreEqual(civ.Id, entry.CivilizationId);
            Assert.AreEqual(s.Id, entry.SettlementId);
            Assert.IsTrue((entry.Text).EndsWith("discovered Tools.", StringComparison.Ordinal), entry.Text);
            Assert.IsTrue((entry.Text).StartsWith("The ", StringComparison.Ordinal), entry.Text);
        }

        [Test]
        public void Research_BoundedTo8TechsPerRun()
        {
            var db = BuildCustomDb(d =>
            {
                for (int i = 0; i < 12; i++) d.Register(new TechDefinition { Key = "cheap." + i, Name = "Cheap " + i, Age = AgeId.Dawn, Cost = 1f });
            });
            var ctx = NewWorld(2, out var civ, out _, RaceId.Human, db);
            civ.ResearchPool = 100f;
            var sys = new ResearchSystem();
            TickResearch(sys, ctx);
            Assert.AreEqual(ResearchSystem.MaxTechsPerRun, civ.KnownTechs.Count);
            TickResearch(sys, ctx);
            Assert.AreEqual(12, civ.KnownTechs.Count);
        }

        // ------------------------------------------------------------------ ages

        [Test]
        public void Age_AdvancesOnlyWhenAllCriteriaMet_AndPublishesOnce()
        {
            var ctx = NewWorld(3, out var civ, out var s);
            var sys = new AgeProgressionSystem();
            var events = new List<AgeAdvancedEvent>();
            ctx.Events.Subscribe<AgeAdvancedEvent>(e => events.Add(e));
            sys.Initialize(ctx);

            civ.TotalResearch = 150f;
            civ.KnownTechs.Add(_fire); // 1 of 2 Dawn techs
            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Dawn, civ.Age, "missing techs");

            civ.KnownTechs.Add(_hunting);
            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Dawn, civ.Age, "population 3 < 5");
            Assert.AreEqual(0.6f, civ.Mem(ProgressionKeys.AgeProgress), Eps, "progress = min criterion (pop 3/5)");

            ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Male, 25f);
            ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Female, 25f);
            civ.TotalResearch = 99f;
            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Dawn, civ.Age, "research below threshold");

            civ.TotalResearch = 150f;
            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Bronze, civ.Age);
            Assert.AreEqual(ctx.State.Tick, civ.AgeEnteredTick);
            Assert.Greater(civ.Mem(ProgressionKeys.Dirty), 0f);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(AgeId.Dawn, events[0].OldAge);
            Assert.AreEqual(AgeId.Bronze, events[0].NewAge);
            var entry = ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1];
            Assert.AreEqual(ChronicleCategory.Age, entry.Category);
            Assert.AreEqual(3, entry.Importance);
            Assert.IsTrue((entry.Text).EndsWith("have entered the Bronze Age.", StringComparison.Ordinal), entry.Text);

            for (int i = 0; i < 5; i++) TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Bronze, civ.Age, "Iron needs 400 research");
            Assert.AreEqual(1, events.Count, "event published once");
        }

        [Test]
        public void Age_AdvancesOneAgePerCheck_AndCapsTechRequirement()
        {
            var ctx = NewWorld(6, out var civ, out _);
            var sys = new AgeProgressionSystem();
            civ.TotalResearch = 1000f;
            foreach (int t in new[] { _fire, _tools, _hunting, _casting, _writing }) civ.KnownTechs.Add(t);

            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Bronze, civ.Age);
            var check = TechQueries.EvaluateNextAge(civ, _db, ctx.State);
            Assert.AreEqual(2, check.TechsRequired, "10 required, capped at the 2 Bronze techs that exist");
            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Iron, civ.Age);
            Assert.IsTrue((ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1].Text).EndsWith("have entered the Iron Age.", StringComparison.Ordinal), ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1].Text);
        }

        [Test]
        public void Age_NeverAdvancesPastAscension()
        {
            var ctx = NewWorld(6, out var civ, out _);
            var sys = new AgeProgressionSystem();
            int events = 0;
            ctx.Events.Subscribe<AgeAdvancedEvent>(e => events++);
            civ.Age = AgeId.Information;
            civ.TotalResearch = 1e9f;
            TickAges(sys, ctx); // stub Ascension has no tech/pop requirement
            Assert.AreEqual(AgeId.Ascension, civ.Age);
            for (int i = 0; i < 4; i++) TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Ascension, civ.Age);
            Assert.AreEqual(1, events);
            Assert.AreEqual(1f, civ.Mem(ProgressionKeys.AgeProgress), Eps);
            Assert.AreEqual(1f, TechQueries.ProgressToNextAge(civ, _db, ctx.State), Eps);
            Assert.AreEqual(0, TechQueries.MissingRequirements(civ, _db, ctx.State).Count);
        }

        [Test]
        public void FullPipeline_ReachesBronzeAndLearnsBronzeTechsAfterPrerequisites()
        {
            var ctx = NewWorld(6, out var civ, out var s);
            s.ServiceOutput[(int)ServiceType.Research] = 5f;
            var learned = new List<int>();
            ctx.Events.Subscribe<TechResearchedEvent>(e => learned.Add(e.TechId));
            TestWorlds.RunAll(new ISimSystem[] { new ModifierSystem(), new ResearchSystem(), new AgeProgressionSystem() }, ctx, 600);

            Assert.GreaterOrEqual((int)civ.Age, (int)AgeId.Bronze);
            Assert.IsTrue(civ.KnownTechs.Contains(_casting));
            Assert.Less(learned.IndexOf(_tools), learned.IndexOf(_casting), "prerequisite learned first");
            Assert.Less(learned.IndexOf(_fire), learned.IndexOf(_tools));
            Assert.AreEqual(learned.Count, civ.KnownTechs.Count, "each tech learned once");
        }

        // ------------------------------------------------------------------ robustness

        [Test]
        public void NoSettlementsAndBadNumbers_StayFiniteAndNeverThrow()
        {
            var ctx = TestWorlds.CreateContext(_db);
            var civ = ctx.Ops.CreateCivilization(RaceId.Human, "Wanderers");
            var ctx2Settlement = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(20, 20), 4, out var elves);
            ctx2Settlement.ServiceOutput[(int)ServiceType.Research] = float.NaN;
            elves.TotalResearch = float.NaN;
            elves.ResearchPool = float.PositiveInfinity;
            elves.Modifiers.Research = float.NaN;

            Assert.DoesNotThrow(() =>
                TestWorlds.RunAll(new ISimSystem[] { new ModifierSystem(), new ResearchSystem(), new AgeProgressionSystem() }, ctx, 200));

            foreach (var c in new[] { civ, elves })
            {
                AssertFinite(c.TotalResearch, "TotalResearch");
                AssertFinite(c.ResearchPool, "ResearchPool");
                AssertFinite(c.Modifiers.Research, "Research mod");
                for (int i = 0; i < c.Modifiers.Production.Length; i++) AssertFinite(c.Modifiers.Production[i], "Production");
                float p = c.Mem(ProgressionKeys.AgeProgress, -1f);
                Assert.That(p >= 0f && p <= 1f, "progress in 0..1: " + p);
            }
            Assert.AreEqual(0f, civ.TotalResearch, Eps);
            Assert.AreEqual(0, civ.Population);
            Assert.AreEqual(AgeId.Dawn, civ.Age, "Bronze needs a settlement");
            Assert.IsTrue(TechQueries.MissingRequirements(civ, _db, ctx.State).Contains("Settlements 0 / 1"));
            Assert.IsFalse(civ.KnownTechs.Contains(_elfMagic));
            var buffer = new List<int>();
            TechQueries.ResearchableTechs(elves, _db, buffer);
            Assert.IsTrue(buffer.Contains(_elfMagic), "race-restricted tech researchable by its race");
        }

        [Test]
        public void TechQueries_ReportResearchablesProgressAndMissingRequirements()
        {
            var ctx = NewWorld(3, out var civ, out _);
            var buffer = new List<int> { 42, 43 };
            Assert.AreEqual(2, TechQueries.ResearchableTechs(civ, _db, buffer));
            Assert.AreEqual(new[] { _fire, _hunting }, buffer.ToArray());

            civ.TotalResearch = 50f;
            var missing = TechQueries.MissingRequirements(civ, _db, ctx.State);
            Assert.AreEqual(3, missing.Count, string.Join("; ", missing));
            Assert.AreEqual(0f, TechQueries.ProgressToNextAge(civ, _db, ctx.State), Eps, "no techs known yet");

            civ.KnownTechs.Add(_fire);
            civ.KnownTechs.Add(_hunting);
            Assert.AreEqual(2, TechQueries.TechsKnownInAge(civ, _db, AgeId.Dawn));
            Assert.AreEqual(0, TechQueries.TechsKnownInAge(civ, _db, AgeId.Bronze));
            Assert.AreEqual(0.5f, TechQueries.ProgressToNextAge(civ, _db, ctx.State), Eps, "research 50/100 is the weakest criterion");
            Assert.AreEqual(1, TechQueries.ResearchableTechs(civ, _db, buffer));
            Assert.AreEqual(new[] { _tools }, buffer.ToArray());
            Assert.AreEqual(3, TechQueries.TechsAvailableInAge(_db, AgeId.Dawn, RaceId.Human));
            Assert.AreEqual(4, TechQueries.TechsAvailableInAge(_db, AgeId.Dawn, RaceId.Elf));
            Assert.AreEqual(0, TechQueries.ResearchableTechs(null, _db, buffer));
            Assert.AreEqual(0f, TechQueries.ResearchPerTick((SimContext)null, civ));
        }

        // ------------------------------------------------------------------ module entry point

        [Test]
        public void Module_CreatesFreshSystemsInRequiredOrder()
        {
            var a = new List<ISimSystem>(ProgressionModule.CreateSystems());
            var b = new List<ISimSystem>(ProgressionModule.CreateSystems());
            Assert.AreEqual(3, a.Count);
            Assert.IsTrue(a[0] is ModifierSystem, "1st: modifiers");
            Assert.IsTrue(a[1] is ResearchSystem, "2nd: research");
            Assert.IsTrue(a[2] is AgeProgressionSystem, "3rd: age progression");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreNotSame(a[i], b[i], "each call returns new instances");
                Assert.IsFalse(string.IsNullOrEmpty(a[i].Name));
                Assert.GreaterOrEqual(a[i].TickInterval, 1);
            }
            Assert.AreEqual(3, new HashSet<string> { a[0].Name, a[1].Name, a[2].Name }.Count, "unique profiler names");
        }

        // ------------------------------------------------------------------ research target publication

        [Test]
        public void Research_FallbackIsPublishedInMemory_AiFieldHoldsOnlyTheAiChoice()
        {
            var ctx = NewWorld(2, out var civ, out _);
            var sys = new ResearchSystem();
            sys.Initialize(ctx);
            Assert.AreEqual(-1, civ.CurrentResearchTechId);
            Assert.AreEqual((float)_fire, civ.Mem(ProgressionKeys.ResearchTarget, -99f), "fallback published at world start");

            TickResearch(sys, ctx);
            Assert.AreEqual(-1, civ.CurrentResearchTechId, "the fallback is never written into the AI field");
            Assert.AreEqual((float)_fire, civ.Mem(ProgressionKeys.ResearchTarget, -99f), "canonical target = fallback");
            Assert.AreEqual(_fire, TechQueries.EffectiveResearchTarget(civ, _db));

            civ.CurrentResearchTechId = _hunting; // the AI chooses
            TickResearch(sys, ctx);
            Assert.AreEqual(_hunting, civ.CurrentResearchTechId, "a valid AI choice is kept while it is researched");
            Assert.AreEqual((float)_hunting, civ.Mem(ProgressionKeys.ResearchTarget, -99f));

            civ.ResearchPool = 60f;
            TickResearch(sys, ctx);
            Assert.IsTrue(civ.KnownTechs.Contains(_hunting));
            Assert.AreEqual(-1, civ.CurrentResearchTechId, "cleared after the learn: the AI picks again");
            Assert.AreEqual((float)_fire, civ.Mem(ProgressionKeys.ResearchTarget, -99f), "meanwhile the next fallback is published");
        }

        [Test]
        public void Research_NonFiniteCostTechIsNeverChosenOrLearned()
        {
            var db = BuildCustomDb(d =>
            {
                d.Register(new AgeDefinition { Id = AgeId.Dawn, Name = "Dawn", ResearchThreshold = 0f });
                d.Register(new AgeDefinition { Id = AgeId.Bronze, Name = "Bronze", ResearchThreshold = 50f, RequiredTechsFromPreviousAge = 3, MinPopulation = 0 });
                d.Register(new TechDefinition { Key = "cheap", Name = "Cheap", Age = AgeId.Dawn, Cost = 5f });
                d.Register(new TechDefinition { Key = "inf", Name = "Infinite", Age = AgeId.Dawn, Cost = float.PositiveInfinity });
                d.Register(new TechDefinition { Key = "nan", Name = "Undefined", Age = AgeId.Dawn, Cost = float.NaN });
            });
            Assert.AreEqual(0, db.Validate().Count, "the database accepts these costs");
            int cheap = db.TechIdOf("cheap"), inf = db.TechIdOf("inf"), nan = db.TechIdOf("nan");
            var ctx = NewWorld(2, out var civ, out var s, RaceId.Human, db);

            Assert.IsTrue(TechQueries.IsAffordable(db.Tech(cheap)));
            Assert.IsFalse(TechQueries.IsAffordable(db.Tech(inf)));
            Assert.IsFalse(TechQueries.IsAffordable(db.Tech(nan)));
            Assert.AreEqual(cheap, TechQueries.CheapestResearchable(civ, db));
            var buffer = new List<int>();
            TechQueries.ResearchableTechs(civ, db, buffer);
            Assert.AreEqual(new[] { cheap }, buffer.ToArray());
            Assert.AreEqual(5f, TechQueries.MaxTechCostInAge(db, AgeId.Dawn, RaceId.Human), Eps, "non-finite costs do not inflate the idle cap");
            Assert.AreEqual(1, TechQueries.TechsAvailableInAge(db, AgeId.Dawn, RaceId.Human));
            Assert.AreEqual(1, TechQueries.EvaluateNextAge(civ, db, ctx.State).TechsRequired, "unlearnable techs are not required");

            var sys = new ResearchSystem();
            civ.ResearchPool = 1f;
            civ.CurrentResearchTechId = inf; // an AI that picked it
            TickResearch(sys, ctx);
            Assert.AreEqual(0, civ.KnownTechs.Count, "never learned for a token cost");
            Assert.AreEqual(-1, civ.CurrentResearchTechId, "an unaffordable AI choice is handed back");
            Assert.AreEqual((float)cheap, civ.Mem(ProgressionKeys.ResearchTarget, -99f));

            s.ServiceOutput[(int)ServiceType.Research] = 1f;
            TestWorlds.RunAll(new ISimSystem[] { new ModifierSystem(), sys, new AgeProgressionSystem() }, ctx, 400);
            Assert.IsTrue(civ.KnownTechs.Contains(cheap));
            Assert.IsFalse(civ.KnownTechs.Contains(inf));
            Assert.IsFalse(civ.KnownTechs.Contains(nan));
            Assert.GreaterOrEqual((int)civ.Age, (int)AgeId.Bronze);
            Assert.LessOrEqual(civ.ResearchPool, 2f * 5f + Eps);
            AssertFinite(civ.ResearchPool, "ResearchPool");
            AssertFinite(civ.TotalResearch, "TotalResearch");
        }

        // ------------------------------------------------------------------ attainability (age requirement cap)

        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Elf)]
        public void Age_TechRequirementIgnoresGeneralTechsGatedBehindAnotherRace(RaceId gatingRace)
        {
            var db = BuildCustomDb(d =>
            {
                d.Register(new AgeDefinition { Id = AgeId.Dawn, Name = "Dawn", ResearchThreshold = 0f });
                d.Register(new AgeDefinition { Id = AgeId.Bronze, Name = "Bronze", ResearchThreshold = 100f, RequiredTechsFromPreviousAge = 2, MinPopulation = 0 });
                d.Register(new TechDefinition { Key = "racial.only", Name = "Racial Lore", Age = AgeId.Dawn, Cost = 30f, AllowedRaces = gatingRace.ToMask() });
                d.Register(new TechDefinition { Key = "gen.gated", Name = "Gated", Age = AgeId.Dawn, Cost = 30f, PrerequisiteKeys = new[] { "racial.only" } });
                d.Register(new TechDefinition { Key = "gen.open", Name = "Open", Age = AgeId.Dawn, Cost = 40f });
            });
            int racial = db.TechIdOf("racial.only"), gated = db.TechIdOf("gen.gated"), open = db.TechIdOf("gen.open");
            Assert.IsFalse(TechQueries.IsAttainable(db, racial, RaceId.Human));
            Assert.IsFalse(TechQueries.IsAttainable(db, gated, RaceId.Human), "general tech behind a foreign racial tech");
            Assert.IsTrue(TechQueries.IsAttainable(db, gated, gatingRace));
            Assert.IsTrue(TechQueries.IsAttainable(db, open, RaceId.Human));
            Assert.AreEqual(1, TechQueries.TechsAvailableInAge(db, AgeId.Dawn, RaceId.Human));
            Assert.AreEqual(3, TechQueries.TechsAvailableInAge(db, AgeId.Dawn, gatingRace));
            Assert.AreEqual(40f, TechQueries.MaxTechCostInAge(db, AgeId.Dawn, RaceId.Human), Eps);

            var ctx = NewWorld(10, out var civ, out var s, RaceId.Human, db);
            s.ServiceOutput[(int)ServiceType.Research] = 5f;
            Assert.AreEqual(1, TechQueries.EvaluateNextAge(civ, db, ctx.State).TechsRequired, "capped at the 1 Dawn tech Humans can obtain");
            var advanced = new List<AgeAdvancedEvent>();
            ctx.Events.Subscribe<AgeAdvancedEvent>(e => { if (e.CivilizationId == civ.Id) advanced.Add(e); });

            TestWorlds.RunAll(new ISimSystem[] { new ModifierSystem(), new ResearchSystem(), new AgeProgressionSystem() }, ctx, 2000);

            Assert.IsTrue(civ.KnownTechs.Contains(open));
            Assert.IsFalse(civ.KnownTechs.Contains(gated));
            Assert.GreaterOrEqual((int)civ.Age, (int)AgeId.Bronze, "no permanent stall in Dawn");
            Assert.AreEqual(AgeId.Dawn, advanced[0].OldAge);
            Assert.AreEqual(AgeId.Bronze, advanced[0].NewAge);

            // The gating race can reach all three, so it still needs 2.
            TestWorlds.AddSettlement(ctx, gatingRace, new Vec2i(10, 10), 2, out var gaters);
            Assert.AreEqual(2, TechQueries.EvaluateNextAge(gaters, db, ctx.State).TechsRequired);

            // A Human civ handed the racial tech counts it (and can now research the gated one).
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(50, 50), 2, out var gifted);
            gifted.KnownTechs.Add(racial);
            Assert.AreEqual(2, TechQueries.TechsAvailableToCiv(gifted, db, AgeId.Dawn));
            var check = TechQueries.EvaluateNextAge(gifted, db, ctx.State);
            Assert.AreEqual(2, check.TechsRequired);
            Assert.AreEqual(1, check.TechsKnown);
            Assert.IsTrue(TechQueries.IsValidResearchTarget(gifted, db, gated));
        }

        [Test]
        public void Attainability_IsCycleSafeAndIterativeOnUnfrozenContent()
        {
            var db = new GameDatabase(); // not frozen: prerequisites are set by hand and nothing is validated
            var a = new TechDefinition { Key = "cyc.a", Age = AgeId.Dawn };
            var b = new TechDefinition { Key = "cyc.b", Age = AgeId.Dawn };
            var free = new TechDefinition { Key = "free", Age = AgeId.Dawn };
            var dangling = new TechDefinition { Key = "dangling", Age = AgeId.Dawn };
            var later = new TechDefinition { Key = "later", Age = AgeId.Iron };
            var early = new TechDefinition { Key = "early", Age = AgeId.Dawn };
            foreach (var t in new[] { a, b, free, dangling, later, early }) db.Register(t);
            a.Prerequisites = new[] { b.Id };
            b.Prerequisites = new[] { a.Id };
            dangling.Prerequisites = new[] { 9999 };
            early.Prerequisites = new[] { later.Id }; // later-age prerequisite: unobtainable while in Dawn
            Assert.IsFalse(TechQueries.IsAttainable(db, a.Id, RaceId.Human), "cycle");
            Assert.IsFalse(TechQueries.IsAttainable(db, b.Id, RaceId.Human), "cycle");
            Assert.IsTrue(TechQueries.IsAttainable(db, free.Id, RaceId.Human));
            Assert.IsFalse(TechQueries.IsAttainable(db, dangling.Id, RaceId.Human), "unknown prerequisite id");
            Assert.IsTrue(TechQueries.IsAttainable(db, later.Id, RaceId.Human));
            Assert.IsFalse(TechQueries.IsAttainable(db, early.Id, RaceId.Human), "later-age prerequisite");
            Assert.IsFalse(TechQueries.IsAttainable(db, -1, RaceId.Human));
            Assert.IsFalse(TechQueries.IsAttainable(db, free.Id, (RaceId)200), "undefined race value");
            Assert.AreEqual(1, TechQueries.TechsAvailableInAge(db, AgeId.Dawn, RaceId.Human));

            // A very long prerequisite chain must not overflow the stack.
            var chain = new GameDatabase();
            const int n = 20000;
            var defs = new TechDefinition[n];
            for (int i = 0; i < n; i++) { defs[i] = new TechDefinition { Key = "chain." + i, Age = AgeId.Dawn }; chain.Register(defs[i]); }
            for (int i = 1; i < n; i++) defs[i].Prerequisites = new[] { i - 1 };
            Assert.IsTrue(TechQueries.IsAttainable(chain, n - 1, RaceId.Human));
            Assert.AreEqual(n, TechQueries.TechsAvailableInAge(chain, AgeId.Dawn, RaceId.Human));
        }

        // ------------------------------------------------------------------ save/load consistency

        [Test]
        public void SaveLoad_InitializeOnTickedWorld_IsBitIdenticalAfterLearnAndAdvance()
        {
            var db = BuildCustomDb(d =>
            {
                d.Register(new AgeDefinition { Id = AgeId.Dawn, Name = "Dawn", ResearchThreshold = 0f });
                d.Register(new AgeDefinition { Id = AgeId.Bronze, Name = "Bronze", ResearchThreshold = 100f, RequiredTechsFromPreviousAge = 1, MinPopulation = 0, ResearchBonus = 0.5f });
                d.Register(new TechDefinition
                {
                    Key = "dawn.herbs", Name = "Herbs", Age = AgeId.Dawn, Cost = 30f,
                    Effects = new[] { TechEffect.ServiceBoost(ServiceType.Health, 0.5f), TechEffect.Unlock(BuildingType.Herbalist) }
                });
                d.Register(new TechDefinition { Key = "dawn.pricey", Name = "Pricey", Age = AgeId.Dawn, Cost = 5000f });
            });
            var ctx = NewWorld(3, out var civ, out var s, RaceId.Human, db);
            s.ServiceOutput[(int)ServiceType.Research] = 2f;
            var systems = new List<ISimSystem>(ProgressionModule.CreateSystems());
            long learnedAt = -1, advancedAt = -1;
            ctx.Events.Subscribe<TechResearchedEvent>(e => { if (learnedAt < 0) learnedAt = ctx.State.Tick; });
            ctx.Events.Subscribe<AgeAdvancedEvent>(e => { if (advancedAt < 0) advancedAt = ctx.State.Tick; });
            InitAll(systems, ctx);

            while (learnedAt < 0 && ctx.State.Tick < 400) StepAll(systems, ctx);
            Assert.Greater(learnedAt, 0L, "a tech was learned");
            Assert.AreEqual(0, learnedAt % 4, "learned on a research tick");
            Assert.AreNotEqual(0, (learnedAt + civ.Id) % ModifierSystem.RecomputeInterval, "not a periodic modifier slot");
            Assert.AreEqual(1.5f, civ.Modifiers.Service[(int)ServiceType.Health], Eps, "fresh at the tick boundary (next tick's Economy reads it)");
            Assert.IsTrue(civ.UnlockedBuildings[(int)BuildingType.Herbalist]);
            Assert.Greater(civ.Mem(ProgressionKeys.Dirty), 0f, "signal kept for other modules");
            AssertReloadIsIdentical(ctx, civ, "after a learn");

            while (advancedAt < 0 && ctx.State.Tick < 2000) StepAll(systems, ctx);
            Assert.Greater(advancedAt, 0L, "the civ advanced");
            Assert.AreEqual(AgeId.Bronze, civ.Age);
            Assert.AreNotEqual(0, (advancedAt + civ.Id) % ModifierSystem.RecomputeInterval, "not a periodic modifier slot");
            Assert.AreEqual(1.5f, civ.Modifiers.Research, Eps, "Bronze research bonus applied on the advance tick");
            AssertReloadIsIdentical(ctx, civ, "after an advance");

            // The next tick consumes the signal; values stay identical.
            float research = civ.Modifiers.Research;
            StepAll(systems, ctx);
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty));
            Assert.AreEqual(research, civ.Modifiers.Research);
        }

        [Test]
        public void Initialize_FreshWorldDerivesValues_TickedWorldKeepsSavedValuesAndPendingRequests()
        {
            var ctx = NewWorld(4, out var civ, out var s);
            civ.KnownTechs.Add(_fire);
            civ.Memory[ProgressionKeys.Dirty] = 1f;
            var systems = new List<ISimSystem>(ProgressionModule.CreateSystems());
            InitAll(systems, ctx); // tick 0: a new world
            Assert.AreEqual(1.65f, civ.Modifiers.Research, Eps, "fresh world: recomputed despite the request");
            Assert.AreEqual(4, civ.Population);
            Assert.AreEqual(1f, civ.Mem(ProgressionKeys.Dirty), "Initialize never consumes the request");
            Assert.AreEqual((float)_hunting, civ.Mem(ProgressionKeys.ResearchTarget, -99f));
            Assert.IsTrue(civ.Memory.ContainsKey(ProgressionKeys.AgeProgress));

            for (int i = 0; i < 30; i++) StepAll(systems, ctx);
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty));

            // After this tick's Progression phase another module grants a tech and asks for a recompute, a person
            // arrives, and the published values are whatever the last runs wrote.
            civ.KnownTechs.Add(_writing);
            civ.Memory[ProgressionKeys.Dirty] = 1f;
            ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Male, 30f);
            civ.Memory[ProgressionKeys.AgeProgress] = 0.123f;
            civ.Memory[ProgressionKeys.ResearchTarget] = _tools;
            AssertReloadIsIdentical(ctx, civ, "pending request on a ticked world");
            Assert.AreEqual(1.65f, civ.Modifiers.Research, Eps, "kept until the next tick, as in an uninterrupted run");
            Assert.AreEqual(4, civ.Population);

            var reloaded = new List<ISimSystem>(ProgressionModule.CreateSystems());
            InitAll(reloaded, ctx);
            StepAll(reloaded, ctx);
            Assert.AreEqual(1.5f * 1.3f, civ.Modifiers.Research, Eps, "the next tick applies the request (fire + writing)");
            Assert.AreEqual(1.5f, civ.Modifiers.Service[(int)ServiceType.Health], Eps);
            Assert.IsFalse(civ.Memory.ContainsKey(ProgressionKeys.Dirty));
            Assert.AreEqual(5, civ.Population);
        }

        // ------------------------------------------------------------------ criteria isolation & ownership

        [Test]
        public void Age_MinSettlementsCriterionIsIsolated()
        {
            var db = BuildCustomDb(d =>
            {
                d.Register(new AgeDefinition { Id = AgeId.Dawn, Name = "Dawn", ResearchThreshold = 0f });
                d.Register(new AgeDefinition { Id = AgeId.Bronze, Name = "Bronze", ResearchThreshold = 100f, RequiredTechsFromPreviousAge = 1, MinPopulation = 3, MinSettlements = 2 });
                d.Register(new AgeDefinition { Id = AgeId.Iron, Name = "Iron", ResearchThreshold = 200f, RequiredTechsFromPreviousAge = 0, MinPopulation = 0, MinSettlements = 2 });
                d.Register(new TechDefinition { Key = "dawn.a", Name = "A", Age = AgeId.Dawn, Cost = 10f });
            });
            var ctx = NewWorld(4, out var civ, out _, RaceId.Human, db);
            civ.KnownTechs.Add(db.TechIdOf("dawn.a"));
            civ.TotalResearch = 150f;
            var sys = new AgeProgressionSystem();
            int events = 0;
            ctx.Events.Subscribe<AgeAdvancedEvent>(e => events++);

            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Dawn, civ.Age, "one settlement short");
            var check = TechQueries.EvaluateNextAge(civ, db, ctx.State);
            Assert.IsTrue(check.ResearchMet && check.TechsMet && check.PopulationMet, "every other criterion is met");
            Assert.IsFalse(check.SettlementsMet);
            Assert.AreEqual(new[] { "Settlements 1 / 2" }, TechQueries.MissingRequirements(civ, db, ctx.State).ToArray());
            Assert.AreEqual(0.5f, civ.Mem(ProgressionKeys.AgeProgress), Eps);

            var second = ctx.Ops.FoundSettlement(civ, new Vec2i(10, 10));
            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Bronze, civ.Age);
            Assert.AreEqual(1, events);

            civ.TotalResearch = 250f; // Iron: research met, 2 settlements needed
            ctx.Ops.DestroySettlement(second, DestroyCause.Abandoned);
            Assert.AreEqual(1, TechQueries.EvaluateNextAge(civ, db, ctx.State).Settlements, "the destroyed settlement stops counting before the flush");
            ctx.Ops.FlushRemovals();
            TickAges(sys, ctx);
            Assert.AreEqual(AgeId.Bronze, civ.Age);
            Assert.AreEqual(1, events);
            Assert.AreEqual(new[] { "Settlements 1 / 2" }, TechQueries.MissingRequirements(civ, db, ctx.State).ToArray());
        }

        [Test]
        public void Ownership_DeadAndCapturedSettlementsStillListedAreIgnored()
        {
            var ctx = NewWorld(3, out var civA, out var s1);
            var s2 = ctx.Ops.FoundSettlement(civA, new Vec2i(10, 10));
            ctx.Ops.CreatePerson(s2, RaceId.Human, Sex.Female, 25f);
            ctx.Ops.CreatePerson(s2, RaceId.Human, Sex.Male, 25f);
            var civB = ctx.Ops.CreateCivilization(RaceId.Orc);
            s1.ServiceOutput[(int)ServiceType.Research] = 5f;
            s2.ServiceOutput[(int)ServiceType.Research] = 7f;

            Assert.AreEqual(2, TechQueries.AliveSettlementsOf(ctx.State, civA), "control");
            Assert.AreEqual(5, TechQueries.PopulationOf(ctx.State, civA), "control");
            Assert.AreEqual(TechQueries.ResearchPerServicePoint * (5f + 7f) + 0.004f * 5, TechQueries.ResearchPerTick(ctx, civA), Eps, "control");

            s1.IsAlive = false;              // destroyed, removal still pending
            s2.CivilizationId = civB.Id;     // captured
            Assert.IsTrue(civA.SettlementIds.Contains(s1.Id) && civA.SettlementIds.Contains(s2.Id), "both ids still listed");

            Assert.AreEqual(0f, TechQueries.ResearchPerTick(ctx, civA));
            Assert.AreEqual(0, TechQueries.PopulationOf(ctx.State, civA));
            Assert.AreEqual(0, TechQueries.AliveSettlementsOf(ctx.State, civA));
            var check = TechQueries.EvaluateNextAge(civA, _db, ctx.State);
            Assert.AreEqual(0, check.Settlements);
            Assert.AreEqual(0, check.Population);

            ctx.State.Tick = 1;
            new ModifierSystem().Tick(ctx);
            Assert.AreEqual(0, civA.Population);
            TickResearch(new ResearchSystem(), ctx);
            Assert.AreEqual(0f, civA.TotalResearch, "no research from towns it no longer holds");
        }

        [Test]
        public void CollapsedCivs_AreSkippedByAllSystems()
        {
            var ctx = NewWorld(6, out var civ, out var s);
            s.ServiceOutput[(int)ServiceType.Research] = 10f;
            civ.TotalResearch = 150f;
            civ.ResearchPool = 500f;
            civ.KnownTechs.Add(_fire);
            civ.KnownTechs.Add(_hunting);
            Assert.IsTrue(TechQueries.EvaluateNextAge(civ, _db, ctx.State).AllMet, "control: alive, it would advance");
            Assert.IsTrue(TechQueries.IsValidResearchTarget(civ, _db, _tools), "control: alive, it would learn Tools");

            ctx.Ops.CollapseCivilization(civ, -1);
            civ.Modifiers.Research = 7f;
            civ.Population = 42;
            int memBefore = civ.Memory.Count, knownBefore = civ.KnownTechs.Count;
            int techEntries = CountEntries(ctx, ChronicleCategory.Technology), ageEntries = CountEntries(ctx, ChronicleCategory.Age);
            int events = 0;
            ctx.Events.Subscribe<TechResearchedEvent>(e => events++);
            ctx.Events.Subscribe<AgeAdvancedEvent>(e => events++);

            TestWorlds.RunAll(new ISimSystem[] { new ModifierSystem(), new ResearchSystem(), new AgeProgressionSystem() }, ctx, 200);

            Assert.AreEqual(150f, civ.TotalResearch);
            Assert.AreEqual(500f, civ.ResearchPool);
            Assert.AreEqual(knownBefore, civ.KnownTechs.Count);
            Assert.AreEqual(AgeId.Dawn, civ.Age);
            Assert.AreEqual(7f, civ.Modifiers.Research, "modifiers untouched");
            Assert.AreEqual(42, civ.Population, "cached stats untouched");
            Assert.AreEqual(memBefore, civ.Memory.Count, "no blackboard writes");
            Assert.AreEqual(0, events);
            Assert.AreEqual(techEntries, CountEntries(ctx, ChronicleCategory.Technology));
            Assert.AreEqual(ageEntries, CountEntries(ctx, ChronicleCategory.Age));
        }

        // ------------------------------------------------------------------ helpers

        private static void InitAll(IList<ISimSystem> systems, SimContext ctx)
        {
            foreach (var sys in systems) { ctx.DeltaTicks = sys.TickInterval; sys.Initialize(ctx); }
            ctx.Ops.FlushRemovals();
        }

        /// <summary>One engine tick with the same interval gating as <see cref="TestWorlds.RunAll"/>.</summary>
        private static void StepAll(IList<ISimSystem> systems, SimContext ctx)
        {
            ctx.State.Tick++;
            foreach (var sys in systems)
            {
                if (sys.TickInterval > 1 && ctx.State.Tick % sys.TickInterval != 0) continue;
                ctx.DeltaTicks = sys.TickInterval;
                sys.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
        }

        /// <summary>Simulates a load: fresh system instances are initialized on the same state, which must not change.</summary>
        private static void AssertReloadIsIdentical(SimContext ctx, Civilization civ, string when)
        {
            string before = Snapshot(civ);
            InitAll(new List<ISimSystem>(ProgressionModule.CreateSystems()), ctx);
            Assert.AreEqual(before, Snapshot(civ), "Initialize on a ticked world changed state " + when);
        }

        private static int CountEntries(SimContext ctx, ChronicleCategory category)
        {
            int n = 0;
            foreach (var e in ctx.State.Chronicle.Entries) if (e.Category == category) n++;
            return n;
        }

        /// <summary>Bit-exact dump of everything the progression systems derive or publish for a civ.</summary>
        private static string Snapshot(Civilization civ)
        {
            var sb = new StringBuilder();
            var m = civ.Modifiers;
            for (int i = 0; i < m.Production.Length; i++) Bits(sb, "production[" + (ResourceType)i + "]", m.Production[i]);
            for (int i = 0; i < m.Service.Length; i++) Bits(sb, "service[" + (ServiceType)i + "]", m.Service[i]);
            Bits(sb, "research", m.Research); Bits(sb, "growth", m.Growth); Bits(sb, "buildSpeed", m.BuildSpeed);
            Bits(sb, "faith", m.Faith); Bits(sb, "military", m.Military); Bits(sb, "storage", m.Storage);
            Bits(sb, "trade", m.Trade); Bits(sb, "moveSpeed", m.MoveSpeed); Bits(sb, "healthBonus", m.HealthBonus);
            Bits(sb, "happinessBonus", m.HappinessBonus); Bits(sb, "lifespanBonusYears", m.LifespanBonusYears);
            Bits(sb, "totalResearch", civ.TotalResearch); Bits(sb, "researchPool", civ.ResearchPool);
            sb.Append("population=").Append(civ.Population).Append('\n');
            sb.Append("currentResearch=").Append(civ.CurrentResearchTechId).Append('\n');
            sb.Append("age=").Append(civ.Age).Append(" entered=").Append(civ.AgeEnteredTick).Append('\n');
            sb.Append("known=").Append(string.Join(",", civ.KnownTechs)).Append('\n');
            sb.Append("unlocked=");
            for (int i = 0; i < civ.UnlockedBuildings.Length; i++) if (civ.UnlockedBuildings[i]) sb.Append(i).Append(',');
            sb.Append('\n');
            var keys = new List<string>(civ.Memory.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) Bits(sb, "memory[" + k + "]", civ.Memory[k]);
            return sb.ToString();
        }

        private static void Bits(StringBuilder sb, string name, float v)
        {
            sb.Append(name).Append('=').Append(v.ToString("R")).Append(" (").Append(BitConverter.ToString(BitConverter.GetBytes(v))).Append(")\n");
        }
    }
}
