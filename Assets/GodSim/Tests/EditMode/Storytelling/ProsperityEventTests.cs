using GodSim.Core;
using GodSim.Core.Storytelling;
using NUnit.Framework;

namespace GodSim.Tests.Storytelling
{
    public class ProsperityEventTests
    {
        private const float Eps = 1e-3f;

        [Test]
        public void GoldenAge_BoostsCapitalHappinessAndResearch_Bounded()
        {
            var w = StoryWorld.Create();
            w.CapA.Tier = SettlementTier.Town;
            StoryWorld.SetMood(w, w.CapA, 0.8f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.GoldenAge);
            Assert.AreEqual(0.95f, w.CapA.Happiness, Eps);
            Assert.IsTrue(w.A.ResearchPool >= 40f && w.A.ResearchPool <= 1500f, "research " + w.A.ResearchPool);
            Assert.AreEqual(w.A.ResearchPool, w.A.TotalResearch, Eps);
            Assert.AreEqual(2, StoryWorld.LastChronicle(w).Importance);

            // bounded: a second golden age can never push happiness past 1
            w.A.Memory.Remove("story.cd." + WorldEventKeys.GoldenAge);
            StoryWorld.Fire(w, w.A, WorldEventKeys.GoldenAge);
            Assert.AreEqual(1f, w.CapA.Happiness, Eps);
            StoryWorld.AssertSane(w);
        }

        [Test]
        public void GoldenAge_RequiresTownAndHighHappiness()
        {
            var w = StoryWorld.Create();
            w.CapA.Tier = SettlementTier.Village;
            StoryWorld.SetMood(w, w.CapA, 0.9f);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GoldenAge));
            w.CapA.Tier = SettlementTier.City;
            StoryWorld.SetMood(w, w.CapA, 0.5f);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GoldenAge));
        }

        [Test]
        public void HarvestFestival_OnlyInAutumn_FeastsAndCheers()
        {
            var w = StoryWorld.Create();
            w.CapA.Stockpile.SetRaw(ResourceType.Food, 120f);
            StoryWorld.SetSeason(w, Season.Spring);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.HarvestFestival));
            StoryWorld.SetSeason(w, Season.Autumn);
            StoryWorld.Fire(w, w.A, WorldEventKeys.HarvestFestival);
            Assert.AreEqual(120f * 0.92f, w.CapA.Stockpile.Get(ResourceType.Food), 0.01f);
            Assert.AreEqual(0.68f, w.CapA.Happiness, Eps);
        }

        [Test]
        public void GoldRush_AddsOneGoldDepositOnHillsInTerritory()
        {
            var w = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GoldRush), "no hills");
            StoryWorld.SetTile(w, 35, 29, Biome.Hills);
            w.A.Age = AgeId.Dawn;
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GoldRush), "needs Bronze age");
            w.A.Age = AgeId.Bronze;
            float gold0 = w.CapA.Stockpile.Get(ResourceType.Gold);
            StoryWorld.Fire(w, w.A, WorldEventKeys.GoldRush);
            var map = w.State.Map;
            int i = map.Index(35, 29);
            Assert.IsTrue(map.Has(i, TileFlags.GoldDeposit));
            Assert.IsTrue(map.Resource[i] >= 250f && map.Resource[i] <= 500f);
            int deposits = 0;
            for (int k = 0; k < map.TileCount; k++) if (map.Has(k, TileFlags.GoldDeposit)) deposits++;
            Assert.AreEqual(1, deposits);
            float gained = w.CapA.Stockpile.Get(ResourceType.Gold) - gold0;
            Assert.IsTrue(gained >= 10f - Eps && gained <= 25f + Eps);
        }

        [Test]
        public void GoldRush_CapsDepositsPerSettlement()
        {
            var w = StoryWorld.Create();
            w.A.Age = AgeId.Bronze;
            for (int x = 33; x <= 35; x++) StoryWorld.SetTile(w, x, 28, Biome.Hills);
            string key = "story.cd." + WorldEventKeys.GoldRush;
            StoryWorld.Fire(w, w.A, WorldEventKeys.GoldRush);
            w.A.Memory.Remove(key);
            StoryWorld.Fire(w, w.A, WorldEventKeys.GoldRush);
            w.A.Memory.Remove(key);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GoldRush), "at most two deposits per territory");
        }

        [Test]
        public void WanderingSage_GrantsWiseTraitAndResearch()
        {
            var w = StoryWorld.Create();
            int wise0 = CountTrait(w, w.CapA, PersonTraits.Wise);
            StoryWorld.Fire(w, w.A, WorldEventKeys.WanderingSage);
            Assert.AreEqual(wise0 + 1, CountTrait(w, w.CapA, PersonTraits.Wise));
            Assert.IsTrue(w.A.ResearchPool >= 20f - Eps && w.A.ResearchPool <= 600f + Eps);
        }

        [Test]
        public void MasterBuilder_AdvancesOnlyPaidConstruction_CappedBelowCompletion()
        {
            var w = StoryWorld.Create();
            var b = StoryWorld.AddBuildings(w, w.CapA, BuildingType.House, 3, false);
            b[0].CostPaid = true; b[0].ConstructionProgress = 0.3f;
            b[1].CostPaid = true; b[1].ConstructionProgress = 0.9f;
            b[2].CostPaid = false; b[2].ConstructionProgress = 0.1f;
            StoryWorld.Fire(w, w.A, WorldEventKeys.MasterBuilder);
            Assert.AreEqual(0.55f, b[0].ConstructionProgress, Eps);
            Assert.AreEqual(0.99f, b[1].ConstructionProgress, Eps);
            Assert.AreEqual(0.1f, b[2].ConstructionProgress, Eps);
            Assert.IsFalse(b[0].IsComplete || b[1].IsComplete || b[2].IsComplete);
        }

        [Test]
        public void MasterBuilder_NotEligibleWithoutConstruction()
        {
            var w = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.MasterBuilder));
        }

        [Test]
        public void BountifulFishing_OnlyNearWaterAndNotInWinter()
        {
            var w = StoryWorld.Create();
            w.CapA.Stockpile.SetRaw(ResourceType.Food, 50f);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.BountifulFishing), "inland");
            for (int x = 33; x <= 35; x++) StoryWorld.SetTile(w, x, 35, Biome.Lake);
            StoryWorld.SetSeason(w, Season.Winter);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.BountifulFishing), "winter");
            StoryWorld.SetSeason(w, Season.Summer);
            StoryWorld.Fire(w, w.A, WorldEventKeys.BountifulFishing);
            float food = w.CapA.Stockpile.Get(ResourceType.Food);
            Assert.AreEqual(50f + 15f + 1.5f * 12f, food, 0.01f);
            Assert.IsTrue(food <= w.CapA.Stockpile.Capacity);
        }

        [Test]
        public void BabyBoom_CreatesNewbornsWithMothers()
        {
            var w = StoryWorld.Create(adults: 24);
            int pop0 = StoryWorld.Alive(w, w.CapA);
            long births0 = w.State.Stats.TotalBirths;
            StoryWorld.Fire(w, w.A, WorldEventKeys.BabyBoom);
            int born = StoryWorld.Alive(w, w.CapA) - pop0;
            Assert.IsTrue(born >= 2 && born <= 3, "born " + born); // 24 / 8 = 3 max
            Assert.AreEqual(births0 + born, w.State.Stats.TotalBirths);
            int babies = 0;
            foreach (var p in StoryWorld.Residents(w, w.CapA))
            {
                if (p.AgeYears > 0.01f) continue;
                babies++;
                var mother = w.State.People.Get(p.MotherId);
                Assert.IsNotNull(mother);
                Assert.AreEqual(Sex.Female, mother.Sex);
            }
            Assert.AreEqual(born, babies);
        }

        [Test]
        public void BabyBoom_RequiresHappinessAndFood()
        {
            var w = StoryWorld.Create();
            StoryWorld.SetMood(w, w.CapA, 0.4f);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.BabyBoom));
        }

        [Test]
        public void GreatMigration_Adds10To20OfSameRaceToSmallestSettlement()
        {
            var w = StoryWorld.Create();
            var small = StoryWorld.AddTown(w, w.A, new Vec2i(30, 70), 4, RaceId.Human);
            int big0 = StoryWorld.Alive(w, w.CapA);
            StoryWorld.Fire(w, w.A, WorldEventKeys.GreatMigration);
            int added = StoryWorld.Alive(w, small) - 4;
            Assert.IsTrue(added >= 10 && added <= 20, "added " + added);
            Assert.AreEqual(big0, StoryWorld.Alive(w, w.CapA));
            foreach (var p in StoryWorld.Residents(w, small))
            {
                Assert.AreEqual(RaceId.Human, p.Race);
                Assert.AreEqual(w.A.Id, p.CivilizationId);
            }
        }

        [Test]
        public void GreatMigration_RespectsPopulationCap()
        {
            var w = StoryWorld.Create();
            w.Ctx.Config.MaxPopulation = w.State.LivingPopulation + 5;
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GreatMigration));
        }

        [Test]
        public void TradeCaravan_NeedsContact_BringsGoldGoodsAndOpinion()
        {
            var w = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.TradeCaravan), "no contact");
            var rel = w.State.Diplomacy.Get(w.A.Id, w.B.Id);
            rel.HasContact = true;
            float gold0 = w.CapA.Stockpile.Get(ResourceType.Gold);
            StoryWorld.Fire(w, w.A, WorldEventKeys.TradeCaravan);
            float gold = w.CapA.Stockpile.Get(ResourceType.Gold) - gold0;
            Assert.IsTrue(gold >= 10f - Eps && gold <= 30f + Eps);
            Assert.IsTrue(w.CapA.Stockpile.Get(ResourceType.Goods) >= 5f - Eps);
            Assert.AreEqual(5f, rel.Opinion, Eps);
            rel.WarId = 3;
            w.A.Memory.Remove("story.cd." + WorldEventKeys.TradeCaravan);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.TradeCaravan), "at war");
        }

        [Test]
        public void AncientRuins_GivesResearchAndGold_AndClearsTheRuins()
        {
            var w = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.AncientRuins));
            StoryWorld.SetFlag(w, 26, 33, TileFlags.Ruins);
            StoryWorld.Fire(w, w.A, WorldEventKeys.AncientRuins);
            Assert.IsFalse(w.State.Map.Has(w.State.Map.Index(26, 33), TileFlags.Ruins));
            Assert.IsTrue(w.A.ResearchPool >= 25f - Eps && w.A.ResearchPool <= 800f + Eps);
            Assert.IsTrue(w.CapA.Stockpile.Get(ResourceType.Gold) >= 20f - Eps);
            Assert.AreEqual(2, StoryWorld.LastChronicle(w).Importance);
        }

        [Test]
        public void CometOmen_RaisesDevotion_WorldWideCooldown_Importance2()
        {
            var w = StoryWorld.Create();
            StoryWorld.Fire(w, w.A, WorldEventKeys.CometOmen);
            Assert.AreEqual(0.6f, w.A.Devotion, Eps);
            Assert.AreEqual(0.54f, w.B.Devotion, Eps);
            Assert.AreEqual(2, StoryWorld.LastChronicle(w).Importance);
            Assert.IsTrue(w.State.Memory.ContainsKey("story.cd." + WorldEventKeys.CometOmen));
            Assert.IsFalse(StoryWorld.TryFire(w, w.B, WorldEventKeys.CometOmen), "one comet per 75 years world-wide");
            w.A.Devotion = 0.99f;
            w.State.Tick += SimTime.YearsToTicks(75f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.CometOmen);
            Assert.AreEqual(1f, w.A.Devotion, Eps);
        }

        [Test]
        public void SacredSpring_RaisesFaithAndDevotion()
        {
            var w = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.SacredSpring));
            StoryWorld.SetFlag(w, 27, 27, TileFlags.Sacred);
            float faith0 = w.CapA.Faith;
            StoryWorld.Fire(w, w.A, WorldEventKeys.SacredSpring);
            Assert.AreEqual(faith0 + 0.15f, w.CapA.Faith, Eps);
            Assert.AreEqual(0.54f, w.A.Devotion, Eps);
            Assert.IsTrue(w.State.Map.Has(w.State.Map.Index(27, 27), TileFlags.Sacred), "spring stays sacred");
        }

        [Test]
        public void HolyVisions_MakesADevoutSeer()
        {
            var w = StoryWorld.Create();
            foreach (var p in StoryWorld.Residents(w, w.CapA)) p.Traits &= ~PersonTraits.Devout;
            StoryWorld.Fire(w, w.A, WorldEventKeys.HolyVisions);
            Assert.AreEqual(1, CountTrait(w, w.CapA, PersonTraits.Devout));
            Assert.AreEqual(0.56f, w.A.Devotion, Eps);
        }

        [Test]
        public void HeroBorn_GrantsHeroOnce_AndNeedsPopulation()
        {
            var w = StoryWorld.Create(adults: 10);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.HeroBorn), "too few people");
            StoryWorld.AddTown(w, w.A, new Vec2i(30, 70), 6, RaceId.Human);
            StoryWorld.Fire(w, w.A, WorldEventKeys.HeroBorn);
            int heroes = 0;
            foreach (var p in w.State.People.Items)
                if (p.IsAlive && p.CivilizationId == w.A.Id && p.HasTrait(PersonTraits.Hero))
                {
                    heroes++;
                    Assert.IsTrue(p.HasTrait(PersonTraits.Brave));
                    Assert.IsTrue(p.Skill <= 1.4f + Eps);
                }
            Assert.AreEqual(1, heroes);
        }

        internal static int CountTrait(StoryWorld.World w, Settlement s, PersonTraits t)
        {
            int n = 0;
            foreach (var p in StoryWorld.Residents(w, s)) if (p.HasTrait(t)) n++;
            return n;
        }
    }
}
