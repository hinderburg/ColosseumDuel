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
    /// items - items are only worth a detour when they are both close AND meaningfully closer than
    /// the opponent.
    /// </summary>
    public static class BotAI
    {
        public static BotDecision Decide(GladiatorInstance me, GladiatorInstance opp, ItemSystem items, System.Random rng)
        {
            var decision = new BotDecision();
            if (opp == null)
            {
                decision.Action = ActionType.Defend;
                return decision;
            }

            float oppDist = Vector2.Distance(me.Pos, opp.Pos);
            ArenaItem nearestItem = null;
            float itemDist = float.MaxValue;
            foreach (var item in items.Items)
            {
                float d = Vector2.Distance(me.Pos, item.Pos);
                if (d < itemDist) { itemDist = d; nearestItem = item; }
            }

            bool wantsAbility = me.CanActivateAbility && rng.NextDouble() < 0.8; // aggressive: use it almost whenever ready
            decision.UseAbility = wantsAbility;

            // Only detour for an item if it's genuinely close AND notably closer than the opponent.
            bool seekItem = nearestItem != null
                && itemDist < GameConstants.ArenaRadius * 0.3f
                && itemDist < oppDist * 0.55f;

            if (!seekItem && rng.NextDouble() < 0.10)
            {
                // occasional defensive play
                decision.Action = ActionType.Defend;
                return decision;
            }

            decision.Action = ActionType.Move;
            Vector2 target = seekItem ? nearestItem.Pos : opp.Pos;
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
