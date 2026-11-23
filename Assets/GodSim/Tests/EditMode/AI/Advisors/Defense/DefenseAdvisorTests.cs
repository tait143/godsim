using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Advisors.Defense
{
    public class DefenseAdvisorTests
    {
        private static readonly BuildingType[] Military =
        {
            BuildingType.Palisade, BuildingType.Watchtower, BuildingType.Barracks, BuildingType.Armory,
            BuildingType.StoneWall, BuildingType.Castle, BuildingType.MilitaryBase,
        };

        // ------------------------------------------------------------------ fixtures
        private static GameDatabase MakeDb()
        {
            var db = TestDb.CreateStub(false);
            var orc = db.Races[(int)RaceId.Orc]; orc.Aggression = 0.9f; orc.Diplomacy = 0.2f; orc.Xenophobia = 0.4f;
            var elf = db.Races[(int)RaceId.Elf]; elf.Aggression = 0.1f; elf.Diplomacy = 0.8f; elf.Xenophobia = 0.6f;
            var dwarf = db.Races[(int)RaceId.Dwarf]; dwarf.Aggression = 0.5f; dwarf.Diplomacy = 0.5f;

            db.Register(Def(BuildingType.Palisade, BuildingCategory.Military, AgeId.Bronze, AgeId.Medieval, 2f, JobType.None, 0, -1, 30f, 30f));
            db.Register(Def(BuildingType.Watchtower, BuildingCategory.Military, AgeId.Iron, AgeId.Industrial, 3f, JobType.Soldier, 1, -1, 49f, 35f));
            db.Register(Def(BuildingType.Barracks, BuildingCategory.Military, AgeId.Iron, AgeId.Ascension, 2f, JobType.Soldier, 6, 2, 84f, 60f));
            db.Register(Def(BuildingType.StoneWall, BuildingCategory.Military, AgeId.Classical, AgeId.Industrial, 4f, JobType.None, 0, 24, 34f, 100f));
            db.Register(Def(BuildingType.Castle, BuildingCategory.Military, AgeId.Medieval, AgeId.Renaissance, 20f, JobType.Soldier, 10, 1, 325f, 400f));
            db.Register(Def(BuildingType.MilitaryBase, BuildingCategory.Military, AgeId.Modern, AgeId.Ascension, 60f, JobType.Soldier, 20, 1, 410f, 550f));
            var armory = Def(BuildingType.Armory, BuildingCategory.Industry, AgeId.Iron, AgeId.Ascension, 0f, JobType.Smith, 2, -1, 64f, 45f);
            armory.InputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.04f) };
            armory.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Weapons, 0.03f) };
            db.Register(armory);
            db.Freeze();
            return db;
        }

        private static BuildingDefinition Def(BuildingType t, BuildingCategory cat, AgeId min, AgeId max, float defense, JobType job, int slots, int maxPer, float cost, float work)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = cat, MinAge = min, MaxAge = max,
                Job = job, JobSlots = slots, MaxPerSettlement = maxPer, BuildWork = work,
                Cost = new[] { new ResourceAmount(ResourceType.Stone, cost) },
                Services = defense > 0f ? new[] { new ServiceAmount(ServiceType.Defense, defense) } : ServiceAmount.None,
            };
        }

        private static void DeclareWar(SimContext ctx, Civilization a, Civilization b)
        {
            var war = new War { Id = ctx.State.Ids.War(), AttackerId = a.Id, DefenderId = b.Id, StartTick = ctx.State.Tick };
            ctx.State.Diplomacy.Wars.Add(war);
            var rel = ctx.State.Diplomacy.Get(a.Id, b.Id);
            rel.WarId = war.Id; rel.Stance = DiplomaticStance.War; rel.HasContact = true;
        }

        private static Army AddArmy(SimContext ctx, Civilization owner, Settlement target, Vec2i pos, float strength, ArmyState state = ArmyState.Marching)
        {
            var a = new Army
            {
                Id = ctx.State.Ids.Army(), CivilizationId = owner.Id, TargetSettlementId = target.Id, State = state,
                Position = pos, Destination = target.Center, Strength = strength,
            };
            ctx.State.Diplomacy.Armies.Add(a);
            return a;
        }

        private static float MaxMilitaryBias(Settlement s)
        {
            float best = float.NegativeInfinity;
            for (int i = 0; i < Military.Length; i++) best = System.Math.Max(best, AdvisorBoard.BuildBias(s, Military[i]));
            return best;
        }

        /// <summary>Human home town at (20,20), Iron age, 40 adults; optional neighbour of a race at a distance.</summary>
        private static Settlement Scenario(GameDatabase db, out SimContext ctx, out Civilization us, RaceId ourRace = RaceId.Human, int size = 96)
        {
            ctx = TestWorlds.CreateContext(db, size);
            var home = TestWorlds.AddSettlement(ctx, ourRace, new Vec2i(20, 20), 40, out us);
            us.Age = AgeId.Iron;
            return home;
        }

        private static Settlement Neighbour(SimContext ctx, RaceId race, Vec2i at, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, race, at, 30, out civ);
            civ.Age = AgeId.Iron;
            return s;
        }

        // ------------------------------------------------------------------ tests
        [Test]
        public void ApproachingEnemyArmy_GivesStrongFortificationAndSoldierVotes()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            Neighbour(ctx, RaceId.Orc, new Vec2i(70, 20), out var orcs);
            DeclareWar(ctx, orcs, us);
            var army = AddArmy(ctx, orcs, home, new Vec2i(home.Center.X + 14, home.Center.Y), 25f);
            var adv = new DefenseAdvisor();

            TestWorlds.Run(adv, ctx, 24); // one council cycle
            float wallOrBarracks = System.Math.Max(System.Math.Max(AdvisorBoard.BuildBias(home, BuildingType.Palisade), AdvisorBoard.BuildBias(home, BuildingType.Watchtower)), AdvisorBoard.BuildBias(home, BuildingType.Barracks));
            Assert.That(wallOrBarracks, Is.GreaterThanOrEqualTo(0.5f), "strong fortification / garrison vote");
            Assert.That(AdvisorBoard.JobBias(home, JobType.Soldier), Is.GreaterThanOrEqualTo(0.5f), "strong soldier vote");
            Assert.That(AdvisorBoard.Urgency(home, DefenseAdvisor.Id), Is.GreaterThanOrEqualTo(0.6f));
            Assert.That(home.Memory.ContainsKey(DefenseAdvisorKeys.ArmyDist), Is.True);

            bool explained = false;
            for (int i = 0; i < home.AiLog.Count; i++) if (home.AiLog[i].Contains("Orc army") && home.AiLog[i].Contains("tiles away") && home.AiLog[i].Contains("defense")) explained = true;
            Assert.That(explained, Is.True, "a player-readable reason is logged: " + string.Join(" | ", home.AiLog));

            // never vote for buildings this civ cannot build yet
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(home, BuildingType.StoneWall));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(home, BuildingType.Castle));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(home, BuildingType.MilitaryBase));

            // the army closes in: the trend is remembered
            army.Position = new Vec2i(home.Center.X + 8, home.Center.Y);
            TestWorlds.Run(adv, ctx, 12, false);
            Assert.That(home.Mem(DefenseAdvisorKeys.ArmyClosing), Is.GreaterThan(0f));
            Assert.That(home.Mem(DefenseAdvisorKeys.Imminence), Is.GreaterThan(0.7f));
        }

        [Test]
        public void PeacefulInterior_VotesZeroOrNegativeOnMilitary()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            var far = Neighbour(ctx, RaceId.Elf, new Vec2i(85, 85), out var elves);
            ctx.State.Diplomacy.Get(us.Id, elves.Id).Stance = DiplomaticStance.Friendly;
            var adv = new DefenseAdvisor();

            TestWorlds.Run(adv, ctx, 24);
            for (int i = 0; i < Military.Length; i++)
                Assert.That(AdvisorBoard.BuildBias(home, Military[i]), Is.LessThanOrEqualTo(0f), Military[i].ToString());
            Assert.That(AdvisorBoard.BuildBias(home, BuildingType.Barracks), Is.LessThan(0f), "guns vs butter: mild negative vote");
            Assert.That(AdvisorBoard.BuildBias(home, BuildingType.Palisade), Is.LessThan(0f), "guns vs butter: mild negative vote");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(home, BuildingType.Castle), "unavailable types are never voted, not even negatively");
            Assert.That(AdvisorBoard.BuildBias(home, BuildingType.Barracks), Is.GreaterThan(-0.5f), "only mildly negative");
            Assert.That(AdvisorBoard.JobBias(home, JobType.Soldier), Is.LessThanOrEqualTo(0f));
            Assert.That(AdvisorBoard.Urgency(home, DefenseAdvisor.Id), Is.LessThan(0.1f));
            Assert.That(far.IsAlive, Is.True);
        }

        [Test]
        public void Frontier_VotesMoreThanInterior()
        {
            var db = MakeDb();
            var front = Scenario(db, out var ctxF, out _);
            Neighbour(ctxF, RaceId.Dwarf, new Vec2i(36, 20), out _);
            var inner = Scenario(db, out var ctxI, out _);
            Neighbour(ctxI, RaceId.Dwarf, new Vec2i(85, 85), out _);

            TestWorlds.Run(new DefenseAdvisor(), ctxF, 24);
            TestWorlds.Run(new DefenseAdvisor(), ctxI, 24);

            Assert.That(front.Mem(DefenseAdvisorKeys.Frontier), Is.GreaterThan(0.9f));
            Assert.That(inner.Mem(DefenseAdvisorKeys.Frontier), Is.LessThan(0.1f));
            Assert.That(MaxMilitaryBias(front), Is.GreaterThan(MaxMilitaryBias(inner)));
            Assert.That(MaxMilitaryBias(front), Is.GreaterThan(0f));
            Assert.That(AdvisorBoard.Urgency(front, DefenseAdvisor.Id), Is.GreaterThan(AdvisorBoard.Urgency(inner, DefenseAdvisor.Id)));
            Assert.That(AdvisorBoard.JobBias(front, JobType.Soldier), Is.GreaterThanOrEqualTo(AdvisorBoard.JobBias(inner, JobType.Soldier)));
        }

        [Test]
        public void AggressiveNeighbour_IsMoreThreateningThanPeacefulNeighbour()
        {
            var db = MakeDb();
            var a = Scenario(db, out var ctxA, out _);
            Neighbour(ctxA, RaceId.Orc, new Vec2i(48, 20), out _);
            var b = Scenario(db, out var ctxB, out _);
            Neighbour(ctxB, RaceId.Elf, new Vec2i(48, 20), out _);

            TestWorlds.Run(new DefenseAdvisor(), ctxA, 24);
            TestWorlds.Run(new DefenseAdvisor(), ctxB, 24);

            Assert.That(a.Mem(DefenseAdvisorKeys.Threat), Is.GreaterThan(b.Mem(DefenseAdvisorKeys.Threat)));
            Assert.That(MaxMilitaryBias(a), Is.GreaterThan(MaxMilitaryBias(b)));
            Assert.That(AdvisorBoard.Urgency(a, DefenseAdvisor.Id), Is.GreaterThan(AdvisorBoard.Urgency(b, DefenseAdvisor.Id)));
        }

        [Test]
        public void AggressiveCivilization_VotesHarderThanPeacefulOne()
        {
            var db = MakeDb();
            var orcHome = Scenario(db, out var ctxO, out var orcs, RaceId.Orc);
            Neighbour(ctxO, RaceId.Dwarf, new Vec2i(50, 20), out var d1);
            ctxO.State.Diplomacy.Get(orcs.Id, d1.Id).Stance = DiplomaticStance.Hostile;
            var elfHome = Scenario(db, out var ctxE, out var elves, RaceId.Elf);
            Neighbour(ctxE, RaceId.Dwarf, new Vec2i(50, 20), out var d2);
            ctxE.State.Diplomacy.Get(elves.Id, d2.Id).Stance = DiplomaticStance.Hostile;

            TestWorlds.Run(new DefenseAdvisor(), ctxO, 12);
            TestWorlds.Run(new DefenseAdvisor(), ctxE, 12);

            Assert.That(MaxMilitaryBias(orcHome), Is.GreaterThan(MaxMilitaryBias(elfHome)));
            Assert.That(MaxMilitaryBias(elfHome), Is.GreaterThan(0f), "pacifists still respond to a hostile neighbour");
        }

        [Test]
        public void BesiegedSettlement_AlwaysGetsSoldierVotes()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            Neighbour(ctx, RaceId.Orc, new Vec2i(70, 20), out var orcs);
            DeclareWar(ctx, orcs, us);
            AddArmy(ctx, orcs, home, new Vec2i(home.Center.X + 2, home.Center.Y), 5f, ArmyState.Besieging);
            home.DefenseStrength = 500f; // strong walls: still keep the garrison manned

            TestWorlds.Run(new DefenseAdvisor(), ctx, 12);
            Assert.That(AdvisorBoard.JobBias(home, JobType.Soldier), Is.GreaterThan(0.3f), "soldier vote from the very first run");
            TestWorlds.Run(new DefenseAdvisor(), ctx, 12);
            Assert.That(AdvisorBoard.JobBias(home, JobType.Soldier), Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(AdvisorBoard.Urgency(home, DefenseAdvisor.Id), Is.GreaterThanOrEqualTo(0.9f));
        }

        [Test]
        public void SoldierVotes_FallAfterDangerPasses()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            Neighbour(ctx, RaceId.Orc, new Vec2i(80, 80), out var orcs);
            DeclareWar(ctx, orcs, us);
            var army = AddArmy(ctx, orcs, home, new Vec2i(home.Center.X + 10, home.Center.Y), 25f);
            var systems = new List<ISimSystem> { new AdvisorCouncilSystem(), new DefenseAdvisor() };

            TestWorlds.RunAll(systems, ctx, 36);
            float mobilised = AdvisorBoard.JobBias(home, JobType.Soldier);
            Assert.That(mobilised, Is.GreaterThan(0.5f));

            // the army leaves and peace is signed
            ctx.State.Diplomacy.Armies.Remove(army.Id);
            ctx.State.Diplomacy.Wars[0].IsActive = false;
            var rel = ctx.State.Diplomacy.Get(us.Id, orcs.Id);
            rel.WarId = -1; rel.Stance = DiplomaticStance.Neutral;

            for (int i = 0; i < systems.Count; i++) { ctx.DeltaTicks = systems[i].TickInterval; }
            for (int t = 0; t < 36; t++) Step(systems, ctx);
            float standingDown = AdvisorBoard.JobBias(home, JobType.Soldier);
            Assert.That(standingDown, Is.LessThan(mobilised), "votes fall once the danger passes");

            for (int t = 0; t < 240; t++) Step(systems, ctx);
            Assert.That(AdvisorBoard.JobBias(home, JobType.Soldier), Is.LessThanOrEqualTo(0f), "demobilised");
            Assert.That(home.Memory.ContainsKey(DefenseAdvisorKeys.ArmyDist), Is.False);
        }

        private static void Step(List<ISimSystem> systems, SimContext ctx)
        {
            ctx.State.Tick++;
            for (int i = 0; i < systems.Count; i++)
            {
                var s = systems[i];
                if (s.TickInterval > 1 && ctx.State.Tick % s.TickInterval != 0) continue;
                ctx.DeltaTicks = s.TickInterval;
                s.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
        }

        [Test]
        public void LostBattle_IsRememberedAndTriggersRebuilding()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            Neighbour(ctx, RaceId.Orc, new Vec2i(85, 85), out var orcs);
            var adv = new DefenseAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new BattleEvent { Position = home.Center, AttackerCivId = orcs.Id, DefenderCivId = us.Id, SettlementId = home.Id, AttackerLosses = 3, DefenderLosses = 8, AttackerWon = true });
            Assert.That(home.Memory.ContainsKey(DefenseAdvisorKeys.BattleTick), Is.True);
            Assert.AreEqual(8f, home.Mem(DefenseAdvisorKeys.BattleLosses), 1e-4f);

            TestWorlds.Run(adv, ctx, 24, false);
            Assert.That(MaxMilitaryBias(home), Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(AdvisorBoard.Urgency(home, DefenseAdvisor.Id), Is.GreaterThan(0.4f));
            Assert.That(AdvisorBoard.JobBias(home, JobType.Soldier), Is.GreaterThan(0f));

            // the memory fades and is cleaned up
            TestWorlds.Run(adv, ctx, 200, false);
            Assert.That(home.Memory.ContainsKey(DefenseAdvisorKeys.BattleTick), Is.False);
        }

        [Test]
        public void DawnAge_NeverVotesForUnavailableBuildings()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            us.Age = AgeId.Dawn;
            Neighbour(ctx, RaceId.Orc, new Vec2i(70, 20), out var orcs);
            DeclareWar(ctx, orcs, us);
            AddArmy(ctx, orcs, home, new Vec2i(home.Center.X + 12, home.Center.Y), 25f);

            TestWorlds.Run(new DefenseAdvisor(), ctx, 24);
            for (int i = 0; i < Military.Length; i++) Assert.AreEqual(0f, AdvisorBoard.BuildBias(home, Military[i]), Military[i].ToString());
            Assert.That(AdvisorBoard.JobBias(home, JobType.Soldier), Is.GreaterThan(0.3f));
        }

        [Test]
        public void ModernAge_PrefersTheModernLadder()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            us.Age = AgeId.Modern;
            home.Tier = SettlementTier.Town;
            Neighbour(ctx, RaceId.Orc, new Vec2i(60, 20), out var orcs);
            DeclareWar(ctx, orcs, us);
            orcs.MilitaryPower = 300f;

            TestWorlds.Run(new DefenseAdvisor(), ctx, 12);
            Assert.That(AdvisorBoard.BuildBias(home, BuildingType.MilitaryBase), Is.GreaterThan(AdvisorBoard.BuildBias(home, BuildingType.Barracks)));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(home, BuildingType.Palisade), "obsolete");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(home, BuildingType.Castle), "obsolete");
        }

        [Test]
        public void Deterministic_SameInputsSameMemory()
        {
            string a = RunDeterminism();
            string b = RunDeterminism();
            Assert.AreEqual(a, b);
        }

        private static string RunDeterminism()
        {
            var db = MakeDb();
            var home = Scenario(db, out var ctx, out var us);
            var second = ctx.Ops.FoundSettlement(us, new Vec2i(20, 60));
            Neighbour(ctx, RaceId.Orc, new Vec2i(60, 30), out var orcs);
            Neighbour(ctx, RaceId.Elf, new Vec2i(70, 70), out _);
            DeclareWar(ctx, orcs, us);
            var army = AddArmy(ctx, orcs, home, new Vec2i(50, 30), 30f);
            var adv = new DefenseAdvisor();
            adv.Initialize(ctx);
            var sb = new System.Text.StringBuilder();
            for (int step = 0; step < 6; step++)
            {
                army.Position = new Vec2i(army.Position.X - 4, army.Position.Y);
                TestWorlds.Run(adv, ctx, 12, false);
                Dump(sb, home);
                Dump(sb, second);
            }
            return sb.ToString();
        }

        private static void Dump(System.Text.StringBuilder sb, Settlement s)
        {
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                float v = s.Memory[keys[i]];
                Assert.That(float.IsNaN(v) || float.IsInfinity(v), Is.False, keys[i]);
                sb.Append(keys[i]).Append('=').Append(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            for (int i = 0; i < s.AiLog.Count; i++) sb.Append(s.AiLog[i]).Append('|');
            sb.Append('\n');
        }
    }
}
