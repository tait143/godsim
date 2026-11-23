using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Governance
{
    public class GovernanceSystemTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = TestDb.CreateStub(); }

        private SimContext Ctx(ulong seed = 1) => TestWorlds.CreateContext(_db, 64, seed);

        private static void Year(GovernanceSystem sys, SimContext ctx, bool init = false) => TestWorlds.Run(sys, ctx, 96, init);

        private static void SetGovernment(Civilization civ, GovernmentType t, float stability)
        {
            civ.Memory[GovernanceKeys.Type] = (int)t;
            civ.Memory[GovernanceKeys.Since] = 0f;
            civ.Memory[GovernanceKeys.Stability] = stability;
            civ.Memory[GovernanceKeys.Tax] = 0.15f;
            civ.Memory[GovernanceKeys.Conscription] = 0.05f;
            civ.Memory[GovernanceKeys.FaithPolicy] = 1f;
            civ.Memory[GovernanceKeys.SeenAge] = (int)civ.Age;
        }

        private static int CountChronicle(SimContext ctx, string contains, int importance)
        {
            int n = 0;
            var e = ctx.State.Chronicle.Entries;
            for (int i = 0; i < e.Count; i++) if (e[i].Importance == importance && e[i].Text.Contains(contains)) n++;
            return n;
        }

        [Test]
        public void NewCivilization_StartsAsTribalCouncil()
        {
            var ctx = Ctx();
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 10, out var civ);
            var sys = new GovernanceSystem();
            Assert.AreEqual("gov", sys.AdvisorId);
            Assert.AreEqual(96, sys.TickInterval);
            sys.Initialize(ctx);
            Assert.AreEqual(GovernmentType.TribalCouncil, GovernanceQueries.Government(civ));
            Assert.IsTrue(civ.Memory.ContainsKey("gov.type") && civ.Memory.ContainsKey("gov.since") && civ.Memory.ContainsKey("gov.stability"));
            Assert.IsTrue(civ.Memory.ContainsKey("gov.tax") && civ.Memory.ContainsKey("gov.conscription") && civ.Memory.ContainsKey("gov.faithPolicy"));
            float st = civ.Mem("gov.stability");
            Assert.IsTrue(st >= 0f && st <= 1f);
            string d = GovernanceQueries.Describe(civ);
            StringAssert.Contains("Tribal Council", d);
            StringAssert.Contains("tax", d);
        }

        [Test]
        public void AgeAdvances_EvolveTheGovernment()
        {
            var ctx = Ctx();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 12, out var civ);
            var sys = new GovernanceSystem();
            Year(sys, ctx, true);
            Assert.AreEqual(GovernmentType.TribalCouncil, GovernanceQueries.Government(civ));

            var expected = new[]
            {
                new KeyValuePair<AgeId, GovernmentType>(AgeId.Bronze, GovernmentType.Chiefdom),
                new KeyValuePair<AgeId, GovernmentType>(AgeId.Iron, GovernmentType.Monarchy),
                new KeyValuePair<AgeId, GovernmentType>(AgeId.Medieval, GovernmentType.FeudalRealm),
                new KeyValuePair<AgeId, GovernmentType>(AgeId.Industrial, GovernmentType.ConstitutionalState),
                new KeyValuePair<AgeId, GovernmentType>(AgeId.Ascension, GovernmentType.AscendantCommonwealth),
            };
            foreach (var kv in expected)
            {
                civ.Age = kv.Key;
                s.Happiness = 0.6f;
                Year(sys, ctx);
                Assert.AreEqual(kv.Value, GovernanceQueries.Government(civ), "age " + kv.Key);
                Assert.IsTrue(GovernmentCatalog.Get(kv.Value).IsEligible(kv.Key));
            }
            Assert.GreaterOrEqual(CountChronicle(ctx, " became the ", 1), 5);
            Assert.AreEqual(0, GovernanceQueries.Revolutions(civ));
        }

        [Test]
        public void Piety_LeadsToTheocracy_AndBiasesFaithAdvisor()
        {
            var ctx = Ctx();
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 12, out var civ);
            AdvisorBoard.SetTrait(civ, AITrait.Piety, 0.95f);
            var sys = new GovernanceSystem();
            Year(sys, ctx, true);
            civ.Age = AgeId.Iron;
            Year(sys, ctx);
            Assert.AreEqual(GovernmentType.Theocracy, GovernanceQueries.Government(civ));
            Assert.AreEqual(1.2f, AdvisorBoard.Weight(civ, "faith"), 1e-3f);
            Assert.AreEqual(FaithPolicy.Zealotry, GovernanceQueries.Faith(civ));
        }

        [Test]
        public void Mercantilism_LeadsToMerchantRepublic_AndBiasesTreasury()
        {
            var ctx = Ctx();
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 12, out var civ);
            AdvisorBoard.SetTrait(civ, AITrait.Mercantilism, 0.95f);
            var sys = new GovernanceSystem();
            Year(sys, ctx, true);
            civ.Age = AgeId.Classical;
            Year(sys, ctx);
            Assert.AreEqual(GovernmentType.MerchantRepublic, GovernanceQueries.Government(civ));
            Assert.AreEqual(1.2f, AdvisorBoard.Weight(civ, "treasury"), 1e-3f);
        }

        [Test]
        public void AggressionWithManySettlements_LeadsToEmpire_ButNotWithOne()
        {
            var ctxA = Ctx();
            TestWorlds.AddSettlement(ctxA, RaceId.Human, new Vec2i(10, 10), 8, out var big);
            for (int i = 0; i < 5; i++) ctxA.Ops.FoundSettlement(big, new Vec2i(20 + i * 8, 40));
            AdvisorBoard.SetTrait(big, AITrait.Aggression, 0.9f);

            var ctxB = Ctx();
            TestWorlds.AddSettlement(ctxB, RaceId.Human, new Vec2i(10, 10), 8, out var small);
            AdvisorBoard.SetTrait(small, AITrait.Aggression, 0.9f);

            var sysA = new GovernanceSystem();
            var sysB = new GovernanceSystem();
            Year(sysA, ctxA, true); Year(sysB, ctxB, true);
            big.Age = AgeId.Classical; small.Age = AgeId.Classical;
            Year(sysA, ctxA); Year(sysB, ctxB);
            Assert.AreEqual(GovernmentType.Empire, GovernanceQueries.Government(big));
            Assert.AreNotEqual(GovernmentType.Empire, GovernanceQueries.Government(small));
        }

        [Test]
        public void CuriosityInLateAge_LeadsToTechnocracy()
        {
            var ctxA = Ctx();
            TestWorlds.AddSettlement(ctxA, RaceId.Human, new Vec2i(20, 20), 12, out var curious);
            AdvisorBoard.SetTrait(curious, AITrait.Curiosity, 0.95f);
            var ctxB = Ctx();
            TestWorlds.AddSettlement(ctxB, RaceId.Human, new Vec2i(20, 20), 12, out var plain);
            var sysA = new GovernanceSystem();
            var sysB = new GovernanceSystem();
            Year(sysA, ctxA, true); Year(sysB, ctxB, true);
            curious.Age = AgeId.Industrial; plain.Age = AgeId.Industrial;
            Year(sysA, ctxA); Year(sysB, ctxB);
            Assert.AreEqual(GovernmentType.Technocracy, GovernanceQueries.Government(curious));
            Assert.AreEqual(GovernmentType.ConstitutionalState, GovernanceQueries.Government(plain));
            Assert.AreEqual(1.2f, AdvisorBoard.Weight(curious, "science"), 1e-3f);
        }

        [Test]
        public void LowStability_CausesRevolution_WithChronicle()
        {
            var ctx = Ctx();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 12, out var civ);
            s.Name = "Aldmere";
            civ.CapitalId = s.Id;
            civ.Age = AgeId.Iron;
            AdvisorBoard.SetTrait(civ, AITrait.Mercantilism, 0.95f);
            SetGovernment(civ, GovernmentType.Monarchy, 0.02f);
            s.Unrest = 1f;
            s.Happiness = 0.1f;
            var sys = new GovernanceSystem();
            Year(sys, ctx, true);

            Assert.AreEqual(GovernmentType.MerchantRepublic, GovernanceQueries.Government(civ));
            Assert.AreEqual(1, GovernanceQueries.Revolutions(civ));
            Assert.AreEqual(1, CountChronicle(ctx, "The Merchant Guilds overthrew the King of Aldmere; the Republic of Aldmere was proclaimed.", 2));
            Assert.IsTrue(GovernanceQueries.Stability(civ) > 0.3f, "new regime gets a fragile fresh start");
            Assert.AreEqual(96L, GovernanceQueries.Since(civ));
        }

        [Test]
        public void LostWar_ShakesStability()
        {
            var ctxA = Ctx();
            TestWorlds.AddSettlement(ctxA, RaceId.Human, new Vec2i(20, 20), 12, out var loser);
            TestWorlds.AddSettlement(ctxA, RaceId.Orc, new Vec2i(50, 50), 12, out var winner);
            var ctxB = Ctx();
            TestWorlds.AddSettlement(ctxB, RaceId.Human, new Vec2i(20, 20), 12, out var calm);
            var sysA = new GovernanceSystem();
            var sysB = new GovernanceSystem();
            Year(sysA, ctxA, true); Year(sysB, ctxB, true);
            ctxA.State.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = loser.Id, DefenderId = winner.Id, IsActive = false, StartTick = 100, EndTick = 180, WarScore = -70f });
            Year(sysA, ctxA); Year(sysB, ctxB);
            Assert.Less(GovernanceQueries.Stability(loser), GovernanceQueries.Stability(calm) - 0.1f);
        }

        [Test]
        public void Taxes_RaiseGold_AndLowerHappiness_Bounded()
        {
            var ctx = Ctx();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 20, out var civ);
            var sys = new GovernanceSystem();
            sys.Initialize(ctx);
            civ.Memory[GovernanceKeys.Tax] = 0.25f;
            s.Happiness = 0.6f;
            float gold0 = s.Stockpile.Get(ResourceType.Gold);
            Year(sys, ctx);
            float gained = s.Stockpile.Get(ResourceType.Gold) - gold0;
            Assert.Greater(gained, 0f);
            Assert.LessOrEqual(gained, GovernanceModel.MaxGoldPerRun);
            Assert.Less(s.Happiness, 0.6f);
            Assert.GreaterOrEqual(s.Happiness, 0.6f - GovernanceModel.MaxHappinessCostPerRun - 1e-5f);

            // Model: monotonic in tax, bounded even for absurd inputs, never NaN.
            var def = GovernmentCatalog.Get(GovernmentType.Monarchy);
            Assert.Greater(GovernanceModel.TaxGold(def, 20, 0.4f, 96, 1f), GovernanceModel.TaxGold(def, 20, 0.1f, 96, 1f));
            Assert.LessOrEqual(GovernanceModel.TaxGold(def, 1000000, 1f, 100000, float.NaN), GovernanceModel.MaxGoldPerRun);
            Assert.LessOrEqual(GovernanceModel.TaxHappinessCost(def, 1f, 100000), GovernanceModel.MaxHappinessCostPerRun);
            Assert.AreEqual(0f, GovernanceModel.TaxHappinessCost(def, float.NaN, 96));
        }

        [Test]
        public void TaxPolicy_RisesWhenBroke_FallsWithUnrest()
        {
            var ctxA = Ctx();
            var poor = TestWorlds.AddSettlement(ctxA, RaceId.Human, new Vec2i(20, 20), 20, out var poorCiv);
            var ctxB = Ctx();
            var angry = TestWorlds.AddSettlement(ctxB, RaceId.Human, new Vec2i(20, 20), 20, out var angryCiv);
            var sysA = new GovernanceSystem();
            var sysB = new GovernanceSystem();
            sysA.Initialize(ctxA); sysB.Initialize(ctxB);
            float t0 = GovernanceQueries.Tax(poorCiv);
            angry.Unrest = 0.8f;
            angry.Stockpile.SetRaw(ResourceType.Gold, 140f);
            Year(sysA, ctxA); Year(sysB, ctxB);
            Assert.Greater(GovernanceQueries.Tax(poorCiv), t0);
            Assert.Less(GovernanceQueries.Tax(angryCiv), t0);
            Assert.Less(GovernanceQueries.Tax(angryCiv), GovernanceQueries.Tax(poorCiv));
            Assert.IsNotNull(poor);
        }

        [Test]
        public void War_RaisesConscription_AndVotesSoldiers()
        {
            var ctx = Ctx();
            var sa = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(15, 15), 12, out var a);
            var sb = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(50, 50), 12, out var b);
            var sc = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(15, 50), 12, out var c);
            ctx.State.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = b.Id, DefenderId = a.Id, IsActive = true });
            var sys = new GovernanceSystem();
            Year(sys, ctx, true);
            Year(sys, ctx);
            Assert.Greater(GovernanceQueries.Conscription(a), GovernanceQueries.Conscription(c) + 0.1f);
            Assert.Greater(AdvisorBoard.JobBias(sa, JobType.Soldier), 0f);
            Assert.Greater(AdvisorBoard.JobBias(sb, JobType.Soldier), 0f);
            Assert.AreEqual(0f, AdvisorBoard.JobBias(sc, JobType.Soldier));
            Assert.Greater(AdvisorBoard.BuildBias(sa, BuildingType.MilitaryBase), 0f);
            Assert.AreEqual(1f, a.Mem(GovernanceKeys.AtWar));
        }

        [Test]
        public void FaithPolicy_VotesFaithBuildings()
        {
            var ctxA = Ctx();
            var pious = TestWorlds.AddSettlement(ctxA, RaceId.Human, new Vec2i(20, 20), 12, out var pCiv);
            SetGovernment(pCiv, GovernmentType.Theocracy, 0.8f);
            pCiv.Age = AgeId.Iron;
            pCiv.Memory[GovernanceKeys.SeenAge] = (int)AgeId.Iron;
            AdvisorBoard.SetTrait(pCiv, AITrait.Piety, 0.9f);

            var ctxB = Ctx();
            var secular = TestWorlds.AddSettlement(ctxB, RaceId.Human, new Vec2i(20, 20), 12, out var sCiv);
            sCiv.Age = AgeId.Information;
            SetGovernment(sCiv, GovernmentType.Technocracy, 0.8f);
            AdvisorBoard.SetTrait(sCiv, AITrait.Piety, 0.5f);

            var sysA = new GovernanceSystem();
            var sysB = new GovernanceSystem();
            Year(sysA, ctxA, true); Year(sysB, ctxB, true);
            Assert.AreEqual(FaithPolicy.Zealotry, GovernanceQueries.Faith(pCiv));
            Assert.Greater(AdvisorBoard.BuildBias(pious, BuildingType.Cathedral), 0.3f);
            Assert.Greater(AdvisorBoard.JobBias(pious, JobType.Priest), 0f);
            Assert.AreEqual(FaithPolicy.Secular, GovernanceQueries.Faith(sCiv));
            Assert.Less(AdvisorBoard.BuildBias(secular, BuildingType.Cathedral), 0f);
        }

        [Test]
        public void CorruptMemory_IsSanitized()
        {
            var ctx = Ctx();
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 12, out var civ);
            var sys = new GovernanceSystem();
            sys.Initialize(ctx);
            civ.Memory[GovernanceKeys.Stability] = float.NaN;
            civ.Memory[GovernanceKeys.Tax] = float.PositiveInfinity;
            civ.Memory[GovernanceKeys.Type] = 999f;
            s.Happiness = float.NaN;
            Year(sys, ctx);
            foreach (var kv in civ.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            Assert.IsFalse(float.IsNaN(s.Happiness));
            float st = GovernanceQueries.Stability(civ);
            Assert.IsTrue(st >= 0f && st <= 1f);
        }

        private static string RunScenario(ulong seed)
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, 64, seed);
            var s1 = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(15, 15), 14, out var a);
            var s2 = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(48, 48), 14, out var b);
            AdvisorBoard.SetTrait(b, AITrait.Aggression, 0.9f);
            AdvisorBoard.SetTrait(a, AITrait.Mercantilism, 0.8f);
            var sys = new GovernanceSystem();
            Year(sys, ctx, true);
            for (int year = 0; year < 40; year++)
            {
                if (year % 4 == 0 && a.Age < AgeId.Ascension) a.Age++;
                if (year % 5 == 0 && b.Age < AgeId.Ascension) b.Age++;
                s1.Unrest = (year % 7) / 7f; // waves of unrest feed the revolution lottery
                s2.Unrest = (year % 3) / 3f;
                s2.Happiness = 0.3f;
                if (year == 10) ctx.State.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = b.Id, DefenderId = a.Id, IsActive = true, WarScore = 40f });
                Year(sys, ctx);
            }
            var sb = new System.Text.StringBuilder();
            foreach (var e in ctx.State.Chronicle.Entries) sb.Append(e.Tick).Append(':').Append(e.Text).Append('\n');
            sb.Append(GovernanceQueries.Describe(a)).Append('|').Append(GovernanceQueries.Describe(b));
            sb.Append('|').Append(a.Mem(GovernanceKeys.Stability).ToString("R")).Append('|').Append(b.Mem(GovernanceKeys.Tax).ToString("R"));
            sb.Append('|').Append(s1.Stockpile.Get(ResourceType.Gold).ToString("R"));
            return sb.ToString();
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            string x = RunScenario(42);
            string y = RunScenario(42);
            Assert.AreEqual(x, y);
            StringAssert.Contains(" became the ", x);
        }

        [Test]
        public void Catalog_IsComplete_AndEveryAgeHasAGovernment()
        {
            Assert.AreEqual(10, GovernmentCatalog.All.Length);
            for (int i = 0; i < GovernmentCatalog.Count; i++)
            {
                var d = GovernmentCatalog.All[i];
                Assert.AreEqual((GovernmentType)i, d.Type);
                Assert.IsFalse(string.IsNullOrEmpty(d.Name) || string.IsNullOrEmpty(d.StateName) || string.IsNullOrEmpty(d.RulerTitle) || string.IsNullOrEmpty(d.Faction));
                Assert.AreEqual(Counts.Races, d.RaceAffinity.Length);
                Assert.LessOrEqual(d.MinAge, d.MaxAge);
            }
            foreach (var age in EnumUtil.AllAges)
            {
                int eligible = 0;
                foreach (var d in GovernmentCatalog.All) if (d.IsEligible(age)) eligible++;
                Assert.GreaterOrEqual(eligible, 2, age.ToString());
            }
        }
    }
}
