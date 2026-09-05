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
    /// The planning phase runs the world in slow motion, and the wall torches are what makes that
    /// visible on an otherwise motionless arena.
    /// </summary>
    public class PlanningSlowMotionTests
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
        public IEnumerator PlanningLastsItsTwoSecondsInRealTime()
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
            Assert.AreEqual(2f, GameConstants.PlanningTime, 0.001f, "the phase is meant to be two seconds");
            Assert.AreEqual(GameConstants.PlanningTime, elapsed, 0.35f,
                $"planning took {elapsed:0.00}s of real time");
        }

        [UnityTest]
        public IEnumerator TimeRunsSlowWhilePlanningAndNormalWhileActing()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);

            Assert.AreEqual(MatchPhase.Planning, State.Phase);
            Assert.Less(Time.timeScale, 0.9f, "planning should visibly slow the world down");
            Assert.AreEqual(_controller.PlanningTimeScale, Time.timeScale, 0.001f);

            yield return RunSeconds(GameConstants.PlanningTime + 0.2f);

            Assert.AreEqual(MatchPhase.Action, State.Phase);
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "the action phase runs at full speed");
        }

        [UnityTest]
        public IEnumerator TheSlowdownDoesNotStretchThePlanningPhaseItself()
        {
            // The whole reason the simulation ticks on unscaled time. Scaling its tick as well would
            // make two seconds of planning take six real ones at a third speed.
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.05f);
            Assert.AreEqual(MatchPhase.Planning, State.Phase);

            float startedAt = Time.realtimeSinceStartup;
            yield return RunUntil(() => State.Phase != MatchPhase.Planning, 20f);
            float elapsed = Time.realtimeSinceStartup - startedAt;

            Assert.Less(elapsed, GameConstants.PlanningTime * 1.6f,
                $"planning took {elapsed:0.0}s of real time; the slowdown is stretching the phase");
        }

        [UnityTest]
        public IEnumerator TimeScaleIsRestoredWhenTheSceneGoesAway()
        {
            _controller.SubmitPlayerPick(GladiatorId.Brutius);
            yield return RunSeconds(GameConstants.RevealTime + 0.2f);
            Assert.Less(Time.timeScale, 0.9f);

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
