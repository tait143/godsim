using GodSim.Core;
using GodSim.Core.Security;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Security
{
    public class GodCommandValidatorTests
    {
        private GameDatabase _db;
        private SimContext _ctx;
        private Civilization _civ;
        private GodCommandValidator _v;

        [SetUp]
        public void SetUp()
        {
            _db = SecurityTestData.CreatePowerDb();
            _ctx = SecurityTestData.CreateWorld(_db, out _, out _civ);
            _v = new GodCommandValidator(new SimConfig { MaxCommandsPerTick = 4 });
        }

        private bool Check(GodCommand c, out string reason) => _v.ValidateForExecution(c, _ctx.State, _db, out reason);

        private void AssertRejected(GodCommand c, string expectedFragment = null)
        {
            Assert.IsFalse(_v.Validate(c, _ctx.State, _db, out string reason), "expected rejection");
            Assert.IsFalse(string.IsNullOrEmpty(reason), "a rejection must carry a reason");
            if (expectedFragment != null) SecurityTestData.AssertContains(expectedFragment, reason);
        }

        [Test]
        public void ValidCommand_IsAccepted()
        {
            Assert.IsTrue(_v.Validate(SecurityTestData.Cmd(GodPowerType.Rain, 22, 22, 3), _ctx.State, _db, out string reason), reason);
            Assert.IsNull(reason);
            Assert.IsTrue(_v.Validate(SecurityTestData.Cmd(GodPowerType.Lightning, 30, 30, 0), _ctx.State, _db, out reason), reason);
            Assert.IsTrue(_v.Validate(SecurityTestData.Cmd(GodPowerType.SpawnTribe, 40, 40, 1, (int)RaceId.Elf), _ctx.State, _db, out reason), reason);
        }

        [Test]
        public void NullCommand_IsRejected()
        {
            AssertRejected(null, "No command");
        }

        [Test]
        public void MissingStateOrDatabase_IsRejected()
        {
            Assert.IsFalse(_v.Validate(SecurityTestData.Cmd(GodPowerType.Bless, 5, 5), null, _db, out string reason));
            Assert.IsNotNull(reason);
            Assert.IsFalse(_v.Validate(SecurityTestData.Cmd(GodPowerType.Bless, 5, 5), _ctx.State, null, out reason));
            Assert.IsNotNull(reason);
        }

        [TestCase(-1, 5)]
        [TestCase(64, 5)]
        [TestCase(5, 64)]
        [TestCase(5, -1)]
        [TestCase(int.MinValue, int.MinValue)]
        [TestCase(int.MaxValue, int.MaxValue)]
        public void OutOfBoundsTarget_IsRejected(int x, int y)
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, x, y), "outside the world");
        }

        [TestCase(0)]
        [TestCase(7)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void RadiusOutsideDefinition_IsRejected(int radius)
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, radius), "Radius");
        }

        [Test]
        public void RadiusIsCappedEvenWhenContentAllowsHuge()
        {
            var db = new GameDatabase();
            db.Register(new GodPowerDefinition { Type = GodPowerType.Bless, Name = "Bless", ManaCost = 1f, MinRadius = 0, MaxRadius = int.MaxValue });
            TestDb.FillMissing(db);
            db.Freeze();
            var ctx = TestWorlds.CreateContext(db, 64);
            var v = new GodCommandValidator(new SimConfig());
            Assert.IsTrue(v.ValidateForExecution(SecurityTestData.Cmd(GodPowerType.Bless, 5, 5, GodCommandValidator.AbsoluteMaxRadius), ctx.State, db, out string reason), reason);
            Assert.IsFalse(v.ValidateForExecution(SecurityTestData.Cmd(GodPowerType.Bless, 5, 5, GodCommandValidator.AbsoluteMaxRadius + 1), ctx.State, db, out reason));
            Assert.IsFalse(v.ValidateForExecution(SecurityTestData.Cmd(GodPowerType.Bless, 5, 5, int.MaxValue), ctx.State, db, out reason));
        }

        [TestCase(1)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void NonZeroParameter_IsRejected_ForPowersWithoutParameter(int parameter)
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, parameter), "parameter");
        }

        [TestCase(-1)]
        [TestCase(5)]
        [TestCase(99)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void InvalidRaceParameter_IsRejected(int parameter)
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.SpawnTribe, 40, 40, 1, parameter), "valid race");
        }

        [Test]
        public void UnknownPower_IsRejected()
        {
            AssertRejected(SecurityTestData.Cmd((GodPowerType)200, 10, 10), "Unknown");
            AssertRejected(SecurityTestData.Cmd((GodPowerType)19, 10, 10), "Unknown");
        }

        [Test]
        public void InsufficientMana_IsRejected()
        {
            _ctx.State.God.Mana = 9f;
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Rain, 22, 22, 2), "mana");
        }

        [Test]
        public void NonFiniteMana_IsRejected()
        {
            _ctx.State.God.Mana = float.NaN;
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10), "unstable");
            _ctx.State.God.Mana = float.PositiveInfinity;
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10), "unstable");
        }

        [Test]
        public void Cooldown_IsEnforced_AndElapses()
        {
            _ctx.State.God.CooldownUntil[(int)GodPowerType.Rain] = _ctx.State.Tick + 10;
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Rain, 22, 22, 2), "recharging");
            _ctx.State.Tick += 10;
            Assert.IsTrue(Check(SecurityTestData.Cmd(GodPowerType.Rain, 22, 22, 2), out string reason), reason);
        }

        [Test]
        public void WaterTarget_IsRejected_WhenLandRequired()
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Lightning, SecurityTestData.OceanFromX + 2, 10, 1), "land");
            // A power without the land requirement may target water.
            Assert.IsTrue(Check(SecurityTestData.Cmd(GodPowerType.Bless, SecurityTestData.OceanFromX + 2, 10, 1), out string reason), reason);
        }

        [Test]
        public void MissingSettlementInRange_IsRejected()
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Rain, 50, 50, 1), "settlement");
        }

        [Test]
        public void DeadSettlement_DoesNotCount()
        {
            foreach (var s in _ctx.State.Settlements.Items) s.IsAlive = false;
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Rain, 22, 22, 2), "settlement");
        }

        [Test]
        public void SettlementReach_IsRadiusPlusSlack()
        {
            var center = _ctx.State.Settlements.Items[0].Center;
            int radius = 2, reach = radius + GodCommandValidator.SettlementReachSlack;
            Assert.IsTrue(Check(SecurityTestData.Cmd(GodPowerType.Rain, center.X + reach, center.Y, radius), out string reason), reason);
            Assert.IsFalse(Check(SecurityTestData.Cmd(GodPowerType.Rain, center.X + reach + 1, center.Y, radius), out reason));
        }

        [Test]
        public void WorldAgeGate_IsEnforced()
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Meteor, 30, 30, 2), "Iron");
            _civ.Age = AgeId.Iron;
            Assert.IsTrue(Check(SecurityTestData.Cmd(GodPowerType.Meteor, 30, 30, 2), out string reason), reason);
        }

        [Test]
        public void BadSources_AreRejected()
        {
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, null), "source");
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, ""), "source");
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, new string('a', 65)), "too long");
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, new string('a', 10000000)), "too long");
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, "local\n"), "invalid characters");
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, "loécal"), "invalid characters");
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, "net‮work"), "invalid characters");
            AssertRejected(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, "x\0"), "invalid characters");
            Assert.IsTrue(Check(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, new string('a', 64)), out string reason), reason);
            Assert.IsTrue(Check(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, "network:peer-7 ~ok"), out reason), reason);
        }

        [Test]
        public void ReasonsNeverEchoTheSource()
        {
            const string evil = "<script>‮";
            Assert.IsFalse(_v.Validate(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, evil), _ctx.State, _db, out string reason));
            Assert.IsFalse(reason.Contains("script"));
        }

        [Test]
        public void MisconfiguredPowerDefinition_IsRejected()
        {
            var db = new GameDatabase();
            db.Register(new GodPowerDefinition { Type = GodPowerType.Bless, Name = "Bless", ManaCost = float.NaN, MinRadius = 0, MaxRadius = 3 });
            TestDb.FillMissing(db);
            db.Freeze(); // NaN slips past GameDatabase.Validate (NaN < 0 is false)
            var ctx = TestWorlds.CreateContext(db, 64);
            Assert.IsFalse(new GodCommandValidator(null).ValidateForExecution(SecurityTestData.Cmd(GodPowerType.Bless, 5, 5, 1), ctx.State, db, out string reason));
            SecurityTestData.AssertContains("misconfigured", reason);
        }

        [Test]
        public void RateLimit_CapsCommandsPerTick_ThroughTheQueue()
        {
            var queue = new CommandQueue(32, _v);
            for (int i = 0; i < 4; i++)
                Assert.IsTrue(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10), _ctx.State, _db, out string r), r);
            Assert.IsFalse(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10), _ctx.State, _db, out string reason));
            SecurityTestData.AssertContains("Too many", reason);
            _ctx.State.Tick++;
            Assert.IsTrue(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10), _ctx.State, _db, out reason), reason);
        }

        [Test]
        public void InvalidCommands_DoNotConsumeTokens()
        {
            for (int i = 0; i < 50; i++) _v.Validate(SecurityTestData.Cmd(GodPowerType.Bless, -1, -1), _ctx.State, _db, out _);
            Assert.AreEqual(0, _v.RateLimiter.AcceptedInTick(_ctx.State.Tick));
            Assert.AreEqual(0, _v.RateLimiter.TrackedSourceCount);
        }

        [Test]
        public void ValidateForExecution_DoesNotConsumeTokens()
        {
            var c = SecurityTestData.Cmd(GodPowerType.Bless, 10, 10);
            for (int i = 0; i < 100; i++) Assert.IsTrue(_v.ValidateForExecution(c, _ctx.State, _db, out string r), r);
            Assert.AreEqual(0, _v.RateLimiter.AcceptedInTick(_ctx.State.Tick));
        }

        [Test]
        public void ExecutionRevalidation_ViaInterface_DoesNotConsumeTokens()
        {
            var queue = new CommandQueue(32, _v);
            Assert.IsTrue(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10), _ctx.State, _db, out string r), r);
            Assert.IsTrue(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 11, 11), _ctx.State, _db, out r), r);
            Assert.AreEqual(2, _v.PendingAdmissions);
            // The divine module drains and re-validates through ICommandValidator (Sequence > 0 now).
            ICommandValidator asInterface = _v;
            while (queue.TryDequeue(out GodCommand cmd))
            {
                Assert.IsTrue((cmd.Sequence) > (0));
                Assert.IsTrue(asInterface.Validate(cmd, _ctx.State, _db, out r), r);
            }
            Assert.AreEqual(2, _v.RateLimiter.AcceptedInTick(_ctx.State.Tick));
            Assert.AreEqual(0, _v.PendingAdmissions);
            // Two more enqueues fit the cap of 4 only because re-validation was free; the fifth does not.
            Assert.IsTrue(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 12, 12), _ctx.State, _db, out r), r);
            Assert.IsTrue(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 13, 13), _ctx.State, _db, out r), r);
            Assert.IsFalse(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Bless, 14, 14), _ctx.State, _db, out r));
        }

        [Test]
        public void ExecutionRevalidation_StillChecksState()
        {
            var queue = new CommandQueue(32, _v);
            Assert.IsTrue(queue.TryEnqueue(SecurityTestData.Cmd(GodPowerType.Rain, 22, 22, 2), _ctx.State, _db, out string r), r);
            queue.TryDequeue(out GodCommand cmd);
            _ctx.State.God.Mana = 0f; // spent elsewhere between enqueue and execution (TOCTOU)
            Assert.IsFalse(_v.Validate(cmd, _ctx.State, _db, out r));
            SecurityTestData.AssertContains("mana", r);
        }

        [Test]
        public void ForgedSequence_DoesNotBypassRateLimit()
        {
            for (int i = 0; i < 4; i++)
            {
                var forged = SecurityTestData.Cmd(GodPowerType.Bless, 10, 10);
                forged.Sequence = 1000 + i;
                Assert.IsTrue(_v.Validate(forged, _ctx.State, _db, out string r), r);
            }
            var fifth = SecurityTestData.Cmd(GodPowerType.Bless, 10, 10);
            fifth.Sequence = 99;
            Assert.IsFalse(_v.Validate(fifth, _ctx.State, _db, out string reason));
            SecurityTestData.AssertContains("Too many", reason);
        }

        [Test]
        public void ResubmittingTheSameObject_CannotFloodTheQueue()
        {
            var queue = new CommandQueue(32, _v);
            var c = SecurityTestData.Cmd(GodPowerType.Bless, 10, 10);
            int accepted = 0;
            for (int i = 0; i < 32; i++) if (queue.TryEnqueue(c, _ctx.State, _db, out _)) accepted++;
            // 4 tokens buy at most 4 paid admissions + 4 free re-validations.
            Assert.IsTrue((accepted) <= (2 * 4));
        }

        [Test]
        public void ManySources_AreBoundedByGlobalCap()
        {
            int accepted = 0;
            for (int i = 0; i < 1000; i++)
                if (_v.Validate(SecurityTestData.Cmd(GodPowerType.Bless, 10, 10, 1, 0, "network:peer" + i), _ctx.State, _db, out _)) accepted++;
            Assert.AreEqual(4, accepted);
            Assert.IsTrue((_v.RateLimiter.TrackedSourceCount) <= (RateLimiter.MaxTrackedSources));
        }

        [Test]
        public void Constructor_ClampsConfig_WithoutMutatingIt()
        {
            var cfg = new SimConfig { MaxCommandsPerTick = int.MaxValue };
            var v = new GodCommandValidator(cfg);
            Assert.AreEqual(64, v.MaxCommandsPerTick);
            Assert.AreEqual(int.MaxValue, cfg.MaxCommandsPerTick);
            Assert.AreEqual(1, new GodCommandValidator(new SimConfig { MaxCommandsPerTick = -5 }).MaxCommandsPerTick);
            Assert.AreEqual(new SimConfig().MaxCommandsPerTick, new GodCommandValidator(null).MaxCommandsPerTick);
        }

        [Test]
        public void StubDatabase_AcceptsSimpleCommand()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, 64);
            var v = new GodCommandValidator(ctx.Config);
            Assert.IsTrue(v.Validate(SecurityTestData.Cmd(GodPowerType.Heal, 3, 3, 0), ctx.State, db, out string reason), reason);
        }
    }
}
