using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The arena is an ellipse, and four separate systems depend on agreeing where its wall is:
    /// the action phase's bounce, the trajectory preview, the danger rings and item spawning.
    /// </summary>
    public class ArenaShapeTests
    {
        [Test]
        public void TheArenaIsLongerUpTheScreenThanAcrossIt()
        {
            Assert.Greater(ArenaShape.RadiusY, ArenaShape.RadiusX,
                "the long axis runs up the screen, which is what fills a portrait frame");
            Assert.AreEqual(GameConstants.ArenaRadius, ArenaShape.RadiusX, 0.001f);
        }

        [Test]
        public void NormalizedDistanceReadsOneOnTheWallInEveryDirection()
        {
            for (int i = 0; i < 36; i++)
            {
                float angle = i * 10f * Mathf.Deg2Rad;
                var onWall = ArenaShape.FromUnitCircle(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
                Assert.AreEqual(1f, ArenaShape.NormalizedDistance(onWall), 0.0001f,
                    $"at {i * 10} degrees the wall should read as exactly 1");
            }

            Assert.AreEqual(0f, ArenaShape.NormalizedDistance(Vector2.zero), 0.0001f);
        }

        [Test]
        public void APointBeyondTheWallIsPulledBackInside()
        {
            for (int i = 0; i < 24; i++)
            {
                float angle = i * 15f * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                var pos = ArenaShape.FromUnitCircle(direction) * 1.4f; // well outside
                var vel = direction * 100f;

                Assert.IsTrue(ArenaShape.Bounce(ref pos, ref vel, GameConstants.GladiatorRadius));
                Assert.LessOrEqual(ArenaShape.NormalizedDistance(pos), 1f,
                    $"at {i * 15} degrees the gladiator ended up outside the wall");
            }
        }

        [Test]
        public void APointInsideIsLeftAlone()
        {
            var pos = new Vector2(10f, 10f);
            var vel = new Vector2(5f, -3f);
            var originalPos = pos;
            var originalVel = vel;

            Assert.IsFalse(ArenaShape.Bounce(ref pos, ref vel, GameConstants.GladiatorRadius));
            Assert.AreEqual(originalPos, pos);
            Assert.AreEqual(originalVel, vel);
        }

        [Test]
        public void ABounceSendsTheBodyBackInwards()
        {
            // Straight out along the short axis: the reflection must reverse it.
            var pos = new Vector2(ArenaShape.RadiusX + 50f, 0f);
            var vel = new Vector2(120f, 0f);
            ArenaShape.Bounce(ref pos, ref vel, GameConstants.GladiatorRadius);
            Assert.Less(vel.x, 0f, "a body that hit the right wall must be travelling left");

            // And off-axis, where reflecting about the position vector instead of the ellipse's
            // own normal would give a visibly wrong angle.
            var diagonal = ArenaShape.FromUnitCircle(new Vector2(0.707f, 0.707f)) * 1.2f;
            var diagonalVel = diagonal.normalized * 120f;
            ArenaShape.Bounce(ref diagonal, ref diagonalVel, GameConstants.GladiatorRadius);
            Assert.Less(Vector2.Dot(diagonalVel, diagonal), 0f,
                "after bouncing, the body should be heading back towards the middle");
        }

        [Test]
        public void ARunningGladiatorNeverLeavesTheArena()
        {
            // The real loop: many substeps at speed, in a direction that keeps hitting the wall.
            var pos = Vector2.zero;
            var vel = new Vector2(1f, 0.6f).normalized * (GameConstants.SpeedScale * 20f);

            for (int i = 0; i < 4000; i++)
            {
                pos += vel * (1f / 360f);
                ArenaShape.Bounce(ref pos, ref vel, GameConstants.GladiatorRadius);
                Assert.LessOrEqual(ArenaShape.NormalizedDistance(pos), 1.0001f,
                    $"escaped the arena on step {i} at {pos}");
            }
        }

        [Test]
        public void TheTrajectoryPreviewStaysInsideTheArenaToo()
        {
            var g = new GladiatorInstance(GladiatorDef.Hilius); // the fastest one
            g.Pos = Vector2.zero;

            for (int i = 0; i < 16; i++)
            {
                float angle = i * 22.5f * Mathf.Deg2Rad;
                var aim = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var points = GameManager.ComputeTrajectoryPreview(g, aim, 1f);

                foreach (var p in points)
                    Assert.LessOrEqual(ArenaShape.NormalizedDistance(p), 1.0001f,
                        $"the preview promised a point outside the wall at {p}");
            }
        }
    }
}
