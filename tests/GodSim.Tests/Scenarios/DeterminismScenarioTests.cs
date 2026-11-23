using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;

namespace GodSim.Tests.Scenarios
{
    /// <summary>
    /// Scenario 9 (determinism under god powers) and the mid-construction half of scenario 12 (save/load),
    /// plus purity of ComputeHash/Save and tamper detection of signed saves.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public sealed class DeterminismScenarioTests
    {
        private const ulong Seed = 8675309UL;

        private static SimConfig Config(ulong seed)
        {
            var cfg = ScenarioWorlds.SmallWorld(seed);
            cfg.StartingMana = 500f;
            return cfg;
        }

        // ------------------------------------------------------------------ 9) same seed + same casts => same hashes
        private struct ScriptedCast
        {
            public int Tick;
            public GodPowerType Power;
            public int Radius;
            public bool OnEmptyLand;
            public ScriptedCast(int tick, GodPowerType power, int radius, bool onEmptyLand = false) { Tick = tick; Power = power; Radius = radius; OnEmptyLand = onEmptyLand; }
        }

        private static readonly ScriptedCast[] Script =
        {
            new ScriptedCast(120, GodPowerType.Bless, 5),
            new ScriptedCast(240, GodPowerType.Rain, 8),
            new ScriptedCast(330, GodPowerType.Fertility, 4),
            new ScriptedCast(420, GodPowerType.Plague, 4),
            new ScriptedCast(540, GodPowerType.Meteor, 3),
            new ScriptedCast(600, GodPowerType.SpawnTribe, 0, true),
            new ScriptedCast(700, GodPowerType.Earthquake, 4),
            new ScriptedCast(760, GodPowerType.RaiseLand, 3, true),
            new ScriptedCast(800, GodPowerType.Wildfire, 3),
            new ScriptedCast(880, GodPowerType.Flood, 4),
            new ScriptedCast(940, GodPowerType.Lightning, 1),
            new ScriptedCast(1000, GodPowerType.Drought, 6),
            new ScriptedCast(1050, GodPowerType.Heal, 4),
        };

        private const int ScriptTicks = 1152; // 12 years

        private sealed class RunRecord
        {
            public readonly List<long> Ticks = new List<long>();
            public readonly List<ulong> Hashes = new List<ulong>();
            public readonly List<string> Casts = new List<string>();
        }

        private static RunRecord RunScripted(ulong seed)
        {
            var rec = new RunRecord();
            using (var h = new ScenarioHarness(ScenarioWorlds.Create(Config(seed))))
            {
                h.SampleEveryTicks = 0;
                int next = 0;
                while (h.Now - h.StartTick < ScriptTicks)
                {
                    while (next < Script.Length && Script[next].Tick <= h.Now - h.StartTick)
                    {
                        var sc = Script[next++];
                        var def = h.Db.GodPower(sc.Power);
                        int radius = Math.Min(def.MaxRadius, Math.Max(def.MinRadius, sc.Radius));
                        Vec2i target; int param = 0;
                        if (sc.OnEmptyLand)
                        {
                            ScenarioWorlds.TryFindEmptyLand(h.State, h.Db, 15, out target, out var race);
                            if (!target.IsValid) target = new Vec2i(h.State.Map.Width / 2, h.State.Map.Height / 2);
                            if (def.UsesRaceParameter) param = (int)race;
                        }
                        else
                        {
                            var st = ScenarioWorlds.LargestSettlement(h.State);
                            target = st != null ? st.Center : new Vec2i(h.State.Map.Width / 2, h.State.Map.Height / 2);
                        }
                        if (h.State.God.Mana < 400f) h.State.God.Mana = Math.Min(h.State.God.MaxMana, 400f); // identical in both runs
                        var o = h.Cast(sc.Power, target, radius, param, 0);
                        rec.Casts.Add("t" + sc.Tick + " " + sc.Power + "@" + target + " accepted=" + o.Accepted);
                        rec.Ticks.Add(h.Now);
                        rec.Hashes.Add(h.Sim.ComputeHash());
                    }
                    h.Step();
                    if ((h.Now - h.StartTick) % 48 == 0) { rec.Ticks.Add(h.Now); rec.Hashes.Add(h.Sim.ComputeHash()); }
                }
                foreach (var c in h.Casts) rec.Casts.Add("exec t" + c.Tick + " " + c.Event.Power + " success=" + c.Event.Success);
                rec.Ticks.Add(h.Now);
                rec.Hashes.Add(h.Sim.ComputeHash());
            }
            return rec;
        }

        [Test]
        public void SameSeedAndScriptedCasts_ProduceIdenticalHashes()
        {
            var a = RunScripted(Seed);
            var b = RunScripted(Seed);
            Assert.That(b.Hashes.Count, Is.EqualTo(a.Hashes.Count), "Runs recorded a different number of checkpoints.");
            for (int i = 0; i < a.Hashes.Count; i++)
                if (a.Hashes[i] != b.Hashes[i] || a.Ticks[i] != b.Ticks[i])
                    Assert.Fail("Runs diverged at checkpoint " + i + " (" + SimTime.Format(a.Ticks[i]) + "): " + a.Hashes[i].ToString("X16") + " vs " + b.Hashes[i].ToString("X16") +
                                "\nrun A casts:\n" + ScenarioWorlds.Join(a.Casts, 40) + "run B casts:\n" + ScenarioWorlds.Join(b.Casts, 40));
            Assert.That(b.Casts, Is.EqualTo(a.Casts), "Cast outcomes differ between identical runs.");
            int executed = 0;
            foreach (var c in a.Casts) if (c.StartsWith("exec ")) executed++;
            Assert.That(executed, Is.GreaterThan(0), "No scripted power was executed; the determinism check did not cover god powers.\n" + ScenarioWorlds.Join(a.Casts, 40));
            TestContext.WriteLine(ScenarioWorlds.Join(a.Casts, 40));
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentHashes()
        {
            var a = ScenarioWorlds.Create(Config(Seed));
            var b = ScenarioWorlds.Create(Config(Seed + 1));
            Assert.That(a.ComputeHash(), Is.EqualTo(a.ComputeHash()), "ComputeHash is not stable for an unchanged world.");
            Assert.That(b.ComputeHash(), Is.Not.EqualTo(a.ComputeHash()), "Different seeds produced the same world hash.");
            ulong h0 = a.ComputeHash();
            a.Step();
            Assert.That(a.ComputeHash(), Is.Not.EqualTo(h0), "Hash did not change after a tick (does it cover the clock/state?).");
        }

        [Test]
        public void ObservingTheWorld_HashAndSave_DoesNotPerturbIt()
        {
            const int ticks = 2 * SimTime.TicksPerYear;
            var plain = ScenarioWorlds.Create(Config(Seed));
            plain.StepMany(ticks);
            var observed = ScenarioWorlds.Create(Config(Seed));
            for (int i = 0; i < ticks; i++)
            {
                observed.Step();
                if (i % 10 == 0) observed.ComputeHash();
                if (i % 64 == 0) observed.Save();
            }
            Assert.That(observed.Tick, Is.EqualTo(plain.Tick));
            Assert.That(observed.ComputeHash(), Is.EqualTo(plain.ComputeHash()), "Calling ComputeHash()/Save() changed the simulation outcome.");
        }

        // ------------------------------------------------------------------ 12) save/load mid-construction
        [Test]
        public void SaveLoad_MidConstruction_HashesMatchStraightRun()
        {
            var cfg = Config(Seed);
            using (var h = new ScenarioHarness(ScenarioWorlds.Create(cfg)))
            {
                h.SampleEveryTicks = 0;
                h.RunYears(6);
                // Prefer a site whose cost is paid and work has begun, i.e. genuinely mid-construction.
                Building site = null;
                h.RunUntil(x => (site = FindSite(x.State, true)) != null, 4 * SimTime.TicksPerYear);
                if (site == null) site = FindSite(h.State, false);
                Assert.That(site, Is.Not.Null, "No building under construction found after 10 years.\n" + h.Summary());
                int siteId = site.Id;
                float progress = site.ConstructionProgress;
                bool costPaid = site.CostPaid;

                long saveTick = h.Now;
                ulong hashAtSave = h.Sim.ComputeHash();
                byte[] data = h.Sim.Save();
                Assert.That(data, Is.Not.Null.And.Not.Empty, "Save returned no data.");
                const int resume = 2 * SimTime.TicksPerYear;
                h.RunTicks(resume);
                ulong straight = h.Sim.ComputeHash();

                var loaded = GodSimulation.Load(data, cfg.Clone().Sanitize(), new CollectingLogger());
                Assert.That(loaded.State.Tick, Is.EqualTo(saveTick), "Loaded tick differs.");
                Assert.That(loaded.ComputeHash(), Is.EqualTo(hashAtSave), "Hash right after Load differs from the hash at Save.");
                var b = loaded.State.Buildings.Get(siteId);
                Assert.That(b, Is.Not.Null, "The construction site was lost in the save.");
                Assert.That(b.IsComplete, Is.False, "Construction site became complete through save/load.");
                Assert.That(b.ConstructionProgress, Is.EqualTo(progress), "ConstructionProgress changed through save/load.");
                Assert.That(b.CostPaid, Is.EqualTo(costPaid), "CostPaid changed through save/load.");
                for (int i = 0; i < resume; i++) loaded.Step();
                Assert.That(loaded.ComputeHash(), Is.EqualTo(straight),
                    "Resumed run diverged from the straight run " + resume + " ticks after a save at " + SimTime.Format(saveTick) + ".");
                Assert.That(ScenarioWorlds.DescribeProfiles(loaded.Engine), Does.Not.Contain("DISABLED"));
            }
        }

        private static Building FindSite(WorldState s, bool started)
        {
            var b = s.Buildings.Items;
            for (int i = 0; i < b.Count; i++)
            {
                var x = b[i];
                if (x.IsDestroyed || x.IsComplete) continue;
                if (!started || (x.CostPaid && x.ConstructionProgress > 0f && x.ConstructionProgress < 1f)) return x;
            }
            return null;
        }

        [Test]
        public void SaveLoad_SignedSave_RoundTripsAndDetectsTampering()
        {
            var cfg = Config(Seed);
            var sim = ScenarioWorlds.Create(cfg);
            sim.StepMany(3 * SimTime.TicksPerYear);
            var key = new byte[32];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(i * 7 + 3);
            byte[] data = sim.Save(key);
            Assert.That(data, Is.Not.Null.And.Not.Empty);
            var ok = GodSimulation.Load(data, cfg.Clone().Sanitize(), null, key);
            Assert.That(ok.ComputeHash(), Is.EqualTo(sim.ComputeHash()), "Signed round trip changed the world.");

            var tampered = (byte[])data.Clone();
            tampered[tampered.Length / 2] ^= 0x5A;
            Assert.That(() => GodSimulation.Load(tampered, cfg.Clone().Sanitize(), null, key), Throws.InstanceOf<Exception>(), "Tampered signed save was accepted.");
            var wrongKey = (byte[])key.Clone();
            wrongKey[0] ^= 0xFF;
            Assert.That(() => GodSimulation.Load(data, cfg.Clone().Sanitize(), null, wrongKey), Throws.InstanceOf<Exception>(), "Save loaded with the wrong HMAC key.");
            var truncated = new byte[data.Length / 3];
            Array.Copy(data, truncated, truncated.Length);
            Assert.That(() => GodSimulation.Load(truncated, cfg.Clone().Sanitize(), null, key), Throws.InstanceOf<Exception>(), "Truncated save was accepted.");
            Assert.That(() => GodSimulation.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, cfg.Clone().Sanitize()), Throws.InstanceOf<Exception>(), "Garbage bytes were accepted as a save.");
        }
    }
}
