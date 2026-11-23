using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class WorldOpsTests
    {
        private SimContext _ctx;
        private WorldOps _ops;
        private WorldState _s;

        [SetUp]
        public void SetUp()
        {
            _ctx = TestWorlds.CreateContext(WorldInvariants.CreateDb(), 64, 5);
            _ops = _ctx.Ops;
            _s = _ctx.State;
        }

        private void AssertCore()
        {
            var v = WorldInvariants.CheckCore(_s, _ctx.Db);
            Assert.AreEqual(0, v.Count, string.Join("\n", v));
        }

        private Building House(Settlement st, Vec2i at)
        {
            var b = _ops.CreateBuilding(st, BuildingType.House, at, 0, true);
            Assert.IsNotNull(b, "house placement failed at " + at);
            return b;
        }

        // ------------------------------------------------------------------ buildings
        [Test]
        public void CreateBuilding_OnBlockedFootprint_ReturnsNullWithoutSideEffects()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.CreateSettlement(civ, new Vec2i(20, 20));
            var first = _ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(10, 10));
            Assert.IsNotNull(first);
            int nextId = _s.Ids.NextBuildingId;
            int published = 0;
            _ctx.Events.Subscribe<BuildingPlacedEvent>(e => published++);

            Assert.IsNull(_ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(11, 11)), "overlaps existing building");
            Assert.IsNull(_ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(63, 63)), "partially out of bounds");
            Assert.IsNull(_ops.CreateBuilding(st, BuildingType.House, new Vec2i(-1, 5)), "out of bounds");
            _s.Map.Biomes[_s.Map.Index(30, 30)] = Biome.Lake;
            Assert.IsNull(_ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(29, 29)), "water tile");
            _s.Map.Set(_s.Map.Index(40, 40), TileFlags.Road);
            Assert.IsNull(_ops.CreateBuilding(st, BuildingType.House, new Vec2i(40, 40)), "road tile");
            _s.Map.Set(_s.Map.Index(41, 41), TileFlags.Burning);
            Assert.IsNull(_ops.CreateBuilding(st, BuildingType.House, new Vec2i(41, 41)), "burning tile");
            _s.Map.Biomes[_s.Map.Index(42, 42)] = Biome.Mountain;
            Assert.IsNull(_ops.CreateBuilding(st, BuildingType.House, new Vec2i(42, 42)), "mountain without allowRugged");
            Assert.IsNotNull(_ops.CreateBuilding(st, BuildingType.House, new Vec2i(42, 42), 0, false, true), "mountain with allowRugged");

            Assert.AreEqual(nextId + 1, _s.Ids.NextBuildingId, "blocked placements must not allocate ids");
            Assert.AreEqual(1, published);
            Assert.AreEqual(2, st.BuildingIds.Count);
            AssertCore();
        }

        [Test]
        [Ignore("BUG (low): WorldOps.CanPlace overflows for origins near int.MaxValue: `x < origin.X + width` wraps negative, the loop never runs and CanPlace returns true (WorldOps.cs:369-378); CreateBuilding then creates an off-map phantom building. Fix: reject !map.InBounds(origin) || !map.InBounds(origin.X + width - 1, origin.Y + height - 1) up front (with width/height >= 1).")]
        public void CanPlace_HugeOrigin_IsRejected()
        {
            Assert.IsFalse(_ops.CanPlace(new Vec2i(int.MaxValue, 0), 2, 2));
            Assert.IsFalse(_ops.CanPlace(new Vec2i(5, 5), 0, 0));
        }

        [Test]
        public void CreateBuilding_SetsFootprintOwnerHousingAndStyle()
        {
            var civ = _ops.CreateCivilization(RaceId.Dwarf);
            civ.Age = AgeId.Iron;
            var st = _ops.CreateSettlement(civ, new Vec2i(50, 50), "Far", -1, 1);
            var house = _ops.CreateBuilding(st, BuildingType.House, new Vec2i(5, 5), 7);
            Assert.AreEqual(3, house.Rotation, "rotation masked to 0..3");
            Assert.IsFalse(house.IsComplete);
            Assert.IsTrue(house.HousingCapacity >= 3 && house.HousingCapacity <= 5);
            Assert.AreEqual(RaceId.Dwarf, house.StyleRace);
            Assert.AreEqual(AgeId.Iron, house.StyleAge);
            Assert.AreEqual(house.Id, _s.Map.BuildingAt[_s.Map.Index(5, 5)]);
            Assert.AreEqual(st.Id, _s.Map.Owner[_s.Map.Index(5, 5)], "unowned footprint is claimed");
            var farm = _ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(8, 8));
            Assert.AreEqual(0, farm.HousingCapacity);
            for (int y = 8; y < 10; y++) for (int x = 8; x < 10; x++) Assert.AreEqual(farm.Id, _s.Map.BuildingAt[_s.Map.Index(x, y)]);
        }

        [Test]
        public void HouseCapacity_IsAlwaysThreeToFive_AndCoversRange()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.CreateSettlement(civ, new Vec2i(32, 32));
            var seen = new HashSet<int>();
            for (int y = 0; y < 60; y += 2) for (int x = 0; x < 60; x += 2)
                {
                    var b = _ops.CreateBuilding(st, BuildingType.House, new Vec2i(x, y));
                    Assert.IsTrue(b.HousingCapacity >= 3 && b.HousingCapacity <= 5);
                    seen.Add(b.HousingCapacity);
                }
            Assert.AreEqual(3, seen.Count);
        }

        [Test]
        public void CompleteBuilding_IsIdempotent_AndSkipsDestroyed()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.CreateSettlement(civ, new Vec2i(20, 20));
            var b = _ops.CreateBuilding(st, BuildingType.Well, new Vec2i(3, 3));
            int completed = 0;
            _ctx.Events.Subscribe<BuildingCompletedEvent>(e => completed++);
            _ops.CompleteBuilding(b);
            _ops.CompleteBuilding(b);
            Assert.AreEqual(1, completed);
            Assert.IsTrue(b.IsComplete && b.CostPaid);
            Assert.AreEqual(1f, b.ConstructionProgress);
            var c = _ops.CreateBuilding(st, BuildingType.Well, new Vec2i(5, 5));
            _ops.DestroyBuilding(c, DestroyCause.Fire);
            _ops.CompleteBuilding(c);
            Assert.IsFalse(c.IsComplete);
        }

        [Test]
        public void CompleteWonder_WritesChronicle()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.CreateSettlement(civ, new Vec2i(20, 20));
            int before = _s.Chronicle.Entries.Count;
            _ops.CreateBuilding(st, BuildingType.Colossus, new Vec2i(3, 3), 0, true);
            Assert.AreEqual(before + 1, _s.Chronicle.Entries.Count);
            Assert.AreEqual(3, _s.Chronicle.Entries[_s.Chronicle.Entries.Count - 1].Importance);
        }

        [Test]
        public void DestroyBuilding_ClearsLinksMapAndMarksRuins()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var house = House(st, new Vec2i(5, 5));
            var farm = _ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(8, 8), 0, true);
            var p = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f);
            Assert.IsTrue(_ops.AssignHome(p, house));
            Assert.IsTrue(_ops.AssignJob(p, farm, JobType.Farmer));
            _ops.DestroyBuilding(house, DestroyCause.Fire);
            _ops.DestroyBuilding(farm, DestroyCause.Demolished);
            Assert.AreEqual(-1, p.HomeId);
            Assert.AreEqual(-1, p.WorkplaceId);
            Assert.AreEqual(JobType.None, p.Job);
            Assert.AreEqual(-1, _s.Map.BuildingAt[_s.Map.Index(5, 5)]);
            Assert.IsTrue(_s.Map.Has(new Vec2i(5, 5), TileFlags.Ruins), "burned complete building leaves ruins");
            Assert.IsFalse(_s.Map.Has(new Vec2i(8, 8), TileFlags.Ruins), "demolition leaves no ruins");
            Assert.IsFalse(st.BuildingIds.Contains(house.Id));
            _ops.DestroyBuilding(_s.Buildings.Get(st.TownCenterId), DestroyCause.War);
            Assert.AreEqual(-1, st.TownCenterId);
            long destroyed = _s.Stats.BuildingsDestroyed;
            _ops.DestroyBuilding(house, DestroyCause.Fire);
            Assert.AreEqual(destroyed, _s.Stats.BuildingsDestroyed, "idempotent");
            Assert.IsTrue(_s.Buildings.Contains(house.Id), "removal is deferred");
            _ops.FlushRemovals();
            Assert.IsFalse(_s.Buildings.Contains(house.Id));
            AssertCore();
        }

        [Test]
        public void FindFreeSite_ReturnsNearestOrInvalid()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.CreateSettlement(civ, new Vec2i(20, 20));
            Assert.AreEqual(new Vec2i(10, 10), _ops.FindFreeSite(new Vec2i(10, 10), 2, 2, 4));
            _ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(10, 10));
            var site = _ops.FindFreeSite(new Vec2i(10, 10), 2, 2, 4);
            Assert.IsTrue(site.IsValid);
            Assert.IsTrue(_ops.CanPlace(site, 2, 2));
            Assert.AreEqual(2, site.ChebyshevTo(new Vec2i(10, 10)), "nearest free ring");
            for (int i = 0; i < _s.Map.TileCount; i++) _s.Map.Biomes[i] = Biome.DeepOcean;
            Assert.IsFalse(_ops.FindFreeSite(new Vec2i(10, 10), 1, 1, 6).IsValid);
        }

        // ------------------------------------------------------------------ settlements
        [Test]
        public void FoundSettlement_PlacesCompletedTownCenterAndStock()
        {
            var civ = _ops.CreateCivilization(RaceId.Elf);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            Assert.GreaterOrEqual(st.TownCenterId, 0);
            var tc = _s.Buildings.Get(st.TownCenterId);
            Assert.AreEqual(BuildingType.TownCenter, tc.Type);
            Assert.IsTrue(tc.IsComplete);
            Assert.AreEqual(tc.Center, st.Center);
            Assert.IsTrue(st.BuildingIds.Contains(tc.Id));
            Assert.IsTrue(st.IsCapital);
            Assert.AreEqual(st.Id, civ.CapitalId);
            CollectionAssert.AreEqual(new[] { st.Id }, civ.SettlementIds);
            Assert.AreEqual(60f, st.Stockpile.Get(ResourceType.Food));
            Assert.AreEqual(40f, st.Stockpile.Get(ResourceType.Wood));
            Assert.AreEqual(10f, st.Stockpile.Get(ResourceType.Stone));
            Assert.AreEqual(st.Id, _s.Map.Owner[_s.Map.Index(st.Center)]);
            Assert.AreEqual(RaceId.Elf, st.Race);
            Assert.IsFalse(string.IsNullOrEmpty(st.Name));
            var second = _ops.FoundSettlement(civ, new Vec2i(45, 45));
            Assert.IsFalse(second.IsCapital);
            Assert.AreEqual(st.Id, civ.CapitalId);
            AssertCore();
        }

        [Test]
        public void FoundSettlement_BlockedCenter_PlacesTownCenterNearby()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            for (int y = 18; y <= 22; y++) for (int x = 18; x <= 22; x++) _s.Map.Set(_s.Map.Index(x, y), TileFlags.Road);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var tc = _s.Buildings.Get(st.TownCenterId);
            Assert.IsNotNull(tc);
            Assert.LessOrEqual(tc.Origin.ChebyshevTo(new Vec2i(20, 20)), 8);
            Assert.AreEqual(tc.Center, st.Center);
        }

        [Test]
        public void FoundSettlement_NoRoomForTownCenter_StillCreatesSettlement()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            for (int i = 0; i < _s.Map.TileCount; i++) _s.Map.Set(i, TileFlags.Road);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            Assert.IsTrue(st.IsAlive);
            Assert.AreEqual(-1, st.TownCenterId);
            Assert.AreEqual(new Vec2i(20, 20), st.Center);
        }

        [Test]
        public void CreateSettlement_OutOfBounds_Throws()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ops.CreateSettlement(civ, new Vec2i(-1, 3)));
            Assert.Throws<ArgumentNullException>(() => _ops.CreateSettlement(null, new Vec2i(1, 3)));
            Assert.AreEqual(0, civ.SettlementIds.Count);
        }

        [Test]
        public void ClaimTerritory_NeverStealsAndSkipsWater()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var b = _ops.CreateCivilization(RaceId.Orc);
            _s.Map.Biomes[_s.Map.Index(22, 20)] = Biome.Lake;
            var sa = _ops.CreateSettlement(a, new Vec2i(20, 20), "A", -1, 4);
            var owned = new int[_s.Map.TileCount];
            Array.Copy(_s.Map.Owner, owned, owned.Length);
            var sb = _ops.CreateSettlement(b, new Vec2i(25, 20), "B", -1, 6);
            for (int i = 0; i < owned.Length; i++)
                if (owned[i] >= 0) Assert.AreEqual(owned[i], _s.Map.Owner[i], "tile " + _s.Map.PosOf(i) + " was stolen");
            Assert.AreEqual(-1, _s.Map.Owner[_s.Map.Index(22, 20)], "water is never claimed");
            Assert.AreEqual(sb.Id, _s.Map.Owner[_s.Map.Index(30, 20)]);
            Assert.AreEqual(0, _ops.ClaimTerritory(sa, 4), "re-claiming owned land claims nothing");
            int claimed = _ops.ClaimTerritory(sa, 1000);
            Assert.Greater(claimed, 0);
            Assert.AreEqual(64, sa.TerritoryRadius, "radius clamped to 64");
            for (int i = 0; i < owned.Length; i++) if (_s.Map.Owner[i] == sb.Id) Assert.AreNotEqual(sa.Id, _s.Map.Owner[i]);
            Assert.AreEqual(sb.Id, _s.Map.Owner[_s.Map.Index(30, 20)], "B keeps its land after A's big claim");
        }

        [Test]
        public void ClaimTerritory_TerritoryChangedOnlyWhenSomethingClaimed()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var sa = _ops.CreateSettlement(a, new Vec2i(20, 20));
            int events = 0;
            _ctx.Events.Subscribe<TerritoryChangedEvent>(e => events++);
            _ops.ClaimTerritory(sa, 3);
            Assert.AreEqual(0, events);
            _ops.ClaimTerritory(sa, 9);
            Assert.AreEqual(1, events);
        }

        [Test]
        public void DestroySettlement_MovesRefugeesToNearestSameCivSettlement()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var enemy = _ops.CreateCivilization(RaceId.Orc);
            var doomed = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var near = _ops.FoundSettlement(civ, new Vec2i(32, 20));
            var far = _ops.FoundSettlement(civ, new Vec2i(55, 55));
            var foreignCloser = _ops.FoundSettlement(enemy, new Vec2i(24, 26));
            var house = House(doomed, new Vec2i(15, 15));
            var people = new List<Person>();
            for (int i = 0; i < 4; i++) people.Add(_ops.CreatePerson(doomed, RaceId.Human, (Sex)(i % 2), 25f));
            _ops.AssignHome(people[0], house);
            var army = _ops.CreateArmy(civ, doomed, new[] { people[1] });
            _ops.DestroySettlement(doomed, DestroyCause.Disaster);
            Assert.IsFalse(doomed.IsAlive);
            foreach (var p in people)
            {
                Assert.IsTrue(p.IsAlive);
                Assert.AreEqual(near.Id, p.SettlementId);
                Assert.AreEqual(civ.Id, p.CivilizationId);
                Assert.IsTrue(near.ResidentIds.Contains(p.Id));
                Assert.AreEqual(near.Center, p.Position);
            }
            Assert.AreEqual(-1, people[0].HomeId);
            Assert.AreEqual(0, doomed.ResidentIds.Count);
            Assert.AreEqual(0, foreignCloser.ResidentIds.Count);
            Assert.AreEqual(0, far.ResidentIds.Count);
            Assert.IsTrue(house.IsDestroyed);
            Assert.IsFalse(civ.SettlementIds.Contains(doomed.Id));
            Assert.AreEqual(-1, Array.IndexOf(_s.Map.Owner, doomed.Id), "territory released");
            Assert.IsTrue(civ.IsAlive);
            Assert.AreNotEqual(doomed.Id, civ.CapitalId, "capital reassigned");
            Assert.IsTrue(_s.Settlements.Get(civ.CapitalId).IsCapital);
            Assert.IsTrue(army.IsAlive);
            _ops.FlushRemovals();
            Assert.IsFalse(_s.Settlements.Contains(doomed.Id));
            AssertCore();
        }

        [Test]
        public void DestroySettlement_KillResidentsOrNoRefuge_KillsAndCollapsesCiv()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var p1 = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f);
            var p2 = _ops.CreatePerson(st, RaceId.Human, Sex.Female, 30f);
            p1.SpouseId = p2.Id; p2.SpouseId = p1.Id;
            civ.LeaderId = p1.Id;
            var army = _ops.CreateArmy(civ, st, new[] { p1 });
            int collapsed = 0, died = 0;
            _ctx.Events.Subscribe<CivilizationCollapsedEvent>(e => collapsed++);
            _ctx.Events.Subscribe<PersonDiedEvent>(e => { died++; Assert.AreEqual(DeathCause.Violence, e.Cause); });
            _ops.DestroySettlement(st, DestroyCause.War);
            Assert.IsFalse(p1.IsAlive);
            Assert.IsFalse(p2.IsAlive);
            Assert.AreEqual(2, died);
            Assert.AreEqual(-1, civ.LeaderId);
            Assert.IsFalse(civ.IsAlive);
            Assert.AreEqual(1, collapsed);
            Assert.IsFalse(army.IsAlive, "collapse disbands armies");
            _ops.DestroySettlement(st, DestroyCause.War);
            Assert.AreEqual(1, collapsed, "idempotent");
            _ops.FlushRemovals();
            Assert.AreEqual(0, _s.People.Count);
            Assert.AreEqual(0, _s.Buildings.Count);
            Assert.AreEqual(0, _s.Settlements.Count);
            Assert.AreEqual(0, _s.Diplomacy.Armies.Count);
            AssertCore();
        }

        [Test]
        public void DestroySettlement_KillResidentsTrue_IgnoresAvailableRefuge()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var a = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var b = _ops.FoundSettlement(civ, new Vec2i(40, 40));
            var p = _ops.CreatePerson(a, RaceId.Human, Sex.Male, 30f);
            _ops.DestroySettlement(a, DestroyCause.Disaster, true);
            Assert.IsFalse(p.IsAlive);
            Assert.AreEqual(DeathCause.Exposure, p.DeathCause);
            Assert.AreEqual(0, b.ResidentIds.Count);
            Assert.IsTrue(civ.IsAlive);
        }

        [Test]
        public void TransferSettlement_UpdatesPeopleBuildingsAndCollapsesLastSettlementOwner()
        {
            var loser = _ops.CreateCivilization(RaceId.Human);
            var winner = _ops.CreateCivilization(RaceId.Orc);
            var winnerHome = _ops.FoundSettlement(winner, new Vec2i(50, 50));
            var st = _ops.FoundSettlement(loser, new Vec2i(15, 15));
            var house = House(st, new Vec2i(5, 5));
            var p = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f);
            var q = _ops.CreatePerson(st, RaceId.Human, Sex.Female, 30f);
            _ops.AssignHome(p, house);
            var army = _ops.CreateArmy(loser, st, new[] { q });
            st.BuildQueue.Add(new BuildOrder { Type = BuildingType.Well });
            int captured = 0, collapsedBy = -99;
            _ctx.Events.Subscribe<SettlementCapturedEvent>(e => { captured++; Assert.AreEqual(loser.Id, e.OldCivilizationId); Assert.AreEqual(winner.Id, e.NewCivilizationId); });
            _ctx.Events.Subscribe<CivilizationCollapsedEvent>(e => collapsedBy = e.ConquerorId);

            _ops.TransferSettlement(st, winner);

            Assert.AreEqual(1, captured);
            Assert.AreEqual(winner.Id, st.CivilizationId);
            Assert.IsFalse(st.IsCapital);
            Assert.AreEqual(0, st.BuildQueue.Count);
            Assert.IsTrue(winner.SettlementIds.Contains(st.Id));
            Assert.IsFalse(loser.SettlementIds.Contains(st.Id));
            foreach (int bid in st.BuildingIds) Assert.AreEqual(winner.Id, _s.Buildings.Get(bid).CivilizationId);
            Assert.AreEqual(winner.Id, p.CivilizationId);
            Assert.AreEqual(winner.Id, q.CivilizationId);
            Assert.AreEqual(house.Id, p.HomeId, "residents keep their homes");
            Assert.AreEqual(-1, q.ArmyId, "conquered soldiers leave the old army");
            Assert.IsFalse(loser.IsAlive);
            Assert.AreEqual(winner.Id, collapsedBy);
            Assert.IsFalse(army.IsAlive);
            Assert.AreEqual(winnerHome.Id, winner.CapitalId);
            _ops.FlushRemovals();
            AssertCore();
        }

        [Test]
        public void TransferSettlement_NoOpCases()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(a, new Vec2i(15, 15));
            _ops.TransferSettlement(st, a);
            _ops.TransferSettlement(null, a);
            _ops.TransferSettlement(st, null);
            Assert.AreEqual(a.Id, st.CivilizationId);
            Assert.IsTrue(a.IsAlive);
        }

        [Test]
        public void TransferSettlement_OfCapital_ReassignsOldOwnersCapital()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var b = _ops.CreateCivilization(RaceId.Orc);
            _ops.FoundSettlement(b, new Vec2i(55, 55));
            var cap = _ops.FoundSettlement(a, new Vec2i(10, 10));
            var other = _ops.FoundSettlement(a, new Vec2i(30, 30));
            _ops.TransferSettlement(cap, b);
            Assert.AreEqual(other.Id, a.CapitalId);
            Assert.IsTrue(other.IsCapital);
            Assert.IsTrue(a.IsAlive);
        }

        [Test]
        [Ignore("BUG: TransferSettlement does not clear the old civ's LeaderId when the leader lives in the conquered settlement (WorldOps.cs:184-190), so loser.LeaderId points at a person whose CivilizationId is now the conqueror's. MovePersonToSettlement has the same hole for cross-civ migration (WorldOps.cs:350-362). Fix: in both, `var oc = _s.Civilizations.Get(oldCivId); if (oc != null && oc.LeaderId == p.Id) oc.LeaderId = -1;`.")]
        public void TransferSettlement_LeaderInConqueredSettlement_IsCleared()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var b = _ops.CreateCivilization(RaceId.Orc);
            _ops.FoundSettlement(b, new Vec2i(55, 55));
            _ops.FoundSettlement(a, new Vec2i(10, 10));
            var lost = _ops.FoundSettlement(a, new Vec2i(30, 30));
            var leader = _ops.CreatePerson(lost, RaceId.Human, Sex.Male, 40f);
            a.LeaderId = leader.Id;
            _ops.TransferSettlement(lost, b);
            Assert.AreEqual(b.Id, leader.CivilizationId);
            Assert.AreEqual(-1, a.LeaderId, "old civ is led by a foreigner");
        }

        [Test]
        [Ignore("BUG: MovePersonToSettlement across civs keeps the person's ArmyId (WorldOps.cs:350-362), so civ A's army now contains a soldier of civ B; it also keeps the old civ's LeaderId. Fix: when to.CivilizationId != p.CivilizationId, remove the person from their army and clear the old civ's leader.")]
        public void MovePersonToSettlement_CrossCiv_LeavesArmyAndLeadership()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var b = _ops.CreateCivilization(RaceId.Orc);
            var sa = _ops.FoundSettlement(a, new Vec2i(10, 10));
            var sb = _ops.FoundSettlement(b, new Vec2i(40, 40));
            var p = _ops.CreatePerson(sa, RaceId.Human, Sex.Male, 30f);
            a.LeaderId = p.Id;
            var army = _ops.CreateArmy(a, sa, new[] { p });
            _ops.MovePersonToSettlement(p, sb);
            Assert.AreEqual(b.Id, p.CivilizationId);
            Assert.AreEqual(-1, p.ArmyId);
            Assert.IsFalse(army.SoldierIds.Contains(p.Id));
            Assert.AreEqual(-1, a.LeaderId);
        }

        [Test]
        [Ignore("BUG: TransferSettlement to a civ without a capital (e.g. a newly spawned/rebel civ with no settlements) leaves newOwner.CapitalId == -1 and no IsCapital settlement (WorldOps.cs:179-182). Also accepts a collapsed (IsAlive == false) newOwner. Fix: `if (!newOwner.IsAlive) return;` and `if (newOwner.CapitalId < 0 || _s.Settlements.Get(newOwner.CapitalId) == null) { newOwner.CapitalId = s.Id; s.IsCapital = true; }`.")]
        public void TransferSettlement_ToCivWithoutCapital_AssignsCapital()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var rebels = _ops.CreateCivilization(RaceId.Human);
            _ops.FoundSettlement(a, new Vec2i(10, 10));
            var st = _ops.FoundSettlement(a, new Vec2i(40, 40));
            _ops.TransferSettlement(st, rebels);
            Assert.AreEqual(st.Id, rebels.CapitalId);
            Assert.IsTrue(st.IsCapital);
        }

        [Test]
        [Ignore("BUG: DestroySettlement/ReleaseTerritory leaves map.Owner pointing at the removed settlement. ReleaseTerritory only scans TerritoryRadius+4 around the CURRENT center (WorldOps.cs:135-143), but FoundSettlement moves s.Center to the town center (up to ~9 tiles away, WorldOps.cs:104) after claiming around the original center, and CreateBuilding claims footprint tiles anywhere (WorldOps.cs:425). Fix: scan the whole map in ReleaseTerritory (it is rare), or track a claimed bounding box on the settlement.")]
        public void DestroySettlement_ReleasesAllOwnedTiles_WhenCenterMoved()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            for (int y = 13; y <= 27; y++) for (int x = 13; x <= 27; x++) _s.Map.Set(_s.Map.Index(x, y), TileFlags.Road);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            Assert.Greater(st.Center.ChebyshevTo(new Vec2i(20, 20)), 5, "precondition: town center displaced");
            _ops.DestroySettlement(st, DestroyCause.Abandoned);
            _ops.FlushRemovals();
            Assert.AreEqual(-1, Array.IndexOf(_s.Map.Owner, st.Id), "tile still owned by removed settlement");
        }

        [Test]
        [Ignore("BUG: ReleaseTerritory misses footprint tiles of buildings placed beyond TerritoryRadius+4 (CreateBuilding claims them unconditionally, WorldOps.cs:425), leaving map.Owner dangling after DestroySettlement. Same fix as above.")]
        public void DestroySettlement_ReleasesFarBuildingTiles()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.CreateSettlement(civ, new Vec2i(10, 10), "S", -1, 3);
            _ops.CreateBuilding(st, BuildingType.House, new Vec2i(40, 40));
            _ops.DestroySettlement(st, DestroyCause.Abandoned);
            _ops.FlushRemovals();
            Assert.AreEqual(-1, _s.Map.Owner[_s.Map.Index(40, 40)]);
        }

        [Test]
        [Ignore("BUG: CollapseCivilization on a civ that still owns settlements leaves them alive under a dead civ (people, buildings and SettlementIds untouched, WorldOps.cs:46-66). Fix: either guard (only collapse when SettlementIds.Count == 0) or abandon/destroy the remaining settlements first.")]
        public void CollapseCivilization_WithSettlements_LeavesNoOrphans()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            _ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f);
            _ops.CollapseCivilization(civ, -1);
            _ops.FlushRemovals();
            AssertCore();
        }

        [Test]
        [Ignore("BUG (medium): DestroySettlement (WorldOps.cs:146-171) and TransferSettlement (WorldOps.cs:174-199) leave armies whose HomeSettlementId is the destroyed/conquered settlement, so after FlushRemovals army.HomeSettlementId references a removed settlement (or one owned by the enemy). Fix: re-home such armies to NearestSettlementOf(army civ) or disband them when none exists.")]
        public void DestroySettlement_RehomesOrDisbandsArmiesBasedThere()
        {
            var civ = _ops.CreateCivilization(RaceId.Orc);
            var keep = _ops.FoundSettlement(civ, new Vec2i(50, 50));
            var st = _ops.FoundSettlement(civ, new Vec2i(15, 15));
            var army = _ops.CreateArmy(civ, st, null);
            _ops.DestroySettlement(st, DestroyCause.War);
            _ops.FlushRemovals();
            Assert.IsTrue(!army.IsAlive || army.HomeSettlementId == keep.Id, "army home " + army.HomeSettlementId + " was removed");
        }

        [Test]
        public void CollapseCivilization_EndsWarsAndDisbandsArmies()
        {
            var a = _ops.CreateCivilization(RaceId.Human);
            var b = _ops.CreateCivilization(RaceId.Orc);
            var sb = _ops.FoundSettlement(b, new Vec2i(40, 40));
            var war = new War { Id = _s.Ids.War(), AttackerId = b.Id, DefenderId = a.Id };
            _s.Diplomacy.Wars.Add(war);
            _s.Diplomacy.Get(a.Id, b.Id).WarId = war.Id;
            var armyB = _ops.CreateArmy(b, sb, null);
            int peace = 0;
            _ctx.Events.Subscribe<PeaceSignedEvent>(e => peace++);
            _ops.CollapseCivilization(a, b.Id);
            Assert.IsFalse(a.IsAlive);
            Assert.AreEqual(_s.Tick, a.CollapsedTick);
            Assert.IsFalse(war.IsActive);
            Assert.AreEqual(1, peace);
            Assert.IsFalse(_s.Diplomacy.AreAtWar(a.Id, b.Id));
            Assert.IsTrue(armyB.IsAlive, "other civs' armies untouched");
            _ops.CollapseCivilization(a, b.Id);
            Assert.AreEqual(1, peace, "idempotent");
        }

        // ------------------------------------------------------------------ people
        [Test]
        public void CreatePerson_LinksAndDerivedFields()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var mom = _ops.CreatePerson(st, RaceId.Human, Sex.Female, 30f);
            var dad = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f);
            int born = 0;
            _ctx.Events.Subscribe<PersonBornEvent>(e => { born++; Assert.AreEqual(mom.Id, e.MotherId); });
            var kid = _ops.CreatePerson(st, RaceId.Human, Sex.Female, 0f, mom.Id, dad.Id, true);
            Assert.AreEqual(1, born);
            Assert.AreEqual(1, _s.Stats.TotalBirths);
            Assert.AreEqual(1, mom.ChildrenCount);
            Assert.AreEqual(1, dad.ChildrenCount);
            Assert.AreEqual(1, kid.Generation);
            Assert.AreEqual(LifeStage.Child, kid.Stage);
            Assert.AreEqual(LifeStage.Adult, mom.Stage);
            Assert.AreEqual(st.Id, kid.SettlementId);
            Assert.AreEqual(civ.Id, kid.CivilizationId);
            Assert.IsTrue(st.ResidentIds.Contains(kid.Id));
            Assert.IsTrue(kid.Skill >= 0.6f && kid.Skill <= 1.4f);
            Assert.IsTrue(kid.Faith >= 0f && kid.Faith <= 1f);
            Assert.IsFalse(string.IsNullOrEmpty(kid.Name));
            Assert.AreEqual(0, (int)(kid.Traits & PersonTraits.Hero));
            var negative = _ops.CreatePerson(st, RaceId.Human, Sex.Male, -5f);
            Assert.AreEqual(0f, negative.AgeYears);
            var elder = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 80f);
            Assert.AreEqual(LifeStage.Elder, elder.Stage);
            Assert.AreEqual(_s.Tick - SimTime.YearsToTicks(80f), elder.BirthTick);
        }

        [Test]
        [Ignore("BUG: CreatePerson / CreateBuilding / CreateArmy accept a settlement that is already destroyed (IsAlive == false, pending removal) (WorldOps.cs:233-260, 397-428, 480-499). After FlushRemovals the new person/building references a settlement that no longer exists, and the building keeps its map tiles. Fix: return null (or throw) when !s.IsAlive.")]
        public void CreateInDeadSettlement_IsRejected()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            _ops.FoundSettlement(civ, new Vec2i(50, 50));
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            _ops.DestroySettlement(st, DestroyCause.Disaster);
            Person p = null; Building b = null;
            try { p = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 20f); } catch (Exception) { }
            try { b = _ops.CreateBuilding(st, BuildingType.House, new Vec2i(5, 5)); } catch (Exception) { }
            _ops.FlushRemovals();
            Assert.IsTrue(p == null || !_s.People.Contains(p.Id), "person created in dead settlement");
            Assert.IsTrue(b == null || !_s.Buildings.Contains(b.Id), "building created in dead settlement");
            AssertCore();
        }

        [Test]
        public void KillPerson_ClearsAllLinksIncludingLeader()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var house = House(st, new Vec2i(5, 5));
            var farm = _ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(8, 8), 0, true);
            var p = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 40f);
            var spouse = _ops.CreatePerson(st, RaceId.Human, Sex.Female, 40f);
            p.SpouseId = spouse.Id; spouse.SpouseId = p.Id;
            _ops.AssignHome(p, house);
            _ops.AssignJob(p, farm, JobType.Farmer);
            var army = _ops.CreateArmy(civ, st, new[] { p });
            civ.LeaderId = p.Id;
            int died = 0;
            _ctx.Events.Subscribe<PersonDiedEvent>(e => { died++; Assert.AreEqual(st.Id, e.SettlementId); Assert.AreEqual(DeathCause.Disease, e.Cause); });

            _ops.KillPerson(p, DeathCause.Disease);

            Assert.IsFalse(p.IsAlive);
            Assert.AreEqual(_s.Tick, p.DeathTick);
            Assert.AreEqual(-1, civ.LeaderId);
            Assert.AreEqual(-1, p.HomeId);
            Assert.AreEqual(-1, p.WorkplaceId);
            Assert.AreEqual(-1, p.ArmyId);
            Assert.AreEqual(-1, p.SpouseId);
            Assert.AreEqual(-1, spouse.SpouseId);
            Assert.IsFalse(house.Residents.Contains(p.Id));
            Assert.IsFalse(farm.Workers.Contains(p.Id));
            Assert.IsFalse(army.SoldierIds.Contains(p.Id));
            Assert.IsFalse(st.ResidentIds.Contains(p.Id));
            Assert.AreEqual(1, _s.Stats.TotalDeaths);
            Assert.AreEqual(1, _s.Stats.DeathsByCause[(int)DeathCause.Disease]);
            Assert.AreEqual(1, died);
            _ops.KillPerson(p, DeathCause.Disease);
            Assert.AreEqual(1, _s.Stats.TotalDeaths, "idempotent");
            Assert.IsTrue(_s.People.Contains(p.Id), "removal deferred");
            _ops.FlushRemovals();
            Assert.IsFalse(_s.People.Contains(p.Id));
            AssertCore();
        }

        [Test]
        public void KillPerson_DoesNotClearOtherCivsLeaderOrRemarriedSpouse()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var a = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 40f);
            var b = _ops.CreatePerson(st, RaceId.Human, Sex.Female, 40f);
            var c = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 40f);
            civ.LeaderId = c.Id;
            a.SpouseId = b.Id; b.SpouseId = c.Id; c.SpouseId = b.Id; // a's link is stale
            _ops.KillPerson(a, DeathCause.OldAge);
            Assert.AreEqual(c.Id, b.SpouseId);
            Assert.AreEqual(c.Id, civ.LeaderId);
        }

        [Test]
        public void AssignHome_RespectsCapacityCompletionAndDestruction()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var house = House(st, new Vec2i(5, 5));
            var unfinished = _ops.CreateBuilding(st, BuildingType.House, new Vec2i(7, 7));
            var well = _ops.CreateBuilding(st, BuildingType.Well, new Vec2i(9, 9), 0, true);
            var people = new List<Person>();
            for (int i = 0; i < 7; i++) people.Add(_ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f));
            int housed = 0;
            foreach (var p in people) if (_ops.AssignHome(p, house)) housed++;
            Assert.AreEqual(house.HousingCapacity, housed);
            Assert.AreEqual(0, house.FreeHousing);
            Assert.IsTrue(_ops.AssignHome(people[0], house), "re-assigning the same home is a no-op success");
            Assert.AreEqual(housed, house.Residents.Count);
            Assert.IsFalse(_ops.AssignHome(people[6], unfinished), "incomplete");
            Assert.IsFalse(_ops.AssignHome(people[6], well), "not a dwelling");
            Assert.IsFalse(_ops.AssignHome(null, house));
            Assert.IsFalse(_ops.AssignHome(people[6], null));
            _ops.CompleteBuilding(unfinished);
            Assert.IsTrue(_ops.AssignHome(people[0], unfinished), "moving house");
            Assert.IsFalse(house.Residents.Contains(people[0].Id));
            Assert.AreEqual(unfinished.Center, people[0].Position);
            _ops.DestroyBuilding(unfinished, DestroyCause.Fire);
            Assert.IsFalse(_ops.AssignHome(people[1], unfinished));
            _ops.FlushRemovals();
            AssertCore();
        }

        [Test]
        [Ignore("BUG: AssignHome does not check p.IsAlive (WorldOps.cs:305-315), unlike AssignJob. A person killed earlier in the same system (pending removal) can be housed; after FlushRemovals the building's Residents keeps a removed id and FreeHousing is permanently reduced. Fix: `if (p == null || !p.IsAlive || ...) return false;`.")]
        public void AssignHome_DeadPerson_IsRejected()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var house = House(st, new Vec2i(5, 5));
            var p = _ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f);
            _ops.KillPerson(p, DeathCause.Accident);
            Assert.IsFalse(_ops.AssignHome(p, house));
            _ops.FlushRemovals();
            AssertCore();
        }

        [Test]
        public void AssignJob_RespectsSlotsAndClearsPrevious()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var farm = _ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(8, 8), 0, true);
            var barracks = _ops.CreateBuilding(st, BuildingType.Barracks, new Vec2i(12, 8), 0, true);
            var unfinished = _ops.CreateBuilding(st, BuildingType.Farm, new Vec2i(15, 15));
            var people = new List<Person>();
            for (int i = 0; i < 5; i++) people.Add(_ops.CreatePerson(st, RaceId.Human, Sex.Male, 30f));
            int hired = 0;
            foreach (var p in people) if (_ops.AssignJob(p, farm, JobType.Farmer)) hired++;
            Assert.AreEqual(3, hired);
            Assert.IsTrue(_ops.AssignJob(people[0], farm, JobType.Farmer), "same job/workplace is a no-op success");
            Assert.IsFalse(_ops.AssignJob(people[4], unfinished, JobType.Farmer));
            people[0].Experience = 0.8f;
            Assert.IsTrue(_ops.AssignJob(people[0], barracks, JobType.Soldier));
            Assert.AreEqual(0f, people[0].Experience, "experience resets on job change");
            Assert.IsFalse(farm.Workers.Contains(people[0].Id));
            Assert.AreEqual(barracks.Id, people[0].WorkplaceId);
            Assert.IsTrue(_ops.AssignJob(people[1], null, JobType.Builder));
            Assert.AreEqual(-1, people[1].WorkplaceId);
            Assert.AreEqual(JobType.Builder, people[1].Job);
            Assert.IsFalse(farm.Workers.Contains(people[1].Id));
            _ops.KillPerson(people[4], DeathCause.Accident);
            Assert.IsFalse(_ops.AssignJob(people[4], farm, JobType.Farmer), "dead people cannot work");
            _ops.ClearJob(people[2]);
            Assert.AreEqual(JobType.None, people[2].Job);
            _ops.FlushRemovals();
            AssertCore();
        }

        [Test]
        public void MovePersonToSettlement_UpdatesMembershipAndClearsLinks()
        {
            var civ = _ops.CreateCivilization(RaceId.Human);
            var a = _ops.FoundSettlement(civ, new Vec2i(10, 10));
            var b = _ops.FoundSettlement(civ, new Vec2i(40, 40));
            var house = House(a, new Vec2i(3, 3));
            var farm = _ops.CreateBuilding(a, BuildingType.Farm, new Vec2i(5, 5), 0, true);
            var p = _ops.CreatePerson(a, RaceId.Human, Sex.Male, 30f);
            _ops.AssignHome(p, house);
            _ops.AssignJob(p, farm, JobType.Farmer);
            int migrated = 0;
            _ctx.Events.Subscribe<PersonMigratedEvent>(e => { migrated++; Assert.AreEqual(a.Id, e.FromSettlementId); Assert.AreEqual(b.Id, e.ToSettlementId); });
            _ops.MovePersonToSettlement(p, b);
            Assert.AreEqual(1, migrated);
            Assert.AreEqual(b.Id, p.SettlementId);
            Assert.IsFalse(a.ResidentIds.Contains(p.Id));
            Assert.IsTrue(b.ResidentIds.Contains(p.Id));
            Assert.AreEqual(-1, p.HomeId);
            Assert.AreEqual(-1, p.WorkplaceId);
            Assert.AreEqual(b.Center, p.Position);
            _ops.MovePersonToSettlement(p, b);
            Assert.AreEqual(1, migrated, "moving to the current settlement is a no-op");
            Assert.AreEqual(1, CountOf(b.ResidentIds, p.Id));
            _ops.DestroySettlement(a, DestroyCause.Abandoned);
            var q = _ops.CreatePerson(b, RaceId.Human, Sex.Male, 30f);
            _ops.MovePersonToSettlement(q, a);
            Assert.AreEqual(b.Id, q.SettlementId, "cannot move into a dead settlement");
            _ops.KillPerson(q, DeathCause.OldAge);
            _ops.MovePersonToSettlement(q, b);
            _ops.FlushRemovals();
            AssertCore();
        }

        private static int CountOf(List<int> l, int id) { int n = 0; foreach (int x in l) if (x == id) n++; return n; }

        // ------------------------------------------------------------------ armies & chronicle
        [Test]
        public void CreateArmy_SkipsDeadAndAlreadyEnlisted_DisbandReleases()
        {
            var civ = _ops.CreateCivilization(RaceId.Orc);
            var st = _ops.FoundSettlement(civ, new Vec2i(20, 20));
            var a = _ops.CreatePerson(st, RaceId.Orc, Sex.Male, 20f);
            var b = _ops.CreatePerson(st, RaceId.Orc, Sex.Male, 20f);
            var dead = _ops.CreatePerson(st, RaceId.Orc, Sex.Male, 20f);
            _ops.KillPerson(dead, DeathCause.Violence);
            var first = _ops.CreateArmy(civ, st, new List<Person> { a, dead, null });
            var second = _ops.CreateArmy(civ, st, new List<Person> { a, b });
            CollectionAssert.AreEqual(new[] { a.Id }, first.SoldierIds);
            CollectionAssert.AreEqual(new[] { b.Id }, second.SoldierIds);
            Assert.AreEqual(st.Center, first.Position);
            _ops.DisbandArmy(first);
            Assert.AreEqual(-1, a.ArmyId);
            Assert.AreEqual(ArmyState.Disbanding, first.State);
            Assert.AreEqual(0, first.SoldierIds.Count);
            _ops.DisbandArmy(first);
            _ops.FlushRemovals();
            Assert.IsFalse(_s.Diplomacy.Armies.Contains(first.Id));
            Assert.IsTrue(_s.Diplomacy.Armies.Contains(second.Id));
            AssertCore();
        }

        [Test]
        public void Chronicle_IsBoundedAndTruncated()
        {
            for (int i = 0; i < Chronicle.MaxEntries + 50; i++) _ops.AddChronicle(ChronicleCategory.World, "e" + i, -1, -1, default, 9);
            Assert.AreEqual(Chronicle.MaxEntries, _s.Chronicle.Entries.Count);
            Assert.AreEqual("e50", _s.Chronicle.Entries[0].Text);
            Assert.AreEqual(3, _s.Chronicle.Entries[0].Importance, "importance clamped");
            _ops.AddChronicle(ChronicleCategory.World, new string('x', 1000));
            Assert.AreEqual(300, _s.Chronicle.Entries[_s.Chronicle.Entries.Count - 1].Text.Length);
            _ops.AddChronicle(ChronicleCategory.World, null);
            Assert.AreEqual("", _s.Chronicle.Entries[_s.Chronicle.Entries.Count - 1].Text);
        }

        [Test]
        public void DiplomacyState_RelationsAreSymmetric()
        {
            var d = _s.Diplomacy;
            var r = d.Get(5, 2);
            Assert.AreSame(r, d.Get(2, 5));
            Assert.AreEqual(2, r.CivA);
            Assert.AreEqual(5, r.CivB);
            Assert.Throws<ArgumentException>(() => d.Get(3, 3));
            Assert.IsFalse(d.AreAtWar(2, 5));
            r.WarId = 1;
            Assert.IsTrue(d.AreAtWar(5, 2));
            Assert.IsFalse(d.AreAtWar(5, 5));
            d.Get(2, 9);
            d.RemoveRelationsOf(2);
            Assert.IsFalse(d.TryGet(2, 5, out _));
            Assert.IsFalse(d.TryGet(9, 2, out _));
            Assert.AreNotEqual(DiplomacyState.Key(1, 2), DiplomacyState.Key(2, 3));
            Assert.AreEqual(DiplomacyState.Key(1, 2), DiplomacyState.Key(2, 1));
        }

        [Test]
        public void WorldMap_SizeLimitsAndHelpers()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap(15, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap(64, 1025));
            var m = new WorldMap(16, 20);
            Assert.AreEqual(320, m.TileCount);
            Assert.AreEqual(new Vec2i(3, 2), m.PosOf(m.Index(3, 2)));
            Assert.AreEqual(-1, m.Owner[0]);
            Assert.AreEqual(-1, m.BuildingAt[319]);
            Assert.AreEqual(new Vec2i(15, 0), m.ClampToBounds(new Vec2i(99, -4)));
            Assert.IsFalse(m.InBounds(Vec2i.Invalid));
            Assert.IsFalse(m.IsWater(new Vec2i(-1, 0)));
            m.Set(5, TileFlags.Trees);
            Assert.AreEqual(1, m.CountFlagInRadius(m.PosOf(5), 2, TileFlags.Trees));
            m.Clear(5, TileFlags.Trees);
            Assert.IsFalse(m.Has(5, TileFlags.Trees));
            int v = m.TerrainVersion;
            m.MarkTerrainChanged();
            Assert.AreEqual(v + 1, m.TerrainVersion);
        }
    }
}
