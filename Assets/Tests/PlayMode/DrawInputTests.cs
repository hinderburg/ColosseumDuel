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
    /// The drawing control, through the input controller's device-independent calls: a run that
    /// starts on the gladiator, follows the finger, and stops where the finger lifts or his speed
    /// runs out.
    /// </summary>
    public class DrawInputTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;
        private PlayerInputController _input;

        private GladiatorInstance Player => _controller.Manager.State.P1.Active;

        [UnitySetUp]
        public IEnumerator LoadArenaAndReachPlanning()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            _controller = Object.FindFirstObjectByType<GameController>();
            _input = Object.FindFirstObjectByType<PlayerInputController>();
            Assert.IsNotNull(_input, "the Arena scene must contain a PlayerInputController");

            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Planning, 10f);
            Assert.AreEqual(MatchPhase.Planning, _controller.Manager.State.Phase);

            // Open sand in the middle, between the columns, with the opponent out of the way at the
            // far end and nothing underfoot.
            _controller.Manager.State.Traps.Traps.Clear();
            _controller.Manager.State.Bot.Active.Pos = new Vector2(0f, ArenaShape.RadiusY * 0.85f);
            Player.Pos = new Vector2(-40f, -120f);
            _input.Scheme = ControlScheme.Draw;
        }

        [Test]
        public void ADrawnRunStartsOnHimAndFollowsTheFinger()
        {
            var start = Player.Pos;
            var strokes = new[]
            {
                new Vector2(-40f, -60f), new Vector2(-40f, 0f), new Vector2(-40f, 60f), new Vector2(-40f, 100f),
                new Vector2(0f, 100f), new Vector2(40f, 100f),
            };

            Assert.IsTrue(_input.BeginDraw(start));
            foreach (var point in strokes) _input.DrawTo(point);

            // Filed as it grows, before the finger lifts: a planning phase that runs out mid-stroke
            // keeps what was drawn.
            Assert.AreEqual(ActionType.Move, Player.PlannedAction, "the run was not filed while it was drawn");
            _input.EndDraw();

            var run = Player.PlannedPath;
            Assert.Less(Vector2.Distance(run[0], start), 0.01f, "the run should start on him");
            Assert.Less(Vector2.Distance(run[run.Count - 1], new Vector2(40f, 100f)), 0.5f,
                "and end where the finger lifted");
            Assert.IsTrue(run.Any(p => Vector2.Distance(p, new Vector2(-40f, 100f)) < 0.5f),
                "and turn the corner the finger turned");

            var lane = FindLine("TrajectoryPreview");
            Assert.IsTrue(lane.enabled, "the drawn run is not shown");
            Assert.AreEqual(run.Count, lane.positionCount, "the lane is not the run that was filed");
        }

        [Test]
        public void AFirstTouchAwayFromHimJoinsHimToIt()
        {
            var start = Player.Pos;
            var touch = new Vector2(-40f, 0f);

            Assert.IsTrue(_input.BeginDraw(touch));

            Assert.Less(Vector2.Distance(_input.DrawnRun[0], start), 0.01f, "the run should start on him");
            Assert.Less(Vector2.Distance(_input.DrawnRun[_input.DrawnRun.Count - 1], touch), 0.5f,
                "and reach the touch straight away");
            Assert.AreEqual(ActionType.Move, Player.PlannedAction);

            // And the finger carries on from there.
            _input.DrawTo(new Vector2(20f, 0f));
            Assert.Less(Vector2.Distance(Player.PlannedTarget, new Vector2(20f, 0f)), 0.5f);
        }

        [Test]
        public void TheRunStopsWhereHisSpeedRunsOut()
        {
            Player.Pos = new Vector2(0f, -100f);
            float reach = Player.PlannedReach();

            // Up and down the middle, two hundred a stroke, until his four hundred and fifty run out.
            _input.BeginDraw(Player.Pos);
            _input.DrawTo(new Vector2(0f, 100f));
            _input.DrawTo(new Vector2(0f, -100f));
            Assert.IsFalse(_input.DrawExhausted, "four hundred drawn is still inside his reach");
            _input.DrawTo(new Vector2(0f, 100f));

            Assert.IsTrue(_input.DrawExhausted, "the run went past his reach and drawing carried on");
            Assert.AreEqual(reach, ObstacleField.Length(_input.DrawnRun), 0.5f);
            Assert.AreEqual(reach, ObstacleField.Length(Player.PlannedPath), 0.5f);

            // Nothing more is taken, however far the finger goes.
            var end = _input.DrawnRun[_input.DrawnRun.Count - 1];
            int corners = _input.DrawnRun.Count;
            _input.DrawTo(new Vector2(50f, 0f));
            _input.DrawTo(new Vector2(50f, 100f));
            Assert.AreEqual(corners, _input.DrawnRun.Count);
            Assert.AreEqual(end, _input.DrawnRun[_input.DrawnRun.Count - 1]);
            Assert.Less(Vector2.Distance(Player.PlannedTarget, end), 0.5f);
        }

        /// <summary>
        /// Arming Spirit while drawing gives the run the reach it will have, so drawing can carry on
        /// past where it ran out; taking it back cuts the run to the reach he has without it.
        /// </summary>
        [Test]
        public void SpiritChangesHowFarTheRunCanBeDrawn()
        {
            Player.Pos = new Vector2(0f, -100f);
            float plain = Player.PlannedReach();

            _input.BeginDraw(Player.Pos);
            _input.DrawTo(new Vector2(0f, 100f));
            _input.DrawTo(new Vector2(0f, -100f));
            _input.DrawTo(new Vector2(0f, 100f));
            Assert.IsTrue(_input.DrawExhausted);

            Player.Rage = GameConstants.RageMax;
            _input.ToggleAbility();
            Assert.IsTrue(_input.AbilityArmed, "the ability did not arm, so this proves nothing");
            Assert.IsFalse(_input.DrawExhausted, "armed, he has more to run and the drawing should go on");

            _input.DrawTo(new Vector2(0f, 100f));
            _input.DrawTo(new Vector2(0f, -100f));
            Assert.AreEqual(plain * 1.5f, ObstacleField.Length(Player.PlannedPath), 0.5f,
                "Spirit should let the run go half as far again");
            _input.EndDraw();

            _input.ToggleAbility();
            Assert.IsFalse(_input.AbilityArmed);
            Assert.AreEqual(plain, ObstacleField.Length(Player.PlannedPath), 0.5f,
                "taken back, the run should be cut to the reach he has without it");

            var lane = FindLine("TrajectoryPreview");
            Assert.AreEqual(Player.PlannedPath.Count, lane.positionCount, "the lane still shows the longer run");
        }

        /// <summary>
        /// A press on his chest starts the run on him, not on the sand behind him. The camera looks
        /// down at an angle, so the ground under his chest on screen is behind his feet, and taking
        /// it as the first point started every run with a step backwards.
        /// </summary>
        [Test]
        public void APressOnHisBodyStartsTheRunOnHimNotBehindHim()
        {
            var arena = _controller.Arena;
            var camera = _input.ArenaCamera;
            var start = Player.Pos;
            var chest = camera.WorldToScreenPoint(
                arena.ToWorld(start, arena.ScaleLength(GameConstants.GladiatorRadius) * 3f));

            Assert.IsTrue(_input.BeginDrawFromScreen(chest));
            Assert.AreEqual(1, _input.DrawnRun.Count, "a press on him should start the run on him and nowhere else");
            Assert.AreEqual(ActionType.None, Player.PlannedAction, "a press on him is not an order yet");

            _input.DrawToScreen(chest + new Vector3(4f, 6f, 0f));
            Assert.AreEqual(1, _input.DrawnRun.Count, "the finger is still on him");

            var ahead = start + new Vector2(0f, 150f);
            _input.DrawToScreen(camera.WorldToScreenPoint(arena.ToWorld(ahead)));
            _input.EndDraw();

            Assert.IsTrue(_input.DrawnRun.All(p => p.y >= start.y - 0.5f),
                "the run took a step back behind him before it went anywhere");
            Assert.Less(Vector2.Distance(Player.PlannedTarget, ahead), 1f);
        }

        /// <summary>A touch that never becomes a run leaves the run already drawn where it was.</summary>
        [Test]
        public void AStrayTouchDoesNotWipeOutADrawnRun()
        {
            _input.BeginDraw(Player.Pos);
            _input.DrawTo(new Vector2(-40f, 0f));
            _input.DrawTo(new Vector2(20f, 0f));
            _input.EndDraw();
            var filed = Player.PlannedPath.ToList();

            _input.BeginDraw(Player.Pos);
            _input.EndDraw();

            Assert.AreEqual(ActionType.Move, Player.PlannedAction);
            CollectionAssert.AreEqual(filed, Player.PlannedPath, "a touch on him replaced the run");
            Assert.IsTrue(FindLine("TrajectoryPreview").enabled, "and the lane went with it");
        }

        [UnityTest]
        public IEnumerator TheGladiatorRunsWhatWasDrawn()
        {
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Defend, Vector2.zero, 0f, false);

            var corner = new Vector2(-40f, 100f);
            var end = new Vector2(40f, 100f);
            _input.BeginDraw(corner);
            _input.DrawTo(end);
            _input.EndDraw();

            yield return RunUntil(() => _controller.Manager.State.Phase == MatchPhase.Action, 10f);

            float nearestToCorner = float.MaxValue;
            float timeout = 5f;
            while (_controller.Manager.State.Phase == MatchPhase.Action && timeout > 0f)
            {
                nearestToCorner = Mathf.Min(nearestToCorner, Vector2.Distance(Player.Pos, corner));
                yield return null;
                timeout -= Time.unscaledDeltaTime;
            }

            Assert.Less(nearestToCorner, 12f, "he did not run round the corner that was drawn");
            Assert.Less(Vector2.Distance(Player.Pos, end), 16f, "and should have finished where the drawing did");
        }

        /// <summary>
        /// The head sits on the end of the run, pointing along it and lying flat on the sand, and
        /// the line under it widens from his feet towards the head and then narrows away to nothing,
        /// so it never pokes out past the point.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRunEndsInAnArrowHeadPointingAlongIt()
        {
            var end = new Vector2(-40f, 100f);
            _input.BeginDraw(Player.Pos);
            _input.DrawTo(new Vector2(-40f, 0f));
            _input.DrawTo(end);
            _input.EndDraw();
            yield return null;

            var head = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(t => t.name == "TrajectoryHead");
            Assert.IsTrue(head.gameObject.activeSelf, "the run has no head on it");

            var tip = _controller.Arena.ToWorld(end);
            Assert.Less(Vector2.Distance(new Vector2(head.position.x, head.position.z), new Vector2(tip.x, tip.z)), 0.02f,
                "the point of the head should be on the end of the run");
            Assert.Greater(Vector3.Dot(head.forward, Vector3.forward), 0.98f,
                "the run goes straight up the arena and the head should point the same way");
            Assert.Greater(Vector3.Dot(head.up, Vector3.up), 0.98f, "and lie flat on the sand");

            var line = FindLine("TrajectoryPreview");
            Assert.Greater(line.widthCurve.Evaluate(0.5f), line.widthCurve.Evaluate(0f) * 1.2f,
                "the line should widen from his feet towards the head");
            Assert.Less(line.widthCurve.Evaluate(1f), 0.005f, "and narrow to nothing under the point");
        }

        private static LineRenderer FindLine(string name)
            => Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(l => l.name == name);

        private static IEnumerator RunUntil(System.Func<bool> done, float timeout)
        {
            float t = 0f;
            while (t < timeout && !done())
            {
                yield return null;
                t += Time.unscaledDeltaTime;
            }
        }
    }
}
