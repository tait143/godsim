using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Heroes;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Heroes
{
    public class HeroEffectTests
    {
        private SimContext _ctx;
        private Settlement _s;
        private Civilization _civ;
        private HeroSystem _sys;

        [SetUp]
        public void SetUp()
        {
            _ctx = HeroTestKit.NewContext(3);
            _s = HeroTestKit.PlainSettlement(_ctx, RaceId.Human, new Vec2i(32, 32), 12, out _civ);
            _sys = new HeroSystem { RaiseChanceScale = 0f };
            _ctx.DeltaTicks = _sys.TickInterval;
            _sys.Initialize(_ctx);
        }

        private Person Hero(HeroRole role, int index = 0)
        {
            var p = HeroTestKit.Resident(_ctx, _s, index);
            Assert.IsTrue(_sys.Promote(_ctx, p, role));
            return p;
        }

        [Test]
        public void PromoteRejectsChildrenDeadAndExistingHeroes()
        {
            var child = _ctx.Ops.CreatePerson(_s, RaceId.Human, Sex.Male, 3f);
            Assert.IsFalse(_sys.Promote(_ctx, child, HeroRole.Bard));
            var p = Hero(HeroRole.Bard);
            Assert.IsFalse(_sys.Promote(_ctx, p, HeroRole.Sage));
            var dead = HeroTestKit.Resident(_ctx, _s, 1);
            _ctx.Ops.KillPerson(dead, DeathCause.Disease);
            Assert.IsFalse(_sys.Promote(_ctx, dead, HeroRole.Sage));
            Assert.IsFalse(_sys.Promote(_ctx, null, HeroRole.Sage));
        }

        [Test]
        public void GeneralRaisesArmyMoraleUpToCap()
        {
            var p = Hero(HeroRole.General);
            var army = _ctx.Ops.CreateArmy(_civ, _s, new List<Person> { p });
            army.Morale = 1f;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(1.05f, army.Morale, 1e-4f);
            army.Morale = 1.18f;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(1.2f, army.Morale, 1e-4f);
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(1.2f, army.Morale, 1e-4f);
            army.Morale = 1.5f; // set higher by someone else: never lowered
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(1.5f, army.Morale, 1e-4f);
        }

        [Test]
        public void GeneralBoostsDefenseOnlyWhileBesieged()
        {
            var p = Hero(HeroRole.General);
            int def = (int)ServiceType.Defense;
            _s.ServiceOutput[def] = 10f;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(10f, _s.ServiceOutput[def], 1e-4f, "no siege, no bonus");

            var enemy = _ctx.Ops.CreateCivilization(RaceId.Orc);
            var enemyHome = _ctx.Ops.FoundSettlement(enemy, new Vec2i(8, 8));
            var army = _ctx.Ops.CreateArmy(enemy, enemyHome, null);
            army.State = ArmyState.Besieging;
            army.TargetSettlementId = _s.Id;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(11f, _s.ServiceOutput[def], 1e-3f);
            Assert.Greater(HeroQueries.DeedsOf(_ctx.State, p.Id), 0);
        }

        [Test]
        public void SageAddsResearchScaledByAge()
        {
            Hero(HeroRole.Sage);
            _civ.Age = AgeId.Classical;
            float pool = _civ.ResearchPool, total = _civ.TotalResearch;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(pool + 3.5f, _civ.ResearchPool, 1e-4f);
            Assert.AreEqual(total + 3.5f, _civ.TotalResearch, 1e-4f);
        }

        [Test]
        public void ProphetRaisesDevotionAndFaithBounded()
        {
            Hero(HeroRole.Prophet);
            _civ.Devotion = 0.5f;
            _s.Faith = 0.5f;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(0.51f, _civ.Devotion, 1e-4f);
            Assert.AreEqual(0.51f, _s.Faith, 1e-4f);
            _civ.Devotion = 0.995f;
            _s.Faith = 0.999f;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(1f, _civ.Devotion, 1e-6f);
            Assert.AreEqual(1f, _s.Faith, 1e-6f);
        }

        [Test]
        public void ArchitectSpeedsPaidConstructionAndCompletesThroughOps()
        {
            var p = Hero(HeroRole.Architect);
            var site1 = _ctx.Ops.FindFreeSite(new Vec2i(36, 36), 1, 1, 10);
            var slow = _ctx.Ops.CreateBuilding(_s, BuildingType.House, site1);
            slow.CostPaid = true; slow.ConstructionProgress = 0.5f;
            var site2 = _ctx.Ops.FindFreeSite(new Vec2i(28, 36), 1, 1, 10);
            var nearly = _ctx.Ops.CreateBuilding(_s, BuildingType.Well, site2);
            nearly.CostPaid = true; nearly.ConstructionProgress = 0.99f;
            var site3 = _ctx.Ops.FindFreeSite(new Vec2i(36, 28), 1, 1, 10);
            var unpaid = _ctx.Ops.CreateBuilding(_s, BuildingType.Storehouse, site3);
            unpaid.ConstructionProgress = 0.99f;

            int completedEvents = 0;
            _ctx.Events.Subscribe<BuildingCompletedEvent>(e => { if (e.BuildingId == nearly.Id) completedEvents++; });
            long before = _ctx.State.Stats.BuildingsCompleted;

            HeroTestKit.OneRun(_sys, _ctx);

            Assert.AreEqual(0.52f, slow.ConstructionProgress, 1e-4f);
            Assert.IsFalse(slow.IsComplete);
            Assert.IsTrue(nearly.IsComplete);
            Assert.AreEqual(1f, nearly.ConstructionProgress);
            Assert.AreEqual(1, completedEvents, "completed via ctx.Ops (event published once)");
            Assert.AreEqual(before + 1, _ctx.State.Stats.BuildingsCompleted);
            Assert.AreEqual(0.99f, unpaid.ConstructionProgress, 1e-6f, "unpaid sites are not advanced");
            Assert.IsFalse(unpaid.IsComplete);
            Assert.AreEqual(1, HeroQueries.DeedsOf(_ctx.State, p.Id));

            // completions by other systems in the hero's settlement count too
            _ctx.Ops.CompleteBuilding(slow);
            Assert.AreEqual(2, HeroQueries.DeedsOf(_ctx.State, p.Id));
            for (int i = 0; i < 100; i++) HeroTestKit.OneRun(_sys, _ctx);
            Assert.LessOrEqual(slow.ConstructionProgress, 1f);
            Assert.LessOrEqual(unpaid.ConstructionProgress, 1f);
        }

        [Test]
        public void ExplorerRevealsDepositOrRuinsNearTerritoryEdge()
        {
            var p = Hero(HeroRole.Explorer);
            _sys.DiscoveryChanceScale = 1000f;
            var map = _ctx.State.Map;
            const TileFlags finds = TileFlags.Ruins | TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit;
            int version = map.TerrainVersion;
            HeroTestKit.OneRun(_sys, _ctx);

            int found = 0;
            for (int i = 0; i < map.TileCount; i++)
            {
                if ((map.Flags[i] & finds) == 0) continue;
                found++;
                var pos = map.PosOf(i);
                int d = pos.ChebyshevTo(_s.Center);
                Assert.That(d, Is.InRange(_s.TerritoryRadius - 1, _s.TerritoryRadius + 3));
                if ((map.Flags[i] & TileFlags.Ruins) == 0) Assert.That(map.Resource[i], Is.InRange(200f, 400f));
            }
            Assert.AreEqual(1, found);
            Assert.AreNotEqual(version, map.TerrainVersion);
            Assert.AreEqual(1, HeroQueries.DeedsOf(_ctx.State, p.Id));
        }

        [Test]
        public void MerchantHealerBardEffectsAreSmallAndBounded()
        {
            var merchant = Hero(HeroRole.MerchantPrince, 0);
            merchant.Skill = 1f;
            float gold = _s.Stockpile.Get(ResourceType.Gold);
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(gold + 1.5f, _s.Stockpile.Get(ResourceType.Gold), 1e-4f);

            // healer + bard in a second settlement
            var s2 = _ctx.Ops.FoundSettlement(_civ, new Vec2i(10, 10));
            var healer = _ctx.Ops.CreatePerson(s2, RaceId.Human, Sex.Female, 30f);
            var s3 = _ctx.Ops.FoundSettlement(_civ, new Vec2i(54, 54));
            var bard = _ctx.Ops.CreatePerson(s3, RaceId.Human, Sex.Male, 30f);
            var sick = _ctx.Ops.CreatePerson(s2, RaceId.Human, Sex.Male, 30f);
            var sad = _ctx.Ops.CreatePerson(s3, RaceId.Human, Sex.Female, 30f);
            Assert.IsTrue(_sys.Promote(_ctx, healer, HeroRole.Healer));
            Assert.IsTrue(_sys.Promote(_ctx, bard, HeroRole.Bard));
            s2.Health = 0.5f; sick.Health = 0.4f;
            s3.Happiness = 0.5f; sad.Happiness = 0.3f;
            HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(0.51f, s2.Health, 1e-4f);
            Assert.AreEqual(0.405f, sick.Health, 1e-4f);
            Assert.AreEqual(0.51f, s3.Happiness, 1e-4f);
            Assert.AreEqual(0.305f, sad.Happiness, 1e-4f);

            for (int i = 0; i < 400; i++) HeroTestKit.OneRun(_sys, _ctx);
            Assert.AreEqual(1f, s2.Health, 1e-6f);
            Assert.AreEqual(1f, sick.Health, 1e-6f);
            Assert.AreEqual(1f, s3.Happiness, 1e-6f);
            Assert.AreEqual(1f, sad.Happiness, 1e-6f);
            Assert.LessOrEqual(_s.Stockpile.Get(ResourceType.Gold), _s.Stockpile.Capacity + 1e-3f);
        }

        [Test]
        public void AllRolesLongRunStayBoundedAndFinite()
        {
            var roles = (HeroRole[])System.Enum.GetValues(typeof(HeroRole));
            var heroes = new List<Person>();
            var spots = new[] { new Vec2i(8, 8), new Vec2i(8, 32), new Vec2i(8, 56), new Vec2i(32, 8), new Vec2i(32, 56), new Vec2i(56, 8), new Vec2i(56, 32), new Vec2i(56, 56) };
            for (int r = 0; r < roles.Length; r++)
            {
                Settlement s = r == 0 ? _s : _ctx.Ops.FoundSettlement(_civ, spots[r]);
                var p = r == 0 ? HeroTestKit.Resident(_ctx, _s, 0) : _ctx.Ops.CreatePerson(s, RaceId.Human, Sex.Male, 30f);
                Assert.IsTrue(_sys.Promote(_ctx, p, roles[r]));
                heroes.Add(p);
            }
            var army = _ctx.Ops.CreateArmy(_civ, _s, new List<Person> { heroes[0] });
            army.State = ArmyState.Marching;
            _civ.Age = AgeId.Ascension;
            _sys.DiscoveryChanceScale = 5f;
            float research0 = _civ.TotalResearch;
            const int runs = 300;
            for (int i = 0; i < runs; i++) HeroTestKit.OneRun(_sys, _ctx);

            Assert.LessOrEqual(army.Morale, HeroRules.GeneralMoraleCap + 1e-5f);
            Assert.LessOrEqual(_civ.Devotion, 1f);
            Assert.AreEqual(research0 + runs * HeroRules.SageResearch(AgeId.Ascension), _civ.TotalResearch, 0.5f);
            foreach (var sid in _civ.SettlementIds)
            {
                var s = _ctx.State.Settlements.Get(sid);
                Assert.LessOrEqual(s.Health, 1f);
                Assert.LessOrEqual(s.Happiness, 1f);
                Assert.LessOrEqual(s.Faith, 1f);
                Assert.IsFalse(float.IsNaN(s.Stockpile.Get(ResourceType.Gold)));
            }
            foreach (var p in heroes)
            {
                int d = HeroQueries.DeedsOf(_ctx.State, p.Id);
                Assert.That(d, Is.InRange(0, (int)HeroRules.MaxDeeds));
                Assert.IsTrue(HeroQueries.IsHero(_ctx.State, p.Id));
            }
            // steady roles earn about one deed per year
            Assert.AreEqual(runs / 4, HeroQueries.DeedsOf(_ctx.State, heroes[(int)HeroRole.Sage].Id));
            foreach (var kv in _ctx.State.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }
    }
}
