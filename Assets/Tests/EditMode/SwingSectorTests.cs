using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The red wedge on the control: how far a weapon strikes, and how wide.
    ///
    /// Reach on its own made the strike zone a ring, and a ring has no front - a gladiator could be
    /// cut down by somebody who ran past him without ever turning towards him. This is the half of
    /// the rule that makes a heading matter.
    /// </summary>
    public class SwingSectorTests
    {
        private static GladiatorInstance Armed(WeaponKind weapon, Vector2 facing)
        {
            var g = new GladiatorInstance(GladiatorDef.Barbarius) { Pos = Vector2.zero, Facing = facing };
            g.Weapon = weapon;
            return g;
        }

        /// <summary>
        /// A point the given number of degrees off his nose - anticlockwise, with his nose pointing
        /// up - at the given distance.
        /// </summary>
        private static Vector2 At(float degrees, float distance)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(-Mathf.Sin(r), Mathf.Cos(r)) * distance;
        }

        /// <summary>
        /// The three strike zones nest: twin swords inside sword and shield inside the mace, on
        /// both axes at once.
        ///
        /// Sword and shield used to reach exactly as far as the twin swords, which left it with
        /// only the damage it takes to tell it apart from them. It sits between the other two on
        /// every axis now, and this is what says so - written as an ordering rather than as three
        /// numbers, because the numbers have moved four times and the ordering is the design.
        /// </summary>
        [Test]
        public void TheThreeStrikeZonesNestFromTheBladesOutToTheMace()
        {
            var swords = WeaponDef.DualSwords;
            var shield = WeaponDef.SwordAndShield;
            var mace = WeaponDef.TwoHandedMace;

            Assert.Greater(mace.Reach, shield.Reach, "the mace should out-reach the shield");
            Assert.Greater(shield.Reach, swords.Reach, "and the shield should out-reach the blades");

            Assert.Greater(mace.SwingArcDegrees, shield.SwingArcDegrees);
            Assert.Greater(shield.SwingArcDegrees, swords.SwingArcDegrees,
                "the twin swords are the smallest zone in the game, as the mace is the largest");

            // And the middle one really is in the middle rather than a hair off one end: a
            // difference too small to feel is a stat that is not there.
            float span = mace.Reach - swords.Reach;
            Assert.Greater(shield.Reach - swords.Reach, span * 0.25f,
                "the shield is close enough to the blades to be the blades");
            Assert.Greater(mace.Reach - shield.Reach, span * 0.25f,
                "the shield is close enough to the mace to be the mace");
        }

        [Test]
        public void SomebodyStraightAheadAndInsideTheReachIsHit()
        {
            var g = Armed(WeaponKind.SwordAndShield, Vector2.up);
            Assert.IsTrue(g.CanStrikeFrom(g.Pos, At(0f, WeaponDef.SwordAndShield.Reach - 1f)));
        }

        [Test]
        public void SomebodyJustOutOfReachIsNotHitHoweverSquarelyHeIsFaced()
        {
            var g = Armed(WeaponKind.SwordAndShield, Vector2.up);
            Assert.IsFalse(g.CanStrikeFrom(g.Pos, At(0f, WeaponDef.SwordAndShield.Reach + 1f)));
        }

        /// <summary>
        /// The point of the whole change: running past somebody is not the same as attacking them.
        /// </summary>
        [Test]
        public void SomebodyWellInsideTheReachButBehindHimIsNotHit()
        {
            var g = Armed(WeaponKind.SwordAndShield, Vector2.up);
            float half = WeaponDef.SwordAndShield.SwingArcDegrees * 0.5f;

            Assert.IsTrue(g.CanStrikeFrom(g.Pos, At(half - 5f, 50f)), "just inside the sweep");
            Assert.IsFalse(g.CanStrikeFrom(g.Pos, At(half + 5f, 50f)), "just outside it");
            Assert.IsFalse(g.CanStrikeFrom(g.Pos, At(180f, 50f)), "and squarely behind him");
        }

        /// <summary>
        /// A man pressed against his chest is inside every arc there is. Without this a collision -
        /// two gladiators who have run into each other - could resolve as a miss because one of them
        /// happened to be looking over the other's shoulder.
        /// </summary>
        [Test]
        public void ABodyPressedAgainstHimIsInsideEveryArc()
        {
            var g = Armed(WeaponKind.DualSwords, Vector2.up);
            Assert.IsTrue(g.CanStrikeFrom(g.Pos, At(180f, GameConstants.CollideDistance - 1f)));
        }

        /// <summary>
        /// The wedge turns with him rather than with the arena, which is what makes it something the
        /// player steers with the green arc.
        /// </summary>
        [Test]
        public void TheWedgeTurnsWithHim()
        {
            var target = At(0f, 50f); // due north of where he stands

            Assert.IsTrue(Armed(WeaponKind.SwordAndShield, Vector2.up).CanStrikeFrom(Vector2.zero, target));
            Assert.IsFalse(Armed(WeaponKind.SwordAndShield, Vector2.down).CanStrikeFrom(Vector2.zero, target));
        }

        /// <summary>
        /// The mace's wedge covers ground a sword's does not, at an angle both could reach. It is
        /// the same asymmetry reach already had, on the other axis.
        /// </summary>
        [Test]
        public void TheMaceCoversAnAngleTheSwordCannot()
        {
            // Halfway between where the sword stops sweeping and where the mace does, so this keeps
            // measuring the gap between them however wide either one is set.
            float off = (WeaponDef.SwordAndShield.SwingArcDegrees + WeaponDef.TwoHandedMace.SwingArcDegrees)
                        * 0.25f;
            Assert.Greater(off, WeaponDef.SwordAndShield.SwingArcDegrees * 0.5f,
                "the two arcs have converged - there is no angle left that separates them");
            var target = At(off, 50f);

            Assert.IsTrue(Armed(WeaponKind.TwoHandedMace, Vector2.up).CanStrikeFrom(Vector2.zero, target));
            Assert.IsFalse(Armed(WeaponKind.SwordAndShield, Vector2.up).CanStrikeFrom(Vector2.zero, target));
        }
    }
}
