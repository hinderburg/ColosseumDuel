using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>GDD section 5: the rage meter and how an ability consumes it.</summary>
    public class RageAndAbilityTests
    {
        private const float Tol = 0.0001f;

        private static GladiatorInstance Fresh(GladiatorDef def = null)
            => new GladiatorInstance(def ?? GladiatorDef.Brutius);

        [Test]
        public void QuietRound_GainsOnlyThePassive15Percent()
        {
            var g = Fresh();
            g.BeginRound();
            g.ResolveRoundRage();
            Assert.AreEqual(0.15f, g.Rage, Tol);
        }

        [Test]
        public void DealingAndTakingDamage_StackOnTopOfThePassiveGain()
        {
            var g = Fresh();
            g.BeginRound();
            g.DealtDamageThisRound = true;
            g.TookDamageThisRound = true;
            g.ResolveRoundRage();
            Assert.AreEqual(GameConstants.RagePerRoundPassive + GameConstants.RageBonusOnDealDamage
                            + GameConstants.RageBonusOnTakeDamage, g.Rage, Tol, "passive + dealt + taken");
        }

        /// <summary>A guard that met the blow from the front earns more than taking the blow did, and in place of it.</summary>
        [Test]
        public void BlockingABlowFromTheFront_EarnsMoreRageThanTakingIt_InPlaceOfIt()
        {
            var g = Fresh();
            g.BeginRound();
            g.TookDamageThisRound = true;
            g.BlockedFrontThisRound = true;
            g.ResolveRoundRage();
            Assert.AreEqual(GameConstants.RagePerRoundPassive + GameConstants.RageBonusOnBlock, g.Rage, Tol,
                "passive + block, and not the take bonus on top");
            Assert.Greater(GameConstants.RageBonusOnBlock, GameConstants.RageBonusOnTakeDamage);

            g.BeginRound();
            Assert.IsFalse(g.BlockedFrontThisRound, "the block is the round's, not the man's");
        }

        [Test]
        public void ItIsTheGuardMeetingTheBlowHeadOnThatCounts_NotAGuardStruckFromBehind()
        {
            var striker = new GladiatorInstance(GladiatorDef.Hilius) { Pos = new Vector2(0f, 30f) };
            var guard = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero, Facing = Vector2.up };
            guard.PlannedAction = ActionType.Defend;
            CombatResolver.DealDamage(striker, guard);
            Assert.IsTrue(guard.BlockedFrontThisRound, "struck from the front, guarding");

            var behind = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero, Facing = Vector2.down };
            behind.PlannedAction = ActionType.Defend;
            CombatResolver.DealDamage(striker, behind);
            Assert.IsFalse(behind.BlockedFrontThisRound, "struck in the back, the guard was not in the way");
        }

        [Test]
        public void Rage_IsClampedAt100Percent()
        {
            var g = Fresh();
            for (int i = 0; i < 20; i++)
            {
                g.BeginRound();
                g.DealtDamageThisRound = true;
                g.ResolveRoundRage();
            }
            Assert.AreEqual(GameConstants.RageMax, g.Rage, Tol);
        }

        [Test]
        public void AbilityRequiresAFullMeter()
        {
            var g = Fresh();
            g.Rage = 0.99f;
            Assert.IsFalse(g.CanActivateAbility);
            g.Rage = 1f;
            Assert.IsTrue(g.CanActivateAbility);
        }

        [Test]
        public void Activating_ResetsTheMeter_AndFreezesItForTheActivationRoundAndTheNextFullOne()
        {
            // Pins the intent behind AbilityLockedRounds = AbilityLockRounds + 1, which is easy to
            // "simplify" into an off-by-one. GDD: after use the meter does not charge during the
            // next full round - and, since the ability fires mid-round, not during that round either.
            var g = Fresh();
            g.BeginRound();
            g.Rage = 1f;
            g.ActivateAbility();
            Assert.AreEqual(0f, g.Rage, Tol);

            g.ResolveRoundRage(); // end of the activation round
            Assert.AreEqual(0f, g.Rage, Tol, "no rage during the round the ability was used in");

            g.BeginRound();
            g.ResolveRoundRage(); // the next full round
            Assert.AreEqual(0f, g.Rage, Tol, "no rage during the next full round either");

            g.BeginRound();
            g.ResolveRoundRage(); // the round after that charges normally again
            Assert.AreEqual(0.15f, g.Rage, Tol);
        }

        [Test]
        public void RampageBuff_Gives50PercentMoreSpeed_ForTwoRounds()
        {
            var g = Fresh(GladiatorDef.Brutius);
            float speed = GladiatorDef.Brutius.Speed;
            g.Ability = AbilityKey.Rampage;
            g.BeginRound();
            g.Rage = 1f;
            g.ActivateAbility();

            Assert.AreEqual(speed * GameConstants.RampageSpeedMult, g.EffectiveSpeed(), Tol, "activation round");
            g.BeginRound();
            Assert.AreEqual(speed * GameConstants.RampageSpeedMult, g.EffectiveSpeed(), Tol, "second buffed round");
            g.BeginRound();
            Assert.AreEqual(speed, g.EffectiveSpeed(), Tol, "buff has expired");
        }

        [Test]
        public void MongooseBuff_Gives2AttacksPerRound_ForAsLongAsItsCardSays()
        {
            // Regression: AttacksRemainingThisRound used to be reset to a hard-coded 1 in BeginRound,
            // which silently dropped the second attack on the buff's later rounds.
            var g = Fresh(GladiatorDef.Hilius);
            g.Ability = AbilityKey.Mongoose;
            g.BeginRound();
            Assert.AreEqual(1, g.AttacksRemainingThisRound);

            g.Rage = 1f;
            g.ActivateAbility();
            Assert.AreEqual(2, g.AttacksRemainingThisRound, "activation round");

            int rounds = AbilityDef.Get(AbilityKey.Mongoose).Rounds;
            for (int round = 2; round <= rounds; round++)
            {
                g.BeginRound();
                Assert.AreEqual(2, g.AttacksRemainingThisRound, $"buffed round {round} of {rounds}");
            }

            g.BeginRound();
            Assert.AreEqual(1, g.AttacksRemainingThisRound, "back to one attack once the buff expires");
        }
    }
}
