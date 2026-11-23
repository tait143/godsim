using System;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>
    /// Scenario 7 (DivineIntervention): god powers cast through GodSimulation.TryCastPower on a living world.
    /// Natural disasters are off so every DisasterEvent/destruction observed is caused by the cast under test.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public sealed class DivineScenarioTests
    {
        private const ulong Seed = 5150UL;

        private static ScenarioHarness NewWorld(int warmupYears, float mana = 500f)
        {
            var cfg = ScenarioWorlds.SmallWorld(Seed);
            cfg.EnableDisasters = false;
            cfg.EnableWarfare = false;
            cfg.StartingMana = mana;
            var log = new CollectingLogger();
            var h = new ScenarioHarness(ScenarioWorlds.Create(cfg, log), log);
            h.RunYears(warmupYears);
            return h;
        }

        private static void TopUpMana(ScenarioHarness h, float atLeast)
        {
            var god = h.State.God;
            if (god.MaxMana < atLeast) god.MaxMana = atLeast;
            if (god.Mana < atLeast) god.Mana = atLeast; // test setup: the mana economy itself is not under test here
        }

        private static bool Succeeded(CastOutcome o) => o.Accepted && o.SucceededInWorld;

        [Test]
        public void SpawnTribe_OnEmptyLand_CreatesANewCivilization()
        {
            using (var h = NewWorld(1))
            {
                Assert.That(ScenarioWorlds.TryFindEmptyLand(h.State, h.Db, 20, out var tile, out var race), Is.True, "No empty land far from settlements on the map.");
                TopUpMana(h, 300f);
                int civsBefore = h.State.Civilizations.Count;
                var def = h.Db.GodPower(GodPowerType.SpawnTribe);
                var o = h.Cast(GodPowerType.SpawnTribe, tile, def.MinRadius, (int)race);
                TestContext.WriteLine(o.ToString());
                Assert.That(Succeeded(o), Is.True, "SpawnTribe failed: " + o);
                Assert.That(o.CivsFounded, Is.EqualTo(1), "SpawnTribe should found exactly one civilization: " + o);
                Assert.That(h.State.Civilizations.Count, Is.EqualTo(civsBefore + 1));

                var founded = h.CivsFounded[h.CivsFounded.Count - 1].Event;
                Assert.That(founded.Race, Is.EqualTo(race), "Spawned tribe has the wrong race.");
                var civ = h.State.Civilizations.Get(founded.CivilizationId);
                Assert.That(civ != null && civ.IsAlive, Is.True, "Spawned civ is not alive.");
                Assert.That(civ.SettlementIds.Count, Is.GreaterThanOrEqualTo(1), "Spawned civ has no settlement.");
                var st = h.State.Settlements.Get(civ.SettlementIds[0]);
                Assert.That(st, Is.Not.Null);
                Assert.That(st.Center.ChebyshevTo(tile), Is.LessThanOrEqualTo(12), "Tribe settled far from the target tile " + tile + ": " + st.Center);
                Assert.That(ScenarioWorlds.CountCivPopulation(h.State, civ.Id), Is.GreaterThan(0), "Spawned tribe has no people.");
                Assert.That(o.ManaSpent, Is.GreaterThanOrEqualTo(def.ManaCost * 0.5f - 1f), "SpawnTribe did not spend mana: " + o);
                Assert.That(h.State.God.CooldownUntil[(int)GodPowerType.SpawnTribe], Is.GreaterThan(h.Now), "No cooldown set after SpawnTribe.");

                // Cooldown: an immediate second tribe elsewhere must not appear.
                TopUpMana(h, 300f);
                Assert.That(ScenarioWorlds.TryFindEmptyLand(h.State, h.Db, 20, out var tile2, out var race2), Is.True);
                var again = h.Cast(GodPowerType.SpawnTribe, tile2, def.MinRadius, (int)race2);
                TestContext.WriteLine("second: " + again);
                Assert.That(Succeeded(again), Is.False, "SpawnTribe ignored its cooldown: " + again);
                Assert.That(again.CivsFounded, Is.EqualTo(0));

                // The tribe should be a working civilization, not a husk that collapses immediately.
                h.RunYears(1);
                Assert.That(civ.IsAlive && ScenarioWorlds.CountCivPopulation(h.State, civ.Id) > 0, Is.True, "Spawned tribe died out within a year.\n" + h.Summary());
            }
        }

        [Test]
        public void Meteor_OnATown_DestroysBuildingsKillsPeopleSpendsManaAndRespectsCooldown()
        {
            using (var h = NewWorld(4))
            {
                var town = ScenarioWorlds.LargestSettlement(h.State);
                Assert.That(town, Is.Not.Null, "No town to strike.");
                var def = h.Db.GodPower(GodPowerType.Meteor);
                int radius = Math.Min(def.MaxRadius, Math.Max(def.MinRadius, 4));
                int buildingsNear = ScenarioWorlds.CountBuildingsNear(h.State, town, town.Center, radius);
                int popBefore = town.Population;
                TopUpMana(h, 300f);
                var o = h.Cast(GodPowerType.Meteor, town.Center, radius);
                TestContext.WriteLine("town pop=" + popBefore + " buildingsInRadius=" + buildingsNear + " -> " + o);
                Assert.That(Succeeded(o), Is.True, "Meteor failed: " + o);
                h.RunTicks(2); // let fires/aftermath resolve
                int destroyed = 0, killed = 0;
                foreach (var d in h.Destructions) if (d.Tick >= o.IssuedTick && d.Event.SettlementId == town.Id) destroyed++;
                foreach (var d in h.Deaths)
                {
                    var c = d.Event.Cause;
                    if (d.Tick >= o.IssuedTick && (c == DeathCause.DivineWrath || c == DeathCause.Disaster || c == DeathCause.Accident)) killed++;
                }
                Assert.That(destroyed, Is.GreaterThan(0), "Meteor destroyed no building of the town (" + buildingsNear + " in radius). " + o);
                Assert.That(killed, Is.GreaterThan(0), "Meteor killed nobody (town pop " + popBefore + "). " + o);
                Assert.That(o.ManaSpent, Is.GreaterThanOrEqualTo(def.ManaCost * 0.5f - 1f), "Meteor did not spend mana: " + o);
                long cooldownUntil = h.State.God.CooldownUntil[(int)GodPowerType.Meteor];
                Assert.That(cooldownUntil, Is.GreaterThan(h.Now), "No cooldown set after Meteor.");

                // Immediately again: must not strike, must not charge mana.
                TopUpMana(h, 300f);
                var again = h.Cast(GodPowerType.Meteor, town.Center, radius);
                TestContext.WriteLine("during cooldown: " + again);
                Assert.That(Succeeded(again), Is.False, "Meteor ignored its cooldown: " + again);
                Assert.That(again.ManaSpent, Is.LessThan(1f), "A rejected Meteor still charged mana: " + again);

                // After the cooldown the power is usable again.
                h.RunUntil(x => x.Now >= cooldownUntil, (int)(cooldownUntil - h.Now) + 2);
                var target = ScenarioWorlds.LargestSettlement(h.State);
                Assert.That(target, Is.Not.Null);
                TopUpMana(h, 300f);
                var later = h.Cast(GodPowerType.Meteor, target.Center, radius);
                TestContext.WriteLine("after cooldown: " + later);
                Assert.That(Succeeded(later), Is.True, "Meteor unusable after its cooldown expired: " + later);
            }
        }

        [Test]
        public void Rain_EndsADrought()
        {
            using (var h = NewWorld(1))
            {
                var town = ScenarioWorlds.LargestSettlement(h.State);
                Assert.That(town, Is.Not.Null);
                TopUpMana(h, 300f);
                var drought = h.Cast(GodPowerType.Drought, town.Center, Clamp(GodPowerType.Drought, h, 8));
                TestContext.WriteLine("drought: " + drought + " droughtTicks=" + h.State.Weather.DroughtTicksRemaining);
                if (h.State.Weather.DroughtTicksRemaining <= 0)
                {
                    // The Drought power may be local (moisture) rather than weather; emulate a natural drought instead.
                    TestContext.WriteLine("Drought power did not set Weather.DroughtTicksRemaining; setting a natural drought directly.");
                    h.State.Weather.DroughtTicksRemaining = 4 * SimTime.TicksPerSeason;
                }
                h.RunTicks(2);
                Assume.That(h.State.Weather.DroughtTicksRemaining, Is.GreaterThan(0), "Weather cleared the drought on its own immediately; cannot test Rain.");
                TopUpMana(h, 300f);
                var rain = h.Cast(GodPowerType.Rain, town.Center, Clamp(GodPowerType.Rain, h, 8));
                h.RunTicks(1);
                TestContext.WriteLine("rain: " + rain + " droughtTicks=" + h.State.Weather.DroughtTicksRemaining);
                Assert.That(Succeeded(rain), Is.True, "Rain failed: " + rain);
                Assert.That(h.State.Weather.DroughtTicksRemaining, Is.EqualTo(0), "Rain did not end the drought.");
                Assert.That(rain.ManaSpent, Is.GreaterThan(0f), "Rain did not spend mana: " + rain);
            }
        }

        [Test]
        public void Cast_WithInsufficientMana_IsRefusedAndChangesNothing()
        {
            using (var h = NewWorld(2))
            {
                var town = ScenarioWorlds.LargestSettlement(h.State);
                Assert.That(town, Is.Not.Null);
                h.State.God.Mana = 5f;
                var o = h.Cast(GodPowerType.Meteor, town.Center, Clamp(GodPowerType.Meteor, h, 4));
                TestContext.WriteLine(o.ToString());
                Assert.That(Succeeded(o), Is.False, "Meteor succeeded with 5 mana: " + o);
                Assert.That(o.BuildingsDestroyed, Is.EqualTo(0), "Refused Meteor still destroyed buildings.");
                Assert.That(o.ManaAfter, Is.GreaterThanOrEqualTo(o.ManaBefore - 1e-3f), "Refused cast changed mana: " + o);
                Assert.That(h.State.God.Mana, Is.GreaterThanOrEqualTo(0f), "Mana went negative.");
                Assert.That(o.Accepted ? (o.Executed || o.RejectedAtExecution) : !string.IsNullOrEmpty(o.Error), Is.True,
                    "A refused cast must be explained (error at enqueue, or a cast/rejection event at execution): " + o);
            }
        }

        [Test]
        public void CommandSpam_IsRateLimited()
        {
            using (var h = NewWorld(1))
            {
                var town = ScenarioWorlds.LargestSettlement(h.State);
                Assert.That(town, Is.Not.Null);
                TopUpMana(h, 500f);
                var cfg = h.Sim.Config;
                int limit = cfg.MaxCommandsPerTick, capacity = h.Sim.Commands.Capacity;
                int rejectedBefore = h.Sim.Commands.RejectedCount, casts0 = h.Casts.Count;
                long tick = h.Now;
                int accepted = 0, rejected = 0, emptyErrors = 0;
                for (int i = 0; i < 200; i++)
                {
                    var target = new Vec2i(town.Center.X + (i % 7) - 3, town.Center.Y + (i / 7 % 7) - 3);
                    if (h.Sim.TryCastPower(GodPowerType.Lightning, h.State.Map.ClampToBounds(target), 1, 0, out string error)) accepted++;
                    else { rejected++; if (string.IsNullOrEmpty(error)) emptyErrors++; }
                }
                TestContext.WriteLine("spam: accepted=" + accepted + " rejected=" + rejected + " limit/tick=" + limit + " capacity=" + capacity +
                                      " queueRejected+=" + (h.Sim.Commands.RejectedCount - rejectedBefore));
                Assert.That(h.Now, Is.EqualTo(tick), "Spam loop must not advance time.");
                Assert.That(accepted, Is.GreaterThanOrEqualTo(1), "Not even one Lightning was accepted.");
                Assert.That(accepted, Is.LessThanOrEqualTo(capacity), "More commands accepted than the queue capacity.");
                Assert.That(accepted, Is.LessThanOrEqualTo(limit), "Rate limit (MaxCommandsPerTick=" + limit + ") not enforced at enqueue.");
                Assert.That(emptyErrors, Is.EqualTo(0), "Rejected casts must explain why.");
                h.Step();
                int executedThisTick = 0;
                for (int i = casts0; i < h.Casts.Count; i++) if (h.Casts[i].Tick == tick + 1) executedThisTick++;
                Assert.That(executedThisTick, Is.LessThanOrEqualTo(limit), "More than MaxCommandsPerTick commands executed in one tick.");
                Assert.That(h.State.God.Mana, Is.GreaterThanOrEqualTo(0f), "Mana went negative under spam.");

                // The limit is a rate, not a lockout: a later cast goes through.
                bool acceptedLater = false;
                for (int i = 0; i < 60 && !acceptedLater; i++)
                {
                    h.Step();
                    acceptedLater = h.Sim.TryCastPower(GodPowerType.Bless, town.Center, Clamp(GodPowerType.Bless, h, 4), 0, out _);
                }
                Assert.That(acceptedLater, Is.True, "Casting stayed blocked for 60 ticks after the spam.");
            }
        }

        [Test]
        public void MalformedCommands_AreRejectedWithoutSideEffects()
        {
            using (var h = NewWorld(1))
            {
                TopUpMana(h, 500f);
                var town = ScenarioWorlds.LargestSettlement(h.State);
                Assert.That(town, Is.Not.Null);
                int civs = h.State.Civilizations.Count;
                float mana = h.State.God.Mana;
                Expect(h, GodPowerType.Bless, new Vec2i(-5, -5), 3, 0, "target out of bounds");
                Expect(h, GodPowerType.Bless, new Vec2i(h.State.Map.Width + 10, 3), 3, 0, "target out of bounds (x)");
                Expect(h, (GodPowerType)200, town.Center, 1, 0, "undefined power");
                Expect(h, GodPowerType.Bless, town.Center, 3, 7, "non-zero parameter on a power that takes none");
                Assert.That(ScenarioWorlds.TryFindEmptyLand(h.State, h.Db, 15, out var land, out _), Is.True);
                Expect(h, GodPowerType.SpawnTribe, land, 0, 99, "invalid race parameter");
                Expect(h, GodPowerType.SpawnTribe, land, 0, -1, "negative race parameter");
                h.RunTicks(3);
                Assert.That(h.State.Civilizations.Count, Is.EqualTo(civs), "A malformed SpawnTribe created a civilization.");
                Assert.That(h.State.God.Mana, Is.GreaterThanOrEqualTo(mana - 1e-3f), "Malformed commands spent mana.");

                // Oversized radius: either rejected, or clamped before execution.
                var def = h.Db.GodPower(GodPowerType.Bless);
                var big = h.Cast(GodPowerType.Bless, town.Center, def.MaxRadius + 50);
                TestContext.WriteLine("oversized radius: " + big);
                if (big.Executed)
                {
                    foreach (var c in h.Casts)
                        if (c.Tick >= big.IssuedTick && c.Event.Power == GodPowerType.Bless)
                            Assert.That(c.Event.Radius, Is.LessThanOrEqualTo(def.MaxRadius), "Radius above MaxRadius reached execution unclamped.");
                }
            }
        }

        private static void Expect(ScenarioHarness h, GodPowerType power, Vec2i target, int radius, int parameter, string what)
        {
            bool ok = h.Sim.TryCastPower(power, target, radius, parameter, out string error);
            Assert.That(ok, Is.False, "Accepted a malformed command (" + what + ").");
            Assert.That(string.IsNullOrEmpty(error), Is.False, "Rejection without a reason (" + what + ").");
        }

        private static int Clamp(GodPowerType p, ScenarioHarness h, int desired)
        {
            var d = h.Db.GodPower(p);
            return Math.Min(d.MaxRadius, Math.Max(d.MinRadius, desired));
        }
    }
}
