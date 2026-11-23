using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Population;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Population.Lifecycle
{
    public class BirthSystemTests
    {
        private static GameDatabase FertileDb(float fertility, int maxChildren = 5)
        {
            return LifecycleTestSupport.CreateDb(d =>
            {
                foreach (var r in d.Races) { r.FertilityPerYear = fertility; r.MaxChildren = maxChildren; }
            });
        }

        [Test]
        public void BirthsOccur_WithFoodAndCouples()
        {
            var db = FertileDb(0.5f);
            var ctx = TestWorlds.CreateContext(db, seed: 9);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5000f, out var civ);
            LifecycleTestSupport.AddCouples(ctx, s, 8);
            var born = new List<PersonBornEvent>();
            ctx.Events.Subscribe<PersonBornEvent>(e => born.Add(e));

            TestWorlds.Run(new BirthSystem(), ctx, 5 * SimTime.TicksPerYear);

            Assert.IsTrue(born.Count >= 3, "expected births, got " + born.Count);
            Assert.AreEqual(born.Count, (int)ctx.State.Stats.TotalBirths);
            Assert.AreEqual(born.Count, (int)s.Mem(LifecycleMemoryKeys.TotalBirths));
            foreach (var e in born)
            {
                var child = ctx.State.People.Get(e.PersonId);
                var mother = ctx.State.People.Get(e.MotherId);
                Assert.IsNotNull(child);
                Assert.IsNotNull(mother);
                Assert.AreEqual(Sex.Female, mother.Sex);
                Assert.AreEqual(mother.SpouseId, e.FatherId);
                Assert.AreEqual(s.Id, child.SettlementId);
                Assert.AreEqual(civ.Id, child.CivilizationId);
                Assert.AreEqual(LifeStage.Child, child.Stage);
                Assert.AreEqual(0f, child.AgeYears, 1e-6f);
                Assert.IsTrue(s.ResidentIds.Contains(child.Id));
                Assert.IsTrue(mother.ChildrenCount >= 1);
            }
        }

        [Test]
        public void NoBirths_WhenMothersAreHungrySickElderlyOrAtMaxChildren()
        {
            var db = FertileDb(1f);
            var ctx = TestWorlds.CreateContext(db, seed: 4);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5000f, out _);
            var wives = LifecycleTestSupport.AddCouples(ctx, s, 4);
            wives[0].Satiation = 0.35f;
            wives[1].Health = 0.25f;
            wives[2].ChildrenCount = 5;
            wives[3].AgeYears = 60f;
            wives[3].Stage = LifeStage.Elder;
            int births = 0;
            ctx.Events.Subscribe<PersonBornEvent>(e => births++);

            TestWorlds.Run(new BirthSystem(), ctx, 3 * SimTime.TicksPerYear);

            Assert.AreEqual(0, births);
        }

        [Test]
        public void NoBirths_WhenHusbandLivesElsewhereOrIsDead_InAnEstablishedTribe()
        {
            var db = FertileDb(1f);
            var ctx = TestWorlds.CreateContext(db, seed: 4);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(20, 20), 5000f, out var civ);
            var other = ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            var couples = LifecycleTestSupport.AddCouples(ctx, s, 3);
            foreach (var w in couples) w.ChildrenCount = 5; // the couples themselves are done having children
            var away = LifecycleTestSupport.AddCouples(ctx, s, 1)[0];
            ctx.Ops.MovePersonToSettlement(ctx.State.People.Get(away.SpouseId), other);
            var births = new List<PersonBornEvent>();
            ctx.Events.Subscribe<PersonBornEvent>(e => births.Add(e));

            TestWorlds.Run(new BirthSystem(), ctx, 3 * SimTime.TicksPerYear);

            Assert.AreEqual(0, births.Count, "with 3 couples, a woman whose husband lives elsewhere has no children");
        }

        [Test]
        public void Births_StopAtMaxPopulation()
        {
            var db = FertileDb(1f, 50);
            var ctx = TestWorlds.CreateContext(db, seed: 13);
            ctx.Config.MaxPopulation = 100;
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 20000f, out _);
            LifecycleTestSupport.AddCouples(ctx, s, 45);
            Assert.AreEqual(90, ctx.State.LivingPopulation);

            TestWorlds.Run(new BirthSystem(), ctx, 3 * SimTime.TicksPerYear);

            Assert.AreEqual(100, ctx.State.LivingPopulation, "births fill up to the cap and stop exactly there");
            Assert.AreEqual(10, (int)ctx.State.Stats.TotalBirths);
        }

        [Test]
        public void Saurians_LayClutches_OthersRarelyHaveTwins()
        {
            var db = FertileDb(1f, 1000);
            var ctx = TestWorlds.CreateContext(db, seed: 17);
            var lizards = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Saurian, new Vec2i(16, 16), 20000f, out _);
            var humans = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(48, 48), 20000f, out _);
            LifecycleTestSupport.AddCouples(ctx, lizards, 10);
            LifecycleTestSupport.AddCouples(ctx, humans, 10);
            var clutch = new Dictionary<long, int>();
            var raceOfMother = new Dictionary<long, RaceId>();
            ctx.Events.Subscribe<PersonBornEvent>(e =>
            {
                long key = ctx.Tick * 100000L + e.MotherId;
                clutch.TryGetValue(key, out int n);
                clutch[key] = n + 1;
                raceOfMother[key] = e.Race;
            });

            // 7 years (was 2): these settlements have no houses, so the housing gate added by the 2026-11-19 balance
            // pass (LifecycleRules.HousingFactor, x0.3 with no free bed) slows births; the test is about clutch sizes.
            TestWorlds.Run(new BirthSystem(), ctx, 7 * SimTime.TicksPerYear);

            int saurianMax = 0, saurianMin = int.MaxValue, humanMax = 0, saurianClutches = 0, humanBirths = 0;
            foreach (var kv in clutch)
            {
                if (raceOfMother[kv.Key] == RaceId.Saurian)
                {
                    saurianClutches++;
                    if (kv.Value > saurianMax) saurianMax = kv.Value;
                    if (kv.Value < saurianMin) saurianMin = kv.Value;
                }
                else
                {
                    humanBirths++;
                    if (kv.Value > humanMax) humanMax = kv.Value;
                }
            }
            Assert.IsTrue(saurianClutches > 20);
            // Balance pass 2026-11-19: clutches are 1-2 eggs (was 1-3).
            Assert.AreEqual(LifecycleBalance.SaurianClutchMax, saurianMax, "Saurian clutches reach the maximum");
            Assert.AreEqual(1, saurianMin, "single eggs happen too");
            Assert.IsTrue(humanBirths > 20);
            Assert.IsTrue(humanMax <= 2, "humans have at most twins");
        }

        [Test]
        public void TinyTribe_UnpartneredWomanCanBearChildren()
        {
            var db = FertileDb(1f);
            var ctx = TestWorlds.CreateContext(db, seed: 23);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5000f, out _);
            var survivor = LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 20f);
            survivor.Traits = PersonTraits.None;
            var fathers = new List<int>();
            ctx.Events.Subscribe<PersonBornEvent>(e => fathers.Add(e.FatherId));

            TestWorlds.Run(new BirthSystem(), ctx, 10 * SimTime.TicksPerYear);

            Assert.IsTrue(fathers.Count >= 1, "a lone survivor should still keep the tribe alive");
            foreach (int f in fathers) Assert.AreEqual(-1, f);
        }

        [Test]
        public void UnpartneredWomen_DoNotBearChildren_WhenTheTribeHasCouples()
        {
            var db = FertileDb(1f);
            var ctx = TestWorlds.CreateContext(db, seed: 29);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5000f, out _);
            LifecycleTestSupport.AddCouples(ctx, s, 3);
            var singles = new List<Person>();
            for (int i = 0; i < 3; i++) singles.Add(LifecycleTestSupport.Add(ctx, s, RaceId.Human, Sex.Female, 22f));
            int births = 0;
            ctx.Events.Subscribe<PersonBornEvent>(e => { births++; Assert.IsTrue(e.FatherId >= 0); });

            TestWorlds.Run(new BirthSystem(), ctx, 3 * SimTime.TicksPerYear);

            Assert.IsTrue(births > 0);
            foreach (var w in singles) Assert.AreEqual(0, w.ChildrenCount);
        }

        [Test]
        public void Newborn_MovesIntoMothersHome_WhenThereIsRoom()
        {
            var db = FertileDb(1f);
            var ctx = TestWorlds.CreateContext(db, seed: 31);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5000f, out _);
            var wife = LifecycleTestSupport.AddCouples(ctx, s, 2)[0];
            var husband = ctx.State.People.Get(wife.SpouseId);
            var house = ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(10, 10), 0, true);
            Assert.IsNotNull(house);
            Assert.IsTrue(ctx.Ops.AssignHome(wife, house));
            Assert.IsTrue(ctx.Ops.AssignHome(husband, house));
            int childId = -1;
            ctx.Events.Subscribe<PersonBornEvent>(e => { if (e.MotherId == wife.Id && childId < 0) childId = e.PersonId; });

            TestWorlds.Run(new BirthSystem(), ctx, SimTime.TicksPerYear);

            Assert.IsTrue(childId > 0);
            var child = ctx.State.People.Get(childId);
            Assert.AreEqual(house.Id, child.HomeId);
            Assert.IsTrue(house.Residents.Contains(childId));
            Assert.IsTrue(house.Residents.Count <= house.HousingCapacity);
        }

        [Test]
        public void FoodStockFactor_FollowsDaysOfFood()
        {
            Assert.AreEqual(0.3f, LifecycleRules.FoodStockFactor(0f), 1e-6f);
            Assert.AreEqual(0.3f, LifecycleRules.FoodStockFactor(10f), 1e-6f);
            Assert.AreEqual(0.75f, LifecycleRules.FoodStockFactor(35f), 1e-5f);
            Assert.AreEqual(1.2f, LifecycleRules.FoodStockFactor(60f), 1e-6f);
            Assert.AreEqual(1.2f, LifecycleRules.FoodStockFactor(1e9f), 1e-6f);
            Assert.AreEqual(0.3f, LifecycleRules.FoodStockFactor(float.NaN), 1e-6f);
            Assert.AreEqual(20f, LifecycleRules.DaysOfFood(10f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, LifecycleRules.DaysOfFood(0f, 0f), 1e-6f);
            Assert.AreEqual(1.4f, LifecycleRules.HappinessFactor(1f), 1e-6f);
            Assert.AreEqual(0.6f, LifecycleRules.HappinessFactor(0f), 1e-6f);
            Assert.AreEqual(1f, LifecycleRules.HungerFactor(1f), 1e-6f);
            Assert.AreEqual(0.5f, LifecycleRules.HungerFactor(0.75f), 1e-5f);
            Assert.AreEqual(0f, LifecycleRules.HungerFactor(0.4f), 1e-6f);
            Assert.AreEqual(1f, LifecycleRules.HungerFactor(float.NaN), 1e-6f);
        }

        [Test]
        public void CrowdedHousing_AndStarvation_SlowBirths()
        {
            var db = FertileDb(0.5f, 1000);
            int Births(System.Action<Settlement> setup)
            {
                var ctx = TestWorlds.CreateContext(db, seed: 41);
                var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 20000f, out _);
                LifecycleTestSupport.AddCouples(ctx, s, 30);
                setup(s);
                int n = 0;
                ctx.Events.Subscribe<PersonBornEvent>(e => n++);
                TestWorlds.Run(new BirthSystem(), ctx, 10 * SimTime.TicksPerYear);
                return n;
            }
            int normal = Births(s => { });
            int crowded = Births(s => s.Homeless = 30);
            int hungry = Births(s => s.Memory[LifecycleMemoryKeys.FoodRatio] = 0.6f);
            Assert.IsTrue(crowded < normal * 0.7f, "crowded " + crowded + " vs normal " + normal);
            Assert.IsTrue(hungry < normal * 0.5f, "hungry " + hungry + " vs normal " + normal);
        }

        [Test]
        public void ZeroGrowth_PreventsBirths()
        {
            var db = FertileDb(1f);
            var ctx = TestWorlds.CreateContext(db, seed: 37);
            var s = LifecycleTestSupport.EmptySettlement(ctx, RaceId.Human, new Vec2i(32, 32), 5000f, out var civ);
            LifecycleTestSupport.AddCouples(ctx, s, 5);
            civ.Modifiers.Growth = 0f;
            int births = 0;
            ctx.Events.Subscribe<PersonBornEvent>(e => births++);

            TestWorlds.Run(new BirthSystem(), ctx, 2 * SimTime.TicksPerYear);
            Assert.AreEqual(0, births);

            civ.Modifiers.Growth = 1f;
            ctx.Config.GrowthMultiplier = 0f;
            TestWorlds.Run(new BirthSystem(), ctx, 2 * SimTime.TicksPerYear);
            Assert.AreEqual(0, births);
        }
    }
}
