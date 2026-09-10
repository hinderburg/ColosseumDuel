using System.Linq;
using ColosseumDuel.Core;
using NUnit.Framework;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Pins the damage table so a refactor of CombatResolver cannot silently change the feel of a
    /// fight. A blow is now the fighter's own stat put through his weapon, so both halves are here.
    /// </summary>
    public class CombatResolverTests
    {
        private const float Tol = 0.0001f;

        /// <summary>Brutius with nothing in his hands, whatever he is trained in.</summary>
        private static GladiatorInstance Bare()
            => new GladiatorInstance(GladiatorDef.Brutius) { Weapon = WeaponKind.None };

        private static GladiatorInstance With(WeaponKind kind, bool gilded = false)
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius) { Weapon = kind, WeaponIsGilded = gilded };
            return g;
        }

        /// <summary>
        /// Expectations below are fractions of this rather than absolute numbers.
        ///
        /// What CombatResolver decides is the multipliers, and those are what these tests are for.
        /// Restating the stat block in every assertion only meant that a balance pass on the damage
        /// numbers broke ten tests that had nothing to say about it. The stat block is pinned once,
        /// in TheStatTable.
        /// </summary>
        private static float Base => GladiatorDef.Brutius.Damage;

        [Test]
        public void TheStatTable()
        {
            // The one place the actual numbers are stated. They dropped by a factor of ten when the
            // weapon started carrying a multiplier of its own; everything else in this file checks
            // what is applied on top, so a future balance pass touches this test alone.
            Assert.AreEqual(200f, GladiatorDef.Brutius.MaxHp, Tol);
            Assert.AreEqual(10f, GladiatorDef.Brutius.Damage, Tol);

            Assert.AreEqual(100f, GladiatorDef.Barbarius.MaxHp, Tol);
            Assert.AreEqual(13f, GladiatorDef.Barbarius.Damage, Tol);

            Assert.AreEqual(150f, GladiatorDef.Hilius.MaxHp, Tol);
            Assert.AreEqual(7f, GladiatorDef.Hilius.Damage, Tol);
        }

        [Test]
        public void EachArchetypeIsTrainedInADifferentWeapon()
        {
            // Not decoration: it is what makes the three roster slots a composition rather than a
            // preference, and it is the only thing the green weapon badge on a card is reporting.
            var skills = GladiatorDef.All.Select(d => d.SkilledWith).ToList();
            CollectionAssert.AllItemsAreUnique(skills);
            CollectionAssert.AreEquivalent(WeaponDef.All.Select(w => w.Kind).ToList(), skills);
        }

        [Test]
        public void TheWeaponTable()
        {
            Assert.AreEqual(0.7f, WeaponDef.DualSwords.DamageMultiplier, Tol);
            Assert.AreEqual(2, WeaponDef.DualSwords.Attacks);
            Assert.IsTrue(WeaponDef.DualSwords.Bleeds);

            Assert.AreEqual(1f, WeaponDef.SwordAndShield.DamageMultiplier, Tol);
            Assert.AreEqual(1, WeaponDef.SwordAndShield.Attacks);
            Assert.AreEqual(0.5f, WeaponDef.SwordAndShield.IncomingDamageMultiplier, Tol);

            Assert.AreEqual(1.5f, WeaponDef.TwoHandedMace.DamageMultiplier, Tol);
            Assert.AreEqual(1, WeaponDef.TwoHandedMace.Attacks);
            Assert.Greater(WeaponDef.TwoHandedMace.Knockback, 0f);

            // The three have to trade against each other, or two of the roster slots are decoration.
            // Twin swords land less per blow than the mace but twice as often; the shield gives up
            // the mace's weight for the only damage reduction any weapon carries.
            Assert.Less(WeaponDef.DualSwords.DamageMultiplier, WeaponDef.TwoHandedMace.DamageMultiplier);
            Assert.Greater(WeaponDef.DualSwords.Attacks * WeaponDef.DualSwords.DamageMultiplier,
                WeaponDef.SwordAndShield.Attacks * WeaponDef.SwordAndShield.DamageMultiplier);
            Assert.Less(WeaponDef.SwordAndShield.IncomingDamageMultiplier, 1f);
        }

        [Test]
        public void AWeaponMultipliesTheWieldersOwnDamage()
        {
            Assert.AreEqual(Base * 0.7f, CombatResolver.DealDamage(With(WeaponKind.DualSwords), Bare()), Tol);
            Assert.AreEqual(Base, CombatResolver.DealDamage(With(WeaponKind.SwordAndShield), Bare()), Tol);
            Assert.AreEqual(Base * 1.5f, CombatResolver.DealDamage(With(WeaponKind.TwoHandedMace), Bare()), Tol);
        }

        [Test]
        public void AGildedWeaponHitsHarderThanTheOneHeWalkedInWith()
        {
            // The whole reason to break off and cross a mined arena for one.
            float plain = CombatResolver.DealDamage(With(WeaponKind.TwoHandedMace), Bare());
            float gilded = CombatResolver.DealDamage(With(WeaponKind.TwoHandedMace, gilded: true), Bare());

            Assert.AreEqual(plain * GameConstants.GildedWeaponMult, gilded, Tol);
            Assert.Greater(gilded, plain);
        }

        [Test]
        public void Defending_Reduces30Percent()
        {
            var d = Bare();
            d.PlannedAction = ActionType.Defend;
            Assert.AreEqual(Base * 0.7f, CombatResolver.DealDamage(With(WeaponKind.SwordAndShield), d), Tol);
        }

        [Test]
        public void AShieldHalvesWhatItsCarrierTakes_AndStacksWithDefending()
        {
            var shielded = With(WeaponKind.SwordAndShield);
            Assert.AreEqual(Base * 0.5f,
                CombatResolver.DealDamage(With(WeaponKind.SwordAndShield), shielded), Tol);

            var both = With(WeaponKind.SwordAndShield);
            both.PlannedAction = ActionType.Defend;
            Assert.AreEqual(Base * 0.35f,
                CombatResolver.DealDamage(With(WeaponKind.SwordAndShield), both), Tol,
                "0.5 shield x 0.7 defend = 0.35");
        }

        [Test]
        public void FuryBuff_Reduces25PercentOfIncomingDamage()
        {
            var furious = Bare();
            furious.Buff = new ActiveBuff { Key = AbilityKey.Fury, CyclesLeft = 2 };
            Assert.AreEqual(Base * 0.75f,
                CombatResolver.DealDamage(With(WeaponKind.SwordAndShield), furious), Tol);
        }

        [Test]
        public void AWeaponSurvivesTheBlowItLands()
        {
            // It used to break after one hit, which made sense while a weapon was a bonus lying on
            // the floor. Now it is half of who a gladiator is: one that vanished after a single
            // exchange would leave him fighting the rest of the match as nobody in particular.
            var attacker = With(WeaponKind.TwoHandedMace, gilded: true);
            var defender = With(WeaponKind.SwordAndShield);

            CombatResolver.DealDamage(attacker, defender);

            Assert.AreEqual(WeaponKind.TwoHandedMace, attacker.Weapon);
            Assert.IsTrue(attacker.WeaponIsGilded);
            Assert.IsTrue(defender.HasShield, "a shield is not spent by being hit");
        }

        [Test]
        public void MongooseDoublesWhateverTheWeaponAlreadySwings()
        {
            // Multiplied, not overridden: the ability says "twice as many attacks", and a weapon
            // that already strikes twice should not have half of it quietly cancelled.
            var single = With(WeaponKind.TwoHandedMace);
            var pair = With(WeaponKind.DualSwords);
            Assert.AreEqual(1, single.AttacksPerCycle);
            Assert.AreEqual(2, pair.AttacksPerCycle);

            single.Buff = new ActiveBuff { Key = AbilityKey.Mongoose, CyclesLeft = 2 };
            pair.Buff = new ActiveBuff { Key = AbilityKey.Mongoose, CyclesLeft = 2 };
            Assert.AreEqual(2, single.AttacksPerCycle);
            Assert.AreEqual(4, pair.AttacksPerCycle);
        }

        [Test]
        public void LethalDamage_ClampsHpAtZero_AndMarksDead()
        {
            var victim = Bare();
            victim.Hp = 3f;
            CombatResolver.DealDamage(With(WeaponKind.TwoHandedMace), victim);
            Assert.AreEqual(0f, victim.Hp, Tol);
            Assert.IsFalse(victim.Alive);
        }

        // ------------------------------------------------------------------
        // bleeding
        // ------------------------------------------------------------------

        [Test]
        public void OnlyTheTwinSwordsOpenAWound()
        {
            foreach (var weapon in WeaponDef.All)
            {
                var victim = Bare();
                CombatResolver.DealDamage(With(weapon.Kind), victim);
                Assert.AreEqual(weapon.Bleeds, victim.IsBleeding,
                    $"{weapon.Name} should {(weapon.Bleeds ? "" : "not ")}leave the target bleeding");
            }
        }

        [Test]
        public void ABleedCostsAQuarterOfTheBlowThatOpenedIt_ForTwoCycles()
        {
            var victim = Bare();
            var swordsman = With(WeaponKind.DualSwords);
            CombatResolver.DealDamage(swordsman, victim);

            float raw = CombatResolver.ComputeAttackDamage(swordsman);
            float expected = raw * GameConstants.BleedFraction;
            float afterBlow = victim.Hp;

            Assert.AreEqual(GameConstants.BleedCycles, victim.BleedCyclesLeft);
            Assert.AreEqual(expected, victim.TickBleed(), Tol);
            Assert.AreEqual(afterBlow - expected, victim.Hp, Tol);

            Assert.AreEqual(expected, victim.TickBleed(), Tol, "and once more on the second cycle");
            Assert.AreEqual(0f, victim.TickBleed(), Tol, "then the wound is closed");
            Assert.IsFalse(victim.IsBleeding);
        }

        [Test]
        public void TheBleedIsTakenOffTheRawBlow_NotOffWhatGotThroughTheShield()
        {
            // A wound is a wound: the shield that softened the hit is not still in the way of the
            // bleeding afterwards. Without this, sword and shield would shrug off the twin swords'
            // whole reason for existing rather than only half of it.
            var shielded = With(WeaponKind.SwordAndShield);
            var swordsman = With(WeaponKind.DualSwords);

            float landed = CombatResolver.DealDamage(swordsman, shielded);
            float raw = CombatResolver.ComputeAttackDamage(swordsman);

            Assert.Less(landed, raw, "the shield should have taken half the blow");
            Assert.AreEqual(raw * GameConstants.BleedFraction, shielded.TickBleed(), Tol);
        }

        [Test]
        public void AFreshCutRestartsTheCount_AndNeverTalksTheWoundDown()
        {
            var victim = Bare();
            var heavy = With(WeaponKind.DualSwords, gilded: true);
            var light = With(WeaponKind.DualSwords);

            CombatResolver.DealDamage(heavy, victim);
            float strong = victim.BleedPerCycle;

            victim.TickBleed();
            Assert.AreEqual(GameConstants.BleedCycles - 1, victim.BleedCyclesLeft);

            // Keep landing and the bleed never runs out - that is what the pair of swords buys.
            CombatResolver.DealDamage(light, victim);
            Assert.AreEqual(GameConstants.BleedCycles, victim.BleedCyclesLeft, "the count starts again");
            Assert.AreEqual(strong, victim.BleedPerCycle, Tol,
                "a light blow arriving after a heavy one must not talk the wound down");
        }
    }
}
