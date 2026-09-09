using System.Collections;
using System.Linq;
using ColosseumDuel.Core;
using ColosseumDuel.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ColosseumDuel.Tests
{
    /// <summary>
    /// Exercises the slingshot through PlayerInputController's device-independent API, so no mouse
    /// events need synthesising. Everything here is measured in the simulation's virtual units.
    /// </summary>
    public class SlingshotInputTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private PlayerInputController _input;

        [UnitySetUp]
        public IEnumerator LoadArenaAndReachPlanning()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _input = Object.FindFirstObjectByType<PlayerInputController>();
            Assert.IsNotNull(_controller);
            Assert.IsNotNull(_input, "the Arena scene must contain a PlayerInputController");

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase);
        }

        private GladiatorInstance Player => _controller.Manager.State.P1.Active;

        [Test]
        public void APressAwayFromTheGladiatorDoesNotStartAPull()
        {
            Vector2 farAway = Player.Pos + new Vector2(GameConstants.ArenaRadius * 0.5f, 0f);
            Assert.IsFalse(_input.TryBeginDrag(farAway));
            Assert.IsFalse(_input.IsDragging);
        }

        [Test]
        public void PullingBackAimsForwards_AndScalesPowerByPullDistance()
        {
            Assert.IsTrue(_input.TryBeginDrag(Player.Pos));

            // Pull to the left of the gladiator; the launch must go right.
            _input.UpdateDrag(Player.Pos + new Vector2(-GameConstants.MaxDragVirtual * 0.5f, 0f));

            Assert.AreEqual(1f, _input.CurrentAim.x, 0.001f, "release runs opposite the pull");
            Assert.AreEqual(0.5f, _input.CurrentPower, 0.01f);
        }

        [Test]
        public void PowerIsClampedAtAFullPull()
        {
            _input.TryBeginDrag(Player.Pos);
            _input.UpdateDrag(Player.Pos + new Vector2(-GameConstants.MaxDragVirtual * 10f, 0f));
            Assert.AreEqual(1f, _input.CurrentPower, 0.001f, "over-pulling must not exceed full power");
        }

        [Test]
        public void AMisClickWithAlmostNoPullSubmitsNothing()
        {
            _input.TryBeginDrag(Player.Pos);
            Assert.IsFalse(_input.ReleaseDrag(Player.Pos + new Vector2(-0.5f, 0f)),
                "a pull of half a unit is a mis-click, not a move");
            Assert.AreEqual(ActionType.None, Player.PlannedAction);
        }

        [Test]
        public void ReleasingSubmitsAMoveInTheAimedDirection()
        {
            _input.TryBeginDrag(Player.Pos);
            Assert.IsTrue(_input.ReleaseDrag(Player.Pos + new Vector2(0f, -GameConstants.MaxDragVirtual)));

            Assert.AreEqual(ActionType.Move, Player.PlannedAction);
            Assert.AreEqual(1f, Player.PlannedPower, 0.01f);
            Assert.AreEqual(1f, Player.PlannedAimDirection.y, 0.001f);
            Assert.IsFalse(_input.IsDragging);
        }

        [UnityTest]
        public IEnumerator TheDragIsAbandonedWhenPlanningEnds()
        {
            _input.TryBeginDrag(Player.Pos);
            Assert.IsTrue(_input.IsDragging);

            yield return RunSeconds(GameConstants.PlanningTime + 0.1f);

            Assert.AreNotEqual(MatchPhase.Planning, _controller.Manager.State.Phase);
            Assert.IsFalse(_input.IsDragging, "a pull must not survive into the action phase");
        }

        [Test]
        public void TheAbilityToggleOnlyArmsWhenTheAbilityCouldActuallyFire()
        {
            Player.Rage = 0f;
            _input.ToggleAbility();
            Assert.IsFalse(_input.AbilityArmed, "arming a meter that is not full would silently do nothing");

            Player.Rage = GameConstants.RageMax;
            _input.ToggleAbility();
            Assert.IsTrue(_input.AbilityArmed);
        }

        [UnityTest]
        public IEnumerator ArmingTheAbilitySurvivesOrderingAMove_ButNotThePhase()
        {
            Player.Rage = GameConstants.RageMax;
            _input.ToggleAbility();
            Assert.IsTrue(Player.AbilityArmed, "arming should reach the plan on its own");

            _input.TryBeginDrag(Player.Pos);
            _input.ReleaseDrag(Player.Pos + new Vector2(-GameConstants.MaxDragVirtual, 0f));

            // This used to clear the toggle: the ability was only carried as an argument to the move
            // submission, so it was consumed by it. That made it an alternative to moving rather
            // than a supplement to it, and made the order of the two presses matter.
            Assert.IsTrue(Player.AbilityArmed, "the move must not have swallowed the ability");
            Assert.IsTrue(_input.AbilityArmed, "and the button must still show it armed");

            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);

            Assert.IsFalse(_input.AbilityArmed, "it is a decision about one cycle, not a standing order");
        }

        [UnityTest]
        public IEnumerator TheTrajectoryPreviewIsDrawnWhilePullingAndClearedOnRelease()
        {
            // By name: there is more than one line in the scene now, and picking whichever comes
            // back first found the pull line instead - a two-point line that trivially fails a
            // "should be a polyline" check.
            var line = FindLine("TrajectoryPreview");
            Assert.IsNotNull(line, "the input controller should have built a trajectory LineRenderer");
            Assert.IsFalse(line.enabled, "nothing to preview before a pull starts");

            _input.TryBeginDrag(Player.Pos);
            _input.UpdateDrag(Player.Pos + new Vector2(-GameConstants.MaxDragVirtual, 0f));
            yield return null;

            Assert.IsTrue(line.enabled);
            Assert.Greater(line.positionCount, 2, "the preview should be a real polyline");

            _input.CancelDrag();
            Assert.IsFalse(line.enabled, "cancelling clears the preview");
        }

        private LineRenderer FindLine(string name)
            => Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(l => l.name == name);

        [UnityTest]
        public IEnumerator ThePullIsDrawnFromTheGladiatorTowardsThePointer()
        {
            var pullLine = FindLine("PullLine");
            Assert.IsNotNull(pullLine, "the input controller should have built a pull line");
            Assert.IsFalse(pullLine.enabled, "nothing to draw before a pull starts");

            var player = Player;
            var pointer = player.Pos + new Vector2(-GameConstants.MaxDragVirtual * 0.5f, 0f);
            _input.TryBeginDrag(player.Pos);
            _input.UpdateDrag(pointer);
            yield return null;

            Assert.IsTrue(pullLine.enabled);
            Assert.AreEqual(2, pullLine.positionCount, "the pull is a straight line, not a curve");

            var arena = _controller.Arena;
            Assert.Less(Vector3.Distance(pullLine.GetPosition(0), arena.ToWorld(player.Pos, 0.07f)), 0.01f,
                "it should start on the gladiator");
            Assert.Less(Vector3.Distance(pullLine.GetPosition(1), arena.ToWorld(pointer, 0.07f)), 0.01f,
                "and end where the pointer is");

            _input.CancelDrag();
            Assert.IsFalse(pullLine.enabled, "cancelling clears it");
        }

        [Test]
        public void TheTrajectoryIsWideWhiteAndDashed()
        {
            // Regression for "the movement line is hard to see": it used to be a thin yellow
            // hairline, which disappeared against bright sand and the red danger rings.
            var line = FindLine("TrajectoryPreview");
            Assert.IsNotNull(line);

            Assert.GreaterOrEqual(line.widthMultiplier, 0.18f,
                "the trajectory should be several times wider than a hairline");
            Assert.AreEqual(LineTextureMode.Tile, line.textureMode,
                "dashes come from a tiled texture, so they stay even through a bounce");
            Assert.IsNotNull(line.sharedMaterial.mainTexture, "the dash pattern is a texture");

            var colour = line.sharedMaterial.color;
            Assert.Greater(Mathf.Min(colour.r, colour.g, colour.b), 0.9f, "and it should be white");
        }

        [UnityTest]
        public IEnumerator ThePullDoesNotPromiseMorePowerThanAReleaseWouldDeliver()
        {
            var pullLine = FindLine("PullLine");
            var player = Player;

            _input.TryBeginDrag(player.Pos);
            // Drag far past the maximum useful pull.
            _input.UpdateDrag(player.Pos + new Vector2(-GameConstants.MaxDragVirtual * 4f, 0f));
            yield return null;

            float drawn = Vector3.Distance(pullLine.GetPosition(0), pullLine.GetPosition(1));
            float maximum = _controller.Arena.ScaleLength(GameConstants.MaxDragVirtual);
            Assert.Less(drawn, maximum * 1.02f,
                "over-pulling must not draw a longer band than full power");
        }

        [UnityTest]
        public IEnumerator ThePreviewEndsWhereTheGladiatorActuallyEndsUp()
        {
            // The whole point of the preview: it runs the same maths the action phase will. Keep the
            // bot out of the way and standing still, and pick a pull short enough not to reach a
            // wall, so the two integrations are comparing the same uninterrupted run.
            var bot = _controller.Manager.State.Bot.Active;
            bot.Pos = new Vector2(0f, GameConstants.ArenaRadius * 0.85f);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            var player = Player;
            player.Pos = Vector2.zero;

            var aim = new Vector2(1f, 0f);
            const float power = 0.5f;
            var preview = GameManager.ComputeTrajectoryPreview(player, aim, power);
            Vector2 predicted = preview[preview.Count - 1];

            _input.TryBeginDrag(player.Pos);
            _input.ReleaseDrag(player.Pos - aim * (GameConstants.MaxDragVirtual * power));

            yield return RunSeconds(GameConstants.PlanningTime + GameConstants.ActionTime + 0.2f);

            Assert.Less(Vector2.Distance(player.Pos, predicted), 5f,
                $"the preview promised {predicted} but the gladiator finished at {player.Pos}");
        }

        // ------------------------------------------------------------------
        // grabbing the figure, not the patch of sand under him
        // ------------------------------------------------------------------

        [Test]
        public void APressAnywhereOnTheFigureStartsAPull()
        {
            var camera = _controller.Arena.ArenaCamera;
            float bodyTop = _controller.Arena.ScaleLength(GameConstants.GladiatorRadius) * 5.6f;

            // Feet, waist and head. The head is the case that used to fail: the camera looks down at
            // 66 degrees, so the top of the model is a long way up the screen from the ground point
            // it stands on, and the press projected onto the sand well behind him.
            foreach (float height in new[] { 0f, bodyTop * 0.5f, bodyTop })
            {
                var screen = camera.WorldToScreenPoint(_controller.Arena.ToWorld(Player.Pos, height));

                _input.CancelDrag();
                Assert.IsTrue(_input.TryBeginDragFromScreen(screen),
                    $"a press {height:0.0} units up the figure should have grabbed him");
                Assert.IsTrue(_input.IsDragging);
            }

            _input.CancelDrag();
        }

        [Test]
        public void APressWellClearOfTheFigureDoesNotStartAPull()
        {
            var camera = _controller.Arena.ArenaCamera;
            var onHim = camera.WorldToScreenPoint(_controller.Arena.ToWorld(Player.Pos));

            Assert.IsFalse(_input.TryBeginDragFromScreen(onHim + new Vector3(Screen.height * 0.3f, 0f, 0f)),
                "the whole screen must not be a grab handle");
            Assert.IsFalse(_input.IsDragging);
        }

        // ------------------------------------------------------------------
        // tap to move
        // ------------------------------------------------------------------

        /// <summary>
        /// A swipe starts anywhere and, like the pull, runs opposite itself.
        ///
        /// Both halves matter. Starting anywhere is the whole reason it exists - a pull has to begin
        /// on the gladiator, so the hand aiming him covers him. Running opposite is what makes it the
        /// same gesture as the pull rather than a second, contradictory one: two controls in the same
        /// game that answer the same drag in opposite directions is worse than either alone.
        /// </summary>
        [Test]
        public void ASwipeStartsAnywhereAndRunsOppositeItself()
        {
            var g = _controller.Manager.State.P1.Active;
            _input.Scheme = ControlScheme.Swipe;

            // Deliberately nowhere near him - further away than a pull would ever be allowed to
            // start, and on the other side of the arena.
            var far = g.Pos + new Vector2(-160f, -140f);
            Assert.IsTrue(_input.TryBeginSwipe(far), "a swipe must start anywhere, not on the man");

            // Drawn down the arena, so he should be ordered up it.
            var drawnTo = far + new Vector2(0f, -GameConstants.MaxDragVirtual * 0.8f);
            _input.UpdateDrag(drawnTo);

            Assert.Greater(Vector2.Dot(_input.CurrentAim, Vector2.up), 0.95f,
                "the swipe ran the way it was drawn instead of opposite it");
            Assert.AreEqual(0.8f, _input.CurrentPower, 0.05f,
                "the swipe's length is its power");

            Assert.IsTrue(_input.ReleaseDrag(drawnTo));
            Assert.AreEqual(ActionType.Move, g.PlannedAction);
            Assert.Greater(Vector2.Dot(g.PlannedAimDirection, Vector2.up), 0.95f);
        }

        /// <summary>
        /// The head sits at the far end of the lane, past the band's own rounded cap.
        ///
        /// It looked short of the end for a while and the maths was right: a LineRenderer with
        /// rounded caps draws a half-disc of its own width past its last point, so the band kept
        /// going after the arrow's tip.
        /// </summary>
        [UnityTest]
        public IEnumerator TheArrowHeadSitsAtTheFarEndOfTheLane()
        {
            var line = FindLine("TrajectoryPreview");
            var head = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == "TrajectoryHead");
            if (head == null) Assert.Ignore("No arrow head - the palette predates it.");

            var player = Player;
            _input.Scheme = ControlScheme.Swipe;
            var anchor = player.Pos + new Vector2(-120f, -120f);
            _input.TryBeginSwipe(anchor);
            _input.UpdateDrag(anchor + new Vector2(0f, -GameConstants.MaxDragVirtual));
            yield return null;

            Assert.IsTrue(head.gameObject.activeSelf, "the lane has no head on it");

            // Pointing the way the run goes. The quad's own up is where the texture's point is, so
            // this is the arrow's direction and not merely the object's orientation.
            var lastLeg = (line.GetPosition(line.positionCount - 1)
                           - line.GetPosition(line.positionCount - 2)).normalized;
            Assert.Greater(Vector3.Dot(head.up, lastLeg), 0.98f,
                $"the head points {head.up} while the run goes {lastLeg}");

            // Lying on the floor, not standing up out of it: the camera looks down, and an arrow on
            // its edge is a line. Measured against the quad's own back, because a Unity quad's
            // visible face is along its -Z - so an arrow facing the sky has its forward in the sand.
            Assert.Greater(Vector3.Dot(-head.forward, Vector3.up), 0.98f,
                "the head is not lying flat on the sand");

            var arena = _controller.Arena;
            var start = line.GetPosition(0);
            var end = line.GetPosition(line.positionCount - 1);

            // Measured along the run rather than as a distance, so "past the end" and "short of the
            // end" are different answers rather than the same one.
            var along = (end - start).normalized;
            float headAlong = Vector3.Dot(head.position - start, along);
            float lineAlong = Vector3.Dot(end - start, along);

            // The threshold sits between the two measured cases rather than at a round number: with
            // the cap accounted for the head's centre lands a seventh of a body radius short of the
            // last point, and without it a whole radius and a third short. This is between them, so
            // the test still fails if the cap is forgotten again.
            float radius = arena.ScaleLength(GameConstants.GladiatorRadius);
            Assert.Greater(headAlong, lineAlong - radius * 0.7f,
                "the head is sitting back down the lane, short of where the band actually ends");
            Assert.Less(headAlong, lineAlong + radius * 3f,
                "the head has floated off past the end of the run");
        }

        /// <summary>
        /// Arming the speed ability lengthens the run the preview promises.
        ///
        /// The buff has not fired yet while the player is still planning - it fires at the top of
        /// the action phase - so the preview was drawing the unbuffed run and then the gladiator
        /// went half as far again as the line said he would.
        /// </summary>
        [UnityTest]
        public IEnumerator ArmingTheSpeedAbilityLengthensThePreviewedRun()
        {
            var line = FindLine("TrajectoryPreview");
            var player = Player;
            if (player.Def.Ability != AbilityKey.Spirit)
                Assert.Ignore("This fixture's gladiator does not have the speed ability.");

            _input.Scheme = ControlScheme.Swipe;
            var anchor = player.Pos + new Vector2(-120f, -120f);
            var drawnTo = anchor + new Vector2(0f, -GameConstants.MaxDragVirtual);

            _input.TryBeginSwipe(anchor);
            _input.UpdateDrag(drawnTo);
            yield return null;
            float plain = Vector3.Distance(line.GetPosition(0), line.GetPosition(line.positionCount - 1));
            _input.CancelDrag();

            player.Rage = GameConstants.RageMax;
            _input.ToggleAbility();
            Assert.IsTrue(_input.AbilityArmed, "the ability did not arm, so this proves nothing");

            _input.TryBeginSwipe(anchor);
            _input.UpdateDrag(drawnTo);
            yield return null;
            float spirited = Vector3.Distance(line.GetPosition(0), line.GetPosition(line.positionCount - 1));

            Assert.Greater(spirited, plain * 1.3f,
                $"the previewed run was {plain:0.00} without the ability and {spirited:0.00} with it");
        }

        /// <summary>
        /// The order stays drawn after the finger comes off, and goes when the phase does.
        ///
        /// Letting go used to take the preview with it, which left an order filed, seconds still on
        /// the clock, and nothing on screen saying what had been ordered - so the only way to check
        /// was to swipe again and read the new one.
        /// </summary>
        [UnityTest]
        public IEnumerator AReleasedOrderStaysDrawnUntilThePhaseEnds()
        {
            var line = FindLine("TrajectoryPreview");
            _input.Scheme = ControlScheme.Swipe;

            var anchor = Player.Pos + new Vector2(-120f, -120f);
            _input.TryBeginSwipe(anchor);
            Assert.IsTrue(_input.ReleaseDrag(anchor + new Vector2(0f, -GameConstants.MaxDragVirtual)));

            yield return null;
            Assert.IsTrue(line.enabled, "the released order left nothing on screen");
            Assert.IsFalse(_input.IsDragging, "the gesture is over even though its picture is not");

            yield return RunSeconds(GameConstants.PlanningTime + 0.3f);

            Assert.AreNotEqual(MatchPhase.Planning, _controller.Manager.State.Phase);
            Assert.IsFalse(line.enabled,
                "a run drawn while the gladiators are running describes an order already carried out");
        }

        /// <summary>
        /// A pull still has to start on him. The two schemes differ in exactly this, and it is worth
        /// pinning: a swipe that only worked on the gladiator would be a pull with a new name.
        /// </summary>
        [Test]
        public void APullStillHasToStartOnTheGladiator()
        {
            var g = _controller.Manager.State.P1.Active;
            _input.Scheme = ControlScheme.Drag;

            var far = g.Pos + new Vector2(-160f, -140f);
            Assert.IsFalse(_input.TryBeginDrag(far), "a pull started from across the arena");
            Assert.IsTrue(_input.TryBeginDrag(g.Pos), "a pull would not start on the man himself");
            _input.CancelDrag();
        }

        [Test]
        public void ATapInsideHisReachSendsHimExactlyThere()
        {
            _input.Scheme = ControlScheme.Tap;
            Player.Pos = Vector2.zero;

            // Half a dash away, so the power should come out at about half.
            float reach = Player.DashReach();
            var target = new Vector2(0f, reach * 0.5f);
            var screen = _controller.Arena.ArenaCamera.WorldToScreenPoint(_controller.Arena.ToWorld(target));

            Assert.IsTrue(_input.TapTo(screen));
            Assert.AreEqual(ActionType.Move, Player.PlannedAction);
            Assert.AreEqual(0.5f, Player.PlannedPower, 0.05f,
                "a tap half a dash away should ask for half power, not a full charge past it");
            Assert.AreEqual(1f, Player.PlannedAimDirection.y, 0.05f);
        }

        [Test]
        public void ATapBeyondHisReachSendsHimAsFarAsHeCanGo()
        {
            _input.Scheme = ControlScheme.Tap;
            Player.Pos = new Vector2(0f, -ArenaShape.RadiusY * 0.8f);

            // Right across the arena - further than one dash carries for any archetype.
            var target = new Vector2(0f, ArenaShape.RadiusY * 0.8f);
            Assert.Greater(Vector2.Distance(Player.Pos, target), Player.DashReach(),
                "the test needs a target he genuinely cannot reach");

            var screen = _controller.Arena.ArenaCamera.WorldToScreenPoint(_controller.Arena.ToWorld(target));

            Assert.IsTrue(_input.TapTo(screen));
            Assert.AreEqual(1f, Player.PlannedPower, 0.001f, "out of range means flat out");
        }


        /// <summary>
        /// The tap follows the finger while it is held, which is just the same order refiled every
        /// frame - so the thing that has to be true is that refiling replaces rather than adds.
        ///
        /// The latch that turns held frames into these calls lives in Update and reads
        /// Input.mousePosition, which is not something a test can move; what is testable is the
        /// mechanism underneath it, and that is what would break the feature if it stopped holding.
        /// </summary>
        [UnityTest]
        public IEnumerator DraggingATapReplacesTheOrderRatherThanAddingToIt()
        {
            _input.Scheme = ControlScheme.Tap;
            Player.Pos = Vector2.zero;

            var envelope = MoveEnvelope.For(Player);
            Vector3 Screen(Vector2 virtualPoint)
                => _controller.Arena.ArenaCamera.WorldToScreenPoint(_controller.Arena.ToWorld(virtualPoint));

            // Along the finger's path: near and dead ahead, then out and round to one side.
            var near = MoveEnvelope.Rotate(Player.Facing, 0f) * (envelope.OuterRadius * 0.35f);
            float turn = -envelope.HalfAngleDegrees * 0.8f;
            var far = MoveEnvelope.Rotate(Player.Facing, turn) * (envelope.ReachAtTurn(turn) * 0.95f);

            Assert.IsTrue(_input.TapTo(Screen(near)));
            float firstPower = Player.PlannedPower;
            var firstAim = Player.PlannedAimDirection;
            yield return null;

            Assert.IsTrue(_input.TapTo(Screen(far)));

            Assert.Greater(Player.PlannedPower, firstPower + 0.2f,
                "the finger moved further out and the order should have followed it");
            Assert.Greater(Vector2.Angle(Player.PlannedAimDirection, firstAim), 20f,
                "and round to the side, rather than keeping the heading the press started on");

            // One order, not two. A plan is a single field, so this is really a check that nothing
            // was queued up behind it on the way.
            Assert.AreEqual(ActionType.Move, Player.PlannedAction);
            Assert.AreEqual(0f, Vector2.Angle(Player.PlannedAimDirection, far.normalized), 1f,
                "the order that stands is the last one filed");

            // And the run drawn under it is the one that stands, not the one the press started with.
            var lane = FindLine("TrajectoryPreview");
            Assert.IsTrue(lane.enabled, "the lane should still be up under the finger");

            var drawnEnd = _controller.Arena.ToVirtual(lane.GetPosition(lane.positionCount - 1));
            Assert.AreEqual(0f, Vector2.Distance(drawnEnd, Player.Pos + far), envelope.OuterRadius * 0.08f,
                "the lane ends where the finger is, not where it started");
        }

        [UnityTest]
        public IEnumerator ATapLeavesAMarkerAndADottedRunBehindIt()
        {
            _input.Scheme = ControlScheme.Tap;
            Player.Pos = Vector2.zero;

            var marker = _controller.Arena.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "TapMarker");
            var dashes = _input.GetComponentsInChildren<LineRenderer>(true)
                .First(l => l.name == "TrajectoryPreview");

            Assert.IsNotNull(marker, "no tap marker was built");
            Assert.IsFalse(marker.gameObject.activeSelf, "nothing tapped yet");
            Assert.IsFalse(dashes.enabled);

            var target = new Vector2(0f, Player.DashReach() * 0.6f);
            Assert.IsTrue(_input.TapTo(
                _controller.Arena.ArenaCamera.WorldToScreenPoint(_controller.Arena.ToWorld(target))));

            // Tapping used to acknowledge nothing at all: the order went in and the screen stayed
            // exactly as it was, so a registered tap and a missed one looked identical.
            Assert.IsTrue(marker.gameObject.activeSelf, "the tapped point is not marked");
            Assert.AreEqual(_controller.Arena.ToWorld(target).x, marker.position.x, 0.1f);
            Assert.AreEqual(_controller.Arena.ToWorld(target).z, marker.position.z, 0.1f);

            Assert.IsTrue(dashes.enabled, "the run to the tap is not drawn");
            Assert.Greater(dashes.positionCount, 1);

            // And it belongs to this planning phase only - left up, it would still be describing an
            // order while that order was being carried out.
            yield return RunSeconds(GameConstants.PlanningTime + 0.3f);

            Assert.AreEqual(MatchPhase.Action, _controller.Manager.State.Phase);
            Assert.IsFalse(marker.gameObject.activeSelf, "the marker outlived its phase");
            Assert.IsFalse(dashes.enabled);
        }

        [Test]
        public void TappingOnHimselfIsNotAMove()
        {
            _input.Scheme = ControlScheme.Tap;
            var screen = _controller.Arena.ArenaCamera.WorldToScreenPoint(
                _controller.Arena.ToWorld(Player.Pos));

            Assert.IsFalse(_input.TapTo(screen), "a tap with nowhere to go is a mis-tap, not an order");
            Assert.AreEqual(ActionType.None, Player.PlannedAction);
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
            }
        }
    }
}
