using UnityEngine;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// The arena's footprint: an ellipse, longer along the simulation's Y axis - which the camera
    /// looks down, so the long axis runs up the screen and fills a portrait frame.
    ///
    /// Everything that used to test <c>pos.magnitude</c> against a single radius goes through here
    /// instead. Concentrating the geometry in one place is the point: wall bounces, the danger
    /// rings, item spawns and the trajectory preview all have to agree on where the wall is, and
    /// four separate copies of the ellipse equation would not stay in agreement for long.
    /// </summary>
    public static class ArenaShape
    {
        /// <summary>Semi-axis across the screen.</summary>
        public static float RadiusX => GameConstants.ArenaRadius;

        /// <summary>Semi-axis up the screen - the long one.</summary>
        public static float RadiusY => GameConstants.ArenaRadius * GameConstants.ArenaElongation;

        /// <summary>
        /// Distance from the centre measured in wall units: 0 at the centre, 1 exactly on the wall,
        /// more than 1 outside. This is what the danger rings are expressed in, so a ring at 0.75
        /// is the same fraction of the way out in every direction rather than a circle inside an
        /// oval.
        /// </summary>
        public static float NormalizedDistance(Vector2 p)
            => Mathf.Sqrt(p.x * p.x / (RadiusX * RadiusX) + p.y * p.y / (RadiusY * RadiusY));

        /// <summary>Maps a point on the unit circle onto the arena's boundary.</summary>
        public static Vector2 FromUnitCircle(Vector2 unitCirclePoint)
            => new Vector2(unitCirclePoint.x * RadiusX, unitCirclePoint.y * RadiusY);

        public static bool Contains(Vector2 p, float bodyRadius = 0f)
            => NormalizedDistanceInset(p, bodyRadius) <= 1f;

        /// <summary>
        /// Keeps a moving body inside the wall, reflecting its velocity if it crossed.
        /// Returns true if a bounce happened, so callers can react to it.
        /// </summary>
        public static bool Bounce(ref Vector2 pos, ref Vector2 vel, float bodyRadius)
        {
            float a = Mathf.Max(RadiusX - bodyRadius, 0.001f);
            float b = Mathf.Max(RadiusY - bodyRadius, 0.001f);

            float d = Mathf.Sqrt(pos.x * pos.x / (a * a) + pos.y * pos.y / (b * b));
            if (d <= 1f) return false;

            // Pull the body back onto the wall along the ray from the centre. Not quite the nearest
            // point on an ellipse - that needs an iterative solve - but it is stable, cheap, and at
            // the overshoot of a single substep the difference is far below anything visible.
            pos /= d;

            // Outward normal of an ellipse is the gradient of (x/a)^2 + (y/b)^2, not the position.
            // Reflecting about the position vector instead is the mistake that makes a bounce off
            // an oval look subtly wrong everywhere except on the two axes.
            var normal = new Vector2(pos.x / (a * a), pos.y / (b * b));
            if (normal.sqrMagnitude < 1e-12f) return false;
            normal.Normalize();

            vel -= 2f * Vector2.Dot(vel, normal) * normal;
            return true;
        }

        /// <summary>
        /// Angles that cut the ellipse into <paramref name="count"/> arcs of equal length.
        ///
        /// Stepping the angle in equal increments instead is the obvious thing and the wrong one:
        /// on an oval twice as long as it is wide, equal angles put the points shoulder to shoulder
        /// at the pointed ends and strand them along the flanks. Everything laid out around the
        /// wall - blocks, posts, torches - reads that unevenness immediately, so they all share
        /// this one walk.
        /// </summary>
        public static float[] EvenlySpacedAngles(int count, float radiusX, float radiusY)
        {
            var angles = new float[Mathf.Max(count, 0)];
            if (angles.Length == 0) return angles;

            // Cumulative arc length by sampling. An ellipse's perimeter has no closed form, and at
            // this sample count the error is orders of magnitude below the width of a wall block.
            const int samples = 2048;
            var arc = new float[samples + 1];
            for (int i = 1; i <= samples; i++)
            {
                float t = (i - 0.5f) / samples * Mathf.PI * 2f;
                float dx = -Mathf.Sin(t) * radiusX;
                float dy = Mathf.Cos(t) * radiusY;
                arc[i] = arc[i - 1] + Mathf.Sqrt(dx * dx + dy * dy) * (Mathf.PI * 2f / samples);
            }

            float perimeter = arc[samples];
            int cursor = 0;
            for (int i = 0; i < angles.Length; i++)
            {
                float target = perimeter * i / angles.Length;
                while (cursor < samples && arc[cursor + 1] < target) cursor++;

                float span = arc[cursor + 1] - arc[cursor];
                float within = span > 1e-6f ? (target - arc[cursor]) / span : 0f;
                angles[i] = (cursor + within) / samples * Mathf.PI * 2f;
            }

            return angles;
        }

        /// <summary>Total length of the wall, for deciding how many pieces it takes to build one.</summary>
        public static float Perimeter(float radiusX, float radiusY)
        {
            const int samples = 2048;
            float total = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (i + 0.5f) / samples * Mathf.PI * 2f;
                float dx = -Mathf.Sin(t) * radiusX;
                float dy = Mathf.Cos(t) * radiusY;
                total += Mathf.Sqrt(dx * dx + dy * dy) * (Mathf.PI * 2f / samples);
            }
            return total;
        }

        /// <summary>
        /// Outward unit normal at a parametric angle - the gradient of the ellipse, not the
        /// direction from the centre. They differ everywhere except on the two axes, and a wall
        /// block turned to face the centre instead of facing out sits visibly askew.
        /// </summary>
        public static Vector2 OutwardNormal(float t, float radiusX, float radiusY)
        {
            var normal = new Vector2(Mathf.Cos(t) / Mathf.Max(radiusX, 1e-4f),
                                     Mathf.Sin(t) / Mathf.Max(radiusY, 1e-4f));
            return normal.sqrMagnitude < 1e-12f ? Vector2.right : normal.normalized;
        }

        private static float NormalizedDistanceInset(Vector2 p, float inset)
        {
            float a = Mathf.Max(RadiusX - inset, 0.001f);
            float b = Mathf.Max(RadiusY - inset, 0.001f);
            return Mathf.Sqrt(p.x * p.x / (a * a) + p.y * p.y / (b * b));
        }
    }
}
