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
        private readonly ObstacleField _obstacles;
        public readonly List<ArenaTrap> Traps = new List<ArenaTrap>();

        public TrapSystem(System.Random rng, ObstacleField obstacles = null)
        {
            _rng = rng;
            _obstacles = obstacles ?? ObstacleField.Empty;
        }

        /// <summary>
        /// What a trap takes off. The weakest attack in the game, so being caught by the scenery is
        /// never worse than being caught by a gladiator - the arena is a complication, not the main
        /// threat, and it should not out-hit the fighters it is there to inconvenience.
        /// </summary>
        public static float Damage => GladiatorDef.All.Min(d => d.Damage);

        /// <summary>How close the centres have to be for the jaws to close.</summary>
        public static float TriggerDistance => GameConstants.GladiatorRadius + GameConstants.TrapRadius;

        /// <summary>
        /// Lays out the round's traps, half in each fighter's end of the arena.
        ///
        /// Split rather than scattered freely, because the two ends are not interchangeable: the
        /// player's fighter starts in one and the opponent's in the other, and a free scatter that
        /// happened to put four of six in one end would hand that round to whoever was standing in
        /// the other. Both sides get the same number of holes to worry about.
        /// </summary>
        public void SpawnForRound()
        {
            Traps.Clear();

            int perSide = GameConstants.TrapCount / 2;
            for (int i = 0; i < GameConstants.TrapCount; i++)
            {
                // First half in the near end, second half in the far end.
                float side = i < perSide ? -1f : 1f;
                Traps.Add(new ArenaTrap { Pos = RandomPos(side) });
            }
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
                g.StopRunning();
                g.TakeDamage(Damage);
                return trap;
            }

            return null;
        }

        /// <summary>A point in one half of the arena, in the half the sign of <paramref name="side"/> picks.</summary>
        private Vector2 RandomPos(float side)
        {
            // Not on a column or a crate. Drawn again rather than pushed off, so the spread stays
            // what the draw below makes it - pushing would pile traps up along the obstacles' edges.
            // The obstacles cover a small share of the sand, so a handful of tries is plenty; the
            // last one stands if they all somehow fail, rather than leaving a slot empty.
            Vector2 pos = AnyPos(side);
            for (int attempt = 0; attempt < 16 && !_obstacles.IsFree(pos, GameConstants.TrapRadius * 2f); attempt++)
                pos = AnyPos(side);
            return pos;
        }

        private Vector2 AnyPos(float side)
        {
            // Drawn on a unit circle and stretched onto the ellipse, the same walk item spawns use,
            // so the shape of the arena is stated in one place rather than three. The angle is
            // confined to a half turn and then given its side's sign.
            float r = Mathf.Sqrt((float)_rng.NextDouble());
            float a = (float)(_rng.NextDouble() * Math.PI);

            // Kept out of the middle as well as off the wall: a trap sitting on the centre of the
            // arena is the one square both fighters cross on their way to each other, so it would
            // be sprung by whoever moved first, every round - a coin toss rather than a hazard.
            float margin = GameConstants.TrapRadius * 2f;
            var onUnitCircle = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * side) * Mathf.Lerp(0.35f, 1f, r);

            return new Vector2(
                onUnitCircle.x * (ArenaShape.RadiusX - margin),
                onUnitCircle.y * (ArenaShape.RadiusY - margin));
        }
    }
}
