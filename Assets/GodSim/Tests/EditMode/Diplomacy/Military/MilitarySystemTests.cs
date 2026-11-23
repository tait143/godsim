using GodSim.Core;
using GodSim.Core.Diplomacy;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Military
{
    public class MilitarySystemTests
    {
        [Test]
        public void DefenseStrength_IsServicePlusSoldiers()
        {
            var b = MilitaryTestKit.Create(12, 0, 12, 5, defService: 10f, war: false);
            b.Init();
            Assert.AreEqual(15f, b.D.DefenseStrength, 1e-4f);
            Assert.AreEqual(15f, b.Defender.MilitaryPower, 1e-4f);
            Assert.AreEqual(0f, b.A.DefenseStrength, 1e-4f);
        }

        [Test]
        public void Weapons_And_MilitaryModifier_ScaleSoldierStrength()
        {
            var b = MilitaryTestKit.Create(12, 0, 12, 5, defService: 10f, war: false);
            b.D.Stockpile.Add(ResourceType.Weapons, 2.5f); // exactly 0.5 per soldier
            b.Defender.Modifiers.Military = 2f;
            b.Init();
            Assert.AreEqual(10f + 5 * 2f * 1.3f, b.D.DefenseStrength, 1e-3f);

            b.D.Stockpile.ConsumeUpTo(ResourceType.Weapons, 1f); // below 0.5 per soldier
            b.Run(6);
            Assert.AreEqual(10f + 5 * 2f, b.D.DefenseStrength, 1e-3f);
        }

        [Test]
        public void MilitaryPower_IncludesFieldArmies_AndExcludesSoldiersAway()
        {
            var b = MilitaryTestKit.Create(12, 0, 12, 5, defService: 10f, war: false);
            var soldiers = new System.Collections.Generic.List<Person>();
            foreach (int id in b.D.ResidentIds) { var p = b.State.People.Get(id); if (p.Job == JobType.Soldier && soldiers.Count < 2) soldiers.Add(p); }
            var army = b.Ctx.Ops.CreateArmy(b.Defender, b.D, soldiers);
            army.Strength = 7f;
            army.State = ArmyState.Marching;
            b.Init();
            Assert.AreEqual(10f + 3f, b.D.DefenseStrength, 1e-4f, "2 of 5 soldiers are away");
            Assert.AreEqual(13f + 7f, b.Defender.MilitaryPower, 1e-4f);
        }

        [Test]
        public void BadModifiers_NeverProduceNaN()
        {
            var b = MilitaryTestKit.Create(12, 0, 12, 5, defService: float.NaN, war: false);
            b.Defender.Modifiers.Military = float.PositiveInfinity;
            b.Init();
            Assert.IsFalse(float.IsNaN(b.D.DefenseStrength) || float.IsInfinity(b.D.DefenseStrength));
            Assert.IsFalse(float.IsNaN(b.Defender.MilitaryPower) || float.IsInfinity(b.Defender.MilitaryPower));
        }

        [Test]
        public void SiegeDamage_ReducesServiceDefense_AndHealsWhenNotBesieged()
        {
            var b = MilitaryTestKit.Create(12, 0, 12, 0, defService: 20f, war: false);
            b.D.Memory[MilitaryMath.KeySiegeDamage] = 0.5f;
            b.Init();
            Assert.AreEqual(10f, b.D.DefenseStrength, 1e-4f);
            b.Run(120);
            Assert.IsFalse(b.D.Memory.ContainsKey(MilitaryMath.KeySiegeDamage));
            Assert.AreEqual(20f, b.D.DefenseStrength, 1e-4f);
        }
    }
}
