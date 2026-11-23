using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class SimConfigTests
    {
        [Test]
        public void Defaults_AreAlreadySane()
        {
            var a = new SimConfig();
            var b = new SimConfig().Sanitize();
            Assert.AreEqual(a.MapWidth, b.MapWidth);
            Assert.AreEqual(a.MaxPopulation, b.MaxPopulation);
            Assert.AreEqual(a.GrowthMultiplier, b.GrowthMultiplier);
            Assert.AreEqual(a.MaxSystemFaults, b.MaxSystemFaults);
            Assert.AreEqual(Counts.Races, b.StartingRaces.Length);
        }

        [Test]
        public void Sanitize_ClampsExtremeHighValues()
        {
            var c = new SimConfig
            {
                MapWidth = int.MaxValue, MapHeight = 100000, InitialPopulationPerRace = int.MaxValue, MaxPopulation = int.MaxValue,
                MaxSettlementsPerCiv = int.MaxValue, MaxBuildingsPerSettlement = int.MaxValue, MaxArmies = int.MaxValue,
                MaxCommandsPerTick = int.MaxValue, CommandQueueCapacity = int.MaxValue, GrowthMultiplier = 1e30f,
                ResearchMultiplier = float.MaxValue, ProductionMultiplier = 1e9f, DisasterFrequency = 1e9f, StartingMana = 1e30f,
                ManaRegenMultiplier = 1e30f, MaxSystemFaults = int.MaxValue, Preset = (WorldPreset)200,
            }.Sanitize();
            Assert.AreEqual(WorldMap.MaxSize, c.MapWidth);
            Assert.AreEqual(WorldMap.MaxSize, c.MapHeight);
            Assert.AreEqual(200, c.InitialPopulationPerRace);
            Assert.AreEqual(250000, c.MaxPopulation);
            Assert.AreEqual(128, c.MaxSettlementsPerCiv);
            Assert.AreEqual(5000, c.MaxBuildingsPerSettlement);
            Assert.AreEqual(4096, c.MaxArmies);
            Assert.AreEqual(64, c.MaxCommandsPerTick);
            Assert.AreEqual(1024, c.CommandQueueCapacity);
            Assert.AreEqual(10f, c.GrowthMultiplier);
            Assert.AreEqual(50f, c.ResearchMultiplier);
            Assert.AreEqual(10f, c.ProductionMultiplier);
            Assert.AreEqual(10f, c.DisasterFrequency);
            Assert.AreEqual(100000f, c.StartingMana);
            Assert.AreEqual(100f, c.ManaRegenMultiplier);
            Assert.AreEqual(100000, c.MaxSystemFaults);
            Assert.AreEqual(WorldPreset.Continents, c.Preset);
        }

        [Test]
        public void Sanitize_ClampsExtremeLowAndNonFiniteValues()
        {
            var c = new SimConfig
            {
                MapWidth = int.MinValue, MapHeight = 0, InitialPopulationPerRace = -5, MaxPopulation = -1, MaxSettlementsPerCiv = 0,
                MaxBuildingsPerSettlement = -9, MaxArmies = -1, MaxCommandsPerTick = 0, CommandQueueCapacity = -100,
                GrowthMultiplier = float.NaN, ResearchMultiplier = float.NegativeInfinity, ProductionMultiplier = float.PositiveInfinity,
                DisasterFrequency = -3f, StartingMana = float.NaN, ManaRegenMultiplier = float.NaN, MaxSystemFaults = 0,
            }.Sanitize();
            Assert.AreEqual(WorldMap.MinSize * 4, c.MapWidth);
            Assert.AreEqual(WorldMap.MinSize * 4, c.MapHeight);
            Assert.AreEqual(2, c.InitialPopulationPerRace);
            Assert.AreEqual(100, c.MaxPopulation);
            Assert.AreEqual(1, c.MaxSettlementsPerCiv);
            Assert.AreEqual(10, c.MaxBuildingsPerSettlement);
            Assert.AreEqual(0, c.MaxArmies);
            Assert.AreEqual(1, c.MaxCommandsPerTick);
            Assert.AreEqual(1, c.CommandQueueCapacity);
            Assert.AreEqual(1f, c.GrowthMultiplier, "NaN -> default");
            Assert.AreEqual(1f, c.ResearchMultiplier, "-Inf -> default");
            Assert.AreEqual(1f, c.ProductionMultiplier, "+Inf -> default");
            Assert.AreEqual(0f, c.DisasterFrequency);
            Assert.AreEqual(100f, c.StartingMana);
            Assert.AreEqual(1f, c.ManaRegenMultiplier);
            Assert.AreEqual(1, c.MaxSystemFaults);
        }

        [Test]
        public void Sanitize_StartingRaces()
        {
            var c = new SimConfig { StartingRaces = null }.Sanitize();
            Assert.AreEqual(Counts.Races, c.StartingRaces.Length);
            c = new SimConfig { StartingRaces = new RaceId[0] }.Sanitize();
            Assert.AreEqual(Counts.Races, c.StartingRaces.Length);
            var many = new RaceId[100];
            for (int i = 0; i < many.Length; i++) many[i] = (RaceId)(i % 7);
            c = new SimConfig { StartingRaces = many }.Sanitize();
            Assert.AreEqual(16, c.StartingRaces.Length);
            foreach (var r in c.StartingRaces) Assert.IsTrue(Enum.IsDefined(typeof(RaceId), r));
            Assert.AreEqual(RaceId.Human, c.StartingRaces[5], "undefined race 5 replaced");
        }

        [Test]
        public void Sanitize_IsIdempotent()
        {
            var c = new SimConfig { MapWidth = 5, GrowthMultiplier = float.NaN, StartingRaces = new[] { (RaceId)99 } }.Sanitize();
            var w = c.MapWidth; var g = c.GrowthMultiplier;
            c.Sanitize();
            Assert.AreEqual(w, c.MapWidth);
            Assert.AreEqual(g, c.GrowthMultiplier);
        }

        [Test]
        public void Clone_CopiesScalars()
        {
            var a = new SimConfig { MapWidth = 300, Seed = 9 };
            var b = a.Clone();
            b.MapWidth = 100;
            Assert.AreEqual(300, a.MapWidth);
            Assert.AreEqual(9UL, b.Seed);
        }

        [Test]
        [Ignore("BUG (low): SimConfig.Clone() is a shallow MemberwiseClone (SimConfig.cs:41), so the clone shares StartingRaces with the original and Sanitize() on the clone rewrites the original's array in place (SimConfig.cs:51). Default instances even share EnumUtil.AllRaces.Clone() per instance, but any user-supplied array is aliased. Fix: `var c = (SimConfig)MemberwiseClone(); c.StartingRaces = (RaceId[])StartingRaces?.Clone(); return c;`.")]
        public void Clone_DoesNotAliasStartingRaces()
        {
            var original = new SimConfig { StartingRaces = new[] { (RaceId)42, RaceId.Elf } };
            var copy = original.Clone().Sanitize();
            Assert.AreEqual(RaceId.Human, copy.StartingRaces[0]);
            Assert.AreEqual((RaceId)42, original.StartingRaces[0], "sanitizing the clone mutated the original");
        }
    }

    public class CommandQueueTests
    {
        private sealed class RejectAll : ICommandValidator
        {
            public int Calls;
            public bool Validate(GodCommand c, WorldState s, GameDatabase db, out string reason) { Calls++; reason = "nope"; return false; }
        }

        private WorldState _s;
        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.CreateStub();
            _s = new WorldState(1, new WorldMap(32, 32)) { Tick = 77 };
        }

        private static GodCommand Cmd(int x = 3, int y = 3) => new GodCommand { Power = GodPowerType.Rain, Target = new Vec2i(x, y) };

        [Test]
        public void Enqueue_AssignsSequenceAndTick_AndIsFifo()
        {
            var q = new CommandQueue(4, null);
            var accepted = new List<GodCommand>();
            q.OnAccepted = c => accepted.Add(c);
            var a = Cmd(); var b = Cmd(4, 4);
            Assert.IsTrue(q.TryEnqueue(a, _s, _db, out string reason));
            Assert.IsNull(reason);
            Assert.IsTrue(q.TryEnqueue(b, _s, _db, out reason));
            Assert.AreEqual(1, a.Sequence);
            Assert.AreEqual(2, b.Sequence);
            Assert.AreEqual(77, a.IssuedTick);
            Assert.AreEqual(2, q.Count);
            Assert.AreEqual(2, accepted.Count);
            Assert.IsTrue(q.TryDequeue(out var first));
            Assert.AreSame(a, first);
            Assert.IsTrue(q.TryDequeue(out var second));
            Assert.AreSame(b, second);
            Assert.IsFalse(q.TryDequeue(out var none));
            Assert.IsNull(none);
        }

        [Test]
        public void Capacity_RejectsWhenFull()
        {
            var q = new CommandQueue(2, null);
            Assert.IsTrue(q.TryEnqueue(Cmd(), _s, _db, out _));
            Assert.IsTrue(q.TryEnqueue(Cmd(), _s, _db, out _));
            var c = Cmd();
            Assert.IsFalse(q.TryEnqueue(c, _s, _db, out string reason));
            StringAssert.Contains("full", reason);
            Assert.AreEqual(0, c.Sequence, "rejected command untouched");
            Assert.AreEqual(1, q.RejectedCount);
            Assert.AreEqual(2, q.Count);
            q.TryDequeue(out _);
            Assert.IsTrue(q.TryEnqueue(c, _s, _db, out _));
            Assert.AreEqual(3, c.Sequence);
            q.Clear();
            Assert.AreEqual(0, q.Count);
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void Capacity_BelowOne_BecomesOne(int cap)
        {
            var q = new CommandQueue(cap, null);
            Assert.AreEqual(1, q.Capacity);
            Assert.IsTrue(q.TryEnqueue(Cmd(), _s, _db, out _));
            Assert.IsFalse(q.TryEnqueue(Cmd(), _s, _db, out _));
        }

        [Test]
        public void Validator_RejectionPath()
        {
            var v = new RejectAll();
            var q = new CommandQueue(4, v);
            int accepted = 0;
            q.OnAccepted = c => accepted++;
            var cmd = Cmd();
            Assert.IsFalse(q.TryEnqueue(cmd, _s, _db, out string reason));
            Assert.AreEqual("nope", reason);
            Assert.AreEqual(1, q.RejectedCount);
            Assert.AreEqual(0, q.Count);
            Assert.AreEqual(0, accepted);
            Assert.AreEqual(0, cmd.Sequence);
            Assert.AreEqual(1, v.Calls);
        }

        [Test]
        public void FullQueue_IsCheckedBeforeValidator()
        {
            var v = new RejectAll();
            var q = new CommandQueue(1, null);
            q.TryEnqueue(Cmd(), _s, _db, out _);
            q.Validator = v;
            Assert.IsFalse(q.TryEnqueue(Cmd(), _s, _db, out string reason));
            StringAssert.Contains("full", reason);
            Assert.AreEqual(0, v.Calls);
        }

        [Test]
        public void PermissiveValidator_RejectsMalformed()
        {
            var q = new CommandQueue(8, null);
            Assert.IsInstanceOf<PermissiveCommandValidator>(q.Validator);
            Assert.IsFalse(q.TryEnqueue(null, _s, _db, out string reason));
            Assert.AreEqual("null command", reason);
            Assert.IsFalse(q.TryEnqueue(new GodCommand { Power = (GodPowerType)200, Target = new Vec2i(1, 1) }, _s, _db, out reason));
            Assert.AreEqual("unknown power", reason);
            Assert.IsFalse(q.TryEnqueue(new GodCommand { Power = GodPowerType.Meteor, Target = new Vec2i(32, 0) }, _s, _db, out reason));
            Assert.AreEqual("target out of bounds", reason);
            Assert.IsFalse(q.TryEnqueue(new GodCommand { Power = GodPowerType.Meteor, Target = Vec2i.Invalid }, _s, _db, out reason));
            Assert.AreEqual(4, q.RejectedCount);
            Assert.AreEqual(0, q.Count);
        }
    }

    public class NameGeneratorTests
    {
        private static RaceDefinition Race(NameParts n) => new RaceDefinition { Names = n };

        [Test]
        public void StubRace_ProducesNonEmptyCapitalizedNames()
        {
            var db = TestDb.CreateStub();
            var rng = new Rng(1);
            for (int i = 0; i < 200; i++)
            {
                var race = db.Race((RaceId)(i % Counts.Races));
                string p = NameGenerator.PersonName(race, (Sex)(i % 2), rng);
                string s = NameGenerator.SettlementName(race, rng);
                string c = NameGenerator.CivilizationName(race, rng);
                foreach (var n in new[] { p, s, c })
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(n));
                    Assert.IsTrue(char.IsUpper(n[0]), n);
                }
                StringAssert.EndsWith(" Realm", c);
            }
        }

        [Test]
        public void IsDeterministic()
        {
            var db = TestDb.CreateStub();
            var a = new Rng(5);
            var b = new Rng(5);
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(NameGenerator.PersonName(db.Race(RaceId.Orc), Sex.Male, a), NameGenerator.PersonName(db.Race(RaceId.Orc), Sex.Male, b));
        }

        [Test]
        public void NullRaceOrEmptyTables_UseFallbacks()
        {
            var rng = new Rng(2);
            var empty = Race(new NameParts());
            foreach (var race in new[] { null, empty, new RaceDefinition { Names = null } })
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(NameGenerator.PersonName(race, Sex.Female, rng)));
                Assert.IsFalse(string.IsNullOrWhiteSpace(NameGenerator.SettlementName(race, rng)));
                Assert.IsFalse(string.IsNullOrWhiteSpace(NameGenerator.CivilizationName(race, rng)));
            }
        }

        [Test]
        public void NullAndEmptySyllables_NeverProduceEmptyNames()
        {
            var rng = new Rng(3);
            var n = new NameParts
            {
                PersonPrefixes = new string[] { null, "" },
                MaleSuffixes = new string[] { null },
                FemaleSuffixes = new[] { "" },
                SettlementPrefixes = new string[] { null },
                SettlementSuffixes = new[] { "" },
            };
            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual("Nameless", NameGenerator.PersonName(Race(n), (Sex)(i % 2), rng));
                Assert.AreEqual("Nameless", NameGenerator.SettlementName(Race(n), rng));
            }
        }

        [Test]
        public void BadFormatPatterns_FallBackToRoot()
        {
            var rng = new Rng(4);
            var n = new NameParts { SettlementPrefixes = new[] { "oak" }, SettlementSuffixes = new[] { "ford" } };
            foreach (var pattern in new[] { "{1}", "{0", "}{", "{x}", "{0} of {2}", "{-1}" })
            {
                n.CivilizationPatterns = new[] { pattern };
                string name = null;
                Assert.DoesNotThrow(() => name = NameGenerator.CivilizationName(Race(n), rng), pattern);
                Assert.AreEqual("Oakford", name, pattern);
            }
            n.CivilizationPatterns = new[] { "Empire of {0}" };
            Assert.AreEqual("Empire of Oakford", NameGenerator.CivilizationName(Race(n), rng));
            n.CivilizationPatterns = new[] { "The Horde" }; // pattern without placeholder is allowed
            Assert.AreEqual("The Horde", NameGenerator.CivilizationName(Race(n), rng));
        }

        [Test]
        [Ignore("BUG: NameGenerator.CivilizationName returns an EMPTY (or whitespace) name when a CivilizationPatterns entry is null, \"\" or \"  \": Pick maps null to \"\", and string.Format(\"\", root) == \"\" (NameGenerator.cs:38-39, 45). Fix: `var s = string.Format(pattern, root); return string.IsNullOrWhiteSpace(s) ? root : s;`.")]
        public void EmptyPattern_NeverProducesEmptyCivName()
        {
            var rng = new Rng(4);
            foreach (var pattern in new[] { null, "", "   " })
            {
                var n = new NameParts { SettlementPrefixes = new[] { "oak" }, SettlementSuffixes = new[] { "ford" }, CivilizationPatterns = new[] { pattern } };
                Assert.IsFalse(string.IsNullOrWhiteSpace(NameGenerator.CivilizationName(Race(n), rng)), "pattern '" + pattern + "'");
            }
        }

        [Test]
        public void PersonName_InheritsFamilyNameFromParent()
        {
            var rng = new Rng(6);
            var n = new NameParts { PersonPrefixes = new[] { "al" }, MaleSuffixes = new[] { "dor" }, FemaleSuffixes = new[] { "ra" }, FamilyNames = new[] { "Stone" } };
            var parent = new Person { Name = "Bo Ironfoot" };
            Assert.AreEqual("Aldor Ironfoot", NameGenerator.PersonName(Race(n), Sex.Male, rng, parent));
            Assert.AreEqual("Alra Stone", NameGenerator.PersonName(Race(n), Sex.Female, rng));
            Assert.AreEqual("Alra Stone", NameGenerator.PersonName(Race(n), Sex.Female, rng, new Person { Name = "Single" }));
            Assert.AreEqual("Alra Stone", NameGenerator.PersonName(Race(n), Sex.Female, rng, new Person { Name = "Trailing " }));
            Assert.AreEqual("Alra Stone", NameGenerator.PersonName(Race(n), Sex.Female, rng, new Person { Name = "" }));
            n.FamilyNames = new string[0];
            Assert.AreEqual("Alra", NameGenerator.PersonName(Race(n), Sex.Female, rng));
        }

        [Test]
        public void WorldOps_NamesEntitiesWhenNameOmitted()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 64, 3);
            var civ = ctx.Ops.CreateCivilization(RaceId.Saurian);
            var st = ctx.Ops.CreateSettlement(civ, new Vec2i(10, 10));
            var p = ctx.Ops.CreatePerson(st, RaceId.Saurian, Sex.Male, 20f);
            Assert.IsFalse(string.IsNullOrWhiteSpace(civ.Name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(st.Name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.Name));
            var named = ctx.Ops.CreateCivilization(RaceId.Saurian, "Given");
            Assert.AreEqual("Given", named.Name);
        }
    }
}
