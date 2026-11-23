using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Core.Races;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class OrcCultureAdvisorTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = BuildDb();
        }

        // ------------------------------------------------------------------ warrior heart
        [Test]
        public void IronAge_VotesBarracksAndArmoryEarly_AndTheMetalChain()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Iron, 40, out _);
            Run(ctx);

            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Barracks), Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Armory), Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Smelter), Is.GreaterThan(0.3f), "metal for the armory");
            Assert.IsTrue(LogContains(s, "war-hall"), "flavourful reason logged");
        }

        [Test]
        public void ArmoryBuilt_StaffsSmithsWhenWeaponsAreLow()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Iron, 40, out _);
            Place(ctx, s, BuildingType.Armory, true);
            Run(ctx);
            Assert.That(AdvisorBoard.JobBias(s, JobType.Smith), Is.GreaterThan(0.3f));
        }

        // ------------------------------------------------------------------ hunter-herders
        [Test]
        public void DawnAndBronze_PreferHuntingAndHerdingOverFarms()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var dawn = Orc(ctx, AgeId.Dawn, 20, out _);
            Run(ctx);
            Assert.That(AdvisorBoard.BuildBias(dawn, BuildingType.HuntingLodge), Is.GreaterThan(0.4f));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(dawn, BuildingType.Farm), "farm not available in Dawn: no vote");

            var ctx2 = TestWorlds.CreateContext(_db, 96);
            var bronze = Orc(ctx2, AgeId.Bronze, 20, out _);
            Run(ctx2);
            float farm = AdvisorBoard.BuildBias(bronze, BuildingType.Farm);
            Assert.That(AdvisorBoard.BuildBias(bronze, BuildingType.Pasture), Is.GreaterThan(0.3f));
            Assert.That(AdvisorBoard.BuildBias(bronze, BuildingType.HuntingLodge), Is.GreaterThan(farm));
            Assert.That(farm, Is.LessThan(0f), "fields are scorned while the hunt feeds the clan");
        }

        [Test]
        public void Medieval_AdoptsFarms()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Medieval, 40, out _);
            Run(ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Farm), Is.GreaterThan(0f));
        }

        [Test]
        public void BronzeStarving_WithNoHuntOrHerds_AcceptsFarms()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Bronze, 20, out _);
            s.Memory["econ.foodDays"] = 1f;
            for (int i = 0; i < 3; i++) Queue(s, BuildingType.HuntingLodge); // lodge limit reached
            for (int i = 0; i < 3; i++) Queue(s, BuildingType.Pasture);
            Run(ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Farm), Is.GreaterThan(0f));
        }

        // ------------------------------------------------------------------ raiding posture
        [Test]
        public void WeakerHostileNeighbour_RaisesSoldierVotes()
        {
            var calm = TestWorlds.CreateContext(_db, 96);
            var a = Orc(calm, AgeId.Iron, 40, out _);
            Place(calm, a, BuildingType.Barracks, true);
            Run(calm);
            float baseline = AdvisorBoard.JobBias(a, JobType.Soldier);

            var tense = TestWorlds.CreateContext(_db, 96);
            var b = Orc(tense, AgeId.Iron, 40, out var orcs);
            Place(tense, b, BuildingType.Barracks, true);
            var weak = Other(tense, RaceId.Human, new Vec2i(40, 30), 4, out var humans);
            tense.State.Diplomacy.Get(orcs.Id, humans.Id).Stance = DiplomaticStance.Hostile;
            Run(tense);

            Assert.That(AdvisorBoard.JobBias(b, JobType.Soldier), Is.GreaterThan(baseline + 0.3f));
            Assert.That(b.Mem(OrcCultureAdvisor.RaidKey), Is.GreaterThan(0.3f));
            Assert.IsTrue(LogContains(b, "The weak invite the strong"));
            Assert.AreEqual(0, CountKeys(weak, "ai."), "the neighbour itself is untouched");
        }

        [Test]
        public void StrongerHostileNeighbour_IsRespectedNotRaided()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Iron, 12, out var orcs);
            var strong = Other(ctx, RaceId.Human, new Vec2i(40, 30), 40, out var humans);
            strong.DefenseStrength = 50f;
            ctx.State.Diplomacy.Get(orcs.Id, humans.Id).Stance = DiplomaticStance.Hostile;
            Run(ctx);

            Assert.IsFalse(s.Memory.ContainsKey(OrcCultureAdvisor.RaidKey));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Palisade), Is.GreaterThan(0.5f));
        }

        [Test]
        public void NeutralNeighbour_IsNotRaided()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Iron, 40, out var orcs);
            Other(ctx, RaceId.Human, new Vec2i(40, 30), 4, out var humans);
            ctx.State.Diplomacy.Get(orcs.Id, humans.Id).Stance = DiplomaticStance.Neutral;
            Run(ctx);
            Assert.IsFalse(s.Memory.ContainsKey(OrcCultureAdvisor.RaidKey));
        }

        // ------------------------------------------------------------------ honoring wrath
        [Test]
        public void WrathNearby_GivesFearfulDevotionVotes_NeverResentment()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Iron, 40, out _);
            var adv = new OrcCultureAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Meteor, Target = s.Center + new Vec2i(3, 0), Radius = 3, Success = true });
            Assert.That(s.Mem(OrcCultureAdvisor.DreadKey), Is.GreaterThan(0.5f));
            TestWorlds.Run(adv, ctx, 24, false);

            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Temple), Is.GreaterThan(0.4f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Shrine), Is.GreaterThan(0.3f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.WarIdol), Is.GreaterThan(0.6f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Priest), Is.GreaterThan(0.3f));
            foreach (var t in new[] { BuildingType.SpiritTotem, BuildingType.Shrine, BuildingType.Temple, BuildingType.Cathedral, BuildingType.WarIdol })
                Assert.That(AdvisorBoard.BuildBias(s, t), Is.GreaterThanOrEqualTo(0f), "no resentment toward " + t);
            Assert.IsTrue(LogContains(s, "anger is just"));
        }

        [Test]
        public void Dread_FadesOverTime_AndBlessingsOrFarWrathCauseNone()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Iron, 40, out _);
            var adv = new OrcCultureAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Bless, Target = s.Center, Radius = 3, Success = true });
            ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Meteor, Target = new Vec2i(90, 90), Radius = 2, Success = true });
            ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Meteor, Target = s.Center, Radius = 2, Success = false });
            Assert.IsFalse(s.Memory.ContainsKey(OrcCultureAdvisor.DreadKey));

            ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Lightning, Target = s.Center, Radius = 1, Success = true });
            float d0 = s.Mem(OrcCultureAdvisor.DreadKey);
            Assert.That(d0, Is.GreaterThan(0f));
            TestWorlds.Run(adv, ctx, 24 * 30, false);
            Assert.IsFalse(s.Memory.ContainsKey(OrcCultureAdvisor.DreadKey), "dread fades");
        }

        // ------------------------------------------------------------------ arena, idol, housing, defenses
        [Test]
        public void Classical_ArenaUp_TheaterDown_StoneWallScorned()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Classical, 50, out _);
            Run(ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Arena), Is.GreaterThan(0.8f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Theater), Is.LessThan(-0.5f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.StoneWall), Is.LessThan(0f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Palisade), Is.GreaterThan(0.4f));
        }

        [Test]
        public void WarIdol_CapitalHoardsWhenAvailable_ButNotWhenQueued()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Bronze, 20, out _);
            Run(ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.WarIdol), Is.GreaterThan(0.5f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Woodcutter), Is.GreaterThan(0.2f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Quarrier), Is.GreaterThan(0.2f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Storehouse), Is.GreaterThan(0.3f), "180 wood will not fit in 150 capacity");

            var ctx2 = TestWorlds.CreateContext(_db, 96);
            var q = Orc(ctx2, AgeId.Bronze, 20, out _);
            Queue(q, BuildingType.WarIdol);
            Run(ctx2);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(q, BuildingType.WarIdol));
            Assert.AreEqual(0f, AdvisorBoard.JobBias(q, JobType.Quarrier));
            Assert.That(AdvisorBoard.JobBias(q, JobType.Builder), Is.GreaterThan(0.3f));
        }

        [Test]
        public void Housing_SprawlsWithFertility_AndHomelessPushHarder()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Bronze, 20, out _);
            Run(ctx);
            float calm = AdvisorBoard.BuildBias(s, BuildingType.House);
            // Calibrated as > 0.3 at the pre-2026-11-19 Orc fertility of 0.45/yr; the vote scales with fertility, so the
            // bar scales with it too (0.22/yr after the balance pass -> > 0.147).
            float fertility = _db.Race(RaceId.Orc).FertilityPerYear;
            Assert.That(calm, Is.GreaterThan(0.3f * fertility / 0.45f), "orc fertility (" + fertility + "/yr) drives an early house vote");

            var ctx2 = TestWorlds.CreateContext(_db, 96);
            var h = Orc(ctx2, AgeId.Bronze, 20, out _);
            h.Homeless = 8;
            Run(ctx2);
            Assert.That(AdvisorBoard.BuildBias(h, BuildingType.House), Is.GreaterThan(calm + 0.4f));
        }

        // ------------------------------------------------------------------ isolation & determinism
        [Test]
        public void NonOrcs_AreUntouched()
        {
            foreach (var race in new[] { RaceId.Human, RaceId.Elf, RaceId.Dwarf, RaceId.Saurian })
            {
                var ctx = TestWorlds.CreateContext(_db, 96);
                var s = TestWorlds.AddSettlement(ctx, race, new Vec2i(30, 30), 40, out var civ);
                civ.Age = AgeId.Iron;
                s.Tier = SettlementTier.Village;
                Other(ctx, RaceId.Orc == race ? RaceId.Human : RaceId.Orc, new Vec2i(40, 30), 3, out var other);
                ctx.State.Diplomacy.Get(civ.Id, other.Id).Stance = DiplomaticStance.War;
                var adv = new OrcCultureAdvisor();
                adv.Initialize(ctx);
                ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Meteor, Target = s.Center, Radius = 3, Success = true });
                TestWorlds.Run(adv, ctx, 24 * 3, false);
                Assert.AreEqual(0, CountKeys(s, "ai."), race + " must be untouched");
                Assert.AreEqual(0, s.AiLog.Count);
            }
        }

        [Test]
        public void SameWorld_SameVotes()
        {
            Assert.AreEqual(Scenario(), Scenario());
        }

        [Test]
        public void NoNaN_WithGarbageInputs()
        {
            var ctx = TestWorlds.CreateContext(_db, 96);
            var s = Orc(ctx, AgeId.Iron, 40, out var civ);
            s.Memory["econ.foodDays"] = float.NaN;
            s.Memory[OrcCultureAdvisor.DreadKey] = float.PositiveInfinity;
            s.ServiceCoverage[(int)ServiceType.Entertainment] = float.NaN;
            s.DefenseStrength = float.NaN;
            civ.Memory["ai.threat"] = float.NaN;
            Run(ctx);
            foreach (var kv in s.Memory)
                if (kv.Key.StartsWith("ai.", System.StringComparison.Ordinal))
                    Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Barracks), Is.GreaterThan(0f));
        }

        // ================================================================== helpers
        private string Scenario()
        {
            var ctx = TestWorlds.CreateContext(_db, 96, 7);
            var s = Orc(ctx, AgeId.Iron, 40, out var orcs);
            var second = ctx.Ops.FoundSettlement(orcs, new Vec2i(60, 60));
            second.Tier = SettlementTier.Hamlet;
            for (int i = 0; i < 10; i++) ctx.Ops.CreatePerson(second, RaceId.Orc, Sex.Male, 20f);
            Other(ctx, RaceId.Elf, new Vec2i(45, 35), 5, out var elves);
            ctx.State.Diplomacy.Get(orcs.Id, elves.Id).Stance = DiplomaticStance.War;
            var adv = new OrcCultureAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Earthquake, Target = s.Center, Radius = 4, Success = true });
            TestWorlds.Run(adv, ctx, 24 * 8, false);
            return Dump(s) + "|" + Dump(second);
        }

        private static string Dump(Settlement s)
        {
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var k in keys) sb.Append(k).Append('=').Append(s.Memory[k].ToString("R")).Append(';');
            foreach (var l in s.AiLog) sb.Append(l).Append('\n');
            return sb.ToString();
        }

        private static void Run(SimContext ctx) => TestWorlds.Run(new OrcCultureAdvisor(), ctx, 24);

        private static Settlement Orc(SimContext ctx, AgeId age, int adults, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(30, 30), adults, out civ);
            civ.Age = age;
            s.Tier = SettlementTier.Village;
            s.Stockpile.SetRaw(ResourceType.Food, 150f);
            return s;
        }

        private static Settlement Other(SimContext ctx, RaceId race, Vec2i at, int adults, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, race, at, adults, out civ);
            civ.Age = AgeId.Iron;
            return s;
        }

        private static void Place(SimContext ctx, Settlement s, BuildingType t, bool complete)
        {
            var def = ctx.Db.Building(t);
            var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 12);
            Assert.IsTrue(site.IsValid);
            Assert.IsNotNull(ctx.Ops.CreateBuilding(s, t, site, 0, complete));
        }

        private static void Queue(Settlement s, BuildingType t) => s.BuildQueue.Add(new BuildOrder { Type = t, Priority = 1f, Reason = "test" });

        private static bool LogContains(Settlement s, string text)
        {
            foreach (var l in s.AiLog) if (l.Contains(text)) return true;
            return false;
        }

        private static int CountKeys(Settlement s, string prefix)
        {
            int n = 0;
            foreach (var k in s.Memory.Keys) if (k.StartsWith(prefix, System.StringComparison.Ordinal)) n++;
            return n;
        }

        // ------------------------------------------------------------------ database: real races, minimal buildings
        private static GameDatabase BuildDb()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);
            Reg(db, BuildingType.House, BuildingCategory.Housing, AgeId.Dawn, d => { d.HousingMin = 3; d.HousingMax = 5; });
            Reg(db, BuildingType.TownCenter, BuildingCategory.Civic, AgeId.Dawn, d => { d.MaxPerSettlement = 1; d.Width = 2; d.Height = 2; });
            Reg(db, BuildingType.Storehouse, BuildingCategory.Storage, AgeId.Dawn, d => d.StorageCapacity = 100f);
            Reg(db, BuildingType.GathererHut, BuildingCategory.Food, AgeId.Dawn, d => { d.MaxAge = AgeId.Iron; Job(d, JobType.Gatherer, 2, ResourceType.Food, 0.08f); });
            Reg(db, BuildingType.HuntingLodge, BuildingCategory.Food, AgeId.Dawn, d => { d.MaxAge = AgeId.Medieval; Job(d, JobType.Hunter, 2, ResourceType.Food, 0.12f); });
            Reg(db, BuildingType.WoodcutterCamp, BuildingCategory.Resource, AgeId.Dawn, d => Job(d, JobType.Woodcutter, 2, ResourceType.Wood, 0.12f));
            Reg(db, BuildingType.StoneQuarry, BuildingCategory.Resource, AgeId.Dawn, d => Job(d, JobType.Quarrier, 2, ResourceType.Stone, 0.08f));
            Reg(db, BuildingType.SpiritTotem, BuildingCategory.Religion, AgeId.Dawn, d => { d.MaxAge = AgeId.Iron; d.MinPopulation = 5; });
            Reg(db, BuildingType.Campfire, BuildingCategory.Culture, AgeId.Dawn, d => d.MaxAge = AgeId.Classical);
            Reg(db, BuildingType.Farm, BuildingCategory.Food, AgeId.Bronze, d => Job(d, JobType.Farmer, 4, ResourceType.Food, 0.22f));
            Reg(db, BuildingType.Pasture, BuildingCategory.Food, AgeId.Bronze, d => { d.MinPopulation = 8; Job(d, JobType.Herder, 2, ResourceType.Food, 0.1f); });
            Reg(db, BuildingType.Mine, BuildingCategory.Resource, AgeId.Bronze, d => Job(d, JobType.Miner, 3, ResourceType.Ore, 0.08f));
            Reg(db, BuildingType.Smelter, BuildingCategory.Industry, AgeId.Bronze, d => { Job(d, JobType.Smith, 2, ResourceType.Metal, 0.05f); d.InputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.06f) }; });
            Reg(db, BuildingType.Shrine, BuildingCategory.Religion, AgeId.Bronze, d => { d.Job = JobType.Priest; d.JobSlots = 1; });
            Reg(db, BuildingType.Palisade, BuildingCategory.Military, AgeId.Bronze, d => d.MaxAge = AgeId.Medieval);
            Reg(db, BuildingType.Temple, BuildingCategory.Religion, AgeId.Iron, d => { d.Job = JobType.Priest; d.JobSlots = 2; d.MinTier = SettlementTier.Hamlet; });
            Reg(db, BuildingType.Barracks, BuildingCategory.Military, AgeId.Iron, d => { d.Job = JobType.Soldier; d.JobSlots = 6; d.MinPopulation = 30; d.MinTier = SettlementTier.Hamlet; d.MaxPerSettlement = 2; d.Width = 2; d.Height = 2; });
            Reg(db, BuildingType.Watchtower, BuildingCategory.Military, AgeId.Iron, d => { d.MaxAge = AgeId.Industrial; d.Job = JobType.Soldier; d.JobSlots = 1; d.MinPopulation = 15; });
            Reg(db, BuildingType.Armory, BuildingCategory.Industry, AgeId.Iron, d =>
            {
                Job(d, JobType.Smith, 2, ResourceType.Weapons, 0.03f);
                d.InputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.04f), new ResourceAmount(ResourceType.Wood, 0.02f) };
                d.MinPopulation = 30; d.MinTier = SettlementTier.Hamlet; d.Width = 2;
            });
            Reg(db, BuildingType.Theater, BuildingCategory.Culture, AgeId.Classical, d => { d.MinTier = SettlementTier.Village; d.MinPopulation = 25; });
            Reg(db, BuildingType.Arena, BuildingCategory.Culture, AgeId.Classical, d => { d.MinTier = SettlementTier.Village; d.MinPopulation = 45; });
            Reg(db, BuildingType.StoneWall, BuildingCategory.Military, AgeId.Classical, d => { d.MaxAge = AgeId.Industrial; d.MinPopulation = 10; });
            Reg(db, BuildingType.Cathedral, BuildingCategory.Religion, AgeId.Medieval, null);
            Reg(db, BuildingType.Tavern, BuildingCategory.Culture, AgeId.Medieval, null);
            Reg(db, BuildingType.Stadium, BuildingCategory.Culture, AgeId.Modern, null);
            Reg(db, BuildingType.WarIdol, BuildingCategory.Wonder, AgeId.Bronze, d =>
            {
                d.AllowedRaces = RaceMask.Orc; d.IsWonder = true; d.UniquePerCivilization = true; d.MinTier = SettlementTier.Hamlet;
                d.Width = 2; d.Height = 2;
                d.Cost = new[] { new ResourceAmount(ResourceType.Wood, 180f), new ResourceAmount(ResourceType.Stone, 120f) };
            });
            for (int i = 0; i < Counts.GodPowers; i++)
            {
                var p = (GodPowerType)i;
                bool wrath = p >= GodPowerType.Lightning;
                float mana = p == GodPowerType.Lightning ? 5f : p == GodPowerType.Meteor ? 120f : 60f;
                db.Register(new GodPowerDefinition { Type = p, Name = p.ToString(), Category = wrath ? GodPowerCategory.Wrath : GodPowerCategory.Blessing, ManaCost = mana, CooldownTicks = 5, MinRadius = 0, MaxRadius = 8 });
            }
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static void Reg(GameDatabase db, BuildingType t, BuildingCategory cat, AgeId minAge, System.Action<BuildingDefinition> edit)
        {
            var d = new BuildingDefinition { Type = t, Name = t.ToString(), Category = cat, MinAge = minAge, BuildWork = 10f };
            edit?.Invoke(d);
            db.Register(d);
        }

        private static void Job(BuildingDefinition d, JobType job, int slots, ResourceType output, float amount)
        {
            d.Job = job;
            d.JobSlots = slots;
            d.OutputsPerWorker = new[] { new ResourceAmount(output, amount) };
        }
    }
}
