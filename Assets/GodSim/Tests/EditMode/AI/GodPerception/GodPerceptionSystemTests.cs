using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.GodPerception
{
    public class GodPerceptionSystemTests
    {
        private static readonly Vec2i Home = new Vec2i(32, 32);

        private sealed class World
        {
            public SimContext Ctx;
            public GodPerceptionSystem Sys;
            public Civilization Civ;
            public Settlement Town;
        }

        private static World Create(float piety, RaceId race = RaceId.Human, ulong seed = 3)
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, 64, seed);
            var town = TestWorlds.AddSettlement(ctx, race, Home, 6, out var civ);
            AdvisorBoard.SetTrait(civ, AITrait.Piety, piety);
            var sys = new GodPerceptionSystem();
            ctx.DeltaTicks = sys.TickInterval;
            sys.Initialize(ctx);
            return new World { Ctx = ctx, Sys = sys, Civ = civ, Town = town };
        }

        private static void Cast(World w, GodPowerType power, Vec2i at, int radius = 3)
        {
            w.Ctx.Events.Publish(new GodPowerCastEvent { Power = power, Target = at, Radius = radius, Success = true, Message = power.ToString() });
        }

        private static void Run(World w, int ticks) => TestWorlds.Run(w.Sys, w.Ctx, ticks, false);

        private static int DivineChronicles(World w)
        {
            int n = 0;
            var e = w.Ctx.State.Chronicle.Entries;
            for (int i = 0; i < e.Count; i++) if (e[i].Category == ChronicleCategory.Divine) n++;
            return n;
        }

        // ------------------------------------------------------------------ classification
        [TestCase(GodPowerType.Bless, DivineActKind.Blessing)]
        [TestCase(GodPowerType.RevealOre, DivineActKind.Blessing)]
        [TestCase(GodPowerType.PlantForest, DivineActKind.Blessing)]
        [TestCase(GodPowerType.RaiseLand, DivineActKind.Shaping)]
        [TestCase(GodPowerType.SpawnTribe, DivineActKind.Shaping)]
        [TestCase(GodPowerType.Meteor, DivineActKind.Wrath)]
        [TestCase(GodPowerType.Drought, DivineActKind.Wrath)]
        public void Classify_MatchesDesign(GodPowerType power, DivineActKind expected)
        {
            Assert.AreEqual(expected, DivineActClassifier.Classify(power));
        }

        [Test]
        public void System_IsGodAdvisorEvery24Ticks()
        {
            var sys = new GodPerceptionSystem();
            Assert.AreEqual("god", sys.AdvisorId);
            Assert.AreEqual(24, sys.TickInterval);
        }

        // ------------------------------------------------------------------ blessings
        [Test]
        public void Blessing_RaisesGratitude_AndRecordsHistory()
        {
            var w = Create(0.5f);
            Cast(w, GodPowerType.Bless, Home);
            Assert.Greater(GodPerceptionQueries.Gratitude(w.Civ), 0.15f);
            Assert.AreEqual(1f, w.Civ.Mem(GodPerceptionKeys.BlessCount));
            Assert.AreEqual((float)DivineActKind.Blessing, w.Civ.Mem(GodPerceptionKeys.LastKind));
            Assert.AreEqual(DivineActKind.Blessing, GodPerceptionQueries.LastAct(w.Civ));
            Run(w, 24);
            Assert.Greater(w.Civ.Devotion, 0.5f);
            Assert.AreEqual(GodMood.Grateful, GodPerceptionQueries.Mood(w.Civ));
        }

        [Test]
        public void FailedCast_AndFarAway_AreIgnored()
        {
            var w = Create(0.5f);
            w.Ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Bless, Target = Home, Radius = 3, Success = false });
            Cast(w, GodPowerType.Bless, new Vec2i(2, 2), 2);
            Assert.AreEqual(0f, GodPerceptionQueries.Gratitude(w.Civ));
            Assert.IsFalse(w.Civ.Memory.ContainsKey(GodPerceptionKeys.BlessCount));
        }

        [Test]
        public void NearbyCast_IsFeltLessThanDirectHit()
        {
            var direct = Create(0.5f);
            var near = Create(0.5f);
            Cast(direct, GodPowerType.Bless, Home, 2);
            Cast(near, GodPowerType.Bless, new Vec2i(Home.X + 6, Home.Y), 2);
            float gd = GodPerceptionQueries.Gratitude(direct.Civ), gn = GodPerceptionQueries.Gratitude(near.Civ);
            Assert.Greater(gn, 0f);
            Assert.Greater(gd, gn);
        }

        [Test]
        public void RepeatedBlessings_BreedMoreGratitude()
        {
            var once = Create(0.5f);
            var many = Create(0.5f);
            Cast(once, GodPowerType.Bless, Home);
            for (int i = 0; i < 4; i++) Cast(many, GodPowerType.Bless, Home);
            Assert.Greater(GodPerceptionQueries.Gratitude(many.Civ), GodPerceptionQueries.Gratitude(once.Civ) * 2.5f);
            Assert.AreEqual(4f, many.Civ.Mem(GodPerceptionKeys.Streak));
        }

        [Test]
        public void CherishedGift_MovesRaceMore()
        {
            var dwarfOre = Create(0.5f, RaceId.Dwarf);
            var dwarfRain = Create(0.5f, RaceId.Dwarf);
            Cast(dwarfOre, GodPowerType.RevealOre, Home);
            Cast(dwarfRain, GodPowerType.Rain, Home);
            Assert.Greater(GodPerceptionQueries.Gratitude(dwarfOre.Civ), GodPerceptionQueries.Gratitude(dwarfRain.Civ));
        }

        // ------------------------------------------------------------------ wrath & piety
        [Test]
        public void Wrath_PiousCiv_FearsAndGrowsDevout()
        {
            var w = Create(0.95f);
            Cast(w, GodPowerType.Lightning, Home);
            Assert.Greater(GodPerceptionQueries.Fear(w.Civ), 0.2f);
            Assert.Less(GodPerceptionQueries.Resentment(w.Civ), 0.01f);
            Run(w, 24);
            Assert.Greater(w.Civ.Devotion, 0.5f);
            Assert.AreEqual(1f, w.Civ.Mem(GodPerceptionKeys.WrathCount));
        }

        [Test]
        public void Wrath_ImpiousCiv_FearsAndResents_DevotionAndFavorFall()
        {
            var w = Create(0.1f);
            Cast(w, GodPowerType.Lightning, Home);
            Assert.Greater(GodPerceptionQueries.Fear(w.Civ), 0.2f);
            Assert.Greater(GodPerceptionQueries.Resentment(w.Civ), 0.2f);
            Run(w, 24);
            Assert.Less(w.Civ.Devotion, 0.5f);
            Assert.Less(w.Civ.GodFavor, 0f);
        }

        [Test]
        public void RepeatedWrath_ImpiousCiv_TurnsItsBackOnTheHeavens()
        {
            var w = Create(0.1f);
            for (int i = 0; i < 5; i++) Cast(w, GodPowerType.Meteor, Home);
            Run(w, 24);
            Assert.IsTrue(GodPerceptionQueries.HasTurnedAway(w.Civ));
            Assert.AreEqual(GodMood.Resentful, GodPerceptionQueries.Mood(w.Civ));
            var faith = GodPerceptionQueries.PreferredFaithBuilding(w.Ctx.Db, w.Civ);
            Assert.Less(AdvisorBoard.BuildBias(w.Town, faith), 0f);
            StringAssert.Contains("Resentful", GodPerceptionQueries.Describe(w.Civ));
        }

        [Test]
        public void Devotion_NudgePerRun_IsCapped()
        {
            var w = Create(1f);
            for (int i = 0; i < 12; i++) Cast(w, GodPowerType.Bless, Home);
            float before = w.Civ.Devotion;
            Run(w, 24);
            Assert.LessOrEqual(w.Civ.Devotion - before, GodPerceptionTuning.MaxNudgePerRun + 1e-5f);
            Assert.Greater(w.Civ.Devotion, before);
        }

        // ------------------------------------------------------------------ erratic god
        [Test]
        public void ErraticGod_RaisesUncertainty_AndLowersDevotion()
        {
            var steady = Create(0.55f);
            var erratic = Create(0.55f);
            for (int i = 0; i < 6; i++)
            {
                Cast(steady, GodPowerType.Bless, Home);
                Cast(erratic, i % 2 == 0 ? GodPowerType.Bless : GodPowerType.Lightning, Home);
                Run(steady, 24);
                Run(erratic, 24);
            }
            Assert.Greater(GodPerceptionQueries.Uncertainty(erratic.Civ), 0.3f);
            Assert.AreEqual(0f, GodPerceptionQueries.Uncertainty(steady.Civ));
            Assert.Less(erratic.Civ.Devotion, steady.Civ.Devotion);
            Assert.Less(erratic.Civ.Devotion, 0.5f);
            Assert.GreaterOrEqual(erratic.Civ.Mem(GodPerceptionKeys.Flips), 5f);
        }

        // ------------------------------------------------------------------ memory decay
        [Test]
        public void Feelings_DecaySlowly_FearFasterThanGratitude()
        {
            var w = Create(0.6f);
            Cast(w, GodPowerType.Bless, Home);
            Cast(w, GodPowerType.Bless, Home);
            Cast(w, GodPowerType.Earthquake, new Vec2i(Home.X + 4, Home.Y), 1);
            float g0 = GodPerceptionQueries.Gratitude(w.Civ), f0 = GodPerceptionQueries.Fear(w.Civ);
            Run(w, 24 * 10);
            float g1 = GodPerceptionQueries.Gratitude(w.Civ), f1 = GodPerceptionQueries.Fear(w.Civ);
            Assert.Less(g1, g0);
            Assert.Less(f1, f0);
            Assert.Greater(g1, g0 * 0.5f, "gratitude should fade slowly");
            Assert.Less(f1 / f0, g1 / g0, "fear fades faster than gratitude");
            Run(w, 24 * 200);
            Assert.Less(GodPerceptionQueries.Gratitude(w.Civ), 0.05f);
            Assert.AreEqual(GodMood.Indifferent, GodPerceptionQueries.Mood(w.Civ));
        }

        [Test]
        public void LongMemoryRace_RemembersLonger()
        {
            var human = Create(0.5f, RaceId.Human);
            var saurian = Create(0.5f, RaceId.Saurian);
            Cast(human, GodPowerType.Bless, Home);
            Cast(saurian, GodPowerType.Bless, Home);
            float h0 = GodPerceptionQueries.Gratitude(human.Civ), s0 = GodPerceptionQueries.Gratitude(saurian.Civ);
            Run(human, 24 * 20);
            Run(saurian, 24 * 20);
            Assert.Greater(GodPerceptionQueries.Gratitude(saurian.Civ) / s0, GodPerceptionQueries.Gratitude(human.Civ) / h0);
        }

        // ------------------------------------------------------------------ responses (votes)
        [Test]
        public void Wrath_ProducesAppeasementVote_WithReadableReason()
        {
            var w = Create(0.9f);
            Cast(w, GodPowerType.Lightning, Home);
            Run(w, 24);
            var faith = GodPerceptionQueries.PreferredFaithBuilding(w.Ctx.Db, w.Civ);
            Assert.Greater(AdvisorBoard.BuildBias(w.Town, faith), 0.3f);
            Assert.Greater(AdvisorBoard.JobBias(w.Town, JobType.Priest), 0f);
            Assert.Greater(AdvisorBoard.Urgency(w.Town, "god"), 0.5f);
            bool found = false;
            foreach (var line in w.Town.AiLog) if (line.Contains("Offerings to calm the Storm-Father")) found = true;
            Assert.IsTrue(found, string.Join(" | ", w.Town.AiLog));
        }

        [Test]
        public void Blessing_ProducesThanksgivingVote()
        {
            var w = Create(0.9f, RaceId.Saurian);
            Cast(w, GodPowerType.Rain, Home);
            Run(w, 24);
            var faith = GodPerceptionQueries.PreferredFaithBuilding(w.Ctx.Db, w.Civ);
            Assert.Greater(AdvisorBoard.BuildBias(w.Town, faith), 0.3f);
            bool found = false;
            foreach (var line in w.Town.AiLog) if (line.Contains("Thanksgiving to the Sun-Serpent")) found = true;
            Assert.IsTrue(found, string.Join(" | ", w.Town.AiLog));
        }

        [Test]
        public void ResponsePressure_FadesWithoutNewActs()
        {
            var w = Create(0.9f);
            Cast(w, GodPowerType.Lightning, Home);
            Run(w, 24 * 12);
            Assert.IsFalse(w.Town.Memory.ContainsKey(GodPerceptionKeys.SettlementAppease));
            Assert.AreEqual(0f, AdvisorBoard.Urgency(w.Town, "god"));
        }

        // ------------------------------------------------------------------ natural disasters
        [Test]
        public void NaturalDisaster_BlamedByPious_IgnoredByImpious()
        {
            var pious = Create(0.95f);
            var impious = Create(0.2f);
            var e = new DisasterEvent { Type = DisasterType.Volcano, Position = Home, Radius = 3, Severity = 0.8f };
            pious.Ctx.Events.Publish(e);
            impious.Ctx.Events.Publish(e);
            Run(pious, 24);
            Run(impious, 24);
            Assert.Greater(GodPerceptionQueries.Fear(pious.Civ), 0.1f);
            Assert.AreEqual(1f, pious.Civ.Mem(GodPerceptionKeys.BlameCount));
            Assert.AreEqual(0f, GodPerceptionQueries.Fear(impious.Civ));
            Assert.AreEqual(DivineActKind.BlamedDisaster, GodPerceptionQueries.LastAct(pious.Civ));
        }

        [Test]
        public void WrathCast_WithItsOwnDisasterEvent_IsCountedOnce()
        {
            var w = Create(0.95f);
            w.Ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Earthquake, Position = Home, Radius = 3, Severity = 1f });
            Cast(w, GodPowerType.Earthquake, Home);
            Run(w, 48);
            Assert.AreEqual(1f, w.Civ.Mem(GodPerceptionKeys.WrathCount));
            Assert.AreEqual(0f, w.Civ.Mem(GodPerceptionKeys.BlameCount));
        }

        // ------------------------------------------------------------------ chronicle
        [Test]
        public void Chronicle_IsRareAndRateLimited()
        {
            var w = Create(0.1f);
            for (int t = 0; t < 8; t++)
            {
                for (int i = 0; i < 4; i++) Cast(w, i % 2 == 0 ? GodPowerType.Meteor : GodPowerType.Bless, Home);
                Run(w, 12);
            }
            // 96 ticks of relentless casting: at most one entry (two-year per-civ gap).
            Assert.AreEqual(1, DivineChronicles(w));
            var entries = w.Ctx.State.Chronicle.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Category != ChronicleCategory.Divine) continue;
                Assert.That(entries[i].Importance, Is.InRange(1, 2));
                Assert.AreEqual(w.Civ.Id, entries[i].CivilizationId);
            }
            Run(w, 24 * 10);
            Assert.LessOrEqual(DivineChronicles(w), 3);
        }

        [Test]
        public void Chronicle_FirstBlessing_IsRecorded()
        {
            var w = Create(0.5f);
            Cast(w, GodPowerType.Bless, Home);
            Run(w, 24);
            Assert.AreEqual(1, DivineChronicles(w));
            StringAssert.Contains("first gift", w.Ctx.State.Chronicle.Entries[w.Ctx.State.Chronicle.Entries.Count - 1].Text);
        }

        [Test]
        public void Chronicle_GreatWrath_PiousReadsJustPunishment()
        {
            var w = Create(0.95f);
            Cast(w, GodPowerType.Plague, Home);
            Cast(w, GodPowerType.Plague, Home);
            Run(w, 24);
            var last = w.Ctx.State.Chronicle.Entries[w.Ctx.State.Chronicle.Entries.Count - 1];
            Assert.AreEqual(ChronicleCategory.Divine, last.Category);
            Assert.AreEqual(2, last.Importance);
            StringAssert.Contains("just punishment", last.Text);
            StringAssert.Contains("the Pale Rider", last.Text);
        }

        // ------------------------------------------------------------------ queries & robustness
        [Test]
        public void Queries_AreNullSafe_AndFreshCivIsIndifferent()
        {
            var w = Create(0.5f);
            Assert.AreEqual(GodMood.Indifferent, GodPerceptionQueries.Mood(null));
            Assert.AreEqual(GodMood.Indifferent, GodPerceptionQueries.Mood(w.Civ));
            Assert.IsFalse(string.IsNullOrEmpty(GodPerceptionQueries.Describe(null)));
            StringAssert.StartsWith("Indifferent", GodPerceptionQueries.Describe(w.Civ));
            foreach (GodMood m in System.Enum.GetValues(typeof(GodMood)))
                Assert.IsFalse(string.IsNullOrEmpty(GodPerceptionQueries.MoodText(m)));
        }

        [Test]
        public void PiousCivUnderWrath_IsFearfulOrReverent()
        {
            var w = Create(0.95f, RaceId.Saurian);
            for (int i = 0; i < 4; i++) Cast(w, GodPowerType.Drought, Home);
            Run(w, 24);
            Assert.AreEqual(GodMood.Fearful, GodPerceptionQueries.Mood(w.Civ));
            StringAssert.Contains("the Thirsting Sun", GodPerceptionQueries.Describe(w.Civ));
        }

        [Test]
        public void GarbageEvents_NeverProduceNaN()
        {
            var w = Create(0.5f);
            w.Ctx.Events.Publish(new GodPowerCastEvent { Power = (GodPowerType)200, Target = Home, Radius = 3, Success = true });
            w.Ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Meteor, Target = Vec2i.Invalid, Radius = 3, Success = true });
            w.Ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Meteor, Target = Home, Radius = -50, Success = true });
            w.Ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Bless, Target = Home, Radius = int.MaxValue, Success = true });
            w.Ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Flood, Position = Home, Radius = 999, Severity = float.NaN });
            w.Civ.Devotion = float.NaN;
            Run(w, 48);
            foreach (var kv in w.Civ.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            Assert.IsFalse(float.IsNaN(w.Civ.Devotion));
            Assert.IsFalse(float.IsNaN(w.Civ.GodFavor));
        }

        [Test]
        public void Deterministic_SameInputsSameState()
        {
            Assert.AreEqual(Scenario(11), Scenario(11));
        }

        private static string Scenario(ulong seed)
        {
            var w = Create(0.6f, RaceId.Elf, seed);
            var other = w.Ctx.Ops.FoundSettlement(w.Civ, new Vec2i(44, 30));
            GodPowerType[] seq = { GodPowerType.Bless, GodPowerType.Wildfire, GodPowerType.PlantForest, GodPowerType.RaiseLand, GodPowerType.Lightning, GodPowerType.Heal };
            for (int i = 0; i < 12; i++)
            {
                Cast(w, seq[i % seq.Length], i % 3 == 0 ? Home : new Vec2i(44, 30), 2 + i % 3);
                if (i % 4 == 0) w.Ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Storm, Position = new Vec2i(40, 31), Radius = 4, Severity = 0.7f });
                Run(w, 30);
            }
            var sb = new StringBuilder();
            Dump(sb, w.Civ.Memory);
            Dump(sb, w.Town.Memory);
            Dump(sb, other.Memory);
            sb.Append(w.Civ.Devotion.ToString("R")).Append('|').Append(w.Civ.GodFavor.ToString("R")).Append('|');
            foreach (var e in w.Ctx.State.Chronicle.Entries) sb.Append(e.Tick).Append(e.Text).Append(';');
            return sb.ToString();
        }

        private static void Dump(StringBuilder sb, Dictionary<string, float> mem)
        {
            var keys = new List<string>(mem.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            foreach (var k in keys) sb.Append(k).Append('=').Append(mem[k].ToString("R")).Append(';');
            sb.Append('#');
        }
    }
}
