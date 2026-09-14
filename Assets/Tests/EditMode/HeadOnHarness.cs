using System.Collections.Generic;
using System.Linq;
using System.Text;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// How many rounds a clash lasts when the two men simply run at each other every round: no
    /// abilities, no guard, no going round. The pace of the fight with every decision taken out of
    /// it - the number to tune when a clash feels long.
    ///
    /// Explicit, like the balance harness: a tuning instrument rather than a guard. It logs, per
    /// pairing, the rounds, the blows each side landed and what they were worth, what the wounds
    /// bled, and what the closing arena took - which says whether a clash was ended by the two of
    /// them or by the spikes.
    /// </summary>
    public class HeadOnHarness
    {
        private const float Dt = 1f / 30f;
        private const float MaxSeconds = 900f;

        [Test, Explicit("plays a head-on duel for every pairing - run it by name when tuning")]
        public void HowManyRoundsAHeadOnClashLasts()
        {
            var defs = GladiatorDef.All;
            var matrix = new StringBuilder("[HeadOn] rounds until one falls (row = near end, column = far end)\n            ");
            foreach (var d in defs) matrix.Append($"{d.Name,11}");
            matrix.Append('\n');
            var detail = new StringBuilder("[HeadOn] detail: blows landed x average, bled, scorched - near end / far end\n");

            foreach (var a in defs)
            {
                matrix.Append($"{a.Name,-12}");
                foreach (var b in defs)
                {
                    var r = Duel(a, b, 7);
                    matrix.Append($"{(r.Winner == null ? "draw" : r.Rounds.ToString()),11}");
                    detail.Append($"  {a.Name,-10} v {b.Name,-10} {r.Rounds,3} rounds, won by {r.Winner?.Name ?? "nobody",-10}" +
                                  $" | {r.Blows[0],2} x {Avg(r.Dealt[0], r.Blows[0]),5:0.0} bled {r.Bled[1],5:0} " +
                                  $"/ {r.Blows[1],2} x {Avg(r.Dealt[1], r.Blows[1]),5:0.0} bled {r.Bled[0],5:0}" +
                                  $" | scorched {r.Scorched[0],4:0} / {r.Scorched[1],4:0}\n");
                }
                matrix.Append('\n');
            }

            Debug.Log(matrix.ToString() + detail);
        }

        private static float Avg(float total, int n) => n > 0 ? total / n : 0f;

        private sealed class Result
        {
            public GladiatorDef Winner;
            public int Rounds;
            public readonly int[] Blows = new int[2];      // landed by [near, far]
            public readonly float[] Dealt = new float[2];  // by [near, far]
            public readonly float[] Bled = new float[2];   // suffered by [near, far]
            public readonly float[] Scorched = new float[2];
        }

        private static Result Duel(GladiatorDef near, GladiatorDef far, int seed)
        {
            var r = new Result();
            var m = new GameManager(new System.Random(seed));
            m.StartMatch(new[] { near }, new[] { far }, false,
                new Dictionary<GladiatorId, AbilityKey> { { near.Id, near.Abilities[0] } },
                new Dictionary<GladiatorId, AbilityKey> { { far.Id, far.Abilities[0] } });
            m.SubmitPick(PlayerSide.P1, near.Id);

            // The event names the victim; the blow is the other side's.
            m.Damaged += (victim, amount) =>
            {
                int striker = victim == PlayerSide.Bot ? 0 : 1;
                r.Blows[striker]++;
                r.Dealt[striker] += amount;
            };
            m.Bled += (victim, amount) => r.Bled[victim == PlayerSide.P1 ? 0 : 1] += amount;
            m.Scorched += (victim, amount) => r.Scorched[victim == PlayerSide.P1 ? 0 : 1] += amount;

            int planned = -1;
            for (float t = 0f; t < MaxSeconds; t += Dt)
            {
                var s = m.State;
                if (s.Phase == MatchPhase.ClashEnd || s.Phase == MatchPhase.MatchEnd) break;

                if (s.Phase == MatchPhase.Planning && planned != s.Round)
                {
                    planned = s.Round;
                    s.P1.Active.Rage = 0f;
                    s.Bot.Active.Rage = 0f;
                    Charge(m, PlayerSide.P1);
                    Charge(m, PlayerSide.Bot);
                }
                m.Tick(Dt);
            }

            r.Rounds = m.State.Round;
            bool nearStanding = m.State.P1.Roster.Any(g => g.Alive);
            bool farStanding = m.State.Bot.Roster.Any(g => g.Alive);
            r.Winner = nearStanding == farStanding ? null : nearStanding ? near : far;
            return r;
        }

        /// <summary>Straight at the other man, as far as his speed carries him.</summary>
        private static void Charge(GameManager m, PlayerSide side)
        {
            var me = m.State.Get(side).Active;
            var other = m.State.Get(side == PlayerSide.P1 ? PlayerSide.Bot : PlayerSide.P1).Active;
            var toward = other.Pos - me.Pos;
            m.SubmitPlanningAction(side, ActionType.Move, toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector2.up, 1f, false);
        }
    }
}
