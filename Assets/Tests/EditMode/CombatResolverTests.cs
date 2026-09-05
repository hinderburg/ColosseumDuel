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

        private static GladiatorInstance Brutius() => new GladiatorInstance(GladiatorDef.Brutius);

        /// <summary>
        /// Expectations below are fractions of this rather than absolute numbers.
        ///
        /// What CombatResolver decides is the multipliers - half on a pass-by, 0.7 while guarding,
        /// 0.5 behind a shield - and those are what these tests are for. Restating the stat block in
        /// every assertion only meant that a balance pass on the damage numbers broke ten tests that
        /// had nothing to say about it. The stat block itself is pinned once, in TheDamageTable.
        /// </summary>
        private static float Base => GladiatorDef.Brutius.Damage;

        [Test]
        public void TheDamageTable()
        {
            // The one place the actual numbers are stated. Doubled across the board in a balance
            // pass; everything else in this file checks the multipliers applied on top of them, so
            // a future pass touches this test alone.
            Assert.AreEqual(20f, GladiatorDef.Brutius.Damage, Tol);
            Assert.AreEqual(26f, GladiatorDef.Barbarius.Damage, Tol);
            Assert.AreEqual(14f, GladiatorDef.Hilius.Damage, Tol);
        }

        [Test]
        public void Collision_Unarmed_DealsFullBaseDamage()
        {
            var a = Brutius();
            var b = Brutius();
            float dealt = CombatResolver.DealDamage(a, b, isCollision: true);
            Assert.AreEqual(Base, dealt, Tol);
            Assert.AreEqual(GladiatorDef.Brutius.MaxHp - Base, b.Hp, Tol);
        }

        [Test]
        public void PassBy_Unarmed_DealsHalfDamage()
        {
            var a = Brutius();
            var b = Brutius();
            Assert.AreEqual(Base * 0.5f, CombatResolver.DealDamage(a, b, isCollision: false), Tol);
        }

        [Test]
        public void OneHandedAxe_Multiplies15x_OnBothCollisionAndPassBy()
        {
            var a = Brutius();
            a.Weapon = WeaponType.OneHanded;
            Assert.AreEqual(Base * 1.5f, CombatResolver.DealDamage(a, Brutius(), isCollision: true), Tol);

            var c = Brutius();
            c.Weapon = WeaponType.OneHanded;
            Assert.AreEqual(Base * 0.75f, CombatResolver.DealDamage(c, Brutius(), isCollision: false), Tol);
        }

        [Test]
        public void TwoHandedTrident_TurnsAPassByIntoFullDamage_ButDoesNotBoostCollisions()
        {
            var passer = Brutius();
            passer.Weapon = WeaponType.TwoHanded;
            Assert.AreEqual(Base, CombatResolver.DealDamage(passer, Brutius(), isCollision: false), Tol,
                "a trident pass-by should land the full 100%, not the usual 50%");

            var crasher = Brutius();
            crasher.Weapon = WeaponType.TwoHanded;
            Assert.AreEqual(Base, CombatResolver.DealDamage(crasher, Brutius(), isCollision: true), Tol,
                "a trident gives no bonus on a head-on collision");
        }

        [Test]
        public void Defending_Reduces30Percent()
        {
            var d = Brutius();
            d.PlannedAction = ActionType.Defend;
            Assert.AreEqual(Base * 0.7f, CombatResolver.DealDamage(Brutius(), d, isCollision: true), Tol);
        }

        [Test]
        public void Shield_Reduces50Percent_AndStacksMultiplicativelyWithDefend()
        {
            var shielded = Brutius();
            shielded.HasShield = true;
            Assert.AreEqual(Base * 0.5f, CombatResolver.DealDamage(Brutius(), shielded, isCollision: true), Tol);

            var both = Brutius();
            both.HasShield = true;
            both.PlannedAction = ActionType.Defend;
            Assert.AreEqual(Base * 0.35f, CombatResolver.DealDamage(Brutius(), both, isCollision: true), Tol,
                "0.5 shield x 0.7 defend = 0.35");
        }

        [Test]
        public void FuryBuff_Reduces25PercentOfIncomingDamage()
        {
            var furious = new GladiatorInstance(GladiatorDef.Barbarius);
            furious.Buff = new ActiveBuff { Key = AbilityKey.Fury, CyclesLeft = 2 };
            Assert.AreEqual(Base * 0.75f, CombatResolver.DealDamage(Brutius(), furious, isCollision: true), Tol);
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

            Assert.AreEqual(Base, CombatResolver.DealDamage(attacker, defender, isCollision: true), Tol,
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
