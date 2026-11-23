using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.History;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.History
{
    public class WorldStatisticsTests
    {
        private GameDatabase _db;
        private SimContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _db = new GameDatabase();
            _db.Register(new BuildingDefinition { Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, IsWonder = true, Width = 3, Height = 3 });
            _db.Register(new BuildingDefinition { Type = BuildingType.Colossus, Name = "Colossus", Category = BuildingCategory.Wonder, IsWonder = true, Width = 2, Height = 2 });
            _db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food });
            TestDb.FillMissing(_db);
            _db.Freeze();
            _ctx = TestWorlds.CreateContext(_db);
        }

        private Building House(Settlement s, Vec2i at, int capacity)
        {
            var b = _ctx.Ops.CreateBuilding(s, BuildingType.House, at, 0, true);
            Assert.IsNotNull(b);
            b.HousingCapacity = capacity;
            return b;
        }

        [Test]
        public void HousingSummaryOnHandBuiltSettlement()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(20, 20), 10, out _);
            var h1 = House(s, new Vec2i(10, 10), 4);
            var h2 = House(s, new Vec2i(12, 10), 3);
            _ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(14, 10)); // under construction
            for (int i = 0; i < 4; i++) Assert.IsTrue(_ctx.Ops.AssignHome(_ctx.State.People.Get(s.ResidentIds[i]), h1));
            for (int i = 4; i < 6; i++) Assert.IsTrue(_ctx.Ops.AssignHome(_ctx.State.People.Get(s.ResidentIds[i]), h2));

            var hs = WorldStatistics.HousingSummary(_ctx.State, _db, s);
            Assert.AreEqual(2, hs.Houses);
            Assert.AreEqual(1, hs.HousesUnderConstruction);
            Assert.AreEqual(7, hs.Capacity);
            Assert.AreEqual(6, hs.Residents);
            Assert.AreEqual(4, hs.Homeless);
            Assert.AreEqual(3f, hs.AvgPerHouse, 1e-5f);
            Assert.AreEqual(1, hs.Vacancies);

            var empty = WorldStatistics.HousingSummary(_ctx.State, _db, null);
            Assert.AreEqual(0, empty.Houses);
            Assert.AreEqual(0f, empty.AvgPerHouse);
        }

        [Test]
        public void BuildingsByCategoryForSettlementAndCiv()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(20, 20), 2, out var civ);
            House(s, new Vec2i(10, 10), 4);
            House(s, new Vec2i(12, 10), 4);
            _ctx.Ops.CreateBuilding(s, BuildingType.Farm, new Vec2i(14, 10), 0, true);
            _ctx.Ops.CreateBuilding(s, BuildingType.Farm, new Vec2i(16, 10)); // unfinished
            var s2 = _ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            House(s2, new Vec2i(40, 40), 5);

            var counts = new int[WorldStatistics.CategoryCount];
            Assert.AreEqual(4, WorldStatistics.BuildingsByCategory(_ctx.State, _db, s, counts));
            Assert.AreEqual(2, counts[(int)BuildingCategory.Housing]);
            Assert.AreEqual(1, counts[(int)BuildingCategory.Food]);
            Assert.AreEqual(1, counts[(int)BuildingCategory.Civic]);
            Assert.AreEqual(5, WorldStatistics.BuildingsByCategory(_ctx.State, _db, s, counts, true));
            Assert.AreEqual(2, counts[(int)BuildingCategory.Food]);

            Assert.AreEqual(6, WorldStatistics.BuildingsByCategory(_ctx.State, _db, civ, counts));
            Assert.AreEqual(3, counts[(int)BuildingCategory.Housing]);
            Assert.AreEqual(2, counts[(int)BuildingCategory.Civic]);
        }

        [Test]
        public void PopulationByRaceAndAge()
        {
            TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(16, 16), 5, out _);
            TestWorlds.AddSettlement(_ctx, RaceId.Orc, new Vec2i(48, 48), 3, out var orcs);
            orcs.Age = AgeId.Iron;

            var byRace = WorldStatistics.PopulationByRace(_ctx.State);
            Assert.AreEqual(Counts.Races, byRace.Length);
            Assert.AreEqual(5, byRace[(int)RaceId.Human]);
            Assert.AreEqual(3, byRace[(int)RaceId.Orc]);
            Assert.AreEqual(0, byRace[(int)RaceId.Elf]);

            var byAge = new int[Counts.Ages];
            Assert.AreEqual(8, WorldStatistics.PopulationByAge(_ctx.State, byAge));
            Assert.AreEqual(5, byAge[(int)AgeId.Dawn]);
            Assert.AreEqual(3, byAge[(int)AgeId.Iron]);
        }

        [Test]
        public void RankingsAreOrderedAndDeterministic()
        {
            var big = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(16, 16), 9, out var humans);
            TestWorlds.AddSettlement(_ctx, RaceId.Elf, new Vec2i(48, 48), 4, out var elves);
            var small = TestWorlds.AddSettlement(_ctx, RaceId.Dwarf, new Vec2i(16, 48), 2, out var dwarves);
            elves.Age = AgeId.Classical;   // +600
            elves.TotalResearch = 5000f;   // +50
            _ctx.Ops.FoundSettlement(humans, new Vec2i(48, 16));

            var top = new List<Settlement>();
            Assert.AreEqual(2, WorldStatistics.TopSettlements(_ctx.State, 2, top));
            Assert.AreSame(big, top[0]);
            Assert.AreEqual(4, top[1].Population);
            WorldStatistics.TopSettlements(_ctx.State, 10, top);
            Assert.AreEqual(4, top.Count);
            Assert.AreEqual(0, top[3].Population, "empty new settlement last");
            Assert.AreSame(small, top[2]);

            var ranking = new List<CivScore>();
            Assert.AreEqual(3, WorldStatistics.CivRanking(_ctx.State, ranking));
            Assert.AreEqual(elves.Id, ranking[0].CivId);
            Assert.AreEqual(4f + 50f + 600f + 50f, ranking[0].Score, 1e-3f);
            Assert.AreEqual(humans.Id, ranking[1].CivId);
            Assert.AreEqual(9f + 100f, ranking[1].Score, 1e-3f);
            Assert.AreEqual(2, ranking[1].Settlements);
            Assert.AreEqual(dwarves.Id, ranking[2].CivId);

            _ctx.Ops.CollapseCivilization(dwarves, -1);
            Assert.AreEqual(2, WorldStatistics.CivRanking(_ctx.State, ranking));
            Assert.AreEqual(3, WorldStatistics.CivRanking(_ctx.State, ranking, true));
        }

        [Test]
        public void WonderOwnersRankedByCountThenArea()
        {
            var a = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(16, 16), 2, out var humans);
            var b = TestWorlds.AddSettlement(_ctx, RaceId.Elf, new Vec2i(48, 48), 2, out var elves);
            Assert.IsNotNull(_ctx.Ops.CreateBuilding(a, BuildingType.Colossus, new Vec2i(8, 8), 0, true));
            Assert.IsNotNull(_ctx.Ops.CreateBuilding(b, BuildingType.GreatLibrary, new Vec2i(40, 40), 0, true));
            Assert.IsNotNull(_ctx.Ops.CreateBuilding(b, BuildingType.Colossus, new Vec2i(36, 36))); // unfinished: ignored

            var owners = new List<WonderOwnership>();
            Assert.AreEqual(2, WorldStatistics.LargestWonderOwners(_ctx.State, _db, owners));
            Assert.AreEqual(elves.Id, owners[0].CivId, "same count, larger footprint first");
            Assert.AreEqual(9, owners[0].TotalArea);
            Assert.AreEqual(BuildingType.GreatLibrary, owners[0].LargestWonder);
            Assert.AreEqual(humans.Id, owners[1].CivId);

            Assert.IsNotNull(_ctx.Ops.CreateBuilding(a, BuildingType.GreatLibrary, new Vec2i(20, 4), 0, true));
            WorldStatistics.LargestWonderOwners(_ctx.State, _db, owners);
            Assert.AreEqual(humans.Id, owners[0].CivId);
            Assert.AreEqual(2, owners[0].WonderCount);
            Assert.AreEqual(13, owners[0].TotalArea);
        }

        [Test]
        public void LifespanAndLifeExpectancy()
        {
            var s = TestWorlds.AddSettlement(_ctx, RaceId.Human, new Vec2i(16, 16), 6, out var civ);
            var rec = new HistoryRecorder();
            TestWorlds.Run(rec, _ctx, 0);

            float baseline = WorldStatistics.LifeExpectancyEstimate(_ctx.State, _db, RaceId.Human, rec.Lifespans);
            Assert.AreEqual(_db.Race(RaceId.Human).LifeExpectancy, baseline, 1e-4f);
            civ.Modifiers.LifespanBonusYears = 10f;
            Assert.AreEqual(_db.Race(RaceId.Human).LifeExpectancy + 10f, WorldStatistics.LifeExpectancyEstimate(_ctx.State, _db, RaceId.Human), 1e-4f);
            Assert.AreEqual(_db.Race(RaceId.Human).LifeExpectancy + 10f, WorldStatistics.LifeExpectancyEstimate(_ctx.State, _db), 1e-4f);

            // Ages are 20..25 for the six adults: kill them all.
            float sum = 0f;
            while (s.ResidentIds.Count > 0)
            {
                var p = _ctx.State.People.Get(s.ResidentIds[0]);
                sum += p.AgeYears;
                _ctx.Ops.KillPerson(p, DeathCause.Violence);
            }
            _ctx.Ops.FlushRemovals();
            Assert.AreEqual(sum / 6f, WorldStatistics.AverageLifespanAtDeath(rec.Lifespans), 1e-4f);
            Assert.AreEqual(sum / 6f, WorldStatistics.AverageLifespanAtDeath(rec.Lifespans, RaceId.Human), 1e-4f);
            Assert.AreEqual(0f, WorldStatistics.AverageLifespanAtDeath(rec.Lifespans, RaceId.Elf));
            Assert.AreEqual(6, rec.Lifespans.CountFor(DeathCause.Violence));
            float blended = WorldStatistics.LifeExpectancyEstimate(_ctx.State, _db, RaceId.Human, rec.Lifespans);
            Assert.Less(blended, _db.Race(RaceId.Human).LifeExpectancy + 10f);
            Assert.Greater(blended, sum / 6f);
            Assert.AreEqual(0f, WorldStatistics.AverageLifespanAtDeath(null));
        }

        [Test]
        public void EmptyWorldQueriesReturnZeros()
        {
            var st = _ctx.State;
            Assert.AreEqual(0, WorldStatistics.PopulationByRace(st, new int[Counts.Races]));
            Assert.AreEqual(0, WorldStatistics.PopulationByAge(st, new int[Counts.Ages]));
            Assert.AreEqual(0, WorldStatistics.TopSettlements(st, 5, new List<Settlement>()));
            Assert.AreEqual(0, WorldStatistics.CivRanking(st, new List<CivScore>()));
            Assert.AreEqual(0, WorldStatistics.LargestWonderOwners(st, _db, new List<WonderOwnership>()));
            Assert.AreEqual(0f, WorldStatistics.LifeExpectancyEstimate(st, _db));
            Assert.AreEqual(0, WorldStatistics.PopulationByRace(null, new int[Counts.Races]));
        }
    }
}
