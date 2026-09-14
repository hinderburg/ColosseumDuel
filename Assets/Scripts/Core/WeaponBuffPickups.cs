using UnityEngine;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// The weapon blessing: laid on the sand every third cycle and taken up by whoever runs over it
    /// first. A blessed weapon hits WeaponBuffDamageMult times as hard for the three cycles after
    /// the one it was taken in - see GladiatorInstance.BlessWeapon.
    ///
    /// It replaced the gilded weapons that lay on the floor all round, and the traps with them: one
    /// thing worth running for, arriving on a beat, rather than a floor covered in things.
    ///
    /// One at a time. It is laid only if the last one has been taken, so a blessing nobody went for
    /// stays where it is rather than a second one joining it somewhere else.
    /// </summary>
    public sealed class WeaponBuffPickups
    {
        /// <summary>How far to either side of the middle of the two it can land, at most.</summary>
        private const float MaxSpread = 150f;

        /// <summary>Clear ground it needs round it - a little over its own size.</summary>
        private const float Clearance = GameConstants.BuffRadius * 2f;

        private readonly System.Random _rng;
        private readonly ObstacleField _obstacles;

        public WeaponBuffPickups(System.Random rng, ObstacleField obstacles = null)
        {
            _rng = rng ?? new System.Random();
            _obstacles = obstacles ?? ObstacleField.Empty;
        }

        /// <summary>Where the blessing lies, or null when there is none on the sand.</summary>
        public Vector2? Position { get; private set; }

        /// <summary>Whether this cycle is one a blessing is laid on.</summary>
        public static bool IsDueOn(int cycle)
            => cycle > 0 && cycle % GameConstants.WeaponBuffEveryCycles == 0;

        public void Clear() => Position = null;

        /// <summary>Puts it exactly here. For the tutorial, which lays it on the player's path.</summary>
        public void PlaceAt(Vector2 position) => Position = position;

        /// <summary>
        /// Lays one down between two gladiators, unless one is lying there already.
        ///
        /// On the line across the middle of them - every point of which is as far from one as from
        /// the other - at a random distance along it. Random anywhere on the sand would hand the
        /// blessing to whoever happened to be standing nearer the spot.
        /// </summary>
        public bool SpawnBetween(Vector2 a, Vector2 b)
        {
            if (Position.HasValue) return false;

            var middle = (a + b) * 0.5f;
            var between = b - a;
            var across = between.sqrMagnitude > 0.0001f
                ? new Vector2(-between.y, between.x).normalized
                : Vector2.right;
            float spread = Mathf.Min(between.magnitude * 0.35f, MaxSpread);

            // Drawn again, closer in each time, when it lands on stone or past the wall - so it stays
            // on the fair line rather than being shoved off it.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var candidate = middle + across * (((float)_rng.NextDouble() * 2f - 1f) * spread);
                if (_obstacles.IsFree(candidate, Clearance))
                {
                    Position = candidate;
                    return true;
                }
                spread *= 0.75f;
            }

            Position = _obstacles.NearestFree(middle, Clearance);
            return true;
        }

        /// <summary>
        /// Gives him the blessing if he is standing on it, and takes it off the sand. One already
        /// blessed takes it too and starts his count again - better that than a blessing nobody could
        /// take while the other man's weapon is still glowing.
        /// </summary>
        public bool TryPickup(GladiatorInstance g)
        {
            if (!Position.HasValue || g == null || !g.Alive) return false;
            if (Vector2.Distance(g.Pos, Position.Value) > GameConstants.PickupDistance) return false;

            g.BlessWeapon();
            Position = null;
            return true;
        }
    }
}
