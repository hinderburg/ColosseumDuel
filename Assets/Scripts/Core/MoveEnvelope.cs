using UnityEngine;

namespace ColosseumDuel.Core
{
    /// <summary>
    /// The ground a gladiator may be ordered onto this cycle: an arc in front of him, from his own
    /// feet out to as far as one dash carries.
    ///
    /// He used to be orderable to any point on the sand, which made a turn free - a man at full
    /// speed could reverse into his own footprints, and the direction he was facing meant nothing
    /// because he could be facing anywhere by the next phase. An arc makes the heading he already
    /// has a thing he is carrying: turning costs the ground he could have covered going the other
    /// way, and the sectors on his body (see GladiatorInstance.SectorHitFrom) become something an
    /// opponent can actually work around behind.
    ///
    /// Both of its dimensions come off his speed and run the same way. A quick gladiator reaches
    /// further and turns wider; a heavy one covers less ground and is stuck harder with the
    /// heading he set off on - the long, wide envelope and the short, narrow one are the same
    /// shape read at two speeds, and the ability that makes him faster opens both at once.
    /// </summary>
    public readonly struct MoveEnvelope
    {
        /// <summary>Where he is standing: the point the arc is struck from.</summary>
        public readonly Vector2 Origin;

        /// <summary>Unit vector down the middle of the arc - the way he is already facing.</summary>
        public readonly Vector2 Facing;

        /// <summary>
        /// The hole in the middle: his own body. Nothing is forbidden inside it - it is drawn empty
        /// because that is where he is standing, not because he may not go there. An order that
        /// lands in it is too short to be a run and falls through to the guard, exactly as a tap on
        /// his own feet always has.
        /// </summary>
        public readonly float InnerRadius;

        /// <summary>How far one dash carries him, ability included.</summary>
        public readonly float OuterRadius;

        /// <summary>Half the arc's span, in degrees, either side of <see cref="Facing"/>.</summary>
        public readonly float HalfAngleDegrees;

        public float ArcDegrees => HalfAngleDegrees * 2f;

        public MoveEnvelope(Vector2 origin, Vector2 facing, float innerRadius, float outerRadius,
            float halfAngleDegrees)
        {
            Origin = origin;
            Facing = facing.sqrMagnitude > 0.000001f ? facing.normalized : Vector2.up;
            InnerRadius = innerRadius;
            OuterRadius = outerRadius;
            HalfAngleDegrees = halfAngleDegrees;
        }

        /// <summary>
        /// The envelope this gladiator has for the cycle about to start.
        ///
        /// Built off PlannedSpeed rather than EffectiveSpeed, so an armed ability is already in it:
        /// the buff fires at the top of the action phase, and a zone drawn during planning that did
        /// not know about it would promise the run he is not going to make.
        /// </summary>
        public static MoveEnvelope For(GladiatorInstance g)
        {
            if (g == null) return default;

            float speed = g.PlannedSpeed();
            float reach = speed * GameConstants.SpeedScale * GameConstants.ActionTime;

            return new MoveEnvelope(g.Pos, g.Facing, GameConstants.GladiatorRadius, reach,
                ArcDegreesFor(speed) * 0.5f);
        }

        /// <summary>
        /// How wide the arc is at a given speed, in degrees.
        ///
        /// Interpolated across the speeds the three archetypes actually have rather than against
        /// numbers written here, so the ends of the scale stay the slowest and fastest gladiator in
        /// the game if either is ever retuned. Anything quicker than the fastest - which is what the
        /// speed ability produces - sits at the cap rather than off the end of it.
        /// </summary>
        public static float ArcDegreesFor(float speed)
        {
            float slowest = float.MaxValue, fastest = float.MinValue;
            foreach (var def in GladiatorDef.All)
            {
                slowest = Mathf.Min(slowest, def.Speed);
                fastest = Mathf.Max(fastest, def.Speed);
            }

            float t = Mathf.InverseLerp(slowest, fastest, speed);
            return Mathf.Lerp(GameConstants.MoveArcSlowestDegrees, GameConstants.MoveArcFastestDegrees, t);
        }

        /// <summary>
        /// How much of one dash it costs to run to a point this far off his nose.
        ///
        /// A gladiator leaves along the way he is already facing and bends round onto the point -
        /// he does not pivot on the spot and set off - so the ground he covers getting somewhere is
        /// the arc, not the straight line to it, and the arc is always the longer of the two. At
        /// forty-five degrees off the nose it costs a tenth more than the chord; at sixty-five, a
        /// quarter more.
        ///
        /// The ratio is theta over sine theta, and it is one at dead ahead. Written this way so it
        /// stays a curve through zero rather than a special case with a hole in it.
        /// </summary>
        public static float ArcCostFactor(float turnDegrees)
        {
            float theta = Mathf.Abs(turnDegrees) * Mathf.Deg2Rad;
            if (theta < 0.0001f) return 1f;
            return theta / Mathf.Sin(theta);
        }

        /// <summary>
        /// How far out the far edge sits at this much turn, in virtual units.
        ///
        /// Not a constant radius, which is what makes the zone a leaf rather than a plain wedge: a
        /// dash spent bending round covers less ground than a dash spent going straight, so the
        /// edge draws in towards the sides. Drawn as a plain wedge it would offer corners he cannot
        /// actually get to - the one thing the zone exists to stop the player believing.
        /// </summary>
        public float ReachAtTurn(float turnDegrees) => OuterRadius / ArcCostFactor(turnDegrees);

        /// <summary>
        /// The turn from his nose onto a point, in degrees, signed anticlockwise. Clamped to the
        /// sharpest he has, so a point behind him answers with the sharpest turn rather than a
        /// refusal.
        /// </summary>
        public float TurnOnto(Vector2 target)
        {
            var offset = target - Origin;
            if (offset.sqrMagnitude < 0.000001f) return 0f;
            return Mathf.Clamp(Vector2.SignedAngle(Facing, offset),
                -HalfAngleDegrees, HalfAngleDegrees);
        }

        /// <summary>Whether a point is somewhere he could be ordered to run.</summary>
        public bool Contains(Vector2 point)
        {
            var offset = point - Origin;
            if (offset.sqrMagnitude < 0.000001f) return true;

            float turn = Vector2.SignedAngle(Facing, offset);
            // A hair of slack on the angle: the point most often asked about is one this same
            // struct just produced at exactly the half-angle, and asking whether it is inside
            // itself must not come back no because the last digit went the wrong way.
            if (Mathf.Abs(turn) > HalfAngleDegrees + 0.001f) return false;
            return offset.magnitude <= ReachAtTurn(turn) + 0.001f;
        }

        /// <summary>
        /// The nearest point inside the envelope to the one asked for.
        ///
        /// Every order goes through this, whether it was given inside the arc or well outside it -
        /// a tap behind him is not a mis-click to be thrown away, it is a request to turn as far as
        /// he can, and the control answers it with the sharpest turn he has. The angle is clamped
        /// before the distance, so a far-off tap keeps its direction rather than being pulled
        /// sideways by the shortening.
        /// </summary>
        public Vector2 Clamp(Vector2 target)
        {
            var offset = target - Origin;
            float distance = offset.magnitude;
            if (distance < 0.000001f) return Origin;

            float turn = TurnOnto(target);

            // Only the far edge clamps. The hole in the middle is where he stands, not a wall - see
            // InnerRadius - so a short order stays short and is rejected by the caller as too small
            // to be a run, which is how standing still has always been ordered.
            float carried = Mathf.Min(distance, ReachAtTurn(turn));

            return Origin + Rotate(Facing, turn) * carried;
        }

        /// <summary>
        /// What share of one dash a run to this point spends. One is everything he has.
        ///
        /// Measured along the arc rather than across the chord, which is the whole difference the
        /// turn makes: two points the same distance away cost differently depending on how far
        /// round they are.
        /// </summary>
        public float PowerOnto(Vector2 target)
        {
            if (OuterRadius < 0.0001f) return 0f;

            var offset = target - Origin;
            float chord = offset.magnitude;
            if (chord < 0.000001f) return 0f;

            float travelled = chord * ArcCostFactor(TurnOnto(target));
            return Mathf.Clamp01(travelled / OuterRadius);
        }

        /// <summary>
        /// How sharply he has to bend to land on a point, as signed curvature - one over the radius
        /// of the turn, positive anticlockwise, zero for a straight run.
        ///
        /// This is the whole of what the action phase is told about the shape of a run. There is
        /// exactly one circle that leaves his nose without a kink and passes through the point, and
        /// its total turn is twice the angle to that point: he does half the turning on the way out
        /// and half on the way in, which is why an order to a point sixty-five degrees round ends
        /// with him facing a hundred and thirty degrees from where he started.
        ///
        /// Divided by the ground actually covered rather than by a full dash, so a half-power order
        /// completes the same turn in half the distance - a tighter circle. Otherwise a short order
        /// would arrive under-turned and somewhere other than where it was pointed.
        /// </summary>
        public static float CurvatureFor(float turnDegrees, float runLength)
        {
            if (runLength < 0.0001f) return 0f;
            return 2f * turnDegrees * Mathf.Deg2Rad / runLength;
        }

        /// <summary>
        /// The nearest heading inside the arc to the one asked for.
        ///
        /// Direction only, so the length of a run is untouched: an order to sprint somewhere he
        /// cannot turn to becomes an order to sprint as close to it as he can, at the same speed,
        /// rather than a shorter run in the direction he wanted.
        /// </summary>
        public Vector2 ClampAim(Vector2 aim)
        {
            if (aim.sqrMagnitude < 0.000001f) return aim;

            float turn = Mathf.Clamp(Vector2.SignedAngle(Facing, aim),
                -HalfAngleDegrees, HalfAngleDegrees);
            return Rotate(Facing, turn);
        }

        /// <summary>Turns a unit vector by an angle in degrees, anticlockwise on the virtual plane.</summary>
        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
