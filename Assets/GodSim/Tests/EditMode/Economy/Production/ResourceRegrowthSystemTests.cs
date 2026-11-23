using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Economy;
using GodSim.Tests.Shared;
using NUnit.Framework;
using Kit = GodSim.Tests.Economy.Production.ProductionTestKit;

namespace GodSim.Tests.Economy.Production
{
    public class ResourceRegrowthSystemTests
    {
        private const int Cycle = ResourceRegrowthSystem.WindowsPerCycle; // runs per full map sweep

        private static SimContext Forest(ulong seed = 3, bool wooded = true)
        {
            var ctx = TestWorlds.CreateContext(Kit.CreateDb(), 64, seed, Biome.Forest);
            if (wooded)
            {
                var map = ctx.State.Map;
                for (int i = 0; i < map.TileCount; i++) { map.Set(i, TileFlags.Trees); map.Resource[i] = 100f; }
            }
            return ctx;
        }

        private static void Clear(SimContext ctx, int x, int y)
        {
            var map = ctx.State.Map;
            int i = map.Index(x, y);
            map.Clear(i, TileFlags.Trees);
            map.Resource[i] = 0f;
        }

        [Test]
        public void Clearing_RegrowsTrees_ExceptOnRoads()
        {
            var ctx = Forest();
            var map = ctx.State.Map;
            for (int y = 30; y <= 34; y++) for (int x = 30; x <= 34; x++) Clear(ctx, x, y);
            map.Set(map.Index(32, 32), TileFlags.Road);

            Kit.Step(new ResourceRegrowthSystem(), ctx, Cycle * 3);

            int regrown = 0;
            for (int y = 30; y <= 34; y++)
                for (int x = 30; x <= 34; x++)
                {
                    int i = map.Index(x, y);
                    if (x == 32 && y == 32) { Assert.IsFalse(map.Has(i, TileFlags.Trees), "road tile regrew trees"); continue; }
                    if (!map.Has(i, TileFlags.Trees)) continue;
                    regrown++;
                    Assert.That(map.Resource[i], Is.GreaterThan(0f).And.LessThanOrEqualTo(ResourceRegrowthSystem.MaxTreeWood));
                }
            Assert.IsTrue(regrown >= 20, "most of the clearing should be wooded again after three sweeps");
        }

        [Test]
        public void TreeTiles_RegainWoodUpToCap()
        {
            var ctx = Forest(wooded: false);
            Kit.SetFlag(ctx, 10, 10, TileFlags.Trees, 10f);
            Kit.SetFlag(ctx, 11, 10, TileFlags.Trees, 150f);
            Kit.SetFlag(ctx, 12, 10, TileFlags.Trees, 200f);  // generated above the cap: left alone
            Kit.SetFlag(ctx, 13, 10, TileFlags.Trees, float.NaN);
            Kit.Step(new ResourceRegrowthSystem(), ctx, Cycle);
            var map = ctx.State.Map;
            float years = Cycle * 48 / (float)SimTime.TicksPerYear;
            Assert.AreEqual(10f + ResourceRegrowthSystem.WoodRegrowthPerYear * years, map.Resource[map.Index(10, 10)], 1e-3f);
            Assert.AreEqual(ResourceRegrowthSystem.MaxTreeWood, map.Resource[map.Index(11, 10)], 1e-3f);
            Assert.AreEqual(200f, map.Resource[map.Index(12, 10)], 1e-3f);
            Assert.AreEqual(ResourceRegrowthSystem.WoodRegrowthPerYear * years, map.Resource[map.Index(13, 10)], 1e-3f);
        }

        [Test]
        public void Scorched_Heals_And_Fertility_MovesTowardBaseline()
        {
            var ctx = Forest(wooded: false);
            var map = ctx.State.Map;
            int scorched = map.Index(5, 5), poor = map.Index(6, 5), rich = map.Index(7, 5);
            map.Set(scorched, TileFlags.Scorched);
            map.Fertility[poor] = 0.1f;
            map.Fertility[rich] = 1f;
            float baseline = ResourceRegrowthSystem.Baseline(map, poor);
            Assert.That(baseline, Is.InRange(0.3f, 0.9f));

            var sys = new ResourceRegrowthSystem();
            Kit.Step(sys, ctx, Cycle);
            float years = Cycle * 48 / (float)SimTime.TicksPerYear;
            Assert.AreEqual(System.Math.Min(baseline, 0.1f + ResourceRegrowthSystem.FertilityRecoveryPerYear * years), map.Fertility[poor], 1e-4f);
            Assert.AreEqual(System.Math.Max(baseline, 1f - ResourceRegrowthSystem.FertilityDecayPerYear * years), map.Fertility[rich], 1e-4f);

            Kit.Step(sys, ctx, Cycle * 4);
            Assert.AreEqual(baseline, map.Fertility[poor], 1e-4f);
            Assert.AreEqual(baseline, map.Fertility[rich], 1e-4f);
            Assert.IsFalse(map.Has(scorched, TileFlags.Scorched), "scorched earth heals within decades");
        }

        [Test]
        public void EachRun_ProcessesOnlyOneWindow()
        {
            var ctx = TestWorlds.CreateContext(Kit.CreateDb(), 64, 1, Biome.Grassland);
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++) map.Fertility[i] = 0.1f;
            Kit.Step(new ResourceRegrowthSystem(), ctx);
            int window = (map.TileCount + Cycle - 1) / Cycle;
            int changed = 0, first = -1, last = -1;
            for (int i = 0; i < map.TileCount; i++)
            {
                if (map.Fertility[i] == 0.1f) continue;
                changed++;
                if (first < 0) first = i;
                last = i;
            }
            Assert.AreEqual(window, changed);
            Assert.AreEqual(window - 1, last - first, "the window is contiguous");

            Kit.Step(new ResourceRegrowthSystem(), ctx, Cycle - 1);
            for (int i = 0; i < map.TileCount; i++) Assert.AreNotEqual(0.1f, map.Fertility[i], "every tile is visited once per sweep");
        }

        [Test]
        public void PublishesAtMostOneBoundedTerrainEventPerRun()
        {
            var ctx = Forest();
            var map = ctx.State.Map;
            for (int y = 10; y <= 50; y += 4) for (int x = 3; x <= 60; x += 5) Clear(ctx, x, y);
            var events = new List<TerrainChangedEvent>();
            ctx.Events.Subscribe<TerrainChangedEvent>(e => events.Add(e));
            var sys = new ResourceRegrowthSystem();
            int total = 0;
            for (int run = 0; run < Cycle * 2; run++)
            {
                events.Clear();
                int version = map.TerrainVersion;
                Kit.Step(sys, ctx);
                Assert.IsTrue(events.Count <= 1);
                if (events.Count == 0) { Assert.AreEqual(version, map.TerrainVersion); continue; }
                total++;
                var e = events[0];
                Assert.AreNotEqual(version, map.TerrainVersion);
                Assert.That(e.MinX, Is.InRange(0, e.MaxX));
                Assert.That(e.MinY, Is.InRange(0, e.MaxY));
                Assert.IsTrue(e.MaxX < map.Width);
                Assert.IsTrue(e.MaxY < map.Height);
            }
            Assert.IsTrue(total > 0);
        }

        [Test]
        public void Trees_DoNotRegrowOnUsedOrUnsuitableLand()
        {
            var ctx = Forest();
            var map = ctx.State.Map;
            for (int x = 20; x <= 26; x++) Clear(ctx, x, 20);
            map.Set(map.Index(20, 20), TileFlags.Farmland);
            Kit.SetFlag(ctx, 21, 20, TileFlags.OreDeposit, 300f);
            map.BuildingAt[map.Index(22, 20)] = 999;
            map.Set(map.Index(23, 20), TileFlags.Road);
            map.Set(map.Index(24, 20), TileFlags.Burning);
            map.Biomes[map.Index(25, 20)] = Biome.Grassland;
            // (26,20) is a plain clearing inside the forest: the control tile

            Kit.Step(new ResourceRegrowthSystem(), ctx, Cycle * 3);

            for (int x = 20; x <= 25; x++) Assert.IsFalse(map.Has(map.Index(x, 20), TileFlags.Trees), "tile " + x + " regrew");
            Assert.AreEqual(300f, map.Resource[map.Index(21, 20)], 1e-4f, "deposits are not touched");
            Assert.IsTrue(map.Has(map.Index(26, 20), TileFlags.Trees), "control clearing regrew");
        }

        [Test]
        public void SameSeed_SameForest()
        {
            TileFlags[] a = RunClearcut(11), b = RunClearcut(11);
            Assert.AreEqual(a, b);
        }

        private static TileFlags[] RunClearcut(ulong seed)
        {
            var ctx = Forest(seed);
            for (int y = 0; y < 64; y += 2) for (int x = 0; x < 64; x += 3) Clear(ctx, x, y);
            TestWorlds.Run(new ResourceRegrowthSystem(), ctx, 48 * Cycle);
            return (TileFlags[])ctx.State.Map.Flags.Clone();
        }

        [Test]
        public void ZeroDeltaTicks_DoesNothing()
        {
            var ctx = Forest(wooded: false);
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++) map.Fertility[i] = 0.1f;
            ctx.DeltaTicks = 0;
            ctx.State.Tick = 48;
            new ResourceRegrowthSystem().Tick(ctx);
            for (int i = 0; i < map.TileCount; i++) Assert.AreEqual(0.1f, map.Fertility[i]);
        }
    }
}
