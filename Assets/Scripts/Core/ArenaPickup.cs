using System.Collections.Generic;
using UnityEngine;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// One of the things laid on the sand to be run over: the weapon blessing, the apple or the horn.
    /// Each is laid on every third round and taken up by whoever runs over it first.
    ///
    ///  - The blessing: his weapon hits WeaponBuffDamageMult times as hard for the three rounds
    ///    after the one it was taken in - see GladiatorInstance.BlessWeapon.
    ///  - The apple: a third of his health back, at once - see GladiatorInstance.Heal.
    ///  - The horn: rage straight onto the meter - see GladiatorInstance.BlowHorn.
    ///
    /// Each of the three is its own: laid only if the last one of its kind has been taken, so one
    /// nobody went for stays where it is rather than a second joining it - and whether one is laid
    /// says nothing about whether the others are. All three can be lying there at once.
    ///
    /// Laid beside a column, not in the open: the columns are the landmarks of the arena, a thing
    /// lying by one is a thing somebody has to decide to go round a column for, and they are spread
    /// so evenly that there is always one about as far from both men as from each other.
    /// </summary>
    public sealed class ArenaPickup
    {
        /// <summary>How far to either side of the middle of the two it can land, at most. The fallback only.</summary>
        private const float MaxSpread = 150f;

        /// <summary>Clear ground it needs round it - a little over its own size.</summary>
        private const float Clearance = GameConstants.BuffRadius * 2f;

        /// <summary>
        /// How far from a column's face it lies: clear of the stone, with room for a man to run over
        /// it without having to scrape along the column to do it.
        /// </summary>
        private const float ColumnGap = GameConstants.BuffRadius * 1.5f;

        /// <summary>How many places round each column are looked at.</summary>
        private const int SpotsPerColumn = 8;

        /// <summary>
        /// How many of the fairest places the one it goes to is drawn from. The fairest alone would
        /// lay it on the same spot every time the two stood the same way; a few of them keep it moving
        /// round the arena without handing it to whoever stands nearer.
        /// </summary>
        private const int FairestDrawn = 4;

        /// <summary>Not so near a man that he takes it up the moment it appears.</summary>
        public const float MinFromGladiator = GameConstants.PickupDistance * 1.5f;

        /// <summary>Not on top of another: two taken in one step would be one pickup with two names.</summary>
        public const float MinApart = GameConstants.PickupDistance * 2f;

        private readonly System.Random _rng;
        private readonly ObstacleField _obstacles;

        public readonly PickupKind Kind;

        public ArenaPickup(System.Random rng, ObstacleField obstacles = null, PickupKind kind = PickupKind.Blessing)
        {
            _rng = rng ?? new System.Random();
            _obstacles = obstacles ?? ObstacleField.Empty;
            Kind = kind;
        }

        /// <summary>Where it lies, or null when there is none on the sand.</summary>
        public Vector2? Position { get; private set; }

        /// <summary>Whether this round is one they are laid on.</summary>
        public static bool IsDueOn(int round)
            => round > 0 && round % GameConstants.WeaponBuffEveryRounds == 0;

        public void Clear() => Position = null;

        /// <summary>Puts it exactly here. For the tutorial, which lays the blessing on the player's path.</summary>
        public void PlaceAt(Vector2 position) => Position = position;

        /// <summary>
        /// Lays one down beside a column, unless one of this kind is lying there already.
        ///
        /// Every place round the near side of every column is looked at, and those that will not do are dropped: on
        /// stone or past the wall, in a ring of spikes that is burning or about to, close enough to
        /// either man that he would take it without meaning to, or on top of something already lying
        /// there. Of the rest, the ones nearest to as far from one man as from the other are kept,
        /// and it goes to one of those at random.
        ///
        /// With nowhere by a column that will do - the rings closed right in - it goes back to the old
        /// rule, on the line across the middle of the two.
        /// </summary>
        public bool SpawnByColumn(Vector2 a, Vector2 b, IEnumerable<Vector2> taken, int round)
        {
            if (Position.HasValue) return false;

            var others = new List<Vector2>(taken ?? new Vector2[0]);
            var spots = new List<KeyValuePair<float, Vector2>>();
            foreach (var column in _obstacles.Obstacles)
            {
                if (column.Kind != ObstacleKind.Column) continue;

                float distance = column.Size + ColumnGap + GameConstants.BuffRadius;
                for (int i = 0; i < SpotsPerColumn; i++)
                {
                    float angle = (i + 0.5f) / SpotsPerColumn * Mathf.PI * 2f;

                    // Only the near side of a column, the half facing the camera. The camera looks up
                    // the arena from the player's end, and a thing laid behind a column had the stone
                    // standing between it and the eye.
                    if (Mathf.Sin(angle) > 0f) continue;

                    var spot = column.Centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    if (!Fits(spot, a, b, others, round)) continue;

                    float unfair = Mathf.Abs(Vector2.Distance(spot, a) - Vector2.Distance(spot, b));
                    spots.Add(new KeyValuePair<float, Vector2>(unfair, spot));
                }
            }

            if (spots.Count == 0) return SpawnBetween(a, b);

            spots.Sort((x, y) => x.Key.CompareTo(y.Key));
            Position = spots[_rng.Next(Mathf.Min(FairestDrawn, spots.Count))].Value;
            return true;
        }

        private bool Fits(Vector2 spot, Vector2 a, Vector2 b, List<Vector2> others, int round)
        {
            if (!_obstacles.IsFree(spot, Clearance)) return false;
            if (HazardSystem.IsInActiveHazard(spot, round) || HazardSystem.IsInActiveHazard(spot, round + 1)) return false;
            if (Vector2.Distance(spot, a) < MinFromGladiator || Vector2.Distance(spot, b) < MinFromGladiator) return false;
            foreach (var other in others)
                if (Vector2.Distance(spot, other) < MinApart) return false;
            return true;
        }

        /// <summary>
        /// Lays one down between two gladiators, unless one is lying there already.
        ///
        /// On the line across the middle of them - every point of which is as far from one as from
        /// the other - at a random distance along it. What the blessing did before the columns; kept
        /// for when there is no column to lay it by.
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

        public bool TryPickup(GladiatorInstance g) => TryPickup(g, out _);

        /// <summary>
        /// Gives him what it is if he is standing on it, and takes it off the sand. <paramref name="amount"/>
        /// is what it gave: the health the apple put back, the rage the horn put on; nothing for the
        /// blessing, which is not an amount.
        ///
        /// Taken whether he needs it or not - a man at full health eats the apple, and one already
        /// blessed starts his count again. Better that than a thing on the sand only one of the two
        /// can take, which the other could never clear out of his way.
        /// </summary>
        public bool TryPickup(GladiatorInstance g, out float amount)
        {
            amount = 0f;
            if (!Position.HasValue || g == null || !g.Alive) return false;
            if (Vector2.Distance(g.Pos, Position.Value) > GameConstants.PickupDistance) return false;

            switch (Kind)
            {
                case PickupKind.Apple:
                    amount = g.Heal(g.Def.MaxHp * (g.HasBoon(BoonKey.Quartermaster) ? GameConstants.QuartermasterAppleHealFraction : GameConstants.AppleHealFraction));
                    break;
                case PickupKind.Horn:
                    amount = g.BlowHorn(g.HasBoon(BoonKey.Quartermaster) ? GameConstants.QuartermasterHornRage : GameConstants.HornRage);
                    break;
                default:
                    g.BlessWeapon(g.HasBoon(BoonKey.Quartermaster) ? GameConstants.QuartermasterBlessingExtraRounds : 0);
                    break;
            }

            Position = null;
            return true;
        }
    }
}
