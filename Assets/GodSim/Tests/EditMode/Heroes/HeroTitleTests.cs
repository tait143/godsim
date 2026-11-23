using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Heroes;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Heroes
{
    public class HeroTitleTests
    {
        [Test]
        public void EpithetsAreDeterministicAndNonEmpty()
        {
            var roles = (HeroRole[])System.Enum.GetValues(typeof(HeroRole));
            foreach (var role in roles)
                foreach (var race in EnumUtil.AllRaces)
                    foreach (int deeds in new[] { 0, 5, 15, 26, 500 })
                        for (ulong seed = 0; seed < 20; seed++)
                        {
                            string a = HeroTitles.Epithet(role, race, deeds, seed, "Folk");
                            Assert.IsFalse(string.IsNullOrEmpty(a));
                            Assert.AreEqual(a, HeroTitles.Epithet(role, race, deeds, seed, "Folk"));
                        }
            Assert.AreEqual(HeroTitles.Epithet(HeroRole.Sage, RaceId.Elf, 3, 9), HeroTitles.Epithet(HeroRole.Sage, RaceId.Elf, 4, 9), "stable within a tier");
        }

        [Test]
        public void TitlesGrowWithDeedsAndIncludeSignatureEpithets()
        {
            Assert.AreEqual(0, HeroTitles.Tier(0));
            Assert.AreEqual(1, HeroTitles.Tier(HeroTitles.Tier1Deeds));
            Assert.AreEqual(2, HeroTitles.Tier(HeroTitles.Tier2Deeds));
            Assert.AreEqual(2, HeroTitles.Tier(HeroRules.LegendaryDeeds));
            Assert.AreEqual(3, HeroTitles.Tier(HeroRules.LegendaryDeeds + 1));

            var generals = new HashSet<string>();
            var sages = new HashSet<string>();
            for (ulong seed = 0; seed < 200; seed++)
            {
                generals.Add(HeroTitles.Epithet(HeroRole.General, RaceId.Human, 20, seed));
                sages.Add(HeroTitles.Epithet(HeroRole.Sage, RaceId.Human, 0, seed));
            }
            Assert.IsTrue(generals.Contains("Breaker of Sieges"));
            Assert.IsTrue(sages.Contains("the Wise"));
            Assert.IsFalse(sages.Contains("Breaker of Sieges"));
            Assert.Greater(generals.Count, 2, "variety across heroes");
        }

        [Test]
        public void RoleNamesAreReadable()
        {
            Assert.AreEqual("Merchant Prince", HeroTitles.RoleName(HeroRole.MerchantPrince));
            Assert.AreEqual("Architect", HeroTitles.RoleName(HeroRole.Architect));
            Assert.AreEqual("Hero", HeroTitles.RoleName((HeroRole)200));
            Assert.AreEqual("the Renowned", HeroTitles.Epithet((HeroRole)200, RaceId.Human, 0, 1));
        }

        [Test]
        public void QueriesTitleAndLiving()
        {
            var ctx = HeroTestKit.NewContext();
            var s = HeroTestKit.PlainSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 12, out var civ);
            var sys = new HeroSystem { RaiseChanceScale = 0f };
            var p = HeroTestKit.Resident(ctx, s, 0);
            var q = HeroTestKit.Resident(ctx, s, 1);
            Assert.AreEqual("", HeroQueries.Title(ctx.State, ctx.Db, p));
            Assert.AreEqual("", HeroQueries.Title(null, null, p));
            Assert.AreEqual("", HeroQueries.Title(ctx.State, null, null));
            Assert.IsFalse(HeroQueries.IsHero(ctx.State, -1));
            Assert.IsFalse(HeroQueries.IsHero(null, p.Id));

            sys.Promote(ctx, q, HeroRole.Explorer);
            sys.Promote(ctx, p, HeroRole.General);
            string t = HeroQueries.Title(ctx.State, ctx.Db, p);
            Assert.IsFalse(string.IsNullOrEmpty(t));
            Assert.AreEqual(t, HeroQueries.Title(ctx.State, null, p));

            var living = new List<HeroInfo>(HeroQueries.Living(ctx.State));
            Assert.AreEqual(2, living.Count);
            Assert.Less(living[0].PersonId, living[1].PersonId, "ordered by id");
            Assert.IsTrue(HeroQueries.TryGet(ctx.State, p.Id, out var info));
            Assert.AreEqual(HeroRole.General, info.Role);
            Assert.AreEqual(s.Id, info.HomeSettlementId);
            Assert.AreEqual(civ.Id, info.CivilizationId);
            Assert.AreEqual(ctx.State.Tick, info.SinceTick);
            Assert.IsFalse(info.IsLegendary);
            Assert.AreEqual(0, new List<HeroInfo>(HeroQueries.Living(null)).Count);
        }

        [Test]
        public void KeyParsing()
        {
            Assert.IsTrue(HeroKeys.TryParseRoleKey("hero.123.role", out int id));
            Assert.AreEqual(123, id);
            Assert.IsFalse(HeroKeys.TryParseRoleKey("hero.123.deeds", out _));
            Assert.IsFalse(HeroKeys.TryParseRoleKey("hero..role", out _));
            Assert.IsFalse(HeroKeys.TryParseRoleKey("hero.-1.role", out _));
            Assert.IsFalse(HeroKeys.TryParseRoleKey("hero.99999999999.role", out _));
            Assert.IsFalse(HeroKeys.TryParseRoleKey("ai.hero.1.role", out _));
            Assert.IsFalse(HeroKeys.TryParseRoleKey(null, out _));
            Assert.AreEqual("hero.7.role", HeroKeys.Role(7));
        }

        [Test]
        public void BiographyTextVariants()
        {
            Assert.AreEqual("Mira Vale, the Healer of Oakford, succumbed to sickness aged 40; she tended the sick for 1 year.",
                HeroChronicle.Biography("Mira Vale", HeroRole.Healer, "Oakford", 40.9f, DeathCause.Disease, Sex.Female, 1, false));
            Assert.AreEqual("Kor, the General, died aged 30.",
                HeroChronicle.Biography("Kor", HeroRole.General, null, 30f, DeathCause.OldAge, Sex.Male, 0, false));
            StringAssert.EndsWith("Their legend will be told for ages.",
                HeroChronicle.Biography("Kor", HeroRole.General, null, 30f, DeathCause.OldAge, Sex.Male, 99, true));
            Assert.AreEqual("The Sage of the Realm passed from the world.",
                HeroChronicle.Biography(null, HeroRole.Sage, "the Realm", float.NaN, null, null, 0, false));
        }
    }
}
