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
        public void QuietCycle_GainsOnlyThePassive15Percent()
        {
            var g = Fresh();
            g.BeginCycle();
            g.ResolveCycleRage();
            Assert.AreEqual(0.15f, g.Rage, Tol);
        }

        [Test]
        public void DealingAndTakingDamage_StackOnTopOfThePassiveGain()
        {
            var g = Fresh();
            g.BeginCycle();
            g.DealtDamageThisCycle = true;
            g.TookDamageThisCycle = true;
            g.ResolveCycleRage();
            Assert.AreEqual(0.40f, g.Rage, Tol, "0.15 passive + 0.15 dealt + 0.10 taken");
        }

        [Test]
        public void Rage_IsClampedAt100Percent()
        {
            var g = Fresh();
            for (int i = 0; i < 20; i++)
            {
                g.BeginCycle();
                g.DealtDamageThisCycle = true;
                g.ResolveCycleRage();
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
        public void Activating_ResetsTheMeter_AndFreezesItForTheActivationCycleAndTheNextFullOne()
        {
            // Pins the intent behind AbilityLockedCycles = AbilityLockCycles + 1, which is easy to
            // "simplify" into an off-by-one. GDD: after use the meter does not charge during the
            // next full cycle - and, since the ability fires mid-cycle, not during that cycle either.
            var g = Fresh();
            g.BeginCycle();
            g.Rage = 1f;
            g.ActivateAbility();
            Assert.AreEqual(0f, g.Rage, Tol);

            g.ResolveCycleRage(); // end of the activation cycle
            Assert.AreEqual(0f, g.Rage, Tol, "no rage during the cycle the ability was used in");

            g.BeginCycle();
            g.ResolveCycleRage(); // the next full cycle
            Assert.AreEqual(0f, g.Rage, Tol, "no rage during the next full cycle either");

            g.BeginCycle();
            g.ResolveCycleRage(); // the cycle after that charges normally again
            Assert.AreEqual(0.15f, g.Rage, Tol);
        }

        [Test]
        public void SpiritBuff_Gives50PercentMoreSpeed_ForTwoCycles()
        {
            var g = Fresh(GladiatorDef.Brutius); // speed 10, ability Spirit
            g.BeginCycle();
            g.Rage = 1f;
            g.ActivateAbility();

            Assert.AreEqual(15f, g.EffectiveSpeed(), Tol, "activation cycle");
            g.BeginCycle();
            Assert.AreEqual(15f, g.EffectiveSpeed(), Tol, "second buffed cycle");
            g.BeginCycle();
            Assert.AreEqual(10f, g.EffectiveSpeed(), Tol, "buff has expired");
        }

        [Test]
        public void MongooseBuff_Gives2AttacksPerCycle_ForTwoCycles()
        {
            // Regression: AttacksRemainingThisCycle used to be reset to a hard-coded 1 in BeginCycle,
            // which silently dropped the second attack on the buff's second cycle.
            var g = Fresh(GladiatorDef.Hilius);
            g.BeginCycle();
            Assert.AreEqual(1, g.AttacksRemainingThisCycle);

            g.Rage = 1f;
            g.ActivateAbility();
            Assert.AreEqual(2, g.AttacksRemainingThisCycle, "activation cycle");

            g.BeginCycle();
            Assert.AreEqual(2, g.AttacksRemainingThisCycle, "second buffed cycle");

            g.BeginCycle();
            Assert.AreEqual(1, g.AttacksRemainingThisCycle, "back to one attack once the buff expires");
        }
    }
}
