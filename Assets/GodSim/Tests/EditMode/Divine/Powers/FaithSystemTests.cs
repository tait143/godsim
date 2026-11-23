using GodSim.Core;
using GodSim.Core.Divine;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Divine.Powers
{
    public class FaithSystemTests
    {
        private SimContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = PowersTestKit.CreateContext();
            _ctx.State.Tick = 0;
            _ctx.State.God.Mana = 0f;
        }

        private Civilization AddCiv(int people, float faithOutput, float devotion)
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(10 + _ctx.State.Settlements.Count * 20, 32), people, out var civ);
            s.ServiceOutput[(int)ServiceType.Faith] = faithOutput;
            civ.Devotion = devotion;
            return civ;
        }

        [Test]
        public void Regenerates_ManaFromFaithAndDevotion()
        {
            AddCiv(10, 10f, 0.5f);
            var sys = new FaithSystem();
            Assert.AreEqual(6, sys.TickInterval);
            TestWorlds.Run(sys, _ctx, 6);
            float perTick = 10f * 0.5f * 0.02f + 10 * 0.5f * 0.0004f;
            Assert.AreEqual(perTick, _ctx.State.God.ManaPerTick, 1e-4f);
            Assert.AreEqual(perTick * 6f, _ctx.State.God.Mana, 1e-3f);
            Assert.AreEqual(305f, _ctx.State.God.MaxMana, 1e-3f);
        }

        [Test]
        public void Regen_IsProportionalToFaithAndDevotion()
        {
            var civ = AddCiv(0, 10f, 0.4f);
            float a = FaithSystem.ComputeRegenPerTick(_ctx, out _);
            _ctx.State.Settlements.Items[0].ServiceOutput[(int)ServiceType.Faith] = 20f;
            float b = FaithSystem.ComputeRegenPerTick(_ctx, out _);
            civ.Devotion = 0.8f;
            float c = FaithSystem.ComputeRegenPerTick(_ctx, out _);
            Assert.AreEqual(2f * a, b, 1e-5f);
            Assert.AreEqual(2f * b, c, 1e-5f);
            _ctx.Config.ManaRegenMultiplier = 3f;
            Assert.AreEqual(3f * c, FaithSystem.ComputeRegenPerTick(_ctx, out _), 1e-5f);
        }

        [Test]
        public void DeadCivilizations_DoNotPray()
        {
            var civ = AddCiv(10, 10f, 0.9f);
            civ.IsAlive = false;
            Assert.AreEqual(0f, FaithSystem.ComputeRegenPerTick(_ctx, out int followers));
            Assert.AreEqual(0, followers);
        }

        [Test]
        public void Mana_IsClampedToMaxMana_AndMaxManaScalesWithFollowers()
        {
            AddCiv(40, 50f, 1f);
            _ctx.State.God.Mana = 100000f;
            TestWorlds.Run(new FaithSystem(), _ctx, 6);
            Assert.AreEqual(320f, _ctx.State.God.MaxMana, 1e-3f);
            Assert.AreEqual(320f, _ctx.State.God.Mana, 1e-3f);
            Assert.AreEqual(300f, FaithSystem.MaxManaFor(0));
            Assert.AreEqual(5000f, FaithSystem.MaxManaFor(1000000));
        }

        [Test]
        public void NaNMana_IsSanitized()
        {
            AddCiv(4, 1f, 0.5f);
            _ctx.State.God.Mana = float.NaN;
            TestWorlds.Run(new FaithSystem(), _ctx, 6);
            Assert.IsFalse(float.IsNaN(_ctx.State.God.Mana));
            Assert.IsTrue(_ctx.State.God.Mana >= 0f);
        }

        [Test]
        public void Devotion_DriftsTowardTarget_AndFavorDecays()
        {
            var civ = AddCiv(4, 0f, 0.1f);
            _ctx.State.Settlements.Items[0].Faith = 0.8f;
            civ.GodFavor = 0.5f;
            TestWorlds.Run(new FaithSystem(), _ctx, 6);
            Assert.AreEqual(0.495f, civ.GodFavor, 1e-5f);
            Assert.IsTrue(civ.Devotion > 0.1f && civ.Devotion < 0.8f, "devotion " + civ.Devotion);
            TestWorlds.Run(new FaithSystem(), _ctx, 96 * 30, false);
            float target = 0.3f + 0.5f * 0.8f + 0.2f * civ.GodFavor;
            Assert.AreEqual(target, civ.Devotion, 0.02f);
            Assert.IsTrue(civ.GodFavor < 0.01f);
        }

        [Test]
        public void EmptyWorld_HasBaseCapacityAndNoRegen()
        {
            TestWorlds.Run(new FaithSystem(), _ctx, 12);
            Assert.AreEqual(0f, _ctx.State.God.ManaPerTick);
            Assert.AreEqual(300f, _ctx.State.God.MaxMana);
            Assert.AreEqual(0f, _ctx.State.God.Mana);
        }
    }
}
