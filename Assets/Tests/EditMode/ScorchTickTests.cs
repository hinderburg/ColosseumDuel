using System.Collections.Generic;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The fire reports what it has taken on a beat, four times a second.
    ///
    /// The damage itself has always been continuous - charged per substep against the position the
    /// gladiator actually occupied. What changed is how often the running total is announced: the
    /// whole phase in one number read as a single hit from something invisible.
    /// </summary>
    public class ScorchTickTests
    {
        private const float Dt = 1f / 60f;

        private static GameManager StartedRound()
        {
            var m = new GameManager(new System.Random(20260909));
            m.StartMatch(
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius },
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius },
                tutorial: false);
            m.SubmitPick(PlayerSide.P1, 0);
            m.SubmitPick(PlayerSide.Bot, 0);
            return m;
        }

        private static void AdvanceUntilPhaseLeaves(GameManager m, MatchPhase phase, float maxSeconds = 30f)
        {
            float t = 0f;
            while (m.State.Phase == phase && t < maxSeconds)
            {
                m.Tick(Dt);
                t += Dt;
            }
        }

        /// <summary>
        /// A whole action phase standing in the fire is reported as a tick every quarter second, not
        /// as one number.
        ///
        /// Both halves are asserted: the count, which is the point of the change, and the total,
        /// which is what stops the beat from quietly charging four times over.
        /// </summary>
        [Test]
        public void AFullPhaseInTheFireIsReportedAsATickEveryQuarterSecond()
        {
            var m = StartedRound();

            var ticks = new List<float>();
            m.Scorched += (side, amount) => { if (side == PlayerSide.P1) ticks.Add(amount); };

            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            // Late enough that the outermost ring is burning, and standing in it.
            m.State.Cycle = 9;
            var player = m.State.P1.Active;
            player.Pos = ArenaShape.FromUnitCircle(Vector2.right) * 0.88f;
            player.Hp = 10000f; // so a whole phase of burning cannot end the round early

            m.State.Bot.Active.Pos = new Vector2(0f, 0f);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            int beats = Mathf.RoundToInt(GameConstants.ActionTime / GameManager.ScorchTickInterval);
            Assert.AreEqual(beats, ticks.Count,
                $"a phase in the fire should read as {beats} ticks, not {ticks.Count}");

            // The beat does not change the price. A full phase is still what a full phase cost.
            float total = 0f;
            foreach (float tick in ticks) total += tick;
            Assert.AreEqual(GameConstants.HazardDamagePerPhase, total, 1.5f,
                $"the ticks add up to {total:0.0}, not what a whole phase costs, {GameConstants.HazardDamagePerPhase}");

            foreach (float tick in ticks)
                Assert.Greater(tick, 0f, "an empty tick is a number on screen saying nothing");
        }

        /// <summary>
        /// Standing out of the fire reports nothing at all.
        ///
        /// The beat runs whether or not anyone is burning, so without this a gladiator on clean sand
        /// would be told four times a second that he had taken nothing.
        /// </summary>
        [Test]
        public void ClearOfTheFireNothingIsReported()
        {
            var m = StartedRound();

            int ticks = 0;
            m.Scorched += (_, __) => ticks++;

            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            // Rings out to 0.25 are burning; the middle is not.
            m.State.Cycle = 9;
            m.State.P1.Active.Pos = Vector2.zero;
            m.State.Bot.Active.Pos = new Vector2(0f, 30f);

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.AreEqual(0, ticks, "nobody was in the fire and it still spoke up");
        }
    }
}
