using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Divine;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Divine.Nature
{
    public class FireSystemTests
    {
        /// <summary>Advances one fire run (2 ticks) the way the engine would.</summary>
        private static void Run(FireSystem fire, SimContext ctx)
        {
            ctx.State.Tick += fire.TickInterval;
            ctx.DeltaTicks = fire.TickInterval;
            fire.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        private static int RunUntilOut(FireSystem fire, SimContext ctx, int maxRuns)
        {
            for (int r = 1; r <= maxRuns; r++)
            {
                Run(fire, ctx);
                if (NatureTestWorlds.CountFlag(ctx.State.Map, TileFlags.Burning) == 0) return r;
            }
            return -1;
        }

        [Test]
        public void Fire_Spreads_Into_Trees_And_Burns_Out()
        {
            var ctx = NatureTestWorlds.Flat(21);
            var map = ctx.State.Map;
            NatureTestWorlds.Forest(map, 20, 20, 31, 31, 80f);
            int trees = NatureTestWorlds.CountFlag(map, TileFlags.Trees);
            ctx.State.Tick = SimTime.TicksPerSeason; // Summer
            var fire = new FireSystem();
            fire.Initialize(ctx);
            map.Set(map.Index(25, 25), TileFlags.Burning);
            int terrainEvents = 0;
            ctx.Events.Subscribe<TerrainChangedEvent>(e => terrainEvents++);

            int maxBurning = 0;
            for (int r = 0; r < 400; r++)
            {
                Run(fire, ctx);
                int burning = NatureTestWorlds.CountFlag(map, TileFlags.Burning);
                if (burning > maxBurning) maxBurning = burning;
                Assert.LessOrEqual(fire.LastProcessedCount, FireSystem.MaxBurningPerRun);
            }
            Assert.Greater(maxBurning, 3, "fire should spread through the forest");
            Assert.AreEqual(0, NatureTestWorlds.CountFlag(map, TileFlags.Burning), "fire must burn out");
            Assert.AreEqual(0, fire.TrackedFireCount);
            int scorched = NatureTestWorlds.CountFlag(map, TileFlags.Scorched);
            Assert.Greater(scorched, 3);
            Assert.Less(NatureTestWorlds.CountFlag(map, TileFlags.Trees), trees);
            Assert.Greater(terrainEvents, 0);
            for (int i = 0; i < map.TileCount; i++)
                if ((map.Flags[i] & TileFlags.Scorched) != 0 && map.Biomes[i] == Biome.Forest)
                    Assert.AreEqual(0, (int)(map.Flags[i] & TileFlags.Trees), "scorched tiles lost their trees");
            // Fire never escapes onto plain grassland (no fuel).
            for (int i = 0; i < map.TileCount; i++)
                if (map.Biomes[i] == Biome.Grassland) Assert.AreEqual(0, (int)(map.Flags[i] & TileFlags.Scorched));
        }

        [Test]
        public void Burning_Tree_Consumes_Wood_And_Becomes_Scorched()
        {
            var ctx = NatureTestWorlds.Flat(2);
            var map = ctx.State.Map;
            NatureTestWorlds.Forest(map, 10, 10, 10, 10, 12f);
            var fire = new FireSystem();
            fire.Initialize(ctx);
            int i = map.Index(10, 10);
            map.Set(i, TileFlags.Burning);
            Run(fire, ctx);
            Assert.Less(map.Resource[i], 12f);
            Run(fire, ctx);
            Assert.AreEqual(0, (int)(map.Flags[i] & TileFlags.Trees));
            Assert.AreNotEqual(0, (int)(map.Flags[i] & TileFlags.Scorched));
            Assert.AreEqual(0f, map.Resource[i]);
        }

        [Test]
        public void Fire_Damages_And_Destroys_A_Building()
        {
            var ctx = NatureTestWorlds.Flat(5);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(50, 50), 2, out _);
            var house = ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(10, 10), 0, true);
            Assert.IsNotNull(house);
            var destroyed = new List<BuildingDestroyedEvent>();
            ctx.Events.Subscribe<BuildingDestroyedEvent>(e => destroyed.Add(e));
            var fire = new FireSystem();
            fire.Initialize(ctx);
            int tile = map.Index(10, 10);
            map.Set(tile, TileFlags.Burning);

            Run(fire, ctx);
            Assert.IsTrue(house.IsOnFire);
            Assert.AreEqual(0.92f, house.Condition, 1e-4f);
            Assert.IsFalse(house.IsDestroyed);

            house.Condition = 0.05f;
            Run(fire, ctx);
            Assert.IsTrue(house.IsDestroyed);
            Assert.AreEqual(1, destroyed.Count);
            Assert.AreEqual(DestroyCause.Fire, destroyed[0].Cause);
            Assert.AreEqual(-1, map.BuildingAt[tile]);
            Assert.IsFalse(ctx.State.Buildings.Contains(house.Id));

            Assert.Greater(RunUntilOut(fire, ctx, 20), 0, "the ruin burns out");
        }

        [Test]
        public void Surviving_Building_Stops_Burning_When_Fire_Ends()
        {
            var ctx = NatureTestWorlds.Flat(6);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(50, 50), 2, out _);
            var house = ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(20, 20), 0, true);
            var fire = new FireSystem();
            fire.Initialize(ctx);
            map.Set(map.Index(20, 20), TileFlags.Burning);
            Run(fire, ctx);
            Assert.IsTrue(house.IsOnFire);
            Assert.Greater(RunUntilOut(fire, ctx, 20), 0);
            Assert.IsFalse(house.IsOnFire);
            Assert.IsFalse(house.IsDestroyed);
            Assert.Less(house.Condition, 1f);
            Assert.GreaterOrEqual(house.Condition, 0.19f);
        }

        [Test]
        public void Fire_Spreads_From_Forest_Into_A_Building()
        {
            int caught = 0;
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var ctx = NatureTestWorlds.Flat(seed);
                var map = ctx.State.Map;
                NatureTestWorlds.Forest(map, 10, 10, 16, 16);
                var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(50, 50), 2, out _);
                var house = ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(13, 13), 0, true);
                map.Clear(map.Index(13, 13), TileFlags.Trees);
                ctx.State.Tick = SimTime.TicksPerSeason; // Summer
                var fire = new FireSystem();
                fire.Initialize(ctx);
                map.Set(map.Index(12, 13), TileFlags.Burning);
                bool onFire = false;
                for (int r = 0; r < 120; r++)
                {
                    Run(fire, ctx);
                    onFire |= house.IsOnFire;
                }
                if (onFire && (house.Condition < 1f || house.IsDestroyed)) caught++;
                Assert.AreEqual(0, NatureTestWorlds.CountFlag(map, TileFlags.Burning));
                Assert.IsFalse(!house.IsDestroyed && house.IsOnFire, "IsOnFire cleared after the fire");
            }
            Assert.Greater(caught, 0, "a house surrounded by burning forest should catch fire");
        }

        [Test]
        public void Rain_Extinguishes_Faster()
        {
            var dry = NatureTestWorlds.Flat(8);
            var wet = NatureTestWorlds.Flat(8);
            wet.State.Weather.RainTicksRemaining = 10000;
            var a = new FireSystem();
            var b = new FireSystem();
            a.Initialize(dry);
            b.Initialize(wet);
            dry.State.Map.Set(dry.State.Map.Index(30, 30), TileFlags.Burning);
            wet.State.Map.Set(wet.State.Map.Index(30, 30), TileFlags.Burning);
            int dryRuns = RunUntilOut(a, dry, 50);
            int wetRuns = RunUntilOut(b, wet, 50);
            Assert.Greater(dryRuns, 0);
            Assert.Greater(wetRuns, 0);
            Assert.Less(wetRuns, dryRuns);
        }

        [Test]
        public void Rain_Suppresses_Spread()
        {
            int Burned(bool rain)
            {
                var ctx = NatureTestWorlds.Flat(13);
                var map = ctx.State.Map;
                NatureTestWorlds.Forest(map, 10, 10, 30, 30);
                ctx.State.Tick = SimTime.TicksPerSeason;
                if (rain) ctx.State.Weather.RainTicksRemaining = 100000;
                var fire = new FireSystem();
                fire.Initialize(ctx);
                map.Set(map.Index(20, 20), TileFlags.Burning);
                RunUntilOut(fire, ctx, 600);
                return NatureTestWorlds.CountFlag(map, TileFlags.Scorched);
            }
            int withRain = Burned(true), without = Burned(false);
            Assert.Less(withRain, without);
            Assert.Less(withRain, 15);
        }

        [Test]
        public void Timers_Are_Recreated_For_Burning_Flags_Without_Timers()
        {
            var ctx = NatureTestWorlds.Flat(17);
            var map = ctx.State.Map;
            NatureTestWorlds.Forest(map, 40, 40, 44, 44, 30f);
            var fire = new FireSystem();
            fire.Initialize(ctx);
            Assert.AreEqual(0, fire.TrackedFireCount);
            // Simulate a loaded save: Burning flags exist but the private timers do not.
            var burning = new List<int>();
            for (int k = 0; k < 30; k++)
            {
                int i = map.Index((k * 7) % 64, (k * 13) % 64);
                map.Set(i, TileFlags.Burning);
                burning.Add(i);
            }
            map.Set(map.Index(42, 42), TileFlags.Burning);
            burning.Add(map.Index(42, 42));
            Run(fire, ctx);
            foreach (int i in burning)
                if ((map.Flags[i] & TileFlags.Burning) != 0)
                    Assert.That(fire.RemainingTicks(i), Is.InRange(1, FireSystem.MaxBurnTicks), "timer recreated for tile " + i);
            Assert.Greater(RunUntilOut(fire, ctx, 200), 0, "rediscovered fires burn out");
            Assert.AreEqual(0, fire.TrackedFireCount);
        }

        [Test]
        public void Fresh_System_Adopts_Fires_From_A_Loaded_Map_And_Fixes_Stale_OnFire()
        {
            var ctx = NatureTestWorlds.Flat(19);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(50, 50), 2, out _);
            var stale = ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(5, 5), 0, true);
            stale.IsOnFire = true; // saved while burning, tile already extinguished
            for (int x = 20; x < 30; x++) map.Set(map.Index(x, 20), TileFlags.Burning);
            var fire = new FireSystem();
            fire.Initialize(ctx);
            Assert.AreEqual(10, fire.TrackedFireCount);
            for (int r = 0; r < 8; r++) Run(fire, ctx);
            Assert.IsFalse(stale.IsOnFire);
        }

        [Test]
        public void Swapping_In_A_Loaded_World_Resets_Fire_Tracking()
        {
            var a = NatureTestWorlds.Flat(51);
            var b = NatureTestWorlds.Flat(52);
            var fire = new FireSystem();
            fire.Initialize(a);
            a.State.Map.Set(a.State.Map.Index(5, 5), TileFlags.Burning);
            Run(fire, a);
            Assert.AreEqual(1, fire.TrackedFireCount);
            for (int x = 10; x < 14; x++) b.State.Map.Set(b.State.Map.Index(x, 40), TileFlags.Burning);
            Run(fire, b); // same system instance, new WorldState (e.g. a save was loaded)
            Assert.AreEqual(4, fire.TrackedFireCount);
            Assert.AreEqual(-1, fire.RemainingTicks(a.State.Map.Index(5, 5)));
            Assert.Greater(RunUntilOut(fire, b, 20), 0);
        }

        [Test]
        public void Rolling_Window_Finds_Fires_On_Large_Maps()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 256, 23);
            var map = ctx.State.Map;
            var fire = new FireSystem();
            fire.Initialize(ctx);
            int far = map.Index(250, 250);
            map.Set(far, TileFlags.Burning);
            int runs = 0;
            while (fire.RemainingTicks(far) < 0 && (map.Flags[far] & TileFlags.Burning) != 0 && runs < 20) { Run(fire, ctx); runs++; }
            int sweep = (map.TileCount + FireSystem.MinScanWindow - 1) / FireSystem.MinScanWindow;
            Assert.LessOrEqual(runs, sweep, "the rolling window covers the whole map within one sweep");
            Assert.Greater(RunUntilOut(fire, ctx, 40), 0);
        }

        [Test]
        public void Ignition_Hint_Is_Picked_Up_Immediately()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 256, 29);
            var map = ctx.State.Map;
            var fire = new FireSystem();
            fire.Initialize(ctx);
            Run(fire, ctx); // advance the scan cursor away from the start
            var dirty = new NatureDirtyRect();
            int n = NaturalEffects.IgniteDisc(ctx.State, new Vec2i(200, 200), 2, ref dirty);
            Assert.Greater(n, 0);
            Assert.IsTrue(dirty.Any);
            Run(fire, ctx);
            Assert.AreEqual(n, fire.TrackedFireCount);
            Assert.IsFalse(ctx.State.Memory.ContainsKey(NaturalEffects.KeyFireHint));
        }

        [Test]
        public void Burning_Water_Is_Extinguished()
        {
            var ctx = NatureTestWorlds.Flat(31);
            var map = ctx.State.Map;
            int i = map.Index(3, 3);
            map.Biomes[i] = Biome.Lake;
            map.Set(i, TileFlags.Burning);
            var fire = new FireSystem();
            fire.Initialize(ctx);
            Run(fire, ctx);
            Assert.AreEqual(0, (int)(map.Flags[i] & TileFlags.Burning));
            Assert.IsFalse(NaturalEffects.Ignite(map, i));
        }

        [Test]
        public void Processing_Is_Capped_And_Huge_Fires_Still_Burn_Out()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 160, 37);
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++) map.Set(i, TileFlags.Burning);
            var fire = new FireSystem();
            fire.Initialize(ctx);
            Assert.LessOrEqual(fire.TrackedFireCount, FireSystem.MaxTrackedFires);
            int runs = 0;
            while (NatureTestWorlds.CountFlag(map, TileFlags.Burning) > 0 && runs < 2000)
            {
                Run(fire, ctx);
                Assert.LessOrEqual(fire.LastProcessedCount, FireSystem.MaxBurningPerRun);
                runs++;
            }
            Assert.Less(runs, 2000, "every fire burns out eventually");
        }

        [Test]
        public void Scorched_Land_Recovers_And_Forest_Regrows()
        {
            var ctx = NatureTestWorlds.Flat(41);
            var map = ctx.State.Map;
            var tiles = new List<int>();
            for (int x = 10; x < 20; x++)
            {
                int i = map.Index(x, 10);
                map.Biomes[i] = Biome.Forest;
                map.Set(i, TileFlags.Scorched);
                tiles.Add(i);
            }
            int grass = map.Index(30, 30);
            map.Set(grass, TileFlags.Scorched);
            var fire = new FireSystem();
            fire.Initialize(ctx);
            for (int r = 0; r < SimTime.TicksPerYear * 60 / 2; r++) Run(fire, ctx);
            foreach (int i in tiles)
            {
                Assert.AreEqual(0, (int)(map.Flags[i] & TileFlags.Scorched));
                Assert.AreNotEqual(0, (int)(map.Flags[i] & TileFlags.Trees));
                Assert.GreaterOrEqual(map.Resource[i], FireSystem.RegrownTreeWood);
            }
            Assert.AreEqual(0, (int)(map.Flags[grass] & TileFlags.Scorched));
            Assert.AreEqual(0, (int)(map.Flags[grass] & TileFlags.Trees), "grassland does not grow a forest");
        }

        [Test]
        public void Fire_Is_Deterministic()
        {
            ulong Simulate()
            {
                var ctx = NatureTestWorlds.Flat(99);
                var map = ctx.State.Map;
                NatureTestWorlds.Forest(map, 5, 5, 40, 40);
                var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(50, 50), 2, out _);
                NatureTestWorlds.AddHouses(ctx, s, new[] { new Vec2i(20, 20), new Vec2i(21, 20), new Vec2i(22, 21) });
                ctx.State.Tick = SimTime.TicksPerSeason;
                var fire = new FireSystem();
                fire.Initialize(ctx);
                map.Set(map.Index(6, 6), TileFlags.Burning);
                map.Set(map.Index(30, 35), TileFlags.Burning);
                for (int r = 0; r < 300; r++) Run(fire, ctx);
                return NatureTestWorlds.Fingerprint(ctx);
            }
            Assert.AreEqual(Simulate(), Simulate());
        }
    }
}
