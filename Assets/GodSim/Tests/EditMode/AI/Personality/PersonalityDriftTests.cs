using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Personality
{
    public class PersonalityDriftTests
    {
        private const float Eps = 1e-4f;

        private static GameDatabase MakeDb()
        {
            var db = TestDb.CreateStub(false);
            var elf = db.Races[(int)RaceId.Elf];
            elf.PluralName = "Elves"; elf.Aggression = 0.2f; elf.Xenophobia = 0.75f; elf.Piety = 0.85f; elf.Diplomacy = 0.5f; elf.Curiosity = 0.6f;
            var orc = db.Races[(int)RaceId.Orc];
            orc.PluralName = "Orcs"; orc.Aggression = 0.6f; orc.Xenophobia = 0.6f; orc.Piety = 0.2f; orc.Diplomacy = 0.2f; orc.Expansionism = 0.8f;
            db.Freeze();
            return db;
        }

        private static void RunOnce(PersonalityDriftSystem sys, SimContext ctx)
        {
            ctx.State.Tick += sys.TickInterval;
            ctx.DeltaTicks = sys.TickInterval;
            sys.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        private static PersonalityDriftSystem Start(SimContext ctx)
        {
            var sys = new PersonalityDriftSystem();
            ctx.DeltaTicks = sys.TickInterval;
            sys.Initialize(ctx);
            return sys;
        }

        private static int PersonalityChronicles(SimContext ctx, int civId)
        {
            int n = 0;
            var e = ctx.State.Chronicle.Entries;
            for (int i = 0; i < e.Count; i++)
                if (e[i].CivilizationId == civId && e[i].Category == ChronicleCategory.Civilization && e[i].Importance == 1) n++;
            return n;
        }

        [Test]
        public void TraitsInitializeToRaceDefaults()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 0, out var elf);
            var sys = Start(ctx);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(50, 50), 0, out var orc); // founded after Initialize
            for (int t = 0; t < PersonalityKeys.TraitCount; t++)
            {
                var trait = (AITrait)t;
                Assert.IsTrue(elf.Memory.ContainsKey(AdvisorBoard.TraitKey(trait)), "elf " + trait);
                Assert.IsTrue(orc.Memory.ContainsKey(AdvisorBoard.TraitKey(trait)), "orc " + trait);
                Assert.AreEqual(AdvisorBoard.RaceTrait(db, RaceId.Elf, trait), AdvisorBoard.Trait(elf, db, trait), Eps);
                Assert.AreEqual(AdvisorBoard.RaceTrait(db, RaceId.Orc, trait), AdvisorBoard.Trait(orc, db, trait), Eps);
            }
            Assert.AreEqual(0.6f, AdvisorBoard.Trait(orc, db, AITrait.Aggression), Eps);
            Assert.NotNull(sys);
        }

        [Test]
        public void RepeatedVictoriesRaiseAggressionWithinBounds()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var sa = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(10, 10), 0, out var orc);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(50, 50), 0, out var elf);
            var sys = Start(ctx);
            float baseAggr = AdvisorBoard.RaceTrait(db, RaceId.Orc, AITrait.Aggression);
            for (int run = 0; run < 60; run++)
            {
                ctx.Events.Publish(new BattleEvent { AttackerCivId = orc.Id, DefenderCivId = elf.Id, SettlementId = sa.Id, AttackerWon = true, Position = new Vec2i(30, 30) });
                RunOnce(sys, ctx);
                float a = AdvisorBoard.Trait(orc, db, AITrait.Aggression);
                Assert.LessOrEqual(a, baseAggr + PersonalityModel.MaxDeviation + Eps);
                Assert.IsFalse(float.IsNaN(a));
            }
            Assert.Greater(AdvisorBoard.Trait(orc, db, AITrait.Aggression), baseAggr + 0.15f);

            // Saturating every warlike experience still respects the ±0.35 bound.
            orc.Memory[PersonalityKeys.ExperienceKey(PersonalityCause.Victory)] = 1e6f;
            orc.Memory[PersonalityKeys.ExperienceKey(PersonalityCause.Defeat)] = 1e6f;
            orc.Memory[PersonalityKeys.ExperienceKey(PersonalityCause.Invaded)] = float.NaN;
            for (int run = 0; run < 80; run++) RunOnce(sys, ctx);
            Assert.AreEqual(baseAggr + PersonalityModel.MaxDeviation, AdvisorBoard.Trait(orc, db, AITrait.Aggression), 2e-3f);
            Assert.IsFalse(float.IsNaN(orc.Mem(PersonalityKeys.ExperienceKey(PersonalityCause.Invaded))));
        }

        [Test]
        public void FamineRaisesPiety()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 0, out var human);
            var sys = Start(ctx);
            float basePiety = AdvisorBoard.RaceTrait(db, RaceId.Human, AITrait.Piety);
            for (int run = 0; run < 40; run++)
            {
                for (int k = 0; k < 5; k++) ctx.Events.Publish(new FamineEvent { SettlementId = s.Id, Shortfall = 0.8f });
                RunOnce(sys, ctx);
            }
            float p = AdvisorBoard.Trait(human, db, AITrait.Piety);
            Assert.Greater(p, basePiety + 0.1f);
            Assert.LessOrEqual(p, basePiety + PersonalityModel.MaxDeviation + Eps);
        }

        [Test]
        public void WrathRaisesPietyForPiousAndLowersItForImpious()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var se = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 0, out var elf);
            var so = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(50, 50), 0, out var orc);
            var sys = Start(ctx);
            for (int run = 0; run < 40; run++)
            {
                ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Lightning, Target = se.Center, Radius = 2, Success = true });
                ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Meteor, Target = so.Center, Radius = 2, Success = true });
                ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Plague, Target = so.Center, Radius = 2, Success = false }); // ignored
                RunOnce(sys, ctx);
            }
            Assert.Greater(AdvisorBoard.Trait(elf, db, AITrait.Piety), 0.85f + 0.05f);
            Assert.Less(AdvisorBoard.Trait(orc, db, AITrait.Piety), 0.2f - 0.1f);
            Assert.GreaterOrEqual(AdvisorBoard.Trait(orc, db, AITrait.Piety), 0f);
        }

        [Test]
        public void BlessingsRaisePiety()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var so = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(30, 30), 0, out var orc);
            var sys = Start(ctx);
            for (int run = 0; run < 40; run++)
            {
                ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.BountifulHarvest, Target = so.Center, Radius = 3, Success = true });
                RunOnce(sys, ctx);
            }
            Assert.Greater(AdvisorBoard.Trait(orc, db, AITrait.Piety), 0.2f + 0.1f);
        }

        [Test]
        public void ContactWithManyRacesLowersXenophobia()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 0, out var elf);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(50, 10), 0, out var human);
            TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(10, 50), 0, out var dwarf);
            TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(50, 50), 0, out var saurian);
            ctx.State.Diplomacy.Get(elf.Id, human.Id).HasContact = true;
            ctx.State.Diplomacy.Get(elf.Id, dwarf.Id).HasContact = true;
            ctx.State.Diplomacy.Get(elf.Id, saurian.Id).HasContact = true;
            var sys = Start(ctx);
            for (int run = 0; run < 200; run++) RunOnce(sys, ctx);
            Assert.Less(AdvisorBoard.Trait(elf, db, AITrait.Xenophobia), 0.75f - 0.1f);
        }

        [Test]
        public void DriftRateIsBoundedPerRun()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 0, out var elf);
            var sys = Start(ctx);
            for (int c = 0; c < PersonalityKeys.CauseCount; c++)
                elf.Memory[PersonalityKeys.ExperienceKey((PersonalityCause)c)] = 500f;
            var prev = new float[PersonalityKeys.TraitCount];
            for (int run = 0; run < 30; run++)
            {
                for (int t = 0; t < prev.Length; t++) prev[t] = AdvisorBoard.Trait(elf, db, (AITrait)t);
                RunOnce(sys, ctx);
                for (int t = 0; t < prev.Length; t++)
                {
                    float now = AdvisorBoard.Trait(elf, db, (AITrait)t);
                    Assert.LessOrEqual(Math.Abs(now - prev[t]), PersonalityModel.MaxStepPerRun + 1e-5f, ((AITrait)t).ToString());
                    float race = AdvisorBoard.RaceTrait(db, RaceId.Elf, (AITrait)t);
                    Assert.LessOrEqual(Math.Abs(now - race), PersonalityModel.MaxDeviation + Eps);
                    Assert.IsTrue(now >= 0f && now <= 1f);
                }
            }
        }

        [Test]
        public void ChronicleIsRateLimitedPerTrait()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 0, out var elf);
            var sys = Start(ctx);
            float race = AdvisorBoard.RaceTrait(db, RaceId.Elf, AITrait.Aggression);
            int before = PersonalityChronicles(ctx, elf.Id);

            AdvisorBoard.SetTrait(elf, AITrait.Aggression, race + 0.3f);
            RunOnce(sys, ctx);
            Assert.AreEqual(before + 1, PersonalityChronicles(ctx, elf.Id));

            // Swing back and forth within 50 years: no new entry.
            for (int i = 0; i < 5; i++)
            {
                AdvisorBoard.SetTrait(elf, AITrait.Aggression, race);
                RunOnce(sys, ctx);
                AdvisorBoard.SetTrait(elf, AITrait.Aggression, race + 0.3f);
                RunOnce(sys, ctx);
            }
            Assert.AreEqual(before + 1, PersonalityChronicles(ctx, elf.Id));

            // Half a century later the next notable swing is chronicled again.
            ctx.State.Tick += SimTime.YearsToTicks(51);
            AdvisorBoard.SetTrait(elf, AITrait.Aggression, race);
            RunOnce(sys, ctx);
            AdvisorBoard.SetTrait(elf, AITrait.Aggression, race + 0.3f);
            RunOnce(sys, ctx);
            Assert.AreEqual(before + 2, PersonalityChronicles(ctx, elf.Id));
        }

        [Test]
        public void DefeatsMakeProudElvesSuspiciousAndChronicleSaysWhy()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var se = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 0, out var elf);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(50, 50), 0, out var orc);
            var sys = Start(ctx);
            for (int run = 0; run < 80; run++)
            {
                ctx.Events.Publish(new BattleEvent { AttackerCivId = orc.Id, DefenderCivId = elf.Id, SettlementId = se.Id, AttackerWon = true });
                ctx.Events.Publish(new BattleEvent { AttackerCivId = orc.Id, DefenderCivId = elf.Id, SettlementId = se.Id, AttackerWon = true });
                RunOnce(sys, ctx);
            }
            Assert.Greater(AdvisorBoard.Trait(elf, db, AITrait.Xenophobia), 0.75f + 0.2f - Eps);
            bool found = false;
            var e = ctx.State.Chronicle.Entries;
            for (int i = 0; i < e.Count; i++)
                if (e[i].Text.Contains("Bitter from their defeats") && e[i].Text.Contains("Elves of " + elf.Name) && e[i].Text.Contains("suspicious of outsiders")) found = true;
            Assert.IsTrue(found, "expected an evocative chronicle entry about the elves' bitterness");
        }

        [Test]
        public void DescribeReturnsTwoOrThreeAdjectives()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(10, 10), 0, out var orc);
            var sys = Start(ctx);
            string d0 = PersonalityQueries.Describe(orc, db);
            int n0 = d0.Split(',').Length;
            Assert.IsTrue(n0 >= 2 && n0 <= 3, d0);
            orc.Memory[PersonalityKeys.ExperienceKey(PersonalityCause.Victory)] = 200f;
            for (int run = 0; run < 60; run++) RunOnce(sys, ctx);
            string d1 = PersonalityQueries.Describe(orc, db);
            int n1 = d1.Split(',').Length;
            Assert.IsTrue(n1 >= 2 && n1 <= 3, d1);
            StringAssert.Contains("warlike", d1);
        }

        [Test]
        public void IsDeterministic()
        {
            Assert.AreEqual(Scenario(), Scenario());
        }

        private static string Scenario()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db, 64, 7);
            var se = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 0, out var elf);
            var so = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(50, 50), 0, out var orc);
            var sys = Start(ctx);
            for (int run = 0; run < 120; run++)
            {
                if (run % 3 == 0) ctx.Events.Publish(new BattleEvent { AttackerCivId = orc.Id, DefenderCivId = elf.Id, AttackerWon = run % 2 == 0 });
                if (run % 5 == 0) ctx.Events.Publish(new FamineEvent { SettlementId = so.Id, Shortfall = 0.5f });
                if (run % 7 == 0) ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Bless, Target = se.Center, Radius = 2, Success = true });
                if (run % 11 == 0) ctx.Events.Publish(new TechResearchedEvent { CivilizationId = elf.Id, TechId = 0 });
                if (run == 40) ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = orc.Id, DefenderId = elf.Id });
                if (run == 60) ctx.Events.Publish(new PeaceSignedEvent { WarId = 1, AttackerId = orc.Id, DefenderId = elf.Id, WarScore = 60f });
                RunOnce(sys, ctx);
            }
            var sb = new System.Text.StringBuilder();
            var civs = new List<Civilization> { elf, orc };
            foreach (var c in civs)
                for (int t = 0; t < PersonalityKeys.TraitCount; t++)
                    sb.Append(AdvisorBoard.Trait(c, db, (AITrait)t).ToString("R")).Append(';');
            foreach (var e in ctx.State.Chronicle.Entries) sb.Append(e.Tick).Append(e.Text).Append('|');
            sb.Append(PersonalityQueries.Describe(elf, db)).Append(PersonalityQueries.Describe(orc, db));
            return sb.ToString();
        }
    }
}
