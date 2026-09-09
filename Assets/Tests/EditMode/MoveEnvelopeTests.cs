using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The arc a gladiator may be ordered onto, and the wedge he can strike inside.
    ///
    /// Both are the same idea from the two ends: a heading is now a thing he carries between
    /// cycles, so choosing where to run is choosing what he can hit and what can be done to him.
    /// </summary>
    public class MoveEnvelopeTests
    {
        private static GladiatorInstance Standing(GladiatorDef def, Vector2 facing)
            => new GladiatorInstance(def) { Pos = Vector2.zero, Facing = facing };

        [Test]
        public void TheFasterHeIsTheFurtherHeReachesAndTheWiderHeTurns()
        {
            var slow = MoveEnvelope.For(Standing(GladiatorDef.Brutius, Vector2.up));   // speed 10
            var quick = MoveEnvelope.For(Standing(GladiatorDef.Hilius, Vector2.up));   // speed 20

            Assert.Greater(quick.OuterRadius, slow.OuterRadius,
                "the quick one covers more ground in a phase");
            Assert.Greater(quick.ArcDegrees, slow.ArcDegrees,
                "and swings wider round with it - both dimensions run the same way");

            // Not a rounding difference. Speed is a straight advantage at moving and is paid for on
            // the other stats; a difference too small to see would make it neither.
            Assert.Greater(quick.ArcDegrees - slow.ArcDegrees, 40f);
        }

        /// <summary>The far edge is exactly the run he is about to make, not a number of its own.</summary>
        [Test]
        public void TheFarEdgeIsOneDash()
        {
            var g = Standing(GladiatorDef.Barbarius, Vector2.up);
            Assert.AreEqual(g.DashReach(), MoveEnvelope.For(g).OuterRadius, 0.01f);
        }

        /// <summary>
        /// The speed ability lengthens the arc and narrows it, during planning - before it has
        /// fired. Drawn off the unbuffed speed the zone would promise a run he is not going to make.
        /// </summary>
        [Test]
        public void AnArmedSpeedAbilityIsAlreadyInTheZone()
        {
            var g = Standing(GladiatorDef.Brutius, Vector2.up);

            // Asserted, not assumed. Written as an assumption it quietly marked itself inconclusive
            // when it was pointed at the wrong archetype, and an inconclusive test is a test that
            // proves nothing while looking like it ran.
            Assert.AreEqual(AbilityKey.Spirit, g.Def.Ability, "this test is about the speed ability");

            var before = MoveEnvelope.For(g);
            g.Rage = GameConstants.RageMax;
            g.AbilityArmed = true;

            var after = MoveEnvelope.For(g);
            Assert.Greater(after.OuterRadius, before.OuterRadius * 1.4f);
            Assert.Greater(after.ArcDegrees, before.ArcDegrees,
                "and opens the arc with it, rather than trading one against the other");
        }

        [Test]
        public void APointInsideTheArcIsLeftAlone()
        {
            var envelope = MoveEnvelope.For(Standing(GladiatorDef.Barbarius, Vector2.up));
            var asked = MoveEnvelope.Rotate(Vector2.up, envelope.HalfAngleDegrees * 0.5f)
                        * (envelope.OuterRadius * 0.5f);

            Assert.That(Vector2.Distance(envelope.Clamp(asked), asked), Is.LessThan(0.01f));
            Assert.IsTrue(envelope.Contains(asked));
        }

        /// <summary>
        /// An order given behind him is answered with the sharpest turn he has, at the distance
        /// asked for - not thrown away, and not straightened out to dead ahead.
        /// </summary>
        [Test]
        public void AnOrderBehindHimBecomesTheSharpestTurnHeHas()
        {
            var envelope = MoveEnvelope.For(Standing(GladiatorDef.Hilius, Vector2.up));
            var behind = new Vector2(0f, -40f);

            var clamped = envelope.Clamp(behind);

            Assert.AreEqual(envelope.HalfAngleDegrees, Vector2.Angle(Vector2.up, clamped), 0.01f,
                "he turns as far as he is able and no further");
            Assert.AreEqual(40f, clamped.magnitude, 0.01f, "and still runs as far as he was told to");
            Assert.IsTrue(envelope.Contains(clamped));
        }

        [Test]
        public void AFarOffOrderKeepsItsDirectionAndLosesOnlyItsLength()
        {
            var envelope = MoveEnvelope.For(Standing(GladiatorDef.Brutius, Vector2.up));
            var asked = MoveEnvelope.Rotate(Vector2.up, 20f) * (envelope.OuterRadius * 10f);

            var clamped = envelope.Clamp(asked);

            Assert.AreEqual(20f, Vector2.Angle(Vector2.up, clamped), 0.01f);
            Assert.AreEqual(envelope.ReachAtTurn(20f), clamped.magnitude, 0.01f,
                "shortened to the far edge as it stands at that much turn, not to a flat radius");
        }

        /// <summary>
        /// Rotate and SignedAngle have to agree about which way round is positive, or the clamp
        /// answers a turn to the left with a turn to the right - which reads on screen as the
        /// control ignoring the tap entirely.
        /// </summary>
        [Test]
        public void TurningByTheAngleBetweenTwoHeadingsLandsOnTheSecond()
        {
            var from = new Vector2(0.6f, 0.8f);
            var to = new Vector2(-0.8f, 0.6f);

            var turned = MoveEnvelope.Rotate(from, Vector2.SignedAngle(from, to));

            Assert.AreEqual(0f, Vector2.Angle(turned, to), 0.01f);
        }
    }
}
