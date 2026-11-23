using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;
using Util = GodSim.Tests.Integration.IntegrationTestUtil;

namespace GodSim.Tests.Integration
{
    /// <summary>
    /// (d) God powers through the full pipeline (TryCastPower -> CommandQueue + validator -> divine module).
    /// Mana regeneration is disabled so every change in mana comes from casting.
    /// </summary>
    [TestFixture]
    public class GodPowerIntegrationTests
    {
        private GodSimulation _sim;
        private Settlement _capital;

        [SetUp]
        public void CreateWorld()
        {
            var config = Util.SmallConfig(2468UL, Util.SmallMap);
            config.StartingMana = 400f;
            config.ManaRegenMultiplier = 0f;
            config.EnableDisasters = false;
            _sim = GodSimulation.CreateNew(config);
            _sim.StepMany(4);
            _capital = Util.FirstCapital(_sim.State);
            Assert.IsNotNull(_capital, "no settlement to target");
        }

        [Test]
        public void BlessRainAndRaiseLandSucceedAndSpendMana()
        {
            float manaBefore = _sim.State.God.Mana;
            int castBefore = _sim.State.God.PowersCast;
            Assert.That(manaBefore, Is.GreaterThan(50f), "starting mana was not applied");
            Vec2i target = _capital.Center;

            string error;
            Assert.IsTrue(_sim.TryCastPower(GodPowerType.Bless, target, 4, 0, out error), "Bless refused: " + error);
            Assert.IsTrue(_sim.TryCastPower(GodPowerType.Rain, target, 5, 0, out error), "Rain refused: " + error);
            Assert.IsTrue(_sim.TryCastPower(GodPowerType.RaiseLand, target, 2, 0, out error), "RaiseLand refused: " + error);

            _sim.StepMany(3);

            Assert.That(_sim.State.God.Mana, Is.LessThan(manaBefore), "casting did not spend mana");
            Assert.That(_sim.State.God.PowersCast, Is.GreaterThan(castBefore), "no power was recorded as cast");
            Assert.AreEqual("", Util.FaultReport(_sim));
        }

        public enum InvalidCast
        {
            UnknownPower,
            TargetOutOfBoundsNegative,
            TargetOutOfBoundsBeyondMap,
            RadiusTooLarge,
            NegativeRadius,
            InvalidRaceParameter,
            ParameterOnPowerThatTakesNone,
            NotEnoughMana,
        }

        [TestCase(InvalidCast.UnknownPower)]
        [TestCase(InvalidCast.TargetOutOfBoundsNegative)]
        [TestCase(InvalidCast.TargetOutOfBoundsBeyondMap)]
        [TestCase(InvalidCast.RadiusTooLarge)]
        [TestCase(InvalidCast.NegativeRadius)]
        [TestCase(InvalidCast.InvalidRaceParameter)]
        [TestCase(InvalidCast.ParameterOnPowerThatTakesNone)]
        [TestCase(InvalidCast.NotEnoughMana)]
        public void InvalidCastsFailWithAnErrorAndCostNothing(InvalidCast kind)
        {
            var map = _sim.State.Map;
            Vec2i target = _capital.Center;
            GodPowerType power = GodPowerType.Bless;
            int radius = 4, parameter = 0;
            switch (kind)
            {
                case InvalidCast.UnknownPower: power = (GodPowerType)200; break;
                case InvalidCast.TargetOutOfBoundsNegative: target = new Vec2i(-3, -7); break;
                case InvalidCast.TargetOutOfBoundsBeyondMap: target = new Vec2i(map.Width + 5, map.Height); break;
                case InvalidCast.RadiusTooLarge: radius = 1000; break;
                case InvalidCast.NegativeRadius: radius = -2; break;
                case InvalidCast.InvalidRaceParameter: power = GodPowerType.SpawnTribe; radius = 0; parameter = 99; break;
                case InvalidCast.ParameterOnPowerThatTakesNone: parameter = 7; break;
                case InvalidCast.NotEnoughMana: _sim.State.God.Mana = 0f; power = GodPowerType.Meteor; radius = 3; break;
            }
            float manaBefore = _sim.State.God.Mana;
            int castBefore = _sim.State.God.PowersCast;

            string error;
            bool ok = _sim.TryCastPower(power, target, radius, parameter, out error);

            Assert.IsFalse(ok, kind + " was accepted");
            Assert.IsFalse(string.IsNullOrEmpty(error), kind + " failed without an error message");
            _sim.StepMany(2);
            Assert.AreEqual(manaBefore, _sim.State.God.Mana, 1e-4f, kind + " still cost mana");
            Assert.AreEqual(castBefore, _sim.State.God.PowersCast, kind + " was still executed");
            Assert.AreEqual("", Util.FaultReport(_sim));
        }
    }
}
