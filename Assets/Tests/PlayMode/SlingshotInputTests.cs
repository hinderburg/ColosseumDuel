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
