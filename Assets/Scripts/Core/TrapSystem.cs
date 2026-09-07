using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public sealed class ArenaTrap
    {
        public Vector2 Pos;

        /// <summary>False once something has stepped in it. A sprung trap stays where it fell.</summary>
        public bool Armed = true;
    }

    /// <summary>
    /// Jaw traps scattered on the sand. Running into one stops the charge dead and bites.
    ///
    /// They are the arena's own hazard rather than either fighter's: nothing aims them, both sides
    /// can be caught, and they turn a straight full-power dash across open ground - the strongest
    /// and least interesting move in the game - into a gamble.
    /// </summary>
    public sealed class TrapSystem
    {
        private readonly System.Random _rng;
        public readonly List<ArenaTrap> Traps = new List<ArenaTrap>();

        public TrapSystem(System.Random rng)
        {
            _rng = rng;
        }

        /// <summary>
        /// What a trap takes off. The weakest attack in the game, so being caught by the scenery is
        /// never worse than being caught by a gladiator - the arena is a complication, not the main
        /// threat, and it should not out-hit the fighters it is there to inconvenience.
        /// </summary>
        public static float Damage => GladiatorDef.All.Min(d => d.Damage);

        /// <summary>How close the centres have to be for the jaws to close.</summary>
        public static float TriggerDistance => GameConstants.GladiatorRadius + GameConstants.TrapRadius;

        public void SpawnForRound()
        {
            Traps.Clear();
            for (int i = 0; i < GameConstants.TrapCount; i++)
                Traps.Add(new ArenaTrap { Pos = RandomPos() });
        }

        /// <summary>
        /// Springs any trap the gladiator has just run into: stops him where he stands and bites.
        /// Returns the trap that caught him, or null.
        ///
        /// Stopping him is most of the point. The damage is small; losing the rest of a dash - and
        /// the position it was going to buy - is what actually costs.
        /// </summary>
        public ArenaTrap TryTrigger(GladiatorInstance g)
        {
            if (g == null || !g.Alive) return null;

            foreach (var trap in Traps)
            {
                if (!trap.Armed) continue;
                if (Vector2.Distance(g.Pos, trap.Pos) > TriggerDistance) continue;

                trap.Armed = false;
                g.Vel = Vector2.zero;
                g.TakeDamage(Damage);
                return trap;
            }

            return null;
        }

        private Vector2 RandomPos()
        {
            // Drawn on a unit circle and stretched onto the ellipse, the same walk item spawns use,
            // so the shape of the arena is stated in one place rather than three.
            float r = Mathf.Sqrt((float)_rng.NextDouble());
            float a = (float)(_rng.NextDouble() * Math.PI * 2.0);

            // Kept out of the middle as well as off the wall: the fighters start on the long axis
            // and a trap on the centre line would be stepped on by whoever moved first, every round,
            // which is a coin toss rather than a hazard.
            float margin = GameConstants.TrapRadius * 2f;
            var onUnitCircle = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Lerp(0.35f, 1f, r);

            return new Vector2(
                onUnitCircle.x * (ArenaShape.RadiusX - margin),
                onUnitCircle.y * (ArenaShape.RadiusY - margin));
        }
    }
}
