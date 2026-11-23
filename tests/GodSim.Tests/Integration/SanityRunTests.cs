using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;
using Util = GodSim.Tests.Integration.IntegrationTestUtil;

namespace GodSim.Tests.Integration
{
    /// <summary>
    /// (a) A 40-year run on a 128x128 map must produce a living, growing world: several populated
    /// civilizations, new buildings, enough housing (3-5 per House), expansion or progress, and no faults.
    /// The world is simulated once in OneTimeSetUp; each test checks one property for precise failures.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    public class SanityRunTests
    {
        private const int Years = 40;
        private GodSimulation _sim;
        private int _initialCompleteBuildings;
        private int _initialNextSettlementId;
        private long _startTick;

        [OneTimeSetUp]
        public void RunWorld()
        {
            _sim = GodSimulation.CreateNew(Util.SmallConfig(Util.DefaultSeed, Util.SmallMap));
            _startTick = _sim.Tick;
            _initialCompleteBuildings = Util.CompleteBuildings(_sim.State);
            _initialNextSettlementId = _sim.State.Ids.NextSettlementId;
            _sim.StepMany(Util.Ticks(Years));
        }

        [Test]
        public void ClockAdvancedByFortyYears()
        {
            Assert.AreEqual((long)Util.Ticks(Years), _sim.Tick - _startTick);
            Assert.AreEqual(_sim.Tick, _sim.State.Tick);
        }

        [Test]
        public void AtLeastThreeCivilizationsArePopulated()
        {
            Assert.That(Util.CivsWithPopulation(_sim.State), Is.GreaterThanOrEqualTo(3), Util.Describe(_sim.State));
        }

        [Test]
        public void MoreBuildingsThanAtStart()
        {
            Assert.That(Util.CompleteBuildings(_sim.State), Is.GreaterThan(_initialCompleteBuildings),
                "complete buildings did not grow from " + _initialCompleteBuildings + "\n" + Util.Describe(_sim.State));
        }

        [Test]
        public void HousesCoverAtLeastSeventyPercentOfPopulation()
        {
            int pop = _sim.State.LivingPopulation;
            int capacity = Util.CompleteHouseCapacity(_sim.State);
            Assert.That(pop, Is.GreaterThan(0), "world died out");
            Assert.That(capacity, Is.GreaterThanOrEqualTo((int)System.Math.Ceiling(0.7 * pop)),
                "complete House capacity " + capacity + " < 70% of population " + pop);
        }

        [Test]
        public void EveryHouseHoldsThreeToFiveResidents()
        {
            var items = _sim.State.Buildings.Items;
            int houses = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b.Type != BuildingType.House || b.IsDestroyed) continue;
                houses++;
                Assert.That(b.HousingCapacity, Is.InRange(3, 5), "House " + b.Id + " capacity");
                Assert.That(b.Residents.Count, Is.LessThanOrEqualTo(b.HousingCapacity), "House " + b.Id + " is overfull");
            }
            Assert.That(houses, Is.GreaterThan(0), "no houses were built");
        }

        [Test]
        public void ExpandedOrAdvancedAnAge()
        {
            bool founded = _sim.State.Ids.NextSettlementId > _initialNextSettlementId;
            bool advanced = Util.AnyCivAtLeast(_sim.State, AgeId.Bronze);
            Assert.IsTrue(founded || advanced, "no new settlement and no age advance in " + Years + " years\n" + Util.Describe(_sim.State));
        }

        [Test]
        public void NoSystemFaulted()
        {
            Assert.AreEqual("", Util.FaultReport(_sim));
        }

        [Test]
        public void NoEventHandlerFaulted()
        {
            Assert.AreEqual(0, _sim.Events.HandlerFaults);
        }

        [Test]
        public void StateContainsNoNaNOrInfinity()
        {
            Assert.AreEqual("", Util.NonFiniteReport(_sim.State));
        }

        [Test]
        public void ReferencesAreConsistent()
        {
            var s = _sim.State;
            var people = s.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!p.IsAlive) continue;
                if (p.HomeId >= 0)
                {
                    var home = s.Buildings.Get(p.HomeId);
                    Assert.IsNotNull(home, "person " + p.Id + " lives in missing building " + p.HomeId);
                    Assert.IsTrue(home.Residents.Contains(p.Id), "person " + p.Id + " not listed as resident of " + home.Id);
                }
                if (p.SettlementId >= 0) Assert.IsNotNull(s.Settlements.Get(p.SettlementId), "person " + p.Id + " in missing settlement " + p.SettlementId);
            }
            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i];
                Assert.IsTrue(s.Map.InBounds(st.Center), "settlement " + st.Id + " center out of bounds");
                Assert.IsNotNull(s.Civilizations.Get(st.CivilizationId), "settlement " + st.Id + " owned by missing civ " + st.CivilizationId);
            }
        }
    }

    /// <summary>Fast smoke checks (not Slow): one year runs cleanly.</summary>
    [TestFixture]
    public class QuickSmokeTests
    {
        [Test]
        public void OneYearRunsWithoutFaults()
        {
            var sim = GodSimulation.CreateNew(Util.SmallConfig(7UL, 64));
            Assert.That(sim.State.Civilizations.Count, Is.GreaterThan(0), "no civilizations created");
            Assert.That(sim.State.LivingPopulation, Is.GreaterThan(0), "no initial population");
            sim.StepMany(Util.Ticks(1));
            Assert.AreEqual(1, sim.State.Year);
            Assert.AreEqual("", Util.FaultReport(sim));
        }

        [Test]
        public void BuildDatabaseIsFrozenAndComplete()
        {
            var db = GodSimulation.BuildDatabase();
            Assert.IsTrue(db.IsFrozen, "BuildDatabase must return a frozen database");
            List<string> errors = db.Validate();
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            Assert.AreEqual(3, db.Building(BuildingType.House).HousingMin);
            Assert.AreEqual(5, db.Building(BuildingType.House).HousingMax);
        }
    }
}
