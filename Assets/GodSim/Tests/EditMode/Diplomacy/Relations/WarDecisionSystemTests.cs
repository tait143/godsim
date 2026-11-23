using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Diplomacy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Relations
{
    public class WarDecisionSystemTests
    {
        private static GameDatabase WarDb(float orcAggression = 0.8f, float humanAggression = 0.1f, float elfAggression = 0.1f)
        {
            return RelationsTestKit.Db(r =>
            {
                RelationsTestKit.Hate(r, RaceId.Orc, RaceId.Human);
                RelationsTestKit.Hate(r, RaceId.Elf, RaceId.Human);
                r[(int)RaceId.Orc].Aggression = orcAggression;
                r[(int)RaceId.Human].Aggression = humanAggression;
                r[(int)RaceId.Elf].Aggression = elfAggression;
            });
        }

        // ------------------------------------------------------------------ declarations

        [Test]
        public void AggressiveStrongCiv_DeclaresWarOnHatedNeighbour()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);
            var declared = new List<WarDeclaredEvent>();
            ctx.Events.Subscribe<WarDeclaredEvent>(e => declared.Add(e));

            TestWorlds.RunAll(RelationsTestKit.AllSystems(), ctx, SimTime.TicksPerYear * 30);

            Assert.IsTrue(declared.Count > 0, "orcs should have declared war");
            Assert.AreEqual(orc.Id, declared[0].AttackerId);
            Assert.AreEqual(human.Id, declared[0].DefenderId);
            Assert.AreEqual(declared.Count, ctx.State.Stats.WarsDeclared);
            var war = ctx.State.Diplomacy.GetWar(declared[0].WarId);
            Assert.IsNotNull(war);
            Assert.IsTrue(war.CasusBelli == RelationsTuning.CasusConquest || war.CasusBelli == RelationsTuning.CasusOldGrudge, war.CasusBelli);
            Assert.IsTrue(RelationsTestKit.CountChronicle(ctx, "declared war") >= 1);
        }

        [Test]
        public void PeacefulCiv_DoesNotDeclareWar_EvenWhenHating()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(20, 20), AgeId.Bronze, 100f);
            RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);

            TestWorlds.RunAll(RelationsTestKit.AllSystems(), ctx, SimTime.TicksPerYear * 30);

            Assert.AreEqual(0, ctx.State.Diplomacy.Wars.Count);
            Assert.AreEqual(0, ctx.State.Stats.WarsDeclared);
        }

        [Test]
        public void DeclaringWar_SetsUpWarAndRelation()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Iron, 60f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(35, 20), AgeId.Bronze, 20f);
            var rel = RelationsTestKit.Contact(ctx, orc, human, -90f);
            rel.TradeAgreement = true;
            var stances = new List<StanceChangedEvent>();
            ctx.Events.Subscribe<StanceChangedEvent>(e => stances.Add(e));

            Assert.IsTrue(RelationsTestKit.RunUntil(new WarDecisionSystem(), ctx, () => rel.AtWar, SimTime.TicksPerYear * 40));

            var war = ctx.State.Diplomacy.GetWar(rel.WarId);
            Assert.IsNotNull(war);
            Assert.IsTrue(war.IsActive);
            Assert.AreEqual(orc.Id, war.AttackerId);
            Assert.AreEqual(human.Id, war.DefenderId);
            Assert.AreEqual(RelationsTuning.CasusBorderDispute, war.CasusBelli, "settlements 15 tiles apart");
            Assert.AreEqual(DiplomaticStance.War, rel.Stance);
            Assert.IsFalse(rel.TradeAgreement);
            Assert.AreEqual(DiplomaticStance.War, stances[0].NewStance);
            Assert.AreEqual(80f, ctx.State.Memory[RelationsMemoryKeys.WarInitialPower(war.Id)], 1e-3f);
        }

        [Test]
        public void HolyWar_WhenGodFavorSignsDiffer()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 60f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(70, 20), AgeId.Bronze, 20f);
            orc.GodFavor = -0.5f; human.GodFavor = 0.6f;
            var rel = RelationsTestKit.Contact(ctx, orc, human, -90f);

            Assert.IsTrue(RelationsTestKit.RunUntil(new WarDecisionSystem(), ctx, () => rel.AtWar, SimTime.TicksPerYear * 40));
            Assert.AreEqual(RelationsTuning.CasusHolyWar, ctx.State.Diplomacy.GetWar(rel.WarId).CasusBelli);
        }

        [Test]
        public void Opportunist_DeclaresOnlyWhenMuchStronger()
        {
            var db = RelationsTestKit.Db(r => r[(int)RaceId.Orc].Aggression = 0.9f);
            var ctx = RelationsTestKit.Ctx(db);
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 25f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(70, 20), AgeId.Bronze, 20f);
            var rel = RelationsTestKit.Contact(ctx, orc, human, -20f); // dislike, not hatred
            var sys = new WarDecisionSystem();

            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 20);
            Assert.IsFalse(rel.AtWar, "ratio 1.25 is not enough");

            orc.MilitaryPower = 60f; // ratio 3
            Assert.IsTrue(RelationsTestKit.RunUntil(sys, ctx, () => rel.AtWar, SimTime.TicksPerYear * 40, false));
            Assert.AreEqual(orc.Id, ctx.State.Diplomacy.GetWar(rel.WarId).AttackerId);
        }

        [Test]
        public void EnableWarfareFalse_PreventsWars()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            ctx.Config.EnableWarfare = false;
            RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);

            TestWorlds.RunAll(RelationsTestKit.AllSystems(), ctx, SimTime.TicksPerYear * 30);

            Assert.AreEqual(0, ctx.State.Diplomacy.Wars.Count);
            Assert.AreEqual(0, ctx.State.Stats.WarsDeclared);
        }

        [Test]
        public void EnableWarfareFalse_StillProcessesPeace()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            ctx.Config.EnableWarfare = false;
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var war = RelationsTestKit.StartWar(ctx, orc, human);
            war.WarScore = -80f;

            TestWorlds.Run(new WarDecisionSystem(), ctx, 48);

            Assert.IsFalse(war.IsActive);
        }

        [Test]
        public void DawnAgeCivs_DoNotDeclare()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Dawn, 100f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);
            RelationsTestKit.Contact(ctx, orc, human, -95f);

            TestWorlds.Run(new WarDecisionSystem(), ctx, SimTime.TicksPerYear * 20);

            Assert.AreEqual(0, ctx.State.Diplomacy.Wars.Count);
        }

        [Test]
        public void NoDeclaration_WithoutContact_OrAgainstAllies()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);
            var elf = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(20, 50), AgeId.Bronze, 20f);
            var noContact = ctx.State.Diplomacy.Get(orc.Id, human.Id);
            noContact.Opinion = -95f;
            RelationsTestKit.Contact(ctx, orc, elf, -95f, DiplomaticStance.Allied);

            TestWorlds.Run(new WarDecisionSystem(), ctx, SimTime.TicksPerYear * 20);

            Assert.AreEqual(0, ctx.State.Diplomacy.Wars.Count);
        }

        [Test]
        public void AggressorWithTwoWars_DoesNotStartAThird()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var h1 = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 20), AgeId.Bronze, 5f);
            var h2 = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(20, 60), AgeId.Bronze, 5f);
            var h3 = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 60), AgeId.Bronze, 5f);
            RelationsTestKit.StartWar(ctx, orc, h1, 0);
            RelationsTestKit.StartWar(ctx, orc, h2, 0);
            var third = RelationsTestKit.Contact(ctx, orc, h3, -95f);

            TestWorlds.Run(new WarDecisionSystem(), ctx, SimTime.TicksPerYear * 5);

            Assert.IsFalse(third.AtWar);
            Assert.AreEqual(2, DiplomacyQueries.ActiveWarCount(ctx.State, orc.Id));
        }

        // ------------------------------------------------------------------ truce

        [Test]
        public void Truce_PreventsRedeclaration_UntilItExpires()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);
            var rel = RelationsTestKit.Contact(ctx, orc, human, -90f, DiplomaticStance.Hostile);
            rel.TruceUntilTick = SimTime.YearsToTicks(30f);
            var sys = new WarDecisionSystem();

            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 25);
            Assert.AreEqual(0, ctx.State.Diplomacy.Wars.Count, "truce holds");

            Assert.IsTrue(RelationsTestKit.RunUntil(sys, ctx, () => ctx.State.Diplomacy.Wars.Count > 0, SimTime.TicksPerYear * 40, false),
                "war resumes after the truce");
            Assert.IsTrue(ctx.State.Diplomacy.Wars[0].StartTick >= SimTime.YearsToTicks(30f));
        }

        // ------------------------------------------------------------------ peace

        [Test]
        public void DecisiveScore_EndsWar_WithTributeAndTruce()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);
            var rel = RelationsTestKit.Contact(ctx, orc, human, -90f);
            var war = RelationsTestKit.StartWar(ctx, orc, human, 0);
            war.WarScore = 70f;
            RelationsTestKit.Capital(ctx, human).Stockpile.Add(ResourceType.Gold, 100f);
            var peace = new List<PeaceSignedEvent>();
            ctx.Events.Subscribe<PeaceSignedEvent>(e => peace.Add(e));
            var sys = new WarDecisionSystem();

            TestWorlds.Run(sys, ctx, 48);

            Assert.IsFalse(war.IsActive);
            Assert.AreEqual(48, war.EndTick);
            Assert.AreEqual(30f, RelationsTestKit.Capital(ctx, orc).Stockpile.Get(ResourceType.Gold), 1e-3f);
            Assert.AreEqual(70f, RelationsTestKit.Capital(ctx, human).Stockpile.Get(ResourceType.Gold), 1e-3f);
            Assert.AreEqual(-1, rel.WarId);
            Assert.AreEqual(DiplomaticStance.Hostile, rel.Stance);
            Assert.AreEqual(48 + SimTime.YearsToTicks(RelationsTuning.TruceYears), rel.TruceUntilTick); // 20 y since the 2026-11-19 balance pass (was 10)
            Assert.AreEqual(48L, DiplomacyQueries.LastWarEndTick(ctx.State, orc.Id, human.Id));
            Assert.IsFalse(ctx.State.Memory.ContainsKey(RelationsMemoryKeys.WarInitialPower(war.Id)));
            Assert.AreEqual(1, peace.Count);
            Assert.AreEqual(war.Id, peace[0].WarId);
            Assert.AreEqual(70f, peace[0].WarScore);
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "sued"));

            // the truce holds even though the orcs still hate the humans
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 9, false);
            Assert.AreEqual(1, ctx.State.Diplomacy.Wars.Count);
            Assert.IsFalse(rel.AtWar);
        }

        [Test]
        public void DecisiveDefenderVictory_PaysTributeToDefender()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var war = RelationsTestKit.StartWar(ctx, orc, human, 0);
            war.WarScore = -60f;
            RelationsTestKit.Capital(ctx, orc).Stockpile.Add(ResourceType.Gold, 50f);

            TestWorlds.Run(new WarDecisionSystem(), ctx, 48);

            Assert.IsFalse(war.IsActive);
            Assert.AreEqual(15f, RelationsTestKit.Capital(ctx, human).Stockpile.Get(ResourceType.Gold), 1e-3f);
            Assert.AreEqual(35f, RelationsTestKit.Capital(ctx, orc).Stockpile.Get(ResourceType.Gold), 1e-3f);
        }

        [Test]
        public void Exhaustion_EndsWar_WhenLossesExceedFortyPercentOfInitialPower()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 100f);
            var war = RelationsTestKit.StartWar(ctx, orc, human, 0);
            var sys = new WarDecisionSystem();

            TestWorlds.Run(sys, ctx, 48);
            Assert.AreEqual(200f, ctx.State.Memory[RelationsMemoryKeys.WarInitialPower(war.Id)], 1e-3f, "tracked on first sight");

            war.AttackerLosses = 40; war.DefenderLosses = 40; // exactly 40%: not yet
            orc.MilitaryPower = 5f; human.MilitaryPower = 5f;  // current power is irrelevant
            TestWorlds.Run(sys, ctx, 48, false);
            Assert.IsTrue(war.IsActive);

            war.DefenderLosses = 41;
            TestWorlds.Run(sys, ctx, 48, false);
            Assert.IsFalse(war.IsActive);
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "Exhausted"));
        }

        [Test]
        public void LongWar_AlwaysEnds_AfterTwentyFiveYears_AndNotByStalemateBeforeEight()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var war = RelationsTestKit.StartWar(ctx, orc, human, 0);
            war.WarScore = 40f; // neither stalemate (< 25) nor decisive (>= 60)
            var sys = new WarDecisionSystem();

            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 25);
            Assert.IsTrue(war.IsActive, "25 years is the cap, not before");
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear, false);
            Assert.IsFalse(war.IsActive);
            Assert.IsTrue(war.EndTick - war.StartTick <= SimTime.YearsToTicks(25f) + 48);
        }

        [Test]
        public void Stalemate_EndsWar_OnlyAfterEightYears()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var war = RelationsTestKit.StartWar(ctx, orc, human, 0);
            war.WarScore = 5f;
            var sys = new WarDecisionSystem();

            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 8);
            Assert.IsTrue(war.IsActive);
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 16, false);
            Assert.IsFalse(war.IsActive);
            Assert.IsTrue(war.EndTick > SimTime.YearsToTicks(8f));
            Assert.AreEqual(1, RelationsTestKit.CountChronicle(ctx, "stalemate"));
        }

        [Test]
        public void WarWithDeadOrSettlementlessCiv_Ends()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var elf = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(20, 50));
            var w1 = RelationsTestKit.StartWar(ctx, orc, human, 0);
            var w2 = RelationsTestKit.StartWar(ctx, elf, orc, 0);
            human.IsAlive = false;
            RelationsTestKit.Capital(ctx, elf).IsAlive = false; // elves lost their only settlement
            int peace = 0;
            ctx.Events.Subscribe<PeaceSignedEvent>(e => peace++);

            TestWorlds.Run(new WarDecisionSystem(), ctx, 48);

            Assert.IsFalse(w1.IsActive);
            Assert.IsFalse(w2.IsActive);
            Assert.AreEqual(2, peace);
            Assert.AreEqual(2, RelationsTestKit.CountChronicle(ctx, "ended with the fall"));
            Assert.IsFalse(ctx.State.Diplomacy.Get(orc.Id, human.Id).AtWar);
        }

        [Test]
        public void WarEndedByCollapse_IsRemembered()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var war = RelationsTestKit.StartWar(ctx, orc, human, 0);
            var sys = new WarDecisionSystem();
            TestWorlds.Run(sys, ctx, 48);
            ctx.Ops.CollapseCivilization(human, orc.Id);
            long endedAt = war.EndTick;

            TestWorlds.Run(sys, ctx, 48, false);

            Assert.AreEqual(endedAt, DiplomacyQueries.LastWarEndTick(ctx.State, orc.Id, human.Id));
            Assert.IsFalse(ctx.State.Memory.ContainsKey(RelationsMemoryKeys.WarInitialPower(war.Id)));
        }

        // ------------------------------------------------------------------ allies

        [Test]
        public void AlliesOfTheDefender_JoinTheWar()
        {
            var ctx = RelationsTestKit.Ctx(WarDb(orcAggression: 1f, elfAggression: 0f));
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20), AgeId.Bronze, 100f);
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20), AgeId.Bronze, 20f);
            var elf = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(50, 60), AgeId.Bronze, 30f);
            var dwarf = RelationsTestKit.Civ(ctx, RaceId.Dwarf, new Vec2i(110, 110), AgeId.Bronze, 30f);
            RelationsTestKit.Contact(ctx, orc, human, -90f);
            RelationsTestKit.Contact(ctx, human, elf, 90f, DiplomaticStance.Allied);
            RelationsTestKit.Contact(ctx, human, dwarf, 90f, DiplomaticStance.Allied);
            var orcElf = RelationsTestKit.Contact(ctx, orc, elf, 0f);
            // dwarves never met the orcs: they cannot join
            var declared = new List<WarDeclaredEvent>();
            ctx.Events.Subscribe<WarDeclaredEvent>(e => declared.Add(e));

            Assert.IsTrue(RelationsTestKit.RunUntil(new WarDecisionSystem(), ctx, () => declared.Count > 0, SimTime.TicksPerYear * 40));

            Assert.AreEqual(2, declared.Count);
            Assert.AreEqual(orc.Id, declared[0].AttackerId);
            Assert.AreEqual(human.Id, declared[0].DefenderId);
            Assert.AreEqual(elf.Id, declared[1].AttackerId);
            Assert.AreEqual(orc.Id, declared[1].DefenderId);
            Assert.IsTrue(orcElf.AtWar);
            Assert.AreEqual(RelationsTuning.CasusCallToArms, ctx.State.Diplomacy.GetWar(orcElf.WarId).CasusBelli);
            Assert.IsFalse(ctx.State.Diplomacy.TryGet(orc.Id, dwarf.Id, out var od) && od.AtWar);
            Assert.AreEqual(2, ctx.State.Stats.WarsDeclared);
        }

        // ------------------------------------------------------------------ maintenance

        [Test]
        public void InactiveWars_ArePrunedToTheMostRecent200()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var wars = ctx.State.Diplomacy.Wars;
            for (int i = 0; i < 210; i++)
                wars.Add(new War { Id = ctx.State.Ids.War(), AttackerId = orc.Id, DefenderId = human.Id, StartTick = 0, EndTick = 1000 - i, IsActive = false });
            var active = RelationsTestKit.StartWar(ctx, orc, human, 0);

            TestWorlds.Run(new WarDecisionSystem(), ctx, 48);

            int inactive = 0;
            long minEnd = long.MaxValue;
            foreach (var w in wars) if (!w.IsActive) { inactive++; if (w.EndTick < minEnd) minEnd = w.EndTick; }
            Assert.AreEqual(200, inactive);
            Assert.AreEqual(1000 - 199, minEnd, "the oldest (smallest EndTick) were removed");
            Assert.IsTrue(wars.Contains(active));
        }

        [Test]
        public void DanglingWarReference_IsRepaired()
        {
            var ctx = RelationsTestKit.Ctx(WarDb());
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(20, 20));
            var human = RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(50, 20));
            var rel = RelationsTestKit.Contact(ctx, orc, human, 0f, DiplomaticStance.War);
            rel.WarId = 999;

            TestWorlds.Run(new WarDecisionSystem(), ctx, 48);

            Assert.AreEqual(-1, rel.WarId);
            Assert.AreEqual(DiplomaticStance.Hostile, rel.Stance);
        }

        [Test]
        public void LongRun_WarsHappenSometimes_AndAlwaysEnd()
        {
            var db = RelationsTestKit.Db(r =>
            {
                RelationsTestKit.Hate(r, RaceId.Orc, RaceId.Human);
                RelationsTestKit.Hate(r, RaceId.Orc, RaceId.Elf);
                r[(int)RaceId.Orc].Aggression = 0.8f;
            });
            var ctx = RelationsTestKit.Ctx(db, 99);
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(30, 30), AgeId.Bronze, 50f);
            RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(60, 30), AgeId.Bronze, 40f);
            RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(30, 60), AgeId.Bronze, 40f);
            RelationsTestKit.Civ(ctx, RaceId.Dwarf, new Vec2i(60, 60), AgeId.Bronze, 40f);

            int years = 150;
            TestWorlds.RunAll(RelationsTestKit.AllSystems(), ctx, SimTime.TicksPerYear * years);

            var wars = ctx.State.Diplomacy.Wars;
            Assert.IsTrue(wars.Count >= 1, "some war in 150 years");
            Assert.IsTrue(wars.Count <= 30, "not constant war: " + wars.Count);
            long cap = SimTime.YearsToTicks(25f) + 48;
            foreach (var w in wars)
            {
                long end = w.IsActive ? ctx.State.Tick : w.EndTick;
                Assert.IsTrue(end - w.StartTick <= cap, "war " + w.Id + " lasted too long");
            }
            Assert.IsTrue(DiplomacyQueries.ActiveWarCount(ctx.State, orc.Id) <= 2);
        }
    }
}
