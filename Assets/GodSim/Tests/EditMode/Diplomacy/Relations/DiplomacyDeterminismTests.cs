using System.Text;
using GodSim.Core;
using GodSim.Core.Diplomacy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Relations
{
    public class DiplomacyDeterminismTests
    {
        private static string Simulate(ulong seed, int years)
        {
            var db = RelationsTestKit.Db(r =>
            {
                RelationsTestKit.Hate(r, RaceId.Orc, RaceId.Human);
                RelationsTestKit.Like(r, RaceId.Elf, RaceId.Saurian);
                r[(int)RaceId.Orc].Aggression = 0.9f;
                r[(int)RaceId.Dwarf].Aggression = 0.7f;
                r[(int)RaceId.Elf].Diplomacy = 0.9f;
            });
            var ctx = RelationsTestKit.Ctx(db, seed);
            var orc = RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(30, 30), AgeId.Bronze, 60f);
            RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(55, 30), AgeId.Iron, 30f);
            var elf = RelationsTestKit.Civ(ctx, RaceId.Elf, new Vec2i(30, 70), AgeId.Bronze, 20f);
            RelationsTestKit.Civ(ctx, RaceId.Saurian, new Vec2i(60, 70), AgeId.Bronze, 25f);
            RelationsTestKit.Civ(ctx, RaceId.Dwarf, new Vec2i(90, 50), AgeId.Bronze, 80f);
            ctx.Ops.FoundSettlement(orc, new Vec2i(45, 45));
            elf.GodFavor = 0.4f;

            var systems = RelationsTestKit.AllSystems();
            // Let some war score move as a military module would, deterministically.
            foreach (var s in systems) { ctx.DeltaTicks = s.TickInterval; s.Initialize(ctx); }
            for (int t = 0; t < years * SimTime.TicksPerYear; t++)
            {
                ctx.State.Tick++;
                foreach (var s in systems)
                {
                    if (ctx.State.Tick % s.TickInterval != 0) continue;
                    ctx.DeltaTicks = s.TickInterval;
                    s.Tick(ctx);
                    ctx.Ops.FlushRemovals();
                }
                if (ctx.State.Tick % 24 == 0)
                    foreach (var w in ctx.State.Diplomacy.Wars)
                        if (w.IsActive) { w.WarScore = MathUtil.Clamp(w.WarScore + ctx.Rng.Range(-6f, 7f), -100f, 100f); w.AttackerLosses += ctx.Rng.Range(0, 2); }
            }

            var sb = new StringBuilder();
            foreach (var r in ctx.State.Diplomacy.AllRelations)
                sb.Append(r.CivA).Append('-').Append(r.CivB).Append(':').Append(r.Opinion.ToString("R"))
                  .Append(',').Append(r.Stance).Append(',').Append(r.TradeAgreement).Append(',').Append(r.WarId)
                  .Append(',').Append(r.TruceUntilTick).Append(',').Append(r.HasContact).Append(';');
            foreach (var w in ctx.State.Diplomacy.Wars)
                sb.Append('W').Append(w.Id).Append(':').Append(w.AttackerId).Append('>').Append(w.DefenderId)
                  .Append(',').Append(w.StartTick).Append(',').Append(w.EndTick).Append(',').Append(w.CasusBelli).Append(';');
            sb.Append("chron=").Append(ctx.State.Chronicle.Entries.Count).Append(";rng=").Append(ctx.State.Rng.State)
              .Append(";declared=").Append(ctx.State.Stats.WarsDeclared);
            return sb.ToString();
        }

        [Test]
        public void SameSeed_ProducesIdenticalDiplomacy()
        {
            string a = Simulate(1234, 80);
            string b = Simulate(1234, 80);
            Assert.AreEqual(a, b);
            Assert.IsFalse(a.EndsWith("declared=0"), "the scenario should contain at least one war: " + a);
        }

        [Test]
        public void AllSystems_HaveExpectedIntervalsAndNames()
        {
            Assert.AreEqual(24, new ContactSystem().TickInterval);
            Assert.AreEqual(24, new RelationsSystem().TickInterval);
            Assert.AreEqual(48, new WarDecisionSystem().TickInterval);
            Assert.IsTrue(new ContactSystem().Name != new RelationsSystem().Name);
            Assert.IsTrue(new RelationsSystem().Name != new WarDecisionSystem().Name);
        }

        [Test]
        public void Systems_RunInsideTheEngine_WithoutFaults()
        {
            var db = RelationsTestKit.Db(r => { RelationsTestKit.Hate(r, RaceId.Orc, RaceId.Human); r[(int)RaceId.Orc].Aggression = 1f; });
            var ctx = RelationsTestKit.Ctx(db);
            RelationsTestKit.Civ(ctx, RaceId.Orc, new Vec2i(30, 30), AgeId.Bronze, 60f);
            RelationsTestKit.Civ(ctx, RaceId.Human, new Vec2i(55, 30), AgeId.Bronze, 30f);
            var engine = new SimulationEngine(ctx);
            engine.AddSystems(RelationsTestKit.AllSystems());
            engine.StepMany(SimTime.TicksPerYear * 40);
            foreach (var p in engine.Profiles) Assert.AreEqual(0, p.TotalFaults, p.Name + ": " + p.LastError);
            Assert.IsTrue(ctx.State.Stats.WarsDeclared > 0);
        }
    }
}
