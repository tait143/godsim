using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Storytelling;
using NUnit.Framework;

namespace GodSim.Tests.Storytelling
{
    public class CalamityAndPoliticsEventTests
    {
        private const float Eps = 1e-3f;

        private static readonly ResourceType[] Loot =
        {
            ResourceType.Food, ResourceType.Wood, ResourceType.Gold, ResourceType.Goods,
            ResourceType.Tools, ResourceType.Metal, ResourceType.Cloth, ResourceType.Weapons
        };

        private static Settlement RaidableColony(StoryWorld.World w)
        {
            var colony = StoryWorld.AddTown(w, w.A, new Vec2i(80, 30), 10, RaceId.Human);
            colony.Stockpile.SetRaw(ResourceType.Food, 100f);
            colony.Stockpile.SetRaw(ResourceType.Gold, 50f);
            colony.Stockpile.SetRaw(ResourceType.Wood, 40f);
            colony.Stockpile.SetRaw(ResourceType.Tools, 20f);
            return colony;
        }

        [Test]
        public void BanditRaid_HitsOutskirts_StealsTenToTwentyFivePercent_KillsWithoutSoldiers()
        {
            var w = StoryWorld.Create();
            var colony = RaidableColony(w);
            var before = new float[Loot.Length];
            for (int i = 0; i < Loot.Length; i++) before[i] = colony.Stockpile.Get(Loot[i]);
            float capFood = w.CapA.Stockpile.Get(ResourceType.Food);
            StoryWorld.Fire(w, w.A, WorldEventKeys.BanditRaid);
            int hit = 0;
            for (int i = 0; i < Loot.Length; i++)
            {
                float after = colony.Stockpile.Get(Loot[i]);
                Assert.IsTrue(after <= before[i] + Eps && after >= before[i] * 0.75f - Eps, Loot[i] + " lost too much");
                if (after < before[i] - Eps) { hit++; Assert.IsTrue(after <= before[i] * 0.9f + Eps, "at least 10%"); }
            }
            Assert.IsTrue(hit >= 2 && hit <= 3, "stockpiles hit: " + hit);
            int dead = 10 - StoryWorld.Alive(w, colony);
            Assert.IsTrue(dead >= 1 && dead <= 2, "deaths " + dead);
            Assert.AreEqual(capFood, w.CapA.Stockpile.Get(ResourceType.Food), Eps, "capital untouched");
            StoryWorld.AssertSane(w);
        }

        [Test]
        public void BanditRaid_NoDeathsWhenSoldiersGuard()
        {
            var w = StoryWorld.Create();
            var colony = RaidableColony(w);
            colony.Soldiers = 4;
            StoryWorld.Fire(w, w.A, WorldEventKeys.BanditRaid);
            Assert.AreEqual(10, StoryWorld.Alive(w, colony));
        }

        [Test]
        public void PlagueOfRats_EatsThirtyPercent_UnlessGranary()
        {
            var w = StoryWorld.Create();
            w.CapA.Stockpile.SetRaw(ResourceType.Food, 120f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.PlagueOfRats);
            Assert.AreEqual(84f, w.CapA.Stockpile.Get(ResourceType.Food), 0.01f);

            var w2 = StoryWorld.Create();
            w2.CapA.Stockpile.SetRaw(ResourceType.Food, 120f);
            StoryWorld.AddBuildings(w2, w2.CapA, BuildingType.Granary, 1, true);
            Assert.IsFalse(StoryWorld.TryFire(w2, w2.A, WorldEventKeys.PlagueOfRats));
            w2.CapA.Stockpile.SetRaw(ResourceType.Food, 30f);
            Assert.IsFalse(StoryWorld.TryFire(w2, w2.B, WorldEventKeys.PlagueOfRats), "small stockpile");
        }

        [Test]
        public void WolfWinter_ColdBiomeWinterOnly_KillsAFew()
        {
            var w = StoryWorld.Create(fill: Biome.Tundra);
            StoryWorld.SetSeason(w, Season.Summer);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.WolfWinter));
            StoryWorld.SetSeason(w, Season.Winter);
            StoryWorld.Fire(w, w.A, WorldEventKeys.WolfWinter);
            int dead = 12 - StoryWorld.Alive(w, w.CapA);
            Assert.IsTrue(dead >= 1 && dead <= 2, "deaths " + dead);

            var warm = StoryWorld.Create();
            StoryWorld.SetSeason(warm, Season.Winter);
            Assert.IsFalse(StoryWorld.TryFire(warm, warm.A, WorldEventKeys.WolfWinter));
        }

        [Test]
        public void EarthTremor_DamagesButNeverDestroys()
        {
            var w = StoryWorld.Create();
            var houses = StoryWorld.AddBuildings(w, w.CapA, BuildingType.House, 4, true);
            int count0 = w.CapA.BuildingIds.Count;
            StoryWorld.Fire(w, w.A, WorldEventKeys.EarthTremor);
            int damaged = 0;
            foreach (int id in w.CapA.BuildingIds)
            {
                var b = w.State.Buildings.Get(id);
                Assert.IsFalse(b.IsDestroyed);
                Assert.IsTrue(b.Condition >= 0.05f && b.Condition <= 1f);
                if (b.Condition < 1f) { damaged++; Assert.IsTrue(b.Condition >= 0.7f - Eps, "minor damage only"); }
            }
            Assert.IsTrue(damaged >= 2 && damaged <= 5, "damaged " + damaged);
            Assert.AreEqual(count0, w.CapA.BuildingIds.Count);
            Assert.IsNotNull(houses);
        }

        [Test]
        public void GreatFire_SetsBuildingsAndTilesBurning_VillageOnly()
        {
            var w = StoryWorld.Create();
            StoryWorld.AddBuildings(w, w.CapA, BuildingType.House, 6, true);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GreatFire), "camp");
            w.CapA.Tier = SettlementTier.Village;
            StoryWorld.Fire(w, w.A, WorldEventKeys.GreatFire);
            var map = w.State.Map;
            int burning = 0;
            foreach (int id in w.CapA.BuildingIds)
            {
                var b = w.State.Buildings.Get(id);
                if (!b.IsOnFire) continue;
                burning++;
                Assert.IsTrue(map.Has(b.Origin, TileFlags.Burning));
                Assert.IsFalse(b.IsDestroyed, "the fire module decides what burns down");
            }
            Assert.IsTrue(burning >= 1 && burning <= 3, "burning " + burning);
            Assert.AreEqual(2, StoryWorld.LastChronicle(w).Importance);
            w.A.Memory.Remove("story.cd." + WorldEventKeys.GreatFire);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.GreatFire), "already burning");
        }

        [Test]
        public void LostExpedition_EitherLosesTwoToFourOrReturnsWithKnowledge()
        {
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var w = StoryWorld.Create(seed, 20);
                StoryWorld.Fire(w, w.A, WorldEventKeys.LostExpedition);
                int lost = 20 - StoryWorld.Alive(w, w.CapA);
                if (lost > 0)
                {
                    Assert.IsTrue(lost >= 2 && lost <= 4, "lost " + lost);
                    Assert.AreEqual(0f, w.A.ResearchPool, Eps);
                }
                else
                {
                    Assert.IsTrue(w.A.ResearchPool >= 15f - Eps && w.A.ResearchPool <= 500f + Eps);
                    Assert.IsTrue(w.CapA.Stockpile.Get(ResourceType.Gold) >= 15f - Eps);
                }
            }
        }

        [Test]
        public void MineCollapse_KillsOnlyMinersAndDamagesMine()
        {
            var w = StoryWorld.Create();
            w.A.Age = AgeId.Bronze;
            var mine = StoryWorld.AddBuildings(w, w.CapA, BuildingType.Mine, 1, true)[0];
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.MineCollapse), "unstaffed");
            var miners = new List<Person>();
            var res = StoryWorld.Residents(w, w.CapA);
            for (int i = 0; i < 3; i++) { Assert.IsTrue(w.Ops.AssignJob(res[i], mine, JobType.Miner)); miners.Add(res[i]); }
            w.CapA.Stockpile.SetRaw(ResourceType.Ore, 50f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.MineCollapse);
            int deadMiners = 0;
            foreach (var m in miners) if (!m.IsAlive) deadMiners++;
            Assert.IsTrue(deadMiners >= 1 && deadMiners <= 3);
            Assert.AreEqual(12 - deadMiners, StoryWorld.Alive(w, w.CapA));
            Assert.AreEqual(0.8f, mine.Condition, Eps);
            Assert.AreEqual(45f, w.CapA.Stockpile.Get(ResourceType.Ore), 0.01f);
        }

        [Test]
        public void Riots_LootAndVentUnrest()
        {
            var w = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Riots), "calm");
            w.CapA.Unrest = 0.7f;
            w.CapA.Stockpile.SetRaw(ResourceType.Gold, 100f);
            StoryWorld.Fire(w, w.A, WorldEventKeys.Riots);
            float gold = w.CapA.Stockpile.Get(ResourceType.Gold);
            Assert.IsTrue(gold >= 75f - Eps && gold <= 85f + Eps, "gold " + gold);
            Assert.AreEqual(0.55f, w.CapA.Unrest, Eps);
            var tc = w.State.Buildings.Get(w.CapA.TownCenterId);
            Assert.AreEqual(0.75f, tc.Condition, Eps);
        }

        [Test]
        public void RoyalWedding_FriendlyCivs_PlusTwentyOpinion_BothOnCooldown()
        {
            var w = StoryWorld.Create();
            var rel = w.State.Diplomacy.Get(w.A.Id, w.B.Id);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.RoyalWedding), "neutral");
            rel.Stance = DiplomaticStance.Friendly;
            rel.Opinion = 90f;
            StoryWorld.Fire(w, w.A, WorldEventKeys.RoyalWedding);
            Assert.AreEqual(100f, rel.Opinion, Eps, "clamped");
            Assert.IsFalse(StoryWorld.TryFire(w, w.B, WorldEventKeys.RoyalWedding), "partner shares the cooldown");
            Assert.AreEqual(2, StoryWorld.LastChronicle(w).Importance);
        }

        [Test]
        public void BorderIncident_NeighboursOnly_MinusTwenty()
        {
            var far = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(far, far.A, WorldEventKeys.BorderIncident));

            var w = StoryWorld.Create();
            StoryWorld.AddTown(w, w.B, new Vec2i(60, 30), 5, RaceId.Elf);
            var rel = w.State.Diplomacy.Get(w.A.Id, w.B.Id);
            rel.Opinion = -90f;
            StoryWorld.Fire(w, w.A, WorldEventKeys.BorderIncident);
            Assert.AreEqual(-100f, rel.Opinion, Eps, "clamped");
            Assert.IsTrue(rel.HasContact);
            Assert.IsFalse(StoryWorld.TryFire(w, w.B, WorldEventKeys.BorderIncident));

            var war = StoryWorld.Create();
            StoryWorld.AddTown(war, war.B, new Vec2i(60, 30), 5, RaceId.Elf);
            war.State.Diplomacy.Get(war.A.Id, war.B.Id).WarId = 1;
            Assert.IsFalse(StoryWorld.TryFire(war, war.A, WorldEventKeys.BorderIncident), "at war: no incident");
        }

        [Test]
        public void SuccessionCrisis_Leaderless_RaisesCapitalUnrest()
        {
            var w = StoryWorld.Create();
            Assert.AreEqual(-1, w.A.LeaderId);
            StoryWorld.Fire(w, w.A, WorldEventKeys.SuccessionCrisis);
            Assert.AreEqual(0.3f, w.CapA.Unrest, Eps);
            Assert.AreEqual(2, StoryWorld.LastChronicle(w).Importance);
        }

        [Test]
        public void SuccessionCrisis_DetectsLeaderDeath_NotWhileLeaderLives()
        {
            var w = StoryWorld.Create();
            var leader = StoryWorld.Residents(w, w.CapA)[0];
            w.A.LeaderId = leader.Id;
            var sys = new WorldEventSystem();
            sys.Initialize(w.Ctx);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.SuccessionCrisis), "a living leader");

            w.Ops.KillPerson(leader, DeathCause.OldAge);
            w.Ops.FlushRemovals();
            w.State.Memory[WorldEventSystem.ChanceScaleKey] = 0f;
            w.Ctx.DeltaTicks = sys.TickInterval;
            sys.Tick(w.Ctx);
            Assert.AreEqual((float)w.State.Tick, w.A.Mem(WorldEventSystem.LeaderLostTickKey, -1f), Eps);
            w.CapA.Unrest = 0.9f;
            StoryWorld.Fire(w, w.A, WorldEventKeys.SuccessionCrisis);
            Assert.AreEqual(1f, w.CapA.Unrest, Eps, "clamped");
        }

        [Test]
        public void HeroicDefence_BesiegedSettlement_DefenseBoost_BesiegersShaken()
        {
            var w = StoryWorld.Create();
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.HeroicDefence), "no siege");
            var army = w.Ops.CreateArmy(w.B, w.CapB, null);
            army.State = ArmyState.Besieging;
            army.TargetSettlementId = w.CapA.Id;
            w.CapA.DefenseStrength = 20f;
            w.CapA.ServiceOutput[(int)ServiceType.Defense] = 5f;
            StoryWorld.Fire(w, w.A, WorldEventKeys.HeroicDefence);
            Assert.AreEqual(20f + 20f, w.CapA.DefenseStrength, Eps); // 10 + 0.5 * 20
            Assert.AreEqual(5f + 20f, w.CapA.ServiceOutput[(int)ServiceType.Defense], Eps);
            Assert.AreEqual(0.75f, army.Morale, Eps);
            Assert.AreEqual(1, ProsperityEventTests.CountTrait(w, w.CapA, PersonTraits.Hero));
        }

        [Test]
        public void Exodus_UnhappyPeopleMoveToAnotherCiv()
        {
            var w = StoryWorld.Create(adults: 20);
            Assert.IsFalse(StoryWorld.TryFire(w, w.A, WorldEventKeys.Exodus), "content");
            StoryWorld.SetMood(w, w.CapA, 0.2f);
            int b0 = StoryWorld.Alive(w, w.CapB);
            StoryWorld.Fire(w, w.A, WorldEventKeys.Exodus);
            int left = 20 - StoryWorld.Alive(w, w.CapA);
            Assert.IsTrue(left >= 2 && left <= 15, "left " + left);
            Assert.IsTrue(StoryWorld.Alive(w, w.CapA) >= 3);
            Assert.AreEqual(b0 + left, StoryWorld.Alive(w, w.CapB));
            foreach (var p in StoryWorld.Residents(w, w.CapB)) Assert.AreEqual(w.B.Id, p.CivilizationId);

            var war = StoryWorld.Create(adults: 20);
            StoryWorld.SetMood(war, war.CapA, 0.2f);
            war.State.Diplomacy.Get(war.A.Id, war.B.Id).WarId = 2;
            Assert.IsFalse(StoryWorld.TryFire(war, war.A, WorldEventKeys.Exodus), "no refuge with the enemy");
        }
    }
}
