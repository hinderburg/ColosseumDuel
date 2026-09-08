using System.Collections.Generic;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// What the closing arena costs, and how often it says so.
    ///
    /// The spikes are the one source of damage with no moment of its own: a blow lands on a substep,
    /// a trap closes on a substep, but the fire burns continuously and is applied a fraction of a
    /// point at a time, six times a frame. Anything that wants to show the player a number needs it
    /// totalled, so the totalling is a rule of the simulation rather than something the view is left
    /// to work out.
    /// </summary>
    public class HazardReportTests
    {
        private const float Dt = 1f / 60f;

        private static readonly IReadOnlyList<GladiatorDef> Squad =
            new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius };

        private static GameManager StartedRound(int seed = 1234)
        {
            var m = new GameManager(new System.Random(seed));
            m.StartMatch(Squad, Squad);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
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
            Assert.AreNotEqual(phase, m.State.Phase, $"stuck in {phase} for {maxSeconds}s");
        }

        /// <summary>Somewhere the fire has already reached by the cycle under test.</summary>
        private static Vector2 InTheSpikes()
            => new Vector2(0f, GameConstants.ArenaRadius * GameConstants.ArenaElongation * 0.92f);

        [Test]
        public void APhaseInTheSpikesIsAnnouncedOnce_WithThePhasesWholeCost()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            var burnt = new List<float>();
            m.Scorched += (side, amount) =>
            {
                Assert.AreEqual(PlayerSide.P1, side, "only the one standing in it should be burnt");
                burnt.Add(amount);
            };

            m.State.Cycle = 7;   // only the outer ring burns; from cycle 11 the whole arena does
            m.State.P1.Active.Pos = InTheSpikes();
            m.State.Bot.Active.Pos = Vector2.zero;   // inside the outer ring, so he is not burning

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.AreEqual(1, burnt.Count,
                "the fire is applied six times a frame - it has to be announced once, at the end");

            // A whole phase standing in it, so it should be the phase's whole price. Not exact: the
            // phase can be cut short by a collision, and the last tick lands wherever it lands.
            Assert.That(burnt[0], Is.EqualTo(GameConstants.HazardDamagePerPhase).Within(6f),
                $"a full phase in the fire reported {burnt[0]:0.#}");
        }

        /// <summary>
        /// A tally cannot survive the phase it belongs to.
        ///
        /// It is cleared when a phase begins as well as when it is announced, so a phase abandoned
        /// partway - a round restarted, a match thrown away - cannot have its unreported damage
        /// turn up on the end of the next one.
        /// </summary>
        [Test]
        public void ATallyDoesNotCarryIntoTheNextPhase()
        {
            var m = StartedRound();
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);

            m.State.Cycle = 7;   // only the outer ring burns; from cycle 11 the whole arena does
            m.State.P1.Active.Pos = InTheSpikes();
            m.State.Bot.Active.Pos = Vector2.zero;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            // Out of the fire, and only now is anybody listening.
            float reported = 0f;
            m.Scorched += (_, amount) => reported += amount;

            m.State.P1.Active.Pos = Vector2.zero;
            m.State.Bot.Active.Pos = new Vector2(60f, 0f);
            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Defend, Vector2.zero, 0f, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);

            Assert.AreEqual(0f, reported,
                "the previous phase's burn was reported on a phase nobody spent in the fire");
        }
    }
}
