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
    /// The planning phase: how long it lasts, and how fast the world runs while it does.
    ///
    /// It used to run the world at a third speed. That is gone - the phase is where the player
    /// reads the fight to decide what to do about it, and at a third speed everything they were
    /// reading arrived late - and these are what would catch it coming back.
    /// </summary>
    public class PlanningPhaseTests
    {
        private const string ScenePath = "Assets/Scenes/Arena.unity";

        private GameController _controller;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            _controller = Object.FindFirstObjectByType<GameController>();
            Assert.IsNotNull(_controller);
        }

        [TearDown]
        public void RestoreTime()
        {
            // Belt and braces around GameController.OnDisable: a leaked timeScale would slow every
            // test that runs after this one, and the failures would look unrelated.
            Time.timeScale = 1f;
        }

        private MatchState State => _controller.Manager.State;

        // ------------------------------------------------------------------

        [Test]
        public void TorchesRingTheWall()
        {
            var torchRoot = _controller.Arena.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Torches");

            if (torchRoot == null)
            {
                Assert.Ignore("No torch prefab in the palette - Epic Toon FX is not imported here.");
                return;
            }

            var torches = torchRoot.Cast<Transform>().ToList();
            Assert.AreEqual(_controller.Arena.TorchCount, torches.Count);

            foreach (var torch in torches)
            {
                // On the wall, not scattered across the floor: the ellipse reads as 1 there.
                var onFloor = new Vector2(torch.localPosition.x / _controller.Arena.WorldRadiusX,
                                          torch.localPosition.z / _controller.Arena.WorldRadiusZ);
                Assert.AreEqual(1f, onFloor.magnitude, 0.02f, $"{torch.name} is not on the wall");
                Assert.Greater(torch.localPosition.y, 0.5f, $"{torch.name} should sit on top of the wall");
            }
        }

        [UnityTest]
        public IEnumerator PlanningLastsItsFullLengthInRealTime()
        {
            // The phase is timed on unscaled time while the world runs at a third speed, which is
            // exactly the sort of pairing that quietly turns two seconds into six. Measured on the
            // wall clock, because that is the only unit the player experiences it in.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.1f);
            Assert.AreEqual(MatchPhase.Planning, State.Phase);

            float started = Time.realtimeSinceStartup;
            while (State.Phase == MatchPhase.Planning && Time.realtimeSinceStartup - started < 10f)
                yield return null;

            float elapsed = Time.realtimeSinceStartup - started;
            Assert.AreEqual(4f, GameConstants.PlanningTime, 0.001f, "the phase is meant to be four seconds");
            Assert.AreEqual(GameConstants.PlanningTime, elapsed, 0.35f,
                $"planning took {elapsed:0.00}s of real time");
        }

        [UnityTest]
        public IEnumerator TheWorldRunsAtFullSpeedWhilePlanning()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Assert.AreEqual(MatchPhase.Planning, State.Phase);
            Assert.AreEqual(1f, Time.timeScale, 0.001f,
                "planning used to run at a third speed; everything the player reads arrived late");

            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);

            Assert.AreEqual(MatchPhase.Action, State.Phase);
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "and the action phase always did");
        }

        /// <summary>
        /// A knockout keeps its slow motion. It is the opposite case to planning - there is nothing
        /// to decide and nothing to read, only something to watch.
        /// </summary>
        [UnityTest]
        public IEnumerator AKnockoutStillPlaysOutSlowly()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            State.Bot.Active.Hp = 0.01f;
            State.P1.Active.Pos = new Vector2(-40f, 0f);
            State.Bot.Active.Pos = new Vector2(40f, 0f);
            State.P1.Active.Facing = Vector2.right;
            State.Bot.Active.Facing = Vector2.left;
            _controller.Manager.SubmitPlanningAction(PlayerSide.P1, ActionType.Move, Vector2.right, 1f, false);
            _controller.Manager.SubmitPlanningAction(PlayerSide.Bot, ActionType.Move, Vector2.left, 1f, false);

            yield return RunUntil(() => State.Phase == MatchPhase.RoundEnd, 20f);

            Assert.AreEqual(_controller.DeathTimeScale, Time.timeScale, 0.001f);
        }

        /// <summary>
        /// GameController hands the world back at full speed when it goes away.
        ///
        /// Time.timeScale is global: a knockout's slow motion left behind would follow the scene out
        /// and slow whatever loaded next, including the rest of a test run. Set by hand here rather
        /// than by reaching a phase that slows it, so this stays a test of the handing back.
        /// </summary>
        [UnityTest]
        public IEnumerator TimeScaleIsRestoredWhenTheSceneGoesAway()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Time.timeScale = 0.25f;
            _controller.gameObject.SetActive(false);
            yield return null;

            Assert.AreEqual(1f, Time.timeScale, 0.001f,
                "timeScale is global - leaving it low would slow down whatever runs next");
        }

        private static IEnumerator RunSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static IEnumerator RunUntil(System.Func<bool> done, float maxSeconds)
        {
            float t = 0f;
            while (!done() && t < maxSeconds) { yield return null; t += Time.unscaledDeltaTime; }
            Assert.IsTrue(done(), $"condition not reached within {maxSeconds}s");
        }
    }
}
