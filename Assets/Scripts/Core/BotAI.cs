using System;
using System.Collections.Generic;
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
    /// what lies on the sand - a pickup is only worth a detour when it is meaningfully closer than
    /// the opponent and there is something in it for him.
    /// </summary>
    public static class BotAI
    {
        /// <summary>With only the blessing to think about. For tests that set up nothing else.</summary>
        public static BotDecision Decide(GladiatorInstance me, GladiatorInstance opp, ArenaPickup buffs,
            System.Random rng)
            => Decide(me, opp, buffs != null ? new[] { buffs } : null, rng);

        public static BotDecision Decide(GladiatorInstance me, GladiatorInstance opp, IEnumerable<ArenaPickup> pickups,
            System.Random rng)
        {
            var decision = new BotDecision();
            if (opp == null)
            {
                decision.Action = ActionType.Defend;
                return decision;
            }

            float oppDist = Vector2.Distance(me.Pos, opp.Pos);

            // The nearest thing on the sand worth having. Worth the detour when it is clearly nearer
            // than he is - they lie by the columns, so often somewhere off the line to him.
            Vector2? pickup = NearestWorthHaving(me, pickups);
            bool seekPickup = pickup.HasValue
                && Vector2.Distance(me.Pos, pickup.Value) < oppDist * 0.8f;

            bool wantsAbility = me.CanActivateAbility && rng.NextDouble() < 0.8; // aggressive: use it almost whenever ready
            decision.UseAbility = wantsAbility;

            if (!seekPickup && rng.NextDouble() < 0.10)
            {
                // occasional defensive play
                decision.Action = ActionType.Defend;
                return decision;
            }

            decision.Action = ActionType.Move;
            Vector2 target = seekPickup ? pickup.Value : opp.Pos;
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

        private static Vector2? NearestWorthHaving(GladiatorInstance me, IEnumerable<ArenaPickup> pickups)
        {
            if (pickups == null) return null;

            Vector2? best = null;
            float bestDistance = float.MaxValue;
            foreach (var pickup in pickups)
            {
                if (pickup == null || !pickup.Position.HasValue || !WorthHaving(me, pickup.Kind)) continue;
                float distance = Vector2.Distance(me.Pos, pickup.Position.Value);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pickup.Position;
                }
            }
            return best;
        }

        /// <summary>
        /// Whether there is something in it for him: a second blessing on a weapon already blessed only
        /// restarts the count, an apple is wasted on a man barely scratched, and a horn on a man whose
        /// ability is already there to be used.
        /// </summary>
        private static bool WorthHaving(GladiatorInstance me, PickupKind kind)
        {
            switch (kind)
            {
                case PickupKind.Apple: return me.Hp < me.Def.MaxHp * 0.75f;
                case PickupKind.Horn: return !me.CanActivateAbility && me.Rage < GameConstants.RageMax;
                default: return !me.WeaponBuffed;
            }
        }

        private static float Lerp(float t, float a, float b) => a + (b - a) * t;
    }
}
