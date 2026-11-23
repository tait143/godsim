using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Espionage
{
    public class EspionageSystemTests
    {
        private const string Bronze = "esp.t.bronze";
        private const string Pottery = "esp.t.pottery";
        private const string Iron = "esp.t.iron";
        private const string Steel = "esp.t.steel";
        private const string Philosophy = "esp.t.philosophy";
        private const string Mystery = "esp.t.mystery";
        private const string Forbidden = "esp.t.forbidden";
        private const string ElvenSong = "esp.t.elvensong";

        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new TechDefinition { Key = Bronze, Name = "Bronze Casting", Age = AgeId.Bronze, Cost = 100f });
            db.Register(new TechDefinition { Key = Pottery, Name = "Pottery", Age = AgeId.Dawn, Cost = 40f });
            db.Register(new TechDefinition { Key = Iron, Name = "Iron Working", Age = AgeId.Iron, Cost = 200f, PrerequisiteKeys = new[] { Bronze }, Effects = new[] { TechEffect.Unlock(BuildingType.Blacksmith), TechEffect.Of(TechEffectKind.MilitaryMultiplier, 0.1f) } });
            db.Register(new TechDefinition { Key = Steel, Name = "Steel", Age = AgeId.Iron, Cost = 300f, PrerequisiteKeys = new[] { Iron } });
            db.Register(new TechDefinition { Key = Philosophy, Name = "Philosophy", Age = AgeId.Classical, Cost = 150f });
            db.Register(new TechDefinition { Key = Mystery, Name = "Mystery", Age = AgeId.Dawn, Cost = 999f });
            db.Register(new TechDefinition { Key = Forbidden, Name = "Forbidden Lore", Age = AgeId.Iron, Cost = 30f, PrerequisiteKeys = new[] { Mystery } });
            db.Register(new TechDefinition { Key = ElvenSong, Name = "Elven Song", Age = AgeId.Dawn, Cost = 20f, AllowedRaces = RaceMask.Elf });
            db.Register(new BuildingDefinition { Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, IsWonder = true, UniquePerCivilization = true, BuildWork = 10f });
            db.Register(new BuildingDefinition { Type = BuildingType.Granary, Name = "Granary", Category = BuildingCategory.Storage, BuildWork = 10f });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        /// <summary>Odds rigged in the spy's favour: no base counter-intel, guaranteed success, reckless.</summary>
        private static EspionageSettings Favourable() => new EspionageSettings { BaseCounterIntel = 0f, MaxSuccessChance = 1f, MinSuccessChance = 1f, RiskAversion = 0f };

        /// <summary>Every attempt fails and is exposed.</summary>
        private static EspionageSettings Doomed() => new EspionageSettings { MaxSuccessChance = 0f, MinSuccessChance = 0f, RiskAversion = 0f, ExposureMin = 1f, ExposureMax = 1f };

        private sealed class World
        {
            public SimContext Ctx;
            public Civilization Thief, Target;
            public Settlement ThiefTown, TargetTown;
            public Relation Rel;
        }

        private static World Build(GameDatabase db, RaceId thiefRace, RaceId targetRace, ulong seed = 7)
        {
            var w = new World { Ctx = TestWorlds.CreateContext(db, 64, seed) };
            w.ThiefTown = TestWorlds.AddSettlement(w.Ctx, thiefRace, new Vec2i(12, 12), 12, out w.Thief);
            w.TargetTown = TestWorlds.AddSettlement(w.Ctx, targetRace, new Vec2i(44, 44), 12, out w.Target);
            w.Rel = w.Ctx.State.Diplomacy.Get(w.Thief.Id, w.Target.Id);
            w.Rel.HasContact = true;
            w.Rel.Stance = DiplomaticStance.Neutral;
            AdvisorBoard.SetTrait(w.Target, AITrait.Xenophobia, 0f);
            return w;
        }

        /// <summary>Saurian (Iron age, knows Bronze) facing a Classical Human civ that knows more.</summary>
        private static World BuildTheftWorld(GameDatabase db, ulong seed = 7)
        {
            var w = Build(db, RaceId.Saurian, RaceId.Human, seed);
            w.Thief.Age = AgeId.Iron;
            w.Thief.KnownTechs.Add(db.TechIdOf(Bronze));
            w.Target.Age = AgeId.Classical;
            w.Target.KnownTechs.Add(db.TechIdOf(Bronze));
            w.Target.KnownTechs.Add(db.TechIdOf(Pottery));
            w.Target.KnownTechs.Add(db.TechIdOf(Iron));
            w.Target.KnownTechs.Add(db.TechIdOf(Philosophy));
            w.Thief.Memory[EspionageKeys.Network] = 100f;
            return w;
        }

        private static Building AddBuilding(World w, BuildingType type, Vec2i at)
        {
            var b = w.Ctx.Ops.CreateBuilding(w.TargetTown, type, at, 0, true);
            Assert.IsNotNull(b, "test building placement failed for " + type);
            return b;
        }

        private static int CountChronicle(WorldState s, string fragment)
        {
            int n = 0;
            foreach (var e in s.Chronicle.Entries) if (e.Text != null && e.Text.Contains(fragment)) n++;
            return n;
        }

        // ------------------------------------------------------------------------------------------------

        [Test]
        public void Identity()
        {
            var sys = new EspionageSystem();
            Assert.AreEqual("AI.Espionage", sys.Name);
            Assert.AreEqual(96, sys.TickInterval);
        }

        [Test]
        public void SelectTech_PrefersCurrentAge_ThenCheapest()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            // Pottery (Dawn, 40) is cheaper, but Iron Working is current-age for the Iron-age thief.
            Assert.AreEqual(db.TechIdOf(Iron), EspionageSystem.SelectTechToSteal(db, w.Thief, w.Target));
            w.Thief.KnownTechs.Add(db.TechIdOf(Iron));
            Assert.AreEqual(db.TechIdOf(Pottery), EspionageSystem.SelectTechToSteal(db, w.Thief, w.Target));
        }

        [Test]
        public void StealTech_FavourableOdds_LearnsWithUnlocksDirtyFlagEventAndChronicle()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            int iron = db.TechIdOf(Iron);
            w.Thief.CurrentResearchTechId = iron;
            var events = new List<TechResearchedEvent>();
            w.Ctx.Events.Subscribe<TechResearchedEvent>(e => events.Add(e));

            TestWorlds.Run(new EspionageSystem(Favourable()), w.Ctx, 96);

            Assert.IsTrue(w.Thief.KnownTechs.Contains(iron), "Iron Working should have been stolen");
            Assert.IsTrue(w.Thief.UnlockedBuildings[(int)BuildingType.Blacksmith], "UnlockBuilding effect applied");
            Assert.AreEqual(1f, w.Thief.Mem("prog.dirty"));
            Assert.AreEqual(-1, w.Thief.CurrentResearchTechId, "research target repicked");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(w.Thief.Id, events[0].CivilizationId);
            Assert.AreEqual(iron, events[0].TechId);
            Assert.AreEqual(1, CountChronicle(w.Ctx.State, "Saurian spies stole the secret of Iron Working from the Humans."));
            foreach (var e in w.Ctx.State.Chronicle.Entries)
                if (e.Text.Contains("stole")) Assert.AreEqual(1, e.Importance);
            Assert.Less(w.Thief.Mem(EspionageKeys.Network), 100f, "network spent");
            Assert.Greater(w.Target.Mem(EspionageKeys.Guard), 0f, "victim raises its guard");
            Assert.AreEqual(0f, w.Rel.Opinion, "an undetected theft causes no diplomatic fallout");
        }

        [Test]
        public void AtMostOneOperationPerRun()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            int before = w.Thief.KnownTechs.Count;
            TestWorlds.Run(new EspionageSystem(Favourable()), w.Ctx, 96);
            Assert.AreEqual(before + 1, w.Thief.KnownTechs.Count);
            Assert.AreEqual(1f, w.Thief.Mem(EspionageKeys.Operations));
        }

        [Test]
        public void KnownOrUnresearchableTechs_AreNeverStolen()
        {
            var db = CreateDb();
            var w = Build(db, RaceId.Saurian, RaceId.Human);
            w.Thief.Age = AgeId.Iron;
            w.Thief.KnownTechs.Add(db.TechIdOf(Bronze));
            w.Target.Age = AgeId.Classical;
            w.Target.KnownTechs.Add(db.TechIdOf(Bronze));       // already known by the thief
            w.Target.KnownTechs.Add(db.TechIdOf(Philosophy));   // later age than the thief
            w.Target.KnownTechs.Add(db.TechIdOf(Forbidden));    // thief lacks the prerequisite (Mystery)
            w.Target.KnownTechs.Add(db.TechIdOf(Steel));        // thief lacks Iron Working
            w.Target.KnownTechs.Add(db.TechIdOf(ElvenSong));    // race-restricted to elves
            w.Thief.Memory[EspionageKeys.Network] = 100f;

            Assert.AreEqual(-1, EspionageSystem.SelectTechToSteal(db, w.Thief, w.Target));
            var sys = new EspionageSystem(Favourable());
            for (int year = 0; year < 10; year++)
            {
                w.Thief.Memory[EspionageKeys.Network] = 100f;
                TestWorlds.Run(sys, w.Ctx, 96, year == 0);
            }
            Assert.AreEqual(1, w.Thief.KnownTechs.Count);
            Assert.AreEqual(0f, w.Thief.Mem("prog.dirty"));
            Assert.AreEqual(0, CountChronicle(w.Ctx.State, "stole"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Sabotage_OnlyWhileHostileOrAtWar_AndNeverHitsWonders(bool atWar)
        {
            var db = CreateDb();
            var w = Build(db, RaceId.Orc, RaceId.Human);
            AdvisorBoard.SetTrait(w.Thief, AITrait.Aggression, 1f);
            var granary = AddBuilding(w, BuildingType.Granary, new Vec2i(40, 40));
            var wonder = AddBuilding(w, BuildingType.GreatLibrary, new Vec2i(48, 40));
            var house = AddBuilding(w, BuildingType.House, new Vec2i(40, 48));
            float food0 = w.TargetTown.Stockpile.Get(ResourceType.Food);
            var sys = new EspionageSystem(Favourable());

            // Neutral: aggressive, network full, odds perfect -> still no sabotage.
            for (int year = 0; year < 3; year++)
            {
                w.Thief.Memory[EspionageKeys.Network] = 100f;
                TestWorlds.Run(sys, w.Ctx, 96, year == 0);
            }
            foreach (int id in w.TargetTown.BuildingIds) Assert.AreEqual(1f, w.Ctx.State.Buildings.Get(id).Condition);
            Assert.AreEqual(food0, w.TargetTown.Stockpile.Get(ResourceType.Food));
            Assert.AreEqual(0f, w.Thief.Mem(EspionageKeys.Sabotages));

            if (atWar) w.Rel.WarId = 1; else w.Rel.Stance = DiplomaticStance.Hostile;
            for (int year = 0; year < 8; year++)
            {
                w.Thief.Memory[EspionageKeys.Network] = 100f;
                TestWorlds.Run(sys, w.Ctx, 96, false);
            }
            Assert.AreEqual(8f, w.Thief.Mem(EspionageKeys.Sabotages), "one sabotage per year");
            bool damaged = false;
            foreach (int id in w.TargetTown.BuildingIds)
            {
                var b = w.Ctx.State.Buildings.Get(id);
                Assert.IsFalse(b.IsDestroyed);
                Assert.GreaterOrEqual(b.Condition, 0.1f - 1e-4f);
                if (b.Condition < 1f) damaged = true;
            }
            Assert.IsTrue(damaged || w.TargetTown.Stockpile.Get(ResourceType.Food) < food0, "sabotage must hurt the target");
            Assert.AreEqual(1f, wonder.Condition, "wonders are never sabotaged");
            Assert.IsNotNull(granary);
            Assert.IsNotNull(house);
            Assert.Greater(CountChronicle(w.Ctx.State, "Orc saboteurs"), 0);
        }

        [Test]
        public void PeacefulCivs_DoNotSabotageEvenWhenHostile()
        {
            var db = CreateDb();
            var w = Build(db, RaceId.Elf, RaceId.Human);
            AdvisorBoard.SetTrait(w.Thief, AITrait.Aggression, 0.1f);
            w.Rel.Stance = DiplomaticStance.Hostile;
            w.Thief.Memory[EspionageKeys.Network] = 100f;
            TestWorlds.Run(new EspionageSystem(Favourable()), w.Ctx, 96 * 3);
            Assert.AreEqual(0f, w.Thief.Mem(EspionageKeys.Sabotages));
        }

        [Test]
        public void Exposure_CostsOpinion_FlagsCasusBelli_AndChronicles()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            w.Rel.Opinion = 10f;
            int before = w.Thief.KnownTechs.Count;

            TestWorlds.Run(new EspionageSystem(Doomed()), w.Ctx, 96);

            Assert.AreEqual(before, w.Thief.KnownTechs.Count, "failed theft learns nothing");
            Assert.LessOrEqual(w.Rel.Opinion, 10f - 20f + 1e-3f);
            Assert.GreaterOrEqual(w.Rel.Opinion, 10f - 40f - 1e-3f);
            Assert.AreEqual(96f, w.Target.Mem(EspionageKeys.ExposedBy(w.Thief.Id), -1f), "casus belli flag on the victim");
            Assert.AreEqual(1f, w.Thief.Mem(EspionageKeys.TimesExposed));
            Assert.Less(w.Thief.Mem(EspionageKeys.Network), 50f, "ring rolled up");
            Assert.GreaterOrEqual(w.Target.Mem(EspionageKeys.Guard), 0.3f - 1e-4f);
            int scandals = 0;
            foreach (var e in w.Ctx.State.Chronicle.Entries)
                if (e.Text.Contains("unmasked Saurian spies")) { scandals++; Assert.AreEqual(1, e.Importance); }
            Assert.AreEqual(1, scandals);
        }

        [Test]
        public void ExposedSpies_LayLowAgainstThatTarget()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            var sys = new EspionageSystem(Doomed());
            TestWorlds.Run(sys, w.Ctx, 96);
            float opinion = w.Rel.Opinion;
            w.Thief.Memory[EspionageKeys.Network] = 100f;
            TestWorlds.Run(sys, w.Ctx, 96, false);
            Assert.AreEqual(opinion, w.Rel.Opinion, "no second scandal inside the lay-low window");
            Assert.AreEqual(1f, w.Thief.Mem(EspionageKeys.Operations));
        }

        [Test]
        public void Allies_NeverSpyOnEachOther()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            w.Rel.Stance = DiplomaticStance.Allied;
            AdvisorBoard.SetTrait(w.Thief, AITrait.Aggression, 1f);
            int before = w.Thief.KnownTechs.Count;
            foreach (var settings in new[] { Favourable(), Doomed() })
            {
                var sys = new EspionageSystem(settings);
                for (int year = 0; year < 5; year++)
                {
                    w.Thief.Memory[EspionageKeys.Network] = 100f;
                    TestWorlds.Run(sys, w.Ctx, 96, year == 0);
                }
            }
            Assert.AreEqual(before, w.Thief.KnownTechs.Count);
            Assert.AreEqual(0f, w.Rel.Opinion);
            Assert.AreEqual(0f, w.Thief.Mem(EspionageKeys.Operations));
            Assert.AreEqual(0f, w.Target.Mem(EspionageKeys.Guard));
        }

        [Test]
        public void NoContact_NoNetworkGrowth_NoOperations()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            w.Rel.HasContact = false;
            w.Thief.Memory[EspionageKeys.Network] = 50f;
            TestWorlds.Run(new EspionageSystem(Favourable()), w.Ctx, 96 * 2);
            Assert.Less(w.Thief.Mem(EspionageKeys.Network), 50f, "idle networks wither");
            Assert.AreEqual(0f, w.Thief.Mem(EspionageKeys.Operations));
        }

        [Test]
        public void Network_GrowsWithCuriosityAndTrade_AndIsBounded()
        {
            var db = CreateDb();
            var a = BuildTheftWorld(db);
            var b = BuildTheftWorld(db);
            a.Thief.Memory[EspionageKeys.Network] = 0f;
            b.Thief.Memory[EspionageKeys.Network] = 0f;
            AdvisorBoard.SetTrait(a.Thief, AITrait.Curiosity, 0f);
            AdvisorBoard.SetTrait(b.Thief, AITrait.Curiosity, 1f);
            b.Rel.TradeAgreement = true;
            var settings = new EspionageSettings { StealCost = 1e9f, SabotageCost = 1e9f };
            TestWorlds.Run(new EspionageSystem(settings), a.Ctx, 96);
            TestWorlds.Run(new EspionageSystem(settings), b.Ctx, 96);
            Assert.Greater(a.Thief.Mem(EspionageKeys.Network), 0f);
            Assert.Greater(b.Thief.Mem(EspionageKeys.Network), a.Thief.Mem(EspionageKeys.Network));
            TestWorlds.Run(new EspionageSystem(settings), b.Ctx, 96 * 200, false);
            Assert.LessOrEqual(b.Thief.Mem(EspionageKeys.Network), settings.MaxNetwork);
        }

        [Test]
        public void CounterIntelligence_RisesWithAdministrationXenophobiaAndGuard()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            var sys = new EspionageSystem();
            float d0 = sys.CounterIntelligence(w.Ctx.State, db, w.Target);
            w.TargetTown.ServiceCoverage[(int)ServiceType.Administration] = 1f;
            float d1 = sys.CounterIntelligence(w.Ctx.State, db, w.Target);
            AdvisorBoard.SetTrait(w.Target, AITrait.Xenophobia, 1f);
            float d2 = sys.CounterIntelligence(w.Ctx.State, db, w.Target);
            w.Target.Memory[EspionageKeys.Guard] = 1f;
            float d3 = sys.CounterIntelligence(w.Ctx.State, db, w.Target);
            Assert.Less(d0, d1);
            Assert.Less(d1, d2);
            Assert.Less(d2, d3);
            Assert.Greater(sys.SuccessChance(100f, d0), sys.SuccessChance(100f, d3));
        }

        [Test]
        public void GarbageMemory_IsSanitized()
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db);
            w.Thief.Memory[EspionageKeys.Network] = float.NaN;
            w.Target.Memory[EspionageKeys.Guard] = float.PositiveInfinity;
            w.Target.Memory[EspionageKeys.ExposedBy(w.Thief.Id)] = float.NaN;
            TestWorlds.Run(new EspionageSystem(), w.Ctx, 96 * 5);
            foreach (var civ in new[] { w.Thief, w.Target })
                foreach (var kv in civ.Memory)
                    if (kv.Key.StartsWith("esp.network") || kv.Key.StartsWith("esp.guard"))
                        Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
            Assert.IsFalse(float.IsNaN(w.Rel.Opinion));
        }

        [Test]
        public void Deterministic_SameSeedSameHistory()
        {
            Assert.AreEqual(Simulate(42), Simulate(42));
        }

        private static string Simulate(ulong seed)
        {
            var db = CreateDb();
            var w = BuildTheftWorld(db, seed);
            AdvisorBoard.SetTrait(w.Thief, AITrait.Aggression, 0.9f);
            w.Rel.Stance = DiplomaticStance.Hostile;
            AddBuilding(w, BuildingType.Granary, new Vec2i(40, 40));
            AddBuilding(w, BuildingType.House, new Vec2i(40, 48));
            // a third, xenophobic civ the others can spy on
            var third = TestWorlds.AddSettlement(w.Ctx, RaceId.Elf, new Vec2i(12, 48), 9, out var elf);
            elf.Age = AgeId.Classical;
            elf.KnownTechs.Add(db.TechIdOf(Bronze));
            elf.KnownTechs.Add(db.TechIdOf(Mystery));
            elf.KnownTechs.Add(db.TechIdOf(Forbidden));
            var r2 = w.Ctx.State.Diplomacy.Get(w.Thief.Id, elf.Id);
            r2.HasContact = true; r2.TradeAgreement = true;
            var r3 = w.Ctx.State.Diplomacy.Get(w.Target.Id, elf.Id);
            r3.HasContact = true; r3.Stance = DiplomaticStance.Friendly;
            Assert.IsNotNull(third);

            TestWorlds.Run(new EspionageSystem(), w.Ctx, 96 * 40);
            Assert.Greater(CountChronicle(w.Ctx.State, "spies") + CountChronicle(w.Ctx.State, "saboteurs"), 0, "default tuning should produce intrigue");

            var sb = new StringBuilder();
            foreach (var civ in w.Ctx.State.Civilizations.Items)
            {
                sb.Append(civ.Id).Append(':');
                foreach (int t in civ.KnownTechs) sb.Append(t).Append(',');
                sb.Append('|').Append(civ.Mem(EspionageKeys.Network).ToString("R")).Append('|').Append(civ.Mem(EspionageKeys.Guard).ToString("R")).Append(';');
            }
            foreach (var r in w.Ctx.State.Diplomacy.AllRelations) sb.Append(r.Opinion.ToString("R")).Append(';');
            foreach (var e in w.Ctx.State.Chronicle.Entries) sb.Append(e.Tick).Append(e.Text).Append('\n');
            sb.Append(w.Ctx.State.Rng.State);
            return sb.ToString();
        }
    }
}
