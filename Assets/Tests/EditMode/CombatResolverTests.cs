using ColosseumDuel.Core;
using NUnit.Framework;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Pins the damage table from the design doc (GDD sections 6 and 7) so a refactor of
    /// CombatResolver cannot silently change the feel of a fight.
    /// </summary>
    public class CombatResolverTests
    {
        private const float Tol = 0.0001f;

        private static GladiatorInstance Brutius() => new GladiatorInstance(GladiatorDef.Brutius); // 10 dmg

        [Test]
        public void Collision_Unarmed_DealsFullBaseDamage()
        {
            var a = Brutius();
            var b = Brutius();
            float dealt = CombatResolver.DealDamage(a, b, isCollision: true);
            Assert.AreEqual(10f, dealt, Tol);
            Assert.AreEqual(190f, b.Hp, Tol);
        }

        [Test]
        public void PassBy_Unarmed_DealsHalfDamage()
        {
            var a = Brutius();
            var b = Brutius();
            Assert.AreEqual(5f, CombatResolver.DealDamage(a, b, isCollision: false), Tol);
        }

        [Test]
        public void OneHandedAxe_Multiplies15x_OnBothCollisionAndPassBy()
        {
            var a = Brutius();
            a.Weapon = WeaponType.OneHanded;
            Assert.AreEqual(15f, CombatResolver.DealDamage(a, Brutius(), isCollision: true), Tol);

            var c = Brutius();
            c.Weapon = WeaponType.OneHanded;
            Assert.AreEqual(7.5f, CombatResolver.DealDamage(c, Brutius(), isCollision: false), Tol);
        }

        [Test]
        public void TwoHandedTrident_TurnsAPassByIntoFullDamage_ButDoesNotBoostCollisions()
        {
            var passer = Brutius();
            passer.Weapon = WeaponType.TwoHanded;
            Assert.AreEqual(10f, CombatResolver.DealDamage(passer, Brutius(), isCollision: false), Tol,
                "a trident pass-by should land the full 100%, not the usual 50%");

            var crasher = Brutius();
            crasher.Weapon = WeaponType.TwoHanded;
            Assert.AreEqual(10f, CombatResolver.DealDamage(crasher, Brutius(), isCollision: true), Tol,
                "a trident gives no bonus on a head-on collision");
        }

        [Test]
        public void Defending_Reduces30Percent()
        {
            var d = Brutius();
            d.PlannedAction = ActionType.Defend;
            Assert.AreEqual(7f, CombatResolver.DealDamage(Brutius(), d, isCollision: true), Tol);
        }

        [Test]
        public void Shield_Reduces50Percent_AndStacksMultiplicativelyWithDefend()
        {
            var shielded = Brutius();
            shielded.HasShield = true;
            Assert.AreEqual(5f, CombatResolver.DealDamage(Brutius(), shielded, isCollision: true), Tol);

            var both = Brutius();
            both.HasShield = true;
            both.PlannedAction = ActionType.Defend;
            Assert.AreEqual(3.5f, CombatResolver.DealDamage(Brutius(), both, isCollision: true), Tol,
                "0.5 shield x 0.7 defend = 0.35");
        }

        [Test]
        public void FuryBuff_Reduces25PercentOfIncomingDamage()
        {
            var furious = new GladiatorInstance(GladiatorDef.Barbarius);
            furious.Buff = new ActiveBuff { Key = AbilityKey.Fury, CyclesLeft = 2 };
            Assert.AreEqual(7.5f, CombatResolver.DealDamage(Brutius(), furious, isCollision: true), Tol);
        }

        [Test]
        public void WeaponAndShield_AreSingleUse()
        {
            var attacker = Brutius();
            attacker.Weapon = WeaponType.OneHanded;
            var defender = Brutius();
            defender.HasShield = true;

            CombatResolver.DealDamage(attacker, defender, isCollision: true);

            Assert.AreEqual(WeaponType.None, attacker.Weapon, "the axe should break after one hit");
            Assert.IsFalse(defender.HasShield, "the shield should break after one block");

            Assert.AreEqual(10f, CombatResolver.DealDamage(attacker, defender, isCollision: true), Tol,
                "the second hit is unarmed against an unshielded target");
        }

        [Test]
        public void LethalDamage_ClampsHpAtZero_AndMarksDead()
        {
            var victim = Brutius();
            victim.Hp = 3f;
            CombatResolver.DealDamage(Brutius(), victim, isCollision: true);
            Assert.AreEqual(0f, victim.Hp, Tol);
            Assert.IsFalse(victim.Alive);
        }
    }
}
