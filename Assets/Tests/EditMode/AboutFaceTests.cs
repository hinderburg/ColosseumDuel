using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Turning round where you stand.
    ///
    /// A gladiator carries his heading between cycles and can only bend so far off it, which means
    /// he can run himself into a wall, or past an opponent and away from him, and spend the next
    /// cycles crawling back round. This is the way out of that - and because it is the way out of
    /// that, it has to cost something, which here is time rather than rage.
    /// </summary>
    public class AboutFaceTests
    {
        private const float Dt = 1f / 60f;

        private static readonly GladiatorDef[] Squad =
        {
            GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius
        };

        private static GameManager StartedRound()
        {
            var m = new GameManager(new System.Random(4321));
            m.StartMatch(Squad, Squad);
            m.SubmitPick(PlayerSide.P1, GladiatorId.Brutius);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Reveal);
            return m;
        }

        private static void AdvanceUntilPhaseLeaves(GameManager m, MatchPhase phase, float maxSeconds = 30f)
        {
            float spent = 0f;
            while (m.State.Phase == phase && spent < maxSeconds)
            {
                m.Tick(Dt);
                spent += Dt;
            }
        }

        /// <summary>One whole cycle: the planning phase and the action phase after it.</summary>
        private static void RunACycle(GameManager m)
        {
            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            AdvanceUntilPhaseLeaves(m, MatchPhase.Action);
            AdvanceUntilPhaseLeaves(m, MatchPhase.RoundEnd);
        }

        [Test]
        public void ItTurnsHimRightRound()
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius) { Facing = Vector2.up };

            Assert.IsTrue(g.AboutFace());
            Assert.AreEqual(0f, Vector2.Angle(g.Facing, Vector2.down), 0.01f,
                "half a turn, not some fraction of one");
        }

        /// <summary>
        /// And the whole point of turning him: the ground he can be sent onto comes with him. A turn
        /// that moved the model and not the envelope would be an animation, not an action.
        /// </summary>
        [Test]
        public void TheArcOfGroundHeCanBeSentOntoComesRoundWithHim()
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius) { Pos = Vector2.zero, Facing = Vector2.up };

            var behind = new Vector2(0f, -40f);
            Assert.IsFalse(MoveEnvelope.For(g).Contains(behind), "he cannot be sent behind himself");

            g.AboutFace();

            Assert.IsTrue(MoveEnvelope.For(g).Contains(behind),
                "and after turning round, that is the ground in front of him");
        }

        [Test]
        public void ItIsChargedAtTheStartOfARound()
        {
            var g = new GladiatorInstance(GladiatorDef.Brutius);
            Assert.IsTrue(g.CanAboutFace, "a fresh gladiator has it");

            g.AboutFace();
            Assert.IsFalse(g.CanAboutFace);

            g.ResetForNewRound();
            Assert.IsTrue(g.CanAboutFace,
                "a cooldown carried into the next round would punish him for a fight he has left");
            Assert.AreEqual(1f, g.AboutFaceReadiness, 0.001f);
        }

        /// <summary>
        /// Two cycles without it, then it is back. Counted by running whole cycles rather than by
        /// poking the counter, because the counter is not the promise - "two cycles" is.
        /// </summary>
        [Test]
        public void ItComesBackAfterTwoCycles()
        {
            var m = StartedRound();
            var p1 = m.State.P1.Active;

            Assert.IsTrue(m.SubmitAboutFace(PlayerSide.P1), "it starts the round charged");
            Assert.IsFalse(p1.CanAboutFace, "and is gone the moment it is spent");

            RunACycle(m);
            Assert.IsFalse(p1.CanAboutFace, "one cycle later, still charging");

            RunACycle(m);
            Assert.IsFalse(p1.CanAboutFace, "two cycles later, still charging");

            RunACycle(m);
            Assert.IsTrue(p1.CanAboutFace, "and back on the cycle after that");
        }

        /// <summary>
        /// The ring on the button fills over those cycles rather than jumping from empty to full,
        /// which is the only way a player can tell "soon" from "not for a while".
        /// </summary>
        [Test]
        public void TheRingFillsWhileItCharges()
        {
            var m = StartedRound();
            var p1 = m.State.P1.Active;

            m.SubmitAboutFace(PlayerSide.P1);
            float spent = p1.AboutFaceReadiness;

            RunACycle(m);
            float halfway = p1.AboutFaceReadiness;

            Assert.AreEqual(0f, spent, 0.001f, "empty the moment it is spent");
            Assert.Greater(halfway, spent, "and visibly further along a cycle later");
            Assert.Less(halfway, 1f, "but not yet claiming to be ready");
        }

        [Test]
        public void ItCannotBeSpentTwiceOrOutsidePlanning()
        {
            var m = StartedRound();

            Assert.IsTrue(m.SubmitAboutFace(PlayerSide.P1));
            Assert.IsFalse(m.SubmitAboutFace(PlayerSide.P1), "one turn per charge");

            AdvanceUntilPhaseLeaves(m, MatchPhase.Planning);
            Assert.AreEqual(MatchPhase.Action, m.State.Phase);
            Assert.IsFalse(m.SubmitAboutFace(PlayerSide.P1),
                "nothing is decided during the action phase, this included");
        }

        /// <summary>
        /// It drops whatever run was already ordered.
        ///
        /// An order was aimed into the arc he had when it was given. Left standing through a turn it
        /// would be clamped into the new arc - which is to say silently swung round by half a turn
        /// into something nobody asked for. Undecided is the honest answer, and it is a state the
        /// phase already starts in.
        /// </summary>
        [Test]
        public void ItTakesBackTheRunThatWasAlreadyOrdered()
        {
            var m = StartedRound();
            var p1 = m.State.P1.Active;

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, p1.Facing, 1f, false);
            Assert.AreEqual(ActionType.Move, p1.PlannedAction);

            m.SubmitAboutFace(PlayerSide.P1);

            Assert.AreEqual(ActionType.None, p1.PlannedAction,
                "the order pointed at ground that is now behind him");
            Assert.AreEqual(0f, p1.PlannedPower, 0.001f);
        }

        /// <summary>
        /// It is free in every sense except time: no rage, no lock on the rage ability, and it does
        /// not spend the cycle it is used in. A gladiator can turn round and then charge.
        /// </summary>
        [Test]
        public void ItCostsNothingButTime()
        {
            var m = StartedRound();
            var p1 = m.State.P1.Active;
            p1.Rage = GameConstants.RageMax;

            m.SubmitAboutFace(PlayerSide.P1);

            Assert.AreEqual(GameConstants.RageMax, p1.Rage, 0.001f, "it is not paid for in rage");
            Assert.IsTrue(p1.CanActivateAbility, "nor by locking out the ability");

            Assert.IsTrue(m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, p1.Facing, 1f, false));
            Assert.AreEqual(ActionType.Move, p1.PlannedAction, "and he can still spend the cycle running");
        }
    }
}
