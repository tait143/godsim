using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;
using Util = GodSim.Tests.Integration.IntegrationTestUtil;

namespace GodSim.Tests.Integration
{
    /// <summary>(e) Absurd/hostile configs are sanitized by the composition root and never crash the world.</summary>
    [TestFixture]
    public class ConfigSanitizeTests
    {
        [Test]
        public void AbsurdSizesAndNegativeMultipliersDoNotCrash()
        {
            var config = new SimConfig
            {
                Seed = 99UL,
                MapWidth = 99999,
                MapHeight = -50,
                Preset = (WorldPreset)200,
                StartingRaces = new[] { (RaceId)99, RaceId.Elf, RaceId.Orc },
                InitialPopulationPerRace = -10,
                MaxPopulation = -1,
                MaxSettlementsPerCiv = 0,
                MaxBuildingsPerSettlement = -5,
                MaxArmies = -1,
                MaxCommandsPerTick = 0,
                CommandQueueCapacity = -1,
                GrowthMultiplier = -5f,
                ResearchMultiplier = float.NaN,
                ProductionMultiplier = -1f,
                DisasterFrequency = float.PositiveInfinity,
                StartingMana = -100f,
                ManaRegenMultiplier = float.NegativeInfinity,
                MaxSystemFaults = 0,
            };

            GodSimulation sim = null;
            Assert.DoesNotThrow(() => sim = GodSimulation.CreateNew(config));
            Assert.IsNotNull(sim);

            var map = sim.State.Map;
            Assert.That(map.Width, Is.InRange(WorldMap.MinSize, WorldMap.MaxSize));
            Assert.That(map.Height, Is.InRange(WorldMap.MinSize, WorldMap.MaxSize));
            var c = sim.Config;
            Assert.IsNotNull(c);
            Assert.That(c.GrowthMultiplier, Is.GreaterThanOrEqualTo(0f));
            Assert.That(c.ProductionMultiplier, Is.GreaterThanOrEqualTo(0f));
            Assert.IsFalse(float.IsNaN(c.ResearchMultiplier), "ResearchMultiplier NaN survived sanitize");
            Assert.IsFalse(float.IsInfinity(c.DisasterFrequency), "DisasterFrequency Infinity survived sanitize");
            Assert.That(c.StartingMana, Is.GreaterThanOrEqualTo(0f));
            Assert.That(sim.State.God.Mana, Is.GreaterThanOrEqualTo(0f));

            Assert.DoesNotThrow(() => sim.StepMany(Util.Ticks(1)));
            Assert.AreEqual("", Util.NonFiniteReport(sim.State));
        }

        [Test]
        public void ZeroMultipliersAndTinyMapDoNotCrash()
        {
            var config = new SimConfig
            {
                Seed = 3UL,
                MapWidth = 1,
                MapHeight = 1,
                GrowthMultiplier = 0f,
                ResearchMultiplier = 0f,
                ProductionMultiplier = 0f,
                DisasterFrequency = 0f,
                StartingMana = 0f,
                ManaRegenMultiplier = 0f,
            };
            GodSimulation sim = null;
            Assert.DoesNotThrow(() => sim = GodSimulation.CreateNew(config));
            Assert.That(sim.State.Map.Width, Is.GreaterThanOrEqualTo(WorldMap.MinSize));
            Assert.DoesNotThrow(() => sim.StepMany(Util.Ticks(1)));
            Assert.AreEqual("", Util.NonFiniteReport(sim.State));
        }

        [Test]
        public void TooManyOrMissingRacesDoNotCrash()
        {
            var many = new RaceId[40];
            for (int i = 0; i < many.Length; i++) many[i] = (RaceId)(i % 5);
            var crowded = new SimConfig { Seed = 11UL, MapWidth = 64, MapHeight = 64, StartingRaces = many };
            GodSimulation sim = null;
            Assert.DoesNotThrow(() => sim = GodSimulation.CreateNew(crowded));
            Assert.That(sim.State.Civilizations.Count, Is.LessThanOrEqualTo(16), "StartingRaces was not capped");
            Assert.DoesNotThrow(() => sim.StepMany(SimTime.TicksPerYear / 2));

            var none = new SimConfig { Seed = 12UL, MapWidth = 64, MapHeight = 64, StartingRaces = null };
            Assert.DoesNotThrow(() => sim = GodSimulation.CreateNew(none));
            Assert.DoesNotThrow(() => sim.StepMany(SimTime.TicksPerYear / 2));

            var empty = new SimConfig { Seed = 13UL, MapWidth = 64, MapHeight = 64, StartingRaces = new RaceId[0] };
            Assert.DoesNotThrow(() => sim = GodSimulation.CreateNew(empty));
            Assert.DoesNotThrow(() => sim.StepMany(SimTime.TicksPerYear / 2));
        }

        [Test]
        public void NullConfigIsRejectedOrDefaulted()
        {
            // Either behavior is acceptable; it must not corrupt anything or throw something unexpected later.
            GodSimulation sim = null;
            try { sim = GodSimulation.CreateNew(null); }
            catch (System.ArgumentNullException) { Assert.Pass("null config rejected with ArgumentNullException"); }
            Assert.IsNotNull(sim);
            Assert.DoesNotThrow(() => sim.StepMany(10));
        }
    }
}
