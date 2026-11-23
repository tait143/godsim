using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Envoys
{
    public class EnvoySystemTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = TestDb.CreateStub(); }

        // ------------------------------------------------------------------ helpers

        private SimContext NewContext(ulong seed = 7) => TestWorlds.CreateContext(_db, 96, seed);

        private static Civilization AddCiv(SimContext ctx, RaceId race, int x, int y, float military, float gold, string leader = null)
        {
            var s = TestWorlds.AddSettlement(ctx, race, new Vec2i(x, y), 2, out var civ);
            civ.MilitaryPower = military;
            s.Stockpile.SetRaw(ResourceType.Gold, gold);
            s.Stockpile.SetRaw(ResourceType.Food, 100f);
            if (leader != null)
            {
                var p = ctx.State.People.Get(s.ResidentIds[0]);
                p.Name = leader;
                civ.LeaderId = p.Id;
            }
            return civ;
        }

        private static Relation Link(SimContext ctx, Civilization a, Civilization b, float opinion, DiplomaticStance stance = DiplomaticStance.Neutral)
        {
            var r = ctx.State.Diplomacy.Get(a.Id, b.Id);
            r.HasContact = true;
            r.Opinion = opinion;
            r.Stance = stance;
            return r;
        }

        private static float Gold(SimContext ctx, Civilization c) => EnvoyUtil.Capital(ctx.State, c).Stockpile.Get(ResourceType.Gold);

        private static float Opinion(SimContext ctx, Civilization a, Civilization b) => ctx.State.Diplomacy.Get(a.Id, b.Id).Opinion;

        private static int ChronicleCount(SimContext ctx, string fragment)
        {
            int n = 0;
            var e = ctx.State.Chronicle.Entries;
            for (int i = 0; i < e.Count; i++) if (e[i].Text != null && e[i].Text.Contains(fragment)) n++;
            return n;
        }

        private static void SetPersonality(Civilization c, float diplomacy, float mercantilism, float aggression, float xenophobia)
        {
            AdvisorBoard.SetTrait(c, AITrait.Diplomacy, diplomacy);
            AdvisorBoard.SetTrait(c, AITrait.Mercantilism, mercantilism);
            AdvisorBoard.SetTrait(c, AITrait.Aggression, aggression);
            AdvisorBoard.SetTrait(c, AITrait.Xenophobia, xenophobia);
        }

        // ------------------------------------------------------------------ gifts

        [Test]
        public void Gift_MovesGoldCapitalToCapital_AndRaisesOpinion()
        {
            var ctx = NewContext();
            var dwarves = AddCiv(ctx, RaceId.Dwarf, 10, 10, 20f, 140f);
            var humans = AddCiv(ctx, RaceId.Human, 40, 10, 20f, 10f);
            SetPersonality(dwarves, 0.9f, 0.9f, 0.2f, 0.1f);
            Link(ctx, dwarves, humans, 5f);
            ctx.State.Tick = 1000;

            float moved = new GiftEnvoy().TryGive(ctx, dwarves);

            Assert.AreEqual(14f, moved, 1e-3f, "10% of the 140 gold stock");
            Assert.AreEqual(126f, Gold(ctx, dwarves), 1e-3f);
            Assert.AreEqual(24f, Gold(ctx, humans), 1e-3f);
            float gain = Opinion(ctx, dwarves, humans) - 5f;
            Assert.That(gain, Is.InRange(8f, 20f));
            Assert.AreEqual(1, ChronicleCount(ctx, "sent a chest of gold to the Human court"));
        }

        [Test]
        public void Gift_IsRateLimited_AndPersonalityGated()
        {
            var ctx = NewContext();
            var giver = AddCiv(ctx, RaceId.Human, 10, 10, 20f, 140f);
            var other = AddCiv(ctx, RaceId.Elf, 40, 10, 20f, 10f);
            Link(ctx, giver, other, 0f);
            ctx.State.Tick = 1000;
            var gifts = new GiftEnvoy();

            SetPersonality(giver, 0.1f, 0.1f, 0.9f, 0.5f);
            Assert.AreEqual(0f, gifts.TryGive(ctx, giver), "warlike miser does not give");

            SetPersonality(giver, 0.9f, 0.9f, 0.1f, 0.1f);
            Assert.That(gifts.TryGive(ctx, giver), Is.GreaterThan(0f));
            Assert.AreEqual(0f, gifts.TryGive(ctx, giver), "cooldown");
            ctx.State.Tick += SimTime.YearsToTicks(EnvoyTuning.GiftCooldownYears);
            Assert.That(gifts.TryGive(ctx, giver), Is.GreaterThan(0f));
        }

        [Test]
        public void Gift_PoorCivDoesNotGive()
        {
            var ctx = NewContext();
            var giver = AddCiv(ctx, RaceId.Human, 10, 10, 20f, 30f);
            var other = AddCiv(ctx, RaceId.Elf, 40, 10, 20f, 10f);
            SetPersonality(giver, 1f, 1f, 0f, 0f);
            Link(ctx, giver, other, 0f);
            Assert.AreEqual(0f, new GiftEnvoy().TryGive(ctx, giver));
            Assert.AreEqual(30f, Gold(ctx, giver), 1e-4f);
        }

        // ------------------------------------------------------------------ tribute

        [Test]
        public void Tribute_WeakCivPays_ExtendsTruce_AndGainsOpinion()
        {
            var ctx = NewContext();
            var humans = AddCiv(ctx, RaceId.Human, 10, 10, 10f, 100f);
            var orcs = AddCiv(ctx, RaceId.Orc, 40, 10, 50f, 0f);
            SetPersonality(orcs, 0.2f, 0.3f, 0.9f, 0.5f);
            SetPersonality(humans, 0.6f, 0.6f, 0.3f, 0.2f);
            var r = Link(ctx, humans, orcs, -20f, DiplomaticStance.Neutral);
            ctx.State.Tick = 2000;
            r.TruceUntilTick = 2010;

            var outcome = new TributeEnvoy().TryPay(ctx, humans);

            Assert.AreEqual(TributeOutcome.Paid, outcome);
            Assert.AreEqual(2000 + SimTime.YearsToTicks(EnvoyTuning.TributeTruceYears), r.TruceUntilTick, "truce = max(current, tick + 3 years)");
            Assert.AreEqual(85f, Gold(ctx, humans), 1e-3f);
            Assert.AreEqual(15f, Gold(ctx, orcs), 1e-3f);
            Assert.That(r.Opinion, Is.GreaterThan(-20f + EnvoyTuning.TributeMinImpulse - 0.01f));
            Assert.AreEqual(orcs.Id, (int)humans.Mem(EnvoyKeys.TributeTo, -1f));
            Assert.AreEqual(1, ChronicleCount(ctx, "paid a tribute of 15 gold"));
            // While the truce holds, a war roll that respects TruceUntilTick cannot fire; no second payment either.
            Assert.That(r.TruceUntilTick, Is.GreaterThan(ctx.State.Tick));
            Assert.AreEqual(TributeOutcome.None, new TributeEnvoy().TryPay(ctx, humans));
        }

        [Test]
        public void Tribute_LongerTruceIsKept()
        {
            var ctx = NewContext();
            var humans = AddCiv(ctx, RaceId.Human, 10, 10, 10f, 100f);
            var orcs = AddCiv(ctx, RaceId.Orc, 40, 10, 50f, 0f);
            var r = Link(ctx, humans, orcs, -20f);
            ctx.State.Tick = 2000;
            r.TruceUntilTick = 99999;
            Assert.That(new TributeEnvoy().Pay(ctx, humans, orcs), Is.GreaterThan(0f));
            Assert.AreEqual(99999, r.TruceUntilTick);
        }

        [Test]
        public void Tribute_PaysInFood_WhenVaultIsEmpty()
        {
            var ctx = NewContext();
            var humans = AddCiv(ctx, RaceId.Human, 10, 10, 10f, 0f);
            var orcs = AddCiv(ctx, RaceId.Orc, 40, 10, 50f, 0f);
            Link(ctx, humans, orcs, -20f);
            Assert.That(new TributeEnvoy().Pay(ctx, humans, orcs), Is.GreaterThan(0f));
            Assert.AreEqual(85f, EnvoyUtil.Capital(ctx.State, humans).Stockpile.Get(ResourceType.Food), 1e-3f);
        }

        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        public void Tribute_ProudRacesRefuse_UnlessDesperate(RaceId proud)
        {
            var ctx = NewContext();
            var weak = AddCiv(ctx, proud, 10, 10, 20f, 100f);
            var bully = AddCiv(ctx, RaceId.Human, 40, 10, 50f, 0f);
            SetPersonality(bully, 0.3f, 0.3f, 0.9f, 0.5f);
            SetPersonality(weak, 0.6f, 0.6f, 0.3f, 0.2f);
            var r = Link(ctx, weak, bully, -20f);
            ctx.State.Tick = 2000;
            var tribute = new TributeEnvoy();

            Assert.AreEqual(TributeOutcome.Refused, tribute.TryPay(ctx, weak));
            Assert.AreEqual(100f, Gold(ctx, weak), 1e-4f);
            Assert.AreEqual(-1, r.TruceUntilTick);
            Assert.AreEqual(1, ChronicleCount(ctx, "refused to bow"));

            // Desperate: outmatched 5:1 they swallow their pride (after the refusal cooldown).
            bully.MilitaryPower = 100f;
            ctx.State.Tick += SimTime.YearsToTicks(EnvoyTuning.TributeCooldownYears);
            Assert.AreEqual(TributeOutcome.Paid, tribute.TryPay(ctx, weak));
            Assert.That(r.TruceUntilTick, Is.GreaterThan(ctx.State.Tick));
        }

        [Test]
        public void Tribute_NotPaidToPeacefulOrWeakNeighbour()
        {
            var ctx = NewContext();
            var humans = AddCiv(ctx, RaceId.Human, 10, 10, 10f, 100f);
            var elves = AddCiv(ctx, RaceId.Elf, 40, 10, 50f, 0f);
            SetPersonality(elves, 0.8f, 0.5f, 0.1f, 0.6f);
            Link(ctx, humans, elves, -20f);
            Assert.AreEqual(TributeOutcome.None, new TributeEnvoy().TryPay(ctx, humans), "peaceful giant is not feared");
            AdvisorBoard.SetTrait(elves, AITrait.Aggression, 0.9f);
            elves.MilitaryPower = 15f;
            Assert.AreEqual(TributeOutcome.None, new TributeEnvoy().TryPay(ctx, humans), "not much stronger");
        }

        // ------------------------------------------------------------------ coalitions

        [Test]
        public void Coalition_FormsAgainstHegemon_ChroniclesOnce()
        {
            var ctx = NewContext();
            var orcs = AddCiv(ctx, RaceId.Orc, 10, 10, 100f, 0f);
            var humans = AddCiv(ctx, RaceId.Human, 40, 10, 30f, 0f);
            var elves = AddCiv(ctx, RaceId.Elf, 10, 40, 25f, 0f);
            var dwarves = AddCiv(ctx, RaceId.Dwarf, 40, 40, 20f, 0f);
            Link(ctx, orcs, humans, -10f);
            Link(ctx, orcs, elves, -10f);
            Link(ctx, orcs, dwarves, -10f);
            Link(ctx, humans, elves, 0f);
            Link(ctx, humans, dwarves, 0f);
            Link(ctx, elves, dwarves, 0f);

            Assert.AreEqual(orcs.Id, CoalitionEnvoy.FindHegemon(ctx.State, EnvoyTuning.HegemonRatio));
            var coalition = new CoalitionEnvoy();
            Assert.AreEqual(orcs.Id, coalition.Run(ctx));
            Assert.AreEqual(3, coalition.Members.Count);
            ctx.State.Tick += 48;
            Assert.AreEqual(orcs.Id, coalition.Run(ctx));

            Assert.AreEqual(orcs.Id, (int)humans.Mem(EnvoyKeys.Coalition, -1f));
            Assert.AreEqual(orcs.Id, (int)elves.Mem(EnvoyKeys.Coalition, -1f));
            Assert.AreEqual(orcs.Id, (int)dwarves.Mem(EnvoyKeys.Coalition, -1f));
            Assert.IsFalse(orcs.Memory.ContainsKey(EnvoyKeys.Coalition));
            Assert.AreEqual(2f * EnvoyTuning.CoalitionImpulse, Opinion(ctx, humans, elves), 1e-3f);
            Assert.AreEqual(2f * EnvoyTuning.CoalitionImpulse, Opinion(ctx, elves, dwarves), 1e-3f);
            Assert.That(Opinion(ctx, humans, orcs), Is.LessThan(-10f));
            Assert.AreEqual(1, ChronicleCount(ctx, "grand coalition"));

            // The hegemon is humbled: the coalition dissolves.
            orcs.MilitaryPower = 30f;
            ctx.State.Tick += 48;
            Assert.AreEqual(-1, coalition.Run(ctx));
            Assert.IsFalse(humans.Memory.ContainsKey(EnvoyKeys.Coalition));
            Assert.AreEqual(1, ChronicleCount(ctx, "coalition against them dissolved"));
        }

        [Test]
        public void Coalition_NoHegemon_NoCoalition()
        {
            var ctx = NewContext();
            var a = AddCiv(ctx, RaceId.Orc, 10, 10, 40f, 0f);
            var b = AddCiv(ctx, RaceId.Human, 40, 10, 30f, 0f);
            var c = AddCiv(ctx, RaceId.Elf, 10, 40, 35f, 0f);
            Link(ctx, a, b, 0f); Link(ctx, a, c, 0f); Link(ctx, b, c, 0f);
            Assert.AreEqual(-1, new CoalitionEnvoy().Run(ctx));
            Assert.AreEqual(0f, Opinion(ctx, b, c), 1e-4f);
        }

        // ------------------------------------------------------------------ pacts & betrayal

        [Test]
        public void Pact_ExtendsTruceTenYears_ForFriendsWithoutGrievance()
        {
            var ctx = NewContext();
            var humans = AddCiv(ctx, RaceId.Human, 10, 10, 20f, 0f);
            var elves = AddCiv(ctx, RaceId.Elf, 40, 10, 20f, 0f);
            var r = Link(ctx, humans, elves, 50f, DiplomaticStance.Friendly);
            ctx.State.Tick = 500;
            var pacts = new PactEnvoy();
            Assert.IsTrue(pacts.Sign(ctx, humans, elves));
            Assert.AreEqual(500 + SimTime.YearsToTicks(EnvoyTuning.PactYears), r.TruceUntilTick);
            Assert.IsTrue(pacts.HasPact(ctx.State, humans.Id, elves.Id, ctx.State.Tick));
            Assert.AreEqual(1, ChronicleCount(ctx, "pact of non-aggression"));
            Assert.IsFalse(pacts.Sign(ctx, humans, elves), "not renewed while far from expiry");

            // A recent war is a grievance.
            var ctx2 = NewContext();
            var h2 = AddCiv(ctx2, RaceId.Human, 10, 10, 20f, 0f);
            var e2 = AddCiv(ctx2, RaceId.Elf, 40, 10, 20f, 0f);
            Link(ctx2, h2, e2, 50f, DiplomaticStance.Friendly);
            ctx2.State.Tick = 500;
            ctx2.State.Memory[EnvoyKeys.LastWar(h2.Id, e2.Id)] = 400;
            Assert.IsFalse(new PactEnvoy().Sign(ctx2, h2, e2));
        }

        [Test]
        public void PactBetrayal_PenalizesBetrayer_WithVictimAndAllFriends()
        {
            var ctx = NewContext();
            var orcs = AddCiv(ctx, RaceId.Orc, 10, 10, 50f, 0f);
            var elves = AddCiv(ctx, RaceId.Elf, 40, 10, 20f, 0f);
            var dwarves = AddCiv(ctx, RaceId.Dwarf, 10, 40, 20f, 0f);
            var saurians = AddCiv(ctx, RaceId.Saurian, 40, 40, 20f, 0f);
            SetPersonality(orcs, 0.6f, 0.5f, 0.5f, 0.3f);
            Link(ctx, orcs, elves, 50f, DiplomaticStance.Friendly);
            Link(ctx, elves, dwarves, 40f, DiplomaticStance.Friendly);   // friend of the victim
            Link(ctx, orcs, dwarves, 20f);
            Link(ctx, orcs, saurians, 20f);                               // bystander, not friends with the elves
            Link(ctx, elves, saurians, 0f);

            var system = new EnvoySystem();
            system.Initialize(ctx);
            ctx.State.Tick = 1000;
            Assert.IsTrue(system.Pacts.Sign(ctx, orcs, elves));
            float pactOpinion = Opinion(ctx, orcs, elves);

            ctx.State.Tick = 1200;
            ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = orcs.Id, DefenderId = elves.Id });

            Assert.IsTrue(ctx.State.Memory.ContainsKey(EnvoyKeys.Betrayal(orcs.Id, elves.Id)));
            Assert.IsFalse(system.Pacts.HasPact(ctx.State, orcs.Id, elves.Id, ctx.State.Tick));
            Assert.AreEqual(pactOpinion + EnvoyTuning.BetrayalShock, Opinion(ctx, orcs, elves), 1e-3f);
            Assert.AreEqual(20f + EnvoyTuning.BetrayalFriendShock, Opinion(ctx, orcs, dwarves), 1e-3f);
            Assert.AreEqual(20f, Opinion(ctx, orcs, saurians), 1e-4f);
            Assert.AreEqual(1, ChronicleCount(ctx, "broke their sacred pact"));

            // The grudge lasts: decades later the betrayer is still punished by the victim and its friends.
            float od = Opinion(ctx, orcs, dwarves);
            ctx.State.Tick = 1200 + SimTime.YearsToTicks(30f);
            system.Pacts.ApplyGrudges(ctx);
            Assert.That(Opinion(ctx, orcs, dwarves), Is.LessThan(od));
            Assert.AreEqual(20f, Opinion(ctx, orcs, saurians), 1e-4f);
            Assert.IsTrue(system.Pacts.HasGrievance(ctx.State, elves.Id, orcs.Id, ctx.State.Tick));
            Assert.IsFalse(system.Pacts.Sign(ctx, orcs, elves));

            // ... and is finally forgotten.
            ctx.State.Tick = 1200 + SimTime.YearsToTicks(EnvoyTuning.BetrayalYears) + 1;
            system.Pacts.ApplyGrudges(ctx);
            Assert.IsFalse(ctx.State.Memory.ContainsKey(EnvoyKeys.Betrayal(orcs.Id, elves.Id)));
        }

        [Test]
        public void WarWithoutPact_IsNotBetrayal()
        {
            var ctx = NewContext();
            var a = AddCiv(ctx, RaceId.Orc, 10, 10, 50f, 0f);
            var b = AddCiv(ctx, RaceId.Elf, 40, 10, 20f, 0f);
            Link(ctx, a, b, -50f, DiplomaticStance.Hostile);
            var system = new EnvoySystem();
            system.Initialize(ctx);
            ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = a.Id, DefenderId = b.Id });
            Assert.IsFalse(ctx.State.Memory.ContainsKey(EnvoyKeys.Betrayal(a.Id, b.Id)));
            Assert.AreEqual(-50f, Opinion(ctx, a, b), 1e-4f);
        }

        // ------------------------------------------------------------------ royal marriages

        [Test]
        public void RoyalMarriage_SameRaceFriends_OncePerPair_NamesLeaders()
        {
            var ctx = NewContext();
            var a = AddCiv(ctx, RaceId.Human, 10, 10, 20f, 0f, "Aldric");
            var b = AddCiv(ctx, RaceId.Human, 40, 10, 20f, 0f, "Berenice");
            var r = Link(ctx, a, b, 40f, DiplomaticStance.Friendly);
            var marriages = new MarriageEnvoy();
            Assert.IsTrue(marriages.Marry(ctx, a, b));
            Assert.AreEqual(40f + EnvoyTuning.MarriageImpulse, r.Opinion, 1e-3f);
            Assert.AreEqual(1, ChronicleCount(ctx, "Aldric"));
            Assert.AreEqual(1, ChronicleCount(ctx, "Berenice"));
            Assert.IsFalse(marriages.Marry(ctx, a, b), "once per pair");
        }

        [Test]
        public void RoyalMarriage_RequiresAffinityAcrossRaces()
        {
            var ctx = NewContext();
            var a = AddCiv(ctx, RaceId.Human, 10, 10, 20f, 0f, "Aldric");
            var b = AddCiv(ctx, RaceId.Elf, 40, 10, 20f, 0f, "Ilyra");
            Link(ctx, a, b, 40f, DiplomaticStance.Friendly);
            Assert.IsFalse(new MarriageEnvoy().Marry(ctx, a, b), "stub races have 0 affinity");
        }

        // ------------------------------------------------------------------ system

        private static string RunWorld(GameDatabase db, ulong seed, int years)
        {
            var ctx = TestWorlds.CreateContext(db, 96, seed);
            var humans = AddCiv(ctx, RaceId.Human, 10, 10, 20f, 140f, "Aldric");
            var humans2 = AddCiv(ctx, RaceId.Human, 40, 10, 15f, 140f, "Berenice");
            var orcs = AddCiv(ctx, RaceId.Orc, 10, 40, 90f, 60f);
            var elves = AddCiv(ctx, RaceId.Elf, 40, 40, 10f, 140f);
            SetPersonality(humans, 0.9f, 0.9f, 0.2f, 0.1f);
            SetPersonality(humans2, 0.9f, 0.8f, 0.2f, 0.1f);
            SetPersonality(orcs, 0.2f, 0.3f, 0.9f, 0.6f);
            SetPersonality(elves, 0.7f, 0.6f, 0.1f, 0.4f);
            Link(ctx, humans, humans2, 30f, DiplomaticStance.Friendly);
            Link(ctx, humans, orcs, -15f);
            Link(ctx, humans, elves, 5f);
            Link(ctx, humans2, orcs, -15f);
            Link(ctx, humans2, elves, 5f);
            Link(ctx, orcs, elves, -30f, DiplomaticStance.Hostile);

            var system = new EnvoySystem();
            TestWorlds.Run(system, ctx, years * SimTime.TicksPerYear);

            var sb = new StringBuilder();
            var entries = ctx.State.Chronicle.Entries;
            for (int i = 0; i < entries.Count; i++) sb.Append(entries[i].Tick).Append(':').Append(entries[i].Text).Append('\n');
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                sb.Append(civs[i].Id).Append(" gold=").Append(Gold(ctx, civs[i]).ToString("R"));
                for (int j = i + 1; j < civs.Count; j++)
                {
                    var r = ctx.State.Diplomacy.Get(civs[i].Id, civs[j].Id);
                    Assert.IsFalse(float.IsNaN(r.Opinion) || float.IsInfinity(r.Opinion));
                    Assert.That(r.Opinion, Is.InRange(-100f, 100f));
                    sb.Append(" o").Append(j).Append('=').Append(r.Opinion.ToString("R")).Append(" t=").Append(r.TruceUntilTick);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        [Test]
        public void System_IsDeterministic_AndTellsStories()
        {
            string a = RunWorld(_db, 42, 12);
            string b = RunWorld(_db, 42, 12);
            Assert.AreEqual(a, b);
            TestContext.WriteLine(a);
            StringAssert.Contains("grand coalition", a);
            StringAssert.Contains("pact of non-aggression", a);
            StringAssert.Contains("tribute", a);
        }

        [Test]
        public void System_HasContractShape_AndSurvivesEmptyWorld()
        {
            var system = new EnvoySystem();
            Assert.AreEqual("AI.Envoys", system.Name);
            Assert.AreEqual(48, system.TickInterval);
            var ctx = NewContext();
            Assert.DoesNotThrow(() => TestWorlds.Run(system, ctx, 200));
            Assert.DoesNotThrow(() => system.Tick(null));
        }
    }
}
