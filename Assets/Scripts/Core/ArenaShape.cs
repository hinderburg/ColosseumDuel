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

        private static float NormalizedDistanceInset(Vector2 p, float inset)
        {
            float a = Mathf.Max(RadiusX - inset, 0.001f);
            float b = Mathf.Max(RadiusY - inset, 0.001f);
            return Mathf.Sqrt(p.x * p.x / (a * a) + p.y * p.y / (b * b));
        }
    }
}
