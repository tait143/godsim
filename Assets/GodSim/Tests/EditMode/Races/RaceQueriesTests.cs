using System;
using GodSim.Core;
using GodSim.Core.Races;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Races
{
    /// <summary>RaceQueries: preferred biomes, tile habitability and symmetric affinity (incl. bad-input robustness).</summary>
    public class RaceQueriesTests
    {
        private const float Eps = 1e-5f;
        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = new GameDatabase();
            RaceCatalog.Register(_db);
            TestDb.FillMissing(_db);
            _db.Freeze();
        }

        // ------------------------------------------------------------------ PreferredBiomes

        [TestCase(RaceId.Human, Biome.Grassland)]
        [TestCase(RaceId.Elf, Biome.Forest)]
        [TestCase(RaceId.Dwarf, Biome.Hills)]
        [TestCase(RaceId.Orc, Biome.Savanna)]
        [TestCase(RaceId.Saurian, Biome.Swamp)]
        public void PreferredBiomes_StartWithTheHomeBiome_AndAreSortedDescending(RaceId id, Biome home)
        {
            var def = _db.Race(id);
            var list = RaceQueries.PreferredBiomes(def);
            Assert.IsTrue(list.Length >= 3);
            Assert.AreEqual(home, list[0]);
            for (int i = 0; i < list.Length; i++)
            {
                Assert.IsTrue(list[i].IsLand(), list[i] + " is water");
                Assert.IsTrue(def.BiomeHabitability[(int)list[i]] > 0f, list[i] + " is uninhabitable");
                if (i > 0)
                {
                    float prev = def.BiomeHabitability[(int)list[i - 1]], cur = def.BiomeHabitability[(int)list[i]];
                    Assert.IsTrue(prev > cur || (prev == cur && list[i - 1] < list[i]), "order broken at " + i);
                }
            }
            // Every inhabitable land biome is listed exactly once.
            int expected = 0;
            for (int b = 0; b < Counts.Biomes; b++) if (((Biome)b).IsLand() && def.BiomeHabitability[b] > 0f) expected++;
            Assert.AreEqual(expected, list.Length);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void PreferredBiomes_WithGoodThreshold_HasAtLeastThree(RaceId id)
        {
            var good = RaceQueries.PreferredBiomes(_db.Race(id), RaceQueries.GoodHabitability);
            Assert.IsTrue(good.Length >= 3, id + " has " + good.Length + " good biomes");
            foreach (var b in good) Assert.IsTrue(_db.Race(id).BiomeHabitability[(int)b] >= RaceQueries.GoodHabitability);
        }

        [Test]
        public void PreferredBiomes_BreaksTiesByEnumOrder_AndSkipsWaterAndZero()
        {
            var def = new RaceDefinition();
            for (int b = 0; b < Counts.Biomes; b++) def.BiomeHabitability[b] = 0.5f; // water too: must still be skipped
            def.BiomeHabitability[(int)Biome.Snow] = 0f;
            def.BiomeHabitability[(int)Biome.Desert] = 0.9f;
            def.BiomeHabitability[(int)Biome.Volcanic] = float.NaN;
            var list = RaceQueries.PreferredBiomes(def);
            Assert.AreEqual(Biome.Desert, list[0]);
            Assert.AreEqual(Biome.Beach, list[1]);
            Assert.AreEqual(Biome.Grassland, list[2]);
            Assert.AreEqual(Biome.Mountain, list[list.Length - 1]);
            foreach (var b in list)
            {
                Assert.IsFalse(b.IsWater());
                Assert.AreNotEqual(Biome.Snow, b);
                Assert.AreNotEqual(Biome.Volcanic, b);
            }
            Assert.AreEqual(11, list.Length); // 13 land biomes minus Snow (0) and Volcanic (NaN)
        }

        [Test]
        public void PreferredBiomes_BadInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, RaceQueries.PreferredBiomes(null).Length);
            Assert.AreEqual(0, RaceQueries.PreferredBiomes(new RaceDefinition { BiomeHabitability = null }).Length);
            Assert.AreEqual(0, RaceQueries.PreferredBiomes(new RaceDefinition { BiomeHabitability = new float[3] { 1f, 1f, 1f } }).Length);
            Assert.AreEqual(0, RaceQueries.PreferredBiomes(_db.Race(RaceId.Human), 1.5f).Length);
        }

        // ------------------------------------------------------------------ Habitability

        [Test]
        public void Habitability_PlainTile_EqualsBiomeValue()
        {
            var map = Map(Biome.Grassland);
            for (int i = 0; i < Counts.Races; i++)
            {
                var def = _db.Race((RaceId)i);
                Assert.AreEqual(def.BiomeHabitability[(int)Biome.Grassland], RaceQueries.Habitability(def, map, 5), Eps);
                Assert.AreEqual(def.BiomeHabitability[(int)Biome.Grassland], RaceQueries.Habitability(def, map, new Vec2i(3, 4)), Eps);
                Assert.AreEqual(def.BiomeHabitability[(int)Biome.Grassland], RaceQueries.BiomeValue(def, Biome.Grassland), Eps);
            }
        }

        [TestCase(Biome.DeepOcean)]
        [TestCase(Biome.ShallowWater)]
        [TestCase(Biome.Lake)]
        public void Habitability_WaterTile_IsZero_EvenWithFlags(Biome water)
        {
            var map = Map(water);
            map.Set(0, TileFlags.River | TileFlags.Coastal | TileFlags.FertileSoil);
            for (int i = 0; i < Counts.Races; i++)
            {
                Assert.AreEqual(0f, RaceQueries.Habitability(_db.Race((RaceId)i), map, 0));
                Assert.AreEqual(0f, RaceQueries.BiomeValue(_db.Race((RaceId)i), water));
            }
        }

        [Test]
        public void Habitability_BadInput_ReturnsZeroWithoutThrowing()
        {
            var map = Map(Biome.Grassland);
            var human = _db.Race(RaceId.Human);
            Assert.AreEqual(0f, RaceQueries.Habitability(null, map, 0));
            Assert.AreEqual(0f, RaceQueries.Habitability(human, null, 0));
            Assert.AreEqual(0f, RaceQueries.Habitability(human, null, new Vec2i(1, 1)));
            Assert.AreEqual(0f, RaceQueries.Habitability(human, map, -1));
            Assert.AreEqual(0f, RaceQueries.Habitability(human, map, map.TileCount));
            Assert.AreEqual(0f, RaceQueries.Habitability(human, map, int.MaxValue));
            Assert.AreEqual(0f, RaceQueries.Habitability(human, map, Vec2i.Invalid));
            Assert.AreEqual(0f, RaceQueries.Habitability(human, map, new Vec2i(map.Width, 0)));
            Assert.AreEqual(0f, RaceQueries.Habitability(new RaceDefinition { BiomeHabitability = null }, map, 0));
            Assert.AreEqual(0f, RaceQueries.Habitability(new RaceDefinition { BiomeHabitability = new float[4] }, map, 0));
            Assert.AreEqual(0f, RaceQueries.BiomeValue(human, (Biome)200));

            var broken = new RaceDefinition { Architecture = null };
            broken.BiomeHabitability[(int)Biome.Grassland] = float.NaN;
            Assert.AreEqual(0f, RaceQueries.Habitability(broken, map, 0));
            broken.BiomeHabitability[(int)Biome.Grassland] = float.PositiveInfinity;
            Assert.AreEqual(0f, RaceQueries.Habitability(broken, map, 0));
            broken.BiomeHabitability[(int)Biome.Grassland] = 7f;
            map.Set(0, TileFlags.Trees);
            Assert.AreEqual(1f, RaceQueries.Habitability(broken, map, 0), "out-of-range table values are clamped; null Architecture is tolerated");
        }

        [Test]
        public void Habitability_TileFlags_AdjustTheBiomeValue()
        {
            var map = Map(Biome.Hills);
            var human = _db.Race(RaceId.Human);
            float baseHills = human.BiomeHabitability[(int)Biome.Hills];

            map.Set(0, TileFlags.River);
            Assert.AreEqual(baseHills + RaceQueries.RiverBonus, RaceQueries.Habitability(human, map, 0), Eps);
            map.Set(1, TileFlags.FertileSoil);
            Assert.AreEqual(baseHills + RaceQueries.FertileSoilBonus, RaceQueries.Habitability(human, map, 1), Eps);
            map.Set(2, TileFlags.Scorched);
            Assert.AreEqual(baseHills - RaceQueries.ScorchedPenalty, RaceQueries.Habitability(human, map, 2), Eps);
            map.Set(3, TileFlags.OreDeposit);
            Assert.AreEqual(baseHills, RaceQueries.Habitability(human, map, 3), Eps, "Humans do not dig in");
        }

        [Test]
        public void Habitability_RewardsEachRacesBuildingHabits()
        {
            var dwarf = _db.Race(RaceId.Dwarf);
            var elf = _db.Race(RaceId.Elf);
            var saurian = _db.Race(RaceId.Saurian);

            var taiga = Map(Biome.Taiga);
            taiga.Set(0, TileFlags.StoneDeposit);
            Assert.AreEqual(dwarf.BiomeHabitability[(int)Biome.Taiga] + RaceQueries.DeepVeinBonus, RaceQueries.Habitability(dwarf, taiga, 0), Eps);
            taiga.Set(1, TileFlags.Trees);
            Assert.AreEqual(elf.BiomeHabitability[(int)Biome.Taiga] + RaceQueries.CanopyBonus, RaceQueries.Habitability(elf, taiga, 1), Eps);
            Assert.AreEqual(dwarf.BiomeHabitability[(int)Biome.Taiga], RaceQueries.Habitability(dwarf, taiga, 1), Eps);

            var beach = Map(Biome.Beach);
            beach.Set(0, TileFlags.Coastal);
            Assert.AreEqual(saurian.BiomeHabitability[(int)Biome.Beach] + RaceQueries.StiltWaterBonus, RaceQueries.Habitability(saurian, beach, 0), Eps);
            beach.Set(1, TileFlags.River);
            Assert.AreEqual(saurian.BiomeHabitability[(int)Biome.Beach] + RaceQueries.StiltWaterBonus + RaceQueries.RiverBonus,
                RaceQueries.Habitability(saurian, beach, 1), Eps);
        }

        [Test]
        public void Habitability_IsClampedToOne_AndZeroBiomesStayZero()
        {
            var forest = Map(Biome.Forest);
            forest.Set(0, TileFlags.Trees | TileFlags.River | TileFlags.FertileSoil);
            Assert.AreEqual(1f, RaceQueries.Habitability(_db.Race(RaceId.Elf), forest, 0), Eps);

            var snow = Map(Biome.Snow);
            snow.Set(0, TileFlags.River | TileFlags.FertileSoil | TileFlags.Coastal);
            Assert.AreEqual(0f, _db.Race(RaceId.Saurian).BiomeHabitability[(int)Biome.Snow]);
            Assert.AreEqual(0f, RaceQueries.Habitability(_db.Race(RaceId.Saurian), snow, 0), "bonuses never make a hostile biome livable");
        }

        [Test]
        public void Habitability_AllTilesOfAMixedMap_AreInUnitRange()
        {
            var map = new WorldMap(16, 16);
            for (int i = 0; i < map.TileCount; i++)
            {
                map.Biomes[i] = (Biome)(i % Counts.Biomes);
                map.Flags[i] = (TileFlags)(ushort)(i * 2654435761u >> 16);
            }
            for (int r = 0; r < Counts.Races; r++)
                for (int i = 0; i < map.TileCount; i++)
                {
                    float v = RaceQueries.Habitability(_db.Race((RaceId)r), map, i);
                    Assert.IsTrue(v >= 0f && v <= 1f && !float.IsNaN(v), (RaceId)r + " tile " + i + " = " + v);
                    if (map.Biomes[i].IsWater()) Assert.AreEqual(0f, v);
                }
        }

        // ------------------------------------------------------------------ AffinityBetween

        [Test]
        public void AffinityBetween_IsSymmetric_SelfIsOne_AndInRange()
        {
            for (int a = 0; a < Counts.Races; a++)
                for (int b = 0; b < Counts.Races; b++)
                {
                    float ab = RaceQueries.AffinityBetween(_db, (RaceId)a, (RaceId)b);
                    float ba = RaceQueries.AffinityBetween(_db, (RaceId)b, (RaceId)a);
                    Assert.AreEqual(ab, ba, (RaceId)a + "/" + (RaceId)b);
                    Assert.IsTrue(ab >= -1f && ab <= 1f);
                    if (a == b) Assert.AreEqual(1f, ab);
                    else
                    {
                        float expected = (_db.Race((RaceId)a).RaceAffinity[b] + _db.Race((RaceId)b).RaceAffinity[a]) * 0.5f;
                        Assert.AreEqual(expected, ab, Eps, "average of both directions");
                        Assert.AreEqual(ab, RaceQueries.AffinityBetween(_db.Race((RaceId)a), _db.Race((RaceId)b)), Eps);
                    }
                }
        }

        [TestCase(RaceId.Elf, RaceId.Orc, -0.6f)]
        [TestCase(RaceId.Elf, RaceId.Dwarf, -0.3f)]
        [TestCase(RaceId.Human, RaceId.Dwarf, 0.35f)]
        [TestCase(RaceId.Human, RaceId.Elf, 0.2f)]
        [TestCase(RaceId.Human, RaceId.Orc, -0.3f)]
        [TestCase(RaceId.Orc, RaceId.Dwarf, -0.4f)]
        [TestCase(RaceId.Saurian, RaceId.Orc, -0.1f)]
        public void AffinityBetween_KeyRelations_MatchBrief(RaceId a, RaceId b, float expected)
        {
            Assert.AreEqual(expected, RaceQueries.AffinityBetween(_db, a, b), 0.06f);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        public void AffinityBetween_SauriansAreMostlyNeutral(RaceId other)
        {
            float v = RaceQueries.AffinityBetween(_db, RaceId.Saurian, other);
            Assert.IsTrue(v >= 0f && v <= 0.1f + Eps, "Saurian/" + other + " = " + v);
        }

        [Test]
        public void AffinityBetween_OrcsAreHostileToEveryone()
        {
            for (int i = 0; i < Counts.Races; i++)
                if ((RaceId)i != RaceId.Orc) Assert.IsTrue(RaceQueries.AffinityBetween(_db, RaceId.Orc, (RaceId)i) < 0f, ((RaceId)i).ToString());
        }

        [Test]
        public void AffinityBetween_InvalidIds_ReturnZeroWithoutThrowing()
        {
            float v = 123f;
            Assert.DoesNotThrow(() => v = RaceQueries.AffinityBetween(_db, (RaceId)200, RaceId.Human));
            Assert.AreEqual(0f, v);
            Assert.DoesNotThrow(() => v = RaceQueries.AffinityBetween(_db, RaceId.Human, (RaceId)5));
            Assert.AreEqual(0f, v);
            Assert.DoesNotThrow(() => v = RaceQueries.AffinityBetween(_db, (RaceId)200, (RaceId)200));
            Assert.AreEqual(0f, v);
            Assert.AreEqual(0f, RaceQueries.AffinityBetween(null, RaceId.Human, RaceId.Elf));
            Assert.AreEqual(1f, RaceQueries.AffinityBetween(null, RaceId.Elf, RaceId.Elf));
            Assert.AreEqual(0f, RaceQueries.AffinityBetween(null, _db.Race(RaceId.Elf)));
            Assert.AreEqual(0f, RaceQueries.DirectedAffinity(_db.Race(RaceId.Elf), (RaceId)77));
            Assert.AreEqual(0f, RaceQueries.DirectedAffinity(null, RaceId.Elf));
        }

        [Test]
        public void AffinityBetween_DatabaseMissingARace_DoesNotThrow()
        {
            var db = new GameDatabase();
            db.Register(RaceCatalog.Create(RaceId.Human));
            db.Register(RaceCatalog.Create(RaceId.Elf)); // Dwarf, Orc and Saurian slots stay empty
            float v = 123f;
            Assert.DoesNotThrow(() => v = RaceQueries.AffinityBetween(db, RaceId.Human, RaceId.Orc));
            Assert.AreEqual(0f, v);
            Assert.AreEqual(1f, RaceQueries.AffinityBetween(db, RaceId.Orc, RaceId.Orc));
            Assert.AreEqual((0.2f + 0.15f) * 0.5f, RaceQueries.AffinityBetween(db, RaceId.Human, RaceId.Elf), Eps);
        }

        [Test]
        public void AffinityBetween_SanitizesMalformedTables()
        {
            var a = new RaceDefinition { Id = RaceId.Human };
            var b = new RaceDefinition { Id = RaceId.Orc };
            a.RaceAffinity[(int)RaceId.Orc] = float.NaN;
            b.RaceAffinity[(int)RaceId.Human] = -5f;
            Assert.AreEqual(-0.5f, RaceQueries.AffinityBetween(a, b), Eps); // (0 + -1) / 2
            b.RaceAffinity = new float[1]; // short table: only the Human entry (index 0, value 0) exists
            a.RaceAffinity = null;         // missing table reads as 0
            Assert.AreEqual(0f, RaceQueries.AffinityBetween(a, b), Eps);
            b.RaceAffinity = new float[0];
            Assert.AreEqual(0f, RaceQueries.DirectedAffinity(b, RaceId.Human));
        }

        // ------------------------------------------------------------------ helpers

        private static WorldMap Map(Biome fill)
        {
            var map = new WorldMap(16, 16);
            for (int i = 0; i < map.TileCount; i++) map.Biomes[i] = fill;
            return map;
        }
    }
}
