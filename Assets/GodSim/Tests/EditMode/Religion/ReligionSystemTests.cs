using System;
using GodSim.Core;
using GodSim.Core.Religion;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Religion
{
    public class ReligionSystemTests
    {
        private static SimContext NewWorld(out Settlement capital, out Civilization civ, RaceId race = RaceId.Human, GameDatabase db = null, ulong seed = 1)
        {
            var ctx = TestWorlds.CreateContext(db ?? TestDb.CreateStub(), 64, seed);
            capital = TestWorlds.AddSettlement(ctx, race, new Vec2i(16, 16), 6, out civ);
            return ctx;
        }

        // ------------------------------------------------------------------------------------------ founding

        [Test]
        public void System_HasContractShape()
        {
            var sys = new ReligionSystem();
            Assert.AreEqual(48, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
            int n = 0;
            foreach (var s in ReligionModule.CreateSystems()) { Assert.IsTrue(s is ReligionSystem); n++; }
            Assert.AreEqual(1, n);
        }

        [Test]
        public void Faith_IsFounded_WhenAShrineIsCompleted()
        {
            var ctx = NewWorld(out var s, out var civ);
            civ.Devotion = 0.5f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 2);
            Assert.IsFalse(ReligionQueries.HasFaith(civ), "no faith without a shrine or high devotion");

            ReligionTestKit.AddBuilding(ctx, s, BuildingType.Shrine, complete: false);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsFalse(ReligionQueries.HasFaith(civ), "an unfinished shrine does not found a faith");

            ReligionTestKit.AddBuilding(ctx, s, BuildingType.Shrine);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsTrue(ReligionQueries.HasFaith(civ));
            Assert.AreEqual(4, new System.Collections.Generic.List<DoctrineDefinition>(ReligionQueries.Doctrines(civ)).Count);
            Assert.AreEqual(s.Id, ReligionQueries.HolySite(civ), "the shrine's settlement becomes the holy site");
            string name = ReligionQueries.FaithName(ctx.State, ctx.Db, civ);
            Assert.IsFalse(string.IsNullOrEmpty(name));
            var entry = ReligionTestKit.FindChronicle(ctx.State, "proclaimed " + name);
            Assert.IsTrue(entry.HasValue, "founding is chronicled");
            Assert.AreEqual(2, entry.Value.Importance);
            Assert.AreEqual(ChronicleCategory.Divine, entry.Value.Category);
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "consecrated"));
        }

        [TestCase(BuildingType.SpiritTotem)]
        [TestCase(BuildingType.Temple)]
        public void Faith_IsFounded_ForOtherReligiousBuildings(BuildingType type)
        {
            var ctx = NewWorld(out var s, out var civ);
            ReligionTestKit.AddBuilding(ctx, s, type);
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsTrue(ReligionQueries.HasFaith(civ));
        }

        [Test]
        public void Faith_IsFounded_WhenDevotionIsHigh_AndOnlyOnce()
        {
            var ctx = NewWorld(out _, out var civ);
            civ.Devotion = 0.61f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 6);
            Assert.IsTrue(ReligionQueries.HasFaith(civ));
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "proclaimed"));
            Assert.AreEqual(-1, ReligionQueries.HolySite(civ), "no sacred tile and no shrine: no holy site yet");
        }

        [Test]
        public void DoctrineSelection_IsDeterministicAcrossIdenticalWorlds()
        {
            DoctrineId[] Found(ulong seed)
            {
                var ctx = NewWorld(out _, out var civ, RaceId.Elf, null, seed);
                civ.Devotion = 0.9f;
                var sys = new ReligionSystem();
                sys.Initialize(ctx);
                ReligionTestKit.Runs(sys, ctx, 1);
                var ids = new DoctrineId[4];
                for (int a = 0; a < 4; a++) ids[a] = ReligionTestKit.Held(civ, (DoctrineAxis)a);
                return ids;
            }
            var x = Found(77UL);
            var y = Found(77UL);
            for (int a = 0; a < 4; a++) Assert.AreEqual(x[a], y[a]);
        }

        [Test]
        public void DoctrineSelection_FollowsRacePersonality_InTheSystem()
        {
            var db = ReligionTestKit.Db(
                ReligionTestKit.Race(RaceId.Orc, 0.3f, 1f, 0.1f, 0.1f),
                ReligionTestKit.Race(RaceId.Elf, 0.3f, 0.2f, 1f, 0.2f));
            var ctx = TestWorlds.CreateContext(db, 64, 5);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(12, 12), 4, out var orc);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(48, 48), 4, out var elf);
            orc.Devotion = 0.9f; elf.Devotion = 0.9f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(DoctrineId.MartialZeal, ReligionTestKit.Held(orc, DoctrineAxis.Ethos));
            Assert.AreEqual(DoctrineId.Monasticism, ReligionTestKit.Held(elf, DoctrineAxis.Practice));
        }

        [Test]
        public void WrathfulGod_BreedsWrathfulJudgeFaiths_KindGod_LovingParent()
        {
            var ctx = NewWorld(out _, out var civ);
            ctx.State.God.Alignment = -0.8f;
            civ.Devotion = 0.9f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(DoctrineId.WrathfulJudge, ReligionTestKit.Held(civ, DoctrineAxis.View));

            var ctx2 = NewWorld(out _, out var civ2);
            ctx2.State.God.Alignment = 0.8f;
            civ2.Devotion = 0.9f;
            var sys2 = new ReligionSystem();
            sys2.Initialize(ctx2);
            ReligionTestKit.Runs(sys2, ctx2, 1);
            Assert.AreEqual(DoctrineId.LovingParent, ReligionTestKit.Held(civ2, DoctrineAxis.View));
        }

        // ------------------------------------------------------------------------------------------ holy site

        [Test]
        public void HolySite_IsTheSettlementOwningASacredTile()
        {
            var ctx = NewWorld(out var capital, out var civ);
            var map = ctx.State.Map;
            var second = ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            ReligionTestKit.AddBuilding(ctx, capital, BuildingType.Temple); // a temple elsewhere must not beat sacred ground
            int tile = map.Index(46, 44);
            Assert.AreEqual(second.Id, map.Owner[tile]);
            map.Set(tile, TileFlags.Sacred);

            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsTrue(ReligionQueries.HasFaith(civ));
            Assert.AreEqual(second.Id, ReligionQueries.HolySite(civ));
            Assert.IsTrue(ReligionQueries.IsHolySite(ctx.State, second));
            Assert.IsFalse(ReligionQueries.IsHolySite(ctx.State, capital));
            Assert.AreEqual(1f, civ.Mem(ReligionKeys.HolySiteSacred));
            var e = ReligionTestKit.FindChronicle(ctx.State, "consecrated " + second.Name);
            Assert.IsTrue(e.HasValue);
            Assert.AreEqual(2, e.Value.Importance);

            // The holy site is blessed above the rest; pilgrims cheer the others.
            capital.Faith = second.Faith = 0.5f;
            capital.Happiness = second.Happiness = 0.5f;
            ReligionTestKit.Runs(sys, ctx, 5);
            Assert.IsTrue(second.Faith > capital.Faith, "holy site gains extra faith");
            Assert.AreEqual(second.Id, ReligionQueries.HolySite(civ), "holy site is stable");
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "consecrated"));
        }

        [Test]
        public void HolySite_MovesToSacredGround_WhenDiscoveredLater()
        {
            var ctx = NewWorld(out var capital, out var civ);
            var second = ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            ReligionTestKit.AddBuilding(ctx, capital, BuildingType.Shrine);
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(capital.Id, ReligionQueries.HolySite(civ));
            ctx.State.Map.Set(ctx.State.Map.Index(second.Center), TileFlags.Sacred);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(second.Id, ReligionQueries.HolySite(civ));
        }

        // ------------------------------------------------------------------------------------------ heresy, schism, reformation

        [Test]
        public void Heresy_RisesUnderBadFavor_CausesSchismWithUnrest_ThenReformation()
        {
            var ctx = NewWorld(out var capital, out var civ);
            var second = ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            civ.Devotion = 0.9f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsTrue(ReligionQueries.HasFaith(civ));
            Assert.AreEqual(0f, ReligionQueries.Heresy(civ));

            capital.Unrest = 0f; second.Unrest = 0f;
            ReligionTestKit.Runs(sys, ctx, 1, () => { civ.GodFavor = -0.9f; civ.Devotion = 0.1f; });
            float h1 = ReligionQueries.Heresy(civ);
            Assert.IsTrue(h1 > 0f, "heresy rises when the god is cruel and devotion is low");

            int guard = 0;
            while (civ.Mem(ReligionKeys.SchismCount) < 1f && guard++ < 40)
                ReligionTestKit.Runs(sys, ctx, 1, () => { civ.GodFavor = -0.9f; civ.Devotion = 0.1f; });
            Assert.AreEqual(1f, civ.Mem(ReligionKeys.SchismCount), "a schism broke out");
            Assert.IsTrue(second.Unrest >= 0.39f, "the non-capital settlement erupts: " + second.Unrest);
            Assert.IsTrue(capital.Unrest < 0.1f, "the capital is spared: " + capital.Unrest);
            Assert.IsTrue(ReligionQueries.Heresy(civ) <= ReligionTuning.SchismThreshold);
            var e = ReligionTestKit.FindChronicle(ctx.State, "Schism!");
            Assert.IsTrue(e.HasValue);
            Assert.AreEqual(second.Id, e.Value.SettlementId);
            Assert.IsTrue(civ.Memory.ContainsKey(ReligionKeys.ReformationDueTick));

            var before = new DoctrineId[4];
            for (int a = 0; a < 4; a++) before[a] = ReligionTestKit.Held(civ, (DoctrineAxis)a);
            ReligionTestKit.Runs(sys, ctx, 5, () => { civ.GodFavor = 0f; civ.Devotion = 0.6f; });
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "Reformation"));
            Assert.AreEqual(1f, civ.Mem(ReligionKeys.ReformationCount));
            Assert.IsFalse(civ.Memory.ContainsKey(ReligionKeys.ReformationDueTick));
            int changed = 0;
            for (int a = 0; a < 4; a++) if (ReligionTestKit.Held(civ, (DoctrineAxis)a) != before[a]) changed++;
            Assert.AreEqual(1, changed, "the reformation replaces exactly one doctrine");
        }

        [Test]
        public void Heresy_RisesWhenGodContradictsTheView_AndHealsOtherwise()
        {
            var ctx = NewWorld(out _, out var civ);
            ReligionTestKit.SetFaith(civ, DoctrineId.SunWorship, DoctrineId.Mercy, DoctrineId.Festivals, DoctrineId.LovingParent);
            ctx.State.God.Alignment = -1f; // a wrathful god betrays the Loving Parent faith
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 3, () => { civ.GodFavor = 0f; civ.Devotion = 0.8f; });
            float h = ReligionQueries.Heresy(civ);
            Assert.IsTrue(h > 0f);

            ctx.State.God.Alignment = 1f;
            ReligionTestKit.Runs(sys, ctx, 3, () => { civ.GodFavor = 0.5f; civ.Devotion = 0.8f; });
            Assert.IsTrue(ReligionQueries.Heresy(civ) < h, "heresy heals under a kind god");
        }

        // ------------------------------------------------------------------------------------------ persistence

        [Test]
        public void MemoryKeys_Persist_AndANewSystemInstanceResumes()
        {
            var ctx = NewWorld(out var capital, out var civ);
            ReligionTestKit.AddBuilding(ctx, capital, BuildingType.Shrine);
            var a = new ReligionSystem();
            a.Initialize(ctx);
            ReligionTestKit.Runs(a, ctx, 3);
            Assert.IsTrue(ReligionQueries.HasFaith(civ));
            var before = new DoctrineId[4];
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(civ.Memory.ContainsKey(DoctrineCatalog.MemoryKey((DoctrineAxis)i)));
                before[i] = ReligionTestKit.Held(civ, (DoctrineAxis)i);
            }
            Assert.IsTrue(civ.Memory.ContainsKey(ReligionKeys.Founded));
            Assert.IsTrue(civ.Memory.ContainsKey(ReligionKeys.HolySite));
            Assert.IsTrue(civ.Memory.ContainsKey(ReligionKeys.Heresy));
            civ.Memory[ReligionKeys.Heresy] = 0.3f;
            string name = ReligionQueries.FaithName(ctx.State, ctx.Db, civ);
            int holy = ReligionQueries.HolySite(civ);

            var b = new ReligionSystem();
            b.Initialize(ctx);
            ReligionTestKit.Runs(b, ctx, 3);
            for (int i = 0; i < 4; i++) Assert.AreEqual(before[i], ReligionTestKit.Held(civ, (DoctrineAxis)i));
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "proclaimed"), "the new instance does not re-found");
            Assert.AreEqual(name, ReligionQueries.FaithName(ctx.State, ctx.Db, civ));
            Assert.AreEqual(holy, ReligionQueries.HolySite(civ));
            Assert.IsTrue(ReligionQueries.Heresy(civ) > 0f && ReligionQueries.Heresy(civ) < 0.3f, "heresy resumed from memory and healed");
        }

        // ------------------------------------------------------------------------------------------ effects

        [Test]
        public void Effects_AreSmallAndBounded_PerRun()
        {
            var ctx = NewWorld(out var capital, out var civ);
            var second = ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            ctx.Ops.CreatePerson(second, RaceId.Human, Sex.Female, 25f);
            ctx.State.Map.Set(ctx.State.Map.Index(second.Center), TileFlags.Sacred);
            civ.Devotion = 0.9f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsTrue(ReligionQueries.HasFaith(civ));

            capital.Faith = 1f; capital.Happiness = 0.99f; capital.Unrest = 0f;
            var settlements = new[] { capital, second };
            const float eps = 1e-5f;
            for (int run = 0; run < 24; run++)
            {
                civ.GodFavor = run % 2 == 0 ? 1f : -1f;
                civ.Memory[ReligionKeys.Heresy] = 0f; // isolate per-run nudges from the schism
                float dev0 = civ.Devotion;
                var f0 = new float[2]; var h0 = new float[2]; var u0 = new float[2];
                for (int i = 0; i < 2; i++) { f0[i] = settlements[i].Faith; h0[i] = settlements[i].Happiness; u0[i] = settlements[i].Unrest; }
                ReligionTestKit.Runs(sys, ctx, 1);
                Assert.IsTrue(Math.Abs(civ.Devotion - dev0) <= ReligionTuning.MaxNudgePerRun + eps, "devotion nudge too large");
                Assert.IsTrue(civ.Devotion >= 0f && civ.Devotion <= 1f);
                for (int i = 0; i < 2; i++)
                {
                    var s = settlements[i];
                    Assert.IsTrue(Math.Abs(s.Faith - f0[i]) <= ReligionTuning.MaxNudgePerRun + eps, "faith nudge too large");
                    Assert.IsTrue(Math.Abs(s.Happiness - h0[i]) <= ReligionTuning.MaxNudgePerRun + eps, "happiness nudge too large");
                    Assert.IsTrue(Math.Abs(s.Unrest - u0[i]) <= ReligionTuning.MaxNudgePerRun + eps, "unrest nudge too large");
                    Assert.IsTrue(s.Faith >= 0f && s.Faith <= 1f && s.Happiness >= 0f && s.Happiness <= 1f && s.Unrest >= 0f && s.Unrest <= 1f);
                }
                foreach (int pid in second.ResidentIds)
                {
                    var p = ctx.State.People.Get(pid);
                    Assert.IsTrue(p.Faith >= 0f && p.Faith <= 1f && p.Happiness >= 0f && p.Happiness <= 1f);
                }
            }
        }

        [Test]
        public void Monasticism_AddsResearch_AndMartialZeal_PublishesNonCompoundingDefense()
        {
            var ctx = NewWorld(out var s, out var civ);
            ReligionTestKit.SetFaith(civ, DoctrineId.SunWorship, DoctrineId.MartialZeal, DoctrineId.Monasticism, DoctrineId.DistantWatcher);
            s.ServiceOutput[(int)ServiceType.Faith] = 2f;
            s.DefenseStrength = 10f;
            civ.ResearchPool = 0f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsTrue(civ.ResearchPool > 0f, "monks contribute research");
            Assert.IsTrue(civ.ResearchPool <= ReligionTuning.MaxResearchPerTick * 48f + 1e-3f);
            Assert.IsTrue(civ.Mem(ReligionKeys.ResearchBonus) > 0f);
            Assert.AreEqual(0.1f, civ.Mem(ReligionKeys.DefenseBonus), 1e-5f);
            Assert.AreEqual(0.1f, s.Mem(ReligionKeys.DefenseBonus), 1e-5f);
            Assert.AreEqual(11f, s.DefenseStrength, 1e-3f);
            ReligionTestKit.Runs(sys, ctx, 2);
            Assert.AreEqual(11f, s.DefenseStrength, 1e-3f, "no compounding when nothing recomputed defense");
            s.DefenseStrength = 20f; // the military module recomputed it
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(22f, s.DefenseStrength, 1e-3f);
        }

        // ------------------------------------------------------------------------------------------ neighbours

        private static SimContext TwoFaiths(out Civilization a, out Settlement sa, out Civilization b, out Settlement sb)
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 64, 11);
            sa = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(12, 12), 4, out a);
            sb = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(50, 50), 4, out b);
            return ctx;
        }

        [Test]
        public void HolyWar_TargetWritten_ForMartialZealAgainstHostileOpposedView()
        {
            var ctx = TwoFaiths(out var a, out var sa, out var b, out var sb);
            ReligionTestKit.SetFaith(a, DoctrineId.SunWorship, DoctrineId.MartialZeal, DoctrineId.Sacrifice, DoctrineId.WrathfulJudge);
            ReligionTestKit.SetFaith(b, DoctrineId.CelestialOrder, DoctrineId.Mercy, DoctrineId.Festivals, DoctrineId.LovingParent);
            var rel = ctx.State.Diplomacy.Get(a.Id, b.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Hostile;
            rel.Opinion = -60f;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(b.Id, ReligionQueries.HolyWarTarget(a));
            Assert.AreEqual((float)b.Id, a.Memory[ReligionKeys.HolyWarTarget]);
            Assert.AreEqual(-1, ReligionQueries.HolyWarTarget(b), "a merciful faith calls no crusade");
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "holy war"));
            ReligionTestKit.Runs(sys, ctx, 2);
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "holy war"), "announced once");

            rel.Stance = DiplomaticStance.Friendly;
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.IsFalse(a.Memory.ContainsKey(ReligionKeys.HolyWarTarget));

            rel.Stance = DiplomaticStance.Hostile;
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(b.Id, ReligionQueries.HolyWarTarget(a));
            b.IsAlive = false;
            ReligionTestKit.Runs(sys, ctx, 1);
            Assert.AreEqual(-1, ReligionQueries.HolyWarTarget(a), "dead civs are not crusaded against");
        }

        [Test]
        public void Conversion_FerventNeighbourRaisesHeresy_AndEventuallySwapsADoctrine()
        {
            var ctx = TwoFaiths(out var a, out var sa, out var b, out var sb);
            ReligionTestKit.SetFaith(a, DoctrineId.SunWorship, DoctrineId.ProsperityGospel, DoctrineId.Festivals, DoctrineId.Trickster);
            ReligionTestKit.SetFaith(b, DoctrineId.CelestialOrder, DoctrineId.Mercy, DoctrineId.Pilgrimage, DoctrineId.LovingParent);
            var rel = ctx.State.Diplomacy.Get(a.Id, b.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Neutral;
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            Action fervour = () => { sa.Faith = 0.1f; sb.Faith = 0.95f; a.GodFavor = 0f; a.Devotion = 0.5f; b.Devotion = 0.5f; };
            ReligionTestKit.Runs(sys, ctx, 1, fervour);
            Assert.IsTrue(ReligionQueries.Heresy(a) > 0f, "conversion pressure breeds heresy");
            Assert.IsTrue(a.Mem(ReligionKeys.ConversionPressure) > 0f);
            Assert.AreEqual(0f, ReligionQueries.Heresy(b), "the fervent faith is unaffected");

            int guard = 0;
            while (ReligionTestKit.CountChronicle(ctx.State, "Missionaries") == 0 && guard++ < 80)
                ReligionTestKit.Runs(sys, ctx, 1, () => { fervour(); a.Memory[ReligionKeys.Heresy] = 0f; });
            Assert.AreEqual(1, ReligionTestKit.CountChronicle(ctx.State, "Missionaries"));
            int shared = 0;
            for (int x = 0; x < 4; x++) if (ReligionTestKit.Held(a, (DoctrineAxis)x) == ReligionTestKit.Held(b, (DoctrineAxis)x)) shared++;
            Assert.AreEqual(1, shared, "exactly one doctrine was adopted from the neighbour");
            Assert.AreEqual((float)b.Id, a.Mem(ReligionKeys.ConvertedBy));
        }

        // ------------------------------------------------------------------------------------------ robustness

        [Test]
        public void NothingThrows_OnEmptyWorldNullContextAndDeadCivs()
        {
            var sys = new ReligionSystem();
            Assert.DoesNotThrow(() => sys.Initialize(null));
            Assert.DoesNotThrow(() => sys.Tick(null));
            var empty = TestWorlds.CreateContext(TestDb.CreateStub(), 32, 3);
            Assert.DoesNotThrow(() => { sys.Initialize(empty); ReligionTestKit.Runs(sys, empty, 3); });

            var ctx = NewWorld(out var s, out var civ);
            civ.Devotion = 0.95f;
            civ.IsAlive = false;
            ReligionTestKit.AddBuilding(ctx, s, BuildingType.Temple);
            Assert.DoesNotThrow(() => { sys.Initialize(ctx); ReligionTestKit.Runs(sys, ctx, 3); });
            Assert.IsFalse(ReligionQueries.HasFaith(civ), "dead civs found no faith");

            // A living civ that lost every settlement: no faith founded, founded faith keeps running harmlessly.
            var ctx2 = NewWorld(out _, out var civ2);
            civ2.SettlementIds.Clear();
            civ2.CapitalId = -1;
            civ2.Devotion = 0.95f;
            Assert.DoesNotThrow(() => ReligionTestKit.Runs(sys, ctx2, 2));
            Assert.IsFalse(ReligionQueries.HasFaith(civ2));
            ReligionTestKit.SetFaith(civ2, DoctrineId.SunWorship, DoctrineId.Mercy, DoctrineId.Festivals, DoctrineId.Trickster);
            Assert.DoesNotThrow(() => ReligionTestKit.Runs(sys, ctx2, 2));

            // Missing database.
            var ctx3 = NewWorld(out _, out var civ3);
            civ3.Devotion = 0.9f;
            ctx3.Db = null;
            Assert.DoesNotThrow(() => ReligionTestKit.Runs(sys, ctx3, 2));
            Assert.IsTrue(ReligionQueries.HasFaith(civ3));
            Assert.IsFalse(string.IsNullOrEmpty(ReligionQueries.FaithName(ctx3.State, null, civ3)));
        }

        [Test]
        public void CorruptValues_AreSanitized()
        {
            var ctx = NewWorld(out var s, out var civ);
            ReligionTestKit.SetFaith(civ, DoctrineId.SunWorship, DoctrineId.Mercy, DoctrineId.Monasticism, DoctrineId.Trickster);
            s.ServiceOutput[(int)ServiceType.Faith] = float.NaN;
            civ.Memory[ReligionKeys.Heresy] = float.NaN;
            civ.Memory[ReligionKeys.HolySite] = float.PositiveInfinity;
            civ.Memory[DoctrineCatalog.MemoryKey(DoctrineAxis.Ethos)] = 999f;
            civ.Memory[DoctrineCatalog.MemoryKey(DoctrineAxis.View)] = (float)DoctrineId.Mercy; // wrong axis
            civ.Memory[ReligionKeys.ReformationDueTick] = float.NaN;
            civ.Devotion = float.NaN;
            civ.GodFavor = float.PositiveInfinity;
            civ.ResearchPool = float.NaN;
            s.Faith = float.NaN; s.Happiness = float.NegativeInfinity; s.Unrest = float.NaN;
            ctx.State.God.Alignment = float.NaN;
            var sys = new ReligionSystem();
            Assert.DoesNotThrow(() => { sys.Initialize(ctx); ReligionTestKit.Runs(sys, ctx, 3); });

            float h = ReligionQueries.Heresy(civ);
            Assert.IsTrue(h >= 0f && h <= 1f);
            Assert.IsNotNull(ReligionQueries.Doctrine(civ, DoctrineAxis.Ethos), "corrupt doctrine self-heals");
            Assert.IsNotNull(ReligionQueries.Doctrine(civ, DoctrineAxis.View));
            Assert.IsFalse(float.IsNaN(civ.Devotion) || float.IsInfinity(civ.Devotion));
            Assert.IsFalse(float.IsNaN(civ.ResearchPool));
            Assert.IsFalse(float.IsNaN(s.Faith) || float.IsNaN(s.Happiness) || float.IsNaN(s.Unrest));
            Assert.IsTrue(s.Faith >= 0f && s.Faith <= 1f && s.Happiness >= 0f && s.Happiness <= 1f);
            foreach (var kv in civ.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        // ------------------------------------------------------------------------------------------ queries

        [Test]
        public void Queries_DescribeTheFaith()
        {
            var ctx = NewWorld(out var s, out var civ);
            Assert.IsTrue(ReligionQueries.Describe(ctx.State, ctx.Db, civ).Contains("no organized faith"));
            Assert.AreEqual("", ReligionQueries.FaithName(ctx.State, ctx.Db, civ));
            Assert.AreEqual(-1, ReligionQueries.HolySite(civ));
            Assert.AreEqual(0f, ReligionQueries.Heresy(civ));
            Assert.IsFalse(ReligionQueries.HasFaith(null));
            Assert.AreEqual("", ReligionQueries.Describe(ctx.State, ctx.Db, null));

            ReligionTestKit.AddBuilding(ctx, s, BuildingType.Shrine);
            var sys = new ReligionSystem();
            sys.Initialize(ctx);
            ReligionTestKit.Runs(sys, ctx, 1);
            string text = ReligionQueries.Describe(ctx.State, ctx.Db, civ);
            Assert.IsTrue(text.Contains(ReligionQueries.FaithName(ctx.State, ctx.Db, civ)));
            Assert.IsTrue(text.Contains("Holy site: " + s.Name));
            Assert.IsTrue(text.Contains("Heresy: 0%"));
            foreach (var d in ReligionQueries.Doctrines(civ)) Assert.IsTrue(text.Contains(d.Name));
            Assert.IsTrue(ReligionQueries.FoundedTick(civ) >= 0);
            Assert.IsTrue(ReligionQueries.Holds(civ, ReligionTestKit.Held(civ, DoctrineAxis.View)));
        }
    }
}
