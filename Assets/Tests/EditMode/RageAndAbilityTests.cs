using ColosseumDuel.Core;
using NUnit.Framework;

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
        public void MongooseBuff_Gives2AttacksPerRound_ForThreeRounds()
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

            g.BeginRound();
            Assert.AreEqual(2, g.AttacksRemainingThisRound, "second buffed round");

            g.BeginRound();
            Assert.AreEqual(2, g.AttacksRemainingThisRound, "third buffed round");

            g.BeginRound();
            Assert.AreEqual(1, g.AttacksRemainingThisRound, "back to one attack once the buff expires");
        }
    }
}
