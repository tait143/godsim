using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Divine;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Divine.Nature
{
    public class DisasterSystemTests
    {
        private static List<DisasterEvent> Record(SimContext ctx)
        {
            var list = new List<DisasterEvent>();
            ctx.Events.Subscribe<DisasterEvent>(e => list.Add(e));
            return list;
        }

        private static int DisasterEntries(SimContext ctx)
        {
            int n = 0;
            foreach (var e in ctx.State.Chronicle.Entries) if (e.Category == ChronicleCategory.Disaster) n++;
            return n;
        }

        [Test]
        public void No_Disasters_When_Disabled()
        {
            var ctx = NatureTestWorlds.Rich(3, out var town, out _);
            ctx.Config.EnableDisasters = false;
            ctx.Config.DisasterFrequency = 10f;
            var events = Record(ctx);
            int chronicle = ctx.State.Chronicle.Entries.Count;
            TestWorlds.Run(new DisasterSystem(), ctx, SimTime.TicksPerYear * 100);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, DisasterEntries(ctx));
            Assert.AreEqual(chronicle, ctx.State.Chronicle.Entries.Count);
            Assert.AreEqual(0, NatureTestWorlds.CountFlag(ctx.State.Map, TileFlags.Burning));
            foreach (var b in ctx.State.Buildings.Items) Assert.AreEqual(1f, b.Condition);
        }

        [Test]
        public void No_Disasters_When_Frequency_Is_Zero()
        {
            var ctx = NatureTestWorlds.Rich(4, out _, out _);
            ctx.Config.EnableDisasters = true;
            ctx.Config.DisasterFrequency = 0f;
            var events = Record(ctx);
            TestWorlds.Run(new DisasterSystem(), ctx, SimTime.TicksPerYear * 100);
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, DisasterEntries(ctx));

            ctx.Config.DisasterFrequency = float.NaN;
            TestWorlds.Run(new DisasterSystem(), ctx, SimTime.TicksPerYear * 20);
            Assert.AreEqual(0, events.Count, "NaN frequency is treated as 0");
        }

        [Test]
        public void Absurd_Frequency_And_Corrupt_Memory_Are_Clamped()
        {
            var ctx = NatureTestWorlds.Rich(14, out _, out var civ);
            ctx.Config.DisasterFrequency = 1e9f;
            civ.Memory[NaturalEffects.KeyLastDisasterTick] = float.NaN;
            var events = Record(ctx);
            TestWorlds.Run(new DisasterSystem(), ctx, SimTime.TicksPerYear * 20);
            // Clamped to 10: at most one civ disaster plus two wilderness events per run.
            Assert.Greater(events.Count, 0);
            Assert.LessOrEqual(events.Count, 3 * 20 * SimTime.TicksPerYear / 24);
            Assert.IsFalse(float.IsNaN(civ.Mem(NaturalEffects.KeyLastDisasterTick)));
        }

        [Test]
        public void High_Frequency_Produces_Every_Applicable_Kind_With_Events_And_Chronicle()
        {
            var ctx = NatureTestWorlds.Rich(5, out var town, out var civ);
            ctx.Config.DisasterFrequency = 10f;
            var events = Record(ctx);
            var seen = new HashSet<DisasterType>();
            ctx.Events.Subscribe<DisasterEvent>(e => seen.Add(e.Type));
            TestWorlds.Run(new DisasterSystem(), ctx, SimTime.TicksPerYear * 300);

            Assert.IsTrue(seen.Contains(DisasterType.Wildfire), "wildfire");
            Assert.IsTrue(seen.Contains(DisasterType.Earthquake), "earthquake");
            Assert.IsTrue(seen.Contains(DisasterType.Flood), "flood");
            Assert.IsTrue(seen.Contains(DisasterType.Volcano), "volcano");
            Assert.IsTrue(seen.Contains(DisasterType.Storm), "storm");
            Assert.AreEqual(events.Count, DisasterEntries(ctx), "one chronicle entry per disaster");
            foreach (var e in ctx.State.Chronicle.Entries)
            {
                if (e.Category != ChronicleCategory.Disaster) continue;
                Assert.AreEqual(2, e.Importance);
                Assert.IsFalse(string.IsNullOrEmpty(e.Text));
            }
            foreach (var e in events)
            {
                Assert.IsTrue(ctx.State.Map.InBounds(e.Position));
                Assert.That(e.Severity, Is.InRange(0f, 1f));
                Assert.Greater(e.Radius, 0);
            }
            Assert.Greater(civ.Mem(NaturalEffects.KeyDisasterCount), 0f);
        }

        [Test]
        public void Rate_At_Frequency_One_Is_Rare_But_Memorable()
        {
            var ctx = NatureTestWorlds.Rich(6, out var town, out var civ);
            ctx.Config.DisasterFrequency = 1f;
            const int years = 600;
            var systems = new List<ISimSystem> { new SeasonWeatherSystem(), new DisasterSystem(), new FireSystem() };
            TestWorlds.RunAll(systems, ctx, SimTime.TicksPerYear * years);
            float strikes = civ.Mem(NaturalEffects.KeyDisasterCount);
            // A civ with every hazard nearby (coast, river, forest, mountains, volcano): ~1 per 15-20 years.
            Assert.That(strikes, Is.InRange(years / 30f, years / 10f), "strikes=" + strikes);
        }

        [Test]
        public void Earthquake_Damages_Buildings_With_Falloff_And_Destroys_Weak_Ones()
        {
            var ctx = NatureTestWorlds.Flat(7);
            NatureTestWorlds.Fill(ctx.State.Map, 30, 30, 32, 32, Biome.Mountain, 0.7f);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(20, 30), 10, out _);
            var houses = NatureTestWorlds.AddHouses(ctx, s, new[] { new Vec2i(22, 28), new Vec2i(24, 30), new Vec2i(18, 33), new Vec2i(26, 34) });
            houses[0].Condition = 0.05f;
            var destroyed = new List<BuildingDestroyedEvent>();
            ctx.Events.Subscribe<BuildingDestroyedEvent>(e => destroyed.Add(e));
            var events = Record(ctx);
            var sys = new DisasterSystem();
            sys.Initialize(ctx);

            Assert.IsTrue(sys.Trigger(ctx, DisasterType.Earthquake, s));
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(DisasterType.Earthquake, events[0].Type);
            Assert.IsTrue(houses[0].IsDestroyed);
            Assert.AreEqual(1, destroyed.Count);
            Assert.AreEqual(DestroyCause.Disaster, destroyed[0].Cause);
            for (int i = 1; i < houses.Count; i++)
            {
                Assert.IsFalse(houses[i].IsDestroyed);
                Assert.That(houses[i].Condition, Is.InRange(0.3f, 0.93f), "lost 0.2-0.6 with falloff");
            }
            var last = ctx.State.Chronicle.Entries[ctx.State.Chronicle.Entries.Count - 1];
            Assert.AreEqual(ChronicleCategory.Disaster, last.Category);
            Assert.AreEqual(2, last.Importance);
            StringAssert.StartsWith("The earth split beneath " + s.Name + ".", last.Text);
            Assert.AreEqual(s.Id, last.SettlementId);
        }

        [Test]
        public void Flood_Clears_Farmland_And_Damages_Low_Buildings()
        {
            var ctx = NatureTestWorlds.Flat(8);
            var map = ctx.State.Map;
            for (int y = 0; y < 64; y++)
            {
                map.Set(map.Index(25, y), TileFlags.River);
                for (int x = 22; x <= 28; x++) if (x != 25) map.Set(map.Index(x, y), TileFlags.Farmland);
            }
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(16, 30), 20, out _);
            var riverside = new List<Vec2i>();
            for (int y = 16; y <= 46; y += 2) riverside.Add(new Vec2i(24, y));
            var houses = NatureTestWorlds.AddHouses(ctx, s, riverside.ToArray());
            int farmland = NatureTestWorlds.CountFlag(map, TileFlags.Farmland);
            var events = Record(ctx);
            int terrain = 0;
            ctx.Events.Subscribe<TerrainChangedEvent>(e => terrain++);
            var sys = new DisasterSystem();
            sys.Initialize(ctx);

            Assert.IsTrue(sys.Trigger(ctx, DisasterType.Flood, s));
            Assert.AreEqual(DisasterType.Flood, events[0].Type);
            Assert.Less(NatureTestWorlds.CountFlag(map, TileFlags.Farmland), farmland);
            Assert.Greater(terrain, 0);
            int damaged = 0;
            foreach (var h in houses) if (h.IsDestroyed || h.Condition < 1f) damaged++;
            Assert.Greater(damaged, 0);
        }

        [Test]
        public void Volcano_Destroys_Kills_Scorches_And_Raises_The_Vent()
        {
            var ctx = NatureTestWorlds.Flat(9);
            var map = ctx.State.Map;
            int vent = map.Index(30, 30);
            map.Biomes[vent] = Biome.Volcanic;
            map.Elevation[vent] = 0.6f;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(32, 30), 6, out _);
            var far = ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(50, 50), 0, true);
            var deaths = new List<PersonDiedEvent>();
            ctx.Events.Subscribe<PersonDiedEvent>(e => deaths.Add(e));
            var terrain = new List<TerrainChangedEvent>();
            ctx.Events.Subscribe<TerrainChangedEvent>(e => terrain.Add(e));
            var events = Record(ctx);
            int tc = s.TownCenterId;
            Assert.GreaterOrEqual(tc, 0);
            var sys = new DisasterSystem();
            sys.Initialize(ctx);

            Assert.IsTrue(sys.Trigger(ctx, DisasterType.Volcano, s));
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(DisasterType.Volcano, events[0].Type);
            Assert.AreEqual(new Vec2i(30, 30), events[0].Position);
            Assert.IsFalse(ctx.State.Buildings.Contains(tc), "town center 2 tiles from the vent is destroyed");
            Assert.AreEqual(6, deaths.Count);
            foreach (var d in deaths) Assert.AreEqual(DeathCause.Disaster, d.Cause);
            Assert.IsFalse(far.IsDestroyed);
            Assert.AreEqual(1f, far.Condition);
            Assert.Greater(map.Elevation[vent], 0.6f);
            Assert.AreNotEqual(0, (int)(map.Flags[vent] & TileFlags.Scorched));
            Assert.AreNotEqual(0, (int)(map.Flags[map.Index(31, 30)] & TileFlags.Burning));
            Assert.AreEqual(1, terrain.Count);
            Assert.LessOrEqual(terrain[0].MinX, 29);
            Assert.GreaterOrEqual(terrain[0].MaxX, 31);
        }

        [Test]
        public void Storm_Batters_A_Coastal_Settlement()
        {
            var ctx = NatureTestWorlds.Rich(10, out var town, out _);
            var events = Record(ctx);
            var sys = new DisasterSystem();
            sys.Initialize(ctx);
            Assert.IsTrue(sys.Trigger(ctx, DisasterType.Storm, town));
            Assert.AreEqual(DisasterType.Storm, events[0].Type);
            Assert.AreEqual(1f, ctx.State.Weather.StormIntensity);
            int count = 0;
            foreach (var b in ctx.State.Buildings.Items)
            {
                if (b.SettlementId != town.Id) continue;
                Assert.That(b.Condition, Is.InRange(0.69f, 0.91f));
                count++;
            }
            Assert.Greater(count, 1);
        }

        [Test]
        public void Wildfire_Ignites_Forest_Near_Settlement()
        {
            var ctx = NatureTestWorlds.Rich(11, out var town, out _);
            var events = Record(ctx);
            var sys = new DisasterSystem();
            sys.Initialize(ctx);
            Assert.IsTrue(sys.Trigger(ctx, DisasterType.Wildfire, town));
            Assert.AreEqual(DisasterType.Wildfire, events[0].Type);
            Assert.Greater(NatureTestWorlds.CountFlag(ctx.State.Map, TileFlags.Burning), 0);
            Assert.IsTrue(ctx.State.Memory.ContainsKey(NaturalEffects.KeyFireHint));
            var fire = new FireSystem();
            TestWorlds.Run(fire, ctx, 400);
            Assert.AreEqual(0, NatureTestWorlds.CountFlag(ctx.State.Map, TileFlags.Burning), "wildfire burns out");
        }

        [Test]
        public void Kinds_Without_A_Site_Do_Not_Fire()
        {
            var ctx = NatureTestWorlds.Flat(12);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5, out _);
            var events = Record(ctx);
            var sys = new DisasterSystem();
            sys.Initialize(ctx);
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Storm, s), "inland");
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Flood, s), "no river");
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Earthquake, s), "no mountains");
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Volcano, s), "no volcano");
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Wildfire, s), "no forest");
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Plague, s), "not a natural disaster of this module");
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Volcano, null), "no volcanic tiles at all");
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, DisasterEntries(ctx));

            // A whole featureless world at high frequency stays quiet.
            ctx.Config.DisasterFrequency = 10f;
            TestWorlds.Run(sys, ctx, SimTime.TicksPerYear * 50, false);
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void Wilderness_Volcano_Can_Be_Triggered_Without_A_Settlement()
        {
            var ctx = NatureTestWorlds.Rich(13, out _, out _);
            var events = Record(ctx);
            var sys = new DisasterSystem();
            sys.Initialize(ctx);
            Assert.IsTrue(sys.Trigger(ctx, DisasterType.Volcano, null));
            Assert.AreEqual(DisasterType.Volcano, events[0].Type);
            Assert.AreEqual(Biome.Volcanic, ctx.State.Map.BiomeAt(events[0].Position));
            Assert.IsFalse(sys.Trigger(ctx, DisasterType.Storm, null));
        }

        [Test]
        public void Whole_Nature_Module_Is_Deterministic()
        {
            ulong Simulate(ulong seed)
            {
                var ctx = NatureTestWorlds.Rich(seed, out _, out _);
                ctx.Config.DisasterFrequency = 5f;
                var systems = new List<ISimSystem> { new SeasonWeatherSystem(), new DisasterSystem(), new FireSystem() };
                TestWorlds.RunAll(systems, ctx, SimTime.TicksPerYear * 60);
                return NatureTestWorlds.Fingerprint(ctx);
            }
            Assert.AreEqual(Simulate(77), Simulate(77));
            Assert.AreNotEqual(Simulate(77), Simulate(78));
        }
    }
}
