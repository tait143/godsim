using GodSim.Core;
using GodSim.Core.Divine;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Divine.Powers
{
    public class GodPowerEffectsTests
    {
        private SimContext _ctx;
        private GodCommandSystem _sys;
        private DivineEventLog _log;

        [SetUp]
        public void SetUp()
        {
            _ctx = PowersTestKit.CreateContext();
            _sys = new GodCommandSystem();
            _log = new DivineEventLog(_ctx.Events);
        }

        private void Cast(GodPowerType power, int x, int y, int radius, int parameter = 0)
        {
            Assert.IsTrue(_sys.Execute(_ctx, PowersTestKit.Cmd(power, x, y, radius, parameter), out string reason), power + ": " + reason);
        }

        // ------------------------------------------------------------------ shaping

        [Test]
        public void RaiseLand_CreatesLandFromWater_WithCoastAndTerrainEvent()
        {
            _ctx = PowersTestKit.CreateContext(null, Biome.ShallowWater);
            _log = new DivineEventLog(_ctx.Events);
            var c = new Vec2i(32, 32);
            PowersTestKit.SetElevationDisc(_ctx, c, 3, -0.05f, Biome.ShallowWater);
            var map = _ctx.State.Map;
            int version = map.TerrainVersion;

            Cast(GodPowerType.RaiseLand, 32, 32, 3);

            Assert.IsTrue(map.Biomes[map.Index(c)].IsLand(), "center rose above the sea");
            Assert.IsTrue(map.Elevation[map.Index(c)] > 0f);
            Assert.IsTrue(map.Elevation[map.Index(c)] <= 1f);
            Assert.IsTrue(map.TerrainVersion != version);
            Assert.AreEqual(1, _log.Terrain.Count);
            var t = _log.Terrain[0];
            Assert.IsTrue(t.MinX <= 29 && t.MaxX >= 35 && t.MinY <= 29 && t.MaxY >= 35, "bbox covers the radius");
            int coastal = 0, land = 0;
            for (int y = 26; y <= 38; y++)
                for (int x = 26; x <= 38; x++)
                {
                    int i = map.Index(x, y);
                    if (map.IsWater(i)) { Assert.IsFalse(map.Has(i, TileFlags.Coastal)); continue; }
                    land++;
                    if (map.Has(i, TileFlags.Coastal)) { coastal++; Assert.AreEqual(Biome.Beach, map.Biomes[i]); }
                }
            Assert.IsTrue(land > 0 && coastal > 0);
            Assert.AreEqual(Biome.Grassland, map.Biomes[map.Index(c)], "inland new ground is grassland");
            Assert.IsFalse(map.Has(map.Index(c), TileFlags.Coastal));
        }

        [Test]
        public void RaiseLand_HighGroundBecomesHillsAndMountains()
        {
            var map = _ctx.State.Map;
            PowersTestKit.SetElevationDisc(_ctx, new Vec2i(20, 20), 3, 0.40f, Biome.Grassland);
            PowersTestKit.SetElevationDisc(_ctx, new Vec2i(40, 40), 3, 0.58f, Biome.Hills);
            Cast(GodPowerType.RaiseLand, 20, 20, 3);
            Assert.AreEqual(Biome.Hills, map.Biomes[map.Index(20, 20)]);
            _ctx.State.Tick += 2;
            Cast(GodPowerType.RaiseLand, 40, 40, 3);
            Assert.AreEqual(Biome.Mountain, map.Biomes[map.Index(40, 40)]);
        }

        [Test]
        public void LowerLand_FloodsAndDestroysBuilding_WithTerrainEvent()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(48, 48), 2, out _);
            var c = new Vec2i(20, 20);
            PowersTestKit.SetElevationDisc(_ctx, c, 3, 0.05f, Biome.Grassland);
            var map = _ctx.State.Map;
            map.Set(map.Index(c), TileFlags.Trees | TileFlags.OreDeposit);
            var house = _ctx.Ops.CreateBuilding(s, BuildingType.House, c, 0, true);
            Assert.IsNotNull(house);

            Cast(GodPowerType.LowerLand, 20, 20, 3);

            Assert.IsTrue(map.IsWater(c));
            Assert.AreEqual(TileFlags.None, map.Flags[map.Index(c)], "trees, deposits and ruins are submerged");
            Assert.IsTrue(house.IsDestroyed);
            Assert.AreEqual(-1, map.BuildingAt[map.Index(c)]);
            Assert.AreEqual(1, _log.Destroyed.Count);
            Assert.AreEqual(DestroyCause.DivinePower, _log.Destroyed[0].Cause);
            Assert.AreEqual(1, _log.Terrain.Count);
            Assert.IsTrue(map.Has(map.Index(20, 22), TileFlags.Coastal), "new shoreline is coastal");
            Assert.IsTrue(s.IsAlive, "the owning settlement elsewhere survives");
        }

        [Test]
        public void LowerLand_DrownedSettlementWithoutBuildings_IsAbandoned()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(20, 20), 4, out var civ);
            PowersTestKit.SetElevationDisc(_ctx, s.Center, 4, 0.03f, Biome.Grassland);
            Cast(GodPowerType.LowerLand, s.Center.X, s.Center.Y, 4);
            _ctx.Ops.FlushRemovals();
            Assert.IsFalse(s.IsAlive);
            Assert.IsFalse(civ.IsAlive, "its only settlement sank");
            Assert.AreEqual(0, PowersTestKit.Alive(_ctx));
        }

        [Test]
        public void LowerLand_DeepWaterBecomesDeepOcean_AndElevationClamps()
        {
            var map = _ctx.State.Map;
            PowersTestKit.SetElevationDisc(_ctx, new Vec2i(30, 30), 2, -0.98f, Biome.DeepOcean);
            for (int k = 0; k < 3; k++) { Cast(GodPowerType.LowerLand, 30, 30, 2); _ctx.State.Tick += 2; }
            Assert.AreEqual(-1f, map.Elevation[map.Index(30, 30)]);
            Assert.AreEqual(Biome.DeepOcean, map.Biomes[map.Index(30, 30)]);
        }

        [Test]
        public void PlantForest_PlantsTreesOnFreeLandOnly()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(20, 20), 2, out _);
            var map = _ctx.State.Map;
            map.Set(map.Index(new Vec2i(16, 20)), TileFlags.Road);
            Cast(GodPowerType.PlantForest, 18, 20, 4);
            Assert.IsTrue(map.Has(new Vec2i(18, 18), TileFlags.Trees));
            Assert.AreEqual(Biome.Forest, map.Biomes[map.Index(18, 18)]);
            Assert.IsTrue(map.Resource[map.Index(18, 18)] >= 60f);
            Assert.IsFalse(map.Has(new Vec2i(16, 20), TileFlags.Trees), "roads stay clear");
            var tc = _ctx.State.Buildings.Get(s.TownCenterId);
            Assert.IsFalse(map.Has(tc.Origin, TileFlags.Trees), "no trees under buildings");
        }

        [Test]
        public void RevealOre_AddsThreeToEightDeposits_PreferringHills()
        {
            var map = _ctx.State.Map;
            PowersTestKit.SetElevationDisc(_ctx, new Vec2i(30, 30), 2, 0.5f, Biome.Hills);
            Cast(GodPowerType.RevealOre, 30, 30, 5);
            int deposits = 0, onHills = 0;
            for (int i = 0; i < map.TileCount; i++)
            {
                if ((map.Flags[i] & (TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit)) == 0) continue;
                deposits++;
                Assert.IsTrue(map.Resource[i] > 0f);
                if (map.Biomes[i] == Biome.Hills) onHills++;
            }
            Assert.IsTrue(deposits >= 3 && deposits <= 8, "deposits " + deposits);
            Assert.AreEqual(deposits, onHills, "hills are preferred");
        }

        [Test]
        public void SpawnTribe_CreatesCivSettlementAndPeople()
        {
            int civs = _ctx.State.Civilizations.Count;
            Cast(GodPowerType.SpawnTribe, 32, 32, 0, (int)RaceId.Elf);
            Assert.AreEqual(civs + 1, _ctx.State.Civilizations.Count);
            var civ = _ctx.State.Civilizations.Items[_ctx.State.Civilizations.Count - 1];
            Assert.AreEqual(RaceId.Elf, civ.Race);
            Assert.AreEqual(0.9f, civ.Devotion, 1e-5f);
            Assert.AreEqual(0.5f, civ.GodFavor, 1e-5f);
            Assert.AreEqual(1, civ.SettlementIds.Count);
            var s = _ctx.State.Settlements.Get(civ.SettlementIds[0]);
            Assert.IsTrue(s.TownCenterId >= 0, "town center placed");
            var people = PowersTestKit.Residents(_ctx, s);
            Assert.AreEqual(_ctx.Config.InitialPopulationPerRace, people.Count);
            int females = 0;
            foreach (var p in people)
            {
                Assert.AreEqual(RaceId.Elf, p.Race);
                Assert.AreEqual(civ.Id, p.CivilizationId);
                Assert.IsTrue(p.AgeYears >= 14f && p.AgeYears <= 40f, "age " + p.AgeYears);
                if (p.Sex == Sex.Female) females++;
            }
            Assert.IsTrue(females > 0 && females < people.Count, "mixed sexes");
            Assert.AreEqual(150f, 5000f - _ctx.State.God.Mana, 1e-3f);
            var last = _ctx.State.Chronicle.Entries[_ctx.State.Chronicle.Entries.Count - 1];
            Assert.AreEqual(ChronicleCategory.Divine, last.Category);
            Assert.AreEqual(3, last.Importance);
        }

        [TestCase(99)]
        [TestCase(-1)]
        [TestCase(5)]
        public void SpawnTribe_InvalidRace_IsRejected(int race)
        {
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.SpawnTribe, 32, 32, 0, race), out string reason));
            Assert.IsTrue(reason.Contains("race"), reason);
            Assert.AreEqual(0, _ctx.State.Civilizations.Count);
            Assert.AreEqual(5000f, _ctx.State.God.Mana);
        }

        [Test]
        public void SpawnTribe_BadLand_IsRejected()
        {
            var water = PowersTestKit.CreateContext(null, Biome.ShallowWater);
            Assert.IsFalse(_sys.Execute(water, PowersTestKit.Cmd(GodPowerType.SpawnTribe, 32, 32, 0, 0), out string reason));
            Assert.IsTrue(reason.Contains("land"), reason);

            // A tiny island: land target but far fewer than 30 buildable tiles.
            PowersTestKit.SetElevationDisc(water, new Vec2i(32, 32), 2, 0.1f, Biome.Grassland);
            Assert.IsFalse(_sys.Execute(water, PowersTestKit.Cmd(GodPowerType.SpawnTribe, 32, 32, 0, 0), out reason));
            Assert.IsTrue(reason.Contains("open land"), reason);

            // Mountain target.
            PowersTestKit.SetElevationDisc(_ctx, new Vec2i(10, 10), 0, 0.7f, Biome.Mountain);
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.SpawnTribe, 10, 10, 0, 0), out reason));

            // Too close to an existing settlement.
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(40, 40), 2, out _);
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.SpawnTribe, 46, 40, 0, 0), out reason));
            Assert.AreEqual(1, _ctx.State.Civilizations.Count);
            Assert.AreEqual(0L, _ctx.State.God.CooldownUntil[(int)GodPowerType.SpawnTribe]);
        }

        // ------------------------------------------------------------------ blessings

        [Test]
        public void Heal_RestoresHealth_Bless_RaisesHappinessAndHealth()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 6, out _);
            var far = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(5, 5), 2, out _);
            foreach (var p in PowersTestKit.Residents(_ctx, s)) { p.Health = 0.3f; p.Happiness = 0.4f; }
            foreach (var p in PowersTestKit.Residents(_ctx, far)) p.Health = 0.3f;

            Cast(GodPowerType.Heal, s.Center.X, s.Center.Y, 3);
            foreach (var p in PowersTestKit.Residents(_ctx, s)) Assert.AreEqual(1f, p.Health);
            foreach (var p in PowersTestKit.Residents(_ctx, far)) Assert.AreEqual(0.3f, p.Health, "out of range");

            foreach (var p in PowersTestKit.Residents(_ctx, s)) p.Health = 0.5f;
            Cast(GodPowerType.Bless, s.Center.X, s.Center.Y, 3);
            foreach (var p in PowersTestKit.Residents(_ctx, s))
            {
                Assert.AreEqual(0.6f, p.Happiness, 1e-5f);
                Assert.AreEqual(0.6f, p.Health, 1e-5f);
            }
        }

        [Test]
        public void Inspire_AddsResearchPerPopulation()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 10, out var civ);
            Cast(GodPowerType.Inspire, 32, 32, 2);
            Assert.AreEqual(70f, civ.ResearchPool, 1e-4f);
            Assert.AreEqual(70f, civ.TotalResearch, 1e-4f);
        }

        [Test]
        public void Fertility_EnrichesSoilAndBringsBirths()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 10, out _);
            int births = 0;
            _ctx.Events.Subscribe<PersonBornEvent>(e => births++);
            Cast(GodPowerType.Fertility, 32, 32, 4);
            var map = _ctx.State.Map;
            Assert.IsTrue(map.Has(new Vec2i(30, 32), TileFlags.FertileSoil));
            Assert.AreEqual(0.85f, map.Fertility[map.Index(30, 32)], 1e-5f);
            Assert.AreEqual(3, births, "three couples per settlement");
            Assert.AreEqual(13, s.Population);
            int babies = 0;
            foreach (var p in PowersTestKit.Residents(_ctx, s)) if (p.AgeYears == 0f) { babies++; Assert.IsTrue(p.MotherId >= 0 && p.FatherId >= 0); }
            Assert.AreEqual(3, babies);
        }

        [Test]
        public void BountifulHarvest_AddsFoodBoundedByCapacity()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 4, out _);
            float before = s.Stockpile.Get(ResourceType.Food);
            Cast(GodPowerType.BountifulHarvest, 32, 32, 3);
            Assert.AreEqual(before + 40f, s.Stockpile.Get(ResourceType.Food), 1e-3f);
            s.Stockpile.SetRaw(ResourceType.Food, s.Stockpile.Capacity - 5f);
            _ctx.State.Tick += 48;
            Cast(GodPowerType.BountifulHarvest, 32, 32, 3);
            Assert.AreEqual(s.Stockpile.Capacity, s.Stockpile.Get(ResourceType.Food), 1e-3f);
        }

        [Test]
        public void Prophet_AnointsAnAdultAndRaisesDevotion()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 6, out var civ);
            civ.Devotion = 0.4f;
            Cast(GodPowerType.Prophet, 32, 32, 2);
            Person prophet = null;
            foreach (var p in PowersTestKit.Residents(_ctx, s)) if (p.HasTrait(PersonTraits.Hero)) { Assert.IsNull(prophet); prophet = p; }
            Assert.IsNotNull(prophet);
            Assert.IsTrue(prophet.HasTrait(PersonTraits.Devout) && prophet.HasTrait(PersonTraits.Charismatic));
            Assert.IsTrue(civ.Devotion >= 0.6f - 1e-5f, "devotion " + civ.Devotion);
            Assert.AreEqual(prophet.Id, (int)civ.Mem("divine.prophetId", -1f));
            var last = _ctx.State.Chronicle.Entries[_ctx.State.Chronicle.Entries.Count - 1];
            Assert.IsTrue(last.Text.Contains(prophet.Name));
        }

        [Test]
        public void Prophet_WithoutAdults_IsRejected()
        {
            var civ = _ctx.Ops.CreateCivilization(RaceId.Human);
            var s = _ctx.Ops.FoundSettlement(civ, new Vec2i(32, 32));
            _ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Female, 3f);
            Assert.IsFalse(_sys.Execute(_ctx, PowersTestKit.Cmd(GodPowerType.Prophet, 32, 32, 2), out string reason));
            Assert.IsTrue(reason.Contains("mortal"), reason);
            Assert.AreEqual(5000f, _ctx.State.God.Mana);
        }

        [Test]
        public void Rain_SetsWeatherAndDousesFires_Drought_Dries()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 2, out _);
            var tc = _ctx.State.Buildings.Get(s.TownCenterId);
            tc.IsOnFire = true;
            var map = _ctx.State.Map;
            map.Set(map.Index(30, 30), TileFlags.Burning);
            _ctx.State.Weather.DroughtTicksRemaining = 50;
            Cast(GodPowerType.Rain, 32, 32, 5);
            Assert.AreEqual(48, _ctx.State.Weather.RainTicksRemaining);
            Assert.AreEqual(0, _ctx.State.Weather.DroughtTicksRemaining);
            Assert.IsFalse(tc.IsOnFire);
            Assert.IsFalse(map.Has(map.Index(30, 30), TileFlags.Burning));
            Assert.AreEqual(0.65f, map.Moisture[map.Index(32, 32)], 1e-5f);

            Cast(GodPowerType.Drought, 32, 32, 5);
            Assert.AreEqual(96, _ctx.State.Weather.DroughtTicksRemaining);
            Assert.AreEqual(0, _ctx.State.Weather.RainTicksRemaining);
            Assert.AreEqual(0.45f, map.Moisture[map.Index(32, 32)], 1e-5f);
        }

        // ------------------------------------------------------------------ wrath

        [Test]
        public void Meteor_DestroysBuildingsKillsPeopleAndLeavesCrater()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 10, out _);
            var house = _ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(35, 33), 0, true);
            var tc = _ctx.State.Buildings.Get(s.TownCenterId);
            var map = _ctx.State.Map;
            float before = map.Elevation[map.Index(s.Center)];

            Cast(GodPowerType.Meteor, s.Center.X, s.Center.Y, 6);

            Assert.IsTrue(tc.IsDestroyed);
            Assert.IsTrue(house.IsDestroyed);
            Assert.AreEqual(0, s.Population);
            Assert.AreEqual(10, _log.Deaths.Count);
            foreach (var d in _log.Deaths) Assert.AreEqual(DeathCause.DivineWrath, d.Cause);
            Assert.IsTrue(map.Elevation[map.Index(s.Center)] < before);
            Assert.IsTrue(map.Has(new Vec2i(s.Center.X + 5, s.Center.Y), TileFlags.Scorched));
            Assert.AreEqual(1, _log.Disasters.Count);
            Assert.AreEqual(DisasterType.Meteor, _log.Disasters[0].Type);
            Assert.AreEqual(1, _log.Terrain.Count);
        }

        [Test]
        public void Lightning_KillsOneOrTwoAndIgnitesBuilding()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 8, out _);
            var tc = _ctx.State.Buildings.Get(s.TownCenterId);
            Cast(GodPowerType.Lightning, s.Center.X, s.Center.Y, 0);
            Assert.IsTrue(_log.Deaths.Count >= 1 && _log.Deaths.Count <= 2);
            Assert.IsTrue(tc.IsOnFire);
            Assert.IsTrue(_ctx.State.Map.Has(s.Center, TileFlags.Burning));
        }

        [Test]
        public void Wildfire_SetsTreesAndBuildingsBurning()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 2, out _);
            var map = _ctx.State.Map;
            map.Set(map.Index(30, 33), TileFlags.Trees);
            Cast(GodPowerType.Wildfire, 32, 32, 4);
            Assert.IsTrue(map.Has(map.Index(30, 33), TileFlags.Burning));
            Assert.IsTrue(_ctx.State.Buildings.Get(s.TownCenterId).IsOnFire);
            Assert.IsFalse(map.Has(map.Index(32, 40), TileFlags.Burning), "outside radius");
        }

        [Test]
        public void Earthquake_DamagesAndDestroysBuildings()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 20, out _);
            var house = _ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(30, 30), 0, true);
            house.Condition = 0.1f;
            var tc = _ctx.State.Buildings.Get(s.TownCenterId);
            Cast(GodPowerType.Earthquake, 32, 32, 5);
            Assert.IsTrue(house.IsDestroyed);
            Assert.IsTrue(tc.IsDestroyed || tc.Condition <= 0.85f, "condition " + tc.Condition);
            Assert.IsTrue(_log.Deaths.Count <= 2);
            foreach (var d in _log.Deaths) Assert.AreEqual(DeathCause.Disaster, d.Cause);
        }

        [Test]
        public void Plague_SickensAndKillsTenToTwentyFivePercent()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 40, out _);
            Cast(GodPowerType.Plague, 32, 32, 4);
            Assert.IsTrue(_log.Deaths.Count >= 4 && _log.Deaths.Count <= 10, "deaths " + _log.Deaths.Count);
            foreach (var d in _log.Deaths) Assert.AreEqual(DeathCause.Disease, d.Cause);
            foreach (var p in PowersTestKit.Residents(_ctx, s)) Assert.AreEqual(0.6f, p.Health, 1e-5f);
        }

        [Test]
        public void Flood_InundatesLowRiverland()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(40, 40), 2, out _);
            var map = _ctx.State.Map;
            for (int x = 10; x <= 30; x++) map.Set(map.Index(x, 20), TileFlags.River);
            map.Set(map.Index(20, 22), TileFlags.Farmland);
            PowersTestKit.SetElevationDisc(_ctx, new Vec2i(24, 20), 1, 0.6f, Biome.Hills);
            var hut = _ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(20, 21), 0, true);
            Cast(GodPowerType.Flood, 20, 20, 5);
            Assert.IsFalse(map.Has(map.Index(20, 22), TileFlags.Farmland));
            Assert.AreEqual(1f, map.Moisture[map.Index(20, 22)]);
            Assert.IsTrue(hut.Condition < 1f);
            Assert.AreEqual(0.5f, map.Moisture[map.Index(20, 25)], "too far from the river");
            Assert.AreEqual(0.5f, map.Moisture[map.Index(24, 21)], "hills stay dry");
        }

        // ------------------------------------------------------------------ robustness

        [Test]
        public void EveryEffect_IsSafeAtMapEdgesAndRadiusExtremes()
        {
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(2, 2), 6, out _);
            TestWorlds.AddSettlement(_ctx, RaceId.Orc, new Vec2i(61, 61), 6, out _);
            var targets = new[] { new Vec2i(0, 0), new Vec2i(63, 63), new Vec2i(0, 63), new Vec2i(63, 0) };
            foreach (var power in EnumUtil.AllGodPowers)
                foreach (var t in targets)
                    foreach (int r in new[] { 0, 1, 12, 1000, -5 })
                    {
                        var o = GodPowerEffects.Apply(_ctx, power, t, r, 0);
                        Assert.IsNotNull(o);
                        _ctx.Ops.FlushRemovals();
                    }
            var map = _ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++)
            {
                Assert.IsFalse(float.IsNaN(map.Elevation[i]) || map.Elevation[i] < -1f || map.Elevation[i] > 1f);
                Assert.IsFalse(float.IsNaN(map.Moisture[i]) || map.Moisture[i] < 0f || map.Moisture[i] > 1f);
            }
            Assert.IsFalse(GodPowerEffects.Apply(_ctx, GodPowerType.Bless, new Vec2i(-3, 70), 3, 0).Success);
            Assert.IsFalse(GodPowerEffects.Apply(null, GodPowerType.Bless, new Vec2i(3, 3), 3, 0).Success);
        }
    }
}
