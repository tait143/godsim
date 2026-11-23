using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Strategy
{
    public class ResearchDirectorSystemTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = StrategyTestSupport.CreateDb(); }

        private SimContext World(RaceId race, out Civilization civ, out Settlement s, int adults = 10)
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            s = TestWorlds.AddSettlement(ctx, race, new Vec2i(32, 32), adults, out civ);
            return ctx;
        }

        private string KeyOf(Civilization civ) => _db.Tech(civ.CurrentResearchTechId)?.Key;

        [Test]
        public void SystemMetadata()
        {
            var sys = new ResearchDirectorSystem();
            Assert.AreEqual(24, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
        }

        [Test]
        public void PicksValidResearchableTech_AndLogsOnCapital()
        {
            var ctx = World(RaceId.Human, out var civ, out var cap);
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), ctx);
            var tech = _db.Tech(civ.CurrentResearchTechId);
            Assert.IsNotNull(tech);
            Assert.IsTrue(_db.CanResearch(civ, tech));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Research: " + tech.Name + " (score ")));
        }

        [Test]
        public void Deterministic_AcrossIdenticalWorlds()
        {
            var a = World(RaceId.Human, out var civA, out _);
            var b = World(RaceId.Human, out var civB, out _);
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), a);
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), b);
            Assert.AreEqual(civA.CurrentResearchTechId, civB.CurrentResearchTechId);
        }

        [Test]
        public void RespectsPrerequisites()
        {
            var ctx = World(RaceId.Orc, out var civ, out _);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(10, 10), 4, out var enemy);
            StrategyTestSupport.StartWar(ctx, civ, enemy);
            civ.Memory[CivStrategyKeys.Focus] = CivStrategyKeys.FocusMilitary;
            civ.Memory[CivStrategyKeys.Posture] = CivStrategyKeys.PostureMilitarist;
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreNotEqual(StrategyTestSupport.Spears, KeyOf(civ), "Spears needs Tools first");
            Assert.IsTrue(_db.CanResearch(civ, _db.Tech(civ.CurrentResearchTechId)));

            civ.KnownTechs.Add(_db.TechIdOf(StrategyTestSupport.Tools));
            civ.CurrentResearchTechId = -1;
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.Spears, KeyOf(civ), "aggressive civ at war wants military tech");
        }

        [TestCase(CivStrategyKeys.FocusResearch, StrategyTestSupport.Lore)]
        [TestCase(CivStrategyKeys.FocusFaith, StrategyTestSupport.Rites)]
        [TestCase(CivStrategyKeys.FocusWealth, StrategyTestSupport.Barter)]
        [TestCase(CivStrategyKeys.FocusGrowth, StrategyTestSupport.Farming)]
        public void FocusInfluencesChoice(int focus, string expectedKey)
        {
            var ctx = World(RaceId.Human, out var civ, out _); // neutral personality
            civ.Memory[CivStrategyKeys.Focus] = focus;
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), ctx);
            Assert.AreEqual(expectedKey, KeyOf(civ));
        }

        [Test]
        public void PersonalityInfluencesChoice()
        {
            var ctx = World(RaceId.Saurian, out var saurian, out _);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 10, out var elf);
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), ctx);
            Assert.AreEqual(StrategyTestSupport.Rites, KeyOf(saurian));
            Assert.AreEqual(StrategyTestSupport.Lore, KeyOf(elf));
        }

        [Test]
        public void FoodShortage_PrefersFoodTech()
        {
            var ctx = World(RaceId.Human, out var civ, out var cap, 40);
            cap.Memory["econ.foodDays"] = 6f;
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), ctx);
            Assert.AreEqual(StrategyTestSupport.Farming, KeyOf(civ));
        }

        [Test]
        public void FoodShortage_FallbackFromStockpile()
        {
            var ctx = World(RaceId.Human, out var civ, out var cap, 40);
            cap.Stockpile.SetRaw(ResourceType.Food, 10f); // 10 / (40 * 0.05) = 5 days
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), ctx);
            Assert.AreEqual(StrategyTestSupport.Farming, KeyOf(civ));
        }

        [Test]
        public void KeepsValidCurrentResearch()
        {
            var ctx = World(RaceId.Elf, out var civ, out _);
            civ.CurrentResearchTechId = _db.TechIdOf(StrategyTestSupport.Rites);
            TestWorlds.Run(new ResearchDirectorSystem(), ctx, 24 * 4);
            Assert.AreEqual(StrategyTestSupport.Rites, KeyOf(civ));
        }

        [Test]
        public void ReplacesKnownOrInvalidResearch()
        {
            var ctx = World(RaceId.Human, out var civ, out _);
            int lore = _db.TechIdOf(StrategyTestSupport.Lore);
            civ.KnownTechs.Add(lore);
            civ.CurrentResearchTechId = lore;
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreNotEqual(lore, civ.CurrentResearchTechId);
            Assert.IsTrue(_db.CanResearch(civ, _db.Tech(civ.CurrentResearchTechId)));

            civ.CurrentResearchTechId = 12345; // garbage id
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.IsTrue(_db.CanResearch(civ, _db.Tech(civ.CurrentResearchTechId)));

            civ.CurrentResearchTechId = _db.TechIdOf(StrategyTestSupport.BronzeWork); // later age: not researchable
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreNotEqual(StrategyTestSupport.BronzeWork, KeyOf(civ));
        }

        [Test]
        public void LaterAgeTechs_BecomeEligibleAfterAdvancing()
        {
            var ctx = World(RaceId.Human, out var civ, out _);
            foreach (var t in _db.Techs) if (t.Age == AgeId.Dawn) civ.KnownTechs.Add(t.Id);
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(-1, civ.CurrentResearchTechId);
            civ.Age = AgeId.Bronze;
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.BronzeWork, KeyOf(civ));
        }

        [Test]
        public void NothingResearchable_SetsMinusOne_AndLogsOnce()
        {
            var ctx = World(RaceId.Human, out var civ, out var cap);
            foreach (var t in _db.Techs) civ.KnownTechs.Add(t.Id);
            civ.CurrentResearchTechId = 0;
            Assert.DoesNotThrow(() => TestWorlds.Run(new ResearchDirectorSystem(), ctx, 24 * 5));
            Assert.AreEqual(-1, civ.CurrentResearchTechId);
            Assert.AreEqual(1, cap.AiLog.FindAll(l => l.Contains("nothing left")).Count);
        }

        [Test]
        public void TieBreak_LowestId()
        {
            var db = StrategyTestSupport.CreateDb(extraTwins: true);
            var ctx = TestWorlds.CreateContext(db, 64);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 10, out var civ);
            foreach (var t in db.Techs) if (!t.Key.StartsWith("test.twin")) civ.KnownTechs.Add(t.Id);
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual("test.twinA", db.Tech(civ.CurrentResearchTechId).Key);
            Assert.AreEqual(sys.ScoreTech(ctx, civ, db.TechByKey("test.twinA")), sys.ScoreTech(ctx, civ, db.TechByKey("test.twinB")));
        }

        [Test]
        public void CheaperTechPreferred_WhenOtherwiseEqual()
        {
            var ctx = World(RaceId.Human, out var civ, out _);
            var sys = new ResearchDirectorSystem();
            var cheap = new TechDefinition { Id = 900, Key = "x.cheap", Age = AgeId.Dawn, Cost = 40f, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.1f) } };
            var dear = new TechDefinition { Id = 901, Key = "x.dear", Age = AgeId.Dawn, Cost = 400f, Effects = new[] { TechEffect.Of(TechEffectKind.ResearchMultiplier, 0.1f) } };
            Assert.Greater(sys.ScoreTech(ctx, civ, cheap), sys.ScoreTech(ctx, civ, dear));
            Assert.AreEqual(0f, sys.ScoreTech(ctx, civ, null));
        }

        [Test]
        public void DeadCivsIgnored()
        {
            var ctx = World(RaceId.Human, out var civ, out _);
            civ.IsAlive = false;
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), ctx);
            Assert.AreEqual(-1, civ.CurrentResearchTechId);
        }
    }
}
