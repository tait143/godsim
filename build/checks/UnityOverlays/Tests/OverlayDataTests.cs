using System;
using GodSim.Core;
using GodSim.Tests.Shared;
using GodSim.Unity;
using NUnit.Framework;

namespace GodSim.Tests.UnityOverlays
{
    public class ColorRampsTests
    {
        [Test]
        public void ViridisEndpointsAndMonotonicLuminance()
        {
            Assert.AreEqual(0x440154FFu, ColorRamps.Viridis(0f));
            Assert.AreEqual(0xFDE725FFu, ColorRamps.Viridis(1f));
            Assert.AreEqual(ColorRamps.Viridis(0f), ColorRamps.Viridis(float.NaN));
            Assert.AreEqual(ColorRamps.Viridis(1f), ColorRamps.Viridis(5f));
            float prev = -1f;
            for (int i = 0; i <= 32; i++)
            {
                float l = ColorRamps.Luminance(ColorRamps.Viridis(i / 32f));
                Assert.IsTrue(l >= prev - 1e-3f, "viridis luminance must not decrease at " + i);
                prev = l;
            }
        }

        [Test]
        public void MagmaIsMonotonic()
        {
            float prev = -1f;
            for (int i = 0; i <= 32; i++)
            {
                float l = ColorRamps.Luminance(ColorRamps.Magma(i / 32f));
                Assert.IsTrue(l >= prev - 1e-3f);
                prev = l;
            }
        }

        [Test]
        public void DivergingIsRedWhiteBlue()
        {
            uint lo = ColorRamps.DivergingRedWhiteBlue(0f), mid = ColorRamps.DivergingRedWhiteBlue(0.5f), hi = ColorRamps.DivergingRedWhiteBlue(1f);
            Assert.IsTrue(ColorRamps.R(lo) > ColorRamps.B(lo));
            Assert.IsTrue(ColorRamps.B(hi) > ColorRamps.R(hi));
            Assert.AreEqual(0xF7F7F7FFu, mid);
            Assert.AreEqual(ColorRamps.DivergingRedWhiteBlue(0.2f), ColorRamps.DivergingBlueWhiteRed(0.8f));
        }

        [Test]
        public void CategoricalAndCivColors()
        {
            Assert.AreEqual(ColorRamps.Neutral, ColorRamps.Categorical(-1));
            for (int i = 0; i < 40; i++)
            {
                Assert.AreEqual(0xFFu, ColorRamps.Categorical(i) & 0xFFu, "opaque");
                Assert.AreNotEqual(ColorRamps.Categorical(i), ColorRamps.Categorical(i + 1));
            }
            Assert.AreEqual(0x112233FFu, ColorRamps.CivColor(0x11223380u, 3));          // honours banner, opaque
            Assert.AreEqual(ColorRamps.Categorical(3), ColorRamps.CivColor(ColorRamps.UnsetCivColor, 3));
            Assert.AreNotEqual(ColorRamps.CivColor(0x806040FFu, 1, 0), ColorRamps.CivColor(0x806040FFu, 2, 1));
            Assert.AreNotEqual(ColorRamps.CivColor(0x806040FFu, 2, 1), ColorRamps.CivColor(0x806040FFu, 3, 2));
            var lut = new uint[256];
            ColorRamps.FillLut(ColorRampId.Viridis, lut);
            Assert.AreEqual(ColorRamps.Viridis(0f), lut[0]);
            Assert.AreEqual(ColorRamps.Viridis(1f), lut[255]);
        }
    }

    public class OverlayDataTests
    {
        private SimContext _ctx;
        private Settlement _s;
        private Civilization _civ;
        private readonly OverlayField _field = new OverlayField();

        [SetUp]
        public void SetUp()
        {
            _ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 64);
            _s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(20, 20), 12, out _civ);
        }

        private WorldMap Map => _ctx.State.Map;

        [Test]
        public void TerritoryUsesCivIdOnOwnedTilesOnly()
        {
            OverlayData.Compute(_ctx.State, OverlayMode.Territory, _field);
            Assert.IsTrue(_field.IsComplete);
            Assert.AreEqual((float)_civ.Id, _field.Values[Map.Index(20, 20)]);
            Assert.IsTrue(float.IsNaN(_field.Values[Map.Index(60, 60)]));
            Assert.AreEqual(0f, _field.Weights[Map.Index(60, 60)]);
            // border tile is emphasised
            int r = _s.TerritoryRadius;
            Assert.AreEqual(1f, _field.Weights[Map.Index(20 + r, 20)]);
            Assert.AreEqual(OverlayData.TerritoryInteriorWeight, _field.Weights[Map.Index(20, 20)]);
        }

        [Test]
        public void HappinessSplatsWithFalloffInWeight()
        {
            _s.Happiness = 0.8f;
            OverlayData.Compute(_ctx.State, OverlayMode.Happiness, _field);
            var edge = new Vec2i(20, 20 - _s.TerritoryRadius);   // claimed around (20,20); center then moved to the town center
            Assert.AreEqual(_s.Id, Map.Owner[Map.Index(edge)]);
            int c = Map.Index(_s.Center), e = Map.Index(edge);
            Assert.AreEqual(0.8f, _field.Values[c], 1e-5f);
            Assert.AreEqual(0.8f, _field.Values[e], 1e-5f);
            Assert.AreEqual(1f, _field.Weights[c], 1e-5f);
            Assert.AreEqual(OverlayData.MinFalloffWeight, _field.Weights[e], 1e-5f);
            Assert.IsTrue(float.IsNaN(_field.Values[Map.Index(60, 60)]));
            Assert.AreEqual(0f, _field.Min);
            Assert.AreEqual(1f, _field.Max);
            Assert.AreEqual(ColorRampId.DivergingRedWhiteBlue, OverlayData.RampFor(OverlayMode.Happiness));
        }

        [Test]
        public void PopulationDensitySumsToPopulation()
        {
            OverlayData.Compute(_ctx.State, OverlayMode.PopulationDensity, _field);
            double sum = 0;
            for (int i = 0; i < _field.TileCount; i++) if (!float.IsNaN(_field.Values[i])) sum += _field.Values[i];
            Assert.AreEqual(_s.Population, sum, 1e-3);
            Assert.IsTrue(_field.Values[Map.Index(_s.Center)] > _field.Values[Map.Index(20, 20 - _s.TerritoryRadius)]);
            Assert.IsTrue(_field.Max >= _field.DataMax);
        }

        [Test]
        public void WealthAgeAndReligion()
        {
            _s.Stockpile.Add(ResourceType.Gold, 42f);
            OverlayData.Compute(_ctx.State, OverlayMode.Wealth, _field);
            Assert.AreEqual(_s.Stockpile.Get(ResourceType.Gold), _field.Values[Map.Index(20, 20)], 1e-4f);

            _civ.Age = AgeId.Iron;
            OverlayData.Compute(_ctx.State, OverlayMode.Age, _field);
            Assert.AreEqual((float)AgeId.Iron, _field.Values[Map.Index(20, 20)]);
            Assert.AreEqual("Iron", OverlayData.FormatValue(OverlayMode.Age, _field.Values[Map.Index(20, 20)]));

            OverlayData.Compute(_ctx.State, OverlayMode.Religion, _field);
            Assert.AreEqual(-1f, _field.Values[Map.Index(20, 20)], "no founded religion -> folk faith");
            _civ.Memory[OverlayData.ReligionFoundedKey] = 5f;
            OverlayData.Compute(_ctx.State, OverlayMode.Religion, _field);
            Assert.AreEqual((float)_civ.Id, _field.Values[Map.Index(20, 20)]);
        }

        [Test]
        public void TileFieldsMirrorTheMap()
        {
            Map.Elevation[Map.Index(5, 5)] = -0.75f;
            Map.Biomes[Map.Index(6, 6)] = Biome.DeepOcean;
            OverlayData.Compute(_ctx.State, OverlayMode.Elevation, _field);
            Assert.AreEqual(-0.75f, _field.Values[Map.Index(5, 5)]);
            Assert.AreEqual(-1f, _field.Min);
            Assert.AreEqual(1f, _field.Max);
            OverlayData.Compute(_ctx.State, OverlayMode.Fertility, _field);
            Assert.IsTrue(float.IsNaN(_field.Values[Map.Index(6, 6)]), "water has no fertility");
            Assert.AreEqual(0.6f, _field.Values[Map.Index(7, 7)], 1e-5f);
            Map.Resource[Map.Index(9, 9)] = 30f;
            OverlayData.Compute(_ctx.State, OverlayMode.Resources, _field);
            Assert.AreEqual(1, _field.ValidCount);
            Assert.AreEqual(30f, _field.Max);
        }

        [Test]
        public void DangerFromFireArmiesAndWarFronts()
        {
            var st = _ctx.State;
            Map.Set(Map.Index(50, 50), TileFlags.Burning);
            st.Diplomacy.Armies.Add(new Army { Id = st.Ids.Army(), Position = new Vec2i(50, 10), State = ArmyState.Marching, Strength = 100f, WarId = 1 });
            OverlayData.Compute(st, OverlayMode.Danger, _field);
            Assert.AreEqual(1f, _field.Values[Map.Index(50, 50)], 1e-5f);
            float near = _field.Values[Map.Index(52, 50)];
            Assert.AreEqual(1f - 2 * OverlayData.DangerDecayPerTile, near, 1e-4f);
            Assert.AreEqual(0.9f, _field.Values[Map.Index(50, 10)], 1e-4f);
            Assert.IsTrue(float.IsNaN(_field.Values[Map.Index(30, 50)]), "far from danger is transparent");

            // two civs touching, then at war -> front tiles light up
            Civilization civB;
            var b = TestWorlds.AddSettlement(_ctx, RaceId.Orc, new Vec2i(32, 20), 4, out civB);
            int frontX = -1;
            for (int x = 20; x < 40; x++)
                if (Map.Owner[Map.Index(x, 20)] == _s.Id && Map.Owner[Map.Index(x + 1, 20)] == b.Id) { frontX = x; break; }
            Assert.IsTrue(frontX >= 0, "territories should touch");
            OverlayData.Compute(st, OverlayMode.Danger, _field);
            Assert.IsTrue(float.IsNaN(_field.Values[Map.Index(frontX, 20)]), "peace: no front");
            st.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = _civ.Id, DefenderId = civB.Id, IsActive = true });
            OverlayData.Compute(st, OverlayMode.Danger, _field);
            Assert.AreEqual(OverlayData.WarFrontDanger, _field.Values[Map.Index(frontX, 20)], 1e-4f);
            Assert.AreEqual(OverlayData.WarFrontDanger, _field.Values[Map.Index(frontX + 1, 20)], 1e-4f);
        }

        [Test]
        public void SlicedComputationMatchesSinglePass()
        {
            _s.Happiness = 0.3f;
            Map.Set(Map.Index(40, 40), TileFlags.Burning);
            var single = new OverlayField();
            foreach (OverlayMode mode in Enum.GetValues(typeof(OverlayMode)))
            {
                OverlayData.Compute(_ctx.State, mode, single);
                Assert.IsTrue(OverlayData.Begin(_ctx.State, mode, _field));
                for (int y = 0; y < _field.Height; y += 7) OverlayData.ComputeRows(_ctx.State, _field, y, y + 7);
                OverlayData.End(_ctx.State, _field);
                for (int i = 0; i < single.TileCount; i++)
                {
                    float a = single.Values[i], b = _field.Values[i];
                    Assert.IsTrue(a.Equals(b), mode + " value mismatch at " + i);
                    Assert.AreEqual(single.Weights[i], _field.Weights[i], mode + " weight mismatch at " + i);
                }
                Assert.AreEqual(single.Min, _field.Min);
                Assert.AreEqual(single.Max, _field.Max);
            }
        }

        [Test]
        public void BuffersAreReusedAndInputsSanitized()
        {
            OverlayData.Compute(_ctx.State, OverlayMode.Health, _field);
            float[] values = _field.Values, weights = _field.Weights;
            _s.Health = float.NaN;
            _s.Faith = float.PositiveInfinity;
            OverlayData.Compute(_ctx.State, OverlayMode.Health, _field);
            Assert.AreSame(values, _field.Values);
            Assert.AreSame(weights, _field.Weights);
            Assert.AreEqual(1f, _field.Values[Map.Index(20, 20)], "NaN health falls back");
            OverlayData.Compute(_ctx.State, OverlayMode.Faith, _field);
            Assert.AreEqual(0.5f, _field.Values[Map.Index(20, 20)], "infinite faith falls back");
            for (int i = 0; i < _field.TileCount; i++)
                Assert.IsFalse(float.IsNaN(_field.Weights[i]) || float.IsInfinity(_field.Values[i]));

            // owner id of a destroyed/unknown settlement is ignored
            Map.Owner[Map.Index(1, 1)] = 9999;
            OverlayData.Compute(_ctx.State, OverlayMode.Territory, _field);
            Assert.IsTrue(float.IsNaN(_field.Values[Map.Index(1, 1)]));
        }

        [Test]
        public void NullSafetyAndMetadata()
        {
            Assert.DoesNotThrow(() => OverlayData.Compute(null, OverlayMode.Danger, _field));
            Assert.IsTrue(_field.IsComplete);
            Assert.AreEqual(0, _field.ValidCount);
            Assert.DoesNotThrow(() => OverlayData.Compute(_ctx.State, OverlayMode.Happiness, null));
            Assert.DoesNotThrow(() => OverlayData.ComputeRows(_ctx.State, _field, -5, 999));
            foreach (OverlayMode mode in Enum.GetValues(typeof(OverlayMode)))
            {
                Assert.IsNotNull(OverlayData.FormatValue(mode, 0.5f));
                Assert.IsNotNull(OverlayData.RangeLabel(mode, 0.5f, true));
                if (mode != OverlayMode.None) Assert.IsNotEmpty(OverlayData.Title(mode));
            }
            Assert.AreEqual("50%", OverlayData.FormatValue(OverlayMode.Happiness, 0.5f));
            Assert.AreEqual("-0.3", OverlayData.FormatValue(OverlayMode.Elevation, -0.25f));
            Assert.AreEqual("-", OverlayData.FormatValue(OverlayMode.Wealth, float.NaN));
        }
    }
}
