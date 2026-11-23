using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.WarPlanning
{
    public class WarPlannerSystemTests
    {
        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            TestDb.FillMissing(db);
            db.Race(RaceId.Orc).Aggression = 0.9f;
            db.Race(RaceId.Elf).Aggression = 0.1f;
            db.Race(RaceId.Human).Aggression = 0.5f;
            db.Freeze();
            return db;
        }

        private sealed class World
        {
            public SimContext Ctx;
            public Civilization Home, Rival;
            public Settlement Front, Interior, Enemy;
            public Relation Rel;
        }

        /// <summary>Home civ (two settlements: frontier and interior) facing a rival civ to the east.</summary>
        private static World Build(RaceId home, RaceId rival, ulong seed = 7)
        {
            var w = new World();
            w.Ctx = TestWorlds.CreateContext(CreateDb(), 128, seed);
            w.Front = TestWorlds.AddSettlement(w.Ctx, home, new Vec2i(60, 64), 20, out w.Home);
            w.Front.IsCapital = true;
            w.Home.CapitalId = w.Front.Id;
            w.Home.Age = AgeId.Medieval;
            w.Interior = w.Ctx.Ops.FoundSettlement(w.Home, new Vec2i(15, 64));
            for (int i = 0; i < 20; i++) w.Ctx.Ops.CreatePerson(w.Interior, home, i % 2 == 0 ? Sex.Female : Sex.Male, 25f);
            w.Enemy = TestWorlds.AddSettlement(w.Ctx, rival, new Vec2i(90, 64), 20, out w.Rival);
            w.Rival.CapitalId = w.Enemy.Id;
            w.Rival.Age = AgeId.Medieval;
            w.Rel = w.Ctx.State.Diplomacy.Get(w.Home.Id, w.Rival.Id);
            w.Rel.HasContact = true;
            return w;
        }

        private static War StartWar(World w)
        {
            var war = new War { Id = w.Ctx.State.Ids.War(), AttackerId = w.Rival.Id, DefenderId = w.Home.Id, StartTick = w.Ctx.State.Tick };
            w.Ctx.State.Diplomacy.Wars.Add(war);
            w.Rel.WarId = war.Id;
            w.Rel.Stance = DiplomaticStance.War;
            return war;
        }

        private static void EndWar(World w, War war)
        {
            war.IsActive = false;
            war.EndTick = w.Ctx.State.Tick;
            w.Rel.WarId = -1;
            w.Rel.Stance = DiplomaticStance.Neutral;
            w.Rel.Opinion = 0f;
            w.Rel.TruceUntilTick = w.Ctx.State.Tick + SimTime.YearsToTicks(10);
        }

        /// <summary>Runs council + planner for the given ticks (votes decay like in the real game).</summary>
        private static void Run(World w, WarPlannerSystem planner, int ticks, AdvisorCouncilSystem council)
        {
            for (int t = 0; t < ticks; t++)
            {
                w.Ctx.State.Tick++;
                if (w.Ctx.State.Tick % council.TickInterval == 0) { w.Ctx.DeltaTicks = council.TickInterval; council.Tick(w.Ctx); }
                if (w.Ctx.State.Tick % planner.TickInterval == 0) { w.Ctx.DeltaTicks = planner.TickInterval; planner.Tick(w.Ctx); }
                w.Ctx.Ops.FlushRemovals();
            }
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        [Test]
        public void Tension_LeadsToPreparationVotes()
        {
            var w = Build(RaceId.Human, RaceId.Orc);
            var planner = new WarPlannerSystem();
            var council = new AdvisorCouncilSystem();
            w.Rel.Stance = DiplomaticStance.Hostile;
            w.Rel.Opinion = -30f;
            for (int i = 0; i < 12; i++) { w.Rel.Opinion -= 2f; Run(w, planner, 12, council); }

            var phase = WarPlanKeys.PhaseOf(w.Home);
            Assert.IsTrue(phase == WarPhase.Tension || phase == WarPhase.Mobilizing, "phase " + phase);
            Assert.AreEqual(w.Rival.Id, WarPlanKeys.TargetOf(w.Home));
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.Barracks), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.Armory), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.StoneWall), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.Granary), 0f);
            Assert.Greater(AdvisorBoard.JobBias(w.Front, JobType.Soldier), 0f);
            Assert.Greater(AdvisorBoard.JobBias(w.Front, JobType.Smith), 0f);
            Assert.IsTrue(LogContains(w.Front, "Stockpiling grain for the coming war with the Orcs"));
            Assert.AreEqual(0, w.Ctx.State.Diplomacy.Wars.Count, "the planner never declares war itself");
        }

        [Test]
        public void Tension_Escalates_ToMobilization_AggressionShortensIt()
        {
            int Ticks(float aggression)
            {
                var w = Build(RaceId.Human, RaceId.Orc);
                AdvisorBoard.SetTrait(w.Home, AITrait.Aggression, aggression);
                var planner = new WarPlannerSystem();
                var council = new AdvisorCouncilSystem();
                w.Rel.Stance = DiplomaticStance.Hostile;
                w.Rel.Opinion = -45f;
                for (int t = 12; t <= 96 * 8; t += 12)
                {
                    Run(w, planner, 12, council);
                    if (WarPlanKeys.PhaseOf(w.Home) == WarPhase.Mobilizing) return t;
                }
                return int.MaxValue;
            }
            int hawk = Ticks(0.95f), dove = Ticks(0.05f);
            Assert.Less(hawk, int.MaxValue);
            Assert.Less(dove, int.MaxValue);
            Assert.Less(hawk, dove);
        }

        [Test]
        public void War_FrontierSoldierVotes_ExceedInterior_AndObjectiveChosen()
        {
            var w = Build(RaceId.Human, RaceId.Orc);
            var enemy2 = w.Ctx.Ops.FoundSettlement(w.Rival, new Vec2i(115, 64));
            enemy2.DefenseStrength = 1f;
            w.Enemy.DefenseStrength = 2f;
            StartWar(w);
            var planner = new WarPlannerSystem();
            Run(w, planner, 96, new AdvisorCouncilSystem());

            Assert.AreEqual(WarPhase.AtWar, WarPlanKeys.PhaseOf(w.Home));
            Assert.AreEqual(w.Rival.Id, WarPlanKeys.TargetOf(w.Home));
            float front = AdvisorBoard.JobBias(w.Front, JobType.Soldier);
            float interior = AdvisorBoard.JobBias(w.Interior, JobType.Soldier);
            Assert.Greater(interior, 0f, "interior keeps a moderate share");
            Assert.Greater(front, interior);
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.Armory), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.StoneWall), 0f);
            Assert.AreEqual(w.Enemy.Id, WarPlanKeys.ObjectiveOf(w.Home), "nearest weak settlement");
        }

        [Test]
        public void War_ThreatenedSettlement_VotesRepairs()
        {
            var w = Build(RaceId.Human, RaceId.Orc);
            var b = w.Ctx.Ops.CreateBuilding(w.Front, BuildingType.House, w.Ctx.Ops.FindFreeSite(w.Front.Center, 1, 1, 6), 0, true);
            b.Condition = 0.3f;
            StartWar(w);
            Run(w, new WarPlannerSystem(), 12, new AdvisorCouncilSystem());
            Assert.Greater(AdvisorBoard.JobBias(w.Front, JobType.Builder), 0f);
        }

        private static float WearinessAfter(int losses, float diplomacy)
        {
            var w = Build(RaceId.Human, RaceId.Orc);
            AdvisorBoard.SetTrait(w.Home, AITrait.Diplomacy, diplomacy);
            var war = StartWar(w);
            var planner = new WarPlannerSystem();
            var council = new AdvisorCouncilSystem();
            Run(w, planner, 12, council);
            war.DefenderLosses = losses;
            Run(w, planner, 96, council);
            float v = WarPlanKeys.WearinessOf(w.Home);
            Assert.IsFalse(float.IsNaN(v));
            Assert.That(v, Is.InRange(0f, 1f));
            return v;
        }

        [Test]
        public void Weariness_RisesWithLosses_AndDiplomacyRaisesSensitivity()
        {
            float none = WearinessAfter(0, 0.5f);
            float some = WearinessAfter(5, 0.5f);
            float many = WearinessAfter(15, 0.5f);
            Assert.Greater(none, 0f, "duration alone wears a civ down");
            Assert.Greater(some, none);
            Assert.Greater(many, some);
            Assert.Greater(WearinessAfter(5, 0.95f), WearinessAfter(5, 0.05f));
        }

        [Test]
        public void Weariness_RisesWithDuration()
        {
            var w = Build(RaceId.Human, RaceId.Orc);
            StartWar(w);
            var planner = new WarPlannerSystem();
            var council = new AdvisorCouncilSystem();
            Run(w, planner, 96, council);
            float early = WarPlanKeys.WearinessOf(w.Home);
            Run(w, planner, 96 * 4, council);
            Assert.Greater(WarPlanKeys.WearinessOf(w.Home), early);
        }

        [Test]
        public void Peace_StartsDemobilization_ThenReturnsToPeace()
        {
            var w = Build(RaceId.Human, RaceId.Orc);
            var war = StartWar(w);
            var planner = new WarPlannerSystem();
            var council = new AdvisorCouncilSystem();
            Run(w, planner, 96, council);
            Assert.AreEqual(WarPhase.AtWar, WarPlanKeys.PhaseOf(w.Home));
            Assert.Greater(WarPlanKeys.WearinessOf(w.Home), 0f);

            EndWar(w, war);
            Run(w, planner, 48, council);
            Assert.AreEqual(WarPhase.Demobilizing, WarPlanKeys.PhaseOf(w.Home));
            Assert.Less(AdvisorBoard.JobBias(w.Front, JobType.Soldier), 0f);
            Assert.Less(AdvisorBoard.JobBias(w.Interior, JobType.Soldier), 0f);
            Assert.Less(AdvisorBoard.BuildBias(w.Front, BuildingType.Barracks), 0f);
            Assert.Less(AdvisorBoard.BuildBias(w.Front, BuildingType.Armory), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.Farm), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(w.Front, BuildingType.House), 0f);
            Assert.IsTrue(LogContains(w.Front, "Swords to ploughshares"));
            Assert.AreEqual(-1, WarPlanKeys.ObjectiveOf(w.Home));

            Run(w, planner, 96 * 4, council);
            Assert.AreEqual(WarPhase.Demobilizing, WarPlanKeys.PhaseOf(w.Home), "about five years");
            Run(w, planner, 96 * 2, council);
            Assert.AreEqual(WarPhase.Peace, WarPlanKeys.PhaseOf(w.Home));
            Assert.Less(WarPlanKeys.WearinessOf(w.Home), 0.01f, "weariness fades in peace");
        }

        [Test]
        public void PeacefulCivs_NeverMobilize_WithoutTension()
        {
            var w = Build(RaceId.Elf, RaceId.Human);
            w.Rel.Stance = DiplomaticStance.Neutral;
            w.Rel.Opinion = 10f;
            var planner = new WarPlannerSystem();
            var council = new AdvisorCouncilSystem();
            for (int i = 0; i < 20 * 8; i++)
            {
                Run(w, planner, 12, council);
                Assert.AreEqual(WarPhase.Peace, WarPlanKeys.PhaseOf(w.Home));
                Assert.AreEqual(WarPhase.Peace, WarPlanKeys.PhaseOf(w.Rival));
                Assert.LessOrEqual(AdvisorBoard.JobBias(w.Front, JobType.Soldier), 0f);
                Assert.LessOrEqual(AdvisorBoard.BuildBias(w.Front, BuildingType.Barracks), 0f);
            }
            Assert.AreEqual(-1, WarPlanKeys.ObjectiveOf(w.Home));
        }

        [Test]
        public void Deterministic_SameSeedSameMemory()
        {
            Assert.AreEqual(Snapshot(11), Snapshot(11));
        }

        private static string Snapshot(ulong seed)
        {
            var w = Build(RaceId.Human, RaceId.Orc, seed);
            var planner = new WarPlannerSystem();
            var council = new AdvisorCouncilSystem();
            w.Rel.Stance = DiplomaticStance.Hostile;
            w.Rel.Opinion = -35f;
            for (int i = 0; i < 16; i++) { w.Rel.Opinion -= 1.5f; Run(w, planner, 12, council); }
            var war = StartWar(w);
            for (int i = 0; i < 16; i++) { war.DefenderLosses += 1; Run(w, planner, 12, council); }
            EndWar(w, war);
            Run(w, planner, 96, council);
            var sb = new StringBuilder();
            Append(sb, w.Home.Memory);
            Append(sb, w.Rival.Memory);
            Append(sb, w.Front.Memory);
            Append(sb, w.Interior.Memory);
            Append(sb, w.Enemy.Memory);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, Dictionary<string, float> mem)
        {
            var keys = new List<string>(mem.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            foreach (var k in keys)
            {
                Assert.IsFalse(float.IsNaN(mem[k]) || float.IsInfinity(mem[k]), k);
                sb.Append(k).Append('=').Append(mem[k].ToString("R")).Append(';');
            }
            sb.Append('|');
        }
    }
}
