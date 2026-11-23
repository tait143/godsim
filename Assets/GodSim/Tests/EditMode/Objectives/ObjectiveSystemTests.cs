using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Objectives;
using GodSim.Tests.Shared;
using NUnit.Framework;
using static GodSim.Tests.Objectives.ObjectiveTestUtil;

namespace GodSim.Tests.Objectives
{
    public class ObjectiveCatalogTests
    {
        [Test]
        public void Catalog_HasFortyPlusValidObjectives()
        {
            Assert.That(ObjectiveCatalog.All.Count, Is.GreaterThanOrEqualTo(40));
            var errors = ObjectiveCatalog.Validate();
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        }

        [TestCase("first_light")] [TestCase("bronze_shepherd")] [TestCase("thousand_faithful")] [TestCase("five_peoples_thrive")]
        [TestCase("city_of_gods")] [TestCase("wonder_of_the_world")] [TestCase("racial_pride_human")] [TestCase("racial_pride_elf")]
        [TestCase("racial_pride_dwarf")] [TestCase("racial_pride_orc")] [TestCase("racial_pride_saurian")] [TestCase("ascension")]
        [TestCase("merciful")] [TestCase("wrathful")] [TestCase("rain_maker")] [TestCase("kingmaker")] [TestCase("peacekeeper")]
        [TestCase("witness_to_war")] [TestCase("phoenix_rising")] [TestCase("prophets_legacy")]
        public void Catalog_ContainsDesignedObjective(string key)
        {
            Assert.IsNotNull(ObjectiveCatalog.Find(key), key);
        }

        [Test]
        public void Catalog_HasAMilestoneForEveryAgeAfterDawn()
        {
            for (int a = 1; a < Counts.Ages; a++)
            {
                bool found = false;
                foreach (var d in ObjectiveCatalog.All)
                    if (d.Kind == ObjectiveKind.CivilizationAge && d.AgeFilter == (AgeId)a && !d.RaceFilter.HasValue && d.Target <= 1f) found = true;
                Assert.IsTrue(found, "no milestone for " + (AgeId)a);
            }
        }

        [Test]
        public void Catalog_MemoryKeysFollowConvention()
        {
            var d = ObjectiveCatalog.Find("rain_maker");
            Assert.AreEqual("obj.rain_maker.progress", d.ProgressKey);
            Assert.AreEqual("obj.rain_maker.done", d.DoneKey);
            Assert.AreEqual("obj.count.rain", ObjectiveKeys.PowerCount(GodPowerType.Rain));
        }
    }

    public class ObjectiveSystemTests
    {
        [Test]
        public void EmptyWorld_NothingThrows_AndProgressKeysAreWritten()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub());
            var sys = new ObjectiveSystem();
            Assert.DoesNotThrow(() => sys.Tick(null));
            Assert.DoesNotThrow(() => sys.Tick(ctx)); // before Initialize
            Assert.DoesNotThrow(() => TestWorlds.Run(sys, ctx, 24 * 60));
            Assert.AreEqual(0, ObjectiveQueries.CompletedCount(ctx.State));
            foreach (var d in ObjectiveCatalog.All) Assert.IsTrue(ctx.State.Memory.ContainsKey(d.ProgressKey), d.Key);
            Assert.AreEqual("Objectives", sys.Name);
            Assert.AreEqual(24, sys.TickInterval);
        }

        [Test]
        public void RainMaker_ProgressIncrements_AndRewardIsGrantedExactlyOnce()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            s.God.Mana = 0f;
            Cast(ctx, GodPowerType.Rain, 10);
            Cast(ctx, GodPowerType.Rain, 3, success: false);
            var before = Times(s); var god = CopyGod(s.God);
            Evaluate(sys, ctx);
            AssertRewardsExact(s, before, god);
            Assert.AreEqual(10f, Mem(s, "obj.count.rain"));
            Assert.AreEqual(0.4f, ObjectiveQueries.Progress(s, "rain_maker"), 1e-4f);
            Assert.AreEqual(0.4f, Mem(s, "obj.rain_maker.progress"), 1e-4f);
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "rain_maker"));
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "first_miracle"));

            Cast(ctx, GodPowerType.Rain, 15);
            before = Times(s); god = CopyGod(s.God);
            Evaluate(sys, ctx);
            var newly = AssertRewardsExact(s, before, god);
            CollectionAssert.AreEqual(new[] { "rain_maker" }, newly);
            Assert.AreEqual(1f, Mem(s, "obj.rain_maker.done"));
            Assert.AreEqual(1f, ObjectiveQueries.Progress(s, "rain_maker"));
            Assert.AreEqual(1, ChronicleCount(s, "The heavens rejoice: Rain Maker fulfilled."));
            foreach (var e in s.Chronicle.Entries)
                if (e.Text.Contains("Rain Maker")) { Assert.That(e.Importance, Is.InRange(2, 3)); Assert.AreEqual(ChronicleCategory.Divine, e.Category); }
        }

        [Test]
        public void NonRepeatable_NeverRecompletes()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            Cast(ctx, GodPowerType.Rain, 25);
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "rain_maker"));
            float mana = s.God.Mana, maxMana = s.God.MaxMana, align = s.God.Alignment;

            Cast(ctx, GodPowerType.Rain, 24);                 // 49 total: another 24 rains (and below steward_of_skies' 50)
            s.Memory["obj.rain_maker.progress"] = 0f;         // even with tampered progress
            Evaluate(sys, ctx, 5);
            Assert.AreEqual(1f, Mem(s, "obj.rain_maker.done"));
            Assert.AreEqual(1, ChronicleCount(s, "Rain Maker fulfilled"));
            Assert.AreEqual(1, ObjectiveQueries.TimesCompleted(s, "rain_maker"));
            Assert.AreEqual(mana, s.God.Mana, 1e-4f);
            Assert.AreEqual(maxMana, s.God.MaxMana, 1e-4f);
            Assert.AreEqual(align, s.God.Alignment, 1e-6f);
        }

        [Test]
        public void Repeatable_CompletesOncePerTargetIncrement()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            Cast(ctx, GodPowerType.Bless, 25);
            var before = Times(s); var god = CopyGod(s.God);
            Evaluate(sys, ctx);
            Assert.AreEqual(1, ObjectiveQueries.TimesCompleted(s, "generous_hand"));
            Evaluate(sys, ctx);
            Assert.AreEqual(2, ObjectiveQueries.TimesCompleted(s, "generous_hand"));
            Evaluate(sys, ctx, 3);
            Assert.AreEqual(2, ObjectiveQueries.TimesCompleted(s, "generous_hand"));
            Assert.AreEqual(0.5f, ObjectiveQueries.Progress(s, "generous_hand"), 1e-4f);
            AssertRewardsExact(s, before, god);
            Cast(ctx, GodPowerType.Bless, 5);
            Evaluate(sys, ctx);
            Assert.AreEqual(3, ObjectiveQueries.TimesCompleted(s, "generous_hand"));
            Assert.AreEqual(1, ChronicleCount(s, "Generous Hand fulfilled again (3 times)"));
        }

        [Test]
        public void Persistence_ProgressResumesFromMemoryKeys_AfterSimulatedLoad()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            Cast(ctx, GodPowerType.Rain, 20);
            Evaluate(sys, ctx);
            Assert.AreEqual(0.8f, Mem(s, "obj.rain_maker.progress"), 1e-4f);
            Assert.AreEqual(1f, Mem(s, "obj.first_miracle.done"));

            // "Load": brand-new world state + bus + system, restored only from what a save keeps.
            var ctx2 = TestWorlds.CreateContext(ctx.Db);
            var s2 = ctx2.State;
            s2.Tick = s.Tick;
            foreach (var kv in s.Memory) s2.Memory[kv.Key] = kv.Value;
            s2.God.Mana = s.God.Mana; s2.God.MaxMana = s.God.MaxMana; s2.God.Alignment = s.God.Alignment;
            var sys2 = new ObjectiveSystem();
            sys2.Initialize(ctx2);

            var before = Times(s2); var god = CopyGod(s2.God);
            Evaluate(sys2, ctx2);
            CollectionAssert.IsEmpty(AssertRewardsExact(s2, before, god)); // nothing re-granted
            Assert.AreEqual(0, ChronicleCount(s2, "First Miracle"));
            Assert.AreEqual(0.8f, ObjectiveQueries.Progress(s2, "rain_maker"), 1e-4f);

            Cast(ctx2, GodPowerType.Rain, 5);
            before = Times(s2); god = CopyGod(s2.God);
            Evaluate(sys2, ctx2);
            CollectionAssert.AreEqual(new[] { "rain_maker" }, AssertRewardsExact(s2, before, god));
            Assert.AreEqual(25f, Mem(s2, "obj.count.rain"));
        }

        [Test]
        public void Reinitialize_DoesNotDoubleCount_AndDetachStopsCounting()
        {
            var ctx = NewContext(out var sys);
            sys.Initialize(ctx);
            Cast(ctx, GodPowerType.Rain);
            Assert.AreEqual(1f, Mem(ctx.State, "obj.count.rain"));
            sys.Detach();
            Cast(ctx, GodPowerType.Rain);
            Assert.AreEqual(1f, Mem(ctx.State, "obj.count.rain"));
        }

        [Test]
        public void SpawnTribe_TagsTheCivFoundedInTheSameTick_InEitherEventOrder()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            var native = ctx.Ops.CreateCivilization(RaceId.Human);   // world-start civ, tick 0

            s.Tick = 10;
            var spawned = ctx.Ops.CreateCivilization(RaceId.Elf);     // effect publishes CivilizationFoundedEvent first
            Cast(ctx, GodPowerType.SpawnTribe, parameter: (int)RaceId.Elf);
            Assert.AreEqual(1f, spawned.Mem(ObjectiveKeys.SpawnedByGod));
            Assert.AreEqual(0f, native.Mem(ObjectiveKeys.SpawnedByGod));

            s.Tick = 20;
            Cast(ctx, GodPowerType.SpawnTribe, parameter: (int)RaceId.Orc); // cast event first
            var spawned2 = ctx.Ops.CreateCivilization(RaceId.Orc);
            Assert.AreEqual(1f, spawned2.Mem(ObjectiveKeys.SpawnedByGod));

            s.Tick = 30;
            Cast(ctx, GodPowerType.SpawnTribe, parameter: (int)RaceId.Dwarf, success: false);
            var natural = ctx.Ops.CreateCivilization(RaceId.Dwarf);
            s.Tick = 31;
            Cast(ctx, GodPowerType.SpawnTribe, parameter: (int)RaceId.Dwarf); // later tick: no match
            Assert.AreEqual(0f, natural.Mem(ObjectiveKeys.SpawnedByGod));
            s.Tick = 32;
            var later = ctx.Ops.CreateCivilization(RaceId.Dwarf);           // pending from tick 31 must not leak
            Assert.AreEqual(0f, later.Mem(ObjectiveKeys.SpawnedByGod));
            Assert.AreEqual(2f, Mem(s, ObjectiveKeys.CountSpawned));
        }

        [Test]
        public void Kingmaker_RequiresASpawnedCivAtIron()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            var native = ctx.Ops.CreateCivilization(RaceId.Human);
            native.Age = AgeId.Medieval;
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "kingmaker"));
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "keeper_of_keeps"));

            var spawned = ctx.Ops.CreateCivilization(RaceId.Saurian);
            Cast(ctx, GodPowerType.SpawnTribe, parameter: (int)RaceId.Saurian);
            spawned.Age = AgeId.Bronze;
            Evaluate(sys, ctx);
            Assert.AreEqual(0.5f, ObjectiveQueries.Progress(s, "kingmaker"), 1e-4f);
            spawned.Age = AgeId.Iron;
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "kingmaker"));
        }

        [Test]
        public void AscensionGate_SetsVictoryFlag_ChroniclesLegendary_AndSimulationContinues()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            var st = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 4, out var civ);
            var site = ctx.Ops.FindFreeSite(new Vec2i(34, 34), 1, 1, 8);
            var gate = ctx.Ops.CreateBuilding(st, BuildingType.AscensionGate, site, 0, true);
            Assert.IsNotNull(gate);
            Assert.AreEqual(1f, Mem(s, ObjectiveKeys.Ascended));
            Assert.AreEqual((float)civ.Id, Mem(s, ObjectiveKeys.AscendedCivilization));
            Assert.IsTrue(ObjectiveQueries.IsAscended(s));
            bool legendary = false;
            foreach (var e in s.Chronicle.Entries) if (e.Text.StartsWith("Ascension!") && e.Importance == 3) legendary = true;
            Assert.IsTrue(legendary);

            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "ascension"));
            var second = ctx.Ops.CreateBuilding(st, BuildingType.AscensionGate, ctx.Ops.FindFreeSite(new Vec2i(26, 26), 1, 1, 8), 0, true);
            Assert.IsNotNull(second);
            Assert.DoesNotThrow(() => TestWorlds.Run(sys, ctx, 240, false));
            Assert.AreEqual(1, ChronicleCount(s, "Ascension!"));
            Assert.AreEqual(1, ChronicleCount(s, "Ascension fulfilled"));
        }

        [Test]
        public void AscensionGate_CompletedWhileUnsubscribed_IsCaughtByPeriodicScan()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub());
            var st = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(30, 30), 2, out _);
            ctx.Ops.CreateBuilding(st, BuildingType.AscensionGate, ctx.Ops.FindFreeSite(new Vec2i(33, 33), 1, 1, 8), 0, true);
            var sys = new ObjectiveSystem();
            sys.Initialize(ctx);
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsAscended(ctx.State));
            Assert.IsTrue(ObjectiveQueries.IsDone(ctx.State, "ascension"));
        }

        [Test]
        public void SettlementTierAndAgeMilestones_CompleteWithChronicle()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            var st = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 4, out var civ);
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "first_light"));
            st.Tier = SettlementTier.Hamlet;
            civ.Age = AgeId.Bronze;
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "first_light"));
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "bronze_shepherd"));
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "village_green"));
            Assert.AreEqual(0.5f, ObjectiveQueries.Progress(s, "iron_will"), 1e-4f);
            Assert.AreEqual(1, ChronicleCount(s, "The heavens rejoice: First Light fulfilled."));
            Assert.AreEqual(1, ChronicleCount(s, "The heavens rejoice: Bronze Shepherd fulfilled."));

            ctx.Events.Publish(new SettlementTierChangedEvent { SettlementId = st.Id, OldTier = SettlementTier.Town, NewTier = SettlementTier.City });
            Evaluate(sys, ctx); // sticky best tier from the event even though the settlement field says Hamlet
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "city_of_gods"));
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "deep_delvers")); // dwarf-only
        }

        [Test]
        public void Alignment_MercifulAndWrathful_RewardsStayClamped()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            s.God.Alignment = 0.99f;
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "merciful"));
            Assert.That(s.God.Alignment, Is.InRange(-1f, 1f));
            s.God.Alignment = -0.8f;
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "wrathful"));
            Assert.That(s.God.Alignment, Is.InRange(-1f, 1f));
        }

        [Test]
        public void Peacekeeper_HundredYearsWithoutWar()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(10, 10), 2, out var a);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(50, 50), 2, out var b);
            s.Tick = 50 * SimTime.TicksPerYear;
            ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = b.Id, DefenderId = a.Id });
            ctx.Events.Publish(new BattleEvent { AttackerCivId = b.Id, DefenderCivId = a.Id });
            s.Tick = 100 * SimTime.TicksPerYear - sys.TickInterval;
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "peacekeeper"));
            Assert.AreEqual(0.5f, ObjectiveQueries.Progress(s, "peacekeeper"), 1e-3f);
            Assert.AreEqual(0.1f, ObjectiveQueries.Progress(s, "witness_to_war"), 1e-4f);
            s.Tick = 150 * SimTime.TicksPerYear - sys.TickInterval;
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "peacekeeper"));
        }

        [Test]
        public void Peacekeeper_ActiveWarResetsTheClock()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(10, 10), 2, out var a);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(50, 50), 2, out var b);
            s.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = a.Id, DefenderId = b.Id, IsActive = true });
            s.Tick = 200 * SimTime.TicksPerYear;
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "peacekeeper"));
            Assert.AreEqual(0f, ObjectiveQueries.Progress(s, "peacekeeper"), 1e-3f);
        }

        [Test]
        public void Phoenix_MeteorRuinRebuiltNearbyWithinFiftyYears()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            var victim = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 2, out _);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(55, 55), 2, out var elves);
            s.Tick = 100;
            var center = victim.Center;
            ctx.Ops.DestroySettlement(victim, DestroyCause.DivinePower, true);
            Cast(ctx, GodPowerType.Meteor, target: center, radius: 3);
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(100f, Mem(s, ObjectiveKeys.PhoenixSiteTick));

            s.Tick = 100 + 10 * SimTime.TicksPerYear;
            ctx.Ops.FoundSettlement(elves, new Vec2i(center.X + 20, center.Y)); // too far
            Assert.AreEqual(0f, Mem(s, ObjectiveKeys.CountPhoenix));
            ctx.Ops.FoundSettlement(elves, new Vec2i(center.X + 6, center.Y + 4));
            Assert.AreEqual(1f, Mem(s, ObjectiveKeys.CountPhoenix));
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "phoenix_rising"));
        }

        [Test]
        public void Phoenix_TooLateDoesNotCount()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            var victim = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 2, out _);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(55, 55), 2, out var elves);
            s.Tick = 100;
            Cast(ctx, GodPowerType.Meteor, target: victim.Center, radius: 3);  // strike first,
            s.Tick = 150;
            var center = victim.Center;
            ctx.Ops.DestroySettlement(victim, DestroyCause.Abandoned, true);  // abandoned soon after
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(150f, Mem(s, ObjectiveKeys.PhoenixSiteTick));
            s.Tick = 150 + 60 * SimTime.TicksPerYear;
            ctx.Ops.FoundSettlement(elves, new Vec2i(center.X + 3, center.Y));
            Assert.AreEqual(0f, Mem(s, ObjectiveKeys.CountPhoenix));
        }

        [Test]
        public void FivePeoplesThrive_AllRacesAliveAtYear200()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            var spots = new[] { new Vec2i(8, 8), new Vec2i(30, 8), new Vec2i(52, 8), new Vec2i(8, 52), new Vec2i(52, 52) };
            for (int r = 0; r < 4; r++) TestWorlds.AddSettlement(ctx, (RaceId)r, spots[r], 2, out _);
            s.Tick = 200 * SimTime.TicksPerYear;
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "five_peoples_thrive"));
            Assert.AreEqual(0.8f, ObjectiveQueries.Progress(s, "five_peoples_thrive"), 1e-4f);
            TestWorlds.AddSettlement(ctx, RaceId.Saurian, spots[4], 2, out _);
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "five_peoples_thrive"));
        }

        [Test]
        public void FivePeoplesThrive_NotBeforeYear200()
        {
            var ctx = NewContext(out var sys);
            var spots = new[] { new Vec2i(8, 8), new Vec2i(30, 8), new Vec2i(52, 8), new Vec2i(8, 52), new Vec2i(52, 52) };
            for (int r = 0; r < 5; r++) TestWorlds.AddSettlement(ctx, (RaceId)r, spots[r], 2, out _);
            ctx.State.Tick = 100 * SimTime.TicksPerYear;
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.IsDone(ctx.State, "five_peoples_thrive"));
            Assert.AreEqual(0.5f, ObjectiveQueries.Progress(ctx.State, "five_peoples_thrive"), 0.01f);
        }

        [Test]
        public void Wonders_AnyWonderAndRacialPride()
        {
            var db = TestDb.CreateStub(false);
            db.Register(new BuildingDefinition { Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, IsWonder = true });
            db.Register(new BuildingDefinition { Type = BuildingType.WorldTree, Name = "World Tree", Category = BuildingCategory.Wonder, IsWonder = true, AllowedRaces = RaceMask.Elf });
            db.Freeze();
            var ctx = NewContext(out var sys, db);
            var s = ctx.State;
            var hu = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(15, 15), 2, out _);
            var el = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(45, 45), 2, out _);
            ctx.Ops.CreateBuilding(hu, BuildingType.Library, ctx.Ops.FindFreeSite(new Vec2i(18, 18), 1, 1, 8), 0, true);
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "wonder_of_the_world"));
            ctx.Ops.CreateBuilding(hu, BuildingType.GreatLibrary, ctx.Ops.FindFreeSite(new Vec2i(12, 12), 1, 1, 8), 0, true);
            ctx.Ops.CreateBuilding(el, BuildingType.WorldTree, ctx.Ops.FindFreeSite(new Vec2i(48, 48), 1, 1, 8), 0, true);
            Evaluate(sys, ctx);
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "wonder_of_the_world"));
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "keeper_of_scrolls"));
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "racial_pride_elf"));
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "racial_pride_dwarf"));
            Assert.AreEqual(2f / 7f, ObjectiveQueries.Progress(s, "seven_wonders"), 1e-4f);
            Assert.AreEqual(2f, Mem(s, ObjectiveKeys.CountWonders));
        }

        [Test]
        public void Queries_ReportStatusAndHideSecrets()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            int total = 0, visible = 0, hidden = 0;
            foreach (var st in ObjectiveQueries.All(s)) { total++; if (st.IsHidden) hidden++; }
            foreach (var st in ObjectiveQueries.Visible(s)) visible++;
            Assert.AreEqual(ObjectiveCatalog.All.Count, total);
            Assert.That(hidden, Is.GreaterThan(0));
            Assert.AreEqual(total - hidden, visible);
            Assert.AreEqual(0f, ObjectiveQueries.Progress(s, "no_such_objective"));
            Assert.IsFalse(ObjectiveQueries.IsDone(s, "no_such_objective"));
            Assert.IsFalse(ObjectiveQueries.IsDone(null, "rain_maker"));

            Cast(ctx, GodPowerType.Plague, 3);
            Evaluate(sys, ctx);
            Assert.IsFalse(ObjectiveQueries.Status(s, "pestilence").IsHidden);
            Assert.IsTrue(ObjectiveQueries.Status(s, "pestilence").IsDone);
            Assert.AreEqual(2, ObjectiveQueries.CompletedCount(s)); // first_miracle + pestilence
        }

        [Test]
        public void BadValues_NeverThrowOrLeakNaN()
        {
            var ctx = NewContext(out var sys);
            var s = ctx.State;
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(20, 20), 2, out var civ);
            civ.Devotion = float.NaN;
            s.God.Alignment = float.NaN;
            s.God.Mana = float.PositiveInfinity;
            s.God.MaxMana = float.NaN;
            s.Memory["obj.count.rain"] = float.NaN;
            Cast(ctx, (GodPowerType)200);
            ctx.Events.Publish(new BuildingCompletedEvent { BuildingId = -1, Type = (BuildingType)250, SettlementId = -1 });
            ctx.Events.Publish(new BuildingCompletedEvent { BuildingId = -1, Type = BuildingType.AscensionGate, SettlementId = 999 });
            Cast(ctx, GodPowerType.SpawnTribe, parameter: 99);
            Cast(ctx, GodPowerType.Rain, 25);
            Assert.DoesNotThrow(() => Evaluate(sys, ctx, 3));
            Assert.IsTrue(ObjectiveQueries.IsDone(s, "rain_maker"));
            Assert.IsTrue(ObjectiveQueries.IsAscended(s));
            foreach (var kv in s.Memory)
                if (kv.Key.StartsWith("obj.") && kv.Key.EndsWith(".progress"))
                    Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            Assert.IsFalse(float.IsNaN(s.God.Mana) || float.IsInfinity(s.God.Mana));
            Assert.IsFalse(float.IsNaN(s.God.MaxMana) || float.IsInfinity(s.God.MaxMana));
            Assert.IsFalse(float.IsNaN(s.God.Alignment));
        }

        [Test]
        public void Deterministic_SameScriptSameResult()
        {
            Assert.AreEqual(RunScript(7), RunScript(7));
        }

        private static string RunScript(ulong seed)
        {
            var ctx = NewContext(out var sys, null, seed);
            var s = ctx.State;
            var st = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(15, 15), 6, out var a);
            TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(45, 45), 6, out var b);
            for (int i = 0; i < 40; i++)
            {
                Cast(ctx, (GodPowerType)(i % Counts.GodPowers), 1 + i % 3, success: i % 5 != 0, target: new Vec2i(i, i), parameter: i % Counts.Races);
                if (i == 10) { st.Tier = SettlementTier.Village; a.Age = AgeId.Iron; }
                if (i == 20) ctx.Events.Publish(new WarDeclaredEvent { WarId = 1, AttackerId = a.Id, DefenderId = b.Id });
                if (i % 7 == 0) ctx.Events.Publish(new BattleEvent { AttackerCivId = a.Id, DefenderCivId = b.Id });
                s.God.Alignment += 0.03f;
                Evaluate(sys, ctx);
            }
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var k in keys) sb.Append(k).Append('=').Append(s.Memory[k].ToString("R")).Append(';');
            sb.Append(s.God.Mana.ToString("R")).Append('|').Append(s.God.MaxMana.ToString("R")).Append('|').Append(s.God.Alignment.ToString("R"));
            foreach (var e in s.Chronicle.Entries) sb.Append('|').Append(e.Tick).Append(':').Append(e.Text);
            return sb.ToString();
        }
    }
}
