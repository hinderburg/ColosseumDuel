using System.Linq;
using ColosseumDuel.Core;
using NUnit.Framework;
using UnityEngine;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// The shape of a run.
    ///
    /// A gladiator does not pivot on the spot and set off - he leaves along the way he is already
    /// facing and bends round onto where he was sent. That is one circle, and these pin the things
    /// the game depends on about it: that he arrives where he was pointed, that the ground he
    /// covers is the arc rather than the straight line to it, and that the lane drawn under the
    /// finger is that same circle rather than a picture of one.
    /// </summary>
    public class TurnArcTests
    {
        private static GladiatorInstance Standing(GladiatorDef def, Vector2 facing)
            => new GladiatorInstance(def) { Pos = Vector2.zero, Facing = facing };

        /// <summary>Where the preview says he ends up.</summary>
        private static Vector2 EndOfRun(GladiatorInstance g, Vector2 aim, float power)
            => GameManager.ComputeTrajectoryPreview(g, aim, power, GameConstants.ActionTime / 400f).Last();

        [Test]
        public void ADeadAheadOrderIsStillAStraightLine()
        {
            var g = Standing(GladiatorDef.Barbarius, Vector2.up);

            foreach (var p in GameManager.ComputeTrajectoryPreview(g, Vector2.up, 1f))
                Assert.AreEqual(0f, p.x, 0.01f, "nothing was asked of him but forward");
        }

        /// <summary>
        /// The promise the whole control rests on: tap somewhere inside the zone and that is where
        /// he finishes. It is not obvious - the run leaves along his nose, not towards the tap - and
        /// it only holds because the power is measured along the bend rather than across the chord.
        /// </summary>
        [Test]
        public void HeArrivesWhereHeWasSent_ThoughHeSetsOffSomewhereElse()
        {
            foreach (var def in GladiatorDef.All)
            foreach (float turn in new[] { -60f, -30f, -5f, 0f, 5f, 30f, 60f })
            {
                var g = Standing(def, Vector2.up);
                var envelope = MoveEnvelope.For(g);
                if (Mathf.Abs(turn) > envelope.HalfAngleDegrees) continue;

                var target = g.Pos
                             + MoveEnvelope.Rotate(Vector2.up, turn) * (envelope.ReachAtTurn(turn) * 0.7f);
                var landed = EndOfRun(g, (target - g.Pos).normalized, envelope.PowerOnto(target));

                Assert.AreEqual(0f, Vector2.Distance(landed, target), envelope.OuterRadius * 0.02f,
                    $"{def.Name} sent {turn} degrees round finished at {landed} rather than {target}");
            }
        }

        /// <summary>
        /// And the way there really is a bend. Measured as the ground he actually covers against the
        /// straight line to the same point - a run that went straight would cover the same.
        /// </summary>
        [Test]
        public void GettingThereRoundABendCostsMoreGroundThanTheStraightLine()
        {
            var g = Standing(GladiatorDef.Barbarius, Vector2.up);
            var envelope = MoveEnvelope.For(g);
            float turn = envelope.HalfAngleDegrees;

            var target = g.Pos + MoveEnvelope.Rotate(Vector2.up, turn) * (envelope.ReachAtTurn(turn) * 0.9f);
            var points = GameManager.ComputeTrajectoryPreview(g, (target - g.Pos).normalized,
                envelope.PowerOnto(target), GameConstants.ActionTime / 400f);

            float walked = 0f;
            for (int i = 1; i < points.Count; i++) walked += Vector2.Distance(points[i - 1], points[i]);
            float straight = Vector2.Distance(g.Pos, target);

            Assert.Greater(walked, straight * 1.05f,
                $"walked {walked:0.#} to cover {straight:0.#} - that is not a bend, it is a line");
            Assert.AreEqual(MoveEnvelope.ArcCostFactor(turn), walked / straight, 0.03f,
                "and it costs what the envelope charged for it");
        }

        /// <summary>
        /// He finishes the run facing where the bend leaves him: twice the angle to the point, half
        /// of the turn done on the way out and half on the way in. This is what makes the flank of a
        /// man something the other one can steer round behind.
        /// </summary>
        [Test]
        public void HeFinishesTheRunTurnedTwiceAsFarAsThePointHeWasSentTo()
        {
            var g = Standing(GladiatorDef.Barbarius, Vector2.up);
            var envelope = MoveEnvelope.For(g);
            const float turn = 30f;
            Assert.Less(turn, envelope.HalfAngleDegrees, "this test needs a turn he is allowed");

            var target = g.Pos + MoveEnvelope.Rotate(Vector2.up, turn) * (envelope.ReachAtTurn(turn) * 0.8f);
            var points = GameManager.ComputeTrajectoryPreview(g, (target - g.Pos).normalized,
                envelope.PowerOnto(target), GameConstants.ActionTime / 400f);

            var finalHeading = points[points.Count - 1] - points[points.Count - 2];

            Assert.AreEqual(turn * 2f, Vector2.SignedAngle(Vector2.up, finalHeading), 1.5f);
        }

        /// <summary>
        /// The zone draws in towards its sides. It has to: a dash spent bending covers less ground
        /// than a dash spent going straight, and an edge at a flat radius would offer corners that
        /// cannot be got to inside the phase.
        /// </summary>
        [Test]
        public void TheFarEdgeDrawsInTowardsTheSides()
        {
            var envelope = MoveEnvelope.For(Standing(GladiatorDef.Barbarius, Vector2.up));

            Assert.AreEqual(envelope.OuterRadius, envelope.ReachAtTurn(0f), 0.01f,
                "dead ahead costs nothing extra");
            Assert.Less(envelope.ReachAtTurn(envelope.HalfAngleDegrees), envelope.OuterRadius * 0.95f,
                "and the sharpest turn costs enough to see");

            var corner = MoveEnvelope.Rotate(Vector2.up, envelope.HalfAngleDegrees) * envelope.OuterRadius;
            Assert.IsFalse(envelope.Contains(corner),
                "the corner of a flat wedge is ground a bend cannot reach inside one phase");
        }

        /// <summary>
        /// Nobody gets a wider arc than a hundred and thirty degrees, whoever they are and whatever
        /// they have armed. Half of it is how far round he can bend, and past that a run stops
        /// reading as a run and starts reading as a pivot.
        /// </summary>
        [Test]
        public void NobodyTurnsWiderThanTheCap()
        {
            foreach (var def in GladiatorDef.All)
            {
                var g = Standing(def, Vector2.up);
                Assert.LessOrEqual(MoveEnvelope.For(g).ArcDegrees, 130f + 0.01f, def.Name);

                g.Rage = GameConstants.RageMax;
                g.AbilityArmed = true;
                Assert.LessOrEqual(MoveEnvelope.For(g).ArcDegrees, 130f + 0.01f,
                    $"{def.Name} with an ability armed");
            }
        }

        /// <summary>
        /// And the match runs the circle the preview drew. They are two callers of one step for
        /// exactly this reason, and this is the assertion that keeps them one.
        /// </summary>
        [Test]
        public void TheMatchRunsTheCurveThePreviewDrew()
        {
            var m = new GameManager(new System.Random(99));
            m.StartMatch(
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius },
                new[] { GladiatorDef.Brutius, GladiatorDef.Barbarius, GladiatorDef.Hilius });
            m.SubmitPick(PlayerSide.P1, GladiatorId.Barbarius);
            for (int i = 0; i < 900 && m.State.Phase != MatchPhase.Planning; i++) m.Tick(1f / 60f);
            Assert.AreEqual(MatchPhase.Planning, m.State.Phase);

            var p1 = m.State.P1.Active;
            var envelope = MoveEnvelope.For(p1);
            float turn = envelope.HalfAngleDegrees * 0.8f;
            var target = p1.Pos + MoveEnvelope.Rotate(p1.Facing, turn) * (envelope.ReachAtTurn(turn) * 0.6f);
            var aim = (target - p1.Pos).normalized;
            float power = envelope.PowerOnto(target);

            var promised = GameManager.ComputeTrajectoryPreview(p1, aim, power).Last();

            m.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, aim, power, false);
            m.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);
            for (int i = 0; i < 900 && m.State.Phase != MatchPhase.Action; i++) m.Tick(1f / 60f);
            for (int i = 0; i < 900 && m.State.Phase == MatchPhase.Action; i++) m.Tick(1f / 60f);

            Assert.AreEqual(0f, Vector2.Distance(p1.Pos, promised), envelope.OuterRadius * 0.05f,
                $"promised {promised}, ran to {p1.Pos}");
        }
    }
}
