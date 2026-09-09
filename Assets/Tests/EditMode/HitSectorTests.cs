using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Where a blow lands on a man, and what that is worth.
    ///
    /// A gladiator faces the way he is running, so which of him is exposed is decided by the order
    /// the player gave - and getting behind somebody is worth almost half a blow again. These pin
    /// both halves: the geometry that decides the sector, and the price of each one.
    /// </summary>
    public class HitSectorTests
    {
        private static GladiatorInstance Facing(Vector2 facing)
            => new GladiatorInstance(GladiatorDef.Barbarius) { Pos = Vector2.zero, Facing = facing };

        /// <summary>A point the given number of degrees round from his nose, at arm's length.</summary>
        private static Vector2 At(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * 50f;
        }

        [Test]
        public void TheFrontIsFortyFiveEitherSideOfHisNose()
        {
            var g = Facing(Vector2.up);

            Assert.AreEqual(HitSector.Front, g.SectorHitFrom(At(0f)), "straight ahead");
            Assert.AreEqual(HitSector.Front, g.SectorHitFrom(At(40f)));
            Assert.AreEqual(HitSector.Front, g.SectorHitFrom(At(-40f)));

            // And just outside it is not.
            Assert.AreEqual(HitSector.Side, g.SectorHitFrom(At(50f)));
            Assert.AreEqual(HitSector.Side, g.SectorHitFrom(At(-50f)));
        }

        [Test]
        public void TheBackIsFortyFiveEitherSideOfHisSpine()
        {
            var g = Facing(Vector2.up);

            Assert.AreEqual(HitSector.Back, g.SectorHitFrom(At(180f)), "straight behind");
            Assert.AreEqual(HitSector.Back, g.SectorHitFrom(At(140f)));
            Assert.AreEqual(HitSector.Back, g.SectorHitFrom(At(-140f)));

            Assert.AreEqual(HitSector.Side, g.SectorHitFrom(At(130f)));
            Assert.AreEqual(HitSector.Side, g.SectorHitFrom(At(-130f)));
        }

        [Test]
        public void TheFlanksAreWhatIsLeft()
        {
            var g = Facing(Vector2.up);

            Assert.AreEqual(HitSector.Side, g.SectorHitFrom(At(90f)), "square on his right");
            Assert.AreEqual(HitSector.Side, g.SectorHitFrom(At(-90f)), "square on his left");
        }

        /// <summary>
        /// The sectors turn with him. Otherwise this is a fact about the arena rather than about a
        /// man, and the whole point is that he carries it around with him.
        /// </summary>
        [Test]
        public void TheSectorsTurnWithTheManHeIsNotBoltedToTheArena()
        {
            var attackerAt = At(0f);   // due north of him

            Assert.AreEqual(HitSector.Front, Facing(Vector2.up).SectorHitFrom(attackerAt));
            Assert.AreEqual(HitSector.Back, Facing(Vector2.down).SectorHitFrom(attackerAt));
            Assert.AreEqual(HitSector.Side, Facing(Vector2.right).SectorHitFrom(attackerAt));
            Assert.AreEqual(HitSector.Side, Facing(Vector2.left).SectorHitFrom(attackerAt));
        }

        [Test]
        public void ABlowFromBehindIsWorthFortyPercentMore_AndFromTheFlankTwenty()
        {
            Assert.AreEqual(1f, CombatResolver.SectorMultiplier(HitSector.Front), 0.0001f);
            Assert.AreEqual(1.2f, CombatResolver.SectorMultiplier(HitSector.Side), 0.0001f);
            Assert.AreEqual(1.4f, CombatResolver.SectorMultiplier(HitSector.Back), 0.0001f);
        }

        /// <summary>
        /// And the bonus reaches the health, rather than being computed and dropped.
        ///
        /// Measured through the resolver against the same pair of fighters twice - the only thing
        /// changed between the two is which way the man being hit is looking.
        /// </summary>
        [Test]
        public void TheBonusReachesTheHealthNotJustTheArithmetic()
        {
            float FromBehind(Vector2 defenderFacing)
            {
                var attacker = new GladiatorInstance(GladiatorDef.Barbarius) { Pos = new Vector2(0f, -40f) };
                var defender = new GladiatorInstance(GladiatorDef.Brutius)
                {
                    Pos = Vector2.zero,
                    Facing = defenderFacing,
                };
                return CombatResolver.DealDamage(attacker, defender);
            }

            // The attacker stands south of him. Looking south is nose to nose; looking north is a
            // back turned.
            float faced = FromBehind(Vector2.down);
            float behind = FromBehind(Vector2.up);
            float flanked = FromBehind(Vector2.right);

            Assert.Greater(faced, 0f, "the plain blow landed nothing, so nothing here means anything");
            Assert.AreEqual(GameConstants.BackAttackMult, behind / faced, 0.001f,
                $"a blow from behind dealt {behind:0.##} against {faced:0.##} to the face");
            Assert.AreEqual(GameConstants.FlankAttackMult, flanked / faced, 0.001f,
                $"a blow to the flank dealt {flanked:0.##} against {faced:0.##} to the face");
        }
    }
}
