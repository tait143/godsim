using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Storytelling;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Storytelling
{
    public class WorldEventSystemTests
    {
        private const int Runs = 200;

        [Test]
        public void System_HasContractShape()
        {
            var sys = new WorldEventSystem();
            Assert.AreEqual(48, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
        }

        [Test]
        public void Catalog_HasAtLeast25ValidEvents()
        {
            Assert.IsTrue(WorldEventCatalog.All.Count >= 25, "events: " + WorldEventCatalog.All.Count);
            var errors = WorldEventCatalog.Validate();
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            foreach (var d in WorldEventCatalog.All)
            {
                Assert.IsTrue(d.Templates.Length >= 2 && d.Templates.Length <= 4, d.Key);
                Assert.AreSame(d, WorldEventCatalog.Find(d.Key));
                Assert.AreEqual("story.cd." + d.Key, d.CooldownMemoryKey);
            }
            Assert.IsNull(WorldEventCatalog.Find("no_such_event"));
            Assert.IsNull(WorldEventCatalog.Find(null));
        }

        [Test]
        public void StoryText_FillsAllPlaceholders()
        {
            var w = StoryWorld.Create();
            var e = new WorldEventContext();
            e.Reset(w.Ctx, w.A);
            e.Settlement = w.CapA;
            e.OtherCiv = w.B;
            e.OtherSettlement = w.CapB;
            e.Amount = 12.4f;
            e.Count = 3;
            string s = StoryText.Format("{settlement}|{civ}|{race}|{leader}|{other}|{othersettlement}|{person}|{amount}|{count}", e);
            Assert.AreEqual(w.CapA.Name + "|" + w.A.Name + "|Humans|the elders|" + w.B.Name + "|" + w.CapB.Name + "|a wanderer|12|3", s);
            Assert.IsTrue(StoryText.UsesOnlyKnownPlaceholders("The {civ} of {settlement}."));
            Assert.IsFalse(StoryText.UsesOnlyKnownPlaceholders("The {bogus}."));
            Assert.IsFalse(StoryText.UsesOnlyKnownPlaceholders("Broken {civ"));
        }

        [Test]
        public void Cooldown_StoredAsTickInCivMemory_PreventsRepeatsUntilExpired()
        {
            var w = StoryWorld.Create();
            long t0 = w.State.Tick;
            StoryWorld.Fire(w, w.A, WorldEventKeys.WanderingSage);
            Assert.AreEqual((float)t0, w.A.Memory["story.cd." + WorldEventKeys.WanderingSage]);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.WanderingSage));
            Assert.IsTrue(StoryQueries.IsOnCooldown(w.State, w.A.Id, WorldEventKeys.WanderingSage));
            Assert.IsTrue(WorldEventSystem.IsEligible(w.Ctx, w.B, WorldEventKeys.WanderingSage), "per-civ cooldown");
            w.State.Tick = t0 + SimTime.YearsToTicks(20f) - 1;
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.WanderingSage));
            w.State.Tick = t0 + SimTime.YearsToTicks(20f);
            Assert.IsTrue(StoryWorld.TryFire(w, w.A, WorldEventKeys.WanderingSage));
        }

        [Test]
        public void Cooldown_SurvivesSaveLoad_ThroughMemoryBlackboards()
        {
            var w = StoryWorld.Create(5);
            StoryWorld.Fire(w, w.A, WorldEventKeys.CometOmen);
            StoryWorld.Fire(w, w.A, WorldEventKeys.HolyVisions);

            // Simulate a reload: an identical fresh world receives only the serialized Memory dictionaries.
            var loaded = StoryWorld.Create(5);
            foreach (var kv in w.A.Memory) loaded.A.Memory[kv.Key] = kv.Value;
            foreach (var kv in w.State.Memory) loaded.State.Memory[kv.Key] = kv.Value;
            Assert.IsFalse(StoryWorld.TryFire(loaded, loaded.A, WorldEventKeys.HolyVisions));
            Assert.IsFalse(StoryWorld.TryFire(loaded, loaded.B, WorldEventKeys.CometOmen), "world-wide cooldown restored");
            Assert.IsTrue(StoryWorld.TryFire(loaded, loaded.B, WorldEventKeys.HolyVisions));
        }

        [Test]
        public void CorruptCooldownValues_AreIgnored()
        {
            var w = StoryWorld.Create();
            w.A.Memory["story.cd." + WorldEventKeys.WanderingSage] = float.NaN;
            Assert.IsTrue(StoryWorld.TryFire(w, w.A, WorldEventKeys.WanderingSage));
            w.A.Memory["story.cd." + WorldEventKeys.HolyVisions] = 1e12f; // "in the future"
            Assert.IsTrue(StoryWorld.TryFire(w, w.A, WorldEventKeys.HolyVisions));
        }

        [Test]
        public void NothingThrows_OnEmptyWorld_DeadCivs_EmptyCivs_EmptySettlements()
        {
            var db = StoryWorld.CreateDb();
            var empty = TestWorlds.CreateContext(db, 128, 3);
            TestWorlds.Run(new WorldEventSystem(), empty, Runs * 48);

            var w = StoryWorld.Create();
            w.State.Memory[WorldEventSystem.ChanceScaleKey] = 10f;
            var noSettlements = w.Ops.CreateCivilization(RaceId.Orc);
            var hollow = w.Ops.CreateCivilization(RaceId.Dwarf);
            var ghostTown = w.Ops.FoundSettlement(hollow, new Vec2i(60, 100));
            ghostTown.Tier = SettlementTier.Village;
            ghostTown.Unrest = 1f;
            w.B.IsAlive = false;
            foreach (var d in WorldEventCatalog.All)
            {
                Assert.IsFalse(StoryWorld.TryFire(w, w.B, d.Key), "dead civ: " + d.Key);
                Assert.IsFalse(StoryWorld.TryFire(w, noSettlements, d.Key), "no settlements: " + d.Key);
                StoryWorld.TryFire(w, hollow, d.Key); // may fire, must not throw
            }
            Assert.IsFalse(WorldEventSystem.TryFire(null, w.A, WorldEventKeys.CometOmen));
            Assert.IsFalse(WorldEventSystem.TryFire(w.Ctx, null, WorldEventKeys.CometOmen));
            Assert.IsFalse(WorldEventSystem.TryFire(w.Ctx, w.A, null));
            Assert.IsFalse(WorldEventSystem.TryFire(w.Ctx, w.A, "unknown"));
            TestWorlds.Run(new WorldEventSystem(), w.Ctx, Runs * 48);
            new WorldEventSystem().Tick(null);
            StoryWorld.AssertSane(w);
        }

        [Test]
        public void EveryEvent_OnRichWorld_AppliesWithinBounds()
        {
            var w = StoryWorld.CreateRich(21);
            var army = w.Ops.CreateArmy(w.B, w.CapB, null);
            army.State = ArmyState.Besieging;
            army.TargetSettlementId = w.CapA.Id;
            int fired = 0;
            foreach (var season in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
            {
                StoryWorld.SetSeason(w, season);
                foreach (var d in WorldEventCatalog.All)
                {
                    if (StoryWorld.TryFire(w, w.A, d.Key)) fired++;
                    if (StoryWorld.TryFire(w, w.B, d.Key)) fired++;
                    StoryWorld.AssertSane(w);
                }
            }
            Assert.IsTrue(fired >= 20, "fired " + fired);
            Assert.IsTrue(w.State.Chronicle.Entries.Count >= fired);
            foreach (var e in w.State.Chronicle.Entries)
            {
                Assert.IsFalse(e.Text.Contains("{"), "unfilled placeholder: " + e.Text);
                Assert.IsTrue(e.Importance >= 0 && e.Importance <= 3);
            }
        }

        [Test]
        public void Tick_IsDeterministicOver200Runs()
        {
            string a = RunRich(77, 3f, out int firedA);
            string b = RunRich(77, 3f, out int firedB);
            Assert.AreEqual(firedA, firedB);
            Assert.IsTrue(firedA > 5, "fired " + firedA);
            Assert.AreEqual(a, b);
            string c = RunRich(78, 3f, out _);
            Assert.AreNotEqual(a, c, "different seeds tell different stories");
        }

        [Test]
        public void Tick_EventsDoNotDominate_AndMemoryStaysBounded()
        {
            var w = StoryWorld.CreateRich(5);
            var sys = new WorldEventSystem();
            TestWorlds.Run(sys, w.Ctx, Runs * 48);
            int civs = 0;
            foreach (var c in w.State.Civilizations.Items) if (c.IsAlive) civs++;
            Assert.IsTrue(sys.EventsFired > 0, "the deck should play over 100 years");
            Assert.IsTrue(sys.EventsFired <= Runs * civs / 5, "too many events: " + sys.EventsFired);
            Assert.AreEqual(sys.EventsFired, StoryQueries.TotalEvents(w.State));
            foreach (var c in w.State.Civilizations.Items)
            {
                int storyKeys = 0;
                foreach (var kv in c.Memory) if (kv.Key.StartsWith("story.", System.StringComparison.Ordinal)) storyKeys++;
                Assert.IsTrue(storyKeys <= WorldEventCatalog.All.Count + 6, "memory keys: " + storyKeys);
            }
            StoryWorld.AssertSane(w);
        }

        [Test]
        public void Tick_RespectsQuietPeriod_AndZeroChance()
        {
            var w = StoryWorld.CreateRich(9);
            w.State.Memory[WorldEventSystem.ChanceScaleKey] = 0f;
            var quiet = new WorldEventSystem();
            TestWorlds.Run(quiet, w.Ctx, Runs * 48);
            Assert.AreEqual(0, quiet.EventsFired);

            var busy = StoryWorld.CreateRich(9);
            busy.State.Memory[WorldEventSystem.ChanceScaleKey] = 10f;
            var sys = new WorldEventSystem();
            var lastByCiv = new Dictionary<int, long>();
            int violations = 0;
            sys.Initialize(busy.Ctx);
            for (int run = 0; run < Runs; run++)
            {
                busy.State.Tick += 48;
                busy.Ctx.DeltaTicks = 48;
                var before = new Dictionary<int, float>();
                foreach (var c in busy.State.Civilizations.Items) before[c.Id] = c.Mem(WorldEventSystem.LastEventTickKey, -1f);
                sys.Tick(busy.Ctx);
                busy.Ops.FlushRemovals();
                foreach (var c in busy.State.Civilizations.Items)
                {
                    float now = c.Mem(WorldEventSystem.LastEventTickKey, -1f);
                    if (!before.ContainsKey(c.Id) || now == before[c.Id]) continue;
                    if (lastByCiv.TryGetValue(c.Id, out long prev) && (long)now - prev < SimTime.YearsToTicks(WorldEventSystem.MinYearsBetweenEvents)) violations++;
                    lastByCiv[c.Id] = (long)now;
                }
            }
            Assert.IsTrue(sys.EventsFired > 10, "fired " + sys.EventsFired);
            Assert.AreEqual(0, violations, "a civ drew two cards inside its quiet period");
        }

        [Test]
        public void StoryQueries_RecentEventKeys_NewestFirst_WithinWindow()
        {
            var w = StoryWorld.Create();
            CollectionAssertEmpty(StoryQueries.RecentEventKeys(w.State, w.A.Id));
            StoryWorld.Fire(w, w.A, WorldEventKeys.CometOmen);
            w.State.Tick += SimTime.YearsToTicks(2f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.WanderingSage);
            var keys = new List<string>(StoryQueries.RecentEventKeys(w.State, w.A.Id));
            Assert.AreEqual(2, keys.Count);
            Assert.AreEqual(WorldEventKeys.WanderingSage, keys[0]);
            Assert.AreEqual(WorldEventKeys.CometOmen, keys[1]);
            Assert.AreEqual(w.State.Tick, StoryQueries.LastFiredTick(w.State, w.A.Id, WorldEventKeys.WanderingSage));
            Assert.AreEqual(-1L, StoryQueries.LastFiredTick(w.State, w.B.Id, WorldEventKeys.WanderingSage));
            w.State.Tick += SimTime.YearsToTicks(9f);
            keys = new List<string>(StoryQueries.RecentEventKeys(w.State, w.A.Id));
            Assert.AreEqual(1, keys.Count);
            Assert.AreEqual(WorldEventKeys.WanderingSage, keys[0]);
            CollectionAssertEmpty(StoryQueries.RecentEventKeys(w.State, 999));
            CollectionAssertEmpty(StoryQueries.RecentEventKeys(null, w.A.Id));
        }

        private static void CollectionAssertEmpty(IEnumerable<string> keys)
        {
            foreach (var k in keys) Assert.Fail("unexpected key " + k);
        }

        private static string RunRich(ulong seed, float chanceScale, out int fired)
        {
            var w = StoryWorld.CreateRich(seed);
            w.State.Memory[WorldEventSystem.ChanceScaleKey] = chanceScale;
            var sys = new WorldEventSystem();
            TestWorlds.Run(sys, w.Ctx, Runs * 48);
            fired = sys.EventsFired;
            StoryWorld.AssertSane(w);
            return StoryWorld.Signature(w);
        }
    }
}
