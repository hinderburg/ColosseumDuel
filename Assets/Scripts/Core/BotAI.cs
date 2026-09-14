using System;
using UnityEngine;

namespace ColosseumDuel.Core
{
    public struct BotDecision
    {
        public ActionType Action;
        public Vector2 AimDirection; // for Move
        public float Power;          // 0..1 pull strength for Move
        public bool UseAbility;
    }

    /// <summary>
    /// Rebalanced (per design feedback) to prioritize closing distance and attacking over chasing
    /// the blessing - it is only worth a detour when it is meaningfully closer than the opponent and
    /// his own weapon is not blessed already.
    /// </summary>
    public static class BotAI
    {
        public static BotDecision Decide(GladiatorInstance me, GladiatorInstance opp, WeaponBuffPickups buffs,
            System.Random rng)
        {
            var decision = new BotDecision();
            if (opp == null)
            {
                decision.Action = ActionType.Defend;
                return decision;
            }

            float oppDist = Vector2.Distance(me.Pos, opp.Pos);

            // The blessing lies on the line between the two of them, so it is usually a little over
            // half way to the opponent. Worth the detour when it is clearly nearer than he is and
            // there is something to gain - a second one on a weapon already blessed only restarts
            // the count.
            Vector2? blessing = buffs != null && !me.WeaponBuffed ? buffs.Position : null;
            bool seekBlessing = blessing.HasValue
                && Vector2.Distance(me.Pos, blessing.Value) < oppDist * 0.8f;

            bool wantsAbility = me.CanActivateAbility && rng.NextDouble() < 0.8; // aggressive: use it almost whenever ready
            decision.UseAbility = wantsAbility;

            if (!seekBlessing && rng.NextDouble() < 0.10)
            {
                // occasional defensive play
                decision.Action = ActionType.Defend;
                return decision;
            }

            decision.Action = ActionType.Move;
            Vector2 target = seekBlessing ? blessing.Value : opp.Pos;
            Vector2 dir = (target - me.Pos);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            decision.AimDirection = dir.normalized;
            float rolled = Lerp((float)rng.NextDouble(), 0.75f, 1.0f); // committed, aggressive pulls

            // But no further than a charge through what it is going for. A run is three times the
            // dash it used to be, and three quarters of it or more carried the bot straight past
            // the player and across the arena into the far wall.
            float reach = me.DashReach();
            float wanted = dir.magnitude + GameConstants.GladiatorRadius * 2f;
            decision.Power = reach > 0.0001f ? Mathf.Min(rolled, wanted / reach) : rolled;
            return decision;
        }

        private static float Lerp(float t, float a, float b) => a + (b - a) * t;
    }
}
