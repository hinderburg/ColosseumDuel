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
        public void PiecesLaidRoundTheWallAreEquallySpacedAlongIt()
        {
            // Regression guard for the whole decor layout. Stepping the angle evenly instead is the
            // obvious thing and the wrong one on an oval: the pieces crowd together at the pointed
            // ends and spread out along the flanks.
            const int count = 40;
            float radiusX = 8f;
            float radiusY = 16f;

            var angles = ArenaShape.EvenlySpacedAngles(count, radiusX, radiusY);
            Assert.AreEqual(count, angles.Length);

            var gaps = new float[count];
            for (int i = 0; i < count; i++)
            {
                var here = Point(angles[i], radiusX, radiusY);
                var next = Point(angles[(i + 1) % count], radiusX, radiusY);
                gaps[i] = (next - here).magnitude;
            }

            float shortest = Mathf.Min(gaps);
            float longest = Mathf.Max(gaps);
            Assert.Less(longest / shortest, 1.05f,
                $"the widest gap between pieces is {longest:0.000} and the tightest {shortest:0.000}");
        }

        [Test]
        public void ThePerimeterMatchesTheSpacingWalk()
        {
            // A circle is the one case with a closed form, so it is the one case that can be checked
            // against something other than itself.
            Assert.AreEqual(2f * Mathf.PI * 5f, ArenaShape.Perimeter(5f, 5f), 0.01f);
        }

        [Test]
        public void TheOutwardNormalFollowsTheGradientNotThePosition()
        {
            // On the flank of a stretched ellipse the two differ by a wide margin, and a wall block
            // turned by the wrong one sits visibly askew.
            float t = 45f * Mathf.Deg2Rad;
            var normal = ArenaShape.OutwardNormal(t, 8f, 16f);
            var fromCentre = Point(t, 8f, 16f).normalized;

            Assert.AreEqual(1f, normal.magnitude, 1e-4f);
            Assert.Less(Vector2.Dot(normal, fromCentre), 0.99f,
                "on the flank the normal should not coincide with the direction from the centre");

            // Where they do coincide - on the axes - it must be exact.
            Assert.AreEqual(0f, Vector2.Angle(ArenaShape.OutwardNormal(0f, 8f, 16f), Vector2.right), 0.01f);
        }

        private static Vector2 Point(float t, float radiusX, float radiusY)
            => new Vector2(Mathf.Cos(t) * radiusX, Mathf.Sin(t) * radiusY);

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
