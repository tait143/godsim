using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class UrbanDesignAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static BuildingDefinition Def(BuildingType t, BuildingCategory c, AgeId minAge, params ServiceType[] services)
        {
            var d = new BuildingDefinition { Type = t, Name = t.ToString(), Category = c, MinAge = minAge, BuildWork = 10f };
            if (services.Length > 0)
            {
                d.Services = new ServiceAmount[services.Length];
                for (int i = 0; i < services.Length; i++) d.Services[i] = new ServiceAmount(services[i], 1f);
            }
            return d;
        }

        /// <summary>Registers a small real-ish catalog; every other slot is an unavailable (Ascension) stub.</summary>
        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            var tc = Def(BuildingType.TownCenter, BuildingCategory.Civic, AgeId.Dawn, ServiceType.Administration);
            tc.MaxPerSettlement = 1; tc.Width = 2; tc.Height = 2;
            db.Register(tc);
            var house = Def(BuildingType.House, BuildingCategory.Housing, AgeId.Dawn);
            house.HousingMin = 3; house.HousingMax = 5;
            db.Register(house);
            db.Register(Def(BuildingType.Well, BuildingCategory.Infrastructure, AgeId.Dawn, ServiceType.Water));
            db.Register(Def(BuildingType.Storehouse, BuildingCategory.Storage, AgeId.Dawn));
            db.Register(Def(BuildingType.SpiritTotem, BuildingCategory.Religion, AgeId.Dawn, ServiceType.Faith));
            db.Register(Def(BuildingType.Campfire, BuildingCategory.Culture, AgeId.Dawn, ServiceType.Entertainment));
            db.Register(Def(BuildingType.Farm, BuildingCategory.Food, AgeId.Bronze));
            db.Register(Def(BuildingType.Mine, BuildingCategory.Resource, AgeId.Bronze));
            db.Register(Def(BuildingType.Granary, BuildingCategory.Storage, AgeId.Bronze));
            db.Register(Def(BuildingType.Shrine, BuildingCategory.Religion, AgeId.Bronze, ServiceType.Faith));
            db.Register(Def(BuildingType.Smelter, BuildingCategory.Industry, AgeId.Bronze));
            db.Register(Def(BuildingType.Weaver, BuildingCategory.Industry, AgeId.Bronze));
            db.Register(Def(BuildingType.Herbalist, BuildingCategory.Health, AgeId.Bronze, ServiceType.Health));
            db.Register(Def(BuildingType.Palisade, BuildingCategory.Military, AgeId.Bronze, ServiceType.Defense));
            db.Register(Def(BuildingType.Temple, BuildingCategory.Religion, AgeId.Iron, ServiceType.Faith));
            db.Register(Def(BuildingType.Market, BuildingCategory.Commerce, AgeId.Iron, ServiceType.Commerce));
            db.Register(Def(BuildingType.Barracks, BuildingCategory.Military, AgeId.Iron, ServiceType.Defense));
            db.Register(Def(BuildingType.Scriptorium, BuildingCategory.Education, AgeId.Iron, ServiceType.Research));
            db.Register(Def(BuildingType.Library, BuildingCategory.Education, AgeId.Classical, ServiceType.Research));
            db.Register(Def(BuildingType.Bathhouse, BuildingCategory.Health, AgeId.Classical, ServiceType.Health));
            db.Register(Def(BuildingType.Theater, BuildingCategory.Culture, AgeId.Classical, ServiceType.Entertainment));
            var harbor = Def(BuildingType.Harbor, BuildingCategory.Commerce, AgeId.Bronze, ServiceType.Commerce);
            harbor.Placement = PlacementRule.Coastal;
            db.Register(harbor);
            for (int i = 0; i < Counts.Buildings; i++)
            {
                if (db.Buildings[i] != null) continue;
                var t = (BuildingType)i;
                db.Register(new BuildingDefinition { Type = t, Name = t.ToString(), Category = BuildingCategory.Civic, MinAge = AgeId.Ascension });
            }
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement MakeTown(GameDatabase db, RaceId race, AgeId age, SettlementTier tier, int adults, out SimContext ctx, out Civilization civ, ulong seed = 7)
        {
            ctx = TestWorlds.CreateContext(db, 64, seed);
            var s = TestWorlds.AddSettlement(ctx, race, new Vec2i(32, 32), adults, out civ);
            civ.Age = age;
            s.Tier = tier;
            return s;
        }

        private static void Place(SimContext ctx, Settlement s, BuildingType t, int count = 1)
        {
            var def = ctx.Db.Building(t);
            for (int k = 0; k < count; k++)
            {
                var site = ctx.Ops.FindFreeSite(s.Center, def.Width, def.Height, 30);
                Assert.IsTrue(site.IsValid, "no site for " + t);
                Assert.IsNotNull(ctx.Ops.CreateBuilding(s, t, site, 0, true), "could not place " + t);
            }
        }

        private static float IndustryBias(Settlement s) =>
            AdvisorBoard.BuildBias(s, BuildingType.Smelter) + AdvisorBoard.BuildBias(s, BuildingType.Weaver);

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        // ------------------------------------------------------------------ tests
        [Test]
        public void VillageMissingMarketAndTemple_GetsCoreVotesWithReasons()
        {
            var db = MakeDb();
            var s = MakeTown(db, RaceId.Human, AgeId.Iron, SettlementTier.Village, 45, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Well);
            Place(ctx, s, BuildingType.Storehouse);
            Place(ctx, s, BuildingType.Shrine);
            Place(ctx, s, BuildingType.Granary);
            var adv = new UrbanDesignAdvisor();
            adv.Tick(ctx);

            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Market), Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Temple), Is.GreaterThan(0.3f));
            Assert.IsTrue(LogContains(s, "A Village of 45 needs a Market (civic core)"), string.Join(" | ", s.AiLog));
            Assert.That(AdvisorBoard.Urgency(s, UrbanDesignAdvisor.Id), Is.GreaterThan(0f));
            Assert.That(adv.LastCoreCompleteness, Is.LessThan(1f));
            // Never votes for unavailable buildings (Guildhall/Cathedral stubs are Ascension-only).
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Guildhall));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Cathedral));
            // Inland: the coastal Harbor is never pushed.
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Harbor));
        }

        [Test]
        public void CompleteVillageCore_IsSatisfiedAndDoesNotRequestMarket()
        {
            var db = MakeDb();
            var s = MakeTown(db, RaceId.Human, AgeId.Iron, SettlementTier.Village, 45, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Well);
            Place(ctx, s, BuildingType.Storehouse);
            Place(ctx, s, BuildingType.Granary);
            Place(ctx, s, BuildingType.Temple);
            Place(ctx, s, BuildingType.Market);
            Place(ctx, s, BuildingType.House, 13);
            var adv = new UrbanDesignAdvisor();
            adv.Tick(ctx);
            Assert.AreEqual(1f, adv.LastCoreCompleteness, 1e-4f);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Market), Is.LessThanOrEqualTo(0f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Temple), Is.LessThanOrEqualTo(0f));
        }

        [Test]
        public void OverIndustrializedTown_GetsNegativeIndustryVotes()
        {
            var db = MakeDb();
            var s = MakeTown(db, RaceId.Human, AgeId.Iron, SettlementTier.Town, 60, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Smelter, 6);
            Place(ctx, s, BuildingType.Weaver, 4);
            new UrbanDesignAdvisor().Tick(ctx);
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Smelter), Is.LessThan(-0.2f));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Weaver), Is.LessThan(-0.2f));

            // A balanced town is not discouraged.
            var s2 = MakeTown(db, RaceId.Human, AgeId.Iron, SettlementTier.Town, 60, out var ctx2, out var civ2);
            Place(ctx2, s2, BuildingType.Smelter, 2);
            new UrbanDesignAdvisor().Tick(ctx2);
            Assert.That(AdvisorBoard.BuildBias(s2, BuildingType.Smelter), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void RaceFlavour_ShiftsVotes()
        {
            var db = MakeDb();
            var dwarf = MakeTown(db, RaceId.Dwarf, AgeId.Iron, SettlementTier.Town, 100, out var ctxD, out var civD);
            Place(ctxD, dwarf, BuildingType.Smelter, 2);
            new UrbanDesignAdvisor().Tick(ctxD);
            var elf = MakeTown(db, RaceId.Elf, AgeId.Iron, SettlementTier.Town, 100, out var ctxE, out var civE);
            Place(ctxE, elf, BuildingType.Smelter, 2);
            new UrbanDesignAdvisor().Tick(ctxE);

            Assert.That(IndustryBias(dwarf), Is.GreaterThan(IndustryBias(elf) + 0.2f));
            Assert.That(UrbanRaceFlavor.Multiplier(db, civD, BuildingCategory.Industry), Is.GreaterThan(UrbanRaceFlavor.Multiplier(db, civE, BuildingCategory.Industry)));

            var ctx = TestWorlds.CreateContext(db);
            var orc = ctx.Ops.CreateCivilization(RaceId.Orc);
            var human = ctx.Ops.CreateCivilization(RaceId.Human);
            var saurian = ctx.Ops.CreateCivilization(RaceId.Saurian);
            Assert.That(UrbanRaceFlavor.Multiplier(db, orc, BuildingCategory.Military), Is.GreaterThan(UrbanRaceFlavor.Multiplier(db, human, BuildingCategory.Military)));
            Assert.That(UrbanRaceFlavor.Multiplier(db, saurian, BuildingCategory.Religion), Is.GreaterThan(UrbanRaceFlavor.Multiplier(db, human, BuildingCategory.Religion)));
            Assert.That(UrbanRaceFlavor.Multiplier(db, human, BuildingCategory.Commerce), Is.GreaterThan(UrbanRaceFlavor.Multiplier(db, orc, BuildingCategory.Commerce)));

            // FavoredBuildings (data-driven) raise their category; personality drift shifts it further.
            float before = UrbanRaceFlavor.Multiplier(db, human, BuildingCategory.Culture);
            db.Races[(int)RaceId.Human].FavoredBuildings = new[] { BuildingType.Theater };
            Assert.That(UrbanRaceFlavor.Multiplier(db, human, BuildingCategory.Culture), Is.GreaterThan(before));
            float pious = UrbanRaceFlavor.Multiplier(db, human, BuildingCategory.Religion);
            AdvisorBoard.SetTrait(human, AITrait.Piety, 1f);
            Assert.That(UrbanRaceFlavor.Multiplier(db, human, BuildingCategory.Religion), Is.GreaterThan(pious));
        }

        [Test]
        public void Roles_ResolveToAvailableBuildingsByAgeAndRace()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);

            civ.Age = AgeId.Dawn;
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Shrine, out var t)); Assert.AreEqual(BuildingType.SpiritTotem, t);
            Assert.IsFalse(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Market, out t));
            civ.Age = AgeId.Bronze;
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Shrine, out t)); Assert.AreEqual(BuildingType.Shrine, t);
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Temple, out t)); Assert.AreEqual(BuildingType.Shrine, t);
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Healing, out t)); Assert.AreEqual(BuildingType.Herbalist, t);
            civ.Age = AgeId.Iron;
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Temple, out t)); Assert.AreEqual(BuildingType.Temple, t);
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Market, out t)); Assert.AreEqual(BuildingType.Market, t);
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Learning, out t)); Assert.AreEqual(BuildingType.Scriptorium, t);
            civ.Age = AgeId.Classical;
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Learning, out t)); Assert.AreEqual(BuildingType.Library, t);
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.Healing, out t)); Assert.AreEqual(BuildingType.Bathhouse, t);
            civ.Age = AgeId.Medieval;
            Assert.IsFalse(UrbanRoleResolver.TryResolve(db, civ, UrbanRole.HigherLearning, out t), "no University/Observatory available");

            // Race equivalent: Elves favor the Herbalist over the Bathhouse.
            db.Races[(int)RaceId.Elf].FavoredBuildings = new[] { BuildingType.Herbalist };
            var elf = ctx.Ops.CreateCivilization(RaceId.Elf);
            elf.Age = AgeId.Classical;
            Assert.IsTrue(UrbanRoleResolver.TryResolve(db, elf, UrbanRole.Healing, out t)); Assert.AreEqual(BuildingType.Herbalist, t);

            // A shrine does not satisfy the Temple role once a Temple can be built (upgrade role).
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 10, out var civ2);
            civ2.Age = AgeId.Iron;
            Place(ctx, s, BuildingType.Shrine);
            var comp = new UrbanComposition();
            comp.Survey(ctx.State, db, s);
            var st = UrbanRoleResolver.Evaluate(db, civ2, UrbanRole.Temple, s, comp, null, ctx.State);
            Assert.IsTrue(st.Applicable); Assert.IsFalse(st.Satisfied); Assert.AreEqual(BuildingType.Temple, st.Building);
            Assert.IsTrue(UrbanRoleResolver.Evaluate(db, civ2, UrbanRole.Shrine, s, comp, null, ctx.State).Satisfied);
        }

        [Test]
        public void BuildingLoss_TriggersRecoveryAndBuilderVotes()
        {
            var db = MakeDb();
            var s = MakeTown(db, RaceId.Human, AgeId.Iron, SettlementTier.Village, 40, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Well);
            Place(ctx, s, BuildingType.Storehouse);
            Place(ctx, s, BuildingType.Granary);
            Place(ctx, s, BuildingType.Temple);
            Place(ctx, s, BuildingType.Market);
            Place(ctx, s, BuildingType.House, 10);
            var adv = new UrbanDesignAdvisor();
            adv.Tick(ctx);
            Assert.IsFalse(s.Memory.ContainsKey(UrbanDesignAdvisor.RecoverKey));

            for (int i = s.BuildingIds.Count - 1; i >= 0; i--)
            {
                var b = ctx.State.Buildings.Get(s.BuildingIds[i]);
                if (b != null && (b.Type == BuildingType.Market || b.Type == BuildingType.House || b.Type == BuildingType.Temple))
                    ctx.Ops.DestroyBuilding(b, DestroyCause.Fire);
            }
            ctx.Ops.FlushRemovals();
            ctx.State.Tick += 24;
            adv.Tick(ctx);
            Assert.IsTrue(s.Memory.ContainsKey(UrbanDesignAdvisor.RecoverKey));
            Assert.That(AdvisorBoard.BuildBias(s, BuildingType.Market), Is.GreaterThan(0.7f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Builder), Is.GreaterThan(0f));
            Assert.IsTrue(LogContains(s, "rebuilding"), string.Join(" | ", s.AiLog));
        }

        [Test]
        public void Advisor_IsDeterministicAndFinite()
        {
            var db = MakeDb();
            var a = MakeTown(db, RaceId.Saurian, AgeId.Classical, SettlementTier.Town, 85, out var ctxA, out var civA, 11);
            var b = MakeTown(db, RaceId.Saurian, AgeId.Classical, SettlementTier.Town, 85, out var ctxB, out var civB, 11);
            Place(ctxA, a, BuildingType.Farm, 3); Place(ctxB, b, BuildingType.Farm, 3);
            Place(ctxA, a, BuildingType.Shrine); Place(ctxB, b, BuildingType.Shrine);
            TestWorlds.Run(new UrbanDesignAdvisor(), ctxA, 96);
            TestWorlds.Run(new UrbanDesignAdvisor(), ctxB, 96);

            for (int i = 0; i < Counts.Buildings; i++)
            {
                float va = AdvisorBoard.BuildBias(a, (BuildingType)i), vb = AdvisorBoard.BuildBias(b, (BuildingType)i);
                Assert.AreEqual(va, vb, "bias differs for " + (BuildingType)i);
                Assert.IsFalse(float.IsNaN(va) || float.IsInfinity(va));
            }
            Assert.AreEqual(a.AiLog.Count, b.AiLog.Count);
            for (int i = 0; i < a.AiLog.Count; i++) Assert.AreEqual(a.AiLog[i], b.AiLog[i]);
            float u = AdvisorBoard.Urgency(a, UrbanDesignAdvisor.Id);
            Assert.IsFalse(float.IsNaN(u));
            Assert.AreEqual(u, AdvisorBoard.Urgency(b, UrbanDesignAdvisor.Id));
        }

        [Test]
        public void ArchetypeCatalog_CoversAllTiersCumulatively()
        {
            for (int i = 0; i < Counts.Tiers; i++)
            {
                var a = UrbanArchetypeCatalog.Get((SettlementTier)i);
                Assert.IsNotNull(a);
                Assert.AreEqual(a.CoreRoles.Length, a.RoleTier.Length);
                Assert.AreEqual(25f, a.PerHundred[(int)BuildingCategory.Housing], 1e-4f);
                if (i > 0) Assert.That(a.CoreRoles.Length, Is.GreaterThanOrEqualTo(UrbanArchetypeCatalog.Get((SettlementTier)(i - 1)).CoreRoles.Length));
            }
            var village = UrbanArchetypeCatalog.Get(SettlementTier.Village);
            Assert.Contains(UrbanRole.Market, village.CoreRoles);
            Assert.Contains(UrbanRole.Shrine, village.CoreRoles);
        }
    }
}
