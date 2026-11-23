using GodSim.Core;
using GodSim.Core.Divine;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Divine.Nature
{
    public class SeasonWeatherSystemTests
    {
        private static void Step(ISimSystem sys, SimContext ctx)
        {
            ctx.State.Tick++;
            ctx.DeltaTicks = sys.TickInterval;
            sys.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        [Test]
        public void Seasons_Cycle_And_Yield_Is_Highest_In_Summer_Lowest_In_Winter()
        {
            var ctx = NatureTestWorlds.Flat(7);
            var sys = new SeasonWeatherSystem();
            sys.Initialize(ctx);
            var sum = new double[4];
            var n = new int[4];
            for (int t = 0; t < SimTime.TicksPerYear * 6; t++)
            {
                Step(sys, ctx);
                var w = ctx.State.Weather;
                Assert.AreEqual(SimTime.SeasonOf(ctx.State.Tick), w.Season);
                Assert.That(w.FoodYieldMultiplier, Is.InRange(NaturalEffects.MinFoodYield, NaturalEffects.MaxFoodYield));
                sum[(int)w.Season] += w.FoodYieldMultiplier;
                n[(int)w.Season]++;
            }
            for (int i = 0; i < 4; i++) Assert.Greater(n[i], 0, "season " + (Season)i + " never seen");
            double spring = sum[0] / n[0], summer = sum[1] / n[1], autumn = sum[2] / n[2], winter = sum[3] / n[3];
            Assert.Greater(summer, spring);
            Assert.Greater(summer, autumn);
            Assert.Greater(spring, winter);
            Assert.Greater(autumn, winter);
            Assert.Less(winter, 0.6);
        }

        [Test]
        public void SeasonalBaseYield_Matches_Design()
        {
            Assert.AreEqual(1.05f, NaturalEffects.SeasonalBaseYield(Season.Spring), 1e-6f);
            Assert.AreEqual(1.25f, NaturalEffects.SeasonalBaseYield(Season.Summer), 1e-6f);
            Assert.AreEqual(1.1f, NaturalEffects.SeasonalBaseYield(Season.Autumn), 1e-6f);
            Assert.AreEqual(0.4f, NaturalEffects.SeasonalBaseYield(Season.Winter), 1e-6f);
            Assert.AreEqual(1.25f, NaturalEffects.ComputeFoodYield(Season.Summer, 1f, false, false), 1e-6f);
            Assert.AreEqual(0.625f, NaturalEffects.ComputeFoodYield(Season.Summer, 1f, true, false), 1e-6f);
            Assert.AreEqual(1.375f, NaturalEffects.ComputeFoodYield(Season.Summer, 1f, false, true), 1e-5f);
            Assert.AreEqual(0.1f, NaturalEffects.ComputeFoodYield(Season.Winter, 0f, true, false), 0.2f);
            Assert.That(NaturalEffects.ComputeFoodYield(Season.Winter, float.NaN, true, false), Is.InRange(0.1f, 2f));
            Assert.That(NaturalEffects.ComputeFoodYield(Season.Summer, 99f, false, true), Is.LessThanOrEqualTo(2f));
        }

        [Test]
        public void Drought_Halves_Yield_And_Counts_Down()
        {
            var dry = NatureTestWorlds.Flat(3);
            var wet = NatureTestWorlds.Flat(3);
            var a = new SeasonWeatherSystem();
            var b = new SeasonWeatherSystem();
            a.Initialize(dry);
            b.Initialize(wet);
            dry.State.Weather.DroughtTicksRemaining = 10;
            Step(a, dry);
            Step(b, wet);
            float ratio = dry.State.Weather.FoodYieldMultiplier / wet.State.Weather.FoodYieldMultiplier;
            Assert.AreEqual(0.5f, ratio, 0.02f);
            Assert.AreEqual(9, dry.State.Weather.DroughtTicksRemaining);
            for (int i = 0; i < 9; i++) { Step(a, dry); Step(b, wet); }
            Assert.AreEqual(0, dry.State.Weather.DroughtTicksRemaining);
            Assert.AreEqual(0.5f, dry.State.Weather.FoodYieldMultiplier / wet.State.Weather.FoodYieldMultiplier, 0.05f, "last drought tick still halves");
            Step(a, dry);
            Step(b, wet);
            Assert.Greater(dry.State.Weather.FoodYieldMultiplier, 0.8f * wet.State.Weather.FoodYieldMultiplier, "yield recovers after the drought");
            Assert.AreEqual(0, dry.State.Weather.DroughtTicksRemaining);
        }

        [Test]
        public void Rain_Boosts_Yield_And_Counts_Down()
        {
            var ctx = NatureTestWorlds.Flat(4);
            var sys = new SeasonWeatherSystem();
            sys.Initialize(ctx);
            ctx.State.Weather.RainTicksRemaining = 3;
            Step(sys, ctx);
            var w = ctx.State.Weather;
            Assert.AreEqual(2, w.RainTicksRemaining);
            float expected = NaturalEffects.ComputeFoodYield(w.Season, w.Rainfall, false, true);
            Assert.AreEqual(expected, w.FoodYieldMultiplier, 1e-5f);
            Step(sys, ctx);
            Step(sys, ctx);
            Step(sys, ctx);
            Assert.AreEqual(0, w.RainTicksRemaining);
            Assert.AreEqual(NaturalEffects.ComputeFoodYield(w.Season, w.Rainfall, false, false), w.FoodYieldMultiplier, 1e-5f);
        }

        [Test]
        public void Weather_Stays_In_Range_And_Storms_Favor_Autumn_And_Winter()
        {
            var ctx = NatureTestWorlds.Flat(11);
            var sys = new SeasonWeatherSystem();
            sys.Initialize(ctx);
            var w = ctx.State.Weather;
            int warmStarts = 0, coldStarts = 0;
            float prevStorm = 0f;
            for (int t = 0; t < SimTime.TicksPerYear * 300; t++)
            {
                Step(sys, ctx);
                Assert.That(w.Rainfall, Is.InRange(0.5f, 1.5f));
                Assert.That(w.TemperatureOffset, Is.InRange(-0.2f, 0.2f));
                Assert.That(w.StormIntensity, Is.InRange(0f, 1f));
                Assert.That(w.FoodYieldMultiplier, Is.InRange(0.1f, 2f));
                if (w.StormIntensity > prevStorm + 0.2f)
                {
                    if (w.Season == Season.Autumn || w.Season == Season.Winter) coldStarts++; else warmStarts++;
                }
                prevStorm = w.StormIntensity;
            }
            Assert.Greater(coldStarts, warmStarts * 2, "autumn/winter storms should dominate");
            Assert.Greater(warmStarts + coldStarts, 50);
        }

        [Test]
        public void Climate_Oscillation_Is_Deterministic_Bounded_And_Slow()
        {
            float min = 1f, max = -1f;
            for (long t = 0; t < SimTime.TicksPerYear * 1000L; t += SimTime.TicksPerYear)
            {
                float c = NaturalEffects.ClimateOscillation(42, t);
                Assert.AreEqual(c, NaturalEffects.ClimateOscillation(42, t));
                Assert.That(c, Is.InRange(-NaturalEffects.ClimateAmplitude, NaturalEffects.ClimateAmplitude));
                if (c < min) min = c;
                if (c > max) max = c;
                float nextYear = NaturalEffects.ClimateOscillation(42, t + SimTime.TicksPerYear);
                Assert.Less(System.Math.Abs(nextYear - c), 0.02f, "climate must drift slowly");
            }
            Assert.Greater(max - min, 0.15f, "over a millennium the climate should swing noticeably");
            Assert.AreNotEqual(NaturalEffects.ClimateOscillation(1, 5000), NaturalEffects.ClimateOscillation(2, 5000));
        }

        [Test]
        public void Winter_Exposure_Hurts_Homeless_In_Cold_Biomes_Only()
        {
            // Pick a seed whose climate is mild in the first winter so the grassland town is not "cold".
            ulong seed = 1;
            while (NaturalEffects.ClimateOscillation(seed, SeasonWeatherSystem.ExposureDayOfYear) < -0.04f) seed++;
            var ctx = NatureTestWorlds.Flat(seed);
            var map = ctx.State.Map;
            NatureTestWorlds.Fill(map, 0, 0, 25, 25, Biome.Tundra, 0.2f);
            var cold = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(10, 10), 4, out _);
            var warm = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(45, 45), 4, out _);
            Assert.IsTrue(NaturalEffects.IsColdBiome(map.Biomes[map.Index(cold.Center)]));
            var house = ctx.Ops.CreateBuilding(cold, BuildingType.House, new Vec2i(14, 14), 0, true);
            var housed = ctx.State.People.Get(cold.ResidentIds[0]);
            Assert.IsTrue(ctx.Ops.AssignHome(housed, house));

            var sys = new SeasonWeatherSystem();
            sys.Initialize(ctx);
            // Spring..Autumn: nothing happens.
            for (int t = 0; t < SimTime.TicksPerSeason * 3 - 1; t++) Step(sys, ctx);
            foreach (var p in ctx.State.People.Items) Assert.AreEqual(1f, p.Health, 1e-6f);
            // Full winter: exactly one exposure.
            for (int t = 0; t < SimTime.TicksPerSeason; t++) Step(sys, ctx);
            Assert.Greater(ctx.State.Weather.TemperatureOffset, NaturalEffects.ColdClimateThreshold);
            foreach (int id in cold.ResidentIds)
            {
                var p = ctx.State.People.Get(id);
                Assert.AreEqual(p == housed ? 1f : 0.95f, p.Health, 1e-5f, p == housed ? "housed person is sheltered" : "homeless in tundra");
            }
            foreach (int id in warm.ResidentIds) Assert.AreEqual(1f, ctx.State.People.Get(id).Health, 1e-6f, "grassland is not cold");
        }

        [Test]
        public void Winter_Exposure_Clamps_Health_At_Zero_And_Cold_Climate_Counts()
        {
            Assert.IsTrue(NaturalEffects.IsExposureCold(Biome.Grassland, -0.12f));
            Assert.IsFalse(NaturalEffects.IsExposureCold(Biome.Grassland, 0f));
            Assert.IsTrue(NaturalEffects.IsExposureCold(Biome.Snow, 0.1f));
            Assert.IsTrue(NaturalEffects.IsExposureCold(Biome.Taiga, 0f));

            var ctx = NatureTestWorlds.Flat(5, 32);
            NatureTestWorlds.Fill(ctx.State.Map, 0, 0, 31, 31, Biome.Snow, 0.5f);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(16, 16), 2, out _);
            foreach (int id in s.ResidentIds) ctx.State.People.Get(id).Health = 0.02f;
            var sys = new SeasonWeatherSystem();
            sys.Initialize(ctx);
            ctx.State.Tick = SeasonWeatherSystem.ExposureDayOfYear - 1;
            Step(sys, ctx);
            foreach (int id in s.ResidentIds) Assert.AreEqual(0f, ctx.State.People.Get(id).Health, 1e-6f);
        }

        [Test]
        public void Sanitizes_Corrupt_Weather()
        {
            var ctx = NatureTestWorlds.Flat(9);
            var w = ctx.State.Weather;
            w.Rainfall = float.NaN;
            w.TemperatureOffset = float.PositiveInfinity;
            w.StormIntensity = -3f;
            w.DroughtTicksRemaining = -5;
            w.RainTicksRemaining = -1;
            w.FoodYieldMultiplier = float.NaN;
            var sys = new SeasonWeatherSystem();
            sys.Initialize(ctx);
            Step(sys, ctx);
            Assert.That(w.Rainfall, Is.InRange(0.5f, 1.5f));
            Assert.That(w.TemperatureOffset, Is.InRange(-1f, 1f));
            Assert.That(w.StormIntensity, Is.InRange(0f, 1f));
            Assert.That(w.FoodYieldMultiplier, Is.InRange(0.1f, 2f));
            Assert.AreEqual(0, w.DroughtTicksRemaining);
            Assert.AreEqual(0, w.RainTicksRemaining);
        }
    }
}
