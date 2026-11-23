using GodSim.Core;
using GodSim.Core.Divine;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Divine.Powers
{
    public class GodPowerCatalogTests
    {
        [Test]
        public void Register_DefinesEveryPowerWithSaneNumbers()
        {
            var db = PowersTestKit.CreateDb();
            foreach (var type in EnumUtil.AllGodPowers)
            {
                var d = db.GodPower(type);
                Assert.AreEqual(type, d.Type);
                Assert.IsFalse(string.IsNullOrEmpty(d.Name), type + " name");
                Assert.IsTrue(d.Description.Length > 40, type + " description");
                Assert.IsTrue(d.ManaCost > 0f && d.ManaCost <= 500f, type + " mana");
                Assert.IsTrue(d.CooldownTicks >= 1, type + " cooldown");
                Assert.IsTrue(d.MinRadius >= 0 && d.MaxRadius >= d.MinRadius && d.MaxRadius <= 32, type + " radius");
                Assert.IsTrue(d.AlignmentDelta >= -0.1f && d.AlignmentDelta <= 0.1f, type + " alignment");
                Assert.AreEqual(type == GodPowerType.SpawnTribe, d.UsesRaceParameter, type + " race param");
                if (d.Category == GodPowerCategory.Blessing) Assert.IsTrue(d.AlignmentDelta > 0f, type + " blessing is benevolent");
                if (d.Category == GodPowerCategory.Wrath) Assert.IsTrue(d.AlignmentDelta < 0f, type + " wrath is wrathful");
            }
            Assert.AreEqual(GodPowerCategory.Creation, db.GodPower(GodPowerType.SpawnTribe).Category);
            Assert.IsTrue(db.GodPower(GodPowerType.SpawnTribe).RequiresLandTarget);
            Assert.IsTrue(db.GodPower(GodPowerType.Inspire).RequiresSettlementInRange);
            Assert.IsTrue(db.GodPower(GodPowerType.Prophet).RequiresSettlementInRange);
            Assert.AreEqual(120f, db.GodPower(GodPowerType.Meteor).ManaCost);
            Assert.AreEqual(960, db.GodPower(GodPowerType.SpawnTribe).CooldownTicks);
        }

        [Test]
        public void CreateAll_CoversEveryEnumValue()
        {
            var all = GodPowerCatalog.CreateAll();
            Assert.AreEqual(Counts.GodPowers, all.Length);
            for (int i = 0; i < all.Length; i++) Assert.AreEqual((GodPowerType)i, all[i].Type);
        }
    }

    public class GodCommandSystemTests
    {
        private SimContext _ctx;
        private GodCommandSystem _sys;
        private DivineEventLog _log;

        [SetUp]
        public void SetUp()
        {
            _ctx = PowersTestKit.CreateContext();
            _sys = new GodCommandSystem();
            _sys.Initialize(_ctx);
            _log = new DivineEventLog(_ctx.Events);
        }

        [Test]
        public void Cast_DeductsManaSetsCooldownAndCounts()
        {
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 4, out _);
            _ctx.State.God.Mana = 200f;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Bless, 32, 32, 4), out string reason), reason);
            var god = _ctx.State.God;
            Assert.AreEqual(180f, god.Mana, 1e-4f);
            Assert.AreEqual(_ctx.State.Tick + 24, god.CooldownUntil[(int)GodPowerType.Bless]);
            Assert.AreEqual(1, god.PowersCast);
            Assert.AreEqual(_ctx.State.Tick, god.LastCastTick);
            Assert.AreEqual(1L, _ctx.State.Stats.GodPowersCast);
            Assert.AreEqual(1, _log.Casts.Count);
            Assert.IsTrue(_log.Casts[0].Success);
            Assert.AreEqual(GodPowerType.Bless, _log.Casts[0].Power);
            Assert.AreEqual(4, _log.Casts[0].Radius);
            Assert.IsFalse(string.IsNullOrEmpty(_log.Casts[0].Message));
        }

        [Test]
        public void InsufficientMana_IsRejected_AndNothingCharged()
        {
            _ctx.State.God.Mana = 50f;
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Meteor, 32, 32, 3), out string reason));
            Assert.IsTrue(reason != null && reason.Contains("mana"), reason);
            Assert.AreEqual(50f, _ctx.State.God.Mana);
            Assert.AreEqual(0L, _ctx.State.God.CooldownUntil[(int)GodPowerType.Meteor]);
            Assert.AreEqual(1, _log.Rejections.Count);
            Assert.AreEqual(GodPowerType.Meteor, _log.Rejections[0].Power);
            Assert.AreEqual(0, _log.Casts.Count);
        }

        [Test]
        public void NaNMana_IsRejected()
        {
            _ctx.State.God.Mana = float.NaN;
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Lightning, 5, 5, 0), out _));
        }

        [Test]
        public void Cooldown_RejectsUntilExpired()
        {
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, 10, 10, 4), out _));
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, 20, 20, 4), out string reason));
            Assert.IsTrue(reason != null && reason.Contains("cooldown"), reason);
            _ctx.State.Tick += 11;
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, 20, 20, 4), out _));
            _ctx.State.Tick += 1;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, 20, 20, 4), out reason), reason);
            Assert.AreEqual(2, _log.Rejections.Count);
        }

        [TestCase(2)]
        [TestCase(11)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void BadRadius_IsRejected(int radius)
        {
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Bless, 32, 32, radius), out string reason));
            Assert.IsTrue(reason != null && reason.Contains("radius"), reason);
            Assert.AreEqual(1, _log.Rejections.Count);
        }

        [TestCase(-1, 10)]
        [TestCase(64, 10)]
        [TestCase(10, 64)]
        [TestCase(int.MinValue, int.MinValue)]
        public void OutOfBounds_IsRejected(int x, int y)
        {
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, x, y, 4), out string reason));
            Assert.IsTrue(reason != null && reason.Contains("bounds"), reason);
            Assert.AreEqual(1, _log.Rejections.Count);
            Assert.AreEqual(5000f, _ctx.State.God.Mana);
        }

        [Test]
        public void UnusedParameter_MustBeZero()
        {
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, 10, 10, 4, 3), out string reason));
            Assert.IsTrue(reason != null && reason.Contains("parameter"), reason);
        }

        [Test]
        public void UnknownPower_And_NullCommand_AreRejected()
        {
            Assert.IsFalse(_sys.Execute(_ctx, null, out _));
            Assert.IsFalse(_sys.Execute(_ctx, new GodCommand { Power = (GodPowerType)200, Target = new Vec2i(1, 1) }, out string reason));
            Assert.IsTrue(reason != null && reason.Contains("unknown"), reason);
            Assert.AreEqual(2, _log.Rejections.Count);
        }

        [Test]
        public void SettlementRequired_RejectedOnEmptyLand()
        {
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Inspire, 10, 10, 4), out string reason));
            Assert.IsTrue(reason != null && reason.Contains("settlement"), reason);
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(14, 10), 4, out _);
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Inspire, 10, 10, 4), out reason), reason);
        }

        [Test]
        public void Tick_DrainsQueue_BoundedPerTick_AndRevalidates()
        {
            _ctx.Config.MaxCommandsPerTick = 4;
            for (int i = 0; i < 6; i++)
                Assert.IsTrue(_ctx.Commands.TryEnqueue(PowersTestKit.Cmd(GodPowerType.PlantForest, 5 + i * 8, 20, 2), _ctx.State, _ctx.Db, out string r), r);
            _sys.Tick(_ctx);
            Assert.AreEqual(2, _ctx.Commands.Count, "only 4 commands processed in one tick");
            Assert.AreEqual(1, _log.Casts.Count, "first cast succeeds");
            Assert.AreEqual(3, _log.Rejections.Count, "same-tick repeats hit the cooldown");
            _ctx.State.Tick += 4;
            _sys.Tick(_ctx);
            Assert.AreEqual(0, _ctx.Commands.Count);
            Assert.AreEqual(2, _log.Casts.Count);
        }

        [Test]
        public void HardCapOfEightCommandsPerTick()
        {
            _ctx.Config.MaxCommandsPerTick = 64;
            for (int i = 0; i < 12; i++) _ctx.Commands.TryEnqueue(PowersTestKit.Cmd(GodPowerType.Lightning, 3 + i * 4, 3, 0), _ctx.State, _ctx.Db, out _);
            _sys.Tick(_ctx);
            Assert.AreEqual(12 - GodCommandSystem.MaxCommandsPerTick, _ctx.Commands.Count);
        }

        [Test]
        public void Toctou_ManaSpentAfterEnqueue_IsRejectedAtExecution()
        {
            _ctx.State.God.Mana = 130f;
            Assert.IsTrue(_ctx.Commands.TryEnqueue(PowersTestKit.Cmd(GodPowerType.Meteor, 32, 32, 3), _ctx.State, _ctx.Db, out _));
            _ctx.State.God.Mana = 10f;
            _sys.Tick(_ctx);
            Assert.AreEqual(1, _log.Rejections.Count);
            Assert.IsTrue(_log.Rejections[0].Reason != null && _log.Rejections[0].Reason.Contains("mana"), _log.Rejections[0].Reason);
            Assert.AreEqual(10f, _ctx.State.God.Mana);
        }

        private sealed class DenyAllValidator : ICommandValidator
        {
            public bool Validate(GodCommand command, WorldState state, GameDatabase db, out string reason) { reason = "denied by policy"; return false; }
        }

        [Test]
        public void InstalledValidator_IsConsultedAtExecution()
        {
            _ctx.Commands.TryEnqueue(PowersTestKit.Cmd(GodPowerType.Rain, 10, 10, 4), _ctx.State, _ctx.Db, out _);
            _ctx.Commands.Validator = new DenyAllValidator();
            _sys.Tick(_ctx);
            Assert.AreEqual(1, _log.Rejections.Count);
            Assert.AreEqual("denied by policy", _log.Rejections[0].Reason);
        }

        [Test]
        public void Alignment_MovesAndClamps()
        {
            var god = _ctx.State.God;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, 10, 10, 4), out _));
            Assert.AreEqual(0.01f, god.Alignment, 1e-5f);
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Drought, 30, 30, 4), out _));
            Assert.AreEqual(-0.03f, god.Alignment, 1e-5f);
            god.Alignment = 0.99f;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Bless, 10, 10, 4), out _));
            Assert.AreEqual(1f, god.Alignment);
            god.Alignment = -0.95f;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Meteor, 50, 50, 3), out _));
            Assert.AreEqual(-1f, god.Alignment);
        }

        [Test]
        public void Blessing_RaisesFavorAndDevotion_WrathLowersFavor()
        {
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 6, out var civ);
            civ.GodFavor = 0f;
            civ.Devotion = 0.5f;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Bless, 32, 32, 4), out _));
            Assert.IsTrue(civ.GodFavor >= 0.05f && civ.GodFavor <= 0.15f, "favor " + civ.GodFavor);
            Assert.IsTrue(civ.Devotion >= 0.52f && civ.Devotion <= 0.55f, "devotion " + civ.Devotion);
            float favor = civ.GodFavor, devotion = civ.Devotion;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Plague, 32, 32, 4), out _));
            Assert.IsTrue(favor - civ.GodFavor >= 0.1f - 1e-5f && favor - civ.GodFavor <= 0.2f + 1e-5f, "favor drop");
            Assert.AreEqual(devotion, civ.Devotion, 1e-6f, "stub race piety 0.5 is not pious");
        }

        [Test]
        public void Wrath_MakesPiousRacesMoreDevoted()
        {
            var saurian = new RaceDefinition { Id = RaceId.Saurian, Name = "Saurian", PluralName = "Saurians", Adjective = "Saurian", Piety = 0.9f };
            for (int b = 0; b < Counts.Biomes; b++) saurian.BiomeHabitability[b] = 0.6f;
            saurian.RaceAffinity[(int)RaceId.Saurian] = 1f;
            var ctx = PowersTestKit.CreateContext(PowersTestKit.CreateDb(saurian));
            var sys = new GodCommandSystem();
            TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(32, 32), 6, out var civ);
            civ.Devotion = 0.5f;
            Assert.IsTrue(sys.Execute(ctx, PowersTestKit.Cmd(GodPowerType.Earthquake, 32, 32, 4), out string reason), reason);
            Assert.IsTrue(civ.Devotion > 0.5f, "fearful devotion");
            Assert.IsTrue(civ.GodFavor < 0f);
        }

        [Test]
        public void MomentousCasts_AreChronicled()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 6, out _);
            int before = _ctx.State.Chronicle.Entries.Count;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Meteor, 32, 32, 4), out _));
            var e = _ctx.State.Chronicle.Entries[_ctx.State.Chronicle.Entries.Count - 1];
            Assert.IsTrue(_ctx.State.Chronicle.Entries.Count > before);
            Assert.AreEqual(ChronicleCategory.Divine, e.Category);
            Assert.AreEqual("A fiery star fell upon " + s.Name + ".", e.Text);
            Assert.AreEqual(3, e.Importance);

            int count = _ctx.State.Chronicle.Entries.Count;
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Rain, 10, 10, 4), out _));
            Assert.AreEqual(count, _ctx.State.Chronicle.Entries.Count, "minor casts are not chronicled");
        }

        [Test]
        public void Deterministic_SameSeedSameCommands_SameWorld()
        {
            Assert.AreEqual(RunScript(11), RunScript(11));
            Assert.AreNotEqual(RunScript(11), RunScript(12));
        }

        private static string RunScript(ulong seed)
        {
            var ctx = PowersTestKit.CreateContext(null, Biome.Grassland, seed);
            var sys = new GodCommandSystem();
            sys.Initialize(ctx);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 12, out _);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(44, 44), 12, out _);
            var script = new[]
            {
                PowersTestKit.Cmd(GodPowerType.Fertility, 20, 20, 4),
                PowersTestKit.Cmd(GodPowerType.RevealOre, 30, 30, 5),
                PowersTestKit.Cmd(GodPowerType.PlantForest, 10, 40, 6),
                PowersTestKit.Cmd(GodPowerType.Plague, 44, 44, 4),
                PowersTestKit.Cmd(GodPowerType.Earthquake, 20, 20, 6),
                PowersTestKit.Cmd(GodPowerType.Wildfire, 10, 40, 4),
                PowersTestKit.Cmd(GodPowerType.Lightning, 44, 44, 1),
                PowersTestKit.Cmd(GodPowerType.LowerLand, 5, 5, 6),
                PowersTestKit.Cmd(GodPowerType.SpawnTribe, 50, 10, 0, (int)RaceId.Dwarf),
                PowersTestKit.Cmd(GodPowerType.Meteor, 44, 44, 6),
            };
            foreach (var c in script)
            {
                ctx.Commands.TryEnqueue(c, ctx.State, ctx.Db, out _);
                ctx.State.Tick++;
                sys.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
            var sb = new System.Text.StringBuilder();
            sb.Append(ctx.State.God.Mana.ToString("R")).Append('|').Append(ctx.State.God.Alignment.ToString("R")).Append('|');
            sb.Append(ctx.State.People.Count).Append('|').Append(ctx.State.Buildings.Count).Append('|').Append(ctx.State.Civilizations.Count).Append('|');
            foreach (var p in ctx.State.People.Items) sb.Append(p.Id).Append(':').Append(p.Health.ToString("R")).Append(',');
            var map = ctx.State.Map;
            double elev = 0;
            long flags = 0;
            for (int i = 0; i < map.TileCount; i++) { elev += map.Elevation[i] * (i % 7 + 1); flags += (long)map.Flags[i] * (i % 13 + 1); }
            sb.Append('|').Append(elev.ToString("R")).Append('|').Append(flags).Append('|').Append(ctx.State.Rng.State);
            return sb.ToString();
        }
    }
}
